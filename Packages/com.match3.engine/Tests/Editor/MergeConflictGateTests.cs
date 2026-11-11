using System;
using NUnit.Framework;
using Match3.Engine.Runtime.Identity;

namespace Match3.Engine.Tests.Editor
{
    [TestFixture]
    public class MergeConflictGateTests
    {
        // --- ConflictRecord ---

        [Test]
        public void ConflictRecord_Create_NullGameId_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() => ConflictRecord.Create(null, 0, 0));
        }

        [Test]
        public void ConflictRecord_Create_EmptyGameId_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() => ConflictRecord.Create("", 0, 0));
        }

        [Test]
        public void ConflictRecord_Create_NegativeLocalRevision_ThrowsArgumentOutOfRangeException()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => ConflictRecord.Create("GameA", -1, 0));
        }

        [Test]
        public void ConflictRecord_Create_NegativeRemoteRevision_ThrowsArgumentOutOfRangeException()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => ConflictRecord.Create("GameA", 0, -1));
        }

        [Test]
        public void ConflictRecord_HasDivergence_SameRevisions_ReturnsFalse()
        {
            var record = ConflictRecord.Create("GameA", 5, 5);
            Assert.IsFalse(record.HasDivergence);
        }

        [Test]
        public void ConflictRecord_HasDivergence_DifferentRevisions_ReturnsTrue()
        {
            var record = ConflictRecord.Create("GameA", 3, 7);
            Assert.IsTrue(record.HasDivergence);
        }

        [Test]
        public void ConflictRecord_Create_ValidArgs_SetsProperties()
        {
            var record = ConflictRecord.Create("GameB", 1, 2);
            Assert.AreEqual("GameB", record.GameId);
            Assert.AreEqual(1, record.LocalRevision);
            Assert.AreEqual(2, record.RemoteRevision);
        }

        // --- MergePolicy ---

        [Test]
        public void MergePolicy_NullDescription_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() =>
                new MergePolicy(MergeStrategy.LocalWins, false, null));
        }

        [Test]
        public void MergePolicy_EmptyDescription_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() =>
                new MergePolicy(MergeStrategy.LocalWins, false, ""));
        }

        [Test]
        public void MergePolicy_Safe_SetsManualRequiredAndUserConfirmation()
        {
            var policy = MergePolicy.Safe("test");
            Assert.AreEqual(MergeStrategy.ManualRequired, policy.DefaultStrategy);
            Assert.IsTrue(policy.RequireUserConfirmation);
            Assert.AreEqual("test", policy.PolicyDescription);
        }

        [Test]
        public void MergePolicy_Default_IsManualAndRequiresConfirmation()
        {
            var policy = MergePolicy.Default;
            Assert.AreEqual(MergeStrategy.ManualRequired, policy.DefaultStrategy);
            Assert.IsTrue(policy.RequireUserConfirmation);
        }

        // --- MergeConflictGate.CanAutoResolve ---

        [Test]
        public void CanAutoResolve_WonderPop_ExactCase_IsIneligible_Guard1()
        {
            var conflict = ConflictRecord.Create("Wonder_Pop", 1, 2);
            var policy = new MergePolicy(MergeStrategy.LocalWins, false, "allow all");
            var result = MergeConflictGate.CanAutoResolve("Wonder_Pop", conflict, policy);
            Assert.IsFalse(result.IsEligible);
            Assert.AreEqual("WonderPopMergeProhibited", result.Reason);
        }

        [Test]
        public void CanAutoResolve_WonderPop_Lowercase_IsNotBlocked_OrdinalCheck()
        {
            // "wonder_pop" lowercase is NOT blocked — Ordinal comparison is case-sensitive
            var conflict = ConflictRecord.Create("wonder_pop", 1, 2);
            var policy = new MergePolicy(MergeStrategy.LocalWins, false, "allow all");
            var result = MergeConflictGate.CanAutoResolve("wonder_pop", conflict, policy);
            Assert.AreNotEqual("WonderPopMergeProhibited", result.Reason);
        }

        [Test]
        public void CanAutoResolve_RequireUserConfirmation_IsIneligible_Guard2()
        {
            var conflict = ConflictRecord.Create("GameA", 1, 2);
            var policy = new MergePolicy(MergeStrategy.LocalWins, true, "needs confirmation");
            var result = MergeConflictGate.CanAutoResolve("GameA", conflict, policy);
            Assert.IsFalse(result.IsEligible);
            Assert.AreEqual("UserConfirmationRequired", result.Reason);
        }

        [Test]
        public void CanAutoResolve_ManualStrategy_IsIneligible_Guard3()
        {
            var conflict = ConflictRecord.Create("GameA", 1, 2);
            var policy = new MergePolicy(MergeStrategy.ManualRequired, false, "manual only");
            var result = MergeConflictGate.CanAutoResolve("GameA", conflict, policy);
            Assert.IsFalse(result.IsEligible);
            Assert.AreEqual("ManualStrategyRequired", result.Reason);
        }

        [Test]
        public void CanAutoResolve_LocalWins_NoConfirmation_IsEligible()
        {
            var conflict = ConflictRecord.Create("GameA", 1, 2);
            var policy = new MergePolicy(MergeStrategy.LocalWins, false, "auto ok");
            var result = MergeConflictGate.CanAutoResolve("GameA", conflict, policy);
            Assert.IsTrue(result.IsEligible);
        }

        [Test]
        public void CanAutoResolve_RemoteWins_NoConfirmation_IsEligible()
        {
            var conflict = ConflictRecord.Create("GameB", 2, 3);
            var policy = new MergePolicy(MergeStrategy.RemoteWins, false, "auto remote");
            var result = MergeConflictGate.CanAutoResolve("GameB", conflict, policy);
            Assert.IsTrue(result.IsEligible);
        }

        [Test]
        public void CanAutoResolve_WonderPop_Guard1_FiresBeforeGuard2()
        {
            // Even if policy would pass Guard2, Wonder_Pop must be blocked by Guard1
            var conflict = ConflictRecord.Create("Wonder_Pop", 0, 0);
            var policy = new MergePolicy(MergeStrategy.LocalWins, false, "allow all");
            var result = MergeConflictGate.CanAutoResolve("Wonder_Pop", conflict, policy);
            Assert.AreEqual("WonderPopMergeProhibited", result.Reason);
        }

        // --- RequiresConfirmation ---

        [Test]
        public void RequiresConfirmation_True_WhenPolicyRequiresIt()
        {
            var policy = new MergePolicy(MergeStrategy.LocalWins, true, "needs user");
            Assert.IsTrue(MergeConflictGate.RequiresConfirmation(policy));
        }

        [Test]
        public void RequiresConfirmation_False_WhenPolicyDoesNotRequireIt()
        {
            var policy = new MergePolicy(MergeStrategy.LocalWins, false, "auto ok");
            Assert.IsFalse(MergeConflictGate.RequiresConfirmation(policy));
        }

        // --- IsDivergent ---

        [Test]
        public void IsDivergent_SameRevisions_ReturnsFalse()
        {
            var conflict = ConflictRecord.Create("GameA", 4, 4);
            Assert.IsFalse(MergeConflictGate.IsDivergent(conflict));
        }

        [Test]
        public void IsDivergent_DifferentRevisions_ReturnsTrue()
        {
            var conflict = ConflictRecord.Create("GameA", 1, 9);
            Assert.IsTrue(MergeConflictGate.IsDivergent(conflict));
        }

        // --- MergeAuditLog ---

        [Test]
        public void MergeAuditLog_Record_NullReason_ThrowsArgumentException()
        {
            var log = new MergeAuditLog();
            var record = ConflictRecord.Create("GameA", 0, 1);
            Assert.Throws<ArgumentException>(() => log.Record(record, MergeStrategy.LocalWins, null));
        }

        [Test]
        public void MergeAuditLog_Record_EmptyReason_ThrowsArgumentException()
        {
            var log = new MergeAuditLog();
            var record = ConflictRecord.Create("GameA", 0, 1);
            Assert.Throws<ArgumentException>(() => log.Record(record, MergeStrategy.LocalWins, ""));
        }

        [Test]
        public void MergeAuditLog_Record_IncreasesCount()
        {
            var log = new MergeAuditLog();
            var record = ConflictRecord.Create("GameA", 0, 1);
            log.Record(record, MergeStrategy.LocalWins, "applied local");
            Assert.AreEqual(1, log.Count);
        }

        [Test]
        public void MergeAuditLog_Clear_ResetsCount()
        {
            var log = new MergeAuditLog();
            var record = ConflictRecord.Create("GameA", 0, 1);
            log.Record(record, MergeStrategy.LocalWins, "applied local");
            log.Clear();
            Assert.AreEqual(0, log.Count);
        }

        [Test]
        public void MergeAuditLog_HasEntry_ReturnsTrueAfterRecord()
        {
            var log = new MergeAuditLog();
            var record = ConflictRecord.Create("GameA", 0, 1);
            log.Record(record, MergeStrategy.RemoteWins, "applied remote");
            Assert.IsTrue(log.HasEntry("GameA"));
        }

        [Test]
        public void MergeAuditLog_HasEntry_ReturnsFalseForMissingGame()
        {
            var log = new MergeAuditLog();
            Assert.IsFalse(log.HasEntry("NonExistent"));
        }

        [Test]
        public void MergeAuditLog_MultipleEntries_CountMatches()
        {
            var log = new MergeAuditLog();
            log.Record(ConflictRecord.Create("GameA", 1, 2), MergeStrategy.LocalWins, "r1");
            log.Record(ConflictRecord.Create("GameB", 3, 4), MergeStrategy.RemoteWins, "r2");
            log.Record(ConflictRecord.Create("GameC", 5, 6), MergeStrategy.ManualRequired, "r3");
            Assert.AreEqual(3, log.Count);
        }
    }
}
