using System;
using System.Collections.Generic;
using System.Linq;
using Match3.Engine.Runtime;
using NUnit.Framework;

namespace Match3.Engine.Tests
{
    public sealed class SafeRecoveryBoundaryTests
    {
        private static BoardPosition P(int r, int c) => new BoardPosition(r, c);
        private static BoardTopology Board()
        {
            return new BoardTopology(2, 3,
                locked: new[] { P(0, 0) }, holes: new[] { P(1, 2) },
                occupants: new[] {
                    new BoardOccupant(P(0, 0), "LOCKED"),
                    new BoardOccupant(P(0, 1), "A"),
                    new BoardOccupant(P(0, 2), "B"),
                    new BoardOccupant(P(1, 0), "C"),
                    new BoardOccupant(P(1, 1), "D")
                });
        }

        [Test]
        public void GenuineNoMoveSdkStateReturnsBoundedReproducibleRecoveryProposal()
        {
            var board = Board();
            var first = SafeRecoveryBoundary.Propose(board, false, new SeededRandom(42),
                proposal => proposal[P(0, 1)] != "A", 64);
            var second = SafeRecoveryBoundary.Propose(board, false, new SeededRandom(42),
                proposal => proposal[P(0, 1)] != "A", 64);
            Assert.That(first.Status, Is.EqualTo(RecoveryStatus.ShuffleProposed));
            Assert.That(first.IsProposal, Is.True);
            Assert.That(first.Attempts, Is.EqualTo(second.Attempts));
            Assert.That(first.ProposedBoardHash, Is.EqualTo(second.ProposedBoardHash));
            Assert.That(first.ProposedBoardHash, Is.Not.EqualTo(first.OriginalBoardHash));
            Assert.That(first.OriginalBoardHash,
                Is.EqualTo(SafeRecoveryBoundary.HashBoard(board.Occupants)));
            Assert.That(board.Occupants[P(0, 1)], Is.EqualTo("A"));
            Assert.That(first.ProposedOccupants[P(0, 0)], Is.EqualTo("LOCKED"));
            Assert.That(first.ProposedOccupants.ContainsKey(P(1, 2)), Is.False);
            Assert.Throws<NotSupportedException>(() =>
                ((IDictionary<BoardPosition, string>)first.ProposedOccupants).Clear());
        }

        [Test]
        public void ValidSdkMoveAvoidsUnnecessaryShuffleAndConsumesNoRng()
        {
            var board = Board();
            var rng = new SeededRandom(1);
            var before = rng.State;
            var invoked = 0;
            var decision = SafeRecoveryBoundary.Propose(board, true, rng,
                x => { invoked++; return true; });
            Assert.That(decision.Status, Is.EqualTo(RecoveryStatus.NoRecoveryNeeded));
            Assert.That(decision.Attempts, Is.EqualTo(0));
            Assert.That(decision.OriginalBoardHash, Is.EqualTo(decision.ProposedBoardHash));
            Assert.That(decision.IsProposal, Is.False);
            Assert.That(rng.State, Is.EqualTo(before));
            Assert.That(invoked, Is.Zero);
        }

        [Test]
        public void ImpossibleBoardFailsExplicitlyWithoutChangingOriginOrFabricatingWin()
        {
            var board = Board();
            var original = SafeRecoveryBoundary.HashBoard(board.Occupants);
            var decision = SafeRecoveryBoundary.Propose(board, false, new SeededRandom(10),
                x => false, 4);
            Assert.That(decision.Status, Is.EqualTo(RecoveryStatus.NoSafeRecovery));
            Assert.That(decision.Attempts, Is.EqualTo(4));
            Assert.That(decision.IsProposal, Is.False);
            Assert.That(decision.OriginalBoardHash, Is.EqualTo(original));
            Assert.That(decision.ProposedBoardHash, Is.EqualTo(original));
            Assert.That(SafeRecoveryBoundary.HashBoard(board.Occupants), Is.EqualTo(original));
        }

        [Test]
        public void CanonicalBoardHashDoesNotDependOnDictionaryInsertionOrder()
        {
            var board = Board();
            var reversed = new Dictionary<BoardPosition, string>();
            foreach (var x in board.Occupants.Reverse()) reversed.Add(x.Key, x.Value);
            Assert.That(SafeRecoveryBoundary.HashBoard(reversed),
                Is.EqualTo(SafeRecoveryBoundary.HashBoard(board.Occupants)));
            reversed[P(0, 1)] = "ALTERED";
            Assert.That(SafeRecoveryBoundary.HashBoard(reversed),
                Is.Not.EqualTo(SafeRecoveryBoundary.HashBoard(board.Occupants)));
        }

        [Test]
        public void MissingSdkOrIllegalBudgetsRejectAndCannotGrantRewards()
        {
            Assert.Throws<ArgumentNullException>(() => SafeRecoveryBoundary.Propose(
                Board(), false, new SeededRandom(1), null));
            Assert.Throws<ArgumentOutOfRangeException>(() => SafeRecoveryBoundary.Propose(
                Board(), false, new SeededRandom(1), x => true, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => SafeRecoveryBoundary.Propose(
                Board(), false, new SeededRandom(1), x => true, 257));
            Assert.Throws<ArgumentException>(() => SafeRecoveryBoundary.HashBoard(
                new Dictionary<BoardPosition, string> { { P(0, 0), "A|B" } }));
            // The exported result is a proposal only, not a commit, wallet or
            // reward API. No valid or invalid call can spend a paid booster.
            Assert.That(typeof(RecoveryDecision).GetProperties()
                .All(p => !p.CanWrite), Is.True);
        }
    }
}
