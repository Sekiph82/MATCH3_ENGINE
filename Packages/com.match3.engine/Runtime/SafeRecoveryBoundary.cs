using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;

namespace Match3.Engine.Runtime
{
    public enum RecoveryStatus
    {
        NoRecoveryNeeded = 0,
        ShuffleProposed = 1,
        NoSafeRecovery = 2
    }

    public sealed class RecoveryDecision
    {
        public RecoveryStatus Status { get; }
        public int Attempts { get; }
        public string OriginalBoardHash { get; }
        public string ProposedBoardHash { get; }
        public IReadOnlyDictionary<BoardPosition, string> ProposedOccupants { get; }
        public bool IsProposal => Status == RecoveryStatus.ShuffleProposed;

        internal RecoveryDecision(RecoveryStatus status, int attempts,
            string originalHash, string proposalHash,
            IReadOnlyDictionary<BoardPosition, string> occupants)
        {
            Status = status;
            Attempts = attempts;
            OriginalBoardHash = originalHash;
            ProposedBoardHash = proposalHash;
            ProposedOccupants = new ReadOnlyDictionary<BoardPosition, string>(
                new Dictionary<BoardPosition, string>(occupants));
        }
    }

    // A recovery PROPOSAL, never a state commit. No wallet, booster, move,
    // win/lose or save dependency exists here. Only the trusted game session
    // can approve and apply a result after checking its source-board hash.
    public static class SafeRecoveryBoundary
    {
        public static RecoveryDecision Propose(BoardTopology currentBoard,
            bool sdkAlreadyHasLegalMove, SeededRandom rng,
            Func<IReadOnlyDictionary<BoardPosition, string>, bool> sdkIsolatedBoardValid,
            int maximumAttempts = 32)
        {
            if (currentBoard == null) throw new ArgumentNullException(nameof(currentBoard));
            if (rng == null) throw new ArgumentNullException(nameof(rng));
            if (sdkIsolatedBoardValid == null)
                throw new ArgumentNullException(nameof(sdkIsolatedBoardValid));
            if (maximumAttempts < 1 || maximumAttempts > 256)
                throw new ArgumentOutOfRangeException(nameof(maximumAttempts));

            var beforeHash = HashBoard(currentBoard.Occupants);
            if (sdkAlreadyHasLegalMove)
                return new RecoveryDecision(RecoveryStatus.NoRecoveryNeeded, 0,
                    beforeHash, beforeHash, currentBoard.Occupants);

            var plan = DeterministicBoardPlanning.TryShuffle(
                currentBoard, rng, sdkIsolatedBoardValid, maximumAttempts);
            if (!plan.Succeeded)
                return new RecoveryDecision(RecoveryStatus.NoSafeRecovery, plan.Attempts,
                    beforeHash, beforeHash, currentBoard.Occupants);

            var proposedHash = HashBoard(plan.Occupants);
            if (proposedHash == beforeHash)
                throw new InvalidOperationException("A shuffle cannot submit an identical board.");
            return new RecoveryDecision(RecoveryStatus.ShuffleProposed, plan.Attempts,
                beforeHash, proposedHash, plan.Occupants);
        }

        public static string HashBoard(IReadOnlyDictionary<BoardPosition, string> cells)
        {
            if (cells == null) throw new ArgumentNullException(nameof(cells));
            var b = new StringBuilder();
            foreach (var item in cells.OrderBy(x => x.Key.Layer)
                     .ThenBy(x => x.Key.Row).ThenBy(x => x.Key.Column))
            {
                if (String.IsNullOrWhiteSpace(item.Value) ||
                    item.Value.IndexOf('|') >= 0 || item.Value.IndexOf('\n') >= 0)
                    throw new ArgumentException("Noncanonical board content ID.");
                b.Append(item.Key.Layer).Append(':')
                 .Append(item.Key.Row).Append(':').Append(item.Key.Column)
                 .Append('|').Append(item.Value.Length).Append(':')
                 .Append(item.Value).Append('\n');
            }
            return ReplayCodec.BoardHash(b.ToString());
        }
    }
}
