using System;
using NUnit.Framework;
using Match3.Engine.Runtime.LiveOps;

namespace Match3.Engine.Tests.Editor
{
    public class LimitedTimeChallengeGateTests
    {
        // Most relaxed constructible policy: carries no flags, so it must behave exactly like Default.
        private static readonly ChallengeEventPolicy Custom =
            new ChallengeEventPolicy("Custom test policy");

        private static void AssertBlocked(string gameId, ChallengeEventRecord record, ChallengeEventPolicy policy, string expected)
        {
            Assert.IsFalse(LimitedTimeChallengeGate.CanActivateEvent(gameId, record, policy, out string reason));
            Assert.AreEqual(expected, reason);
        }

        private static void AssertEligible(string gameId, ChallengeEventRecord record, ChallengeEventPolicy policy)
        {
            Assert.IsTrue(LimitedTimeChallengeGate.CanActivateEvent(gameId, record, policy, out string reason));
            Assert.AreEqual(string.Empty, reason);
        }

        // ChallengeEventRecord
        [Test]
        public void ChallengeEventRecord_Create_NullGameId_Throws()
        {
            Assert.Throws<ArgumentException>(() =>
                ChallengeEventRecord.Create(null, ChallengeEventType.ScheduledGoal, true, false, false));
        }

        [Test]
        public void ChallengeEventRecord_Create_EmptyGameId_Throws()
        {
            Assert.Throws<ArgumentException>(() =>
                ChallengeEventRecord.Create("", ChallengeEventType.ScheduledGoal, true, false, false));
        }

        [Test]
        public void ChallengeEventRecord_Create_Valid_Succeeds()
        {
            var r = ChallengeEventRecord.Create("TestGame", ChallengeEventType.SeasonalContent, true, true, false);
            Assert.AreEqual("TestGame", r.GameId);
            Assert.AreEqual(ChallengeEventType.SeasonalContent, r.EventType);
            Assert.IsTrue(r.IsWithinWindow);
            Assert.IsTrue(r.HasFallback);
            Assert.IsFalse(r.AffectsPermanentProgress);
        }

        [Test]
        public void ChallengeEventRecord_RequiresFallback_ExpiredNoFallback_True()
        {
            var r = ChallengeEventRecord.Create("G", ChallengeEventType.EventReward, false, false, false);
            Assert.IsTrue(r.RequiresFallback);
        }

        [Test]
        public void ChallengeEventRecord_RequiresFallback_WithinWindow_False()
        {
            var r = ChallengeEventRecord.Create("G", ChallengeEventType.EventReward, true, false, false);
            Assert.IsFalse(r.RequiresFallback);
        }

        [Test]
        public void ChallengeEventRecord_RequiresFallback_ExpiredHasFallback_False()
        {
            var r = ChallengeEventRecord.Create("G", ChallengeEventType.EventReward, false, true, false);
            Assert.IsFalse(r.RequiresFallback);
        }

        [Test]
        public void ChallengeEventRecord_IsExpired_NotWithinWindow_True()
        {
            var r = ChallengeEventRecord.Create("G", ChallengeEventType.ThemeCollection, false, true, false);
            Assert.IsTrue(r.IsExpired);
        }

        [Test]
        public void ChallengeEventRecord_IsExpired_WithinWindow_False()
        {
            var r = ChallengeEventRecord.Create("G", ChallengeEventType.ThemeCollection, true, true, false);
            Assert.IsFalse(r.IsExpired);
        }

        // ChallengeEventPolicy
        [Test]
        public void ChallengeEventPolicy_Safe_Values_Correct()
        {
            var p = ChallengeEventPolicy.Safe("test desc");
            Assert.AreEqual("test desc", p.PolicyDescription);
        }

        [Test]
        public void ChallengeEventPolicy_Default_NotNull()
        {
            Assert.IsNotEmpty(ChallengeEventPolicy.Default.PolicyDescription);
        }

        [Test]
        public void ChallengeEventPolicy_NullDescription_Throws()
        {
            Assert.Throws<ArgumentException>(() => ChallengeEventPolicy.Safe(null));
            Assert.Throws<ArgumentException>(() => ChallengeEventPolicy.Safe(""));
            Assert.Throws<ArgumentException>(() => new ChallengeEventPolicy(null));
            Assert.Throws<ArgumentException>(() => new ChallengeEventPolicy(""));
        }

        // CanActivateEvent — Wonder_Pop
        [Test]
        public void CanActivateEvent_WonderPop_AffectsPermanentProgress_Guard1()
        {
            var r = ChallengeEventRecord.Create("Wonder_Pop", ChallengeEventType.EventReward, true, true, true);
            AssertBlocked("Wonder_Pop", r, ChallengeEventPolicy.Default, "WonderPopPermanentProgressModificationProhibited");
        }

        [Test]
        public void CanActivateEvent_WonderPop_AffectsPermanentProgress_CustomPolicyAndExpired_StillGuard1()
        {
            // Neither a custom policy nor an expired/no-fallback state changes which guard fires first.
            var r = ChallengeEventRecord.Create("Wonder_Pop", ChallengeEventType.SeasonalContent, true, true, true);
            AssertBlocked("Wonder_Pop", r, Custom, "WonderPopPermanentProgressModificationProhibited");
            var expired = ChallengeEventRecord.Create("Wonder_Pop", ChallengeEventType.SeasonalContent, false, false, true);
            AssertBlocked("Wonder_Pop", expired, ChallengeEventPolicy.Default, "WonderPopPermanentProgressModificationProhibited");
        }

        [Test]
        public void CanActivateEvent_WonderPop_NoPermanentMod_WithinWindow_Eligible()
        {
            var r = ChallengeEventRecord.Create("Wonder_Pop", ChallengeEventType.ThemeCollection, true, false, false);
            AssertEligible("Wonder_Pop", r, ChallengeEventPolicy.Default);
        }

        [Test]
        public void CanActivateEvent_WonderPop_NoPermanentMod_Expired_NoFallback_Blocked()
        {
            var r = ChallengeEventRecord.Create("Wonder_Pop", ChallengeEventType.ScheduledGoal, false, false, false);
            AssertBlocked("Wonder_Pop", r, ChallengeEventPolicy.Default, "ExpiredEventRequiresFallback");
        }

        [Test]
        public void CanActivateEvent_WonderPop_NoPermanentMod_Expired_HasFallback_UseLastKnownGood()
        {
            var r = ChallengeEventRecord.Create("Wonder_Pop", ChallengeEventType.LastKnownGoodFallback, false, true, false);
            AssertBlocked("Wonder_Pop", r, ChallengeEventPolicy.Default, "UseLastKnownGood");
        }

        [Test]
        public void CanActivateEvent_WonderPop_Lowercase_PermanentMod_NotBlockedByGuard1()
        {
            var r = ChallengeEventRecord.Create("wonder_pop", ChallengeEventType.EventReward, true, true, true);
            AssertBlocked("wonder_pop", r, ChallengeEventPolicy.Default, "PermanentProgressModificationBlocked");
        }

        // CanActivateEvent — other games
        [Test]
        public void CanActivateEvent_NonWonderPop_PermanentMod_Guard2()
        {
            var r = ChallengeEventRecord.Create("OtherGame", ChallengeEventType.EventReward, true, true, true);
            AssertBlocked("OtherGame", r, ChallengeEventPolicy.Default, "PermanentProgressModificationBlocked");
        }

        [Test]
        public void CanActivateEvent_NonWonderPop_PermanentMod_Expired_Guard2BeforeExpiry()
        {
            var r = ChallengeEventRecord.Create("OtherGame", ChallengeEventType.SeasonalContent, false, false, true);
            AssertBlocked("OtherGame", r, ChallengeEventPolicy.Default, "PermanentProgressModificationBlocked");
        }

        [Test]
        public void CanActivateEvent_NonWonderPop_Valid_WithinWindow_Eligible()
        {
            var r = ChallengeEventRecord.Create("OtherGame", ChallengeEventType.EventUiState, true, false, false);
            AssertEligible("OtherGame", r, ChallengeEventPolicy.Default);
        }

        [Test]
        public void CanActivateEvent_NonWonderPop_Expired_HasFallback_UseLastKnownGood()
        {
            var r = ChallengeEventRecord.Create("OtherGame", ChallengeEventType.SeasonalContent, false, true, false);
            AssertBlocked("OtherGame", r, ChallengeEventPolicy.Default, "UseLastKnownGood");
        }

        [Test]
        public void CanActivateEvent_NonWonderPop_Expired_NoFallback_Blocked()
        {
            var r = ChallengeEventRecord.Create("OtherGame", ChallengeEventType.SeasonalContent, false, false, false);
            AssertBlocked("OtherGame", r, ChallengeEventPolicy.Default, "ExpiredEventRequiresFallback");
        }

        [Test]
        public void CanActivateEvent_AnyPolicy_PermanentMod_AlwaysBlocked()
        {
            var r = ChallengeEventRecord.Create("OtherGame", ChallengeEventType.EventReward, true, false, true);
            AssertBlocked("OtherGame", r, Custom, "PermanentProgressModificationBlocked");
            AssertBlocked("OtherGame", r, default(ChallengeEventPolicy), "PermanentProgressModificationBlocked");
        }

        [Test]
        public void CanActivateEvent_AnyPolicy_Expired_NeverActivates()
        {
            var withFallback = ChallengeEventRecord.Create("OtherGame", ChallengeEventType.ScheduledGoal, false, true, false);
            AssertBlocked("OtherGame", withFallback, Custom, "UseLastKnownGood");
            AssertBlocked("OtherGame", withFallback, default(ChallengeEventPolicy), "UseLastKnownGood");
            var noFallback = ChallengeEventRecord.Create("OtherGame", ChallengeEventType.ScheduledGoal, false, false, false);
            AssertBlocked("OtherGame", noFallback, Custom, "ExpiredEventRequiresFallback");
            AssertBlocked("OtherGame", noFallback, default(ChallengeEventPolicy), "ExpiredEventRequiresFallback");
        }

        [Test]
        public void CanActivateEvent_DefaultStructPolicy_FailsClosed()
        {
            var r = ChallengeEventRecord.Create("OtherGame", ChallengeEventType.EventReward, true, true, true);
            AssertBlocked("OtherGame", r, ChallengeEventPolicy.Default, "PermanentProgressModificationBlocked");
            AssertBlocked("OtherGame", r, default(ChallengeEventPolicy), "PermanentProgressModificationBlocked");
        }

        [Test]
        public void CanActivateEvent_GameIdMismatch_Blocked()
        {
            var r = ChallengeEventRecord.Create("Wonder_Pop", ChallengeEventType.ThemeCollection, true, false, false);
            AssertBlocked("Coral_Rescue", r, ChallengeEventPolicy.Default, "GameIdMismatch");
        }

        [Test]
        public void CanActivateEvent_WonderPopParam_RecordDiffers_GameIdMismatch()
        {
            var r = ChallengeEventRecord.Create("Coral_Rescue", ChallengeEventType.EventReward, true, true, true);
            AssertBlocked("Wonder_Pop", r, ChallengeEventPolicy.Default, "GameIdMismatch");
        }

        // Helpers
        [Test]
        public void RequiresFallback_Helper_True()
        {
            var r = ChallengeEventRecord.Create("G", ChallengeEventType.ScheduledGoal, false, false, false);
            Assert.IsTrue(LimitedTimeChallengeGate.RequiresFallback(r));
        }

        [Test]
        public void RequiresFallback_Helper_False()
        {
            var r = ChallengeEventRecord.Create("G", ChallengeEventType.ScheduledGoal, false, true, false);
            Assert.IsFalse(LimitedTimeChallengeGate.RequiresFallback(r));
        }

        [Test]
        public void IsExpired_Helper_True()
        {
            var r = ChallengeEventRecord.Create("G", ChallengeEventType.ScheduledGoal, false, true, false);
            Assert.IsTrue(LimitedTimeChallengeGate.IsExpired(r));
        }

        [Test]
        public void IsExpired_Helper_False()
        {
            var r = ChallengeEventRecord.Create("G", ChallengeEventType.ScheduledGoal, true, false, false);
            Assert.IsFalse(LimitedTimeChallengeGate.IsExpired(r));
        }

        // ChallengeEventLog
        [Test]
        public void ChallengeEventLog_Record_And_Count()
        {
            var log = new ChallengeEventLog();
            log.Record("GameA", "ScheduledGoal");
            log.Record("GameA", "EventReward");
            Assert.AreEqual(2, log.Count);
        }

        [Test]
        public void ChallengeEventLog_HasEntry_True()
        {
            var log = new ChallengeEventLog();
            log.Record("GameA", "ScheduledGoal");
            Assert.IsTrue(log.HasEntry("GameA"));
            Assert.IsFalse(log.HasEntry("GameB"));
        }

        [Test]
        public void ChallengeEventLog_Clear_ResetsCount()
        {
            var log = new ChallengeEventLog();
            log.Record("GameA", "ScheduledGoal");
            log.Clear();
            Assert.AreEqual(0, log.Count);
        }

        [Test]
        public void ChallengeEventLog_Record_NullGameId_Throws()
        {
            var log = new ChallengeEventLog();
            Assert.Throws<ArgumentException>(() => log.Record(null, "ScheduledGoal"));
            Assert.Throws<ArgumentException>(() => log.Record("", "ScheduledGoal"));
        }

        [Test]
        public void ChallengeEventLog_Record_NullOrEmptyEventType_Throws()
        {
            var log = new ChallengeEventLog();
            Assert.Throws<ArgumentException>(() => log.Record("GameA", null));
            Assert.Throws<ArgumentException>(() => log.Record("GameA", ""));
        }

        [Test]
        public void ChallengeEventLog_HasEntry_IsOrdinal()
        {
            var log = new ChallengeEventLog();
            log.Record("GameA", "ScheduledGoal");
            Assert.IsFalse(log.HasEntry("gamea"));
            Assert.IsFalse(log.HasEntry("Game"));
        }
    }
}
