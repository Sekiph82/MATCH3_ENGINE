using System;
using System.Collections.Generic;
using System.Linq;
using Match3.Engine.Runtime;
using NUnit.Framework;

namespace Match3.Engine.Tests
{
    public sealed class GoalProgressContractTests
    {
        private static GoalRegistry Registry() =>
            new GoalRegistry(new[]
            {
                new GoalDefinition("collect-red", GoalKind.CollectPiece, "piece.red", 3),
                new GoalDefinition("clear-rock", GoalKind.ClearObstacle, "rock", 2),
                new GoalDefinition("all", GoalKind.Composite, null, 1,
                    new[] { "collect-red", "clear-rock" })
            });

        [Test]
        public void M07_005_PostWinLeftoverConversionIsConfigurableBoundedAndRewardAuthorityNeutral()
        {
            var committed = new CommittedWinSnapshot("win-1", 1000, true);

            var disabled = PostWinPlanner.Resolve(
                committed, 50, new PostWinPolicy(false, 10, 100));
            Assert.That(disabled.AuthorityPlan.PresentationScoreDelta, Is.EqualTo(0));
            Assert.That(disabled.AuthorityPlan.FinalScore, Is.EqualTo(1000));
            Assert.That(disabled.AuthorityPlan.AuthoritativeRewardDelta, Is.EqualTo(0));
            Assert.That(disabled.AuthorityPlan.Outcome, Is.SameAs(committed));

            var enabled = PostWinPlanner.Resolve(
                committed, 50, new PostWinPolicy(true, 3, 100));
            Assert.That(enabled.AuthorityPlan.PresentationScoreDelta, Is.EqualTo(300));
            Assert.That(enabled.AuthorityPlan.FinalScore, Is.EqualTo(1300));
            Assert.That(enabled.AuthorityPlan.AuthoritativeRewardDelta, Is.EqualTo(0));
            Assert.That(enabled.AuthorityPlan.Events.Count(x =>
                x.Kind == FinalePresentationKind.ConvertMoveToSpecial), Is.EqualTo(3));
            Assert.That(committed.AuthoritativeScore, Is.EqualTo(1000));
            Assert.That(committed.FirstClearAwarded, Is.True);
        }

        [Test]
        public void M07_006_ProgressStreamKeepsHudAndSummaryInLockstepAndRejectsOutOfOrderEvents()
        {
            var registry = Registry();
            var start = GoalProgressPlanner.Create(registry);

            var one = GoalProgressPlanner.Apply(registry, start,
                new GoalProgressEvent("e1", 1, "collect-red", 2, 100));
            var two = GoalProgressPlanner.Apply(registry, one.Result,
                new GoalProgressEvent("e2", 2, "clear-rock", 2, 50));
            var three = GoalProgressPlanner.Apply(registry, two.Result,
                new GoalProgressEvent("e3", 3, "collect-red", 1, 25));

            var hud = GoalProgressPlanner.ProjectViews(registry, three.Result);
            var summary = GoalProgressPlanner.ProjectViews(registry, three.Result);
            Assert.That(summary.Select(x => x.GoalId),
                Is.EqualTo(hud.Select(x => x.GoalId)));
            Assert.That(summary.Select(x => x.Current),
                Is.EqualTo(hud.Select(x => x.Current)));
            Assert.That(summary.Select(x => x.Complete),
                Is.EqualTo(hud.Select(x => x.Complete)));
            Assert.That(GoalProgressPlanner.IsComplete(
                registry, three.Result, "all"), Is.True);
            Assert.That(three.Result.Score, Is.EqualTo(175));

            var replay = GoalProgressPlanner.Apply(registry, three.Result,
                new GoalProgressEvent("e3", 999, "collect-red", 99, 999));
            Assert.That(replay.Applied, Is.False);
            Assert.That(replay.Result, Is.SameAs(three.Result));

            Assert.Throws<InvalidOperationException>(() =>
                GoalProgressPlanner.Apply(registry, three.Result,
                    new GoalProgressEvent("gap", 5, "collect-red", 1)));
            Assert.Throws<InvalidOperationException>(() =>
                GoalProgressPlanner.Apply(registry, three.Result,
                    new GoalProgressEvent("composite-direct", 4, "all", 1)));
        }

        [Test]
        public void M07_007_LevelGoalValidationRejectsUnavailableAndImpossibleTargetsBeforePlay()
        {
            var registry = Registry();
            Assert.DoesNotThrow(() =>
                LevelGoalValidator.RequirePlayable(registry, new[]
                {
                    new GoalCapacity(GoalKind.CollectPiece, "piece.red", 6),
                    new GoalCapacity(GoalKind.ClearObstacle, "rock", 4)
                }));

            Assert.Throws<InvalidOperationException>(() =>
                LevelGoalValidator.RequirePlayable(registry, new[]
                {
                    new GoalCapacity(GoalKind.CollectPiece, "piece.red", 6)
                }));

            Assert.Throws<InvalidOperationException>(() =>
                LevelGoalValidator.RequirePlayable(registry, new[]
                {
                    new GoalCapacity(GoalKind.CollectPiece, "piece.red", 2),
                    new GoalCapacity(GoalKind.ClearObstacle, "rock", 4)
                }));

            Assert.Throws<ArgumentException>(() =>
                LevelGoalValidator.RequirePlayable(registry, new[]
                {
                    new GoalCapacity(GoalKind.CollectPiece, "piece.red", 6),
                    new GoalCapacity(GoalKind.CollectPiece, "piece.red", 6),
                    new GoalCapacity(GoalKind.ClearObstacle, "rock", 4)
                }));

            var rescueRegistry = new GoalRegistry(new[]
            {
                new GoalDefinition("drop-fish", GoalKind.RescueDrop, "fish", 2)
            });
            Assert.Throws<InvalidOperationException>(() =>
                LevelGoalValidator.RequirePlayable(
                    rescueRegistry, new GoalCapacity[0]));
            Assert.DoesNotThrow(() =>
                LevelGoalValidator.RequirePlayable(rescueRegistry, new[]
                {
                    new GoalCapacity(GoalKind.RescueDrop, "fish", 2)
                }));
        }

        [Test]
        public void M07_008_SeededQaProducesHundredsOfValidFixturesWithStableAggregateHash()
        {
            var a = GoalQaFixtureGenerator.Generate(20261014u, 512);
            var b = GoalQaFixtureGenerator.Generate(20261014u, 512);
            var c = GoalQaFixtureGenerator.Generate(20261015u, 512);

            Assert.That(a.Count, Is.EqualTo(512));
            Assert.That(a.All(x => x.Moves >= 1 && x.Moves <= 50), Is.True);
            Assert.That(a.All(x => x.BasePoints >= 10), Is.True);
            Assert.That(a.All(x => x.ComboDepth >= 0 && x.ComboDepth < 16), Is.True);
            Assert.That(GoalQaFixtureGenerator.Hash(a),
                Is.EqualTo(GoalQaFixtureGenerator.Hash(b)));
            Assert.That(GoalQaFixtureGenerator.Hash(a),
                Is.Not.EqualTo(GoalQaFixtureGenerator.Hash(c)));

            var qaRegistry = new GoalRegistry(new[]
            {
                new GoalDefinition("qa-goal", GoalKind.CollectPiece, "qa", 1)
            });
            var incomplete = GoalProgressPlanner.Create(qaRegistry);
            var complete = GoalProgressPlanner.Apply(
                qaRegistry, incomplete,
                new GoalProgressEvent("qa-complete", 1, "qa-goal", 1)).Result;

            foreach (var fixture in a)
            {
                Assert.DoesNotThrow(() =>
                    DeterministicScorePlanner.ScoreEvent(
                        fixture.BasePoints, fixture.ComboDepth));
                var progress = fixture.GoalsComplete ? complete : incomplete;
                var resolution = LevelTerminalArbitrator.Resolve(
                    true, qaRegistry, progress,
                    new LevelBudgetSnapshot(fixture.Moves));
                Assert.That(resolution.Outcome,
                    Is.EqualTo(fixture.GoalsComplete
                        ? LevelTerminalOutcome.Win
                        : LevelTerminalOutcome.None));
            }
        }

        [Test]
        public void M07_009_FirstClearRewardsAreExactlyOnceAndReplayOnlyImprovesBestPolicy()
        {
            var thresholds = new StarThresholds(500, 1000, 1500);
            var start = new LevelProgressionSnapshot("L-001");
            var firstAttempt = new LevelResultAttempt(
                "attempt-1", "L-001", true, 1200, 2, true);

            var first = FirstClearProgressionPlanner.Apply(
                start, firstAttempt, thresholds);
            Assert.That(first.FirstClearApplied, Is.True);
            Assert.That(first.RewardSourceIds, Is.EqualTo(new[]
            {
                "firstclear:L-001:coins",
                "firstclear:L-001:tasks",
                "firstclear:L-001:album",
                "firstclear:L-001:progression"
            }));
            Assert.That(first.Result.FirstClearGranted, Is.True);
            Assert.That(first.Result.BestScore, Is.EqualTo(1200));
            Assert.That(first.Result.HighestStars, Is.EqualTo(2));

            var duplicateAttempt = FirstClearProgressionPlanner.Apply(
                first.Result, firstAttempt, thresholds);
            Assert.That(duplicateAttempt.Applied, Is.False);
            Assert.That(duplicateAttempt.RewardSourceIds, Is.Empty);

            var replay = FirstClearProgressionPlanner.Apply(
                first.Result,
                new LevelResultAttempt("attempt-2", "L-001",
                    true, 1800, 3, true),
                thresholds);
            Assert.That(replay.FirstClearApplied, Is.False);
            Assert.That(replay.RewardSourceIds, Is.Empty);
            Assert.That(replay.PersonalBestImproved, Is.True);
            Assert.That(replay.HighestStarsImproved, Is.True);
            Assert.That(replay.Result.BestScore, Is.EqualTo(1800));
            Assert.That(replay.Result.HighestStars, Is.EqualTo(3));

            Assert.That(replay.RewardSourceIds.Any(x =>
                x.Contains("streak") || x.Contains("milestone") ||
                x.Contains("ad")), Is.False);

            Assert.Throws<InvalidOperationException>(() =>
                FirstClearProgressionPlanner.Apply(
                    replay.Result,
                    new LevelResultAttempt("forged", "L-001",
                        true, 999999, 3, false),
                    thresholds));
            Assert.Throws<InvalidOperationException>(() =>
                FirstClearProgressionPlanner.Apply(
                    replay.Result,
                    new LevelResultAttempt("bad-stars", "L-001",
                        true, 600, 3, true),
                    thresholds));
        }

        [Test]
        public void M07_010_SkipAndReducedMotionSuppressPresentationOnlyNotFinalScoreStarsOrRewards()
        {
            var win = new CommittedWinSnapshot(
                "win-final", 2000, true);
            var committed = new CommittedPostWinAuthority(
                win, 3, "progress:L-001:attempt-9",
                new[]
                {
                    "firstclear:L-001:coins",
                    "firstclear:L-001:tasks"
                });
            var policy = new PostWinPolicy(true, 4, 125);

            var normal = PostWinPlanner.Resolve(
                committed, 10, policy, false, false);
            var skipped = PostWinPlanner.Resolve(
                committed, 10, policy, true, false);
            var reduced = PostWinPlanner.Resolve(
                committed, 10, policy, false, true);

            Assert.That(normal.AuthorityPlan.FinalScore,
                Is.EqualTo(skipped.AuthorityPlan.FinalScore));
            Assert.That(normal.AuthorityPlan.FinalScore,
                Is.EqualTo(reduced.AuthorityPlan.FinalScore));
            Assert.That(normal.AuthorityPlan.PresentationScoreDelta,
                Is.EqualTo(500));
            Assert.That(normal.AuthorityPlan.AuthoritativeRewardDelta,
                Is.EqualTo(0));
            Assert.That(skipped.AuthorityPlan.AuthoritativeRewardDelta,
                Is.EqualTo(0));
            Assert.That(reduced.AuthorityPlan.AuthoritativeRewardDelta,
                Is.EqualTo(0));
            Assert.That(normal.AuthorityPlan.Outcome.FirstClearAwarded, Is.True);
            Assert.That(skipped.AuthorityPlan.Outcome.FirstClearAwarded, Is.True);
            Assert.That(reduced.AuthorityPlan.Outcome.FirstClearAwarded, Is.True);
            Assert.That(normal.CommittedAuthority, Is.SameAs(committed));
            Assert.That(skipped.CommittedAuthority, Is.SameAs(committed));
            Assert.That(reduced.CommittedAuthority, Is.SameAs(committed));
            Assert.That(normal.CommittedAuthority.Stars, Is.EqualTo(3));
            Assert.That(skipped.CommittedAuthority.ProgressionReceiptId,
                Is.EqualTo("progress:L-001:attempt-9"));
            Assert.That(reduced.CommittedAuthority.RewardSourceIds,
                Is.EqualTo(committed.RewardSourceIds));
            Assert.That(normal.PresentationEvents.Count, Is.GreaterThan(0));
            Assert.That(skipped.PresentationEvents, Is.Empty);
            Assert.That(reduced.PresentationEvents, Is.Empty);
        }
    }
}
