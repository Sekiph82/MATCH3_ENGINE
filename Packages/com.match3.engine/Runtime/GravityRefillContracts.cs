using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Match3.Engine.Runtime
{
    public enum GravityDirection
    {
        Down = 0,
        Up = 1,
        Left = 2,
        Right = 3
    }

    public enum DiagonalTieBreak
    {
        LowerCoordinate = 0,
        HigherCoordinate = 1
    }

    public readonly struct BoardDisplacement
    {
        public BoardPosition Source { get; }
        public BoardPosition Target { get; }
        public string ContentId { get; }

        public BoardDisplacement(BoardPosition source, BoardPosition target, string contentId)
        {
            if (string.IsNullOrWhiteSpace(contentId))
                throw new ArgumentException("Content ID required.", nameof(contentId));
            Source = source;
            Target = target;
            ContentId = contentId;
        }
    }

    public sealed class GravityPlan
    {
        private readonly ReadOnlyDictionary<BoardPosition, string> _occupants;
        private readonly ReadOnlyCollection<BoardDisplacement> _moves;

        public IReadOnlyDictionary<BoardPosition, string> Occupants => _occupants;
        public IReadOnlyList<BoardDisplacement> Moves => _moves;
        public int Operations { get; }

        internal GravityPlan(IDictionary<BoardPosition, string> occupants,
            IEnumerable<BoardDisplacement> moves, int operations)
        {
            _occupants = new ReadOnlyDictionary<BoardPosition, string>(
                new Dictionary<BoardPosition, string>(occupants));
            _moves = new ReadOnlyCollection<BoardDisplacement>(moves.ToList());
            Operations = operations;
        }
    }

    public static class GravityPlanner
    {
        private sealed class Piece
        {
            public BoardPosition Source;
            public string ContentId;
            public Piece(BoardPosition source, string contentId)
            {
                Source = source;
                ContentId = contentId;
            }
        }

        public static GravityPlan PlanCardinal(BoardTopology board,
            GravityDirection direction = GravityDirection.Down)
        {
            if (board == null) throw new ArgumentNullException(nameof(board));
            RequireDirection(direction);

            var result = new Dictionary<BoardPosition, string>();
            var moves = new List<BoardDisplacement>();
            var operations = 0;

            foreach (var pair in board.Occupants)
                if (board.IsLocked(pair.Key))
                    result.Add(pair.Key, pair.Value);

            for (var layer = 0; layer < board.Layers; layer++)
            {
                var lanes = IsVertical(direction) ? board.Columns : board.Rows;
                for (var lane = 0; lane < lanes; lane++)
                {
                    var segment = new List<BoardPosition>();
                    foreach (var p in EnumerateLane(board, layer, lane, direction))
                    {
                        operations++;
                        if (board.IsHole(p) || board.IsLocked(p))
                        {
                            FlushSegment(board, segment, result, moves);
                            segment.Clear();
                            continue;
                        }
                        segment.Add(p);
                    }
                    FlushSegment(board, segment, result, moves);
                }
            }

            return new GravityPlan(result, moves, operations);
        }

        public static GravityPlan PlanDiagonal(BoardTopology board,
            GravityDirection direction, DiagonalTieBreak tieBreak,
            int maximumPasses = 256)
        {
            if (board == null) throw new ArgumentNullException(nameof(board));
            RequireDirection(direction);
            if (!Enum.IsDefined(typeof(DiagonalTieBreak), tieBreak))
                throw new ArgumentOutOfRangeException(nameof(tieBreak));
            if (maximumPasses < 1 || maximumPasses > 4096)
                throw new ArgumentOutOfRangeException(nameof(maximumPasses));

            var occupants = new Dictionary<BoardPosition, string>(board.Occupants);
            var moves = new List<BoardDisplacement>();
            var operations = 0;

            for (var pass = 0; pass < maximumPasses; pass++)
            {
                var changed = false;
                foreach (var source in EnumerateSources(board, direction))
                {
                    operations++;
                    if (board.IsHole(source) || board.IsLocked(source))
                        continue;
                    string content;
                    if (!occupants.TryGetValue(source, out content))
                        continue;

                    var direct = Step(source, direction);
                    BoardPosition target;
                    if (CanEnter(board, occupants, direct))
                    {
                        target = direct;
                    }
                    else
                    {
                        var candidates = Diagonals(source, direction, tieBreak)
                            .Where(p => CanEnter(board, occupants, p)).ToArray();
                        if (candidates.Length == 0) continue;
                        target = candidates[0];
                    }

                    occupants.Remove(source);
                    occupants.Add(target, content);
                    moves.Add(new BoardDisplacement(source, target, content));
                    changed = true;
                }

                if (!changed)
                    return new GravityPlan(occupants, moves, operations);
            }

            throw new InvalidOperationException("Diagonal gravity exceeded its pass budget.");
        }

        private static void FlushSegment(BoardTopology board,
            IList<BoardPosition> targetFirstSegment,
            IDictionary<BoardPosition, string> result,
            IList<BoardDisplacement> moves)
        {
            if (targetFirstSegment.Count == 0) return;
            var pieces = new List<Piece>();
            foreach (var p in targetFirstSegment)
            {
                string content;
                if (board.Occupants.TryGetValue(p, out content))
                    pieces.Add(new Piece(p, content));
            }

            for (var i = 0; i < pieces.Count; i++)
            {
                var target = targetFirstSegment[i];
                var piece = pieces[i];
                result.Add(target, piece.ContentId);
                if (!piece.Source.Equals(target))
                    moves.Add(new BoardDisplacement(piece.Source, target, piece.ContentId));
            }
        }

        private static IEnumerable<BoardPosition> EnumerateLane(
            BoardTopology board, int layer, int lane, GravityDirection direction)
        {
            if (direction == GravityDirection.Down)
                for (var row = board.Rows - 1; row >= 0; row--)
                    yield return new BoardPosition(row, lane, layer);
            else if (direction == GravityDirection.Up)
                for (var row = 0; row < board.Rows; row++)
                    yield return new BoardPosition(row, lane, layer);
            else if (direction == GravityDirection.Right)
                for (var col = board.Columns - 1; col >= 0; col--)
                    yield return new BoardPosition(lane, col, layer);
            else
                for (var col = 0; col < board.Columns; col++)
                    yield return new BoardPosition(lane, col, layer);
        }

        private static IEnumerable<BoardPosition> EnumerateSources(
            BoardTopology board, GravityDirection direction)
        {
            for (var layer = 0; layer < board.Layers; layer++)
            {
                if (direction == GravityDirection.Down)
                    for (var row = board.Rows - 1; row >= 0; row--)
                        for (var col = 0; col < board.Columns; col++)
                            yield return new BoardPosition(row, col, layer);
                else if (direction == GravityDirection.Up)
                    for (var row = 0; row < board.Rows; row++)
                        for (var col = 0; col < board.Columns; col++)
                            yield return new BoardPosition(row, col, layer);
                else if (direction == GravityDirection.Right)
                    for (var col = board.Columns - 1; col >= 0; col--)
                        for (var row = 0; row < board.Rows; row++)
                            yield return new BoardPosition(row, col, layer);
                else
                    for (var col = 0; col < board.Columns; col++)
                        for (var row = 0; row < board.Rows; row++)
                            yield return new BoardPosition(row, col, layer);
            }
        }

        private static BoardPosition Step(BoardPosition source, GravityDirection direction)
        {
            switch (direction)
            {
                case GravityDirection.Down:
                    return new BoardPosition(source.Row + 1, source.Column, source.Layer);
                case GravityDirection.Up:
                    if (source.Row == 0) return source;
                    return new BoardPosition(source.Row - 1, source.Column, source.Layer);
                case GravityDirection.Right:
                    return new BoardPosition(source.Row, source.Column + 1, source.Layer);
                case GravityDirection.Left:
                    if (source.Column == 0) return source;
                    return new BoardPosition(source.Row, source.Column - 1, source.Layer);
                default:
                    throw new ArgumentOutOfRangeException(nameof(direction));
            }
        }

        private static IEnumerable<BoardPosition> Diagonals(
            BoardPosition source, GravityDirection direction, DiagonalTieBreak tieBreak)
        {
            var values = new List<BoardPosition>(2);
            if (direction == GravityDirection.Down)
            {
                if (source.Column > 0)
                    values.Add(new BoardPosition(source.Row + 1, source.Column - 1, source.Layer));
                values.Add(new BoardPosition(source.Row + 1, source.Column + 1, source.Layer));
            }
            else if (direction == GravityDirection.Up)
            {
                if (source.Row == 0) return values;
                if (source.Column > 0)
                    values.Add(new BoardPosition(source.Row - 1, source.Column - 1, source.Layer));
                values.Add(new BoardPosition(source.Row - 1, source.Column + 1, source.Layer));
            }
            else if (direction == GravityDirection.Right)
            {
                if (source.Row > 0)
                    values.Add(new BoardPosition(source.Row - 1, source.Column + 1, source.Layer));
                values.Add(new BoardPosition(source.Row + 1, source.Column + 1, source.Layer));
            }
            else
            {
                if (source.Column == 0) return values;
                if (source.Row > 0)
                    values.Add(new BoardPosition(source.Row - 1, source.Column - 1, source.Layer));
                values.Add(new BoardPosition(source.Row + 1, source.Column - 1, source.Layer));
            }

            values = values.OrderBy(p => IsVertical(direction) ? p.Column : p.Row).ToList();
            if (tieBreak == DiagonalTieBreak.HigherCoordinate) values.Reverse();
            return values;
        }

        private static bool CanEnter(BoardTopology board,
            IDictionary<BoardPosition, string> occupants, BoardPosition p)
        {
            return board.Contains(p) && !board.IsHole(p) && !board.IsLocked(p) &&
                !occupants.ContainsKey(p);
        }

        private static bool IsVertical(GravityDirection direction)
        {
            return direction == GravityDirection.Down || direction == GravityDirection.Up;
        }

        private static void RequireDirection(GravityDirection direction)
        {
            if (!Enum.IsDefined(typeof(GravityDirection), direction))
                throw new ArgumentOutOfRangeException(nameof(direction));
        }
    }

    public readonly struct WeightedContent
    {
        public string ContentId { get; }
        public int Weight { get; }

        public WeightedContent(string contentId, int weight)
        {
            if (string.IsNullOrWhiteSpace(contentId))
                throw new ArgumentException("Content ID required.", nameof(contentId));
            if (weight < 1) throw new ArgumentOutOfRangeException(nameof(weight));
            ContentId = contentId;
            Weight = weight;
        }
    }

    public readonly struct SpawnedContent
    {
        public BoardPosition Position { get; }
        public string ContentId { get; }

        public SpawnedContent(BoardPosition position, string contentId)
        {
            Position = position;
            ContentId = contentId;
        }
    }

    public sealed class RefillPlan
    {
        public IReadOnlyDictionary<BoardPosition, string> Occupants { get; }
        public IReadOnlyList<SpawnedContent> Spawned { get; }
        public int Operations { get; }

        internal RefillPlan(IDictionary<BoardPosition, string> occupants,
            IEnumerable<SpawnedContent> spawned, int operations)
        {
            Occupants = new ReadOnlyDictionary<BoardPosition, string>(
                new Dictionary<BoardPosition, string>(occupants));
            Spawned = new ReadOnlyCollection<SpawnedContent>(spawned.ToList());
            Operations = operations;
        }
    }

    public static class WeightedRefillPlanner
    {
        public static RefillPlan Plan(BoardTopology topology,
            IReadOnlyDictionary<BoardPosition, string> settledOccupants,
            IReadOnlyList<WeightedContent> catalog, SeededRandom rng)
        {
            if (topology == null) throw new ArgumentNullException(nameof(topology));
            if (settledOccupants == null) throw new ArgumentNullException(nameof(settledOccupants));
            if (catalog == null || catalog.Count == 0)
                throw new ArgumentException("Weighted refill catalog required.", nameof(catalog));
            if (rng == null) throw new ArgumentNullException(nameof(rng));

            long totalLong = 0;
            foreach (var item in catalog)
                totalLong += item.Weight;
            if (totalLong < 1 || totalLong > Int32.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(catalog), "Total refill weight out of range.");
            var total = (int)totalLong;

            var result = new Dictionary<BoardPosition, string>();
            foreach (var pair in settledOccupants)
            {
                if (!topology.Contains(pair.Key) || topology.IsHole(pair.Key))
                    throw new ArgumentException("Settled occupant outside active topology.",
                        nameof(settledOccupants));
                if (result.ContainsKey(pair.Key))
                    throw new ArgumentException("Duplicate settled occupant.", nameof(settledOccupants));
                result.Add(pair.Key, pair.Value);
            }

            foreach (var p in topology.ActivePositions())
            {
                if (!topology.IsLocked(p)) continue;
                string original;
                if (topology.Occupants.TryGetValue(p, out original))
                {
                    string current;
                    if (!result.TryGetValue(p, out current) ||
                        !StringComparer.Ordinal.Equals(original, current))
                        throw new InvalidOperationException("Locked occupant changed before refill.");
                }
                else if (result.ContainsKey(p))
                {
                    throw new InvalidOperationException("Locked empty cell cannot be filled.");
                }
            }

            var spawned = new List<SpawnedContent>();
            var operations = 0;
            foreach (var p in topology.ActivePositions())
            {
                operations++;
                if (topology.IsLocked(p) || result.ContainsKey(p)) continue;
                var roll = rng.NextIndex(total);
                var cumulative = 0;
                string chosen = null;
                foreach (var item in catalog)
                {
                    cumulative += item.Weight;
                    if (roll < cumulative)
                    {
                        chosen = item.ContentId;
                        break;
                    }
                }
                if (chosen == null)
                    throw new InvalidOperationException("Weighted refill selection failed.");
                result.Add(p, chosen);
                spawned.Add(new SpawnedContent(p, chosen));
            }

            return new RefillPlan(result, spawned, operations);
        }
    }
}
