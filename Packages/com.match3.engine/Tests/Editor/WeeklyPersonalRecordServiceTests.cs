using System;
using System.Linq;
using Match3.Engine.Runtime;
using Match3.Engine.Runtime.LiveOps;
using NUnit.Framework;
using R = Match3.Engine.Runtime.LiveOps.WeeklyRecordReasons;

namespace Match3.Engine.Tests.Editor
{
    // ENG-M20-012 weekly personal-record challenge.
    public sealed class WeeklyPersonalRecordServiceTests
    {
        private const string Game = "Coral_Rescue";
        private const string Profile = "p1";
        private const string Mode = "classic";
        private const string Level = "cr.level.0001";
        private const string W46 = "2026-W46";
        private const string W47 = "2026-W47";

        private static readonly DateTime W46Wed = Utc(2026, 11, 11, 12);
        private static readonly DateTime W46Fri = Utc(2026, 11, 13, 12);
        private static readonly DateTime W47Wed = Utc(2026, 11, 18, 12);

        private static DateTime Utc(int y, int m, int d, int h = 0, int min = 0, int s = 0) =>
            new DateTime(y, m, d, h, min, s, DateTimeKind.Utc);

        private static FeatureFlagRecord Flag(string game = Game, bool enabled = true, bool consent = true,
            LiveOpsFeature feature = LiveOpsFeature.WeeklyPersonalBest) =>
            FeatureFlagRecord.Create(game, feature, 1, enabled, consent);

        private static WeeklyPersonalRecordService Service(int cap = 3, int minImprovement = 1, string game = Game) =>
            new WeeklyPersonalRecordService(game, Profile,
                new WeeklyPersonalRecordPolicy(cap, minImprovement, "soft.coins", 25));

        private static VerifiedProgressionResult Result(string id, int score, string week = W46,
            string level = Level, string game = Game, string profile = Profile, bool won = true, bool verified = true) =>
            new VerifiedProgressionResult(id, game, profile, Mode, level, level,
                won, false, true, score, won ? 1 : 0, 1, 0, week, verified);

        private static WeeklyRecordOutcome Submit(WeeklyPersonalRecordService svc, VerifiedProgressionResult r,
            uint seed, DateTime completion, DateTime? now = null, FeatureFlagRecord? flag = null) =>
            svc.Submit(r, seed, completion, now ?? completion, flag ?? Flag(svc.GameId), FeatureFlagPolicy.Default);

        // ---- ISO week window ---------------------------------------------------------------

        // Expected values cross-checked with Python datetime.date.isocalendar().
        [TestCase(2026, 11, 11, "2026-W46")]
        [TestCase(2026, 1, 1, "2026-W01")]
        [TestCase(2025, 12, 29, "2026-W01")]
        [TestCase(2020, 12, 31, "2020-W53")]
        [TestCase(2021, 1, 3, "2020-W53")]
        [TestCase(2021, 1, 4, "2021-W01")]
        [TestCase(2027, 1, 1, "2026-W53")]
        [TestCase(2027, 1, 3, "2026-W53")]
        [TestCase(2027, 1, 4, "2027-W01")]
        [TestCase(2024, 12, 30, "2025-W01")]
        [TestCase(2016, 1, 3, "2015-W53")]
        public void WeekId_MatchesIso8601AcrossYearBoundaries(int y, int m, int d, string expected)
        {
            Assert.AreEqual(expected, IsoWeekWindow.WeekId(Utc(y, m, d)));
            Assert.AreEqual(expected, IsoWeekWindow.WeekId(Utc(y, m, d, 23, 59, 59)));
        }

        [Test]
        public void WeekWindow_StartsMondayMidnightUtc_AndRollsOverExactly()
        {
            Assert.AreEqual(Utc(2026, 11, 9), IsoWeekWindow.StartUtc(W46));
            Assert.AreEqual(Utc(2026, 11, 16), IsoWeekWindow.EndUtc(W46));
            Assert.AreEqual(DateTimeKind.Utc, IsoWeekWindow.StartUtc(W46).Kind);
            Assert.AreEqual(W46, IsoWeekWindow.WeekId(Utc(2026, 11, 15, 23, 59, 59).AddTicks(9999999)));
            Assert.AreEqual(W47, IsoWeekWindow.WeekId(Utc(2026, 11, 16)));
            Assert.AreEqual(Utc(2026, 12, 28), IsoWeekWindow.StartUtc("2026-W53"));
            Assert.AreEqual(Utc(2027, 1, 4), IsoWeekWindow.EndUtc("2026-W53"));
        }

        [Test]
        public void WeekWindow_EveryDay2000To2040_RoundTripsAndContainsTheDay()
        {
            string previous = null;
            for (var day = Utc(2000, 1, 1); day < Utc(2041, 1, 1); day = day.AddDays(1))
            {
                var id = IsoWeekWindow.WeekId(day);
                var start = IsoWeekWindow.StartUtc(id);
                Assert.AreEqual(DayOfWeek.Monday, start.DayOfWeek, id);
                Assert.That(day >= start && day < start.AddDays(7), id);
                if (previous != null)
                    Assert.That(string.CompareOrdinal(id, previous) >= 0, "week IDs must sort chronologically");
                previous = id;
            }
        }

        [TestCase("2025-W53")] // 2025 has 52 ISO weeks
        [TestCase("2026-W00")]
        [TestCase("2026-W54")]
        [TestCase("2026-w46")]
        [TestCase("2026W46")]
        [TestCase("2026-W4")]
        [TestCase("+026-W46")]
        [TestCase("")]
        [TestCase(null)]
        public void WeekWindow_InvalidIds_Rejected(string id)
        {
            Assert.IsFalse(IsoWeekWindow.TryGetStartUtc(id, out _));
            Assert.Throws<FormatException>(() => IsoWeekWindow.StartUtc(id));
        }

        [TestCase(DateTimeKind.Local)]
        [TestCase(DateTimeKind.Unspecified)]
        public void WeekId_NonUtcKind_Throws(DateTimeKind kind)
        {
            Assert.Throws<ArgumentException>(() => IsoWeekWindow.WeekId(new DateTime(2026, 11, 11, 12, 0, 0, kind)));
        }

        [Test]
        public void WeekId_IgnoresDst_EuAndUsClockChangesStayInOneUtcWindow()
        {
            // EU DST ended 2026-10-25 01:00 UTC; US DST ended 2026-11-01 06:00 UTC.
            Assert.AreEqual("2026-W43", IsoWeekWindow.WeekId(Utc(2026, 10, 25, 0, 30)));
            Assert.AreEqual("2026-W43", IsoWeekWindow.WeekId(Utc(2026, 10, 25, 3, 30)));
            Assert.AreEqual("2026-W44", IsoWeekWindow.WeekId(Utc(2026, 10, 26)));
            Assert.AreEqual("2026-W44", IsoWeekWindow.WeekId(Utc(2026, 11, 1, 5, 59)));
            Assert.AreEqual("2026-W44", IsoWeekWindow.WeekId(Utc(2026, 11, 1, 6, 1)));
            Assert.AreEqual(TimeSpan.FromDays(7),
                IsoWeekWindow.EndUtc("2026-W43") - IsoWeekWindow.StartUtc("2026-W43"));
        }

        [TestCase(DateTimeKind.Local, true)]
        [TestCase(DateTimeKind.Unspecified, true)]
        [TestCase(DateTimeKind.Local, false)]
        public void Submit_NonUtcInstant_Rejected(DateTimeKind kind, bool completionSide)
        {
            var svc = Service();
            var nonUtc = new DateTime(2026, 11, 11, 12, 0, 0, kind);
            var o = svc.Submit(Result("r1", 100), 1, completionSide ? nonUtc : W46Wed,
                completionSide ? W46Wed : nonUtc, Flag(), FeatureFlagPolicy.Default);
            Assert.AreEqual(WeeklyRecordStatus.Rejected, o.Status);
            Assert.AreEqual(R.NonUtcInstant, o.Reason);
            Assert.IsNull(svc.GetView(Mode, Level, W46));
        }

        // ---- participation, record view, reward -------------------------------------------

        [Test]
        public void FirstVerifiedCompletion_IsParticipation_WithoutReward()
        {
            var svc = Service();
            var o = Submit(svc, Result("r1", 1200), 11, W46Wed);
            Assert.AreEqual(WeeklyRecordStatus.Participated, o.Status);
            Assert.IsNull(o.Grant);
            Assert.IsNull(o.View.PreviousBest);
            Assert.AreEqual(1200, o.View.CurrentBest);
            Assert.AreEqual(0, o.View.ImprovementDelta);
            Assert.AreEqual(W46, o.View.WeekId);
        }

        [Test]
        public void Improvement_ShowsPreviousVsNew_AndEmitsKeyedGrant()
        {
            var svc = Service();
            Submit(svc, Result("r1", 1200), 11, W46Wed);
            var o = Submit(svc, Result("r2", 1450), 12, W46Fri);

            Assert.AreEqual(WeeklyRecordStatus.Improved, o.Status);
            Assert.AreEqual(1200, o.View.PreviousBest);
            Assert.AreEqual(1450, o.View.CurrentBest);
            Assert.AreEqual(250, o.View.ImprovementDelta);
            Assert.IsNotNull(o.Grant);
            Assert.AreEqual("weeklyPB|Coral_Rescue|p1|2026-W46|classic|cr.level.0001|t1", o.Grant.IdempotencyKey);
            Assert.AreEqual(1, o.Grant.Tier);
            Assert.AreEqual("soft.coins", o.Grant.RewardId);
            Assert.AreEqual(25, o.Grant.Amount);
            var stored = svc.GetView(Mode, Level, W46);
            Assert.AreEqual(1200, stored.PreviousBest);
            Assert.AreEqual(1450, stored.CurrentBest);
        }

        [Test]
        public void LowerOrEqualScore_DoesNotRegressRecord()
        {
            var svc = Service();
            Submit(svc, Result("r1", 1200), 11, W46Wed);
            foreach (var (id, score, seed) in new[] { ("r2", 900, 12u), ("r3", 1200, 13u) })
            {
                var o = Submit(svc, Result(id, score), seed, W46Fri);
                Assert.AreEqual(WeeklyRecordStatus.NotImproved, o.Status);
                Assert.IsNull(o.Grant);
                Assert.AreEqual(1200, o.View.CurrentBest);
            }
        }

        [Test]
        public void RecordsAreIsolatedPerModeLevelAndWeek()
        {
            var svc = Service();
            Submit(svc, Result("r1", 500), 1, W46Wed);
            var other = Submit(svc, Result("r2", 100, level: "cr.level.0002"), 2, W46Wed);
            Assert.AreEqual(WeeklyRecordStatus.Participated, other.Status);
            Assert.AreEqual(500, svc.GetView(Mode, Level, W46).CurrentBest);
            Assert.IsNull(svc.GetView("timed", Level, W46));
            Assert.IsNull(svc.GetView(Mode, Level, W47));
        }

        // ---- replay / duplicate / exactly-once --------------------------------------------

        [Test]
        public void DuplicateResultId_IsIdempotent_NoSecondGrant()
        {
            var svc = Service();
            Submit(svc, Result("r1", 100), 1, W46Wed);
            var first = Submit(svc, Result("r2", 300), 2, W46Wed);
            var again = Submit(svc, Result("r2", 300), 2, W46Fri);

            Assert.IsNotNull(first.Grant);
            Assert.AreEqual(WeeklyRecordStatus.Duplicate, again.Status);
            Assert.AreEqual(R.DuplicateResult, again.Reason);
            Assert.IsNull(again.Grant);
            Assert.AreEqual(300, again.View.CurrentBest);
            Assert.AreEqual(100, again.View.PreviousBest);
        }

        [Test]
        public void ReplayedRunSeed_CannotImproveRecord_EvenWithForgedHigherScore()
        {
            var svc = Service();
            Submit(svc, Result("r1", 100), 77, W46Wed);
            var o = Submit(svc, Result("r1-replay", 99999), 77, W46Fri);

            Assert.AreEqual(WeeklyRecordStatus.ReplayRejected, o.Status);
            Assert.AreEqual(R.RunSeedReplayed, o.Reason);
            Assert.IsNull(o.Grant);
            Assert.AreEqual(100, svc.GetView(Mode, Level, W46).CurrentBest);
        }

        [Test]
        public void ReplayedRunSeed_FromPreviousWeek_CannotSeedNewWeek()
        {
            var svc = Service();
            Submit(svc, Result("r1", 800), 77, W46Wed);
            var o = Submit(svc, Result("r1-again", 800, W47), 77, W47Wed);
            Assert.AreEqual(WeeklyRecordStatus.ReplayRejected, o.Status);
            Assert.IsNull(svc.GetView(Mode, Level, W47));
        }

        [Test]
        public void GrantKeys_AreDeterministicAcrossServiceRestarts()
        {
            string Run()
            {
                var svc = Service();
                Submit(svc, Result("r1", 100), 1, W46Wed);
                return Submit(svc, Result("r2", 200), 2, W46Wed).Grant.IdempotencyKey;
            }
            Assert.AreEqual(Run(), Run());
            Assert.AreEqual(Run(), WeeklyPersonalRecordService.GrantKey(Game, Profile, W46, Mode, Level, 1));
        }

        [Test]
        public void ImprovementRewards_CappedPerWindow_RecordStillUpdates()
        {
            var svc = Service(cap: 2);
            Submit(svc, Result("r0", 100), 100, W46Wed);
            var keys = new[] { 200, 300, 400 }
                .Select((score, i) => Submit(svc, Result("r" + (i + 1), score), (uint)(101 + i), W46Wed))
                .ToList();

            Assert.AreEqual(new[] { "t1", "t2" }, keys.Take(2).Select(o => o.Grant.IdempotencyKey.Split('|').Last()));
            Assert.AreEqual(WeeklyRecordStatus.Improved, keys[2].Status);
            Assert.AreEqual(R.WindowCapReached, keys[2].Reason);
            Assert.IsNull(keys[2].Grant);
            Assert.AreEqual(400, svc.GetView(Mode, Level, W46).CurrentBest);

            // Cap is per player per window, across levels.
            Submit(svc, Result("x0", 10, level: "cr.level.0002"), 200, W46Wed);
            var other = Submit(svc, Result("x1", 50, level: "cr.level.0002"), 201, W46Wed);
            Assert.AreEqual(R.WindowCapReached, other.Reason);
            Assert.IsNull(other.Grant);
        }

        [Test]
        public void ZeroCapPolicy_NeverGrants()
        {
            var svc = Service(cap: 0);
            Submit(svc, Result("r1", 100), 1, W46Wed);
            var o = Submit(svc, Result("r2", 500), 2, W46Wed);
            Assert.AreEqual(R.WindowCapReached, o.Reason);
            Assert.IsNull(o.Grant);
        }

        [Test]
        public void ImprovementBelowMinimum_UpdatesRecord_WithoutGrant()
        {
            var svc = Service(minImprovement: 50);
            Submit(svc, Result("r1", 100), 1, W46Wed);
            var small = Submit(svc, Result("r2", 149), 2, W46Wed);
            Assert.AreEqual(WeeklyRecordStatus.Improved, small.Status);
            Assert.AreEqual(R.BelowMinimumImprovement, small.Reason);
            Assert.IsNull(small.Grant);
            var big = Submit(svc, Result("r3", 199), 3, W46Wed);
            Assert.AreEqual(1, big.Grant.Tier);
            Assert.AreEqual(149, big.View.PreviousBest);
        }

        // ---- rollover and offline ---------------------------------------------------------

        [Test]
        public void Rollover_NewWeekStartsFreshRecordAndFreshCap()
        {
            var svc = Service(cap: 1);
            Submit(svc, Result("a1", 500), 1, W46Wed);
            Assert.IsNotNull(Submit(svc, Result("a2", 900), 2, W46Wed).Grant);
            Assert.AreEqual(R.WindowCapReached, Submit(svc, Result("a3", 950), 3, W46Wed).Reason);

            var fresh = Submit(svc, Result("b1", 100, W47), 4, W47Wed);
            Assert.AreEqual(WeeklyRecordStatus.Participated, fresh.Status);
            Assert.IsNull(fresh.View.PreviousBest);
            var improved = Submit(svc, Result("b2", 200, W47), 5, W47Wed);
            Assert.AreEqual("weeklyPB|Coral_Rescue|p1|2026-W47|classic|cr.level.0001|t1", improved.Grant.IdempotencyKey);
            Assert.AreEqual(950, svc.GetView(Mode, Level, W46).CurrentBest);
        }

        [Test]
        public void OfflineResult_SyncedInsideItsWindow_IsAcceptedAndGranted()
        {
            var svc = Service();
            Submit(svc, Result("r1", 100), 1, W46Wed);
            var o = Submit(svc, Result("offline", 300), 2, W46Wed, now: W46Fri);
            Assert.AreEqual(WeeklyRecordStatus.Improved, o.Status);
            Assert.IsNotNull(o.Grant);
            Assert.AreEqual(W46, o.Grant.WeekId);
        }

        [Test]
        public void OfflineLateArrival_AfterRollover_GoesToOwnWeek_NoRetroactiveGrant()
        {
            var svc = Service();
            Submit(svc, Result("r1", 100), 1, W46Wed);
            var late = Submit(svc, Result("offline", 300), 2, W46Fri, now: W47Wed);

            Assert.AreEqual(WeeklyRecordStatus.Improved, late.Status);
            Assert.AreEqual(R.WindowClosed, late.Reason);
            Assert.IsNull(late.Grant);
            Assert.AreEqual(W46, late.View.WeekId);
            Assert.AreEqual(300, svc.GetView(Mode, Level, W46).CurrentBest);
            Assert.IsNull(svc.GetView(Mode, Level, W47));

            // The late W46 tier cannot be granted later either: re-sync is a duplicate.
            Assert.AreEqual(WeeklyRecordStatus.Duplicate, Submit(svc, Result("offline", 300), 2, W46Fri, now: W47Wed).Status);
        }

        [Test]
        public void OfflineResult_ClaimingCurrentWeekButCompletedEarlier_IsRejected()
        {
            var svc = Service();
            var o = Submit(svc, Result("offline", 300, W47), 2, W46Fri, now: W47Wed);
            Assert.AreEqual(WeeklyRecordStatus.Rejected, o.Status);
            Assert.AreEqual(R.WeekIdMismatch, o.Reason);
            Assert.IsNull(svc.GetView(Mode, Level, W47));
            Assert.IsNull(svc.GetView(Mode, Level, W46));
        }

        [Test]
        public void CompletionAfterServerNow_ClockForward_Rejected()
        {
            var svc = Service();
            var o = Submit(svc, Result("future", 300, W47), 2, W47Wed, now: W46Fri);
            Assert.AreEqual(R.CompletionInFuture, o.Reason);
        }

        // ---- verification and eligibility -------------------------------------------------

        [Test]
        public void UnverifiedLostOrForeignResults_Rejected()
        {
            var svc = Service();
            Assert.AreEqual(R.Unverified, Submit(svc, Result("u", 100, verified: false), 1, W46Wed).Reason);
            Assert.AreEqual(R.NotCompleted, Submit(svc, Result("l", 100, won: false), 2, W46Wed).Reason);
            Assert.AreEqual(R.GameOrProfileMismatch, Submit(svc, Result("p", 100, profile: "p2"), 3, W46Wed).Reason);
            Assert.AreEqual(R.GameOrProfileMismatch, Submit(svc, Result("g", 100, game: "Dino_Islands"), 4, W46Wed).Reason);
            Assert.IsNull(svc.GetView(Mode, Level, W46));
            // Rejected submissions do not consume the result ID or seed.
            Assert.AreEqual(WeeklyRecordStatus.Participated, Submit(svc, Result("u", 100), 1, W46Wed).Status);
        }

        [Test]
        public void ConsentOff_NotEligible_ForWonderPopEvenWithPermissivePolicy()
        {
            var svc = Service(game: "Wonder_Pop");
            var noConsent = svc.Submit(Result("r1", 100, game: "Wonder_Pop"), 1, W46Wed, W46Wed,
                Flag("Wonder_Pop", consent: false), FeatureFlagPolicy.Permissive("test"));
            Assert.AreEqual(WeeklyRecordStatus.NotEligible, noConsent.Status);
            Assert.AreEqual("RestrictedFeatureRequiresConsent", noConsent.Reason);
            Assert.IsNull(noConsent.View);
            Assert.IsNull(svc.GetView(Mode, Level, W46));

            var consented = svc.Submit(Result("r1", 100, game: "Wonder_Pop"), 1, W46Wed, W46Wed,
                Flag("Wonder_Pop", consent: true), FeatureFlagPolicy.Default);
            Assert.AreEqual(WeeklyRecordStatus.Participated, consented.Status);
        }

        [Test]
        public void FlagGate_DefaultPolicyNoConsent_DisabledWrongFeatureOrGame_NotEligible()
        {
            var svc = Service();
            var r = Result("r1", 100);
            Assert.AreEqual("RestrictedFeatureRequiresConsent",
                svc.Submit(r, 1, W46Wed, W46Wed, Flag(consent: false), FeatureFlagPolicy.Default).Reason);
            Assert.AreEqual("RestrictedFeatureRequiresConsent",
                svc.Submit(r, 1, W46Wed, W46Wed, Flag(consent: false), default(FeatureFlagPolicy)).Reason);
            Assert.AreEqual("FeatureFlagDisabled",
                svc.Submit(r, 1, W46Wed, W46Wed, Flag(enabled: false), FeatureFlagPolicy.Default).Reason);
            Assert.AreEqual(R.WrongFeatureFlag,
                svc.Submit(r, 1, W46Wed, W46Wed, Flag(feature: LiveOpsFeature.TimedMode), FeatureFlagPolicy.Default).Reason);
            Assert.AreEqual("GameIdMismatch",
                svc.Submit(r, 1, W46Wed, W46Wed, Flag("Wonder_Pop"), FeatureFlagPolicy.Default).Reason);
            Assert.IsNull(svc.GetView(Mode, Level, W46));
        }

        [Test]
        public void ViewModel_ExposesNoRankingOrOtherPlayerData()
        {
            var names = typeof(WeeklyRecordView).GetProperties().Select(p => p.Name.ToLowerInvariant()).ToList();
            CollectionAssert.AreEquivalent(
                new[] { "weekid", "modeid", "levelid", "previousbest", "currentbest", "improvementdelta" }, names);
        }

        [Test]
        public void Constructors_RejectInvalidArguments()
        {
            Assert.Throws<ArgumentException>(() => new WeeklyPersonalRecordService("NotAGame", Profile,
                new WeeklyPersonalRecordPolicy(1, 1, "soft.coins", 1)));
            Assert.Throws<ArgumentException>(() => new WeeklyPersonalRecordService(Game, "a|b",
                new WeeklyPersonalRecordPolicy(1, 1, "soft.coins", 1)));
            Assert.Throws<ArgumentNullException>(() => new WeeklyPersonalRecordService(Game, Profile, null));
            Assert.Throws<ArgumentOutOfRangeException>(() => new WeeklyPersonalRecordPolicy(-1, 1, "x", 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new WeeklyPersonalRecordPolicy(1, 0, "x", 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new WeeklyPersonalRecordPolicy(1, 1, "x", 0));
            Assert.Throws<ArgumentException>(() => new WeeklyPersonalRecordPolicy(1, 1, "a|b", 1));
            Assert.Throws<ArgumentNullException>(() => Service().Submit(null, 1, W46Wed, W46Wed, Flag(), FeatureFlagPolicy.Default));
        }
    }
}
