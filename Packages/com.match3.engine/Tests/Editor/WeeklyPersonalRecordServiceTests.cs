using System;
using System.Linq;
using Match3.Engine.Runtime;
using Match3.Engine.Runtime.Economy;
using Match3.Engine.Runtime.LiveOps;
using NUnit.Framework;
using R = Match3.Engine.Runtime.LiveOps.WeeklyRecordReasons;

namespace Match3.Engine.Tests.Editor
{
    // ENG-M20-012 weekly personal-record challenge (grants through the ENG-M16-011 RewardLedger).
    public sealed class WeeklyPersonalRecordServiceTests
    {
        private const string Game = "Coral_Rescue";
        private const string Profile = "p1";
        private const string Mode = "classic";
        private const string Level = "cr.level.0001";
        private const string W46 = "2026-W46";
        private const string W47 = "2026-W47";
        private const string W48 = "2026-W48";
        private static readonly string Config = new string('c', 64);
        private static readonly string Board = new string('b', 64);

        private static readonly DateTime W45Wed = Utc(2026, 11, 4, 12);
        private static readonly DateTime W46Wed = Utc(2026, 11, 11, 12);
        private static readonly DateTime W46Fri = Utc(2026, 11, 13, 12);
        private static readonly DateTime W47Wed = Utc(2026, 11, 18, 12);
        private static readonly DateTime W48Wed = Utc(2026, 11, 25, 12);

        private RewardLedger _ledger;
        private WeeklyPersonalRecordPolicy _policy;

        private static DateTime Utc(int y, int m, int d, int h = 0, int min = 0, int s = 0) =>
            new DateTime(y, m, d, h, min, s, DateTimeKind.Utc);

        private static FeatureFlagRecord Flag(string game = Game, bool enabled = true, bool consent = true,
            LiveOpsFeature feature = LiveOpsFeature.WeeklyPersonalBest) =>
            FeatureFlagRecord.Create(game, feature, 1, enabled, consent);

        private WeeklyPersonalRecordService Service(int cap = 3, int minImprovement = 1, string game = Game,
            int retainedPastWeeks = 1)
        {
            _ledger = new RewardLedger(game, Profile);
            _policy = new WeeklyPersonalRecordPolicy(cap, minImprovement, "Coins", 25, retainedPastWeeks);
            return new WeeklyPersonalRecordService(game, Profile, _policy, _ledger);
        }

        // Distinct (seed, steps) = distinct verified run; same pair = byte-identical replay trace.
        private static ReplayTrace Trace(uint seed, int steps = 1, string game = Game) =>
            new ReplayTrace(game, Config, seed, Enumerable.Range(1, steps)
                .Select(n => new ReplayStep(n, new BoardPosition(0, 0), new BoardPosition(0, 1), Board)));

        private static VerifiedProgressionResult Result(string id, int score, string week = W46,
            string level = Level, string game = Game, string profile = Profile, bool won = true, bool verified = true) =>
            new VerifiedProgressionResult(id, game, profile, Mode, level, level,
                won, false, true, score, won ? 1 : 0, 1, 0, week, verified);

        private static WeeklyRecordOutcome Submit(WeeklyPersonalRecordService svc, VerifiedProgressionResult r,
            uint seed, DateTime completion, DateTime? now = null, FeatureFlagRecord? flag = null, int steps = 1) =>
            svc.Submit(r, Trace(seed, steps, svc.GameId), completion, now ?? completion,
                flag ?? Flag(svc.GameId), FeatureFlagPolicy.Default);

        private static string Instance(string week, int slot) =>
            WeeklyPersonalRecordService.GrantInstanceId(week, slot);

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
            var o = svc.Submit(Result("r1", 100), Trace(1), completionSide ? nonUtc : W46Wed,
                completionSide ? W46Wed : nonUtc, Flag(), FeatureFlagPolicy.Default);
            Assert.AreEqual(WeeklyRecordStatus.Rejected, o.Status);
            Assert.AreEqual(R.NonUtcInstant, o.Reason);
            Assert.IsNull(svc.GetView(Mode, Level, W46));
        }

        // ---- participation, record view, ledger reward ------------------------------------

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
            Assert.AreEqual(0, _ledger.Count);
        }

        [Test]
        public void Improvement_ShowsPreviousVsNew_AndGrantsOnceThroughLedger()
        {
            var svc = Service();
            Submit(svc, Result("r1", 1200), 11, W46Wed);
            var o = Submit(svc, Result("r2", 1450), 12, W46Fri);

            Assert.AreEqual(WeeklyRecordStatus.Improved, o.Status);
            Assert.AreEqual(string.Empty, o.Reason);
            Assert.AreEqual(1200, o.View.PreviousBest);
            Assert.AreEqual(1450, o.View.CurrentBest);
            Assert.AreEqual(250, o.View.ImprovementDelta);
            Assert.AreEqual(RewardSources.WeeklyPersonalBest, o.Grant.Source);
            Assert.AreEqual("2026-W46|t1", o.Grant.SourceInstanceId);
            Assert.AreEqual(RewardLedgerKey.Derive(Game, Profile, RewardSources.WeeklyPersonalBest,
                "2026-W46|t1"), o.Grant.Key);
            Assert.AreEqual("r2", o.Grant.Reference);
            Assert.AreEqual(Utc(2026, 11, 16), o.Grant.ExpiresUtc);
            Assert.AreEqual(25, _ledger.Balance(CurrencyType.Coins));
            Assert.AreEqual(1, _ledger.Count);
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

        [Test]
        public void WonderPop_ConsentedNonPremiumReward_IsGrantedThroughFamilySafeLedger()
        {
            var svc = Service(game: "Wonder_Pop");
            Submit(svc, Result("r1", 100, game: "Wonder_Pop"), 1, W46Wed);
            var o = Submit(svc, Result("r2", 200, game: "Wonder_Pop"), 2, W46Wed);
            Assert.IsNotNull(o.Grant);
            Assert.AreEqual(25, _ledger.Balance(CurrencyType.Coins));
            Assert.AreEqual(0, _ledger.Balance(CurrencyType.PremiumGems));
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
            Assert.AreEqual(25, _ledger.Balance(CurrencyType.Coins));
        }

        [Test]
        public void ReplayedTrace_UnderNewResultId_CannotImproveRecord()
        {
            var svc = Service();
            Submit(svc, Result("r1", 100), 77, W46Wed);
            var o = Submit(svc, Result("r1-replay", 99999), 77, W46Fri);

            Assert.AreEqual(WeeklyRecordStatus.ReplayRejected, o.Status);
            Assert.AreEqual(R.TraceReplayed, o.Reason);
            Assert.IsNull(o.Grant);
            Assert.AreEqual(100, svc.GetView(Mode, Level, W46).CurrentBest);
            Assert.AreEqual(0, _ledger.Count);
        }

        [Test]
        public void FixedSeedLevel_DifferentMoves_AllowsGenuineImprovement_IdenticalTraceStillRejected()
        {
            var svc = Service();
            Submit(svc, Result("a", 100), 7, W46Wed, steps: 1);
            var better = Submit(svc, Result("b", 300), 7, W46Wed, steps: 2);
            Assert.AreEqual(WeeklyRecordStatus.Improved, better.Status);
            Assert.IsNotNull(better.Grant);

            // Next week, same fixed seed: new moves participate, an identical earlier trace is a replay.
            Assert.AreEqual(WeeklyRecordStatus.Participated,
                Submit(svc, Result("c", 50, W47), 7, W47Wed, steps: 3).Status);
            Assert.AreEqual(WeeklyRecordStatus.ReplayRejected,
                Submit(svc, Result("d", 500, W47), 7, W47Wed, steps: 2).Status);
        }

        [Test]
        public void TraceFromAnotherGame_Rejected()
        {
            var svc = Service();
            var o = svc.Submit(Result("r1", 100), Trace(1, 1, "Dino_Islands"), W46Wed, W46Wed, Flag(),
                FeatureFlagPolicy.Default);
            Assert.AreEqual(R.TraceGameMismatch, o.Reason);
            Assert.IsNull(svc.GetView(Mode, Level, W46));
        }

        [Test]
        public void ImprovementRewards_CappedPerWindow_RecordStillUpdates()
        {
            var svc = Service(cap: 2);
            Submit(svc, Result("r0", 100), 100, W46Wed);
            var outcomes = new[] { 200, 300, 400 }
                .Select((score, i) => Submit(svc, Result("r" + (i + 1), score), (uint)(101 + i), W46Wed))
                .ToList();

            Assert.AreEqual(new[] { Instance(W46, 1), Instance(W46, 2) },
                outcomes.Take(2).Select(o => o.Grant.SourceInstanceId));
            Assert.AreEqual(WeeklyRecordStatus.Improved, outcomes[2].Status);
            Assert.AreEqual(R.WindowCapReached, outcomes[2].Reason);
            Assert.IsNull(outcomes[2].Grant);
            Assert.AreEqual(400, svc.GetView(Mode, Level, W46).CurrentBest);

            // Cap is per player per window, across levels.
            Submit(svc, Result("x0", 10, level: "cr.level.0002"), 200, W46Wed);
            var other = Submit(svc, Result("x1", 50, level: "cr.level.0002"), 201, W46Wed);
            Assert.AreEqual(R.WindowCapReached, other.Reason);
            Assert.IsNull(other.Grant);
            Assert.AreEqual(50, _ledger.Balance(CurrencyType.Coins));
        }

        [Test]
        public void ZeroCapPolicy_NeverGrants()
        {
            var svc = Service(cap: 0);
            Submit(svc, Result("r1", 100), 1, W46Wed);
            var o = Submit(svc, Result("r2", 500), 2, W46Wed);
            Assert.AreEqual(R.WindowCapReached, o.Reason);
            Assert.IsNull(o.Grant);
            Assert.AreEqual(0, _ledger.Count);
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
            Assert.AreEqual(Instance(W46, 1), big.Grant.SourceInstanceId);
            Assert.AreEqual(149, big.View.PreviousBest);
        }

        // Accepted farming bound: one grant per +MinImprovement step, at most MaxImprovementRewardsPerWindow
        // grants per player per week whatever the number of runs or levels.
        [Test]
        public void SmallStepFarming_IsBoundedByMinImprovementAndWindowCap()
        {
            var svc = Service(cap: 3, minImprovement: 50);
            uint seed = 1;
            foreach (var level in new[] { "cr.level.0001", "cr.level.0002" })
                for (var score = 1000; score <= 1150; score++)
                    Submit(svc, Result(level + "#" + score, score, level: level), seed++, W46Wed);
            Assert.AreEqual(0, _ledger.Count, "+1 steps never reach MinImprovement");

            var grants = 0;
            for (var i = 1; i <= 20; i++)
                if (Submit(svc, Result("big" + i, 1150 + 50 * i), seed++, W46Wed).Grant != null) grants++;
            Assert.AreEqual(3, grants);
            Assert.AreEqual(75, _ledger.Balance(CurrencyType.Coins));
            Assert.AreEqual(2150, svc.GetView(Mode, Level, W46).CurrentBest);
        }

        // ---- rollover, offline, retention -------------------------------------------------

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
            Assert.AreEqual(Instance(W47, 1), improved.Grant.SourceInstanceId);
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
            Assert.AreEqual(Instance(W46, 1), o.Grant.SourceInstanceId);
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
            Assert.AreEqual(0, _ledger.Count);
            Assert.AreEqual(WeeklyRecordStatus.Duplicate, Submit(svc, Result("offline", 300), 2, W46Fri, now: W47Wed).Status);
        }

        [Test]
        public void OfflineResult_OlderThanRetention_Rejected()
        {
            var svc = Service(retainedPastWeeks: 1);
            var o = Submit(svc, Result("old", 300, "2026-W45"), 2, W45Wed, now: W47Wed);
            Assert.AreEqual(R.WeekBeyondRetention, o.Reason);
            Assert.IsNull(svc.GetView(Mode, Level, "2026-W45"));
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

        [Test]
        public void Prune_DropsOldWeeks_AndCannotReenableResultsOrGrants()
        {
            var svc = Service(retainedPastWeeks: 1);
            Submit(svc, Result("r1", 100), 1, W46Wed);
            Submit(svc, Result("r2", 300), 2, W46Wed);
            Submit(svc, Result("n1", 100, W48), 3, W48Wed);
            var before = svc.ExportSnapshot();

            Assert.AreEqual(0, svc.Prune(W47Wed), "W46 is inside retention at W47");
            var sequence = svc.Sequence;
            Assert.AreEqual(5, svc.Prune(W48Wed), "W46: 1 record + 2 result IDs + 2 replay keys");
            Assert.That(svc.Sequence, Is.GreaterThan(sequence));
            Assert.IsNull(svc.GetView(Mode, Level, W46));
            Assert.AreEqual(100, svc.GetView(Mode, Level, "2026-W48").CurrentBest);
            Assert.That(svc.ExportSnapshot().Length, Is.LessThan(before.Length));
            Assert.That(svc.ExportSnapshot(), Does.Not.Contain(W46));

            // Pruned result IDs and traces cannot come back: their week is now beyond retention.
            Assert.AreEqual(R.WeekBeyondRetention, Submit(svc, Result("r2", 300), 2, W46Wed, now: W48Wed).Reason);
            Assert.AreEqual(R.WeekBeyondRetention, Submit(svc, Result("r9", 999), 9, W46Wed, now: W48Wed).Reason);
            Assert.AreEqual(25, _ledger.Balance(CurrencyType.Coins));
        }

        // ---- durability (snapshot) ----------------------------------------------------------

        [Test]
        public void Snapshot_RestartRoundTrip_PreservesRecordsReplayDuplicateAndCap()
        {
            var svc = Service(cap: 2);
            Submit(svc, Result("r1", 100), 1, W46Wed);
            Submit(svc, Result("r2", 200), 2, W46Wed);
            Submit(svc, Result("r3", 300), 3, W46Wed); // cap 2 reached
            Submit(svc, Result("x1", 10, level: "cr.level.0002"), 4, W46Wed);
            var text = svc.ExportSnapshot();

            var restored = WeeklyPersonalRecordService.FromSnapshot(text, Game, Profile, _policy, _ledger, out var reason);
            Assert.IsNotNull(restored, reason);
            Assert.AreEqual(text, restored.ExportSnapshot());
            var view = restored.GetView(Mode, Level, W46);
            Assert.AreEqual(300, view.CurrentBest);
            Assert.AreEqual(200, view.PreviousBest);

            Assert.AreEqual(WeeklyRecordStatus.Duplicate, Submit(restored, Result("r3", 300), 3, W46Wed).Status);
            Assert.AreEqual(WeeklyRecordStatus.ReplayRejected, Submit(restored, Result("r3b", 900), 3, W46Wed).Status);
            Assert.AreEqual(R.WindowCapReached, Submit(restored, Result("x2", 500, level: "cr.level.0002"), 5, W46Wed).Reason);
            Assert.AreEqual(50, _ledger.Balance(CurrencyType.Coins));
        }

        [Test]
        public void Snapshot_RestartThenGenuineImprovement_GetsNextTierKey()
        {
            var svc = Service(cap: 3);
            Submit(svc, Result("r1", 100), 1, W46Wed);
            Assert.AreEqual(Instance(W46, 1), Submit(svc, Result("r2", 200), 2, W46Wed).Grant.SourceInstanceId);

            var restored = WeeklyPersonalRecordService.FromSnapshot(svc.ExportSnapshot(), Game, Profile, _policy,
                _ledger, out _);
            var next = Submit(restored, Result("r3", 300), 3, W46Wed);
            Assert.AreEqual(string.Empty, next.Reason);
            Assert.AreEqual(Instance(W46, 2), next.Grant.SourceInstanceId);
            Assert.AreEqual(50, _ledger.Balance(CurrencyType.Coins));
            Assert.AreEqual(2, _ledger.Count);
        }

        [Test]
        public void LostLocalState_LedgerStillPreventsDoubleGrantAndTierCollision()
        {
            var svc = Service(cap: 3);
            Submit(svc, Result("r1", 100), 1, W46Wed);
            var granted = Submit(svc, Result("r2", 200), 2, W46Wed).Grant;

            var fresh = new WeeklyPersonalRecordService(Game, Profile, _policy, _ledger); // local state lost
            Submit(fresh, Result("r1", 100), 1, W46Wed);
            // Resynced two days later: GrantedUtc differs, ledger Duplicate semantics ignore it.
            var again = Submit(fresh, Result("r2", 200), 2, W46Wed, now: W46Fri);
            Assert.AreEqual(R.AlreadyGranted, again.Reason);
            Assert.AreEqual(granted.Key, again.Grant.Key);
            Assert.AreEqual(W46Wed, again.Grant.GrantedUtc, "original grant record is returned");
            Assert.AreEqual(25, _ledger.Balance(CurrencyType.Coins));
            Assert.AreEqual(1, _ledger.Count);

            var next = Submit(fresh, Result("r3", 300), 3, W46Wed);
            Assert.AreEqual(Instance(W46, 2), next.Grant.SourceInstanceId);
        }

        [Test]
        public void LostLocalState_RewardAmountChangedMidWeek_SameResultIsAlreadyGranted_NotRegrantedAtNextSlot()
        {
            var svc = Service(cap: 3);
            Submit(svc, Result("r1", 100), 1, W46Wed);
            Submit(svc, Result("r2", 200), 2, W46Wed);

            var raised = new WeeklyPersonalRecordPolicy(3, 1, "Coins", 40);
            var fresh = new WeeklyPersonalRecordService(Game, Profile, raised, _ledger);
            Submit(fresh, Result("r1", 100), 1, W46Wed);
            var again = Submit(fresh, Result("r2", 200), 2, W46Fri);
            Assert.AreEqual(R.AlreadyGranted, again.Reason, "IdempotencyConflict on own slot is not a new grant");
            Assert.AreEqual("r2", again.Grant.Reference);
            Assert.AreEqual(25, _ledger.Balance(CurrencyType.Coins));
            Assert.AreEqual(1, _ledger.Count);

            var next = Submit(fresh, Result("r3", 300), 3, W46Fri);
            Assert.AreEqual(Instance(W46, 2), next.Grant.SourceInstanceId);
            Assert.AreEqual(65, _ledger.Balance(CurrencyType.Coins));
        }

        // ---- D1: the ledger enforces the cross-level weekly cap ---------------------------

        private void CapTwoAcrossTwoLevels(WeeklyPersonalRecordService svc)
        {
            Submit(svc, Result("a1", 100), 1, W46Wed);
            Assert.AreEqual(Instance(W46, 1), Submit(svc, Result("a2", 200), 2, W46Wed).Grant.SourceInstanceId);
            Submit(svc, Result("b1", 100, level: "cr.level.0002"), 3, W46Wed);
            Assert.AreEqual(Instance(W46, 2),
                Submit(svc, Result("b2", 200, level: "cr.level.0002"), 4, W46Wed).Grant.SourceInstanceId);
        }

        [Test]
        public void StateLossAfterCap_ThirdLevelImprovement_IsWindowCapReached()
        {
            var svc = Service(cap: 2);
            CapTwoAcrossTwoLevels(svc);

            var fresh = new WeeklyPersonalRecordService(Game, Profile, _policy, _ledger);
            Submit(fresh, Result("c1", 100, level: "cr.level.0003"), 5, W46Wed);
            var o = Submit(fresh, Result("c2", 900, level: "cr.level.0003"), 6, W46Wed);
            Assert.AreEqual(WeeklyRecordStatus.Improved, o.Status);
            Assert.AreEqual(R.WindowCapReached, o.Reason);
            Assert.IsNull(o.Grant);
            Assert.AreEqual(50, _ledger.Balance(CurrencyType.Coins));
            Assert.AreEqual(2, _ledger.Count);
        }

        [Test]
        public void OlderValidSnapshotRestored_CapStillHeld_AndHighWaterMarkRejectsIt()
        {
            var svc = Service(cap: 2);
            Submit(svc, Result("c1", 100, level: "cr.level.0003"), 5, W46Wed);
            var older = svc.ExportSnapshot();
            var olderSequence = svc.Sequence;
            CapTwoAcrossTwoLevels(svc);
            Assert.That(svc.Sequence, Is.GreaterThan(olderSequence));

            var restored = WeeklyPersonalRecordService.FromSnapshot(older, Game, Profile, _policy, _ledger, out var reason);
            Assert.IsNotNull(restored, reason);
            Assert.AreEqual(olderSequence, restored.Sequence);
            var o = Submit(restored, Result("c2", 900, level: "cr.level.0003"), 6, W46Wed);
            Assert.AreEqual(R.WindowCapReached, o.Reason);
            Assert.IsNull(o.Grant);
            Assert.AreEqual(50, _ledger.Balance(CurrencyType.Coins));

            Assert.IsNull(WeeklyPersonalRecordService.FromSnapshot(older, Game, Profile, _policy, _ledger,
                out var rollback, svc.Sequence));
            Assert.AreEqual(R.SnapshotRollback, rollback);
            Assert.IsNotNull(WeeklyPersonalRecordService.FromSnapshot(svc.ExportSnapshot(), Game, Profile, _policy,
                _ledger, out _, svc.Sequence));
        }

        [Test]
        public void TwoDevicesSharingOneLedger_CannotExceedWeeklyCap()
        {
            var phone = Service(cap: 2);
            var tablet = new WeeklyPersonalRecordService(Game, Profile, _policy, _ledger);
            Submit(phone, Result("a1", 100), 1, W46Wed);
            Submit(tablet, Result("b1", 100, level: "cr.level.0002"), 2, W46Wed);
            Assert.IsNotNull(Submit(phone, Result("a2", 200), 3, W46Wed).Grant);
            Assert.IsNotNull(Submit(tablet, Result("b2", 200, level: "cr.level.0002"), 4, W46Wed).Grant);

            Assert.AreEqual(R.WindowCapReached, Submit(phone, Result("a3", 300), 5, W46Wed).Reason);
            Assert.AreEqual(R.WindowCapReached,
                Submit(tablet, Result("b3", 300, level: "cr.level.0002"), 6, W46Wed).Reason);
            Assert.AreEqual(2, _ledger.Count);
            Assert.AreEqual(50, _ledger.Balance(CurrencyType.Coins));
        }

        [Test]
        public void Snapshot_TamperedForeignVersionedOrNonCanonical_Rejected()
        {
            var svc = Service();
            Submit(svc, Result("r1", 100), 1, W46Wed);
            Submit(svc, Result("r2", 200), 2, W46Wed);
            var text = svc.ExportSnapshot();

            string Import(string t, string profile = Profile)
            {
                var ledger = profile == Profile ? _ledger : new RewardLedger(Game, profile);
                Assert.IsNull(WeeklyPersonalRecordService.FromSnapshot(t, Game, profile, _policy, ledger, out var r));
                return r;
            }

            Assert.AreEqual(R.IntegrityMismatch, Import(text.Replace("\"score\":200", "\"score\":900")));
            Assert.AreEqual(R.SnapshotOwnerMismatch, Import(text, "p2"));
            // Version 1 (local grant counts) is rejected, not migrated; the ledger is the cap authority.
            Assert.AreEqual(R.UnsupportedSnapshotVersion, Import(text.Replace("\"version\":2", "\"version\":1")));
            Assert.AreEqual(R.UnsupportedSnapshotVersion, Import(text.Replace("\"version\":2", "\"version\":3")));
            Assert.AreEqual(R.NonCanonicalSnapshot, Import(" " + text));
            Assert.AreEqual(R.MalformedSnapshot, Import("not json"));
            Assert.AreEqual(R.MalformedSnapshot, Import("{}"));
            Assert.AreEqual(R.MalformedSnapshot, Import(null));
            Assert.Throws<ArgumentException>(() => WeeklyPersonalRecordService.FromSnapshot(text, Game, Profile,
                _policy, new RewardLedger(Game, "p2"), out _));
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
            // Rejected submissions do not consume the result ID or trace.
            Assert.AreEqual(WeeklyRecordStatus.Participated, Submit(svc, Result("u", 100), 1, W46Wed).Status);
        }

        [Test]
        public void ConsentOff_NotEligible_ForWonderPopEvenWithPermissivePolicy()
        {
            var svc = Service(game: "Wonder_Pop");
            var noConsent = svc.Submit(Result("r1", 100, game: "Wonder_Pop"), Trace(1, 1, "Wonder_Pop"), W46Wed, W46Wed,
                Flag("Wonder_Pop", consent: false), FeatureFlagPolicy.Permissive("test"));
            Assert.AreEqual(WeeklyRecordStatus.NotEligible, noConsent.Status);
            Assert.AreEqual("RestrictedFeatureRequiresConsent", noConsent.Reason);
            Assert.IsNull(noConsent.View);
            Assert.IsNull(svc.GetView(Mode, Level, W46));

            var consented = svc.Submit(Result("r1", 100, game: "Wonder_Pop"), Trace(1, 1, "Wonder_Pop"), W46Wed, W46Wed,
                Flag("Wonder_Pop", consent: true), FeatureFlagPolicy.Default);
            Assert.AreEqual(WeeklyRecordStatus.Participated, consented.Status);
        }

        [Test]
        public void FlagGate_DefaultPolicyNoConsent_DisabledWrongFeatureOrGame_NotEligible()
        {
            var svc = Service();
            var r = Result("r1", 100);
            var t = Trace(1);
            Assert.AreEqual("RestrictedFeatureRequiresConsent",
                svc.Submit(r, t, W46Wed, W46Wed, Flag(consent: false), FeatureFlagPolicy.Default).Reason);
            Assert.AreEqual("RestrictedFeatureRequiresConsent",
                svc.Submit(r, t, W46Wed, W46Wed, Flag(consent: false), default(FeatureFlagPolicy)).Reason);
            Assert.AreEqual("FeatureFlagDisabled",
                svc.Submit(r, t, W46Wed, W46Wed, Flag(enabled: false), FeatureFlagPolicy.Default).Reason);
            Assert.AreEqual(R.WrongFeatureFlag,
                svc.Submit(r, t, W46Wed, W46Wed, Flag(feature: LiveOpsFeature.TimedMode), FeatureFlagPolicy.Default).Reason);
            Assert.AreEqual("GameIdMismatch",
                svc.Submit(r, t, W46Wed, W46Wed, Flag("Wonder_Pop"), FeatureFlagPolicy.Default).Reason);
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
            var policy = new WeeklyPersonalRecordPolicy(1, 1, "Coins", 1);
            var ledger = new RewardLedger(Game, Profile);
            Assert.Throws<ArgumentException>(() => new WeeklyPersonalRecordService("NotAGame", Profile, policy, ledger));
            Assert.Throws<ArgumentException>(() => new WeeklyPersonalRecordService(Game, "a|b", policy, ledger));
            Assert.Throws<ArgumentNullException>(() => new WeeklyPersonalRecordService(Game, Profile, null, ledger));
            Assert.Throws<ArgumentNullException>(() => new WeeklyPersonalRecordService(Game, Profile, policy, null));
            Assert.Throws<ArgumentException>(() => new WeeklyPersonalRecordService(Game, Profile, policy,
                new RewardLedger("Dino_Islands", Profile)));
            Assert.Throws<ArgumentException>(() => new WeeklyPersonalRecordService(Game, Profile, policy,
                new RewardLedger(Game, "p2")));
            Assert.Throws<ArgumentOutOfRangeException>(() => new WeeklyPersonalRecordPolicy(-1, 1, "Coins", 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new WeeklyPersonalRecordPolicy(1, 0, "Coins", 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new WeeklyPersonalRecordPolicy(1, 1, "Coins", 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new WeeklyPersonalRecordPolicy(1, 1, "Coins", 1, -1));
            Assert.Throws<ArgumentException>(() => new WeeklyPersonalRecordPolicy(1, 1, "a|b", 1));
            Assert.Throws<ArgumentException>(() => new WeeklyPersonalRecordPolicy(1, 1, RewardLedger.PaidAssetId, 1));
            var svc = Service();
            Assert.Throws<ArgumentNullException>(() => svc.Submit(null, Trace(1), W46Wed, W46Wed, Flag(), FeatureFlagPolicy.Default));
            Assert.Throws<ArgumentNullException>(() => svc.Submit(Result("r", 1), null, W46Wed, W46Wed, Flag(), FeatureFlagPolicy.Default));
        }
    }
}
