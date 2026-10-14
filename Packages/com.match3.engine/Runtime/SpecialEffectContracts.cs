using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Match3.Engine.Runtime
{
    public enum SpecialActivationKind
    {
        HorizontalLine = 0,
        VerticalLine = 1,
        Area = 2,
        Color = 3,
        Targeted = 4
    }

    public sealed class SpecialCellState
    {
        public BoardPosition Position { get; }
        public string ContentId { get; }
        public int ObstacleLayers { get; }
        public bool Immune { get; }
        public bool StopsLinePropagation { get; }
        public int GoalPriority { get; }
        public SpecialActivationKind? Special { get; }

        public SpecialCellState(BoardPosition position, string contentId = null,
            int obstacleLayers = 0, bool immune = false,
            bool stopsLinePropagation = false, int goalPriority = 0,
            SpecialActivationKind? special = null)
        {
            if (obstacleLayers < 0) throw new ArgumentOutOfRangeException(nameof(obstacleLayers));
            if (goalPriority < 0) throw new ArgumentOutOfRangeException(nameof(goalPriority));
            if (special.HasValue && !Enum.IsDefined(typeof(SpecialActivationKind), special.Value))
                throw new ArgumentOutOfRangeException(nameof(special));
            Position = position;
            ContentId = contentId;
            ObstacleLayers = obstacleLayers;
            Immune = immune;
            StopsLinePropagation = stopsLinePropagation;
            GoalPriority = goalPriority;
            Special = special;
        }
    }

    public sealed class SpecialBoardSnapshot
    {
        private readonly ReadOnlyDictionary<BoardPosition, SpecialCellState> _cells;

        public int Rows { get; }
        public int Columns { get; }
        public int Layers { get; }
        public IReadOnlyDictionary<BoardPosition, SpecialCellState> Cells => _cells;

        public SpecialBoardSnapshot(int rows, int columns,
            IEnumerable<SpecialCellState> cells, int layers = 1)
        {
            if (rows < 1 || columns < 1 || layers < 1 ||
                rows > 1024 || columns > 1024 || layers > 16)
                throw new ArgumentOutOfRangeException(nameof(rows));
            if (cells == null) throw new ArgumentNullException(nameof(cells));
            Rows = rows;
            Columns = columns;
            Layers = layers;
            var map = new Dictionary<BoardPosition, SpecialCellState>();
            foreach (var cell in cells)
            {
                if (cell == null) throw new ArgumentException("Null special cell.", nameof(cells));
                if (!Contains(cell.Position))
                    throw new ArgumentOutOfRangeException(nameof(cells), "Special cell outside board.");
                if (map.ContainsKey(cell.Position))
                    throw new ArgumentException("Duplicate special cell.", nameof(cells));
                map.Add(cell.Position, cell);
            }
            _cells = new ReadOnlyDictionary<BoardPosition, SpecialCellState>(map);
        }

        public bool Contains(BoardPosition p) =>
            p.Row >= 0 && p.Row < Rows &&
            p.Column >= 0 && p.Column < Columns &&
            p.Layer >= 0 && p.Layer < Layers;

        public bool TryGet(BoardPosition p, out SpecialCellState cell) =>
            _cells.TryGetValue(p, out cell);
    }

    public readonly struct SpecialActivation
    {
        public SpecialActivationKind Kind { get; }
        public BoardPosition Origin { get; }
        public int Radius { get; }
        public string TargetColor { get; }
        public BoardPosition? TargetOverride { get; }

        public SpecialActivation(SpecialActivationKind kind, BoardPosition origin,
            int radius = 1, string targetColor = null, BoardPosition? targetOverride = null)
        {
            if (!Enum.IsDefined(typeof(SpecialActivationKind), kind))
                throw new ArgumentOutOfRangeException(nameof(kind));
            if (radius < 1 || radius > 32)
                throw new ArgumentOutOfRangeException(nameof(radius));
            Kind = kind;
            Origin = origin;
            Radius = radius;
            TargetColor = targetColor;
            TargetOverride = targetOverride;
        }

        internal string Key =>
            ((int)Kind) + "|" + Origin + "|" + Radius + "|" +
            (TargetColor ?? string.Empty) + "|" +
            (TargetOverride.HasValue ? TargetOverride.Value.ToString() : string.Empty);
    }

    public readonly struct SpecialHit
    {
        public BoardPosition Position { get; }
        public SpecialActivationKind Source { get; }
        public int Stage { get; }
        public int ObstacleLayersBefore { get; }
        public int ObstacleLayersAfter { get; }

        internal SpecialHit(BoardPosition position, SpecialActivationKind source,
            int stage, int before)
        {
            Position = position;
            Source = source;
            Stage = stage;
            ObstacleLayersBefore = before;
            ObstacleLayersAfter = Math.Max(0, before - 1);
        }
    }

    public readonly struct SpecialConversion
    {
        public BoardPosition Position { get; }
        public SpecialActivationKind ToKind { get; }
        public SpecialConversion(BoardPosition position, SpecialActivationKind toKind)
        {
            Position = position;
            ToKind = toKind;
        }
    }

    public sealed class SpecialEffectPlan
    {
        public IReadOnlyList<SpecialHit> Hits { get; }
        public IReadOnlyList<SpecialActivation> Activations { get; }
        public IReadOnlyList<SpecialConversion> Conversions { get; }
        public string SelectedColor { get; }
        public bool Bounded { get; }

        internal SpecialEffectPlan(IEnumerable<SpecialHit> hits,
            IEnumerable<SpecialActivation> activations,
            IEnumerable<SpecialConversion> conversions,
            string selectedColor, bool bounded)
        {
            Hits = new ReadOnlyCollection<SpecialHit>(hits.ToList());
            Activations = new ReadOnlyCollection<SpecialActivation>(activations.ToList());
            Conversions = new ReadOnlyCollection<SpecialConversion>(conversions.ToList());
            SelectedColor = selectedColor;
            Bounded = bounded;
        }
    }

    public static class SpecialEffectPlanner
    {
        private sealed class Pending
        {
            public SpecialActivation Activation;
            public int Stage;
            public Pending(SpecialActivation activation, int stage)
            {
                Activation = activation;
                Stage = stage;
            }
        }

        public static SpecialEffectPlan ResolveColorWithNormal(
            SpecialBoardSnapshot board, SpecialActivation colorSpecial,
            BoardPosition normalCell, uint seed = 1u,
            int maximumActivations = 128, int maximumTargetCandidates = 4096)
        {
            if (board == null) throw new ArgumentNullException(nameof(board));
            if (colorSpecial.Kind != SpecialActivationKind.Color)
                throw new ArgumentException("Color special required.", nameof(colorSpecial));
            SpecialCellState cell;
            if (!board.TryGet(normalCell, out cell) || cell.Immune ||
                string.IsNullOrEmpty(cell.ContentId))
                throw new ArgumentException("Eligible normal colored cell required.", nameof(normalCell));
            return Resolve(board, new SpecialActivation(
                SpecialActivationKind.Color, colorSpecial.Origin,
                colorSpecial.Radius, cell.ContentId), null, seed,
                maximumActivations, maximumTargetCandidates);
        }

        public static SpecialEffectPlan Resolve(SpecialBoardSnapshot board,
            SpecialActivation first, SpecialActivation? second = null,
            uint seed = 1u, int maximumActivations = 128,
            int maximumTargetCandidates = 4096)
        {
            if (board == null) throw new ArgumentNullException(nameof(board));
            if (seed == 0) throw new ArgumentOutOfRangeException(nameof(seed));
            if (maximumActivations < 1) throw new ArgumentOutOfRangeException(nameof(maximumActivations));
            if (maximumTargetCandidates < 1) throw new ArgumentOutOfRangeException(nameof(maximumTargetCandidates));
            if (!board.Contains(first.Origin))
                throw new ArgumentOutOfRangeException(nameof(first));
            if (second.HasValue && !board.Contains(second.Value.Origin))
                throw new ArgumentOutOfRangeException(nameof(second));

            var rng = new SeededRandom(seed);
            var queue = new Queue<Pending>();
            var hits = new List<SpecialHit>();
            var activations = new List<SpecialActivation>();
            var conversions = new List<SpecialConversion>();
            var hitCells = new HashSet<BoardPosition>();
            var activated = new HashSet<string>(StringComparer.Ordinal);
            string selectedColor = null;

            if (second.HasValue)
                ExpandPair(board, first, second.Value, rng, queue, conversions,
                    maximumTargetCandidates, ref selectedColor);
            else
                queue.Enqueue(new Pending(first, 0));

            var processed = 0;
            while (queue.Count > 0)
            {
                if (++processed > maximumActivations)
                    throw new InvalidOperationException("Special activation budget exceeded.");
                var pending = queue.Dequeue();
                var a = pending.Activation;
                if (!activated.Add(a.Key)) continue;
                activations.Add(a);

                switch (a.Kind)
                {
                    case SpecialActivationKind.HorizontalLine:
                        ResolveLine(board, a, true, pending.Stage, queue, hits, hitCells);
                        break;
                    case SpecialActivationKind.VerticalLine:
                        ResolveLine(board, a, false, pending.Stage, queue, hits, hitCells);
                        break;
                    case SpecialActivationKind.Area:
                        ResolveArea(board, a, pending.Stage, queue, hits, hitCells);
                        break;
                    case SpecialActivationKind.Color:
                    {
                        var color = string.IsNullOrEmpty(a.TargetColor)
                            ? SelectColor(board)
                            : a.TargetColor;
                        if (selectedColor == null) selectedColor = color;
                        if (color != null)
                            foreach (var cell in Ordered(board.Cells.Values)
                                .Where(x => color == "*" || x.ContentId == color))
                                TryHit(board, cell.Position, a.Kind, pending.Stage,
                                    queue, hits, hitCells);
                        break;
                    }
                    case SpecialActivationKind.Targeted:
                    {
                        var target = a.TargetOverride.HasValue &&
                            IsTargetEligible(board, a.TargetOverride.Value)
                            ? a.TargetOverride
                            : SelectTarget(board, rng, maximumTargetCandidates);
                        if (target.HasValue)
                            TryHit(board, target.Value, a.Kind, pending.Stage,
                                queue, hits, hitCells);
                        break;
                    }
                    default:
                        throw new InvalidOperationException("Unsupported special kind.");
                }
            }

            return new SpecialEffectPlan(hits, activations, conversions,
                selectedColor, true);
        }

        private static void ExpandPair(SpecialBoardSnapshot board,
            SpecialActivation a, SpecialActivation b, SeededRandom rng,
            Queue<Pending> queue, List<SpecialConversion> conversions,
            int maximumTargetCandidates, ref string selectedColor)
        {
            if (a.Kind == SpecialActivationKind.Color ||
                b.Kind == SpecialActivationKind.Color)
            {
                var color = a.Kind == SpecialActivationKind.Color ? a : b;
                var other = a.Kind == SpecialActivationKind.Color ? b : a;
                if (other.Kind == SpecialActivationKind.Color)
                {
                    queue.Enqueue(new Pending(new SpecialActivation(
                        SpecialActivationKind.Color, color.Origin,
                        targetColor: "*"), 0));
                    selectedColor = "*";
                    return;
                }

                if (other.Kind == SpecialActivationKind.Targeted)
                {
                    var target = SelectTarget(board, rng, maximumTargetCandidates);
                    if (!target.HasValue) return;
                    string targetColor = null;
                    SpecialCellState targetCell;
                    if (board.TryGet(target.Value, out targetCell))
                        targetColor = targetCell.ContentId;
                    if (string.IsNullOrEmpty(targetColor))
                        targetColor = SelectColor(board);
                    queue.Enqueue(new Pending(new SpecialActivation(
                        SpecialActivationKind.Targeted, other.Origin,
                        targetOverride: target), 0));
                    if (targetColor != null)
                        queue.Enqueue(new Pending(new SpecialActivation(
                            SpecialActivationKind.Color, target.Value,
                            targetColor: targetColor), 1));
                    selectedColor = targetColor;
                    return;
                }

                var chosen = !string.IsNullOrEmpty(color.TargetColor)
                    ? color.TargetColor
                    : GetContent(board, other.Origin) ?? SelectColor(board);
                selectedColor = chosen;
                if (chosen == null) return;

                if (other.Kind == SpecialActivationKind.HorizontalLine ||
                    other.Kind == SpecialActivationKind.VerticalLine ||
                    other.Kind == SpecialActivationKind.Area)
                {
                    foreach (var cell in Ordered(board.Cells.Values)
                        .Where(x => x.ContentId == chosen && !x.Immune))
                    {
                        conversions.Add(new SpecialConversion(cell.Position, other.Kind));
                        queue.Enqueue(new Pending(new SpecialActivation(
                            other.Kind, cell.Position, other.Radius), 1));
                    }
                }
                else
                {
                    queue.Enqueue(new Pending(new SpecialActivation(
                        SpecialActivationKind.Color, color.Origin,
                        targetColor: chosen), 0));
                }
                return;
            }

            if (a.Kind == SpecialActivationKind.Targeted ||
                b.Kind == SpecialActivationKind.Targeted)
            {
                var targeted = a.Kind == SpecialActivationKind.Targeted ? a : b;
                var partner = a.Kind == SpecialActivationKind.Targeted ? b : a;
                var target = SelectTarget(board, rng, maximumTargetCandidates);
                if (!target.HasValue) return;
                queue.Enqueue(new Pending(new SpecialActivation(
                    SpecialActivationKind.Targeted, targeted.Origin,
                    targetOverride: target), 0));
                queue.Enqueue(new Pending(new SpecialActivation(
                    partner.Kind, target.Value, partner.Radius,
                    partner.TargetColor), 1));
                return;
            }

            if (IsLine(a.Kind) && IsLine(b.Kind))
            {
                queue.Enqueue(new Pending(a, 0));
                queue.Enqueue(new Pending(b, 0));
                return;
            }

            if ((IsLine(a.Kind) && b.Kind == SpecialActivationKind.Area) ||
                (IsLine(b.Kind) && a.Kind == SpecialActivationKind.Area))
            {
                var area = a.Kind == SpecialActivationKind.Area ? a : b;
                for (var offset = -1; offset <= 1; offset++)
                {
                    var row = area.Origin.Row + offset;
                    if (row >= 0 && row < board.Rows)
                        queue.Enqueue(new Pending(new SpecialActivation(
                            SpecialActivationKind.HorizontalLine,
                            new BoardPosition(row, area.Origin.Column, area.Origin.Layer)), 0));
                    var col = area.Origin.Column + offset;
                    if (col >= 0 && col < board.Columns)
                        queue.Enqueue(new Pending(new SpecialActivation(
                            SpecialActivationKind.VerticalLine,
                            new BoardPosition(area.Origin.Row, col, area.Origin.Layer)), 0));
                }
                return;
            }

            if (a.Kind == SpecialActivationKind.Area &&
                b.Kind == SpecialActivationKind.Area)
            {
                var radius = Math.Min(32, a.Radius + b.Radius);
                queue.Enqueue(new Pending(new SpecialActivation(
                    SpecialActivationKind.Area, b.Origin, radius), 0));
                return;
            }

            queue.Enqueue(new Pending(a, 0));
            queue.Enqueue(new Pending(b, 0));
        }

        private static bool IsLine(SpecialActivationKind kind) =>
            kind == SpecialActivationKind.HorizontalLine ||
            kind == SpecialActivationKind.VerticalLine;

        private static void ResolveLine(SpecialBoardSnapshot board,
            SpecialActivation a, bool horizontal, int stage,
            Queue<Pending> queue, List<SpecialHit> hits,
            HashSet<BoardPosition> hitCells)
        {
            TryHit(board, a.Origin, a.Kind, stage, queue, hits, hitCells);
            var directions = horizontal
                ? new[] { new[] { 0, -1 }, new[] { 0, 1 } }
                : new[] { new[] { -1, 0 }, new[] { 1, 0 } };
            foreach (var d in directions)
            {
                var r = a.Origin.Row + d[0];
                var c = a.Origin.Column + d[1];
                while (r >= 0 && r < board.Rows && c >= 0 && c < board.Columns)
                {
                    var p = new BoardPosition(r, c, a.Origin.Layer);
                    SpecialCellState cell;
                    if (board.TryGet(p, out cell))
                    {
                        TryHit(board, p, a.Kind, stage, queue, hits, hitCells);
                        if (cell.StopsLinePropagation) break;
                    }
                    r += d[0];
                    c += d[1];
                }
            }
        }

        private static void ResolveArea(SpecialBoardSnapshot board,
            SpecialActivation a, int stage, Queue<Pending> queue,
            List<SpecialHit> hits, HashSet<BoardPosition> hitCells)
        {
            for (var row = Math.Max(0, a.Origin.Row - a.Radius);
                row <= Math.Min(board.Rows - 1, a.Origin.Row + a.Radius); row++)
                for (var col = Math.Max(0, a.Origin.Column - a.Radius);
                    col <= Math.Min(board.Columns - 1, a.Origin.Column + a.Radius); col++)
                    TryHit(board, new BoardPosition(row, col, a.Origin.Layer),
                        a.Kind, stage, queue, hits, hitCells);
        }

        private static void TryHit(SpecialBoardSnapshot board,
            BoardPosition position, SpecialActivationKind source, int stage,
            Queue<Pending> queue, List<SpecialHit> hits,
            HashSet<BoardPosition> hitCells)
        {
            SpecialCellState cell;
            if (!board.TryGet(position, out cell) || cell.Immune ||
                !hitCells.Add(position)) return;
            hits.Add(new SpecialHit(position, source, stage, cell.ObstacleLayers));
            if (cell.Special.HasValue)
                queue.Enqueue(new Pending(new SpecialActivation(
                    cell.Special.Value, position), stage + 1));
        }

        private static BoardPosition? SelectTarget(SpecialBoardSnapshot board,
            SeededRandom rng, int maximumTargetCandidates)
        {
            var candidates = Ordered(board.Cells.Values)
                .Where(x => !x.Immune).ToList();
            if (candidates.Count > maximumTargetCandidates)
                throw new InvalidOperationException("Target candidate budget exceeded.");
            if (candidates.Count == 0) return null;

            var goalMax = candidates.Max(x => x.GoalPriority);
            List<SpecialCellState> tier;
            if (goalMax > 0)
                tier = candidates.Where(x => x.GoalPriority == goalMax).ToList();
            else
            {
                var obstacleMax = candidates.Max(x => x.ObstacleLayers);
                tier = obstacleMax > 0
                    ? candidates.Where(x => x.ObstacleLayers == obstacleMax).ToList()
                    : candidates.Where(x => !string.IsNullOrEmpty(x.ContentId) || x.Special.HasValue).ToList();
            }
            if (tier.Count == 0) return null;
            return tier[rng.NextIndex(tier.Count)].Position;
        }

        private static bool IsTargetEligible(SpecialBoardSnapshot board, BoardPosition p)
        {
            SpecialCellState cell;
            return board.TryGet(p, out cell) && !cell.Immune;
        }

        private static string SelectColor(SpecialBoardSnapshot board)
        {
            return board.Cells.Values
                .Where(x => !x.Immune && !string.IsNullOrEmpty(x.ContentId))
                .GroupBy(x => x.ContentId, StringComparer.Ordinal)
                .OrderByDescending(x => x.Count())
                .ThenBy(x => x.Key, StringComparer.Ordinal)
                .Select(x => x.Key)
                .FirstOrDefault();
        }

        private static string GetContent(SpecialBoardSnapshot board, BoardPosition p)
        {
            SpecialCellState cell;
            return board.TryGet(p, out cell) ? cell.ContentId : null;
        }

        private static IEnumerable<SpecialCellState> Ordered(
            IEnumerable<SpecialCellState> cells)
        {
            return cells.OrderBy(x => x.Position.Layer)
                .ThenBy(x => x.Position.Row)
                .ThenBy(x => x.Position.Column);
        }
    }

    public sealed class SquareSpecialCandidate
    {
        public IReadOnlyList<BoardPosition> Cells { get; }
        public BoardPosition Anchor { get; }

        internal SquareSpecialCandidate(IEnumerable<BoardPosition> cells,
            BoardPosition anchor)
        {
            Cells = new ReadOnlyCollection<BoardPosition>(cells.ToList());
            Anchor = anchor;
        }
    }

    public static class OptionalSquareSpecialDetector
    {
        public static IReadOnlyList<SquareSpecialCandidate> Detect(
            SpecialBoardSnapshot board, GameProfile profile,
            IEnumerable<BoardPosition> sdkReservedCells = null,
            IEnumerable<BoardPosition> preferredAnchors = null)
        {
            if (board == null) throw new ArgumentNullException(nameof(board));
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            var v = EngineCompatibility.MinimumCapability;
            if (!profile.IsEnabled("match_2x2", v) ||
                !profile.IsEnabled("targeted_missile", v))
                return Array.AsReadOnly(new SquareSpecialCandidate[0]);

            var reserved = new HashSet<BoardPosition>(
                sdkReservedCells ?? Enumerable.Empty<BoardPosition>());
            var preferred = (preferredAnchors ?? Enumerable.Empty<BoardPosition>())
                .Distinct().ToList();
            var consumed = new HashSet<BoardPosition>();
            var found = new List<SquareSpecialCandidate>();

            for (var layer = 0; layer < board.Layers; layer++)
                for (var row = 0; row < board.Rows - 1; row++)
                    for (var col = 0; col < board.Columns - 1; col++)
                    {
                        var square = new[]
                        {
                            new BoardPosition(row, col, layer),
                            new BoardPosition(row, col + 1, layer),
                            new BoardPosition(row + 1, col, layer),
                            new BoardPosition(row + 1, col + 1, layer)
                        };
                        if (square.Any(x => reserved.Contains(x) || consumed.Contains(x)))
                            continue;
                        var states = new List<SpecialCellState>();
                        var valid = true;
                        foreach (var p in square)
                        {
                            SpecialCellState cell;
                            if (!board.TryGet(p, out cell) || cell.Immune ||
                                string.IsNullOrEmpty(cell.ContentId))
                            {
                                valid = false;
                                break;
                            }
                            states.Add(cell);
                        }
                        if (!valid || states.Select(x => x.ContentId)
                            .Distinct(StringComparer.Ordinal).Count() != 1)
                            continue;

                        var anchor = preferred.FirstOrDefault(x => square.Contains(x));
                        if (!square.Contains(anchor)) anchor = square[0];
                        foreach (var p in square) consumed.Add(p);
                        found.Add(new SquareSpecialCandidate(square, anchor));
                    }

            return new ReadOnlyCollection<SquareSpecialCandidate>(found);
        }
    }

    public sealed class CommittedWinSnapshot
    {
        public string OutcomeId { get; }
        public int AuthoritativeScore { get; }
        public bool FirstClearAwarded { get; }

        public CommittedWinSnapshot(string outcomeId, int authoritativeScore,
            bool firstClearAwarded)
        {
            if (string.IsNullOrWhiteSpace(outcomeId))
                throw new ArgumentException("Outcome ID required.", nameof(outcomeId));
            if (authoritativeScore < 0)
                throw new ArgumentOutOfRangeException(nameof(authoritativeScore));
            OutcomeId = outcomeId;
            AuthoritativeScore = authoritativeScore;
            FirstClearAwarded = firstClearAwarded;
        }
    }

    public enum FinalePresentationKind
    {
        ConvertMoveToSpecial = 0,
        ScorePayout = 1,
        CelebrationVfx = 2
    }

    public readonly struct FinalePresentationEvent
    {
        public int Sequence { get; }
        public FinalePresentationKind Kind { get; }
        public int Value { get; }

        public FinalePresentationEvent(int sequence,
            FinalePresentationKind kind, int value)
        {
            if (sequence < 1) throw new ArgumentOutOfRangeException(nameof(sequence));
            Sequence = sequence;
            Kind = kind;
            Value = value;
        }
    }

    public sealed class WinFinalePlan
    {
        public CommittedWinSnapshot Outcome { get; }
        public IReadOnlyList<FinalePresentationEvent> Events { get; }
        public int PresentationScoreDelta { get; }
        public int FinalScore => checked(Outcome.AuthoritativeScore + PresentationScoreDelta);
        public int AuthoritativeRewardDelta => 0;

        internal WinFinalePlan(CommittedWinSnapshot outcome,
            IEnumerable<FinalePresentationEvent> events, int presentationScoreDelta)
        {
            Outcome = outcome;
            Events = new ReadOnlyCollection<FinalePresentationEvent>(events.ToList());
            PresentationScoreDelta = presentationScoreDelta;
        }
    }

    public static class WinFinalePlanner
    {
        public static WinFinalePlan Build(CommittedWinSnapshot committedWin,
            int remainingMoves, bool convertLeftoverMoves,
            int scorePerMove)
        {
            if (committedWin == null) throw new ArgumentNullException(nameof(committedWin));
            if (remainingMoves < 0) throw new ArgumentOutOfRangeException(nameof(remainingMoves));
            if (scorePerMove < 0) throw new ArgumentOutOfRangeException(nameof(scorePerMove));
            var events = new List<FinalePresentationEvent>();
            var seq = 1;
            if (convertLeftoverMoves)
                for (var i = 0; i < remainingMoves; i++)
                    events.Add(new FinalePresentationEvent(seq++,
                        FinalePresentationKind.ConvertMoveToSpecial, i));
            var delta = checked(remainingMoves * scorePerMove);
            if (delta > 0)
                events.Add(new FinalePresentationEvent(seq++,
                    FinalePresentationKind.ScorePayout, delta));
            events.Add(new FinalePresentationEvent(seq,
                FinalePresentationKind.CelebrationVfx, 0));
            return new WinFinalePlan(committedWin, events, delta);
        }
    }

    public static class SpecialGoldenHasher
    {
        public static string HashMatchPlans(IEnumerable<MatchGroupPlan> plans)
        {
            if (plans == null) throw new ArgumentNullException(nameof(plans));
            var canonical = string.Join(";", plans.Select(p =>
                ((int)p.Shape) + "|" + ((int)p.Special) + "|" +
                p.ContentId + "|" + p.Anchor + "|" +
                string.Join(",", p.MatchedCells.Select(x => x.ToString()))));
            return Sha256(canonical);
        }

        public static string HashEffectPlan(SpecialEffectPlan plan)
        {
            if (plan == null) throw new ArgumentNullException(nameof(plan));
            var canonical = string.Join(";", plan.Hits.Select(h =>
                h.Position + "|" + ((int)h.Source) + "|" + h.Stage + "|" +
                h.ObstacleLayersBefore + ">" + h.ObstacleLayersAfter)) +
                "#" + string.Join(";", plan.Conversions.Select(c =>
                    c.Position + "|" + ((int)c.ToKind))) +
                "#" + (plan.SelectedColor ?? string.Empty);
            return Sha256(canonical);
        }

        public static string HashNamedMatrix(
            IEnumerable<KeyValuePair<string, string>> namedHashes)
        {
            if (namedHashes == null) throw new ArgumentNullException(nameof(namedHashes));
            var entries = namedHashes.ToList();
            if (entries.Any(x => string.IsNullOrWhiteSpace(x.Key) ||
                string.IsNullOrWhiteSpace(x.Value)))
                throw new ArgumentException("Named golden hashes require nonempty keys and values.",
                    nameof(namedHashes));
            if (entries.GroupBy(x => x.Key, StringComparer.Ordinal).Any(x => x.Count() > 1))
                throw new ArgumentException("Duplicate golden matrix key.", nameof(namedHashes));
            var canonical = string.Join(";", entries
                .OrderBy(x => x.Key, StringComparer.Ordinal)
                .Select(x => x.Key + "=" + x.Value));
            return Sha256(canonical);
        }

        private static string Sha256(string value)
        {
            using (var sha = SHA256.Create())
            {
                var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(value));
                var builder = new StringBuilder(bytes.Length * 2);
                foreach (var b in bytes) builder.Append(b.ToString("x2"));
                return builder.ToString();
            }
        }
    }
}
