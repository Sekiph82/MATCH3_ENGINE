using System;
using System.Collections.Generic;
using System.Linq;
using Match3.Engine.Runtime;
using NUnit.Framework;

namespace Match3.Engine.Tests
{
    public sealed class GoalScoreContractTests
    {
        [Test]
        public void M07_001_GoalRegistrySupportsAtomicCompositeAndRejectsDuplicateMissingOrCyclicGraphs()
        {
            var registry = new GoalRegistry(new[]
            {
                new GoalDefinition("collect-red", GoalKind.CollectPiece,
                    "piece.red", 10),
                new GoalDefinition("clear-rock", GoalKind.ClearObstacle,
                    "rock", 4),
                new GoalDefinition("rescue-fish", GoalKind.RescueDrop,
                    "fish", 2),
                new GoalDefinition("all", GoalKind.Composite,
                    null, 1, new[] { "collect-red", "clear-rock", "rescue-fish" })
            });

            Assert.That(registry.ById.Count, Is.EqualTo(4));
            Assert.That(registry.ById["all"].ChildGoalIds,
                Is.EqualTo(new[] { "collect-red", "clear-rock", "rescue-fish" }));

            Assert.Throws<ArgumentException>(() => new GoalRegistry(new[]
            {
                new GoalDefinition("dup", GoalKind.CollectPiece, "a", 1),
                new GoalDefinition("dup", GoalKind.ClearObstacle, "b", 1)
            }));

            Assert.Throws<ArgumentException>(() => new GoalRegistry(new[]
            {
                new GoalDefinition("atomic", GoalKind.CollectPiece, "a", 1),
                new GoalDefinition("bad", GoalKind.Composite, null, 1,
                    new[] { "missing" })
            }));

            Assert.Throws<ArgumentException>(() => new GoalRegistry(new[]
            {
                new GoalDefinition("a", GoalKind.Composite, null, 1,
                    new[] { "b" }),
                new GoalDefinition("b", GoalKind.Composite, null, 1,
                    new[] { "a" })
            }));

            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new GoalDefinition("too-many", GoalKind.CollectPiece, "a", 1000001));
        }

        [Test]
        public void M07_002_MoveAndTimeBudgetsUseCommittedTurnsMonotonicReceiptsAndReplaySafeIds()
        {
            var moveOnly = new LevelBudgetSnapshot(3);
            var applied = LevelBudgetPlanner.ApplyCommittedTurn(
                moveOnly,
                new TurnReceipt("move-1", TurnMutationKind.PlayerMove, true));
            Assert.That(applied.Applied, Is.True);
            Assert.That(applied.Result.MovesRemaining, Is.EqualTo(2));

            var replay = LevelBudgetPlanner.ApplyCommittedTurn(
                applied.Result,
                new TurnReceipt("move-1", TurnMutationKind.PlayerMove, true));
            Assert.That(replay.Applied, Is.False);
            Assert.That(replay.Result.MovesRemaining, Is.EqualTo(2));

            foreach (var kind in new[]
            {
                TurnMutationKind.Cascade,
                TurnMutationKind.Booster,
                TurnMutationKind.Shuffle,
                TurnMutationKind.Preview,
                TurnMutationKind.ReplayPresentation
            })
            {
                var ignored = LevelBudgetPlanner.ApplyCommittedTurn(
                    applied.Result,
                    new TurnReceipt("ignored-" + kind, kind, true));
                Assert.That(ignored.Applied, Is.False);
                Assert.That(ignored.Result.MovesRemaining, Is.EqualTo(2));
            }

            var uncommitted = LevelBudgetPlanner.ApplyCommittedTurn(
                applied.Result,
                new TurnReceipt("uncommitted", TurnMutationKind.PlayerMove, false));
            Assert.That(uncommitted.Applied, Is.False);

            var timed = new LevelBudgetSnapshot(
                movesRemaining: 10,
                timeEnabled: true,
                timeLimitMilliseconds: 30000,
                elapsedMilliseconds: 5000);

            var tick = LevelBudgetPlanner.ApplyElapsed(
                timed, "tick-1", 10000);
            Assert.That(tick.Result.ElapsedMilliseconds, Is.EqualTo(10000));
            Assert.That(tick.Result.TimeExpired, Is.False);

            var duplicate = LevelBudgetPlanner.ApplyElapsed(
                tick.Result, "tick-1", 12000);
            Assert.That(duplicate.Applied, Is.False);
            Assert.That(duplicate.Result.ElapsedMilliseconds, Is.EqualTo(10000));

            Assert.Throws<InvalidOperationException>(() =>
                LevelBudgetPlanner.ApplyElapsed(
                    tick.Result, "tick-backward", 9000));

            var expired = LevelBudgetPlanner.ApplyElapsed(
                tick.Result, "tick-end", 999999);
            Assert.That(expired.Result.ElapsedMilliseconds, Is.EqualTo(30000));
            Assert.That(expired.Result.TimeExpired, Is.True);

            Assert.Throws<InvalidOperationException>(() =>
                LevelBudgetPlanner.ApplyElapsed(
                    moveOnly, "illegal-time", 1000));
        }

        [Test]
        public void M07_003_ScoreAndStarRulesAreDeterministicBoundedAndStrictlyValidated()
        {
            var thresholds = new StarThresholds(100, 200, 300);
            Assert.That(thresholds.StarsFor(0), Is.EqualTo(0));
            Assert.That(thresholds.StarsFor(100), Is.EqualTo(1));
            Assert.That(thresholds.StarsFor(250), Is.EqualTo(2));
            Assert.That(thresholds.StarsFor(300), Is.EqualTo(3));

            Assert.That(DeterministicScorePlanner.ScoreEvent(
                100, 0), Is.EqualTo(100));
            Assert.That(DeterministicScorePlanner.ScoreEvent(
                100, 2), Is.EqualTo(150));
            Assert.That(DeterministicScorePlanner.ScoreEvent(
                100, 99), Is.EqualTo(400));

            var first = DeterministicScorePlanner.ScoreEvent(
                1250, 7, 20, 350);
            var second = DeterministicScorePlanner.ScoreEvent(
                1250, 7, 20, 350);
            Assert.That(first, Is.EqualTo(second));
            Assert.That(DeterministicScorePlanner.AddScore(
                5000, first), Is.EqualTo(5000 + first));

            Assert.Throws<ArgumentException>(() =>
                new StarThresholds(100, 100, 300));
            Assert.Throws<ArgumentException>(() =>
                new StarThresholds(-1, 100, 200));
            Assert.Throws<OverflowException>(() =>
                DeterministicScorePlanner.AddScore(
                    Int32.MaxValue, 1));
        }

        [Test]
        public void M07_004_FinalSettleAndGoalCompletionWinBeforeBudgetExhaustionLose()
        {
            var exhaustedMoves = new LevelBudgetSnapshot(0);
            var unsettled = LevelTerminalArbitrator.Resolve(
                false, true, exhaustedMoves);
            Assert.That(unsettled.Outcome, Is.EqualTo(LevelTerminalOutcome.None));
            Assert.That(unsettled.FinalSettleComplete, Is.False);

            var sameResolutionWin = LevelTerminalArbitrator.Resolve(
                true, true, exhaustedMoves);
            Assert.That(sameResolutionWin.Outcome,
                Is.EqualTo(LevelTerminalOutcome.Win));

            var moveLoss = LevelTerminalArbitrator.Resolve(
                true, false, exhaustedMoves);
            Assert.That(moveLoss.Outcome,
                Is.EqualTo(LevelTerminalOutcome.LoseMoves));

            var timeExpired = new LevelBudgetSnapshot(
                4, true, 1000, 1000);
            Assert.That(LevelTerminalArbitrator.Resolve(
                true, false, timeExpired).Outcome,
                Is.EqualTo(LevelTerminalOutcome.LoseTime));

            Assert.That(LevelTerminalArbitrator.Resolve(
                true, true, timeExpired).Outcome,
                Is.EqualTo(LevelTerminalOutcome.Win));

            var ongoing = new LevelBudgetSnapshot(2);
            Assert.That(LevelTerminalArbitrator.Resolve(
                true, false, ongoing).Outcome,
                Is.EqualTo(LevelTerminalOutcome.None));
        }
    }
}
