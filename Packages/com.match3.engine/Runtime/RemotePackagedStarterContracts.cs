using System;
using System.Collections.Generic;
using System.Linq;

namespace Match3.Engine.Runtime
{
    // The installation owns the raw bundled bytes and approved public keys.
    // This neutral Engine verifier never embeds title data or private signing keys.
    public sealed class RemotePackagedStarter
    {
        private readonly byte[] _manifest;
        private readonly byte[] _signature;
        private readonly Dictionary<string, byte[]> _files;

        public RemotePackagedStarter(byte[] canonicalManifest, byte[] signature,
            IReadOnlyDictionary<string, byte[]> packagedFiles)
        {
            if (canonicalManifest == null || signature == null ||
                packagedFiles == null)
                throw new ArgumentNullException("Packaged starter inputs are required.");
            if (canonicalManifest.Length < 1 ||
                canonicalManifest.Length > RemoteManifestCodec.MaximumCanonicalBytes ||
                signature.Length < 256 || signature.Length > 1024 ||
                packagedFiles.Count < 1 || packagedFiles.Count > 256)
                throw new ArgumentException("Invalid packaged starter envelope or file count.");
            _manifest = (byte[])canonicalManifest.Clone();
            _signature = (byte[])signature.Clone();
            _files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            long sum = 0;
            foreach (var kv in packagedFiles)
            {
                if (!RemoteContentSafety.IsSafePath(kv.Key) ||
                    kv.Value == null || kv.Value.LongLength < 1 ||
                    kv.Value.LongLength > RemoteContentSafety.FileByteLimit)
                    throw new ArgumentException("Unsafe packaged starter entry.");
                sum = checked(sum + kv.Value.LongLength);
                if (sum > RemoteContentSafety.PackageByteLimit)
                    throw new ArgumentException("Packaged starter byte budget exceeded.");
                _files.Add(kv.Key, (byte[])kv.Value.Clone());
            }
        }

        public RemoteVerifiedRelease VerifyForOffline(
            IEnumerable<RemotePublicSigningKey> approvedPublicKeys,
            string gameId, RemotePlatform platform, RemoteChannel channel,
            EngineVersion engine, long nowUnixSeconds)
        {
            if (approvedPublicKeys == null)
                throw new ArgumentNullException(nameof(approvedPublicKeys));
            var manifest = RemoteManifestCodec.ParseVerified(
                _manifest, _signature, approvedPublicKeys,
                gameId, platform, channel, engine, nowUnixSeconds);
            if (manifest.Entries.Count != _files.Count ||
                _files.Keys.Except(manifest.Entries.Select(x => x.Path),
                    StringComparer.Ordinal).Any())
                throw new InvalidOperationException(
                    "Starter payload set not equal to signed declaration.");
            return RemoteContentStager.VerifyComplete(
                manifest, _signature, approvedPublicKeys, gameId,
                platform, channel, engine, nowUnixSeconds, _files);
        }
    }

    public static class RemoteOfflineBootstrap
    {
        public static RemoteVerifiedRelease Select(
            RemoteReleaseState verifiedInstalledState,
            RemotePackagedStarter neutralStarter,
            IEnumerable<RemotePublicSigningKey> approvedPublicKeys,
            EngineVersion engine, long nowUnixSeconds)
        {
            if (verifiedInstalledState == null)
                throw new ArgumentNullException(nameof(verifiedInstalledState));
            if (approvedPublicKeys == null)
                throw new ArgumentNullException(nameof(approvedPublicKeys));
            // Reverification is mandatory even if a verified active release
            // was returned earlier: key revocations/time can change.
            var keys = approvedPublicKeys.Take(17).ToArray();
            if (keys.Length > 16 || keys.Any(x => x == null))
                throw new ArgumentException("Invalid bounded trust registry.");
            var candidates = new[] { verifiedInstalledState.Active,
                verifiedInstalledState.PreviousValid };
            foreach (var candidate in candidates)
            {
                if (candidate == null) continue;
                try
                {
                    RemoteManifestVerifier.Verify(candidate.Manifest,
                        candidate.Signature, keys,
                        verifiedInstalledState.GameId,
                        verifiedInstalledState.Platform,
                        verifiedInstalledState.Channel, engine, nowUnixSeconds);
                    return candidate;
                }
                catch (InvalidOperationException)
                {
                    // Continue to a separately verified last-known-good
                    // or bundled starter. Never report an invalid one as active.
                }
            }
            if (neutralStarter == null)
                throw new InvalidOperationException(
                    "No usable signed installed content or packaged starter.");
            return neutralStarter.VerifyForOffline(keys,
                verifiedInstalledState.GameId,
                verifiedInstalledState.Platform,
                verifiedInstalledState.Channel, engine, nowUnixSeconds);
        }
    }
}
