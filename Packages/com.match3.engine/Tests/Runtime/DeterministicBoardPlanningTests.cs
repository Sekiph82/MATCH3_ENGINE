using System;
using System.Collections.Generic;
using System.Linq;
using Match3.Engine.Runtime;
using NUnit.Framework;

namespace Match3.Engine.Tests
{
    public sealed class DeterministicBoardPlanningTests
    {
        private static BoardPosition P(int row, int col) => new BoardPosition(row, col);

        private static BoardTopology FourDistinct()
        {
            return new BoardTopology(2, 2, occupants: new[]
            {
                new BoardOccupant(P(0, 0), "A"), new BoardOccupant(P(0, 1), "B"),
                new BoardOccupant(P(1, 0), "C"), new BoardOccupant(P(1, 1), "D")
            });
        }

        [Test]
        public void SeededShuffleProducesIdenticalAcceptedProposalWithoutMutatingOrigin()
        {
            var original = FourDistinct();
            var a = DeterministicBoardPlanning.TryShuffle(original, new SeededRandom(23),
                candidate => candidate[P(0, 0)] != "A", 64);
            var b = DeterministicBoardPlanning.TryShuffle(original, new SeededRandom(23),
                candidate => candidate[P(0, 0)] != "A", 64);
            Assert.That(a.Succeeded, Is.True);
            Assert.That(b.Succeeded, Is.True);
            Assert.That(a.Attempts, Is.EqualTo(b.Attempts));
            foreach (var position in original.ActivePositions())
                Assert.That(a.Occupants[position], Is.EqualTo(b.Occupants[position]));
            CollectionAssert.AreEquivalent(original.Occupants.Values.ToArray(),
                a.Occupants.Values.ToArray());
            Assert.That(original.Occupants[P(0, 0)], Is.EqualTo("A"));
            Assert.That(a.Occupants[P(0, 0)], Is.Not.EqualTo("A"));
            Assert.Throws<NotSupportedException>(() =>
                ((IDictionary<BoardPosition, string>)a.Occupants).Clear());
        }

        [Test]
        public void ShuffleNeverTouchesLockedOrHoledCells()
        {
            var board = new BoardTopology(2, 3,
                holes: new[] { P(1, 2) }, locked: new[] { P(0, 0) },
                occupants: new[] {
                    new BoardOccupant(P(0, 0), "LOCK"),
                    new BoardOccupant(P(0, 1), "A"),
                    new BoardOccupant(P(0, 2), "B"),
                    new BoardOccupant(P(1, 0), "C"),
                    new BoardOccupant(P(1, 1), "D")
                });
            var result = DeterministicBoardPlanning.TryShuffle(board, new SeededRandom(42),
                proposal => proposal[P(0, 1)] != "A", 64);
            Assert.That(result.Succeeded, Is.True);
            Assert.That(result.Occupants[P(0, 0)], Is.EqualTo("LOCK"));
            Assert.That(result.Occupants.ContainsKey(P(1, 2)), Is.False);
            Assert.That(result.Occupants.Count, Is.EqualTo(5));
        }

        [Test]
        public void UnsolvableOrUniformShuffleFailsClosedWithNoEconomySideEffect()
        {
            var board = new BoardTopology(2, 2, occupants: new[] {
                new BoardOccupant(P(0, 0), "A"),
                new BoardOccupant(P(0, 1), "A"),
                new BoardOccupant(P(1, 0), "A"),
                new BoardOccupant(P(1, 1), "A")
            });
            var callbacks = 0;
            var result = DeterministicBoardPlanning.TryShuffle(board, new SeededRandom(19),
                proposal => { callbacks++; return true; }, 7);
            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.Status, Is.EqualTo(BoardPlanStatus.NoValidArrangement));
            Assert.That(result.Attempts, Is.EqualTo(7));
            Assert.That(callbacks, Is.EqualTo(0)); // No actual changed arrangement.
            Assert.That(result.Occupants.Values.All(s => s == "A"), Is.True);

            var distinct = FourDistinct();
            var rejected = DeterministicBoardPlanning.TryShuffle(distinct,
                new SeededRandom(19), proposal => false, 3);
            Assert.That(rejected.Status, Is.EqualTo(BoardPlanStatus.NoValidArrangement));
            Assert.That(rejected.Attempts, Is.EqualTo(3));
            foreach (var p in distinct.ActivePositions())
                Assert.That(rejected.Occupants[p], Is.EqualTo(distinct.Occupants[p]));
        }

        [Test]
        public void InitialSpawnKeepsFixedTemplateAndFillsOnlyUnlockedActiveCells()
        {
            var board = new BoardTopology(2, 3,
                holes: new[] { P(1, 2) }, locked: new[] { P(0, 0) },
                occupants: new[] { new BoardOccupant(P(0, 0), "FIXED") });
            var result = DeterministicBoardPlanning.TrySpawn(board,
                new[] { "B" }, new SeededRandom(20261014),
                proposed => proposed.Count == 5 && proposed[P(0, 0)] == "FIXED", 5);
            Assert.That(result.Succeeded, Is.True);
            Assert.That(result.Attempts, Is.EqualTo(1));
            Assert.That(result.Occupants[P(0, 0)], Is.EqualTo("FIXED"));
            Assert.That(result.Occupants.ContainsKey(P(1, 2)), Is.False);
            Assert.That(result.Occupants[P(1, 1)], Is.EqualTo("B"));
            Assert.That(board.Occupants.Count, Is.EqualTo(1));
        }

        [Test]
        public void SpawnInvalidTemplateOrRejectedSdkCallbackLeavesOriginalUnchanged()
        {
            var board = new BoardTopology(2, 2,
                occupants: new[] { new BoardOccupant(P(0, 0), "X") });
            var result = DeterministicBoardPlanning.TrySpawn(board,
                new[] { "A", "B" }, new SeededRandom(1), candidate => false, 4);
            Assert.That(result.Status, Is.EqualTo(BoardPlanStatus.NoValidArrangement));
            Assert.That(result.Attempts, Is.EqualTo(4));
            Assert.That(result.Occupants.Count, Is.EqualTo(1));
            Assert.That(result.Occupants[P(0, 0)], Is.EqualTo("X"));
            Assert.Throws<ArgumentException>(() => DeterministicBoardPlanning.TrySpawn(
                board, new string[0], new SeededRandom(2), x => true));
            Assert.Throws<ArgumentException>(() => DeterministicBoardPlanning.TrySpawn(
                board, new[] { "" }, new SeededRandom(2), x => true));
        }

        [Test]
        public void PlanningBudgetsOrMissingSdkValidatorAlwaysFailClosed()
        {
            Assert.Throws<ArgumentNullException>(() =>
                DeterministicBoardPlanning.TryShuffle(FourDistinct(), new SeededRandom(1),
                    null));
            Assert.Throws<ArgumentNullException>(() =>
                DeterministicBoardPlanning.TrySpawn(FourDistinct(), new[] { "A" },
                    new SeededRandom(1), null));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                DeterministicBoardPlanning.TryShuffle(FourDistinct(), new SeededRandom(1),
                    x => true, maximumAttempts: 257));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                DeterministicBoardPlanning.TrySpawn(FourDistinct(), new[] { "A" },
                    new SeededRandom(1), x => true, maximumAttempts: 0));
            Assert.Throws<InvalidOperationException>(() =>
                DeterministicBoardPlanning.TryShuffle(FourDistinct(), new SeededRandom(1),
                    x => { throw new InvalidOperationException("SDK copy unavailable"); }, 10));
        }

        [Test]
        public void SpawnWithIdenticalSeedAndPaletteProducesIdenticalLayout()
        {
            var a = new BoardTopology(3, 3);
            var x = DeterministicBoardPlanning.TrySpawn(a,
                new[] { "A", "B", "C" }, new SeededRandom(55u), p => true);
            var y = DeterministicBoardPlanning.TrySpawn(a,
                new[] { "A", "B", "C" }, new SeededRandom(55u), p => true);
            Assert.That(x.Succeeded, Is.True);
            Assert.That(y.Succeeded, Is.True);
            foreach (var p in a.ActivePositions())
                Assert.That(x.Occupants[p], Is.EqualTo(y.Occupants[p]));
        }
    }
}
