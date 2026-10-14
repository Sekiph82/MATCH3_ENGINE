using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Match3.Engine.Runtime
{
    // M02 pure topology is an input/serialization shape, not a substitute
    // for the pinned SDK's match detection, swap, fill or cascade solver.
    public readonly struct BoardPosition : IEquatable<BoardPosition>
    {
        public int Row { get; }
        public int Column { get; }
        public int Layer { get; }
        public BoardPosition(int row, int column, int layer = 0)
        {
            if (row < 0 || column < 0 || layer < 0)
                throw new ArgumentOutOfRangeException(nameof(row), "Coordinates must be nonnegative.");
            Row = row;
            Column = column;
            Layer = layer;
        }
        public bool Equals(BoardPosition other) =>
            Row == other.Row && Column == other.Column && Layer == other.Layer;
        public override bool Equals(object value) => value is BoardPosition other && Equals(other);
        public override int GetHashCode()
        {
            unchecked { return ((Row * 397) ^ Column) * 397 ^ Layer; }
        }
        public override string ToString() => Layer + ":" + Row + ":" + Column;
    }

    public readonly struct BoardOccupant
    {
        public BoardPosition Position { get; }
        public string ContentId { get; }
        public BoardOccupant(BoardPosition position, string contentId)
        {
            if (string.IsNullOrWhiteSpace(contentId))
                throw new ArgumentException("Occupant content ID required.", nameof(contentId));
            Position = position;
            ContentId = contentId;
        }
    }

    public sealed class BoardTopology
    {
        private readonly HashSet<BoardPosition> _holes;
        private readonly HashSet<BoardPosition> _locked;
        private readonly ReadOnlyDictionary<BoardPosition, string> _occupants;

        public int Rows { get; }
        public int Columns { get; }
        public int Layers { get; }
        public IReadOnlyDictionary<BoardPosition, string> Occupants => _occupants;

        public BoardTopology(int rows, int columns, int layers = 1,
            IEnumerable<BoardPosition> holes = null,
            IEnumerable<BoardPosition> locked = null,
            IEnumerable<BoardOccupant> occupants = null)
        {
            if (rows < 1 || columns < 1 || layers < 1 || rows > 1024 ||
                columns > 1024 || layers > 16)
                throw new ArgumentOutOfRangeException(nameof(rows), "Invalid board extents.");
            Rows = rows;
            Columns = columns;
            Layers = layers;
            _holes = ReadPositions(holes);
            _locked = ReadPositions(locked);
            if (_locked.Overlaps(_holes))
                throw new ArgumentException("A hole cannot also be locked.");
            var dictionary = new Dictionary<BoardPosition, string>();
            if (occupants != null)
            {
                foreach (var occupant in occupants)
                {
                    RequireInside(occupant.Position);
                    if (_holes.Contains(occupant.Position))
                        throw new ArgumentException("A hole cannot contain a tile.");
                    if (!dictionary.TryAdd(occupant.Position, occupant.ContentId))
                        throw new ArgumentException("Duplicate occupant on the same layer/cell.");
                }
            }
            _occupants = new ReadOnlyDictionary<BoardPosition, string>(dictionary);
        }

        private HashSet<BoardPosition> ReadPositions(IEnumerable<BoardPosition> positions)
        {
            var result = new HashSet<BoardPosition>();
            if (positions == null) return result;
            foreach (var p in positions)
            {
                RequireInside(p);
                if (!result.Add(p))
                    throw new ArgumentException("Duplicate board cell declaration.");
            }
            return result;
        }

        public bool Contains(BoardPosition p) =>
            p.Row >= 0 && p.Row < Rows &&
            p.Column >= 0 && p.Column < Columns &&
            p.Layer >= 0 && p.Layer < Layers;

        private void RequireInside(BoardPosition p)
        {
            if (!Contains(p)) throw new ArgumentOutOfRangeException(nameof(p), "Position outside topology.");
        }

        public bool IsHole(BoardPosition p) => !Contains(p) || _holes.Contains(p);
        public bool IsLocked(BoardPosition p) => Contains(p) && _locked.Contains(p);

        public bool HasOccupant(BoardPosition p) => _occupants.ContainsKey(p);

        // This only checks topological eligibility. The actual match and
        // invalid rollback MUST remain with LibraStack's SDK board solver.
        public bool IsSwapCandidate(BoardPosition first, BoardPosition second)
        {
            if (!Contains(first) || !Contains(second) || first.Layer != second.Layer ||
                IsHole(first) || IsHole(second) || IsLocked(first) || IsLocked(second) ||
                !HasOccupant(first) || !HasOccupant(second))
                return false;
            return Math.Abs(first.Row - second.Row) +
                   Math.Abs(first.Column - second.Column) == 1;
        }

        public IReadOnlyList<BoardPosition> ActivePositions()
        {
            var positions = new List<BoardPosition>();
            for (var layer = 0; layer < Layers; layer++)
                for (var row = 0; row < Rows; row++)
                    for (var col = 0; col < Columns; col++)
                    {
                        var cell = new BoardPosition(row, col, layer);
                        if (!_holes.Contains(cell)) positions.Add(cell);
                    }
            return positions.AsReadOnly();
        }
    }

    // Explicit, platform-independent xorshift32. Seed 0 is rejected so
    // a zero-state stream cannot quietly produce a fake constant board.
    public sealed class SeededRandom
    {
        private uint _state;
        public uint State => _state;

        public SeededRandom(uint seed)
        {
            if (seed == 0) throw new ArgumentOutOfRangeException(nameof(seed));
            _state = seed;
        }

        public uint NextUInt32()
        {
            var x = _state;
            x ^= x << 13;
            x ^= x >> 17;
            x ^= x << 5;
            _state = x;
            return x;
        }

        public int NextIndex(int exclusiveMax)
        {
            if (exclusiveMax < 1) throw new ArgumentOutOfRangeException(nameof(exclusiveMax));
            var bound = (uint)exclusiveMax;
            var threshold = unchecked((0u - bound) % bound);
            uint number;
            do { number = NextUInt32(); } while (number < threshold);
            return (int)(number % bound);
        }
    }
}
