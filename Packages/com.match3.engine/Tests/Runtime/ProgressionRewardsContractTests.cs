using System;
using Match3.Engine.Runtime;
using NUnit.Framework;

namespace Match3.Engine.Tests
{
    public sealed class ProgressionRewardsContractTests
    {
        private static VerifiedProgressionResult Result(
            string id, bool won, bool firstClear, bool replay,
            string level = "L1", string frontier = "L1",
            int score = 1000, int stars = 3, int attempt = 1,
            long day = 100, string week = "2026-W42",
            string game = "Coral_Rescue", string profile = "P1",
            string mode = "campaign", bool verified = true)
        {
            return new VerifiedProgressionResult(
                id, game, profile, mode, level, frontier,
                won, firstClear, replay, score,
                won ? stars : 0, attempt, day, week, verified);
        }

        [Test]
        public void M08_011_WinStreakUsesFrontierFirstClearOnlyAndIsReplayLossShieldRollbackSafe()
        {
            var policy = new WinStreakPolicy(10, 5, 2);
            var start = new WinStreakSnapshot(
                "Coral_Rescue", "P1", "campaign");

            var win = WinStreakPlanner.ApplyResult(
                start, Result("r1", true, true, false), policy);
            Assert.That(win.Result.Streak, Is.EqualTo(1));
            Assert.That(win.BonusTier, Is.EqualTo(1));
            Assert.That(win.RewardSourceId,
                Is.EqualTo("streak:r1:tier:1"));

            var duplicate = WinStreakPlanner.ApplyResult(
                win.Result, Result("r1", true, true, false), policy);
            Assert.That(duplicate.Applied, Is.False);
            Assert.That(duplicate.Result.Streak, Is.EqualTo(1));

            var replay = WinStreakPlanner.ApplyResult(
                win.Result,
                Result("r2", true, false, true,
                    score: 1500, stars: 3, attempt: 2),
                policy);
            Assert.That(replay.Result.Streak, Is.EqualTo(1));
            Assert.That(replay.RewardSourceId, Is.Null);

            var oldLevel = WinStreakPlanner.ApplyResult(
                replay.Result,
                Result("r3", true, false, false,
                    level: "L0", frontier: "L2",
                    attempt: 3),
                policy);
            Assert.That(oldLevel.Result.Streak, Is.EqualTo(1));

            var shielded = WinStreakPlanner.GrantShield(
                oldLevel.Result, "shield-1", 1, policy);
            Assert.That(shielded.Result.ShieldCharges, Is.EqualTo(1));

            var lossShield = WinStreakPlanner.ApplyResult(
                shielded.Result,
                Result("r4", false, false, false,
                    level: "L2", frontier: "L2",
                    score: 0, stars: 0, attempt: 4),
                policy, true);
            Assert.That(lossShield.Result.Streak, Is.EqualTo(1));
            Assert.That(lossShield.Result.ShieldCharges, Is.EqualTo(0));

            var loss = WinStreakPlanner.ApplyResult(
                lossShield.Result,
                Result("r5", false, false, false,
                    level: "L2", frontier: "L2",
                    score: 0, stars: 0, attempt: 5),
                policy);
            Assert.That(loss.Result.Streak, Is.EqualTo(0));

            var retry = WinStreakPlanner.RecordRetry(
                win.Result, "retry-1", 100, policy);
            Assert.That(retry.Result.Streak, Is.EqualTo(1));

            var reset = WinStreakPlanner.Reset(
                retry.Result, "reset-1", 101, policy);
            Assert.That(reset.Result.Streak, Is.EqualTo(0));

            Assert.Throws<InvalidOperationException>(() =>
                WinStreakPlanner.ApplyResult(
                    win.Result,
                    Result("cross", true, true, false,
                        game: "Garage_Legends"),
                    policy));

            Assert.Throws<InvalidOperationException>(() =>
                WinStreakPlanner.ApplyResult(
                    new WinStreakSnapshot(
                        "Coral_Rescue", "P1", "campaign",
                        1, 0, 200),
                    Result("rollback", true, true, false, day: 199),
                    policy));

            Assert.Throws<InvalidOperationException>(() =>
                WinStreakPlanner.ApplyResult(
                    start,
                    Result("forged", true, true, false, verified: false),
                    policy));
        }

        [Test]
        public void M08_012_StarChestAndGiftMetersRemainSeparateCapClaimOnceAndHonorExpiry()
        {
            var chest = new ProgressMeterDefinition(
                "star-chest", ProgressMeterKind.StarChest,
                10, 15, ProgressMeterExpiryPolicy.Never);
            var gift = new ProgressMeterDefinition(
                "gift", ProgressMeterKind.GiftMeter,
                5, 5, ProgressMeterExpiryPolicy.ResetOnPeriodChange);

            var chestStart = new ProgressMeterSnapshot(
                "star-chest", "season:1", 1);
            var chestAdd = ProgressMeterPlanner.Add(
                chest, chestStart, "stars-1",
                "season:1", 1, 12);
            Assert.That(chestAdd.Result.Progress, Is.EqualTo(12));
            Assert.That(chestAdd.Result.State,
                Is.EqualTo(ProgressMeterState.Claimable));

            var replay = ProgressMeterPlanner.Add(
                chest, chestAdd.Result, "stars-1",
                "season:1", 1, 12);
            Assert.That(replay.Applied, Is.False);

            var capped = ProgressMeterPlanner.Add(
                chest, chestAdd.Result, "stars-2",
                "season:1", 1, 100);
            Assert.That(capped.Result.Progress, Is.EqualTo(15));

            var claim = ProgressMeterPlanner.Claim(
                chest, capped.Result, "claim-1",
                "season:1", 1);
            Assert.That(claim.Result.State,
                Is.EqualTo(ProgressMeterState.Claimed));
            Assert.That(claim.RewardSourceId,
                Is.EqualTo("meter:star-chest:season:1:claim-1"));

            var claimReplay = ProgressMeterPlanner.Claim(
                chest, claim.Result, "claim-1",
                "season:1", 1);
            Assert.That(claimReplay.Applied, Is.False);

            var nextSeasonChest = ProgressMeterPlanner.Add(
                chest, claim.Result, "stars-3",
                "season:2", 2, 1);
            Assert.That(nextSeasonChest.Result.State,
                Is.EqualTo(ProgressMeterState.Claimed));
            Assert.That(nextSeasonChest.Result.Progress, Is.EqualTo(15));

            var giftStart = new ProgressMeterSnapshot(
                "gift", "day:1", 1);
            var giftReady = ProgressMeterPlanner.Add(
                gift, giftStart, "gift-progress",
                "day:1", 1, 5);
            Assert.That(giftReady.Result.State,
                Is.EqualTo(ProgressMeterState.Claimable));

            var giftReset = ProgressMeterPlanner.Add(
                gift, giftReady.Result, "gift-next",
                "day:2", 2, 1);
            Assert.That(giftReset.Result.Progress, Is.EqualTo(1));
            Assert.That(giftReset.Result.State,
                Is.EqualTo(ProgressMeterState.Locked));

            Assert.Throws<InvalidOperationException>(() =>
                ProgressMeterPlanner.Add(
                    gift, giftReset.Result, "rollback",
                    "day:1", 1, 1));

            Assert.Throws<InvalidOperationException>(() =>
                ProgressMeterPlanner.Add(
                    chest,
                    new ProgressMeterSnapshot(
                        "star-chest", "season:1", 1,
                        3, ProgressMeterState.Claimable),
                    "bad-state", "season:1", 1, 1));
        }

        [Test]
        public void M08_018_UnifiedRewardBoundaryIsAtomicGameScopedAndReplaySafeAcrossSources()
        {
            var snapshot = new UnifiedRewardSnapshot();
            var request = new RewardGrantRequest(
                "firstclear:L1", "Coral_Rescue",
                RewardSourceKind.FirstClear,
                new[]
                {
                    new RewardResourceDelta("coins", 100),
                    new RewardResourceDelta("booster.hammer", 1)
                });

            var first = UnifiedRewardPlanner.Apply(snapshot, request);
            Assert.That(first.Applied, Is.True);
            Assert.That(first.Result.Economy.Balance(
                "Coral_Rescue", "coins"), Is.EqualTo(100));
            Assert.That(first.Result.Economy.Balance(
                "Coral_Rescue", "booster.hammer"), Is.EqualTo(1));

            var replay = UnifiedRewardPlanner.Apply(first.Result, request);
            Assert.That(replay.Applied, Is.False);
            Assert.That(replay.Result.Economy.Balance(
                "Coral_Rescue", "coins"), Is.EqualTo(100));

            Assert.Throws<InvalidOperationException>(() =>
                UnifiedRewardPlanner.Apply(
                    first.Result,
                    new RewardGrantRequest(
                        "firstclear:L1", "Garage_Legends",
                        RewardSourceKind.FirstClear,
                        new[]
                        {
                            new RewardResourceDelta("coins", 100)
                        })));

            var spend = UnifiedRewardPlanner.Apply(
                first.Result,
                new RewardGrantRequest(
                    "purchase:booster-1", "Coral_Rescue",
                    RewardSourceKind.Purchase,
                    new[]
                    {
                        new RewardResourceDelta("coins", -40),
                        new RewardResourceDelta("booster.rocket", 1)
                    }));
            Assert.That(spend.Result.Economy.Balance(
                "Coral_Rescue", "coins"), Is.EqualTo(60));
            Assert.That(spend.Result.Economy.Balance(
                "Coral_Rescue", "booster.rocket"), Is.EqualTo(1));

            Assert.Throws<InvalidOperationException>(() =>
                UnifiedRewardPlanner.Apply(
                    snapshot,
                    new RewardGrantRequest(
                        "overspend", "Coral_Rescue",
                        RewardSourceKind.Purchase,
                        new[]
                        {
                            new RewardResourceDelta("coins", -1)
                        })));

            foreach (RewardSourceKind kind in Enum.GetValues(
                typeof(RewardSourceKind)))
            {
                Assert.DoesNotThrow(() =>
                    new RewardGrantRequest(
                        "source:" + kind, "Coral_Rescue", kind,
                        new[] { new RewardResourceDelta("coins", 1) }));
            }
        }
    }
}
