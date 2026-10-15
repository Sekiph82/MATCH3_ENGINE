using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Match3.Engine.Runtime
{
    // Application-private, same-volume, read-only-content publisher.
    // Caller MUST supply an existing app-owned directory, never a user folder.
    // This is a single-process host. Multiple publisher processes require an
    // external process lock, and a trusted installer controls the trust keys.
    public sealed class RemoteDurableActivationHost : IRemoteActivationHost
    {
        private readonly string _folder;
        private readonly string _activePointer;
        private readonly string _previousPointer;
        private readonly string _partition;
        private readonly object _gate = new object();
        private RemoteVerifiedRelease _prepared;
        private string _preparedPath;
        private long _lastHighWater;

        public RemoteDurableActivationHost(string existingPrivateRoot,
            string gameId, RemotePlatform platform, RemoteChannel channel)
        {
            if (!GameProfileCatalog.IsKnownId(gameId) ||
                !Enum.IsDefined(typeof(RemotePlatform), platform) ||
                !Enum.IsDefined(typeof(RemoteChannel), channel))
                throw new ArgumentException("Invalid private content partition.");
            if (string.IsNullOrWhiteSpace(existingPrivateRoot) ||
                !Path.IsPathRooted(existingPrivateRoot))
                throw new ArgumentException(
                    "Existing absolute app-private directory required.", nameof(existingPrivateRoot));
            var root = Path.GetFullPath(existingPrivateRoot);
            RequireRealDirectory(root);
            _partition = gameId + "/" + (int)platform + "/" + (int)channel;
            _folder = Path.Combine(root, gameId, ((int)platform).ToString(),
                ((int)channel).ToString());
            RequireSafeDirectoryTree(root, _folder, create: true);
            _activePointer = Path.Combine(_folder, "active.pointer");
            _previousPointer = Path.Combine(_folder, "previous.pointer");
        }

        public Task PrepareVerifiedAsync(RemoteVerifiedRelease release,
            IReadOnlyDictionary<string, byte[]> bytes,
            CancellationToken cancellation)
        {
            if (release == null) throw new ArgumentNullException(nameof(release));
            if (bytes == null) throw new ArgumentNullException(nameof(bytes));
            cancellation.ThrowIfCancellationRequested();
            if (release.Manifest.PartitionKey != _partition)
                throw new InvalidOperationException("Cross-profile staged release.");
            if (bytes.Count != release.Manifest.Entries.Count)
                throw new InvalidOperationException("Incomplete staged release.");
            // Validate the ENTIRE signed set before allocating any disk space.
            // A failed digest/path cannot accumulate four unusable stages.
            foreach (var entry in release.Manifest.Entries)
            {
                byte[] candidate;
                if (!bytes.TryGetValue(entry.Path, out candidate) ||
                    !RemoteContentSafety.Matches(candidate, entry.Length,
                        entry.Sha256))
                    throw new InvalidOperationException(
                        "Incomplete/corrupt signed stage rejected before writes.");
                RemoteContentSafety.RequireNonExecutable(candidate, entry.Kind);
            }

            lock (_gate)
            {
                if (_prepared != null)
                    throw new InvalidOperationException("Pending release must be committed first.");
                // Never auto-delete owner-visible crash evidence or grow
                // private storage without bound after repeated interruptions.
                if (Directory.GetDirectories(_folder, "stage-*").Length >= 4)
                    throw new InvalidOperationException(
                        "Orphan staging quota reached; explicit review required.");
                var stage = Path.Combine(_folder, "stage-" +
                    Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(stage);
                RequireRealDirectory(stage);
                // An interrupted stage is never live. Do not auto-remove it:
                // owners control storage housekeeping, and cleanup is separate.
                foreach (var entry in release.Manifest.Entries)
                {
                    byte[] payload;
                    if (!bytes.TryGetValue(entry.Path, out payload) ||
                        !RemoteContentSafety.Matches(payload, entry.Length,
                            entry.Sha256))
                        throw new InvalidOperationException(
                            "Staged bytes do not match signed manifest.");
                    RemoteContentSafety.RequireNonExecutable(payload, entry.Kind);
                    var parts = entry.Path.Split('/');
                    var relativeFolder = stage;
                    for (var i = 0; i < parts.Length - 1; i++)
                    {
                        relativeFolder = Path.Combine(relativeFolder, parts[i]);
                        RequireSafeDirectoryTree(stage, relativeFolder, create: true);
                    }
                    WriteOnce(Path.Combine(relativeFolder, parts[parts.Length - 1]),
                        payload);
                }
                WriteOnce(Path.Combine(stage, "manifest.canonical"),
                    release.Manifest.CanonicalBytes());
                WriteOnce(Path.Combine(stage, "manifest.sig"), release.Signature);
                cancellation.ThrowIfCancellationRequested();
                _prepared = release;
                _preparedPath = stage;
            }
            return Task.CompletedTask;
        }

        public Task<bool> ExchangeActiveAsync(string partition,
            string expectedEnvelopeDigest, string replacementEnvelopeDigest,
            long highestSequence, CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();
            lock (_gate)
            {
                if (partition != _partition || _prepared == null ||
                    replacementEnvelopeDigest != _prepared.SignedEnvelopeDigest ||
                    highestSequence != _prepared.Manifest.Sequence)
                    throw new InvalidOperationException("Unprepared/mismatched pointer request.");
                if (!File.Exists(_activePointer) &&
                    File.Exists(_previousPointer))
                    throw new InvalidOperationException(
                        "Missing active pointer with rollback history: fail closed.");
                var current = ReadPointer(_activePointer);
                if ((current == null ? "" : current.Digest) != expectedEnvelopeDigest ||
                    (current != null && highestSequence <= current.HighWater) ||
                    highestSequence <= _lastHighWater)
                    return Task.FromResult(false);
                var published = Path.Combine(_folder, "release-" +
                    _prepared.Manifest.Sequence + "-" +
                    _prepared.SignedEnvelopeDigest);
                if (File.Exists(published) || Directory.Exists(published))
                    throw new InvalidOperationException("Immutable release path already exists.");
                cancellation.ThrowIfCancellationRequested();
                Directory.Move(_preparedPath, published);
                // A crash between immutable directory rename and pointer
                // exchange leaves an orphan, not a falsely active release.
                var next = new Pointer(
                    _prepared.SignedEnvelopeDigest, _prepared.Manifest.Sequence,
                    current == null ? "" : current.Digest);
                WriteAtomicPointer(next);
                _lastHighWater = highestSequence;
                _prepared = null;
                _preparedPath = null;
                return Task.FromResult(true);
            }
        }

        // Reconstruct from signed contents, never from untrusted pointer labels
        // alone. Tampered active content may fall back to the previous signed
        // release, but the sequence high-water remains unchanged.
        public RemoteReleaseState Restore(
            IEnumerable<RemotePublicSigningKey> trusted,
            EngineVersion engine, long nowUnixSeconds)
        {
            if (trusted == null) throw new ArgumentNullException(nameof(trusted));
            lock (_gate)
            {
                if (!File.Exists(_activePointer) &&
                    File.Exists(_previousPointer))
                    throw new InvalidOperationException(
                        "Missing active pointer cannot erase existing high-water history.");
                var pointer = ReadPointer(_activePointer);
                if (pointer == null)
                    return new RemoteReleaseState(PartitionGame(),
                        PartitionPlatform(), PartitionChannel());
                _lastHighWater = Math.Max(_lastHighWater, pointer.HighWater);
                RemoteVerifiedRelease current = null;
                RemoteVerifiedRelease previous = null;
                try
                {
                    current = Load(pointer.Digest, trusted, engine, nowUnixSeconds);
                }
                catch (Exception ex) when (ex is IOException ||
                    ex is InvalidOperationException ||
                    ex is UnauthorizedAccessException ||
                    ex is ArgumentException)
                {
                    // Only a genuinely verified previous release is usable.
                    if (string.IsNullOrEmpty(pointer.PreviousDigest)) throw;
                }
                if (!string.IsNullOrEmpty(pointer.PreviousDigest))
                {
                    try
                    {
                        previous = Load(pointer.PreviousDigest, trusted,
                            engine, nowUnixSeconds);
                    }
                    catch (Exception ex) when (ex is IOException ||
                        ex is InvalidOperationException ||
                        ex is UnauthorizedAccessException ||
                        ex is ArgumentException)
                    {
                        if (current == null) throw;
                        // Broken backup must not invalidate a good active.
                    }
                }
                if (current == null)
                {
                    if (previous == null)
                        throw new InvalidOperationException("No verified rollback candidate.");
                    var fallback = new Pointer(
                        previous.SignedEnvelopeDigest, pointer.HighWater, "");
                    WriteAtomicPointer(fallback);
                    return new RemoteReleaseState(PartitionGame(),
                        PartitionPlatform(), PartitionChannel(), previous,
                        null, pointer.HighWater);
                }
                return new RemoteReleaseState(PartitionGame(),
                    PartitionPlatform(), PartitionChannel(), current,
                    previous, pointer.HighWater);
            }
        }

        private RemoteVerifiedRelease Load(string digest,
            IEnumerable<RemotePublicSigningKey> trusted,
            EngineVersion engine, long now)
        {
            var matches = Directory.GetDirectories(_folder, "release-*");
            if (matches.Length > 64)
                throw new InvalidOperationException("Too many unreviewed release folders.");
            var matching = matches.Where(x =>
                Path.GetFileName(x).EndsWith("-" + digest, StringComparison.Ordinal))
                .ToArray();
            if (matching.Length != 1)
                throw new InvalidOperationException("No unique verified release folder.");
            var dir = matching[0];
            RequireRealDirectory(dir);
            var manifestBytes = ReadSmall(Path.Combine(dir, "manifest.canonical"),
                RemoteManifestCodec.MaximumCanonicalBytes);
            var signature = ReadSmall(Path.Combine(dir, "manifest.sig"), 1024);
            var manifest = RemoteManifestCodec.ParseVerified(manifestBytes,
                signature, trusted, PartitionGame(), PartitionPlatform(),
                PartitionChannel(), engine, now);
            if (Path.GetFileName(dir) != "release-" + manifest.Sequence +
                "-" + digest)
                throw new InvalidOperationException("Release directory identity mismatch.");
            var bytes = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            foreach (var entry in manifest.Entries)
            {
                var cur = dir;
                foreach (var segment in entry.Path.Split('/').Take(
                    entry.Path.Split('/').Length - 1))
                {
                    cur = Path.Combine(cur, segment);
                    RequireRealDirectory(cur);
                }
                var file = Path.Combine(dir,
                    entry.Path.Replace('/', Path.DirectorySeparatorChar));
                bytes.Add(entry.Path, ReadSmall(file, entry.Length));
            }
            var release = RemoteContentStager.VerifyComplete(manifest,
                signature, trusted, PartitionGame(), PartitionPlatform(),
                PartitionChannel(), engine, now, bytes);
            if (release.SignedEnvelopeDigest != digest)
                throw new InvalidOperationException("Release folder digest mismatch.");
            return release;
        }

        private void WriteAtomicPointer(Pointer p)
        {
            var pending = Path.Combine(_folder, "pointer-" +
                Guid.NewGuid().ToString("N") + ".pending");
            WriteOnce(pending, Encoding.ASCII.GetBytes(p.Serialize()));
            if (File.Exists(_activePointer))
                File.Replace(pending, _activePointer, _previousPointer);
            else
                File.Move(pending, _activePointer);
        }

        private Pointer ReadPointer(string path)
        {
            if (!File.Exists(path)) return null;
            var bytes = ReadSmall(path, 256);
            return Pointer.Parse(Encoding.ASCII.GetString(bytes), bytes);
        }

        private static void RequireRealDirectory(string path)
        {
            var d = new DirectoryInfo(path);
            if (!d.Exists || (d.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("Private directory missing or linked.");
        }

        private static void RequireSafeDirectoryTree(string root,
            string target, bool create)
        {
            var basePath = Path.GetFullPath(root).TrimEnd(
                Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var destination = Path.GetFullPath(target).TrimEnd(
                Path.DirectorySeparatorChar);
            if (!destination.StartsWith(basePath,
                StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Path escaped private storage.");
            var next = basePath.TrimEnd(Path.DirectorySeparatorChar);
            foreach (var part in destination.Substring(basePath.Length)
                .Split(Path.DirectorySeparatorChar))
            {
                next = Path.Combine(next, part);
                if (create && !Directory.Exists(next))
                    Directory.CreateDirectory(next);
                RequireRealDirectory(next);
            }
        }

        private static void WriteOnce(string path, byte[] bytes)
        {
            using (var f = new FileStream(path, FileMode.CreateNew,
                FileAccess.Write, FileShare.None))
            {
                f.Write(bytes, 0, bytes.Length);
                f.Flush(true);
            }
        }

        private static byte[] ReadSmall(string path, long max)
        {
            var info = new FileInfo(path);
            if (!info.Exists || max < 1 || info.Length < 1 ||
                info.Length > max || (info.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("Stored signed file missing or unsafe.");
            return File.ReadAllBytes(path);
        }

        private string PartitionGame() => _partition.Split('/')[0];
        private RemotePlatform PartitionPlatform() =>
            (RemotePlatform)int.Parse(_partition.Split('/')[1]);
        private RemoteChannel PartitionChannel() =>
            (RemoteChannel)int.Parse(_partition.Split('/')[2]);

        private sealed class Pointer
        {
            public string Digest { get; }
            public long HighWater { get; }
            public string PreviousDigest { get; }

            public Pointer(string digest, long high, string previous)
            {
                if (!RemoteContentSafety.IsSha256Hex(digest) || high < 1 ||
                    (!string.IsNullOrEmpty(previous) &&
                     !RemoteContentSafety.IsSha256Hex(previous)))
                    throw new InvalidOperationException("Unsafe durable pointer.");
                Digest = digest;
                HighWater = high;
                PreviousDigest = previous;
            }

            public string Serialize() => Digest + "|" + HighWater + "|" +
                PreviousDigest + "\n";

            public static Pointer Parse(string text, byte[] encoded)
            {
                var parts = text.TrimEnd('\n').Split('|');
                long high;
                if (parts.Length != 3 ||
                    !long.TryParse(parts[1], out high) ||
                    !text.EndsWith("\n", StringComparison.Ordinal))
                    throw new InvalidOperationException("Malformed durable pointer.");
                var pointer = new Pointer(parts[0], high, parts[2]);
                if (!Encoding.ASCII.GetBytes(pointer.Serialize()).SequenceEqual(encoded))
                    throw new InvalidOperationException("Noncanonical pointer bytes.");
                return pointer;
            }
        }
    }
}
