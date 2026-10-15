using System;
using System.Threading;
using Match3.Engine.Runtime;
using UnityEngine;

namespace Match3.Engine.Demo
{
    // Isolated, development-build-only SIGNED starter demonstration.
    // Its private signing key is not in the repository or the player. Only a
    // frozen public QA key is embedded, never a production trust root.
    // The Editor QA builder provisions the matching genuine RSA-signed bytes.
    // Shipping/non-development players cannot trust or execute this QA path.
    public sealed class NeutralSignedQaBootstrap : MonoBehaviour
    {
        private NeutralMatch3Demo _demo;
        private NeutralPatchBootstrapView _view;
        private CancellationTokenSource _lifetime;

        public bool IsFinished { get; private set; }
        public bool HasAuthenticatedStarter { get; private set; }

        private void Awake()
        {
            _demo = GetComponent<NeutralMatch3Demo>();
            _view = GetComponent<NeutralPatchBootstrapView>();
            if (_demo == null || _view == null)
                throw new InvalidOperationException(
                    "QA signed bootstrap requires demo and patch-view siblings.");
            // Awake precedes gameplay Start and OnGUI: the board cannot flash
            // visible or accept input before signed content is authenticated.
            _demo.RequireSignedPrelaunch(_view);
        }

        private async void Start()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            _lifetime = new CancellationTokenSource();
            try
            {
                if (Application.platform != RuntimePlatform.WindowsEditor &&
                    Application.platform != RuntimePlatform.WindowsPlayer)
                    throw new InvalidOperationException(
                        "Windows QA starter requires a Windows editor/player.");

                var publicModulus = Convert.FromBase64String(
                    "zoodN2wo20EQxljrIekNMICz96ZUP60ns3ShOygfHdBo4O5k7AcerQx8+Cf3UrCz52k+s+C9RuxlsyU9Y+AlXIJUYJ5CfI3IIpvb7LDHWzgUEu9CXIIrQ9ZX3y4/5D3GMF1CeHZOj9If2rOS41HpVXPf/OdtQxlccGdVdGi9LEw5p5yb39Zt2zA6OFlHzlk7fNB/w27bPVVCinYfk+kipmKHJ93G6dykWqcrh2X4dFarqpcZe3UCFaEAVhA06NiTFm79L1ESeDTJORE2pB7gmgFPY8p0vfexiZwvkYD9BgfGlWZYeA8k7TFxwog1MzLNwZbIEeR5rQ+G5mzkOtN4TQ==");
                var key = new RemotePublicSigningKey(
                    "m11-qa-public", publicModulus,
                    new byte[] { 0x01, 0x00, 0x01 },
                    1792022400L, 1823472000L);
                HasAuthenticatedStarter =
                    await NeutralSignedStarterPrelaunch.TryBindAppPackagedStarterAsync(
                        _view, "Coral_Rescue", RemotePlatform.Windows,
                        RemoteChannel.Development, new EngineVersion(1, 0, 0),
                        new[] { key }, DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                        _lifetime.Token);
            }
            catch (OperationCanceledException)
            {
                // A cancelled scene cannot authorize any gameplay.
                HasAuthenticatedStarter = false;
            }
            catch (Exception)
            {
                // No local paths, signatures or exception internals in UI.
                HasAuthenticatedStarter = false;
                if (_view != null && !_view.HasVerifiedSession)
                    _view.ShowUnavailable("content-unavailable");
            }
            finally
            {
                IsFinished = true;
            }
#else
            // An accidentally referenced QA scene in an actual release must
            // fail closed, not embed a test trust root or unlock the board.
            _view.ShowUnavailable("no-signed-starter");
            IsFinished = true;
#endif
        }

        private void OnDestroy()
        {
            if (_lifetime == null) return;
            _lifetime.Cancel();
            _lifetime.Dispose();
            _lifetime = null;
        }
    }
}
