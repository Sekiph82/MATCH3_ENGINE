using System;
using NUnit.Framework;
using Match3.Engine.Runtime.LiveOps;

namespace Match3.Engine.Tests.Editor
{
    public class WeeklyChallengeCalendarGateTests
    {
        // Most relaxed constructible policy: carries no flags, so it must behave exactly like Default.
        private static readonly CalendarEventPolicy Custom =
            new CalendarEventPolicy("Custom test policy");

        private static CalendarEventRecord Rec(string gameId, bool claimed, bool offline, bool anchor) =>
            CalendarEventRecord.Create(gameId, CalendarEventType.WeeklyChallenge, "2026-W45", claimed, offline, anchor);

        private static void AssertBlocked(string gameId, CalendarEventRecord record, CalendarEventPolicy policy, string expected)
        {
            Assert.IsFalse(WeeklyChallengeCalendarGate.CanClaimReward(gameId, record, policy, out var reason));
            Assert.AreEqual(expected, reason);
        }

        private static void AssertEligible(string gameId, CalendarEventRecord record, CalendarEventPolicy policy)
        {
            Assert.IsTrue(WeeklyChallengeCalendarGate.CanClaimReward(gameId, record, policy, out var reason));
            Assert.AreEqual(string.Empty, reason);
        }

        // CalendarEventRecord
        [Test]
        public void CalendarEventRecord_Create_NullGameId_Throws()
        {
            Assert.Throws<ArgumentException>(() =>
                CalendarEventRecord.Create(null, CalendarEventType.DailyChallenge, "2026-W45", false, false, true));
        }

        [Test]
        public void CalendarEventRecord_Create_EmptyGameId_Throws()
        {
            Assert.Throws<ArgumentException>(() =>
                CalendarEventRecord.Create("", CalendarEventType.DailyChallenge, "2026-W45", false, false, true));
        }

        [Test]
        public void CalendarEventRecord_Create_NullWeekId_Throws()
        {
            Assert.Throws<ArgumentException>(() =>
                CalendarEventRecord.Create("Coral_Rescue", CalendarEventType.DailyChallenge, null, false, false, true));
        }

        [Test]
        public void CalendarEventRecord_Create_EmptyWeekId_Throws()
        {
            Assert.Throws<ArgumentException>(() =>
                CalendarEventRecord.Create("Coral_Rescue", CalendarEventType.DailyChallenge, "", false, false, true));
        }

        [Test]
        public void CalendarEventRecord_Create_Valid_Succeeds()
        {
            var r = CalendarEventRecord.Create("Coral_Rescue", CalendarEventType.EventProgress, "2026-W45", true, false, true);
            Assert.AreEqual("Coral_Rescue", r.GameId);
            Assert.AreEqual(CalendarEventType.EventProgress, r.EventType);
            Assert.AreEqual("2026-W45", r.WeekId);
            Assert.IsTrue(r.IsAlreadyClaimed);
            Assert.IsFalse(r.IsOfflineRecovery);
            Assert.IsTrue(r.HasUtcAnchor);
        }

        [Test]
        public void CalendarEventRecord_IsDuplicateClaim_AlreadyClaimed_True()
        {
            Assert.IsTrue(Rec("Coral_Rescue", true, false, true).IsDuplicateClaim);
        }

        [Test]
        public void CalendarEventRecord_IsDuplicateClaim_NotClaimed_False()
        {
            Assert.IsFalse(Rec("Coral_Rescue", false, false, true).IsDuplicateClaim);
        }

        [Test]
        public void CalendarEventRecord_RequiresUtcAnchor_NoAnchorNoOffline_True()
        {
            Assert.IsTrue(Rec("Coral_Rescue", false, false, false).RequiresUtcAnchor);
        }

        [Test]
        public void CalendarEventRecord_RequiresUtcAnchor_HasAnchor_False()
        {
            Assert.IsFalse(Rec("Coral_Rescue", false, false, true).RequiresUtcAnchor);
        }

        [Test]
        public void CalendarEventRecord_RequiresUtcAnchor_OfflineRecoveryWithoutAnchor_True()
        {
            Assert.IsTrue(Rec("Coral_Rescue", false, true, false).RequiresUtcAnchor);
        }

        // CalendarEventPolicy
        [Test]
        public void CalendarEventPolicy_Safe_Values_Correct()
        {
            Assert.AreEqual("desc", CalendarEventPolicy.Safe("desc").PolicyDescription);
        }

        [Test]
        public void CalendarEventPolicy_Default_NotNull()
        {
            Assert.IsNotEmpty(CalendarEventPolicy.Default.PolicyDescription);
        }

        [Test]
        public void CalendarEventPolicy_NullDescription_Throws()
        {
            Assert.Throws<ArgumentException>(() => CalendarEventPolicy.Safe(null));
        }

        [Test]
        public void CalendarEventPolicy_EmptyDescription_Throws()
        {
            Assert.Throws<ArgumentException>(() => CalendarEventPolicy.Safe(""));
        }

        [Test]
        public void CalendarEventPolicy_Ctor_NullOrEmptyDescription_Throws()
        {
            Assert.Throws<ArgumentException>(() => new CalendarEventPolicy(null));
            Assert.Throws<ArgumentException>(() => new CalendarEventPolicy(""));
        }

        // CanClaimReward — Wonder_Pop
        [Test]
        public void CanClaimReward_WonderPop_AlreadyClaimed_Guard1()
        {
            AssertBlocked("Wonder_Pop", Rec("Wonder_Pop", true, false, true), CalendarEventPolicy.Default, "WonderPopDuplicateClaimProhibited");
        }

        [Test]
        public void CanClaimReward_WonderPop_AlreadyClaimed_CustomPolicy_StillGuard1()
        {
            AssertBlocked("Wonder_Pop", Rec("Wonder_Pop", true, false, true), Custom, "WonderPopDuplicateClaimProhibited");
        }

        [Test]
        public void CanClaimReward_WonderPop_NotClaimed_HasUtcAnchor_Eligible()
        {
            AssertEligible("Wonder_Pop", Rec("Wonder_Pop", false, false, true), CalendarEventPolicy.Default);
        }

        [Test]
        public void CanClaimReward_WonderPop_NotClaimed_NoUtcAnchor_NotOffline_Guard3()
        {
            AssertBlocked("Wonder_Pop", Rec("Wonder_Pop", false, false, false), CalendarEventPolicy.Default, "UtcAnchorRequired");
        }

        [Test]
        public void CanClaimReward_WonderPop_OfflineRecovery_NoUtcAnchor_Blocked()
        {
            AssertBlocked("Wonder_Pop", Rec("Wonder_Pop", false, true, false), CalendarEventPolicy.Default, "UtcAnchorRequired");
        }

        [Test]
        public void CanClaimReward_WonderPop_OfflineRecovery_WithUtcAnchor_Eligible()
        {
            AssertEligible("Wonder_Pop", Rec("Wonder_Pop", false, true, true), CalendarEventPolicy.Default);
        }

        [Test]
        public void CanClaimReward_WonderPop_Lowercase_AlreadyClaimed_UnknownGame()
        {
            // Ordinal catalog lookup: a case variant of Wonder_Pop is not a catalog ID, so it fails closed with UnknownGame (never eligible, never the Wonder_Pop reason).
            AssertBlocked("wonder_pop", Rec("wonder_pop", true, false, true), CalendarEventPolicy.Default, "UnknownGame");
        }

        // CanClaimReward — other games
        [Test]
        public void CanClaimReward_NonWonderPop_AlreadyClaimed_Guard2()
        {
            AssertBlocked("Coral_Rescue", Rec("Coral_Rescue", true, false, true), CalendarEventPolicy.Default, "DuplicateClaimDetected");
        }

        [Test]
        public void CanClaimReward_NonWonderPop_NotClaimed_NoUtcAnchor_Guard3()
        {
            AssertBlocked("Coral_Rescue", Rec("Coral_Rescue", false, false, false), CalendarEventPolicy.Default, "UtcAnchorRequired");
        }

        [Test]
        public void CanClaimReward_NonWonderPop_NotClaimed_HasUtcAnchor_Eligible()
        {
            AssertEligible("Coral_Rescue", Rec("Coral_Rescue", false, false, true), CalendarEventPolicy.Default);
        }

        [Test]
        public void CanClaimReward_NonWonderPop_OfflineRecovery_NoUtcAnchor_Blocked()
        {
            AssertBlocked("Coral_Rescue", Rec("Coral_Rescue", false, true, false), CalendarEventPolicy.Default, "UtcAnchorRequired");
        }

        [Test]
        public void CanClaimReward_AnyPolicy_AlreadyClaimed_AlwaysBlocked()
        {
            AssertBlocked("Coral_Rescue", Rec("Coral_Rescue", true, false, false), Custom, "DuplicateClaimDetected");
            AssertBlocked("Coral_Rescue", Rec("Coral_Rescue", true, false, false), default(CalendarEventPolicy), "DuplicateClaimDetected");
        }

        [Test]
        public void CanClaimReward_AnyPolicy_OfflineRecoveryWithoutAnchor_AlwaysBlocked()
        {
            AssertBlocked("Coral_Rescue", Rec("Coral_Rescue", false, true, false), Custom, "UtcAnchorRequired");
            AssertBlocked("Coral_Rescue", Rec("Coral_Rescue", false, true, false), default(CalendarEventPolicy), "UtcAnchorRequired");
        }

        [Test]
        public void CanClaimReward_DefaultStructPolicy_FailsClosed()
        {
            var r = Rec("Coral_Rescue", true, false, true);
            Assert.IsFalse(WeeklyChallengeCalendarGate.CanClaimReward("Coral_Rescue", r, CalendarEventPolicy.Default, out var expected));
            Assert.IsFalse(WeeklyChallengeCalendarGate.CanClaimReward("Coral_Rescue", r, default(CalendarEventPolicy), out var reason));
            Assert.AreEqual(expected, reason);
            Assert.AreEqual("DuplicateClaimDetected", reason);
        }

        [Test]
        public void CanClaimReward_GameIdMismatch_Blocked()
        {
            AssertBlocked("Coral_Rescue", Rec("Wonder_Pop", false, false, true), CalendarEventPolicy.Default, "GameIdMismatch");
        }

        [Test]
        public void CanClaimReward_WonderPopParam_RecordDiffers_GameIdMismatch()
        {
            AssertBlocked("Wonder_Pop", Rec("Coral_Rescue", true, false, true), CalendarEventPolicy.Default, "GameIdMismatch");
        }

        // Helpers
        [Test]
        public void IsDuplicateClaim_Helper_True()
        {
            Assert.IsTrue(WeeklyChallengeCalendarGate.IsDuplicateClaim(Rec("Coral_Rescue", true, false, true)));
        }

        [Test]
        public void IsDuplicateClaim_Helper_False()
        {
            Assert.IsFalse(WeeklyChallengeCalendarGate.IsDuplicateClaim(Rec("Coral_Rescue", false, false, true)));
        }

        [Test]
        public void RequiresUtcAnchor_Helper_True()
        {
            Assert.IsTrue(WeeklyChallengeCalendarGate.RequiresUtcAnchor(Rec("Coral_Rescue", false, false, false)));
        }

        [Test]
        public void RequiresUtcAnchor_Helper_False()
        {
            Assert.IsFalse(WeeklyChallengeCalendarGate.RequiresUtcAnchor(Rec("Coral_Rescue", false, false, true)));
        }

        // WeeklyClaimLedger
        [Test]
        public void WeeklyClaimLedger_FirstClaim_True()
        {
            Assert.IsTrue(new WeeklyClaimLedger().TryClaim("Coral_Rescue", "p1", "2026-W45"));
        }

        [Test]
        public void WeeklyClaimLedger_RepeatClaim_False()
        {
            var ledger = new WeeklyClaimLedger();
            Assert.IsTrue(ledger.TryClaim("Coral_Rescue", "p1", "2026-W45"));
            Assert.IsFalse(ledger.TryClaim("Coral_Rescue", "p1", "2026-W45"));
            Assert.IsFalse(ledger.TryClaim("Coral_Rescue", "p1", "2026-W45"));
        }

        [Test]
        public void WeeklyClaimLedger_DifferentWeek_True()
        {
            var ledger = new WeeklyClaimLedger();
            Assert.IsTrue(ledger.TryClaim("Coral_Rescue", "p1", "2026-W45"));
            Assert.IsTrue(ledger.TryClaim("Coral_Rescue", "p1", "2026-W46"));
        }

        [Test]
        public void WeeklyClaimLedger_DifferentPlayer_True()
        {
            var ledger = new WeeklyClaimLedger();
            Assert.IsTrue(ledger.TryClaim("Coral_Rescue", "p1", "2026-W45"));
            Assert.IsTrue(ledger.TryClaim("Coral_Rescue", "p2", "2026-W45"));
        }

        [Test]
        public void WeeklyClaimLedger_DifferentGame_True()
        {
            var ledger = new WeeklyClaimLedger();
            Assert.IsTrue(ledger.TryClaim("Coral_Rescue", "p1", "2026-W45"));
            Assert.IsTrue(ledger.TryClaim("Dino_Islands", "p1", "2026-W45"));
        }

        [Test]
        public void WeeklyClaimLedger_IsOrdinal()
        {
            var ledger = new WeeklyClaimLedger();
            Assert.IsTrue(ledger.TryClaim("Coral_Rescue", "p1", "2026-W45"));
            Assert.IsTrue(ledger.TryClaim("Coral_Rescue", "P1", "2026-W45"));
            Assert.IsTrue(ledger.TryClaim("Coral_Rescue", "p1", "2026-w45"));
            // Case-variant / non-catalog game IDs are rejected (Ordinal catalog), never a separate claim key.
            Assert.Throws<ArgumentException>(() => ledger.TryClaim("coral_rescue", "p1", "2026-W45"));
            Assert.Throws<ArgumentException>(() => ledger.TryClaim("Unknown_Game", "p1", "2026-W45"));
        }

        [Test]
        public void WeeklyClaimLedger_SeparatorInIds_NoKeyCollision()
        {
            var ledger = new WeeklyClaimLedger();
            // Game IDs are catalog-only, so the separator boundary under test is player|week.
            Assert.IsTrue(ledger.TryClaim("Coral_Rescue", "a|b", "W1"));
            Assert.IsTrue(ledger.TryClaim("Coral_Rescue", "a", "b|W1"));
        }

        [Test]
        public void WeeklyClaimLedger_NullOrEmptyArgs_Throw()
        {
            var ledger = new WeeklyClaimLedger();
            Assert.Throws<ArgumentException>(() => ledger.TryClaim(null, "p1", "W1"));
            Assert.Throws<ArgumentException>(() => ledger.TryClaim("", "p1", "W1"));
            Assert.Throws<ArgumentException>(() => ledger.TryClaim("Coral_Rescue", null, "W1"));
            Assert.Throws<ArgumentException>(() => ledger.TryClaim("Coral_Rescue", "", "W1"));
            Assert.Throws<ArgumentException>(() => ledger.TryClaim("Coral_Rescue", "p1", null));
            Assert.Throws<ArgumentException>(() => ledger.TryClaim("Coral_Rescue", "p1", ""));
        }

        // CalendarEventLog
        [Test]
        public void CalendarEventLog_Record_And_Count()
        {
            var log = new CalendarEventLog();
            log.Record("Coral_Rescue", "WeeklyChallenge");
            log.Record("Wonder_Pop", "DailyChallenge");
            Assert.AreEqual(2, log.Count);
        }

        [Test]
        public void CalendarEventLog_HasEntry_True()
        {
            var log = new CalendarEventLog();
            log.Record("Coral_Rescue", "ClaimReset");
            Assert.IsTrue(log.HasEntry("Coral_Rescue"));
            Assert.IsFalse(log.HasEntry("Wonder_Pop"));
        }

        [Test]
        public void CalendarEventLog_Clear_ResetsCount()
        {
            var log = new CalendarEventLog();
            log.Record("Coral_Rescue", "EventProgress");
            log.Clear();
            Assert.AreEqual(0, log.Count);
        }

        [Test]
        public void CalendarEventLog_Record_NullGameId_Throws()
        {
            var log = new CalendarEventLog();
            Assert.Throws<ArgumentException>(() => log.Record(null, "WeeklyChallenge"));
        }

        [Test]
        public void CalendarEventLog_Record_NullOrEmptyEventType_Throws()
        {
            var log = new CalendarEventLog();
            Assert.Throws<ArgumentException>(() => log.Record("Coral_Rescue", null));
            Assert.Throws<ArgumentException>(() => log.Record("Coral_Rescue", ""));
        }

        [Test]
        public void CalendarEventLog_HasEntry_IsOrdinal()
        {
            var log = new CalendarEventLog();
            log.Record("Coral_Rescue", "ClaimReset");
            Assert.IsFalse(log.HasEntry("coral_rescue"));
            Assert.IsFalse(log.HasEntry("Coral"));
            // Soft hyphen (U+00AD) is ignorable under culture-sensitive comparison; Ordinal must not match it.
            var softHyphenLog = new CalendarEventLog();
            softHyphenLog.Record("Coral\u00AD_Rescue", "evt");
            Assert.IsFalse(softHyphenLog.HasEntry("Coral_Rescue"));
        }

        [Test]
        public void CalendarEventType_HasExactlySixValues()
        {
            Assert.AreEqual(6, Enum.GetValues(typeof(CalendarEventType)).Length);
        }


        [Test]
        public void NullOrEmptyGameId_DefaultRecord_GameIdMissing()
        {
            foreach (var gameId in new[] { null, string.Empty })
            {
                Assert.IsFalse(WeeklyChallengeCalendarGate.CanClaimReward(gameId, default(CalendarEventRecord), default(CalendarEventPolicy), out var reason));
                Assert.AreEqual("GameIdMissing", reason);
            }
        }

        [Test]
        public void UnknownOrCaseVariantGameId_DefaultRecord_UnknownGame()
        {
            foreach (var gameId in new[] { "Unknown_Game", "coral_rescue", "CORAL_RESCUE", " Coral_Rescue", "Coral_Rescue ", "wonder_pop", "WONDER_POP" })
            {
                Assert.IsFalse(WeeklyChallengeCalendarGate.CanClaimReward(gameId, default(CalendarEventRecord), default(CalendarEventPolicy), out var reason));
                Assert.AreEqual("UnknownGame", reason);
            }
        }
    }
}
