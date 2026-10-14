using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Match3.Engine.Runtime
{
    public enum BoardPlanStatus { Accepted = 0, NoValidArrangement = 1 }

    public sealed class BoardPlan
    {
        public BoardPlanStatus Status { get; }
        public int Attempts { get; }
        public IReadOnlyDictionary<BoardPosition, string> Occupants { get; }
        public bool Succeeded => Status == BoardPlanStatus.Accepted;

        internal BoardPlan(BoardPlanStatus status, int attempts,
            IDictionary<BoardPosition, string> occupants)
        {
            Status = status;
            Attempts = attempts;
            Occupants = new ReadOnlyDictionary<BoardPosition, string>(
                new Dictionary<BoardPosition, string>(occupants));
        }
    }

    // Plans only candidate layouts. The injected validator must evaluate
    // a SEPARATE, nonmutating SDK test board. The pinned LibraStack SDK alone
    // decides whether an initial spawn/shuffle is legal or has valid moves.
    public static class DeterministicBoardPlanning
    {
        public static BoardPlan TryShuffle(BoardTopology existing,
            SeededRandom rng,
            Func<IReadOnlyDictionary<BoardPosition, string>, bool> sdkSnapshotValidator,
            int maximumAttempts = 32)
        {
            Validate(existing, rng, sdkSnapshotValidator, maximumAttempts);
            var original = new Dictionary<BoardPosition, string>(existing.Occupants);
            var movable = existing.ActivePositions()
                .Where(p => existing.HasOccupant(p) && !existing.IsLocked(p))
                .ToArray();
            if (movable.Length < 2)
                return new BoardPlan(BoardPlanStatus.NoValidArrangement, 0, original);
            var colors = movable.Select(p => original[p]).ToArray();
            var positionCount = movable.Length;
            for (var attempt = 1; attempt <= maximumAttempts; attempt++)
            {
                var shuffled = (string[])colors.Clone();
                FisherYates(shuffled, rng);
                if (Enumerable.Range(0, positionCount).All(i =>
                    StringComparer.Ordinal.Equals(shuffled[i], colors[i])))
                    continue;
                var proposal = new Dictionary<BoardPosition, string>(original);
                for (var i = 0; i < positionCount; i++)
                    proposal[movable[i]] = shuffled[i];
                var readonlyProposal = new ReadOnlyDictionary<BoardPosition, string>(proposal);
                if (!sdkSnapshotValidator(readonlyProposal)) continue;
                return new BoardPlan(BoardPlanStatus.Accepted, attempt, proposal);
            }
            return new BoardPlan(BoardPlanStatus.NoValidArrangement, maximumAttempts, original);
        }

        public static BoardPlan TrySpawn(BoardTopology template,
            IReadOnlyList<string> palette, SeededRandom rng,
            Func<IReadOnlyDictionary<BoardPosition, string>, bool> sdkSnapshotValidator,
            int maximumAttempts = 32)
        {
            Validate(template, rng, sdkSnapshotValidator, maximumAttempts);
            if (palette == null || palette.Count == 0 ||
                palette.Any(s => String.IsNullOrWhiteSpace(s)))
                throw new ArgumentException("Spawn palette must contain valid content identifiers.", nameof(palette));
            var original = new Dictionary<BoardPosition, string>(template.Occupants);
            var freeCells = template.ActivePositions()
                .Where(p => !template.HasOccupant(p) && !template.IsLocked(p)).ToArray();
            // A locked empty cell is an explicit permanent board constraint.
            // It must never be silently filled or treated as movable.
            if (freeCells.Length == 0)
                return new BoardPlan(BoardPlanStatus.NoValidArrangement, 0, original);
            for (var attempt = 1; attempt <= maximumAttempts; attempt++)
            {
                var proposal = new Dictionary<BoardPosition, string>(original);
                foreach (var position in freeCells)
                    proposal[position] = palette[rng.NextIndex(palette.Count)];
                if (!sdkSnapshotValidator(new ReadOnlyDictionary<BoardPosition, string>(proposal)))
                    continue;
                return new BoardPlan(BoardPlanStatus.Accepted, attempt, proposal);
            }
            return new BoardPlan(BoardPlanStatus.NoValidArrangement, maximumAttempts, original);
        }

        private static void Validate(BoardTopology board, SeededRandom rng,
            Func<IReadOnlyDictionary<BoardPosition, string>, bool> sdkValidator, int attempts)
        {
            if (board == null) throw new ArgumentNullException(nameof(board));
            if (rng == null) throw new ArgumentNullException(nameof(rng));
            if (sdkValidator == null) throw new ArgumentNullException(nameof(sdkValidator));
            if (attempts < 1 || attempts > 256)
                throw new ArgumentOutOfRangeException(nameof(attempts));
            if ((long)board.Rows * board.Columns * board.Layers > 65536)
                throw new ArgumentOutOfRangeException(nameof(board), "Planning exceeds bounded board size.");
        }

        private static void FisherYates(string[] values, SeededRandom rng)
        {
            for (var i = values.Length - 1; i > 0; i--)
            {
                var j = rng.NextIndex(i + 1);
                var temp = values[i];
                values[i] = values[j];
                values[j] = temp;
            }
        }
    }
}
