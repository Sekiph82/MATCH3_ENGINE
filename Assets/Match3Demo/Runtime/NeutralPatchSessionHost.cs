using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Match3.Engine.Runtime;
using UnityEngine;

namespace Match3.Engine.Demo
{
    // Opt-in Unity adapter. Production supplies a genuine pinned HTTPS
    // transport, a verified installed state, and its durable activation host.
    // This class has no signing key, URL, credential, or automatic network I/O.
    [DefaultExecutionOrder(-10000)]
    [DisallowMultipleComponent]
    public sealed class NeutralPatchSessionHost : MonoBehaviour
    {
        [SerializeField] private bool requireSignedPrelaunchOnSceneStart;

        private NeutralMatch3Demo _game;
        private NeutralPatchBootstrapView _view;
        private SynchronizationContext _unityContext;
        private SessionRequest _request;
        private Task<RemotePrelaunchResult> _attempt;
        private bool _running;
        private bool _destroyed;
        private int _attemptCount;
        private long _lastVerifiedNow;

        public bool IsRunning => _running;
        public int AttemptCount => _attemptCount;
        public RemotePrelaunchResult LastResult { get; private set; }
        public Task<RemotePrelaunchResult> CurrentAttempt => _attempt;

        private void Awake()
        {
            _unityContext = SynchronizationContext.Current;
            _game = GetComponent<NeutralMatch3Demo>();
            _view = GetComponent<NeutralPatchBootstrapView>();
            if (!requireSignedPrelaunchOnSceneStart) return;
            RequireGate();
            _view.ShowUnavailable("content-unavailable");
        }

        // Call BEFORE exposing the game or enabling user input. This method
        // accepts no user-controlled URL. Origin and public keys must be
        // selected by the application, never obtained from untrusted content.
        public Task<RemotePrelaunchResult> BeginPrelaunchAsync(
            RemoteContentManifest candidate, byte[] signature,
            IEnumerable<RemotePublicSigningKey> pinnedKeys,
            RemoteReadOrigin trustedOrigin, IRemoteReadTransport transport,
            RemoteReleaseState verifiedInstalled,
            RemotePackagedStarter packagedStarter,
            IRemoteActivationHost durableActivationHost,
            EngineVersion engine, long nowUnixSeconds,
            CancellationToken cancellation = default(CancellationToken))
        {
            if (_request != null || _running)
                throw new InvalidOperationException(
                    "A signed prelaunch session has already been configured.");
            // Validate all dependencies before granting any UI capability.
            var request = new SessionRequest(candidate, signature, pinnedKeys,
                trustedOrigin, transport, verifiedInstalled, packagedStarter,
                durableActivationHost, engine, nowUnixSeconds, cancellation);
            RequireGate();
            if (_view.HasVerifiedSession)
                throw new InvalidOperationException(
                    "A signed play session is already bound.");
            _request = request;
            _view.ShowUnavailable("network-unavailable");
            _view.RegisterHostRetry(ScheduleRealRetry);
            _attempt = ExecuteAsync();
            return _attempt;
        }

        // Convenience for an explicit real HTTPS path. The approved origin
        // and signed candidate are supplied by the caller; no test transport
        // can be accidentally selected by this entry point.
        public Task<RemotePrelaunchResult> BeginHttpsPrelaunchAsync(
            RemoteContentManifest candidate, byte[] signature,
            IEnumerable<RemotePublicSigningKey> pinnedKeys,
            RemoteReadOrigin trustedOrigin,
            RemoteReleaseState verifiedInstalled,
            RemotePackagedStarter packagedStarter,
            IRemoteActivationHost durableActivationHost,
            EngineVersion engine, long nowUnixSeconds,
            CancellationToken cancellation = default(CancellationToken))
        {
            return BeginPrelaunchAsync(candidate, signature, pinnedKeys,
                trustedOrigin, new RemoteHttpsReadTransport(),
                verifiedInstalled, packagedStarter, durableActivationHost,
                engine, nowUnixSeconds, cancellation);
        }

        private void RequireGate()
        {
            if (_destroyed)
                throw new InvalidOperationException("Patch host is destroyed.");
            if (_game == null) _game = GetComponent<NeutralMatch3Demo>();
            if (_view == null) _view = GetComponent<NeutralPatchBootstrapView>();
            if (_game == null || _view == null)
                throw new InvalidOperationException(
                    "Signed patch host requires game and patch view siblings.");
            _game.RequireSignedPrelaunch(_view);
        }

        private bool ScheduleRealRetry()
        {
            if (_request == null || _destroyed || _running ||
                _view == null || _view.HasVerifiedSession)
                return false;
            // ExecuteAsync immediately starts a new transport invocation;
            // this callback never fabricates a receipt or unlocks gameplay.
            _attempt = ExecuteAsync();
            return true;
        }

        private async Task<RemotePrelaunchResult> ExecuteAsync()
        {
            _running = true;
            _attemptCount++;
            LastResult = null;
            try
            {
                // Every retry must recheck key and manifest validity at
                // the actual current time. A pinned old timestamp must not
                // extend an expired signature's acceptance window.
                _lastVerifiedNow = Math.Max(_lastVerifiedNow,
                    Math.Max(_request.NowUnixSeconds,
                        DateTimeOffset.UtcNow.ToUnixTimeSeconds()));
                var result = await RemotePrelaunchBootstrap.ExecuteAsync(
                    _request.Manifest, _request.Signature, _request.PublicKeys,
                    _request.Origin, _request.Transport, _request.Installed,
                    _request.PackagedStarter, _request.DurableHost,
                    _request.Engine, _lastVerifiedNow,
                    _request.Cancellation, PublishProgress);

                if (_destroyed || _request.Cancellation.IsCancellationRequested)
                    return null;
                LastResult = result;
                if (result != null && result.CanPlay)
                {
                    // The Engine has verified the signed bytes, then (for a
                    // new version) received an actual atomic host commit.
                    // A 100% progress notification alone is insufficient.
                    _view.Bind(result.Flow);
                    if (result.NewReleaseActivated)
                    {
                        _request.Installed = new RemoteReleaseState(
                            _request.Installed.GameId,
                            _request.Installed.Platform,
                            _request.Installed.Channel,
                            result.PlayableContent, _request.Installed.Active,
                            Math.Max(_request.Installed.HighestSequence,
                                result.PlayableContent.Manifest.Sequence));
                    }
                }
                else
                {
                    if (result != null)
                        _view.ObservePrelaunchProgress(result.Flow.Snapshot());
                    _view.MarkHostAttemptFailed();
                }
                return result;
            }
            catch (OperationCanceledException) when
                (_request.Cancellation.IsCancellationRequested)
            {
                if (!_destroyed && _view != null &&
                    !_view.HasVerifiedSession)
                {
                    _view.ShowUnavailable("network-unavailable");
                    _view.DisableHostRetry();
                }
                return null;
            }
            catch (Exception)
            {
                // Invalid envelope, untrusted key, or invalid activation
                // setup cannot be retried through a misleading dead button.
                // Never expose a URL, exception, or storage path to the UI.
                if (!_destroyed && _view != null &&
                    !_view.HasVerifiedSession)
                {
                    _view.ShowUnavailable("content-unavailable");
                    _view.DisableHostRetry();
                }
                return null;
            }
            finally
            {
                _running = false;
            }
        }

        private void PublishProgress(RemotePatchUiSnapshot snapshot)
        {
            if (snapshot == null || _destroyed) return;
            // Engine fetch/verification may finish on a pool thread. Unity
            // presentation state is always observed on the captured UI thread.
            if (_unityContext != null &&
                SynchronizationContext.Current != _unityContext)
            {
                _unityContext.Post(_ =>
                {
                    if (!_destroyed && _view != null)
                        _view.ObservePrelaunchProgress(snapshot);
                }, null);
                return;
            }
            if (_view != null)
                _view.ObservePrelaunchProgress(snapshot);
        }

        private void OnDestroy()
        {
            _destroyed = true;
            // Caller owns the cancellation token and transport lifetime.
            // Never dispose or cancel application-owned security state here.
        }

        private sealed class SessionRequest
        {
            public readonly RemoteContentManifest Manifest;
            public readonly byte[] Signature;
            public readonly RemotePublicSigningKey[] PublicKeys;
            public readonly RemoteReadOrigin Origin;
            public readonly IRemoteReadTransport Transport;
            public RemoteReleaseState Installed;
            public readonly RemotePackagedStarter PackagedStarter;
            public readonly IRemoteActivationHost DurableHost;
            public readonly EngineVersion Engine;
            public readonly long NowUnixSeconds;
            public readonly CancellationToken Cancellation;

            public SessionRequest(RemoteContentManifest manifest, byte[] signature,
                IEnumerable<RemotePublicSigningKey> pinnedKeys,
                RemoteReadOrigin origin, IRemoteReadTransport transport,
                RemoteReleaseState installed,
                RemotePackagedStarter starter, IRemoteActivationHost durableHost,
                EngineVersion engine, long nowUnixSeconds,
                CancellationToken cancellation)
            {
                Manifest = manifest ?? throw new ArgumentNullException(nameof(manifest));
                Signature = signature == null ? throw new ArgumentNullException(nameof(signature))
                    : (byte[])signature.Clone();
                PublicKeys = pinnedKeys == null
                    ? throw new ArgumentNullException(nameof(pinnedKeys))
                    : pinnedKeys.Take(17).ToArray();
                if (PublicKeys.Length < 1 || PublicKeys.Length > 16 ||
                    PublicKeys.Any(k => k == null))
                    throw new ArgumentException("Invalid bounded public-key registry.");
                Origin = origin ?? throw new ArgumentNullException(nameof(origin));
                Transport = transport ?? throw new ArgumentNullException(nameof(transport));
                Installed = installed ?? throw new ArgumentNullException(nameof(installed));
                DurableHost = durableHost ?? throw new ArgumentNullException(nameof(durableHost));
                if (Manifest.GameId != Installed.GameId ||
                    Manifest.Platform != Installed.Platform ||
                    Manifest.Channel != Installed.Channel)
                    throw new InvalidOperationException(
                        "Candidate does not match verified title/OS/channel.");
                PackagedStarter = starter;
                Engine = engine;
                NowUnixSeconds = nowUnixSeconds;
                Cancellation = cancellation;
            }
        }
    }
}
