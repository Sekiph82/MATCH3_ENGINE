using Cysharp.Threading.Tasks;
using Match3.Core.Structs;
using UnityEngine;

namespace Match3.Engine.Demo
{
    public sealed class NeutralMatch3Demo : MonoBehaviour
    {
        private static readonly Color[] ItemColors =
        {
            new Color(0.20f, 0.65f, 0.92f),
            new Color(0.96f, 0.60f, 0.22f),
            new Color(0.42f, 0.78f, 0.46f),
            new Color(0.70f, 0.48f, 0.88f)
        };

        private DemoGame _game;
        private NeutralPatchBootstrapView _signedPrelaunchGate;
        private GUIStyle _cellStyle;
        private bool _isReady;
        private bool _hasSelection;
        private GridPosition _selected;

        public DemoMetrics Metrics { get; private set; }
        public bool IsReady => _isReady;
        public bool IsBusy => _game != null && _game.IsBusy;
        public bool IsBoardInputAllowed =>
            _signedPrelaunchGate == null || _signedPrelaunchGate.CanEnterGame;
        public bool IsSignedContentRequired => _signedPrelaunchGate != null;
        public bool IsPlayable => _isReady && IsBoardInputAllowed;
        // A fully initialized in-memory board is not a playable, signed
        // session. In particular, never label the screen "Ready" when the
        // input gate is closed. This drives both the UI and acceptance tests.
        public string VisibleGameplayStatus
        {
            get
            {
                if (!_isReady)
                    return "Preparing board...";
                if (!IsBoardInputAllowed)
                    return "Locked: " + _signedPrelaunchGate.VisibleStatus;
                if (Metrics != null && Metrics.GameFinished)
                    return "WIN - goal reached after cascade";
                return IsBusy ? "Resolving match..." : "Ready";
            }
        }

        // Opt-in only. A commercial host must install its signed-content
        // prelaunch gate BEFORE exposing the playable board. The default
        // standalone neutral SDK demo stays playable without remote content.
        public void RequireSignedPrelaunch(NeutralPatchBootstrapView view)
        {
            if (view == null) throw new System.ArgumentNullException(nameof(view));
            if (_signedPrelaunchGate != null && _signedPrelaunchGate != view)
                throw new System.InvalidOperationException(
                    "Signed prelaunch gate may not be silently replaced.");
            _signedPrelaunchGate = view;
            _hasSelection = false;
        }
        public string BoardSnapshot => _isReady ? _game.GetSnapshot() : string.Empty;

        private void Start()
        {
            Metrics = new DemoMetrics();
            InitializeAsync().Forget();
        }

        private async UniTaskVoid InitializeAsync()
        {
            _game = new DemoGame(Metrics);
            await _game.InitializeAsync();
            _isReady = true;
        }

        public async UniTask<bool> TrySwapAsync(GridPosition first, GridPosition second)
        {
            if (!_isReady || !IsBoardInputAllowed)
            {
                return false;
            }

            return await _game.TrySwapAsync(first, second);
        }

        private void OnGUI()
        {
            if (Metrics == null)
            {
                GUI.Label(new Rect(16, 16, 360, 32), "Loading neutral Match-3 demo...");
                return;
            }

            GUI.Label(new Rect(16, 12, 420, 30), "Match-3 Engine | Seed " + Metrics.Seed);
            GUI.Label(new Rect(16, 40, 420, 26), "Goal: clear 3 items    Moves: " + Metrics.MovesRemaining);
            GUI.Label(new Rect(16, 64, 650, 26), VisibleGameplayStatus);

            if (!_isReady)
            {
                return;
            }
            if (!IsBoardInputAllowed)
            {
                GUI.Box(new Rect(16, 99, 440, 74),
                    "Signed prelaunch required before board interaction");
                GUI.Label(new Rect(28, 130, 410, 25),
                    _signedPrelaunchGate.VisibleStatus);
                return;
            }

            if (_cellStyle == null)
            {
                _cellStyle = new GUIStyle(GUI.skin.button)
                {
                    fontSize = 24,
                    fontStyle = FontStyle.Bold
                };
            }

            const float cellSize = 68f;
            const float gap = 5f;
            const float left = 16f;
            const float top = 100f;
            var oldColor = GUI.backgroundColor;

            for (var row = 0; row < DemoScenario.Rows; row++)
            {
                for (var column = 0; column < DemoScenario.Columns; column++)
                {
                    var itemId = _game.GetItemId(row, column);
                    var rect = new Rect(left + column * (cellSize + gap), top + row * (cellSize + gap), cellSize, cellSize);
                    GUI.backgroundColor = ItemColors[itemId];
                    var selected = _hasSelection && _selected.RowIndex == row && _selected.ColumnIndex == column;
                    if (GUI.Button(rect, selected ? "[" + (char)('A' + itemId) + "]" : ((char)('A' + itemId)).ToString(), _cellStyle))
                    {
                        SelectCell(row, column);
                    }
                }
            }

            GUI.backgroundColor = oldColor;
            GUI.Label(new Rect(16, top + DemoScenario.Rows * (cellSize + gap) + 8, 420, 26),
                "Click two edge-sharing tiles. Invalid moves return to their starting cells.");
        }

        private void SelectCell(int row, int column)
        {
            if (!IsBoardInputAllowed || IsBusy || Metrics.GameFinished)
            {
                return;
            }

            var clicked = new GridPosition(row, column);
            if (!_hasSelection)
            {
                _selected = clicked;
                _hasSelection = true;
                return;
            }

            var first = _selected;
            _hasSelection = false;
            _game.TrySwapAsync(first, clicked).Forget();
        }
    }
}
