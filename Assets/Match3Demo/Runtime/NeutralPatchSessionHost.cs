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
        private CancellationTokenSource _sceneLifetime;
        private CancellationTokenSource _linkedRequestLifetime;
        private bool _running;
        private bool _destroyed;
        private int _attemptCount;
        private long _lastVerifiedNow;

        public bool IsRunning => _running;
        public bool IsDiscovering => _discoveryInProgress;
        public bool HasConfiguredCandidate => _request != null;
        public int AttemptCount => _attemptCount;
        public RemotePrelaunchResult LastResult { get; private set; }
        public Task<RemotePrelaunchResult> CurrentAttempt => _attempt;

        private void Awake()
        {
            _sceneLifetime = new CancellationTokenSource();
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
            if (_discoveringOrConfigured() && !_discoveryHandoff)
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
            _view.MarkHostAttemptRunning();
            _linkedRequestLifetime = CancellationTokenSource
                .CreateLinkedTokenSource(cancellation, _sceneLifetime.Token);
            _request.Cancellation = _linkedRequestLifetime.Token;
            _attempt = ExecuteAsync();
            return _attempt;
        }

        // Convenience for an explicit real HTTPS path. The approved origin
        // and signed candidate are supplied by the caller; no test transport
        // can be accidentally selected by this entry point.
        private bool _discoveryInProgress;
        private bool _discoveryHandoff;

        // Safe player entry point when the first-party HTTPS server stores
        // manifest.canonical and manifest.sig under a pinned base prefix.
        // Verify BOTH files before the existing host may fetch payloads.
        // A missing/invalid signed notice never unlocks the board; the
        // caller may explicitly invoke this method again after failure.
        public Task<RemotePrelaunchResult> BeginDiscoveredHttpsPrelaunchAsync(
            RemoteReadOrigin trustedOrigin,
            IEnumerable<RemotePublicSigningKey> pinnedPublicKeys,
            RemoteReleaseState verifiedInstalled,
            RemotePackagedStarter packagedStarter,
            IRemoteActivationHost durableActivationHost,
            EngineVersion engine, long nowUnixSeconds,
            CancellationToken cancellation = default(CancellationToken))
        {
            return BeginDiscoveredPrelaunchAsync(trustedOrigin,
                new RemoteHttpsReadTransport(), pinnedPublicKeys,
                verifiedInstalled, packagedStarter, durableActivationHost,
                engine, nowUnixSeconds, cancellation);
        }

        // Injectable TEST ONLY path for deterministic synthetic transport.
        // Production MUST use BeginDiscoveredHttpsPrelaunchAsync instead.
        public async Task<RemotePrelaunchResult> BeginDiscoveredPrelaunchAsync(
            RemoteReadOrigin trustedOrigin, IRemoteReadTransport transport,
            IEnumerable<RemotePublicSigningKey> pinnedPublicKeys,
            RemoteReleaseState verifiedInstalled,
            RemotePackagedStarter packagedStarter,
            IRemoteActivationHost durableActivationHost,
            EngineVersion engine, long nowUnixSeconds,
            CancellationToken cancellation = default(CancellationToken))
        {
            if (_discoveringOrConfigured())
                throw new InvalidOperationException("Signed host is already configured.");
            if (trustedOrigin == null || transport == null ||
                pinnedPublicKeys == null || verifiedInstalled == null ||
                durableActivationHost == null)
                throw new ArgumentNullException(
                    "Pinned origin, transport, trust and durable state required.");
            // Enumerate trust once, prevent lazy trust-set replacement across
            // detached signature, manifest and subsequent payload checks.
            var keys = pinnedPublicKeys.Take(17).ToArray();
            if (keys.Length == 0 || keys.Length > 16 ||
                keys.Any(x => x == null))
                throw new ArgumentException("Invalid bounded pinned public key registry.");

            RequireGate();
            if (_view.HasVerifiedSession)
                throw new InvalidOperationException("Authenticated session already bound.");
            _discoveryInProgress = true;
            _view.ShowUnavailable("network-unavailable");
            // DO NOT dispose this token: the caller owns it. The scene-scoped
            // linked lifetime prevents downloading into a destroyed scene.
            using (var lifetime = CancellationTokenSource.CreateLinkedTokenSource(
                cancellation, _sceneLifetime.Token))
            {
                try
                {
                    var candidate = await NeutralPinnedHttpsManifestReader
                        .FetchVerifiedAsync(trustedOrigin, transport, keys,
                            verifiedInstalled.GameId, verifiedInstalled.Platform,
                            verifiedInstalled.Channel, engine, nowUnixSeconds,
                            lifetime.Token);
                    if (_destroyed || lifetime.IsCancellationRequested)
                        return null;
                    // The guard exception exists ONLY for the synchronous
                    // handoff. Never keep it open while awaiting network I/O.
                    Task<RemotePrelaunchResult> attempt;
                    _discoveryHandoff = true;
                    try
                    {
                        attempt = BeginPrelaunchAsync(candidate.Manifest,
                            candidate.Signature, keys, trustedOrigin, transport,
                            verifiedInstalled, packagedStarter,
                            durableActivationHost, engine,
                            nowUnixSeconds, cancellation);
                    }
                    finally { _discoveryHandoff = false; }
                    return await attempt;
                }
                catch (OperationCanceledException) when
                    (lifetime.IsCancellationRequested)
                {
                    if (!_destroyed && _view != null && !_view.HasVerifiedSession)
                        _view.ShowUnavailable("network-unavailable");
                    return null;
                }
                catch (Exception ex) when (
                    ex is InvalidOperationException || ex is ArgumentException ||
                    ex is System.Net.Http.HttpRequestException ||
                    ex is System.IO.IOException ||
                    ex is OperationCanceledException)
                {
                    // Manifest and detached signature errors never become
                    // playable offline evidence, a visible URL or fake Retry.
                    if (!_destroyed && _view != null && !_view.HasVerifiedSession)
                        _view.ShowUnavailable("network-unavailable");
                    return null;
                }
                finally
                {
                    _discoveryInProgress = false;
                    if (_destroyed && !_running) DisposeLocalCancellation();
                }
            }
        }

        private bool _discoveringOrConfigured()
        {
            return _discoveryInProgress || _request != null || _running ||
                _destroyed;
        }

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
            // Mark busy BEFORE any retry transport invocation can complete
            // synchronously or schedule progress onto Unity's UI thread.
            _view.MarkHostAttemptRunning(isRetry: true);
            // ExecuteAsync starts a fresh signed transport request, not just
            // a visual state reset or a fabricated authenticated receipt.
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
                if (_destroyed) DisposeLocalCancellation();
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
            // Cancel only our linked scene token, never a caller's token,
            // signing registry, transport object, or durable installed data.
            if (_sceneLifetime != null) _sceneLifetime.Cancel();
            if (!_running && !_discoveryInProgress)
                DisposeLocalCancellation();
        }

        private void DisposeLocalCancellation()
        {
            if (_linkedRequestLifetime != null)
            {
                _linkedRequestLifetime.Dispose();
                _linkedRequestLifetime = null;
            }
            if (_sceneLifetime != null)
            {
                _sceneLifetime.Dispose();
                _sceneLifetime = null;
            }
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
            public CancellationToken Cancellation;

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
