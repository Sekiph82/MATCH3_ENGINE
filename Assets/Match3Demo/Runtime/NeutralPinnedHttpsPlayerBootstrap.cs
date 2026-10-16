using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Match3.Engine.Runtime;
using UnityEngine;

namespace Match3.Engine.Demo
{
    // Production opt-in only. The owner of a title must explicitly set the
    // HTTPS base URL and embed a first-party RSA PUBLIC key in this scene.
    // Neither is downloaded, inferred from user prefs or shipped by default.
    // There are NO API credentials, private signing keys or remote writes.
    [DefaultExecutionOrder(-11000)]
    [DisallowMultipleComponent]
    public sealed class NeutralPinnedHttpsPlayerBootstrap : MonoBehaviour
    {
        [SerializeField] private bool enableSignedUpdates;
        [SerializeField] private string gameId = "Coral_Rescue";
        [SerializeField] private RemoteChannel channel = RemoteChannel.FamilyTest;
        [SerializeField] private string approvedHttpsPrefix = "";
        [SerializeField] private string trustedPublicKeyId = "";
        [SerializeField] private string trustedRsaModulusBase64 = "";
        [SerializeField] private long keyValidFromUnixSeconds;
        [SerializeField] private long keyValidUntilUnixSeconds;

        private NeutralMatch3Demo _game;
        private NeutralPatchBootstrapView _view;
        private NeutralPatchSessionHost _host;
        private CancellationTokenSource _lifetime;
        private bool _destroyed;
        private bool _checking;
        private bool _failedToDiscover;
        // A missing/invalid trust anchor is a permanent configuration error,
        // not a network error. Never offer a retry that can only fail.
        private bool _configurationReady;
        private string _playerStatus = "Not configured";

        public bool IsChecking => _checking;
        public bool HasVerifiedPlay => _view != null && _view.CanEnterGame;
        public string PlayerStatus => _playerStatus;
        public bool CanRetryDiscovery =>
            enableSignedUpdates && !_destroyed && !_checking &&
            _failedToDiscover && _configurationReady && _view != null &&
            !_view.HasVerifiedSession && _host != null &&
            !_host.HasConfiguredCandidate && !_host.IsDiscovering;

        private void Awake()
        {
            _game = GetComponent<NeutralMatch3Demo>();
            _view = GetComponent<NeutralPatchBootstrapView>();
            _host = GetComponent<NeutralPatchSessionHost>();
            if (!enableSignedUpdates) return;
            if (_game == null || _view == null || _host == null)
                throw new InvalidOperationException(
                    "Enabled signed patch player requires three sibling components.");
            // Lock immediately, BEFORE the board initializes or renders.
            _game.RequireSignedPrelaunch(_view);
            _view.ShowUnavailable("network-unavailable");
            _lifetime = new CancellationTokenSource();
        }

        private async void Start()
        {
            if (!enableSignedUpdates) return;
            await CheckAsync();
        }

        public bool RequestNewSignedDiscovery()
        {
            if (!CanRetryDiscovery) return false;
            _ = CheckAsync();
            return true;
        }

        private async Task CheckAsync()
        {
            _checking = true;
            _failedToDiscover = false;
            _configurationReady = false;
            _playerStatus = "Checking first-party signed content...";
            try
            {
                // No network is touched until the preinstalled base prefix,
                // game, OS, trust key and private writable root are validated.
                if (!GameProfileCatalog.IsKnownId(gameId) ||
                    string.IsNullOrWhiteSpace(trustedPublicKeyId) ||
                    string.IsNullOrWhiteSpace(trustedRsaModulusBase64))
                    throw new InvalidOperationException(
                        "First-party signed update trust not configured.");

                var platform = CurrentPlatform();
                var trustKey = new RemotePublicSigningKey(
                    trustedPublicKeyId,
                    Convert.FromBase64String(trustedRsaModulusBase64),
                    new byte[] { 0x01, 0x00, 0x01 },
                    keyValidFromUnixSeconds, keyValidUntilUnixSeconds);
                var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                if (trustKey.Revoked || now < trustKey.NotBefore ||
                    now >= trustKey.NotAfter)
                    throw new InvalidOperationException(
                        "First-party public trust is expired or not yet active.");
                var origin = new RemoteReadOrigin(approvedHttpsPrefix);
                // All persistent signed state is confined to Unity's
                // application-private location, not arbitrary scene strings.
                var existingRoot = Application.persistentDataPath;
                if (string.IsNullOrEmpty(existingRoot) ||
                    !Path.IsPathRooted(existingRoot) ||
                    !Directory.Exists(existingRoot))
                    throw new InvalidOperationException(
                        "App-private storage root unavailable.");
                var hostRoot = Path.Combine(existingRoot, "match3-signed-releases");
                Directory.CreateDirectory(hostRoot);
                var durable = new RemoteDurableActivationHost(
                    hostRoot, gameId, platform, channel);
                var engine = new EngineVersion(1, 0, 0);
                var installed = durable.Restore(new[] { trustKey }, engine, now);
                // Only a valid immutable trust config, approved origin and
                // usable app-private durable state make retry meaningful.
                _configurationReady = true;

                // Discovery fetches ONLY canonical manifest and detached
                // signature from a pinned HTTPS origin first. Payload bytes
                // are requested only after RSA verifier accepts the notice.
                var result = await _host.BeginDiscoveredHttpsPrelaunchAsync(
                    origin, new[] { trustKey }, installed, null,
                    durable, engine, now, _lifetime.Token);
                if (_destroyed) return;
                if (result != null && result.CanPlay)
                {
                    _playerStatus = result.FellBackOffline
                        ? "Verified installed release ready offline"
                        : result.NewReleaseActivated
                            ? "Signed update activated"
                            : "Installed signed content is current";
                    return;
                }

                // A bad/missing discovery notice cannot unlock play on its
                // own. Restore ONLY previously verified installed bytes;
                // never treat an HTTP status or 100% progress as content.
                if (!_view.HasVerifiedSession && installed.Active != null)
                {
                    NeutralSignedStarterPrelaunch.BindVerifiedOffline(
                        _view, installed.Active, new[] { trustKey },
                        gameId, platform, channel, engine, now);
                    _playerStatus = "Verified installed offline release";
                    return;
                }
                if (!_view.HasVerifiedSession)
                {
                    // A fresh offline install may use ONLY an authentic
                    // RSA-signed, app-owned StreamingAssets starter. Missing
                    // or forged bundles never grant input or rewards.
                    var starterReady = await NeutralSignedStarterPrelaunch
                        .TryBindAppPackagedStarterAsync(
                            _view, gameId, platform, channel, engine,
                            new[] { trustKey }, now, _lifetime.Token);
                    if (_destroyed) return;
                    if (starterReady && _view.CanEnterGame)
                    {
                        _playerStatus = "Verified packaged offline starter";
                        return;
                    }
                }
                _failedToDiscover = _configurationReady &&
                    !_host.HasConfiguredCandidate;
                _playerStatus = _failedToDiscover
                    ? "Signed notice unavailable. Check connection to retry"
                    : _view.CanRequestHostRetry
                        ? "Signed content unavailable. Use signed host Retry"
                        : "Signed content unavailable. No verified release";
            }
            catch (OperationCanceledException)
            {
                if (!_destroyed)
                {
                    _failedToDiscover = false;
                    _playerStatus = "Signed update check cancelled";
                }
            }
            catch (Exception)
            {
                if (!_destroyed)
                {
                    // No sensitive exception detail, server identity, paths,
                    // or tokens are written to the user-facing patch panel.
                    _failedToDiscover = _configurationReady &&
                        _host != null && !_host.HasConfiguredCandidate;
                    _playerStatus = !_configurationReady
                        ? "Signed update is not configured"
                        : _failedToDiscover
                            ? "Signed notice unavailable. Check connection to retry"
                            : _view != null && _view.CanRequestHostRetry
                                ? "Signed content unavailable. Use signed host Retry"
                                : "Signed content unavailable. No verified release";
                }
            }
            finally
            {
                _checking = false;
                if (_destroyed && _lifetime != null)
                {
                    _lifetime.Dispose();
                    _lifetime = null;
                }
            }
        }

        private static RemotePlatform CurrentPlatform()
        {
            switch (Application.platform)
            {
                case RuntimePlatform.WindowsPlayer:
                case RuntimePlatform.WindowsEditor:
                    return RemotePlatform.Windows;
                case RuntimePlatform.Android:
                    return RemotePlatform.Android;
                case RuntimePlatform.IPhonePlayer:
                    return RemotePlatform.Ios;
                default:
                    throw new PlatformNotSupportedException(
                        "Signed content platform is not supported.");
            }
        }

        private void OnGUI()
        {
            if (!enableSignedUpdates || _destroyed) return;
            // The neutral UX belongs to this Engine demo only. Individual
            // games will replace it through their own title/UI factory.
            var width = Mathf.Min(470f, Mathf.Max(250f, Screen.width - 32f));
            var y = Mathf.Max(210f, Screen.height - 210f);
            // Never silently show a blank locked board after a failed
            // bootstrap. Permanent configuration errors get a truthful
            // message, but no dead retry CTA.
            if (!HasVerifiedPlay)
                GUI.Box(new Rect(16f, y, width, 45f), _playerStatus);
            if (CanRetryDiscovery &&
                GUI.Button(new Rect(16f, y, 175f, 36f),
                    "CHECK SIGNED UPDATE"))
                RequestNewSignedDiscovery();
        }

        private void OnDestroy()
        {
            _destroyed = true;
            if (_lifetime == null) return;
            _lifetime.Cancel();
            // A synchronous completed run can be disposed here. Active
            // async network work owns the token until CheckAsync finishes.
            if (!_checking)
            {
                _lifetime.Dispose();
                _lifetime = null;
            }
        }
    }
}
