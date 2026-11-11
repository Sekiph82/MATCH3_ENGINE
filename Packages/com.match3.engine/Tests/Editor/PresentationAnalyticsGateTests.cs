using System;
using NUnit.Framework;
using Match3.Engine.Runtime.Analytics;

namespace Match3.Engine.Tests.Editor
{
    [TestFixture]
    public class PresentationAnalyticsGateTests
    {
        [Test]
        public void PresentationRecord_Create_NullGameId_Throws()
        {
            Assert.Throws<ArgumentException>(() =>
                PresentationRecord.Create(null, PresentationEventKind.TutorialShown, false, false));
        }

        [Test]
        public void PresentationRecord_Create_EmptyGameId_Throws()
        {
            Assert.Throws<ArgumentException>(() =>
                PresentationRecord.Create("", PresentationEventKind.TutorialShown, false, false));
        }

        [Test]
        public void PresentationRecord_Create_Valid_Succeeds()
        {
            var record = PresentationRecord.Create("TestGame", PresentationEventKind.BannerDisplayed, false, false);
            Assert.AreEqual("TestGame", record.GameId);
            Assert.AreEqual(PresentationEventKind.BannerDisplayed, record.EventKind);
        }

        [Test]
        public void PresentationRecord_IsEligibleSession_NotTestSession_NotConsentDenied_True()
        {
            var record = PresentationRecord.Create("TestGame", PresentationEventKind.PopupShown, false, false);
            Assert.IsTrue(record.IsEligibleSession);
        }

        [Test]
        public void PresentationRecord_IsEligibleSession_IsTestSession_False()
        {
            var record = PresentationRecord.Create("TestGame", PresentationEventKind.PopupShown, true, false);
            Assert.IsFalse(record.IsEligibleSession);
        }

        [Test]
        public void PresentationRecord_IsEligibleSession_IsConsentDenied_False()
        {
            var record = PresentationRecord.Create("TestGame", PresentationEventKind.PopupShown, false, true);
            Assert.IsFalse(record.IsEligibleSession);
        }

        [Test]
        public void PresentationRecord_IsEligibleSession_BothFlags_False()
        {
            var record = PresentationRecord.Create("TestGame", PresentationEventKind.PopupShown, true, true);
            Assert.IsFalse(record.IsEligibleSession);
        }

        [Test]
        public void PresentationAnalyticsPolicy_Safe_Values_Correct()
        {
            var policy = PresentationAnalyticsPolicy.Safe("Test policy");
            Assert.IsFalse(policy.AllowTestSessionEvents);
            Assert.AreEqual("Test policy", policy.PolicyDescription);
        }

        [Test]
        public void PresentationAnalyticsPolicy_Default_NotNull()
        {
            var policy = PresentationAnalyticsPolicy.Default;
            Assert.IsNotNull(policy.PolicyDescription);
            Assert.IsFalse(policy.AllowTestSessionEvents);
        }

        [Test]
        public void PresentationAnalyticsPolicy_NullDescription_Throws()
        {
            Assert.Throws<ArgumentException>(() => PresentationAnalyticsPolicy.Safe(null));
        }

        [Test]
        public void CanRecordEvent_WonderPop_Blocked()
        {
            var record = PresentationRecord.Create("Wonder_Pop", PresentationEventKind.BannerDisplayed, false, false);
            var policy = PresentationAnalyticsPolicy.Default;
            var result = PresentationAnalyticsGate.CanRecordEvent("Wonder_Pop", record, policy, out string reason);
            Assert.IsFalse(result);
            Assert.AreEqual("WonderPopPresentationProhibited", reason);
        }

        [Test]
        public void CanRecordEvent_WonderPop_Lowercase_NotBlocked()
        {
            var record = PresentationRecord.Create("wonder_pop", PresentationEventKind.BannerDisplayed, false, false);
            var policy = PresentationAnalyticsPolicy.Default;
            var result = PresentationAnalyticsGate.CanRecordEvent("wonder_pop", record, policy, out string reason);
            Assert.IsTrue(result);
            Assert.AreEqual(string.Empty, reason);
        }

        [Test]
        public void CanRecordEvent_TestSession_SafePolicy_Blocked()
        {
            var record = PresentationRecord.Create("GameA", PresentationEventKind.PopupShown, true, false);
            var policy = PresentationAnalyticsPolicy.Default;
            var result = PresentationAnalyticsGate.CanRecordEvent("GameA", record, policy, out string reason);
            Assert.IsFalse(result);
            Assert.AreEqual("TestSessionExcluded", reason);
        }

        [Test]
        public void CanRecordEvent_TestSession_AllowPolicy_Eligible()
        {
            var record = PresentationRecord.Create("GameA", PresentationEventKind.PopupShown, true, false);
            var policy = PresentationAnalyticsPolicy.AllowTest("Allow test sessions");
            var result = PresentationAnalyticsGate.CanRecordEvent("GameA", record, policy, out string reason);
            Assert.IsTrue(result);
            Assert.AreEqual(string.Empty, reason);
        }

        [Test]
        public void CanRecordEvent_ConsentDenied_SafePolicy_Blocked()
        {
            var record = PresentationRecord.Create("GameA", PresentationEventKind.PopupShown, false, true);
            var policy = PresentationAnalyticsPolicy.Default;
            var result = PresentationAnalyticsGate.CanRecordEvent("GameA", record, policy, out string reason);
            Assert.IsFalse(result);
            Assert.AreEqual("ConsentDeniedExcluded", reason);
        }

        [Test]
        public void CanRecordEvent_ConsentDenied_MostRelaxedPolicy_StillBlocked()
        {
            var record = PresentationRecord.Create("GameA", PresentationEventKind.PopupShown, false, true);
            var policy = PresentationAnalyticsPolicy.AllowTest("Most relaxed constructible policy");
            Assert.IsFalse(PresentationAnalyticsGate.CanRecordEvent("GameA", record, policy, out string reason));
            Assert.AreEqual("ConsentDeniedExcluded", reason);
            Assert.IsFalse(PresentationAnalyticsGate.CanRecordEvent("GameA", record, default(PresentationAnalyticsPolicy), out reason));
            Assert.AreEqual("ConsentDeniedExcluded", reason);
        }

        [Test]
        public void CanRecordEvent_AllowTestSessions_DoesNotAdmitConsentDeniedTestSession()
        {
            var record = PresentationRecord.Create("GameA", PresentationEventKind.PopupShown, true, true);
            var policy = PresentationAnalyticsPolicy.AllowTest("Allow test sessions");
            Assert.IsFalse(PresentationAnalyticsGate.CanRecordEvent("GameA", record, policy, out string reason));
            Assert.AreEqual("ConsentDeniedExcluded", reason);
        }

        [Test]
        public void CanRecordEvent_EligibleSession_SafePolicy_Eligible()
        {
            var record = PresentationRecord.Create("GameA", PresentationEventKind.PopupShown, false, false);
            var policy = PresentationAnalyticsPolicy.Default;
            var result = PresentationAnalyticsGate.CanRecordEvent("GameA", record, policy, out string reason);
            Assert.IsTrue(result);
            Assert.AreEqual(string.Empty, reason);
        }

        [Test]
        public void CanRecordEvent_TutorialShown_Eligible()
        {
            var record = PresentationRecord.Create("GameA", PresentationEventKind.TutorialShown, false, false);
            var policy = PresentationAnalyticsPolicy.Default;
            Assert.IsTrue(PresentationAnalyticsGate.CanRecordEvent("GameA", record, policy, out _));
        }

        [Test]
        public void CanRecordEvent_RewardVideoShown_Eligible()
        {
            var record = PresentationRecord.Create("GameA", PresentationEventKind.RewardVideoShown, false, false);
            var policy = PresentationAnalyticsPolicy.Default;
            Assert.IsTrue(PresentationAnalyticsGate.CanRecordEvent("GameA", record, policy, out _));
        }

        [Test]
        public void CanRecordEvent_SeasonPassShown_Eligible()
        {
            var record = PresentationRecord.Create("GameA", PresentationEventKind.SeasonPassShown, false, false);
            var policy = PresentationAnalyticsPolicy.Default;
            Assert.IsTrue(PresentationAnalyticsGate.CanRecordEvent("GameA", record, policy, out _));
        }

        [Test]
        public void IsEligibleSession_Helper_EligibleSession_True()
        {
            var record = PresentationRecord.Create("GameA", PresentationEventKind.PopupShown, false, false);
            Assert.IsTrue(PresentationAnalyticsGate.IsEligibleSession(record));
        }

        [Test]
        public void IsEligibleSession_Helper_TestSession_False()
        {
            var record = PresentationRecord.Create("GameA", PresentationEventKind.PopupShown, true, false);
            Assert.IsFalse(PresentationAnalyticsGate.IsEligibleSession(record));
        }

        [Test]
        public void IsConsentDenied_Helper_ConsentDenied_True()
        {
            var record = PresentationRecord.Create("GameA", PresentationEventKind.PopupShown, false, true);
            Assert.IsTrue(PresentationAnalyticsGate.IsConsentDenied(record));
        }

        [Test]
        public void IsConsentDenied_Helper_EligibleSession_False()
        {
            var record = PresentationRecord.Create("GameA", PresentationEventKind.PopupShown, false, false);
            Assert.IsFalse(PresentationAnalyticsGate.IsConsentDenied(record));
        }

        [Test]
        public void PresentationEventLog_Record_And_Count()
        {
            var log = new PresentationEventLog();
            log.Record("GameA", "BannerDisplayed");
            log.Record("GameA", "PopupShown");
            Assert.AreEqual(2, log.Count);
        }

        [Test]
        public void PresentationEventLog_HasEntry_True()
        {
            var log = new PresentationEventLog();
            log.Record("GameA", "BannerDisplayed");
            Assert.IsTrue(log.HasEntry("GameA"));
        }

        [Test]
        public void PresentationEventLog_Clear_ResetsCount()
        {
            var log = new PresentationEventLog();
            log.Record("GameA", "BannerDisplayed");
            log.Clear();
            Assert.AreEqual(0, log.Count);
        }

        [Test]
        public void PresentationEventLog_Record_NullGameId_Throws()
        {
            var log = new PresentationEventLog();
            Assert.Throws<ArgumentException>(() => log.Record(null, "BannerDisplayed"));
        }

        [Test]
        public void PresentationEventLog_Record_NullOrEmptyEventName_Throws()
        {
            var log = new PresentationEventLog();
            Assert.Throws<ArgumentException>(() => log.Record("GameA", null));
            Assert.Throws<ArgumentException>(() => log.Record("GameA", ""));
        }

        [Test]
        public void PresentationEventLog_HasEntry_IsOrdinal()
        {
            var log = new PresentationEventLog();
            log.Record("GameA", "BannerDisplayed");
            Assert.IsFalse(log.HasEntry("gamea"));
            Assert.IsFalse(log.HasEntry("Game"));
            // Soft hyphen (U+00AD) is ignorable under culture-sensitive comparison; Ordinal must not match it.
            var softHyphenLog = new PresentationEventLog();
            softHyphenLog.Record("Game\u00ADA", "evt");
            Assert.IsFalse(softHyphenLog.HasEntry("GameA"));
        }

        [Test]
        public void CanRecordEvent_DefaultStructPolicy_FailsClosed()
        {
            var record = PresentationRecord.Create("GameA", PresentationEventKind.PopupShown, true, false);
            Assert.IsFalse(PresentationAnalyticsGate.CanRecordEvent("GameA", record, PresentationAnalyticsPolicy.Default, out string expected));
            Assert.IsFalse(PresentationAnalyticsGate.CanRecordEvent("GameA", record, default(PresentationAnalyticsPolicy), out string reason));
            Assert.AreEqual(expected, reason);
            Assert.AreEqual("TestSessionExcluded", reason);
        }

        [Test]
        public void CanRecordEvent_GameIdMismatch_Blocked()
        {
            var record = PresentationRecord.Create("Wonder_Pop", PresentationEventKind.PopupShown, false, false);
            Assert.IsFalse(PresentationAnalyticsGate.CanRecordEvent("Coral_Rescue", record, PresentationAnalyticsPolicy.Default, out string reason));
            Assert.AreEqual("GameIdMismatch", reason);
        }

        [Test]
        public void CanRecordEvent_WonderPopParam_RecordDiffers_GameIdMismatch()
        {
            var record = PresentationRecord.Create("Coral_Rescue", PresentationEventKind.PopupShown, false, false);
            Assert.IsFalse(PresentationAnalyticsGate.CanRecordEvent("Wonder_Pop", record, PresentationAnalyticsPolicy.Default, out string reason));
            Assert.AreEqual("GameIdMismatch", reason);
        }


        [Test]
        public void NullOrEmptyGameId_DefaultRecord_GameIdMissing()
        {
            foreach (var gameId in new[] { null, string.Empty })
            {
                Assert.IsFalse(PresentationAnalyticsGate.CanRecordEvent(gameId, default(PresentationRecord), default(PresentationAnalyticsPolicy), out var reason));
                Assert.AreEqual("GameIdMissing", reason);
            }
        }
    }
}
