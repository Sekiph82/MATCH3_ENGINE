using System;
using System.Collections.Generic;
using System.Linq;
using Match3.Engine.Runtime;
using NUnit.Framework;

namespace Match3.Engine.Tests
{
    public sealed class GravityRefillContractTests
    {
        private static BoardPosition P(int row, int col, int layer = 0) =>
            new BoardPosition(row, col, layer);

        [Test]
        public void ClassicDownGravityCompactsWithinHoleAndLockSegmentsWithoutDuplicates()
        {
            var board = new BoardTopology(5, 2,
                holes: new[] { P(2, 0) },
                locked: new[] { P(3, 1) },
                occupants: new[]
                {
                    new BoardOccupant(P(0,0), "A"),
                    new BoardOccupant(P(3,0), "C"),
                    new BoardOccupant(P(0,1), "D"),
                    new BoardOccupant(P(1,1), "E"),
                    new BoardOccupant(P(3,1), "LOCK"),
                    new BoardOccupant(P(4,1), "F")
                });

            var plan = GravityPlanner.PlanCardinal(board, GravityDirection.Down);

            Assert.That(plan.Occupants.Count, Is.EqualTo(6));
            Assert.That(plan.Occupants[P(1,0)], Is.EqualTo("A"));
            Assert.That(plan.Occupants[P(4,0)], Is.EqualTo("C"));
            Assert.That(plan.Occupants[P(1,1)], Is.EqualTo("D"));
            Assert.That(plan.Occupants[P(2,1)], Is.EqualTo("E"));
            Assert.That(plan.Occupants[P(3,1)], Is.EqualTo("LOCK"));
            Assert.That(plan.Occupants[P(4,1)], Is.EqualTo("F"));
            Assert.That(plan.Occupants.ContainsKey(P(2,0)), Is.False);
            Assert.That(plan.Occupants.Keys.Distinct().Count(), Is.EqualTo(plan.Occupants.Count));
            Assert.That(plan.Moves.Any(x => x.Source.Equals(P(3,1))), Is.False);
            Assert.That(plan.Operations, Is.LessThanOrEqualTo(10));
            Assert.That(board.Occupants[P(0,0)], Is.EqualTo("A"));
        }

        [Test]
        public void DiagonalGravityUsesStableTieAndRejectsInvalidConfiguration()
        {
            var board = new BoardTopology(3, 3,
                locked: new[] { P(2,1) },
                occupants: new[] { new BoardOccupant(P(1,1), "X") });

            var lower = GravityPlanner.PlanDiagonal(board, GravityDirection.Down,
                DiagonalTieBreak.LowerCoordinate, 8);
            var higher = GravityPlanner.PlanDiagonal(board, GravityDirection.Down,
                DiagonalTieBreak.HigherCoordinate, 8);

            Assert.That(lower.Occupants.Single().Key, Is.EqualTo(P(2,0)));
            Assert.That(higher.Occupants.Single().Key, Is.EqualTo(P(2,2)));
            Assert.That(lower.Moves.Count, Is.EqualTo(1));
            Assert.That(higher.Moves.Count, Is.EqualTo(1));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                GravityPlanner.PlanDiagonal(board, (GravityDirection)99,
                    DiagonalTieBreak.LowerCoordinate));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                GravityPlanner.PlanDiagonal(board, GravityDirection.Down,
                    (DiagonalTieBreak)99));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                GravityPlanner.PlanDiagonal(board, GravityDirection.Down,
                    DiagonalTieBreak.LowerCoordinate, 0));
        }

        [Test]
        public void CardinalGravityVariantsMoveTowardConfiguredEdgeAndClassicDefaultIsDown()
        {
            var vertical = new BoardTopology(3, 1,
                occupants: new[] { new BoardOccupant(P(1,0), "A") });
            Assert.That(GravityPlanner.PlanCardinal(vertical).Occupants.Single().Key,
                Is.EqualTo(P(2,0)));
            Assert.That(GravityPlanner.PlanCardinal(vertical, GravityDirection.Up)
                .Occupants.Single().Key, Is.EqualTo(P(0,0)));

            var horizontal = new BoardTopology(1, 3,
                occupants: new[] { new BoardOccupant(P(0,1), "B") });
            Assert.That(GravityPlanner.PlanCardinal(horizontal, GravityDirection.Left)
                .Occupants.Single().Key, Is.EqualTo(P(0,0)));
            Assert.That(GravityPlanner.PlanCardinal(horizontal, GravityDirection.Right)
                .Occupants.Single().Key, Is.EqualTo(P(0,2)));

            Assert.Throws<ArgumentOutOfRangeException>(() =>
                GravityPlanner.PlanCardinal(vertical, (GravityDirection)77));
        }

        [Test]
        public void WeightedRefillIsSeededDeterministicPreservesConstraintsAndFillsEligibleCells()
        {
            var board = new BoardTopology(2, 2,
                holes: new[] { P(0,1) },
                locked: new[] { P(1,1) },
                occupants: new[]
                {
                    new BoardOccupant(P(1,0), "KEEP"),
                    new BoardOccupant(P(1,1), "LOCK")
                });
            var settled = new Dictionary<BoardPosition, string>(board.Occupants);
            var catalog = new[]
            {
                new WeightedContent("A", 1),
                new WeightedContent("B", 3)
            };

            var first = WeightedRefillPlanner.Plan(board, settled, catalog,
                new SeededRandom(20261014u));
            var second = WeightedRefillPlanner.Plan(board, settled, catalog,
                new SeededRandom(20261014u));

            Assert.That(first.Occupants.Count, Is.EqualTo(3));
            Assert.That(first.Occupants[P(1,0)], Is.EqualTo("KEEP"));
            Assert.That(first.Occupants[P(1,1)], Is.EqualTo("LOCK"));
            Assert.That(first.Occupants.ContainsKey(P(0,1)), Is.False);
            Assert.That(first.Occupants[P(0,0)], Is.EqualTo(second.Occupants[P(0,0)]));
            Assert.That(first.Spawned.Count, Is.EqualTo(1));
            Assert.That(first.Spawned[0].Position, Is.EqualTo(P(0,0)));
            Assert.Throws<NotSupportedException>(() =>
                ((IDictionary<BoardPosition,string>)first.Occupants).Add(P(0,1), "X"));
        }

        [Test]
        public void WeightedRefillRejectsMalformedCatalogAndLockedMutation()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new WeightedContent("A", 0));
            Assert.Throws<ArgumentException>(() => new WeightedContent("", 1));

            var board = new BoardTopology(1, 1,
                locked: new[] { P(0,0) },
                occupants: new[] { new BoardOccupant(P(0,0), "LOCK") });
            Assert.Throws<InvalidOperationException>(() =>
                WeightedRefillPlanner.Plan(board,
                    new Dictionary<BoardPosition,string> { { P(0,0), "CHANGED" } },
                    new[] { new WeightedContent("A", 1) }, new SeededRandom(1u)));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                WeightedRefillPlanner.Plan(new BoardTopology(1,1),
                    new Dictionary<BoardPosition,string>(),
                    new[]
                    {
                        new WeightedContent("A", Int32.MaxValue),
                        new WeightedContent("B", 1)
                    }, new SeededRandom(1u)));
        }
    }
}
