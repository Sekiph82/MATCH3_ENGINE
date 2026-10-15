using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Match3.Engine.Runtime
{
    public sealed class RemoteVerifiedRelease
    {
        private readonly ReadOnlyDictionary<string, string> _fileDigests;
        private readonly byte[] _signature;
        public byte[] Signature => (byte[])_signature.Clone();
        public RemoteContentManifest Manifest { get; }
        public string SignedEnvelopeDigest { get; }
        public IReadOnlyDictionary<string, string> FileDigests => _fileDigests;

        internal RemoteVerifiedRelease(RemoteContentManifest manifest,
            byte[] signature, IEnumerable<RemoteContentEntry> entries)
        {
            Manifest = manifest;
            _signature = (byte[])signature.Clone();
            SignedEnvelopeDigest = RemoteContentSafety.Sha256(
                manifest.CanonicalBytes().Concat(signature).ToArray());
            _fileDigests = new ReadOnlyDictionary<string, string>(
                entries.ToDictionary(x => x.Path, x => x.Sha256,
                    StringComparer.Ordinal));
        }
    }

    public static class RemoteContentStager
    {
        // No signing keys or write privileges here. Caller supplies actual bytes
        // from an untrusted download OR cache. Every entry is rehashed.
        public static RemoteVerifiedRelease VerifyComplete(
            RemoteContentManifest manifest, byte[] signature,
            IEnumerable<RemotePublicSigningKey> trustedKeys,
            string gameId, RemotePlatform platform, RemoteChannel channel,
            EngineVersion engine, long nowUnixSeconds,
            IReadOnlyDictionary<string, byte[]> candidateBytes)
        {
            if (candidateBytes == null)
                throw new ArgumentNullException(nameof(candidateBytes));
            RemoteManifestVerifier.Verify(manifest, signature, trustedKeys,
                gameId, platform, channel, engine, nowUnixSeconds);
            if (candidateBytes.Count != manifest.Entries.Count ||
                candidateBytes.Keys.Any(key => !RemoteContentSafety.IsSafePath(key)))
                throw new InvalidOperationException(
                    "Incomplete/extra or unsafe staged content paths.");
            foreach (var file in manifest.Entries)
            {
                byte[] bytes;
                if (!candidateBytes.TryGetValue(file.Path, out bytes) ||
                    !RemoteContentSafety.Matches(bytes, file.Length, file.Sha256))
                    throw new InvalidOperationException(
                        "Missing/corrupted staged payload: " + file.Path);
                RemoteContentSafety.RequireNonExecutable(bytes, file.Kind);
            }
            return new RemoteVerifiedRelease(manifest, signature, manifest.Entries);
        }

        public static bool CanSkipDownload(RemoteContentEntry expected,
            byte[] cachedContent)
        {
            if (expected == null) throw new ArgumentNullException(nameof(expected));
            return RemoteContentSafety.Matches(
                cachedContent, expected.Length, expected.Sha256);
        }
    }

    // A pure immutable release-state planner. The platform adapter MUST
    // implement durable atomic pointer commit, journal fsync and recovery.
    // This class never claims to have committed anything on disk.
    public sealed class RemoteReleaseState
    {
        public string GameId { get; }
        public RemotePlatform Platform { get; }
        public RemoteChannel Channel { get; }
        public RemoteVerifiedRelease Active { get; }
        public RemoteVerifiedRelease PreviousValid { get; }
        public long HighestSequence { get; }

        public RemoteReleaseState(string gameId,
            RemotePlatform platform, RemoteChannel channel,
            RemoteVerifiedRelease active = null,
            RemoteVerifiedRelease previousValid = null,
            long highestSequence = 0)
        {
            if (!GameProfileCatalog.IsKnownId(gameId) ||
                !Enum.IsDefined(typeof(RemotePlatform), platform) ||
                !Enum.IsDefined(typeof(RemoteChannel), channel) ||
                highestSequence < 0)
                throw new ArgumentException("Invalid release-state namespace.");
            if (active != null && (!SamePartition(active.Manifest, gameId,
                platform, channel) || active.Manifest.Sequence > highestSequence))
                throw new ArgumentException("Active release is foreign/newer than high-water.");
            if (previousValid != null && (!SamePartition(previousValid.Manifest,
                gameId, platform, channel) ||
                previousValid.Manifest.Sequence > highestSequence))
                throw new ArgumentException("Fallback release is foreign/newer than high-water.");
            GameId = gameId;
            Platform = platform;
            Channel = channel;
            Active = active;
            PreviousValid = previousValid;
            HighestSequence = highestSequence;
        }

        internal static bool SamePartition(RemoteContentManifest m,
            string game, RemotePlatform platform, RemoteChannel channel) =>
            m.GameId == game && m.Platform == platform && m.Channel == channel;
    }

    public sealed class RemoteActivationPlan
    {
        public RemoteReleaseState Result { get; }
        public bool Applied { get; }
        public string StatusKey { get; }

        internal RemoteActivationPlan(RemoteReleaseState result,
            bool applied, string status)
        {
            Result = result;
            Applied = applied;
            StatusKey = status;
        }
    }

    public static class RemoteReleasePlanner
    {
        public static RemoteActivationPlan Promote(RemoteReleaseState state,
            RemoteVerifiedRelease fullyVerified)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (fullyVerified == null)
                throw new ArgumentNullException(nameof(fullyVerified));
            var manifest = fullyVerified.Manifest;
            if (!RemoteReleaseState.SamePartition(manifest,
                state.GameId, state.Platform, state.Channel))
                throw new InvalidOperationException("Foreign release partition.");
            if (state.Active != null &&
                manifest.Sequence == state.Active.Manifest.Sequence &&
                fullyVerified.SignedEnvelopeDigest ==
                    state.Active.SignedEnvelopeDigest)
                return new RemoteActivationPlan(state, false,
                    "patch.no_update");
            if (manifest.Sequence <= state.HighestSequence)
                throw new InvalidOperationException(
                    "Signed content downgrade or sequence reuse refused.");
            return new RemoteActivationPlan(
                new RemoteReleaseState(state.GameId,
                    state.Platform, state.Channel,
                    fullyVerified, state.Active,
                    manifest.Sequence), true, "patch.verified_ready");
        }

        public static RemoteActivationPlan RecoverFromInvalidActive(
            RemoteReleaseState state, string corruptedReleaseId)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (state.Active == null || state.PreviousValid == null ||
                state.Active.Manifest.ReleaseId != corruptedReleaseId)
                throw new InvalidOperationException(
                    "Rollback needs an identified invalid active and prior verified release.");
            return new RemoteActivationPlan(
                new RemoteReleaseState(state.GameId,
                    state.Platform, state.Channel,
                    state.PreviousValid, null, state.HighestSequence),
                true, "patch.restored_last_known_good");
        }

        public static RemoteVerifiedRelease SelectOffline(
            RemoteReleaseState state, RemoteVerifiedRelease verifiedStarter)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (state.Active != null) return state.Active;
            if (state.PreviousValid != null) return state.PreviousValid;
            if (verifiedStarter != null &&
                RemoteReleaseState.SamePartition(verifiedStarter.Manifest,
                    state.GameId, state.Platform, state.Channel))
                return verifiedStarter;
            throw new InvalidOperationException(
                "No verified offline starter or last-known-good release.");
        }
    }

    // UI never writes grants or launches the downloader itself.
    public enum RemotePatchDisplayKind
    {
        NoUpdate = 0, Available = 1, Downloading = 2,
        Retrying = 3, OfflineFallback = 4, Failed = 5,
        ReadyForCommit = 6, Restored = 7
    }

    public sealed class RemotePatchDisplay
    {
        public RemotePatchDisplayKind Kind { get; }
        public string TextKey { get; }
        public int CompletedPercent { get; }
        public bool MayRetry { get; }

        public RemotePatchDisplay(RemotePatchDisplayKind kind,
            long verifiedBytes, long expectedBytes, bool retryAllowed)
        {
            if (!Enum.IsDefined(typeof(RemotePatchDisplayKind), kind) ||
                verifiedBytes < 0 || expectedBytes < 0 ||
                verifiedBytes > expectedBytes || expectedBytes >
                    RemoteContentSafety.PackageByteLimit)
                throw new ArgumentOutOfRangeException(nameof(verifiedBytes));
            if ((kind == RemotePatchDisplayKind.Downloading ||
                 kind == RemotePatchDisplayKind.Retrying) &&
                expectedBytes == 0)
                throw new ArgumentException("Progress requires trusted expected bytes.");
            Kind = kind;
            TextKey = "patch." + kind.ToString().ToLowerInvariant();
            CompletedPercent = expectedBytes == 0 ? 0 :
                (int)(verifiedBytes * 100 / expectedBytes);
            MayRetry = retryAllowed &&
                (kind == RemotePatchDisplayKind.Failed ||
                 kind == RemotePatchDisplayKind.Retrying);
        }
    }

    public enum RemoteMetaKind
    {
        DisabledLevel = 0, Event = 1, Album = 2, Offer = 3
    }

    public sealed class SignedRemoteMetaCommand
    {
        public string GameId { get; }
        public string SourceReceiptId { get; }
        public RemoteMetaKind Kind { get; }
        public string NeutralId { get; }
        public bool Enable { get; }

        public SignedRemoteMetaCommand(string gameId, string sourceReceiptId,
            RemoteMetaKind kind, string neutralId, bool enable)
        {
            if (!GameProfileCatalog.IsKnownId(gameId) ||
                !RemoteContentSafety.IsSafeToken(sourceReceiptId, 120) ||
                !RemoteContentSafety.IsSafeToken(neutralId) ||
                !Enum.IsDefined(typeof(RemoteMetaKind), kind))
                throw new ArgumentException("Unsafe remote metadata record.");
            GameId = gameId;
            SourceReceiptId = sourceReceiptId;
            Kind = kind;
            NeutralId = neutralId;
            Enable = enable;
        }
    }

    public static class RemoteMetaPolicy
    {
        // Only a fully verified manifest may authorize immutable meta claims.
        // The public command constructor grants nothing by itself.
        public static bool Allow(GameProfile profile,
            SignedRemoteMetaCommand command, RemoteVerifiedRelease release,
            bool ageEligible, bool commerceApproved)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            if (command == null) throw new ArgumentNullException(nameof(command));
            if (release == null ||
                profile.Id != command.GameId ||
                release.Manifest.GameId != profile.Id ||
                !release.Manifest.MetaCommands.Any(x =>
                    x.SourceReceiptId == command.SourceReceiptId &&
                    x.Kind == command.Kind &&
                    x.NeutralId == command.NeutralId &&
                    x.Enable == command.Enable))
                throw new InvalidOperationException(
                    "Unsigned, unbound or cross-title metadata rejected.");
            if (command.Kind == RemoteMetaKind.Offer)
                return profile.Id != "Wonder_Pop" &&
                    ageEligible && commerceApproved;
            if (command.Kind == RemoteMetaKind.Event)
                return profile.Id != "Wonder_Pop" || ageEligible;
            return true;
        }
    }
}
