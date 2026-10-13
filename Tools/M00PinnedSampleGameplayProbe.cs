using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Common;
using Common.Interfaces;
using Match3.Core.Structs;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;

[InitializeOnLoad]
public static class M00PinnedSampleGameplayProbe
{
    private sealed class MoveCandidate
    {
        public IUnityGridSlot First;
        public IUnityGridSlot Second;
    }

    private const string ScenePath = "Assets/Scenes/MainScene.unity";
    // Play Mode may reload the editor scripting domain and discard callbacks.
    // SessionState persists within this one temporary Unity Editor process.
    private const string ActiveSessionKey = "M00PinnedSdkProbe.Active";
    private const string StartSessionKey = "M00PinnedSdkProbe.Start";
    private const double ActionSettleSeconds = 2.5;
    private const double ProbeTimeoutSeconds = 120;
    private static readonly MethodInfo PointerDown = typeof(CanvasInputSystem).GetMethod("OnPointerDown", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly MethodInfo PointerDrag = typeof(CanvasInputSystem).GetMethod("OnPointerDrag", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo CameraField = typeof(CanvasInputSystem).GetField("_camera", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly List<MoveCandidate> Candidates = new List<MoveCandidate>();

    private static UnityGame _game;
    private static CanvasInputSystem _input;
    private static UnityGameBoardRenderer _renderer;
    private static Camera _camera;
    private static string _beforeAction;
    private static double _probeStartedAt;
    private static double _playModeRequestedAt;
    private const double PlayModeEntryTimeoutSeconds = 120;
    private static double _actionStartedAt;
    private static int _candidateIndex;
    private static bool _waitingForAction;
    private static bool _tickStarted;

    static M00PinnedSampleGameplayProbe()
    {
        if (SessionState.GetBool(ActiveSessionKey, false))
        {
            EditorApplication.update += RestoreAfterDomainReload;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }
    }

    public static void Run()
    {
        if (PointerDown == null || PointerDrag == null || CameraField == null)
        {
            Fail("original sample input hooks could not be reflected");
            return;
        }

        Debug.Log("M00_SAMPLE_PROBE_ENTRY method=Run scene=" + ScenePath);
        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        _playModeRequestedAt = EditorApplication.timeSinceStartup;
        SessionState.SetFloat(StartSessionKey, (float)_playModeRequestedAt);
        SessionState.SetBool(ActiveSessionKey, true);
        EditorApplication.update += RestoreAfterDomainReload;
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        EditorApplication.isPlaying = true;
    }

    private static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredPlayMode)
        {
            return;
        }

        BeginProbeAfterPlayMode();
    }

    private static void RestoreAfterDomainReload()
    {
        if (!SessionState.GetBool(ActiveSessionKey, false))
        {
            EditorApplication.update -= RestoreAfterDomainReload;
            return;
        }

        if (EditorApplication.isPlaying)
        {
            BeginProbeAfterPlayMode();
            return;
        }

        double requestedAt = SessionState.GetFloat(StartSessionKey, (float)_playModeRequestedAt);
        if (EditorApplication.timeSinceStartup - requestedAt > PlayModeEntryTimeoutSeconds)
        {
            Fail("timed out waiting for actual PlayMode after script-domain reload");
        }
    }

    private static void BeginProbeAfterPlayMode()
    {
        if (_tickStarted || !SessionState.GetBool(ActiveSessionKey, false)) return;
        _tickStarted = true;
        EditorApplication.update -= RestoreAfterDomainReload;
        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        Debug.Log("M00_SAMPLE_PROBE_ENTERED_PLAYMODE");
        _probeStartedAt = EditorApplication.timeSinceStartup;
        EditorApplication.update += Tick;
    }

    private static void Tick()
    {
        if (EditorApplication.timeSinceStartup - _probeStartedAt > ProbeTimeoutSeconds)
        {
            Fail("timed out before an accepted board-changing input action was observed");
            return;
        }

        if (_game == null)
        {
            var context = UnityEngine.Object.FindAnyObjectByType<AppContext>();
            _input = UnityEngine.Object.FindAnyObjectByType<CanvasInputSystem>();
            _renderer = UnityEngine.Object.FindAnyObjectByType<UnityGameBoardRenderer>();
            if (context == null || _input == null || _renderer == null)
            {
                return;
            }

            try
            {
                _game = context.Resolve<UnityGame>();
                _camera = (Camera)CameraField.GetValue(_input);
                if (_game == null || _camera == null)
                {
                    return;
                }
                BuildCandidates(_game.GetGridSlots().ToArray());
                Debug.Log("M00_SAMPLE_PROBE_READY scene=" + ScenePath + " movable_edges=" + Candidates.Count);
            }
            catch (System.Exception exception)
            {
                Debug.LogWarning("M00 sample board is not ready: " + exception.GetType().Name);
                _game = null;
                return;
            }
        }

        if (_waitingForAction)
        {
            if (EditorApplication.timeSinceStartup - _actionStartedAt < ActionSettleSeconds)
            {
                return;
            }

            var after = Snapshot(_game.GetGridSlots());
            if (!string.Equals(after, _beforeAction, System.StringComparison.Ordinal))
            {
                Debug.Log("M00_SAMPLE_GAMEPLAY_PROBE=PASS source_commit=771e62908056c2c5fa69937a52ca52ab589f19ea scene=" + ScenePath + " accepted_input=PointerDown+PointerDrag board_changed=true before=" + _beforeAction + " after=" + after);
                Finish(0);
                return;
            }

            _waitingForAction = false;
        }

        if (_candidateIndex >= Candidates.Count)
        {
            Fail("all movable adjacent pairs rolled back or failed to change the original board");
            return;
        }

        var candidate = Candidates[_candidateIndex++];
        _beforeAction = Snapshot(_game.GetGridSlots());
        SendPointer(PointerDown, candidate.First.GridPosition);
        SendPointer(PointerDrag, candidate.Second.GridPosition);
        _actionStartedAt = EditorApplication.timeSinceStartup;
        _waitingForAction = true;
    }

    private static void BuildCandidates(IUnityGridSlot[] slots)
    {
        Candidates.Clear();
        var byPosition = slots.ToDictionary(slot => Key(slot.GridPosition));
        foreach (var slot in slots.OrderBy(item => item.GridPosition.RowIndex).ThenBy(item => item.GridPosition.ColumnIndex))
        {
            if (!slot.IsMovable)
            {
                continue;
            }

            var row = slot.GridPosition.RowIndex;
            var column = slot.GridPosition.ColumnIndex;
            AddCandidate(byPosition, slot, new GridPosition(row, column + 1));
            AddCandidate(byPosition, slot, new GridPosition(row + 1, column));
        }

        // Choose a genuine 3+ match before trial swaps. Trying board edges in
        // coordinate order often exhausts the editor watchdog on invalid swaps.
        // This is an independent board-read probe, not a replacement game rule.
        var ordered = Candidates.OrderByDescending(candidate =>
            ProducesMatchOfThree(byPosition, candidate.First, candidate.Second)).ToArray();
        var expectedMatches = ordered.Count(candidate =>
            ProducesMatchOfThree(byPosition, candidate.First, candidate.Second));
        Candidates.Clear();
        Candidates.AddRange(ordered);
        Debug.Log("M00_SAMPLE_PREDICTED_MATCH_CANDIDATES=" + expectedMatches +
                  " TOTAL_CANDIDATES=" + Candidates.Count);
    }

    private static bool ProducesMatchOfThree(IDictionary<string, IUnityGridSlot> board,
        IUnityGridSlot first, IUnityGridSlot second)
    {
        if (!first.HasItem || !second.HasItem || first.Item.ContentId == second.Item.ContentId)
        {
            return false;
        }
        return MakesLine(board, first.GridPosition, first, second, 1, 0) ||
               MakesLine(board, first.GridPosition, first, second, 0, 1) ||
               MakesLine(board, second.GridPosition, first, second, 1, 0) ||
               MakesLine(board, second.GridPosition, first, second, 0, 1);
    }

    private static bool MakesLine(IDictionary<string, IUnityGridSlot> board,
        GridPosition position, IUnityGridSlot first, IUnityGridSlot second, int dr, int dc)
    {
        int expectedId = position.Equals(first.GridPosition)
            ? second.Item.ContentId : first.Item.ContentId;
        int count = 1;
        foreach (int sign in new[] { -1, 1 })
        {
            int row = position.RowIndex + sign * dr;
            int col = position.ColumnIndex + sign * dc;
            while (true)
            {
                var probe = new GridPosition(row, col);
                int? actual = ContentAfterSwap(board, probe, first, second);
                if (!actual.HasValue || actual.Value != expectedId) break;
                count++;
                row += sign * dr;
                col += sign * dc;
            }
        }
        return count >= 3;
    }

    private static int? ContentAfterSwap(IDictionary<string, IUnityGridSlot> board,
        GridPosition position, IUnityGridSlot first, IUnityGridSlot second)
    {
        if (position.Equals(first.GridPosition)) return second.Item.ContentId;
        if (position.Equals(second.GridPosition)) return first.Item.ContentId;
        IUnityGridSlot slot;
        return board.TryGetValue(Key(position), out slot) && slot.HasItem
            ? (int?)slot.Item.ContentId : null;
    }

    private static void AddCandidate(IDictionary<string, IUnityGridSlot> byPosition, IUnityGridSlot first, GridPosition secondPosition)
    {
        IUnityGridSlot second;
        if (byPosition.TryGetValue(Key(secondPosition), out second) && second.IsMovable)
        {
            Candidates.Add(new MoveCandidate { First = first, Second = second });
        }
    }

    private static string Key(GridPosition position)
    {
        return position.RowIndex + ":" + position.ColumnIndex;
    }

    private static void SendPointer(MethodInfo method, GridPosition position)
    {
        var world = _renderer.GetWorldPosition(position);
        var screen = _camera.WorldToScreenPoint(world);
        var data = new PointerEventData(EventSystem.current) { position = screen };
        method.Invoke(_input, new object[] { data });
    }

    private static string Snapshot(IEnumerable<IUnityGridSlot> slots)
    {
        return string.Join("/", slots.OrderBy(slot => slot.GridPosition.RowIndex).ThenBy(slot => slot.GridPosition.ColumnIndex)
            .Select(slot => slot.HasItem && slot.Item != null ? RuntimeHelpers.GetHashCode(slot.Item).ToString("x8") : "empty"));
    }

    private static void Fail(string reason)
    {
        Debug.LogError("M00_SAMPLE_GAMEPLAY_PROBE=FAIL reason=" + reason);
        Finish(2);
    }

    private static void Finish(int exitCode)
    {
        SessionState.SetBool(ActiveSessionKey, false);
        EditorApplication.update -= Tick;
        EditorApplication.update -= RestoreAfterDomainReload;
        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        Debug.Log("M00_SAMPLE_PROBE_EXIT=" + exitCode);
        // Calling Exit directly avoids losing delayCall on the PlayMode domain reload.
        EditorApplication.Exit(exitCode);
    }
}
