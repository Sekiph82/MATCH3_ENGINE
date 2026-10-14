using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Match3.Engine.Runtime;
using NUnit.Framework;

namespace Match3.Engine.Tests
{
    public sealed class ObstacleLifecycleContractTests
    {
        private static BoardPosition P(int row, int col, int layer = 0) =>
            new BoardPosition(row, col, layer);

        [Test]
        public void M05_001_LayerModelOrdersCanonicalLayersRejectsDuplicatesAndInvalidDurability()
        {
            var states = new[]
            {
                new ObstacleState("cell", "cell_def", P(1,1), ObstacleLayerKind.Cell, 1),
                new ObstacleState("under", "under_def", P(1,1), ObstacleLayerKind.Underlay, 1),
                new ObstacleState("piece", "piece_def", P(1,1), ObstacleLayerKind.Piece, 1),
                new ObstacleState("overlay", "overlay_def", P(1,1), ObstacleLayerKind.Overlay, 1),
            };
            var snapshot = new ObstacleSnapshot(3, 3, states);
            Assert.That(snapshot.At(P(1,1)).Select(x => x.Layer).ToArray(), Is.EqualTo(new[]
            {
                ObstacleLayerKind.Underlay,
                ObstacleLayerKind.Overlay,
                ObstacleLayerKind.Piece,
                ObstacleLayerKind.Cell
            }));
            Assert.That(snapshot.States.Select(x => x.InstanceId).ToArray(),
                Is.EqualTo(new[] { "under", "overlay", "piece", "cell" }));

            Assert.Throws<ArgumentException>(() => new ObstacleSnapshot(2, 2, new[]
            {
                new ObstacleState("a", "x", P(0,0), ObstacleLayerKind.Overlay, 1),
                new ObstacleState("b", "y", P(0,0), ObstacleLayerKind.Overlay, 1),
            }));

            var invalidHp = new ObstacleSnapshot(1, 1, new[]
            {
                new ObstacleState("too-strong", "fragile", P(0,0), ObstacleLayerKind.Cell, 2)
            });
            Assert.Throws<InvalidOperationException>(() =>
                ObstacleDamagePlanner.Plan(invalidHp,
                    new[] { new ObstacleDefinition("fragile", 1) },
                    new ObstacleHitRequest[0]));
        }

        [Test]
        public void M05_002_MultiHitTransitionsClampOverkillAndEmitOneRemovalReceipt()
        {
            var definition = new ObstacleDefinition("rock", 3, goalKey: "goal.rock");
            var origin = new ObstacleSnapshot(1, 1, new[]
            {
                new ObstacleState("rock-1", "rock", P(0,0), ObstacleLayerKind.Cell, 3)
            });
            var plan = ObstacleDamagePlanner.Plan(origin, new[] { definition }, new[]
            {
                new ObstacleHitRequest(P(0,0), ObstacleDamageKind.Normal, "phase-a", 1),
                new ObstacleHitRequest(P(0,0), ObstacleDamageKind.Normal, "phase-a", 1),
                new ObstacleHitRequest(P(0,0), ObstacleDamageKind.Normal, "phase-b", 99),
            });

            Assert.That(plan.DamageReceipts.Count, Is.EqualTo(2));
            Assert.That(plan.DamageReceipts[0].Before, Is.EqualTo(3));
            Assert.That(plan.DamageReceipts[0].After, Is.EqualTo(2));
            Assert.That(plan.DamageReceipts[1].Before, Is.EqualTo(2));
            Assert.That(plan.DamageReceipts[1].After, Is.EqualTo(0));
            Assert.That(plan.DamageReceipts[1].Removed, Is.True);
            Assert.That(plan.GoalReceipts.Count, Is.EqualTo(1));
            Assert.That(plan.Result.States, Is.Empty);
            Assert.That(origin.ByInstanceId["rock-1"].Durability, Is.EqualTo(3));
        }

        [Test]
        public void M05_003_ImmobilizerBlocksUntilItsOwnLayerIsRemovedWithoutMovingPiece()
        {
            var chain = new ObstacleDefinition("chain", 1, immobilizesPiece: true);
            var snapshot = new ObstacleSnapshot(1, 1, new[]
            {
                new ObstacleState("chain-1", "chain", P(0,0), ObstacleLayerKind.Overlay, 1)
            });

            Assert.That(ObstacleDamagePlanner.IsPieceImmobilized(
                snapshot, new[] { chain }, P(0,0)), Is.True);

            var cleared = ObstacleDamagePlanner.Plan(snapshot, new[] { chain }, new[]
            {
                new ObstacleHitRequest(P(0,0), ObstacleDamageKind.Normal, "clear")
            });
            Assert.That(cleared.Result.States, Is.Empty);
            Assert.That(ObstacleDamagePlanner.IsPieceImmobilized(
                cleared.Result, new[] { chain }, P(0,0)), Is.False);
            Assert.That(snapshot.ByInstanceId.ContainsKey("chain-1"), Is.True);
        }

        [Test]
        public void M05_004_SpreadOccursOnlyOncePerCommittedPlayerTurnAndIsSeededBounded()
        {
            var slime = new ObstacleDefinition("slime", 2, spreads: true);
            var snapshot = new ObstacleSnapshot(3, 3, new[]
            {
                new ObstacleState("s0", "slime", P(1,1), ObstacleLayerKind.Underlay, 2)
            });
            var turn = new TurnReceipt("turn-7", TurnMutationKind.PlayerMove, true);

            var first = ObstacleSpreadPlanner.Plan(snapshot, new[] { slime }, turn,
                seed: 77u, maximumNewCells: 2, maximumTotalObstacles: 3);
            var same = ObstacleSpreadPlanner.Plan(snapshot, new[] { slime }, turn,
                seed: 77u, maximumNewCells: 2, maximumTotalObstacles: 3);
            Assert.That(first.Spawned.Select(x => x.Position).ToArray(),
                Is.EqualTo(same.Spawned.Select(x => x.Position).ToArray()));
            Assert.That(first.Spawned.Count, Is.EqualTo(2));
            Assert.That(first.Result.States.Count, Is.EqualTo(3));
            Assert.That(first.ProcessedTurnIds, Does.Contain("turn-7"));

            var replay = ObstacleSpreadPlanner.Plan(snapshot, new[] { slime }, turn,
                77u, 2, 3, first.ProcessedTurnIds);
            Assert.That(replay.Spawned, Is.Empty);
            Assert.That(replay.Result, Is.SameAs(snapshot));

            foreach (var kind in new[]
            {
                TurnMutationKind.Cascade, TurnMutationKind.Booster,
                TurnMutationKind.Shuffle, TurnMutationKind.Preview,
                TurnMutationKind.ReplayPresentation
            })
            {
                var ignored = ObstacleSpreadPlanner.Plan(snapshot, new[] { slime },
                    new TurnReceipt("ignored-" + kind, kind, true), 77u);
                Assert.That(ignored.Spawned, Is.Empty);
            }
            Assert.That(ObstacleSpreadPlanner.Plan(snapshot, new[] { slime },
                new TurnReceipt("uncommitted", TurnMutationKind.PlayerMove, false), 77u)
                .Spawned, Is.Empty);
        }

        [Test]
        public void M05_006_SpecialDamageKindsHonorPoliciesAndDeduplicateSamePhase()
        {
            var normal = new ObstacleDefinition("normal", 5);
            var noArea = new ObstacleDefinition("no-area", 2, areaDamageAllowed: false);
            var absorb = new ObstacleDefinition("absorb", 2,
                lineInteraction: LineInteraction.AbsorbWithoutDamage);
            var snapshot = new ObstacleSnapshot(1, 3, new[]
            {
                new ObstacleState("n", "normal", P(0,0), ObstacleLayerKind.Cell, 5),
                new ObstacleState("a", "no-area", P(0,1), ObstacleLayerKind.Cell, 2),
                new ObstacleState("b", "absorb", P(0,2), ObstacleLayerKind.Cell, 2)
            });

            var plan = ObstacleDamagePlanner.Plan(snapshot,
                new[] { normal, noArea, absorb }, new[]
                {
                    new ObstacleHitRequest(P(0,0), ObstacleDamageKind.Line, "p1"),
                    new ObstacleHitRequest(P(0,0), ObstacleDamageKind.Line, "p1"),
                    new ObstacleHitRequest(P(0,0), ObstacleDamageKind.Area, "p2"),
                    new ObstacleHitRequest(P(0,0), ObstacleDamageKind.Color, "p3"),
                    new ObstacleHitRequest(P(0,0), ObstacleDamageKind.Targeted, "p4"),
                    new ObstacleHitRequest(P(0,1), ObstacleDamageKind.Area, "area-blocked"),
                    new ObstacleHitRequest(P(0,2), ObstacleDamageKind.Line, "line-absorbed")
                });

            Assert.That(plan.DamageReceipts.Count(x => x.InstanceId == "n"), Is.EqualTo(4));
            Assert.That(plan.Result.ByInstanceId["n"].Durability, Is.EqualTo(1));
            Assert.That(plan.Result.ByInstanceId["a"].Durability, Is.EqualTo(2));
            Assert.That(plan.Result.ByInstanceId["b"].Durability, Is.EqualTo(2));
        }

        [Test]
        public void M05_007_GoalHookUsesStableObstacleIdentityAndReplaySafeReceiptId()
        {
            var coral = new ObstacleDefinition("coral", 1, goalKey: "goal.coral");
            var snapshot = new ObstacleSnapshot(1, 1, new[]
            {
                new ObstacleState("coral-instance-9", "coral", P(0,0),
                    ObstacleLayerKind.Overlay, 1)
            });
            var hit = new[]
            {
                new ObstacleHitRequest(P(0,0), ObstacleDamageKind.Targeted, "wave-3")
            };

            var first = ObstacleDamagePlanner.Plan(snapshot, new[] { coral }, hit);
            var replay = ObstacleDamagePlanner.Plan(snapshot, new[] { coral }, hit);

            Assert.That(first.GoalReceipts.Count, Is.EqualTo(1));
            Assert.That(first.GoalReceipts[0].InstanceId, Is.EqualTo("coral-instance-9"));
            Assert.That(first.GoalReceipts[0].GoalKey, Is.EqualTo("goal.coral"));
            Assert.That(first.GoalReceipts[0].ReceiptId, Is.EqualTo("wave-3|coral-instance-9"));
            Assert.That(replay.GoalReceipts[0].ReceiptId,
                Is.EqualTo(first.GoalReceipts[0].ReceiptId));
            var reapplyCommittedResult = ObstacleDamagePlanner.Plan(
                first.Result, new[] { coral }, hit);
            Assert.That(reapplyCommittedResult.GoalReceipts, Is.Empty);
            Assert.That(new HashSet<string>(new[]
            {
                first.GoalReceipts[0].ReceiptId, replay.GoalReceipts[0].ReceiptId
            }, StringComparer.Ordinal).Count, Is.EqualTo(1));
        }

        [Test]
        public void M05_010_CountdownConsumesOnlyUniqueCommittedPlayerTurnsAndHonorsWinPrecedence()
        {
            var lose = new[] { new CountdownState("bomb", P(0,0), 1, CountdownZeroRule.Lose) };
            var committed = new TurnReceipt("turn-1", TurnMutationKind.PlayerMove, true);

            var loss = CountdownPlanner.Plan(lose, committed, null, false);
            Assert.That(loss.States[0].FuseRemaining, Is.EqualTo(0));
            Assert.That(loss.Outcome, Is.EqualTo(CountdownOutcome.Loss));
            Assert.That(loss.ProcessedTurnIds, Does.Contain("turn-1"));

            var replay = CountdownPlanner.Plan(lose, committed,
                loss.ProcessedTurnIds, false);
            Assert.That(replay.States[0].FuseRemaining, Is.EqualTo(1));
            Assert.That(replay.Outcome, Is.EqualTo(CountdownOutcome.None));

            foreach (var kind in new[]
            {
                TurnMutationKind.Cascade, TurnMutationKind.Booster,
                TurnMutationKind.Shuffle, TurnMutationKind.Preview,
                TurnMutationKind.ReplayPresentation
            })
            {
                var ignored = CountdownPlanner.Plan(lose,
                    new TurnReceipt("x-" + kind, kind, true), null, false);
                Assert.That(ignored.States[0].FuseRemaining, Is.EqualTo(1));
                Assert.That(ignored.Outcome, Is.EqualTo(CountdownOutcome.None));
            }

            var win = CountdownPlanner.Plan(lose,
                new TurnReceipt("turn-win", TurnMutationKind.PlayerMove, true),
                null, true);
            Assert.That(win.Outcome, Is.EqualTo(CountdownOutcome.GoalWinPrecedence));

            var detonate = CountdownPlanner.Plan(
                new[] { new CountdownState("d", P(0,0), 1, CountdownZeroRule.Detonate) },
                new TurnReceipt("turn-d", TurnMutationKind.PlayerMove, true),
                null, false);
            Assert.That(detonate.Outcome, Is.EqualTo(CountdownOutcome.Detonate));
        }

        [Test]
        public void M05_011_MysteryEggStagesAndSeededRevealAreDeterministicAndOneShot()
        {
            var outputs = new[] { new EggOutput("shell-a", 1), new EggOutput("shell-b", 3) };
            var stage0 = new MysteryEggState("egg-1", 0, 2);
            var first = MysteryEggPlanner.Advance(stage0, outputs, 123u);
            Assert.That(first.NewStage, Is.EqualTo(1));
            Assert.That(first.Hatched, Is.False);
            Assert.That(first.OutputContentId, Is.Null);

            var stage1 = new MysteryEggState("egg-1", first.NewStage, 2);
            var hatchA = MysteryEggPlanner.Advance(stage1, outputs, 123u);
            var hatchB = MysteryEggPlanner.Advance(stage1, outputs, 123u);
            Assert.That(hatchA.Hatched, Is.True);
            Assert.That(hatchA.OutputContentId, Is.EqualTo(hatchB.OutputContentId));

            var final = new MysteryEggState("egg-1", 2, 2);
            var replay = MysteryEggPlanner.Advance(final, outputs, 123u);
            Assert.That(replay.Hatched, Is.False);
            Assert.That(replay.OutputContentId, Is.Null);

            Assert.Throws<ArgumentException>(() =>
                MysteryEggPlanner.Advance(stage0, new EggOutput[0], 123u));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                MysteryEggPlanner.Advance(stage0, outputs, 0u));

            var publicNames = typeof(MysteryEggPlanner).GetMethods(
                    BindingFlags.Public | BindingFlags.Static)
                .Select(x => x.Name)
                .Concat(typeof(MysteryEggState).GetProperties().Select(x => x.Name))
                .Concat(typeof(EggOutput).GetProperties().Select(x => x.Name))
                .Select(x => x.ToLowerInvariant()).ToArray();
            Assert.That(publicNames.Any(x => x.Contains("premium") ||
                x.Contains("currency") || x.Contains("reroll")), Is.False);
        }

        [Test]
        public void M05_012_BeamAbsorbStopAndAreaPolicyAreIndependentWithPhaseDedup()
        {
            var absorber = new ObstacleDefinition("absorber", 2,
                lineInteraction: LineInteraction.AbsorbWithoutDamage,
                areaDamageAllowed: true);
            var stop = new ObstacleDefinition("stop", 2,
                lineInteraction: LineInteraction.StopAfterDamage,
                areaDamageAllowed: true);

            var absorbSnapshot = new ObstacleSnapshot(1, 3, new[]
            {
                new ObstacleState("abs", "absorber", P(0,1), ObstacleLayerKind.Cell, 2),
                new ObstacleState("later", "stop", P(0,2), ObstacleLayerKind.Cell, 2)
            });
            var absorbedBeam = ObstacleBeamPlanner.PlanLine(absorbSnapshot,
                new[] { absorber, stop }, new[] { P(0,0), P(0,1), P(0,2) }, "beam");
            Assert.That(absorbedBeam.Stopped, Is.True);
            Assert.That(absorbedBeam.StopPosition, Is.EqualTo(P(0,1)));
            Assert.That(absorbedBeam.Hits, Is.Empty);

            var area = ObstacleDamagePlanner.Plan(absorbSnapshot,
                new[] { absorber, stop }, new[]
                {
                    new ObstacleHitRequest(P(0,1), ObstacleDamageKind.Area, "area")
                });
            Assert.That(area.Result.ByInstanceId["abs"].Durability, Is.EqualTo(1));

            var stopSnapshot = new ObstacleSnapshot(1, 1, new[]
            {
                new ObstacleState("s", "stop", P(0,0), ObstacleLayerKind.Cell, 2)
            });
            var beam = ObstacleBeamPlanner.PlanLine(stopSnapshot, new[] { stop },
                new[] { P(0,0) }, "same-phase");
            var overlap = ObstacleDamagePlanner.Plan(stopSnapshot, new[] { stop },
                beam.Hits.Concat(new[]
                {
                    new ObstacleHitRequest(P(0,0), ObstacleDamageKind.Area, "same-phase")
                }));
            Assert.That(overlap.DamageReceipts.Count, Is.EqualTo(1));
            Assert.That(overlap.Result.ByInstanceId["s"].Durability, Is.EqualTo(1));
            Assert.Throws<InvalidOperationException>(() =>
                ObstacleBeamPlanner.PlanLine(stopSnapshot, new[] { stop },
                    Enumerable.Repeat(P(0,0), 4097), "too-long"));
        }
    }
}
