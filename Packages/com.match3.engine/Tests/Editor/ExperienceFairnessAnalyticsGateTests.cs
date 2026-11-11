using System;
using NUnit.Framework;
using Match3.Engine.Runtime.Analytics;

namespace Match3.Engine.Tests.Editor
{
    [TestFixture]
    public class ExperienceFairnessAnalyticsGateTests
    {
        [Test]
        public void ExperienceRecord_Create_NullGameId_Throws()
        {
            Assert.Throws<ArgumentException>(() =>
                ExperienceRecord.Create(null, ExperienceEventKind.HintShown, false, false));
        }

        [Test]
        public void ExperienceRecord_Create_EmptyGameId_Throws()
        {
            Assert.Throws<ArgumentException>(() =>
                ExperienceRecord.Create(string.Empty, ExperienceEventKind.HintShown, false, false));
        }

        [Test]
        public void ExperienceRecord_Create_Valid_Succeeds()
        {
            var record = ExperienceRecord.Create("GameA", ExperienceEventKind.HintShown, false, false);
            Assert.AreEqual("GameA", record.GameId);
            Assert.AreEqual(ExperienceEventKind.HintShown, record.EventKind);
        }

        [Test]
        public void ExperienceRecord_IsEligiblePlayer_NotTestBot_NotConsentDenied_True()
        {
            var record = ExperienceRecord.Create("GameA", ExperienceEventKind.HintShown, false, false);
            Assert.IsTrue(record.IsEligiblePlayer);
        }

        [Test]
        public void ExperienceRecord_IsEligiblePlayer_IsTestBot_False()
        {
            var record = ExperienceRecord.Create("GameA", ExperienceEventKind.HintShown, true, false);
            Assert.IsFalse(record.IsEligiblePlayer);
        }

        [Test]
        public void ExperienceRecord_IsEligiblePlayer_IsConsentDenied_False()
        {
            var record = ExperienceRecord.Create("GameA", ExperienceEventKind.HintShown, false, true);
            Assert.IsFalse(record.IsEligiblePlayer);
        }

        [Test]
        public void ExperienceRecord_IsEligiblePlayer_BothFlags_False()
        {
            var record = ExperienceRecord.Create("GameA", ExperienceEventKind.HintShown, true, true);
            Assert.IsFalse(record.IsEligiblePlayer);
        }

        [Test]
        public void ExperienceFairnessPolicy_Safe_Values_Correct()
        {
            var policy = ExperienceFairnessPolicy.Safe("Test policy");
            Assert.IsFalse(policy.AllowTestBotEvents);
            Assert.IsFalse(policy.AllowConsentDeniedEvents);
        }

        [Test]
        public void ExperienceFairnessPolicy_Default_NotNull()
        {
            var policy = ExperienceFairnessPolicy.Default;
            Assert.IsNotNull(policy.PolicyDescription);
            Assert.IsNotEmpty(policy.PolicyDescription);
        }

        [Test]
        public void ExperienceFairnessPolicy_NullDescription_Throws()
        {
            Assert.Throws<ArgumentException>(() => ExperienceFairnessPolicy.Safe(null));
        }

        [Test]
        public void CanRecordEvent_WonderPop_Blocked()
        {
            var record = ExperienceRecord.Create("Wonder_Pop", ExperienceEventKind.HintShown, false, false);
            var policy = ExperienceFairnessPolicy.Default;
            bool result = ExperienceFairnessGate.CanRecordEvent("Wonder_Pop", record, policy, out string reason);
            Assert.IsFalse(result);
            Assert.AreEqual("WonderPopExperienceProhibited", reason);
        }

        [Test]
        public void CanRecordEvent_WonderPop_Lowercase_NotBlocked()
        {
            var record = ExperienceRecord.Create("wonder_pop", ExperienceEventKind.HintShown, false, false);
            var policy = ExperienceFairnessPolicy.Default;
            bool result = ExperienceFairnessGate.CanRecordEvent("wonder_pop", record, policy, out string reason);
            Assert.IsTrue(result);
            Assert.AreEqual(string.Empty, reason);
        }

        [Test]
        public void CanRecordEvent_TestBot_SafePolicy_Blocked()
        {
            var record = ExperienceRecord.Create("GameA", ExperienceEventKind.HintShown, true, false);
            var policy = ExperienceFairnessPolicy.Safe("Safe");
            bool result = ExperienceFairnessGate.CanRecordEvent("GameA", record, policy, out string reason);
            Assert.IsFalse(result);
            Assert.AreEqual("TestBotExcluded", reason);
        }

        [Test]
        public void CanRecordEvent_TestBot_AllowPolicy_Eligible()
        {
            var record = ExperienceRecord.Create("GameA", ExperienceEventKind.HintShown, true, false);
            var policy = ExperienceFairnessPolicy.Allow("Allow all");
            bool result = ExperienceFairnessGate.CanRecordEvent("GameA", record, policy, out string reason);
            Assert.IsTrue(result);
        }

        [Test]
        public void CanRecordEvent_ConsentDenied_SafePolicy_Blocked()
        {
            var record = ExperienceRecord.Create("GameA", ExperienceEventKind.HintShown, false, true);
            var policy = ExperienceFairnessPolicy.Safe("Safe");
            bool result = ExperienceFairnessGate.CanRecordEvent("GameA", record, policy, out string reason);
            Assert.IsFalse(result);
            Assert.AreEqual("ConsentDeniedExcluded", reason);
        }

        [Test]
        public void CanRecordEvent_ConsentDenied_AllowPolicy_Eligible()
        {
            var record = ExperienceRecord.Create("GameA", ExperienceEventKind.HintShown, false, true);
            var policy = ExperienceFairnessPolicy.Allow("Allow all");
            bool result = ExperienceFairnessGate.CanRecordEvent("GameA", record, policy, out string reason);
            Assert.IsTrue(result);
        }

        [Test]
        public void CanRecordEvent_EligiblePlayer_SafePolicy_Eligible()
        {
            var record = ExperienceRecord.Create("GameA", ExperienceEventKind.HintShown, false, false);
            var policy = ExperienceFairnessPolicy.Safe("Safe");
            bool result = ExperienceFairnessGate.CanRecordEvent("GameA", record, policy, out string reason);
            Assert.IsTrue(result);
            Assert.AreEqual(string.Empty, reason);
        }

        [Test]
        public void CanRecordEvent_AllEventKinds_EligiblePlayer_Eligible()
        {
            var policy = ExperienceFairnessPolicy.Default;
            ExperienceEventKind[] kinds = { ExperienceEventKind.LevelAbandon, ExperienceEventKind.StreakMilestone, ExperienceEventKind.DailyChallenge };
            foreach (var kind in kinds)
            {
                var record = ExperienceRecord.Create("GameA", kind, false, false);
                bool result = ExperienceFairnessGate.CanRecordEvent("GameA", record, policy, out string reason);
                Assert.IsTrue(result, "Expected eligible for " + kind);
            }
        }

        [Test]
        public void CanRecordEvent_LevelFirstAttemptWin_Eligible()
        {
            var record = ExperienceRecord.Create("GameA", ExperienceEventKind.LevelFirstAttemptWin, false, false);
            var policy = ExperienceFairnessPolicy.Default;
            bool result = ExperienceFairnessGate.CanRecordEvent("GameA", record, policy, out string reason);
            Assert.IsTrue(result);
        }

        [Test]
        public void CanRecordEvent_CatchUpTriggered_Eligible()
        {
            var record = ExperienceRecord.Create("GameA", ExperienceEventKind.CatchUpTriggered, false, false);
            var policy = ExperienceFairnessPolicy.Default;
            bool result = ExperienceFairnessGate.CanRecordEvent("GameA", record, policy, out string reason);
            Assert.IsTrue(result);
        }

        [Test]
        public void IsEligiblePlayer_Helper_EligiblePlayer_True()
        {
            var record = ExperienceRecord.Create("GameA", ExperienceEventKind.HintShown, false, false);
            Assert.IsTrue(ExperienceFairnessGate.IsEligiblePlayer(record));
        }

        [Test]
        public void IsEligiblePlayer_Helper_TestBot_False()
        {
            var record = ExperienceRecord.Create("GameA", ExperienceEventKind.HintShown, true, false);
            Assert.IsFalse(ExperienceFairnessGate.IsEligiblePlayer(record));
        }

        [Test]
        public void IsConsentDenied_Helper_ConsentDenied_True()
        {
            var record = ExperienceRecord.Create("GameA", ExperienceEventKind.HintShown, false, true);
            Assert.IsTrue(ExperienceFairnessGate.IsConsentDenied(record));
        }

        [Test]
        public void IsConsentDenied_Helper_EligiblePlayer_False()
        {
            var record = ExperienceRecord.Create("GameA", ExperienceEventKind.HintShown, false, false);
            Assert.IsFalse(ExperienceFairnessGate.IsConsentDenied(record));
        }

        [Test]
        public void ExperienceEventLog_Record_And_Count()
        {
            var log = new ExperienceEventLog();
            log.Record("GameA", "HintShown");
            log.Record("GameA", "LevelAbandon");
            Assert.AreEqual(2, log.Count);
        }

        [Test]
        public void ExperienceEventLog_HasEntry_True()
        {
            var log = new ExperienceEventLog();
            log.Record("GameA", "HintShown");
            Assert.IsTrue(log.HasEntry("GameA"));
        }

        [Test]
        public void ExperienceEventLog_Clear_ResetsCount()
        {
            var log = new ExperienceEventLog();
            log.Record("GameA", "HintShown");
            log.Clear();
            Assert.AreEqual(0, log.Count);
        }

        [Test]
        public void ExperienceEventLog_Record_NullGameId_Throws()
        {
            var log = new ExperienceEventLog();
            Assert.Throws<ArgumentException>(() => log.Record(null, "HintShown"));
        }

        [Test]
        public void ExperienceEventLog_Record_NullOrEmptyEventName_Throws()
        {
            var log = new ExperienceEventLog();
            Assert.Throws<ArgumentException>(() => log.Record("GameA", null));
            Assert.Throws<ArgumentException>(() => log.Record("GameA", ""));
        }

        [Test]
        public void ExperienceEventLog_HasEntry_IsOrdinal()
        {
            var log = new ExperienceEventLog();
            log.Record("GameA", "HintShown");
            Assert.IsFalse(log.HasEntry("gamea"));
            Assert.IsFalse(log.HasEntry("Game"));
        }

        [Test]
        public void CanRecordEvent_DefaultStructPolicy_FailsClosed()
        {
            var record = ExperienceRecord.Create("GameA", ExperienceEventKind.HintShown, true, false);
            Assert.IsFalse(ExperienceFairnessGate.CanRecordEvent("GameA", record, ExperienceFairnessPolicy.Default, out string expected));
            Assert.IsFalse(ExperienceFairnessGate.CanRecordEvent("GameA", record, default(ExperienceFairnessPolicy), out string reason));
            Assert.AreEqual(expected, reason);
            Assert.AreEqual("TestBotExcluded", reason);
        }

        [Test]
        public void CanRecordEvent_GameIdMismatch_Blocked()
        {
            var record = ExperienceRecord.Create("Wonder_Pop", ExperienceEventKind.HintShown, false, false);
            Assert.IsFalse(ExperienceFairnessGate.CanRecordEvent("Coral_Rescue", record, ExperienceFairnessPolicy.Default, out string reason));
            Assert.AreEqual("GameIdMismatch", reason);
        }

        [Test]
        public void CanRecordEvent_WonderPopParam_RecordDiffers_GameIdMismatch()
        {
            var record = ExperienceRecord.Create("Coral_Rescue", ExperienceEventKind.HintShown, false, false);
            Assert.IsFalse(ExperienceFairnessGate.CanRecordEvent("Wonder_Pop", record, ExperienceFairnessPolicy.Default, out string reason));
            Assert.AreEqual("GameIdMismatch", reason);
        }
    }
}
