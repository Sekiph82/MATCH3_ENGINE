using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Match3.Engine.Runtime
{
    public enum MatchRunAxis
    {
        Horizontal = 0,
        Vertical = 1
    }

    public enum MatchShapeKind
    {
        Three = 0,
        StraightFour = 1,
        StraightFivePlus = 2,
        L = 3,
        T = 4
    }

    public enum MatchSpecialKind
    {
        None = 0,
        HorizontalLine = 1,
        VerticalLine = 2,
        Area = 3,
        Color = 4
    }

    // Immutable projection of one sequence ALREADY detected by the pinned SDK.
    // It validates adapter integrity only; it never scans a board for matches.
    public sealed class MatchRunProjection
    {
        private readonly ReadOnlyCollection<BoardPosition> _cells;

        public MatchRunAxis Axis { get; }
        public string ContentId { get; }
        public IReadOnlyList<BoardPosition> Cells => _cells;
        public int Count => _cells.Count;

        public MatchRunProjection(MatchRunAxis axis, string contentId,
            IEnumerable<BoardPosition> cells)
        {
            if (!Enum.IsDefined(typeof(MatchRunAxis), axis))
                throw new ArgumentOutOfRangeException(nameof(axis));
            if (string.IsNullOrWhiteSpace(contentId))
                throw new ArgumentException("SDK content ID required.", nameof(contentId));
            if (cells == null) throw new ArgumentNullException(nameof(cells));

            var ordered = cells.ToList();
            if (ordered.Count < 3)
                throw new ArgumentException("SDK match projection must contain at least three cells.", nameof(cells));
            if (ordered.Distinct().Count() != ordered.Count)
                throw new ArgumentException("SDK match projection cannot contain duplicate cells.", nameof(cells));

            ordered.Sort(ComparePosition);
            var first = ordered[0];
            for (var i = 0; i < ordered.Count; i++)
            {
                var cell = ordered[i];
                if (cell.Layer != first.Layer)
                    throw new ArgumentException("One SDK run cannot cross layers.", nameof(cells));
                if (axis == MatchRunAxis.Horizontal)
                {
                    if (cell.Row != first.Row || cell.Column != first.Column + i)
                        throw new ArgumentException("Malformed horizontal SDK run projection.", nameof(cells));
                }
                else
                {
                    if (cell.Column != first.Column || cell.Row != first.Row + i)
                        throw new ArgumentException("Malformed vertical SDK run projection.", nameof(cells));
                }
            }

            Axis = axis;
            ContentId = contentId;
            _cells = new ReadOnlyCollection<BoardPosition>(ordered);
        }

        internal string CanonicalKey =>
            ((int)Axis).ToString() + "|" + ContentId + "|" +
            string.Join(",", _cells.Select(x => x.ToString()));

        internal bool Contains(BoardPosition position) => _cells.Contains(position);

        internal static int ComparePosition(BoardPosition a, BoardPosition b)
        {
            var layer = a.Layer.CompareTo(b.Layer);
            if (layer != 0) return layer;
            var row = a.Row.CompareTo(b.Row);
            return row != 0 ? row : a.Column.CompareTo(b.Column);
        }
    }

    public sealed class MatchGroupPlan
    {
        private readonly ReadOnlyCollection<BoardPosition> _matchedCells;
        public MatchShapeKind Shape { get; }
        public MatchSpecialKind Special { get; }
        public string ContentId { get; }
        public BoardPosition Anchor { get; }
        public IReadOnlyList<BoardPosition> MatchedCells => _matchedCells;
        public int RewardCellCount => _matchedCells.Count;

        internal MatchGroupPlan(MatchShapeKind shape, MatchSpecialKind special,
            string contentId, BoardPosition anchor, IEnumerable<BoardPosition> matchedCells)
        {
            Shape = shape;
            Special = special;
            ContentId = contentId;
            Anchor = anchor;
            _matchedCells = new ReadOnlyCollection<BoardPosition>(
                matchedCells.Distinct().OrderBy(x => x, PositionComparer.Instance).ToList());
        }

        private sealed class PositionComparer : IComparer<BoardPosition>
        {
            internal static readonly PositionComparer Instance = new PositionComparer();
            public int Compare(BoardPosition x, BoardPosition y) =>
                MatchRunProjection.ComparePosition(x, y);
        }
    }

    public static class SpecialMatchPlanner
    {
        public static IReadOnlyList<MatchGroupPlan> Plan(
            IEnumerable<MatchRunProjection> sdkRuns,
            IEnumerable<BoardPosition> preferredAnchors = null)
        {
            if (sdkRuns == null) throw new ArgumentNullException(nameof(sdkRuns));
            var anchors = preferredAnchors == null
                ? new List<BoardPosition>()
                : preferredAnchors.Distinct().ToList();

            var runs = sdkRuns.ToList();
            if (runs.Any(x => x == null))
                throw new ArgumentException("Null SDK run projection.", nameof(sdkRuns));

            runs = runs.GroupBy(x => x.CanonicalKey, StringComparer.Ordinal)
                .Select(x => x.First())
                .OrderBy(x => x.Cells[0], PositionComparer.Instance)
                .ThenBy(x => x.Axis)
                .ThenBy(x => x.ContentId, StringComparer.Ordinal)
                .ToList();

            var consumed = new bool[runs.Count];
            var plans = new List<MatchGroupPlan>();
            for (var i = 0; i < runs.Count; i++)
            {
                if (consumed[i]) continue;
                var group = new List<MatchRunProjection>();
                var queue = new Queue<int>();
                queue.Enqueue(i);
                consumed[i] = true;
                while (queue.Count > 0)
                {
                    var current = queue.Dequeue();
                    group.Add(runs[current]);
                    for (var j = 0; j < runs.Count; j++)
                    {
                        if (consumed[j] || runs[j].ContentId != runs[current].ContentId)
                            continue;
                        if (Intersects(runs[current], runs[j]))
                        {
                            consumed[j] = true;
                            queue.Enqueue(j);
                        }
                    }
                }
                plans.Add(BuildPlan(group, anchors));
            }

            return new ReadOnlyCollection<MatchGroupPlan>(plans
                .OrderBy(x => x.Anchor, PositionComparer.Instance)
                .ThenByDescending(x => Priority(x.Shape))
                .ThenBy(x => x.ContentId, StringComparer.Ordinal)
                .ToList());
        }

        private static MatchGroupPlan BuildPlan(
            IReadOnlyList<MatchRunProjection> group,
            IReadOnlyList<BoardPosition> preferredAnchors)
        {
            var uniqueCells = group.SelectMany(x => x.Cells).Distinct()
                .OrderBy(x => x, PositionComparer.Instance).ToList();

            var longest = group.Max(x => x.Count);
            var cross = FindCross(group);
            MatchShapeKind shape;
            MatchSpecialKind special;
            BoardPosition anchor;

            if (longest >= 5)
            {
                shape = MatchShapeKind.StraightFivePlus;
                special = MatchSpecialKind.Color;
                anchor = SelectAnchor(uniqueCells, preferredAnchors);
            }
            else if (cross.HasValue)
            {
                var c = cross.Value;
                var horizontal = group.First(x => x.Axis == MatchRunAxis.Horizontal && x.Contains(c));
                var vertical = group.First(x => x.Axis == MatchRunAxis.Vertical && x.Contains(c));
                var hInterior = !c.Equals(horizontal.Cells[0]) &&
                    !c.Equals(horizontal.Cells[horizontal.Count - 1]);
                var vInterior = !c.Equals(vertical.Cells[0]) &&
                    !c.Equals(vertical.Cells[vertical.Count - 1]);
                shape = hInterior || vInterior ? MatchShapeKind.T : MatchShapeKind.L;
                special = MatchSpecialKind.Area;
                anchor = c;
            }
            else if (longest == 4)
            {
                var run = group.First(x => x.Count == 4);
                shape = MatchShapeKind.StraightFour;
                special = run.Axis == MatchRunAxis.Horizontal
                    ? MatchSpecialKind.HorizontalLine
                    : MatchSpecialKind.VerticalLine;
                anchor = SelectAnchor(run.Cells, preferredAnchors);
            }
            else
            {
                shape = MatchShapeKind.Three;
                special = MatchSpecialKind.None;
                anchor = SelectAnchor(uniqueCells, preferredAnchors);
            }

            return new MatchGroupPlan(shape, special, group[0].ContentId, anchor, uniqueCells);
        }

        private static BoardPosition? FindCross(IReadOnlyList<MatchRunProjection> group)
        {
            var horizontals = group.Where(x => x.Axis == MatchRunAxis.Horizontal).ToList();
            var verticals = group.Where(x => x.Axis == MatchRunAxis.Vertical).ToList();
            BoardPosition? best = null;
            foreach (var h in horizontals)
            foreach (var v in verticals)
            foreach (var cell in h.Cells)
            {
                if (!v.Contains(cell)) continue;
                if (!best.HasValue || MatchRunProjection.ComparePosition(cell, best.Value) < 0)
                    best = cell;
            }
            return best;
        }

        private static bool Intersects(MatchRunProjection a, MatchRunProjection b)
        {
            return a.Cells.Any(b.Contains);
        }

        private static BoardPosition SelectAnchor(
            IReadOnlyList<BoardPosition> cells,
            IReadOnlyList<BoardPosition> preferredAnchors)
        {
            foreach (var preferred in preferredAnchors)
                if (cells.Contains(preferred)) return preferred;
            return cells.OrderBy(x => x, PositionComparer.Instance).First();
        }

        private static int Priority(MatchShapeKind shape)
        {
            switch (shape)
            {
                case MatchShapeKind.StraightFivePlus: return 4;
                case MatchShapeKind.T: return 3;
                case MatchShapeKind.L: return 2;
                case MatchShapeKind.StraightFour: return 1;
                default: return 0;
            }
        }

        private sealed class PositionComparer : IComparer<BoardPosition>
        {
            internal static readonly PositionComparer Instance = new PositionComparer();
            public int Compare(BoardPosition x, BoardPosition y) =>
                MatchRunProjection.ComparePosition(x, y);
        }
    }
}
