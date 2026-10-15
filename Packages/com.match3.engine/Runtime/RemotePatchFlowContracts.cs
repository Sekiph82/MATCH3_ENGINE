using System;
using System.Collections.Generic;
using System.Linq;

namespace Match3.Engine.Runtime
{
    public enum RemotePatchStage
    {
        Available = 0, Downloading = 1, Verified = 2,
        Ready = 3, Offline = 4, Failed = 5
    }

    public sealed class RemotePatchUiSnapshot
    {
        public RemotePatchStage Stage { get; }
        public string LabelKey { get; }
        public int VerifiedPercent { get; }
        public bool CanRetry { get; }
        public bool CanPlay { get; }
        public bool CanClaimReward => false;
        public bool CanPurchase => false;

        internal RemotePatchUiSnapshot(RemotePatchStage stage,
            string labelKey, int percent, bool canRetry, bool canPlay)
        {
            Stage = stage;
            LabelKey = labelKey;
            VerifiedPercent = percent;
            CanRetry = canRetry;
            CanPlay = canPlay;
        }
    }

    // Development-neutral model for a future Unity patch-screen adapter.
    // Success must originate in real verified receipts + activation result.
    // No arbitrary "100%" setter, reward, store, ad or telemetry leak.
    public sealed class RemotePatchFlow
    {
        private readonly RemoteContentManifest _manifest;
        private readonly byte[] _signature;
        private readonly string _gameId;
        private readonly RemotePlatform _platform;
        private readonly RemoteChannel _channel;
        private readonly HashSet<string> _confirmed =
            new HashSet<string>(StringComparer.Ordinal);
        private readonly object _gate = new object();
        private readonly string _targetDigest;
        private long _verifiedBytes;
        private readonly long _totalBytes;
        private RemotePatchStage _stage;

        public RemotePatchFlow(RemoteContentManifest manifest,
            byte[] signedBytes,
            IEnumerable<RemotePublicSigningKey> pinnedPublicKeys,
            string gameId, RemotePlatform platform,
            RemoteChannel channel, EngineVersion engine,
            long nowUnixSeconds)
        {
            if (manifest == null) throw new ArgumentNullException(nameof(manifest));
            if (signedBytes == null)
                throw new ArgumentNullException(nameof(signedBytes));
            RemoteManifestVerifier.Verify(manifest, signedBytes,
                pinnedPublicKeys, gameId, platform, channel,
                engine, nowUnixSeconds);
            _manifest = manifest;
            _signature = (byte[])signedBytes.Clone();
            _gameId = gameId;
            _platform = platform;
            _channel = channel;
            _targetDigest = RemoteContentSafety.Sha256(
                manifest.CanonicalBytes().Concat(_signature).ToArray());
            _totalBytes = manifest.Entries.Sum(e => e.Length);
            _stage = RemotePatchStage.Available;
        }

        public RemotePatchUiSnapshot Snapshot()
        {
            lock (_gate)
            {
                var label = "patch." + _stage.ToString().ToLowerInvariant();
                return new RemotePatchUiSnapshot(_stage, label,
                    checked((int)(_verifiedBytes * 100 / _totalBytes)),
                    _stage == RemotePatchStage.Failed,
                    _stage == RemotePatchStage.Ready ||
                    _stage == RemotePatchStage.Offline);
            }
        }

        public void RecordVerified(RemoteDownloadReceipt receipt)
        {
            if (receipt == null) throw new ArgumentNullException(nameof(receipt));
            lock (_gate)
            {
                if (_stage != RemotePatchStage.Available &&
                    _stage != RemotePatchStage.Downloading)
                    throw new InvalidOperationException("Patch is not downloading.");
                var target = _manifest.Entries.SingleOrDefault(e =>
                    e.Path == receipt.Path);
                if (target == null ||
                    !RemoteContentSafety.Matches(
                        receipt.Bytes, target.Length, target.Sha256) ||
                    !_confirmed.Add(receipt.Path))
                    throw new InvalidOperationException(
                        "Unverified or duplicate patch progress refused.");
                _verifiedBytes = checked(_verifiedBytes + target.Length);
                _stage = _confirmed.Count == _manifest.Entries.Count
                    ? RemotePatchStage.Verified
                    : RemotePatchStage.Downloading;
            }
        }

        public void RecordActivation(RemoteActivationResult activation)
        {
            if (activation == null) throw new ArgumentNullException(nameof(activation));
            lock (_gate)
            {
                if (_stage != RemotePatchStage.Verified ||
                    !activation.Activated ||
                    activation.State.Active == null ||
                    activation.State.GameId != _gameId ||
                    activation.State.Platform != _platform ||
                    activation.State.Channel != _channel ||
                    activation.State.Active.SignedEnvelopeDigest != _targetDigest)
                    throw new InvalidOperationException(
                        "No genuinely committed signed release available.");
                _stage = RemotePatchStage.Ready;
            }
        }

        public void RecordOfflineFallback(
            RemoteVerifiedRelease verifiedOffline,
            IEnumerable<RemotePublicSigningKey> trustedKeys,
            EngineVersion engine, long nowUnixSeconds)
        {
            if (verifiedOffline == null)
                throw new ArgumentNullException(nameof(verifiedOffline));
            RemoteManifestVerifier.Verify(verifiedOffline.Manifest,
                verifiedOffline.Signature, trustedKeys, _gameId,
                _platform, _channel, engine, nowUnixSeconds);
            lock (_gate)
            {
                if (_stage == RemotePatchStage.Ready)
                    throw new InvalidOperationException(
                        "Cannot replace already committed live patch.");
                _stage = RemotePatchStage.Offline;
            }
        }

        public void RecordFailure()
        {
            lock (_gate)
            {
                if (_stage == RemotePatchStage.Ready ||
                    _stage == RemotePatchStage.Offline)
                    throw new InvalidOperationException(
                        "Committed patch cannot be reclassified as failed.");
                // No exception text is exposed to UI: may contain secrets,
                // urls or platform-private paths.
                _stage = RemotePatchStage.Failed;
            }
        }

        public void Retry()
        {
            lock (_gate)
            {
                if (_stage != RemotePatchStage.Failed)
                    throw new InvalidOperationException("Retry requires failed status.");
                _stage = RemotePatchStage.Available;
                _confirmed.Clear();
                _verifiedBytes = 0;
            }
        }
    }
}
