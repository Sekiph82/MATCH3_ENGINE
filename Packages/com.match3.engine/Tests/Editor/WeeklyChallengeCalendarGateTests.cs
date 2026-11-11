using System;
using NUnit.Framework;
using Match3.Engine.Runtime.LiveOps;

namespace Match3.Engine.Tests.Editor
{
    public class WeeklyChallengeCalendarGateTests
    {
        private static CalendarEventRecord Rec(string gameId, bool claimed, bool offline, bool anchor) =>
            CalendarEventRecord.Create(gameId, CalendarEventType.WeeklyChallenge, "2026-W45", claimed, offline, anchor);

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
                CalendarEventRecord.Create("G", CalendarEventType.DailyChallenge, null, false, false, true));
        }

        [Test]
        public void CalendarEventRecord_Create_EmptyWeekId_Throws()
        {
            Assert.Throws<ArgumentException>(() =>
                CalendarEventRecord.Create("G", CalendarEventType.DailyChallenge, "", false, false, true));
        }

        [Test]
        public void CalendarEventRecord_Create_Valid_Succeeds()
        {
            var r = CalendarEventRecord.Create("TestGame", CalendarEventType.EventProgress, "2026-W45", true, false, true);
            Assert.AreEqual("TestGame", r.GameId);
            Assert.AreEqual(CalendarEventType.EventProgress, r.EventType);
            Assert.AreEqual("2026-W45", r.WeekId);
            Assert.IsTrue(r.IsAlreadyClaimed);
            Assert.IsFalse(r.IsOfflineRecovery);
            Assert.IsTrue(r.HasUtcAnchor);
        }

        [Test]
        public void CalendarEventRecord_IsDuplicateClaim_AlreadyClaimed_True()
        {
            Assert.IsTrue(Rec("G", true, false, true).IsDuplicateClaim);
        }

        [Test]
        public void CalendarEventRecord_IsDuplicateClaim_NotClaimed_False()
        {
            Assert.IsFalse(Rec("G", false, false, true).IsDuplicateClaim);
        }

        [Test]
        public void CalendarEventRecord_RequiresUtcAnchor_NoAnchorNoOffline_True()
        {
            Assert.IsTrue(Rec("G", false, false, false).RequiresUtcAnchor);
        }

        [Test]
        public void CalendarEventRecord_RequiresUtcAnchor_HasAnchor_False()
        {
            Assert.IsFalse(Rec("G", false, false, true).RequiresUtcAnchor);
        }

        [Test]
        public void CalendarEventRecord_RequiresUtcAnchor_IsOfflineRecovery_False()
        {
            Assert.IsFalse(Rec("G", false, true, false).RequiresUtcAnchor);
        }

        // CalendarEventPolicy
        [Test]
        public void CalendarEventPolicy_Safe_Values_Correct()
        {
            var p = CalendarEventPolicy.Safe("desc");
            Assert.IsTrue(p.PreventDuplicateClaims);
            Assert.IsTrue(p.RequireUtcAnchor);
            Assert.AreEqual("desc", p.PolicyDescription);
        }

        [Test]
        public void CalendarEventPolicy_Default_NotNull()
        {
            Assert.IsNotNull(CalendarEventPolicy.Default.PolicyDescription);
            Assert.IsTrue(CalendarEventPolicy.Default.PreventDuplicateClaims);
            Assert.IsTrue(CalendarEventPolicy.Default.RequireUtcAnchor);
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

        // CanClaimReward — Wonder_Pop
        [Test]
        public void CanClaimReward_WonderPop_AlreadyClaimed_Guard1()
        {
            bool ok = WeeklyChallengeCalendarGate.CanClaimReward("Wonder_Pop", Rec("Wonder_Pop", true, false, true), CalendarEventPolicy.Default, out var reason);
            Assert.IsFalse(ok);
            Assert.AreEqual("WonderPopDuplicateClaimProhibited", reason);
        }

        [Test]
        public void CanClaimReward_WonderPop_AlreadyClaimed_NoPolicyRestrictions_StillGuard1()
        {
            bool ok = WeeklyChallengeCalendarGate.CanClaimReward("Wonder_Pop", Rec("Wonder_Pop", true, false, true), default(CalendarEventPolicy), out var reason);
            Assert.IsFalse(ok);
            Assert.AreEqual("WonderPopDuplicateClaimProhibited", reason);
        }

        [Test]
        public void CanClaimReward_WonderPop_NotClaimed_HasUtcAnchor_Eligible()
        {
            bool ok = WeeklyChallengeCalendarGate.CanClaimReward("Wonder_Pop", Rec("Wonder_Pop", false, false, true), CalendarEventPolicy.Default, out var reason);
            Assert.IsTrue(ok);
            Assert.AreEqual(string.Empty, reason);
        }

        [Test]
        public void CanClaimReward_WonderPop_NotClaimed_NoUtcAnchor_NotOffline_Guard3()
        {
            bool ok = WeeklyChallengeCalendarGate.CanClaimReward("Wonder_Pop", Rec("Wonder_Pop", false, false, false), CalendarEventPolicy.Default, out var reason);
            Assert.IsFalse(ok);
            Assert.AreEqual("UtcAnchorRequired", reason);
        }

        [Test]
        public void CanClaimReward_WonderPop_NotClaimed_IsOfflineRecovery_Eligible()
        {
            bool ok = WeeklyChallengeCalendarGate.CanClaimReward("Wonder_Pop", Rec("Wonder_Pop", false, true, false), CalendarEventPolicy.Default, out var reason);
            Assert.IsTrue(ok);
            Assert.AreEqual(string.Empty, reason);
        }

        [Test]
        public void CanClaimReward_WonderPop_Lowercase_AlreadyClaimed_Guard2()
        {
            bool ok = WeeklyChallengeCalendarGate.CanClaimReward("wonder_pop", Rec("wonder_pop", true, false, true), CalendarEventPolicy.Default, out var reason);
            Assert.IsFalse(ok);
            Assert.AreEqual("DuplicateClaimDetected", reason);
        }

        // CanClaimReward — other games
        [Test]
        public void CanClaimReward_NonWonderPop_AlreadyClaimed_Guard2()
        {
            bool ok = WeeklyChallengeCalendarGate.CanClaimReward("Coral_Rescue", Rec("Coral_Rescue", true, false, true), CalendarEventPolicy.Default, out var reason);
            Assert.IsFalse(ok);
            Assert.AreEqual("DuplicateClaimDetected", reason);
        }

        [Test]
        public void CanClaimReward_NonWonderPop_NotClaimed_NoUtcAnchor_Guard3()
        {
            bool ok = WeeklyChallengeCalendarGate.CanClaimReward("Coral_Rescue", Rec("Coral_Rescue", false, false, false), CalendarEventPolicy.Default, out var reason);
            Assert.IsFalse(ok);
            Assert.AreEqual("UtcAnchorRequired", reason);
        }

        [Test]
        public void CanClaimReward_NonWonderPop_NotClaimed_HasUtcAnchor_Eligible()
        {
            bool ok = WeeklyChallengeCalendarGate.CanClaimReward("Coral_Rescue", Rec("Coral_Rescue", false, false, true), CalendarEventPolicy.Default, out var reason);
            Assert.IsTrue(ok);
            Assert.AreEqual(string.Empty, reason);
        }

        [Test]
        public void CanClaimReward_NonWonderPop_NotClaimed_IsOfflineRecovery_Eligible()
        {
            bool ok = WeeklyChallengeCalendarGate.CanClaimReward("Coral_Rescue", Rec("Coral_Rescue", false, true, false), CalendarEventPolicy.Default, out var reason);
            Assert.IsTrue(ok);
            Assert.AreEqual(string.Empty, reason);
        }

        [Test]
        public void CanClaimReward_NoPolicyRestrictions_AlreadyClaimed_Eligible()
        {
            // default(CalendarEventPolicy): PreventDuplicateClaims=false, RequireUtcAnchor=false
            var policy = default(CalendarEventPolicy);
            Assert.IsFalse(policy.PreventDuplicateClaims);
            Assert.IsFalse(policy.RequireUtcAnchor);
            bool ok = WeeklyChallengeCalendarGate.CanClaimReward("Coral_Rescue", Rec("Coral_Rescue", true, false, false), policy, out var reason);
            Assert.IsTrue(ok);
            Assert.AreEqual(string.Empty, reason);
        }

        // Helpers
        [Test]
        public void IsDuplicateClaim_Helper_True()
        {
            Assert.IsTrue(WeeklyChallengeCalendarGate.IsDuplicateClaim(Rec("G", true, false, true)));
        }

        [Test]
        public void IsDuplicateClaim_Helper_False()
        {
            Assert.IsFalse(WeeklyChallengeCalendarGate.IsDuplicateClaim(Rec("G", false, false, true)));
        }

        [Test]
        public void RequiresUtcAnchor_Helper_True()
        {
            Assert.IsTrue(WeeklyChallengeCalendarGate.RequiresUtcAnchor(Rec("G", false, false, false)));
        }

        [Test]
        public void RequiresUtcAnchor_Helper_False()
        {
            Assert.IsFalse(WeeklyChallengeCalendarGate.RequiresUtcAnchor(Rec("G", false, false, true)));
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
            log.Record("G", "EventProgress");
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
        public void CalendarEventType_HasExactlySixValues()
        {
            Assert.AreEqual(6, Enum.GetValues(typeof(CalendarEventType)).Length);
        }
    }
}
