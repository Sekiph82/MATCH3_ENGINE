using System;
using NUnit.Framework;
using Match3.Engine.Runtime.LiveOps;

namespace Match3.Engine.Tests.Editor
{
    public class LimitedTimeChallengeGateTests
    {
        private static readonly ChallengeEventPolicy NoRestrictions =
            new ChallengeEventPolicy(false, false, "No restrictions test policy");

        private static readonly ChallengeEventPolicy FallbackOnly =
            new ChallengeEventPolicy(true, false, "Fallback-only test policy");

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
            Assert.IsTrue(p.RequireFallbackOnExpiry);
            Assert.IsTrue(p.BlockPermanentProgressModification);
            Assert.AreEqual("test desc", p.PolicyDescription);
        }

        [Test]
        public void ChallengeEventPolicy_Default_NotNull()
        {
            var p = ChallengeEventPolicy.Default;
            Assert.IsNotNull(p.PolicyDescription);
            Assert.IsTrue(p.RequireFallbackOnExpiry);
            Assert.IsTrue(p.BlockPermanentProgressModification);
        }

        [Test]
        public void ChallengeEventPolicy_NullDescription_Throws()
        {
            Assert.Throws<ArgumentException>(() => ChallengeEventPolicy.Safe(null));
            Assert.Throws<ArgumentException>(() => ChallengeEventPolicy.Safe(""));
            Assert.Throws<ArgumentException>(() => new ChallengeEventPolicy(false, false, null));
        }

        // CanActivateEvent
        [Test]
        public void CanActivateEvent_WonderPop_AffectsPermanentProgress_Guard1()
        {
            var r = ChallengeEventRecord.Create("Wonder_Pop", ChallengeEventType.EventReward, true, true, true);
            AssertBlocked("Wonder_Pop", r, ChallengeEventPolicy.Default, "WonderPopPermanentProgressModificationProhibited");
        }

        [Test]
        public void CanActivateEvent_WonderPop_AffectsPermanentProgress_IsOwnerApproved_StillGuard1()
        {
            // Even the most permissive policy cannot bypass Guard 1
            var r = ChallengeEventRecord.Create("Wonder_Pop", ChallengeEventType.SeasonalContent, true, true, true);
            AssertBlocked("Wonder_Pop", r, NoRestrictions, "WonderPopPermanentProgressModificationProhibited");
            // Guard 1 precedes Guard 3 too (expired, no fallback)
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
        public void CanActivateEvent_WonderPop_NoPermanentMod_Expired_NoFallback_Guard3()
        {
            var r = ChallengeEventRecord.Create("Wonder_Pop", ChallengeEventType.ScheduledGoal, false, false, false);
            AssertBlocked("Wonder_Pop", r, ChallengeEventPolicy.Default, "ExpiredEventRequiresFallback");
        }

        [Test]
        public void CanActivateEvent_WonderPop_NoPermanentMod_Expired_HasFallback_Eligible()
        {
            var r = ChallengeEventRecord.Create("Wonder_Pop", ChallengeEventType.LastKnownGoodFallback, false, true, false);
            AssertEligible("Wonder_Pop", r, ChallengeEventPolicy.Default);
        }

        [Test]
        public void CanActivateEvent_WonderPop_Lowercase_PermanentMod_NotBlockedByGuard1()
        {
            var r = ChallengeEventRecord.Create("wonder_pop", ChallengeEventType.EventReward, true, true, true);
            AssertBlocked("wonder_pop", r, ChallengeEventPolicy.Default, "PermanentProgressModificationBlocked");
        }

        [Test]
        public void CanActivateEvent_NonWonderPop_PermanentMod_Guard2()
        {
            var r = ChallengeEventRecord.Create("OtherGame", ChallengeEventType.EventReward, true, true, true);
            AssertBlocked("OtherGame", r, ChallengeEventPolicy.Default, "PermanentProgressModificationBlocked");
        }

        [Test]
        public void CanActivateEvent_NonWonderPop_PermanentMod_NoPolicyBlock_ExpiredNoFallback_Guard3()
        {
            var r = ChallengeEventRecord.Create("OtherGame", ChallengeEventType.SeasonalContent, false, false, true);
            AssertBlocked("OtherGame", r, FallbackOnly, "ExpiredEventRequiresFallback");
        }

        [Test]
        public void CanActivateEvent_NonWonderPop_Valid_WithinWindow_Eligible()
        {
            var r = ChallengeEventRecord.Create("OtherGame", ChallengeEventType.EventUiState, true, false, false);
            AssertEligible("OtherGame", r, ChallengeEventPolicy.Default);
        }

        [Test]
        public void CanActivateEvent_NonWonderPop_Expired_HasFallback_Eligible()
        {
            var r = ChallengeEventRecord.Create("OtherGame", ChallengeEventType.SeasonalContent, false, true, false);
            AssertEligible("OtherGame", r, ChallengeEventPolicy.Default);
        }

        [Test]
        public void CanActivateEvent_NonWonderPop_Expired_NoFallback_Guard3()
        {
            var r = ChallengeEventRecord.Create("OtherGame", ChallengeEventType.SeasonalContent, false, false, false);
            AssertBlocked("OtherGame", r, ChallengeEventPolicy.Default, "ExpiredEventRequiresFallback");
        }

        [Test]
        public void CanActivateEvent_NoPolicyRestrictions_PermanentMod_Eligible()
        {
            var r = ChallengeEventRecord.Create("OtherGame", ChallengeEventType.EventReward, false, false, true);
            AssertEligible("OtherGame", r, NoRestrictions);
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
    }
}
