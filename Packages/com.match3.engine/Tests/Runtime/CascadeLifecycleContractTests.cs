using System;
using System.Collections.Generic;
using System.Linq;
using Match3.Engine.Runtime;
using NUnit.Framework;

namespace Match3.Engine.Tests
{
    public sealed class CascadeLifecycleContractTests
    {
        private static BoardPosition P(int r, int c, int layer = 0) =>
            new BoardPosition(r, c, layer);

        [Test]
        public void CascadePlannerUsesAuthoritativeCallbackUntilStableAndCapsWaves()
        {
            var calls = new List<int>();
            var timeline = CascadeCoordinator.Plan(wave =>
            {
                calls.Add(wave);
                return wave < 2;
            }, maximumResolvedWaves: 4);

            Assert.That(calls, Is.EqualTo(new[] { 0, 1, 2 }));
            Assert.That(timeline.ResolvedWaves, Is.EqualTo(2));
            Assert.That(timeline.Events.Select(x => x.Phase).ToArray(), Is.EqualTo(new[]
            {
                CascadePhase.Detect, CascadePhase.Damage, CascadePhase.Settle, CascadePhase.Refill,
                CascadePhase.Detect, CascadePhase.Damage, CascadePhase.Settle, CascadePhase.Refill,
                CascadePhase.Detect, CascadePhase.Stable
            }));
            Assert.That(timeline.Events.Select(x => x.Sequence).ToArray(),
                Is.EqualTo(Enumerable.Range(1, timeline.Events.Count).ToArray()));

            Assert.Throws<InvalidOperationException>(() =>
                CascadeCoordinator.Plan(wave => true, maximumResolvedWaves: 2));
            Assert.Throws<ArgumentNullException>(() =>
                CascadeCoordinator.Plan(null));
            Assert.Throws<InvalidOperationException>(() =>
                CascadeCoordinator.Plan(wave =>
                {
                    throw new InvalidOperationException("authoritative SDK adapter failed");
                }));
        }

        [Test]
        public void PresentationSpeedAndSkipNeverChangeLogicalTimelineHash()
        {
            var timeline = CascadeCoordinator.Plan(wave => wave == 0, 3);
            var normal = TimelinePresentation.Project(timeline, 1.0, false);
            var fast = TimelinePresentation.Project(timeline, 2.0, false);
            var skipped = TimelinePresentation.Project(timeline, 1.0, true);

            Assert.That(normal.LogicalHash, Is.EqualTo(timeline.LogicalHash));
            Assert.That(fast.LogicalHash, Is.EqualTo(timeline.LogicalHash));
            Assert.That(skipped.LogicalHash, Is.EqualTo(timeline.LogicalHash));
            Assert.That(normal.Frames.Select(x => x.LogicalEvent.Sequence),
                Is.EqualTo(fast.Frames.Select(x => x.LogicalEvent.Sequence)));
            Assert.That(skipped.Frames.All(x => x.DelaySeconds == 0.0), Is.True);
            Assert.That(normal.Frames.Any(x => x.DelaySeconds > 0.0), Is.True);
            Assert.That(fast.Frames.Where(x => x.DelaySeconds > 0.0)
                .Zip(normal.Frames.Where(x => x.DelaySeconds > 0.0),
                    (a,b) => a.DelaySeconds < b.DelaySeconds).All(x => x), Is.True);
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                TimelinePresentation.Project(timeline, 0.0, false));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                TimelinePresentation.Project(timeline, Double.NaN, false));
        }

        [Test]
        public void PauseInvalidatesStaleCompletionAndResumeCannotDoubleCommit()
        {
            var gate = new SettleCommitGate();
            var stale = gate.Begin();
            Assert.That(gate.PendingCount, Is.EqualTo(1));
            Assert.That(gate.Pause(), Is.True);
            Assert.That(gate.Pause(), Is.False);
            Assert.That(gate.PendingCount, Is.EqualTo(0));
            Assert.That(gate.Resume(), Is.True);
            Assert.That(gate.Resume(), Is.False);
            Assert.That(gate.TryCommit(stale), Is.False);

            var fresh = gate.Begin();
            Assert.That(fresh.Generation, Is.EqualTo(gate.Generation));
            Assert.That(gate.TryCommit(fresh), Is.True);
            Assert.That(gate.TryCommit(fresh), Is.False);
            Assert.That(gate.PendingCount, Is.EqualTo(0));

            Assert.That(gate.Pause(), Is.True);
            Assert.Throws<InvalidOperationException>(() => gate.Begin());
            Assert.That(gate.Resume(), Is.True);
            var cancelled = gate.Begin();
            gate.CancelPending();
            Assert.That(gate.TryCommit(cancelled), Is.False);
        }

        [Test]
        public void GravityWorkAndManagedMemoryStayBoundedAtNineTwelveAndTwentyBoards()
        {
            foreach (var size in new[] { 9, 12, 20 })
            {
                var occupants = new List<BoardOccupant>();
                for (var row = 0; row < size; row += 2)
                    for (var col = 0; col < size; col++)
                        occupants.Add(new BoardOccupant(P(row, col), "A"));

                var board = new BoardTopology(size, size, occupants: occupants);
                var result = GravityBenchmark.MeasureCardinal(board, GravityDirection.Down);
                Assert.That(result.CellCount, Is.EqualTo(size * size));
                Assert.That(result.Operations, Is.EqualTo(size * size));
                Assert.That(result.Operations, Is.LessThanOrEqualTo(result.CellCount * 2));
                Assert.That(result.ElapsedTicks, Is.GreaterThanOrEqualTo(0L));
                Assert.That(result.AbsoluteManagedMemoryDelta,
                    Is.LessThan(32L * 1024L * 1024L));
            }
        }

        [Test]
        public void MixedPipelineCombinesConveyorPortalGravityAndRefillWithoutDuplicatesOrMutation()
        {
            var board = new BoardTopology(4, 3,
                locked: new[] { P(3,1) },
                occupants: new[] { new BoardOccupant(P(0,0), "A") });
            var conveyors = new[]
            {
                new ConveyorEdge(P(0,0), P(0,1), 0)
            };
            var portals = new PortalRegistry(new[]
            {
                new PortalPair("p", P(0,1), P(2,2))
            });

            var plan = SettlementPipeline.Plan(board, conveyors, portals,
                GravityDirection.Down, true, DiagonalTieBreak.LowerCoordinate,
                new[] { new WeightedContent("R", 1) }, new SeededRandom(99u));

            Assert.That(plan.Stages, Is.EqualTo(new[]
                { "conveyor", "portal", "gravity", "refill" }));
            Assert.That(plan.Occupants.Count, Is.EqualTo(11));
            Assert.That(plan.Occupants[P(3,2)], Is.EqualTo("A"));
            Assert.That(plan.Occupants.ContainsKey(P(3,1)), Is.False);
            Assert.That(plan.Occupants.Keys.Distinct().Count(), Is.EqualTo(plan.Occupants.Count));
            Assert.That(plan.Operations, Is.GreaterThan(0));
            Assert.That(plan.Operations, Is.LessThan(256));
            Assert.That(board.Occupants.Count, Is.EqualTo(1));
            Assert.That(board.Occupants[P(0,0)], Is.EqualTo("A"));

            var second = SettlementPipeline.Plan(board, conveyors, portals,
                GravityDirection.Down, true, DiagonalTieBreak.LowerCoordinate,
                new[] { new WeightedContent("R", 1) }, new SeededRandom(99u));
            Assert.That(second.Occupants.OrderBy(x => x.Key.ToString()).Select(x => x.Value),
                Is.EqualTo(plan.Occupants.OrderBy(x => x.Key.ToString()).Select(x => x.Value)));
            Assert.That(second.Operations, Is.EqualTo(plan.Operations));
        }

        [Test]
        public void AuthoritativeSpecialDamageVacancyFeedsMixedSettlementWithoutOwningMatchTruth()
        {
            var board = new BoardTopology(3, 2,
                occupants: new[]
                {
                    new BoardOccupant(P(0,0), "A"),
                    new BoardOccupant(P(1,0), "B"),
                    new BoardOccupant(P(2,0), "C")
                });

            var plan = SettlementPipeline.PlanAfterAuthoritativeDamage(
                board, new[] { P(2,0) },
                new ConveyorEdge[0], new PortalRegistry(new PortalPair[0]),
                GravityDirection.Down, false, DiagonalTieBreak.LowerCoordinate,
                new[] { new WeightedContent("R", 1) }, new SeededRandom(7u));

            Assert.That(plan.Stages, Is.EqualTo(new[]
                { "conveyor", "portal", "gravity", "refill" }));
            Assert.That(plan.Occupants[P(1,0)], Is.EqualTo("A"));
            Assert.That(plan.Occupants[P(2,0)], Is.EqualTo("B"));
            Assert.That(plan.Occupants.Values.Count(x => x == "C"), Is.EqualTo(0));
            Assert.That(plan.Occupants.Count, Is.EqualTo(6));
            Assert.That(board.Occupants[P(2,0)], Is.EqualTo("C"));

            Assert.Throws<ArgumentException>(() =>
                SettlementPipeline.PlanAfterAuthoritativeDamage(
                    board, new[] { P(2,0), P(2,0) },
                    new ConveyorEdge[0], new PortalRegistry(new PortalPair[0]),
                    GravityDirection.Down, false, DiagonalTieBreak.LowerCoordinate,
                    new[] { new WeightedContent("R", 1) }, new SeededRandom(7u)));

            var locked = new BoardTopology(1, 1,
                locked: new[] { P(0,0) },
                occupants: new[] { new BoardOccupant(P(0,0), "LOCK") });
            Assert.Throws<ArgumentException>(() =>
                SettlementPipeline.PlanAfterAuthoritativeDamage(
                    locked, new[] { P(0,0) },
                    new ConveyorEdge[0], new PortalRegistry(new PortalPair[0]),
                    GravityDirection.Down, false, DiagonalTieBreak.LowerCoordinate,
                    new[] { new WeightedContent("R", 1) }, new SeededRandom(7u)));
        }

        [Test]
        public void MixedPipelineFailsClosedOnInvalidTransportInsteadOfLooping()
        {
            var board = new BoardTopology(2, 2,
                occupants: new[]
                {
                    new BoardOccupant(P(0,0), "A"),
                    new BoardOccupant(P(0,1), "B")
                });
            var cycle = new[]
            {
                new ConveyorEdge(P(0,0), P(0,1)),
                new ConveyorEdge(P(0,1), P(0,0))
            };
            Assert.Throws<ArgumentException>(() =>
                SettlementPipeline.Plan(board, cycle,
                    new PortalRegistry(new PortalPair[0]),
                    GravityDirection.Down, false, DiagonalTieBreak.LowerCoordinate,
                    new[] { new WeightedContent("R", 1) }, new SeededRandom(1u)));
        }
    }
}
