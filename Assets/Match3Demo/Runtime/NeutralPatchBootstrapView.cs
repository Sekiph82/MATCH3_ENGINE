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

        public bool HasVerifiedSession => _flow != null;
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
            _lastPublicAction = "status-unavailable";
        }

        public RemotePatchUiSnapshot Snapshot()
        {
            return _flow == null ? null : _flow.Snapshot();
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
                if (_flow == null)
                    return "Content unavailable: " + _unavailableReason;
                var snapshot = _flow.Snapshot();
                switch (snapshot.Stage)
                {
                    case RemotePatchStage.Available:
                        return "Signed update available";
                    case RemotePatchStage.Downloading:
                        return "Verifying content: " + snapshot.VerifiedPercent + "%";
                    case RemotePatchStage.Verified:
                        return "Content verified. Waiting for safe activation";
                    case RemotePatchStage.Ready:
                        return "Signed content activated. Continue";
                    case RemotePatchStage.Offline:
                        return "Offline: verified content available";
                    case RemotePatchStage.NoUpdate:
                        return "Signed content already current. Continue";
                    case RemotePatchStage.Failed:
                        return "Content unavailable. Retry or use verified offline content";
                    default:
                        return "Content status unavailable";
                }
            }
        }

        private void OnGUI()
        {
            // Deliberately not drawn until an explicit host integration
            // binds an authentic signed flow. Preserve existing demo play.
            if (_flow == null) return;
            var width = Mathf.Min(430f, Mathf.Max(260f, Screen.width - 32f));
            var panel = new Rect(16f, Mathf.Max(16f, Screen.height - 140f),
                width, 124f);
            GUI.Box(panel, "ENGINE / SIGNED CONTENT");
            GUI.Label(new Rect(panel.x + 14f, panel.y + 29f,
                panel.width - 28f, 28f), VisibleStatus);
            var snapshot = _flow.Snapshot();
            GUI.Label(new Rect(panel.x + 14f, panel.y + 57f,
                panel.width - 28f, 22f),
                "Progress: " + snapshot.VerifiedPercent +
                "% | grants: disabled | offers: disabled");
            if (snapshot.CanRetry &&
                GUI.Button(new Rect(panel.x + 14f, panel.y + 86f,
                    130f, 27f), "RETRY"))
                TryRetry();
            // The real game host decides navigation after CanEnterGame
            // and never from visual progress alone.
        }
    }
}
