using System;
using Match3.Engine.Runtime;
using UnityEngine;

namespace Match3.Engine.Demo
{
    // Engine-neutral player-facing patch status. Opt-in binding from an
    // authenticated prelaunch bootstrap; never invents a download or reward.
    // Game-specific visuals belong to separate title/VUF adapters.
    public sealed class NeutralPatchBootstrapView : MonoBehaviour
    {
        private RemotePatchFlow _flow;
        private string _unavailableReason = "No signed update session";
        private string _lastPublicAction = "";
        // The real host must provide a callback that starts an actual retry.
        // Showing a clickable RETRY from the patch-state model alone would
        // be a dead CTA and was rejected in the owner's Win64 visual review.
        private Func<bool> _requestHostRetry;
        private bool _hostRetryPending;
        // A transient signed Engine snapshot may inform progress, but only
        // Bind() may grant entry, and only after signed commit/fallback.
        private RemotePatchUiSnapshot _prelaunchProgress;
        private int _completedHostAttempts;

        public bool HasVerifiedSession => _flow != null;
        public bool CanRequestHostRetry =>
            _requestHostRetry != null && !_hostRetryPending &&
            (_flow == null
                ? (_prelaunchProgress == null || _prelaunchProgress.CanRetry)
                : _flow.Snapshot().CanRetry);
        public bool HostRetryPending => _hostRetryPending;
        public bool CanEnterGame => _flow != null && _flow.Snapshot().CanPlay;
        public string LastPublicAction => _lastPublicAction;

        public void Bind(RemotePatchFlow verifiedSession)
        {
            if (verifiedSession == null)
                throw new ArgumentNullException(nameof(verifiedSession));
            if (_flow != null)
                throw new InvalidOperationException(
                    "Already bound: a bootstrap session cannot be swapped silently.");
            _flow = verifiedSession;
            _prelaunchProgress = null;
            _completedHostAttempts++;
            _hostRetryPending = false;
            _unavailableReason = "";
            _lastPublicAction = "signed-session-bound";
        }

        public void ShowUnavailable(string reasonKey)
        {
            if (_flow != null)
                throw new InvalidOperationException(
                    "Do not overwrite authenticated content status.");
            if (reasonKey != "no-signed-starter" &&
                reasonKey != "network-unavailable" &&
                reasonKey != "content-unavailable")
                throw new ArgumentException("Unsafe/unbounded UI reason key.");
            _unavailableReason = reasonKey;
            _prelaunchProgress = null;
            _completedHostAttempts++;
            _hostRetryPending = false;
            _lastPublicAction = "status-unavailable";
        }

        // Register ONCE on the main thread before patch UI can offer RETRY.
        // An accepted callback means a new host attempt was scheduled, never
        // that bytes downloaded or that content was authenticated.
        public void RegisterHostRetry(Func<bool> requestRealRetry)
        {
            if (requestRealRetry == null)
                throw new ArgumentNullException(nameof(requestRealRetry));
            if (_requestHostRetry != null)
                throw new InvalidOperationException(
                    "Cannot silently replace the configured retry host.");
            _requestHostRetry = requestRealRetry;
        }

        // Presentation-only progress. This cannot set CanEnterGame, add a
        // signed session, or grant a reward. Only the host calls this using
        // Engine-produced snapshots; no arbitrary percent setter exists.
        internal void ObservePrelaunchProgress(RemotePatchUiSnapshot snapshot)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            if (_flow != null) return;
            _prelaunchProgress = snapshot;
        }

        internal void MarkHostAttemptFailed()
        {
            if (_flow != null) return;
            _hostRetryPending = false;
            _completedHostAttempts++;
            _lastPublicAction = "status-unavailable";
        }

        // Called only by the owning host when signature configuration,
        // cancellation, or platform setup is unrecoverable. Never leave
        // a visible Retry button that cannot actually issue a safe request.
        internal void DisableHostRetry()
        {
            _requestHostRetry = null;
            _hostRetryPending = false;
            _completedHostAttempts++;
        }

        public bool TryRequestHostRetry()
        {
            if (!CanRequestHostRetry)
                return false;
            var sessionBeforeRequest = _flow;
            var completionBeforeRequest = _completedHostAttempts;
            var accepted = false;
            try
            {
                accepted = _requestHostRetry();
            }
            catch (Exception)
            {
                // Never expose a host error, local file path or network URL.
                // Do not fabricate any download progress or unlock gameplay.
                accepted = false;
            }
            if (!accepted)
                return false;
            // A synchronously completed retry may bind content OR fail.
            // Neither outcome may be overwritten with stale pending UI.
            if (_completedHostAttempts == completionBeforeRequest &&
                ReferenceEquals(_flow, sessionBeforeRequest))
            {
                _hostRetryPending = true;
                _lastPublicAction = "host-retry-scheduled";
            }
            return true;
        }

        public RemotePatchUiSnapshot Snapshot()
        {
            return _flow == null ? _prelaunchProgress : _flow.Snapshot();
        }

        public bool TryRetry()
        {
            if (_flow == null || !_flow.Snapshot().CanRetry)
                return false;
            _flow.Retry();
            _lastPublicAction = "retry-requested";
            return true; // UI request only. Caller owns actual network I/O.
        }

        public string VisibleStatus
        {
            get
            {
                if (_flow == null && _prelaunchProgress == null)
                    return "Content unavailable: " + _unavailableReason;
                var snapshot = Snapshot();
                switch (snapshot.Stage)
                {
                    case RemotePatchStage.Available:
                        return "Signed update available";
                    case RemotePatchStage.Downloading:
                        return "Verifying content: " + snapshot.VerifiedPercent + "%";
                    case RemotePatchStage.Verified:
                        return "Content verified. Waiting for safe activation";
                    case RemotePatchStage.Ready:
                        return _flow == null
                            ? "Verified. Waiting for signed activation"
                            : "Signed content activated. Continue";
                    case RemotePatchStage.Offline:
                        return _flow == null
                            ? "Checking verified offline release"
                            : "Offline: verified content available";
                    case RemotePatchStage.NoUpdate:
                        return _flow == null
                            ? "Checking installed signed release"
                            : "Signed content already current. Continue";
                    case RemotePatchStage.Failed:
                        return "Content unavailable. Retry or use verified offline content";
                    default:
                        return "Content status unavailable";
                }
            }
        }

        private void OnGUI()
        {
            // A dormant demo never shows a fake patch. A configured real
            // host may show a truthful unavailable panel with a working
            // retry CTA even before a signed session has been bound.
            if (_flow == null && _requestHostRetry == null) return;
            var width = Mathf.Min(430f, Mathf.Max(260f, Screen.width - 32f));
            var panel = new Rect(16f, Mathf.Max(16f, Screen.height - 140f),
                width, 124f);
            GUI.Box(panel, "ENGINE / SIGNED CONTENT");
            GUI.Label(new Rect(panel.x + 14f, panel.y + 29f,
                panel.width - 28f, 28f), VisibleStatus);
            var snapshot = Snapshot();
            var progressText = snapshot == null
                ? "No verified content | grants: disabled | offers: disabled"
                : "Progress: " + snapshot.VerifiedPercent +
                  "% | grants: disabled | offers: disabled";
            GUI.Label(new Rect(panel.x + 14f, panel.y + 57f,
                panel.width - 28f, 22f), progressText);
            if (_hostRetryPending)
                GUI.Label(new Rect(panel.x + 14f, panel.y + 86f,
                    panel.width - 28f, 25f), "Retry requested. Waiting for host...");
            else if (CanRequestHostRetry &&
                GUI.Button(new Rect(panel.x + 14f, panel.y + 86f,
                    130f, 27f), "RETRY"))
                TryRequestHostRetry();
            // The real host must bind a newly authenticated session before
            // the gate opens. Retry scheduling never changes CanEnterGame.
        }
    }
}
