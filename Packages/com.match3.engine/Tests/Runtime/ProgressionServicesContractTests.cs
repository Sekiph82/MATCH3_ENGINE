using System;
using System.Collections.Generic;
using System.Linq;
using Match3.Engine.Runtime;
using NUnit.Framework;

namespace Match3.Engine.Tests
{
    public sealed class ProgressionServicesContractTests
    {
        private static IReadOnlyList<KeyValuePair<string, string>> ThemeBindings(
            string suffix)
        {
            return GameProfileCatalog.Ids.Select(id =>
                new KeyValuePair<string, string>(
                    id, id + "." + suffix)).ToArray();
        }

        [Test]
        public void M08_005_GameEconomyIsPerGameIdempotentAndRejectsCrossGameSourceReuse()
        {
            var coral = GameProfileCatalog.Ids[0];
            var garage = GameProfileCatalog.Ids[1];
            var start = new GameEconomySnapshot();

            var credit = new GameEconomyTransaction(
                "earn-1", coral, "coins", 100,
                GameEconomyMutationKind.Credit);
            var first = GameEconomyPlanner.Apply(start, credit);
            Assert.That(first.Result.Balance(coral, "coins"), Is.EqualTo(100));
            Assert.That(first.Result.Balance(garage, "coins"), Is.EqualTo(0));

            var replay = GameEconomyPlanner.Apply(first.Result, credit);
            Assert.That(replay.Applied, Is.False);
            Assert.That(replay.Result.Balance(coral, "coins"), Is.EqualTo(100));

            var debit = GameEconomyPlanner.Apply(first.Result,
                new GameEconomyTransaction(
                    "spend-1", coral, "coins", 30,
                    GameEconomyMutationKind.Debit));
            Assert.That(debit.Result.Balance(coral, "coins"), Is.EqualTo(70));

            var booster = GameEconomyPlanner.Apply(debit.Result,
                new GameEconomyTransaction(
                    "booster-grant-1", coral, "booster.hammer", 2,
                    GameEconomyMutationKind.Credit));
            Assert.That(booster.Result.Balance(
                coral, "booster.hammer"), Is.EqualTo(2));

            Assert.Throws<InvalidOperationException>(() =>
                GameEconomyPlanner.Apply(first.Result,
                    new GameEconomyTransaction(
                        "earn-1", garage, "coins", 100,
                        GameEconomyMutationKind.Credit)));

            Assert.Throws<InvalidOperationException>(() =>
                GameEconomyPlanner.Apply(start,
                    new GameEconomyTransaction(
                        "overspend", coral, "coins", 1,
                        GameEconomyMutationKind.Debit)));
        }

        [Test]
        public void M08_006_DailyRewardsClaimOnceClampRollbackAndHonorMissedDayPolicy()
        {
            var resetPolicy = new DailyRewardPolicy(
                3, MissedDayPolicy.ResetStreak);
            var start = new DailyRewardSnapshot(highWaterDayId: 100);

            var d100 = DailyRewardPlanner.Claim(
                start, 100, "claim-100", resetPolicy);
            Assert.That(d100.Applied, Is.True);
            Assert.That(d100.Result.Streak, Is.EqualTo(1));
            Assert.That(d100.RewardSourceId, Is.EqualTo("daily:100"));

            var replay = DailyRewardPlanner.Claim(
                d100.Result, 100, "claim-100", resetPolicy);
            Assert.That(replay.Applied, Is.False);

            var sameDayDifferentId = DailyRewardPlanner.Claim(
                d100.Result, 100, "claim-100-b", resetPolicy);
            Assert.That(sameDayDifferentId.Applied, Is.False);

            var d101 = DailyRewardPlanner.Claim(
                d100.Result, 101, "claim-101", resetPolicy);
            Assert.That(d101.Result.Streak, Is.EqualTo(2));

            var d103 = DailyRewardPlanner.Claim(
                d101.Result, 103, "claim-103", resetPolicy);
            Assert.That(d103.Result.Streak, Is.EqualTo(1));

            Assert.Throws<InvalidOperationException>(() =>
                DailyRewardPlanner.Claim(
                    d103.Result, 102, "rollback", resetPolicy));
            Assert.Throws<InvalidOperationException>(() =>
                DailyRewardPlanner.Claim(
                    d103.Result, 1000, "future-jump", resetPolicy));

            var capped = DailyRewardPlanner.Claim(
                DailyRewardPlanner.Claim(
                    DailyRewardPlanner.Claim(
                        new DailyRewardSnapshot(200),
                        200, "c200", resetPolicy).Result,
                    201, "c201", resetPolicy).Result,
                202, "c202", resetPolicy);
            var d203 = DailyRewardPlanner.Claim(
                capped.Result, 203, "c203", resetPolicy);
            Assert.That(d203.Result.Streak, Is.EqualTo(3));

            var preserve = new DailyRewardPolicy(
                7, MissedDayPolicy.PreserveStreak);
            var preserved = DailyRewardPlanner.Claim(
                new DailyRewardSnapshot(
                    10, 10, 4, new[] { "old" }),
                20, "late", preserve);
            Assert.That(preserved.Result.Streak, Is.EqualTo(4));
        }

        [Test]
        public void M08_007_DailyWeeklyTasksDeduplicateProgressResetAndClaimExactlyOnce()
        {
            var definitions = new[]
            {
                new TaskDefinition(
                    "daily-match", TaskPeriodKind.Daily, "match", 3),
                new TaskDefinition(
                    "weekly-win", TaskPeriodKind.Weekly, "win", 2)
            };

            var daily = new TaskPeriodSnapshot(
                TaskPeriodKind.Daily, "day:100", 100);
            var e1 = TaskProgressPlanner.ApplyEvent(
                daily, definitions,
                new TaskProgressEvent(
                    "event-1", "day:100", "match", 2));
            Assert.That(e1.Result.Counts["daily-match"], Is.EqualTo(2));

            var replay = TaskProgressPlanner.ApplyEvent(
                e1.Result, definitions,
                new TaskProgressEvent(
                    "event-1", "day:100", "match", 2));
            Assert.That(replay.Applied, Is.False);

            Assert.Throws<InvalidOperationException>(() =>
                TaskProgressPlanner.Claim(
                    e1.Result, definitions, "daily-match"));

            var e2 = TaskProgressPlanner.ApplyEvent(
                e1.Result, definitions,
                new TaskProgressEvent(
                    "event-2", "day:100", "match", 1));
            var claim = TaskProgressPlanner.Claim(
                e2.Result, definitions, "daily-match");
            Assert.That(claim.Applied, Is.True);
            Assert.That(claim.RewardSourceId,
                Is.EqualTo("task:day:100:daily-match"));

            var claimReplay = TaskProgressPlanner.Claim(
                claim.Result, definitions, "daily-match");
            Assert.That(claimReplay.Applied, Is.False);

            var reset = TaskProgressPlanner.Reset(
                claim.Result, "day:101", 101);
            Assert.That(reset.Result.Counts, Is.Empty);
            Assert.That(reset.Result.ClaimedTaskIds, Is.Empty);
            Assert.That(reset.Result.PeriodOrdinal, Is.EqualTo(101));

            Assert.Throws<InvalidOperationException>(() =>
                TaskProgressPlanner.Reset(
                    reset.Result, "day:099", 99));
            Assert.Throws<InvalidOperationException>(() =>
                TaskProgressPlanner.Reset(
                    reset.Result, "day:101-other", 101));

            Assert.Throws<InvalidOperationException>(() =>
                TaskProgressPlanner.ApplyEvent(
                    reset.Result, definitions,
                    new TaskProgressEvent(
                        "wrong-period", "day:100", "match", 1)));

            var weekly = new TaskPeriodSnapshot(
                TaskPeriodKind.Weekly, "week:42", 42);
            weekly = TaskProgressPlanner.ApplyEvent(
                weekly, definitions,
                new TaskProgressEvent(
                    "weekly-1", "week:42", "win", 2)).Result;
            Assert.That(TaskProgressPlanner.Claim(
                weekly, definitions, "weekly-win").RewardSourceId,
                Is.EqualTo("task:week:42:weekly-win"));

            Assert.Throws<ArgumentException>(() =>
                TaskProgressPlanner.ApplyEvent(
                    daily,
                    new TaskDefinition[]
                    {
                        new TaskDefinition(
                            "valid", TaskPeriodKind.Daily, "match", 1),
                        null
                    },
                    new TaskProgressEvent(
                        "null-def", "day:100", "match", 1)));

            Assert.Throws<ArgumentException>(() =>
                TaskProgressPlanner.ApplyEvent(
                    daily,
                    new[]
                    {
                        new TaskDefinition(
                            "same-id", TaskPeriodKind.Daily, "match", 1),
                        new TaskDefinition(
                            "same-id", TaskPeriodKind.Weekly, "win", 1)
                    },
                    new TaskProgressEvent(
                        "dup-def", "day:100", "match", 1)));
        }

        [Test]
        public void M08_008_RestorationMilestonesUseNeutralProgressAndThemeBindingsOnly()
        {
            var milestones = new[]
            {
                new RestorationMilestone(
                    "reef-1", 10, ThemeBindings("restore.one")),
                new RestorationMilestone(
                    "reef-2", 20, ThemeBindings("restore.two"))
            };
            var start = new RestorationSnapshot();

            var first = RestorationPlanner.ApplyProgress(
                start, milestones, "restore-event-1", 15);
            Assert.That(first.Result.Progress, Is.EqualTo(15));
            Assert.That(first.NewlyCompletedMilestoneIds,
                Is.EqualTo(new[] { "reef-1" }));

            var replay = RestorationPlanner.ApplyProgress(
                first.Result, milestones, "restore-event-1", 15);
            Assert.That(replay.Applied, Is.False);

            var second = RestorationPlanner.ApplyProgress(
                first.Result, milestones, "restore-event-2", 5);
            Assert.That(second.NewlyCompletedMilestoneIds,
                Is.EqualTo(new[] { "reef-2" }));

            foreach (var gameId in GameProfileCatalog.Ids)
            {
                Assert.That(milestones[0].PresentationKeys[gameId],
                    Is.EqualTo(gameId + ".restore.one"));
                Assert.That(milestones[0].RequiredProgress, Is.EqualTo(10));
            }

            Assert.Throws<ArgumentException>(() =>
                new RestorationMilestone(
                    "bad", 1,
                    ThemeBindings("x").Take(4)));

            Assert.Throws<ArgumentException>(() =>
                RestorationPlanner.ApplyProgress(
                    new RestorationSnapshot(
                        0,
                        completedMilestoneIds: new[] { "reef-1" }),
                    milestones, "bad-state", 1));
        }

        [Test]
        public void M08_009_SaveMigrationBacksUpHashesSequentiallyAndFailsAtomically()
        {
            var original = new SaveEnvelope(1, "v1:data");
            var steps = new[]
            {
                new SaveMigrationStep(
                    "1-to-2", 1, 2, payload => payload + "|v2"),
                new SaveMigrationStep(
                    "2-to-3", 2, 3, payload => payload + "|v3")
            };

            var plan = SaveMigrationPlanner.Migrate(
                original, 3, steps);
            Assert.That(plan.Applied, Is.True);
            Assert.That(plan.Backup, Is.SameAs(original));
            Assert.That(plan.BackupSha256,
                Is.EqualTo(SaveMigrationPlanner.HashEnvelope(original)));
            Assert.That(plan.Result.SchemaVersion, Is.EqualTo(3));
            Assert.That(plan.Result.Payload, Is.EqualTo("v1:data|v2|v3"));
            Assert.That(plan.AppliedStepIds,
                Is.EqualTo(new[] { "1-to-2", "2-to-3" }));
            Assert.That(original.SchemaVersion, Is.EqualTo(1));
            Assert.That(original.Payload, Is.EqualTo("v1:data"));

            var noOp = SaveMigrationPlanner.Migrate(
                plan.Result, 3, steps);
            Assert.That(noOp.Applied, Is.False);
            Assert.That(noOp.Result, Is.SameAs(plan.Result));

            Assert.Throws<NotSupportedException>(() =>
                SaveMigrationPlanner.Migrate(
                    new SaveEnvelope(4, "future"), 3, steps));

            Assert.Throws<InvalidOperationException>(() =>
                SaveMigrationPlanner.Migrate(
                    original, 3,
                    new[]
                    {
                        new SaveMigrationStep(
                            "1-to-2", 1, 2, x => x + "|v2")
                    }));

            var flip = false;
            Assert.Throws<InvalidOperationException>(() =>
                SaveMigrationPlanner.Migrate(
                    original, 2,
                    new[]
                    {
                        new SaveMigrationStep(
                            "nondeterministic", 1, 2,
                            x =>
                            {
                                flip = !flip;
                                return x + (flip ? "|a" : "|b");
                            })
                    }));
            Assert.That(original.Payload, Is.EqualTo("v1:data"));

            Assert.Throws<InvalidOperationException>(() =>
                SaveMigrationPlanner.Migrate(
                    original, 2,
                    new[]
                    {
                        new SaveMigrationStep(
                            "throws", 1, 2,
                            x => throw new InvalidOperationException("boom"))
                    }));
            Assert.That(original.SchemaVersion, Is.EqualTo(1));
        }

        [Test]
        public void M08_010_PrivacyIsOptInChildSafeReplaySafeAndNeverBlocksLocalPlay()
        {
            var profiles = GameProfileCatalog.LoadPlaceholders();
            var profile = profiles[0];
            var adultPolicy = new PrivacyProfilePolicy(
                profile, false);
            var start = new PrivacySnapshot(profile.Id);

            Assert.That(start.Consent, Is.EqualTo(AnalyticsConsent.Unknown));
            Assert.That(start.AnalyticsEnabled, Is.False);
            Assert.That(start.SocialEnabled, Is.False);
            Assert.That(start.LocalPlayAllowed, Is.True);

            var granted = PrivacyPlanner.ApplyConsent(
                adultPolicy, start, "consent-1",
                AnalyticsConsent.Granted, true);
            Assert.That(granted.Result.AnalyticsEnabled, Is.True);
            Assert.That(granted.Result.SocialEnabled, Is.True);
            Assert.That(granted.Result.LocalPlayAllowed, Is.True);

            var replay = PrivacyPlanner.ApplyConsent(
                adultPolicy, granted.Result, "consent-1",
                AnalyticsConsent.Granted, true);
            Assert.That(replay.Applied, Is.False);

            Assert.Throws<InvalidOperationException>(() =>
                PrivacyPlanner.ApplyConsent(
                    adultPolicy, granted.Result, "consent-1",
                    AnalyticsConsent.Denied, false));

            var denied = PrivacyPlanner.ApplyConsent(
                adultPolicy, granted.Result, "consent-2",
                AnalyticsConsent.Denied, false);
            Assert.That(denied.Result.AnalyticsEnabled, Is.False);
            Assert.That(denied.Result.SocialEnabled, Is.False);
            Assert.That(denied.Result.LocalPlayAllowed, Is.True);

            var childPolicy = new PrivacyProfilePolicy(
                profile, true);
            var child = PrivacyPlanner.ApplyConsent(
                childPolicy,
                new PrivacySnapshot(profile.Id),
                "child-consent",
                AnalyticsConsent.Granted,
                true);
            Assert.That(child.Result.AnalyticsEnabled, Is.False);
            Assert.That(child.Result.SocialEnabled, Is.False);
            Assert.That(child.Result.LocalPlayAllowed, Is.True);

            Assert.Throws<InvalidOperationException>(() =>
                PrivacyPlanner.ApplyConsent(
                    adultPolicy,
                    new PrivacySnapshot(profiles[1].Id),
                    "cross-game",
                    AnalyticsConsent.Granted,
                    false));
        }
    }
}
