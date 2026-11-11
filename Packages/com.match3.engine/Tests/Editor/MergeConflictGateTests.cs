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
                new MergePolicy(MergeStrategy.LocalWins, null));
        }

        [Test]
        public void MergePolicy_EmptyDescription_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() =>
                new MergePolicy(MergeStrategy.LocalWins, ""));
        }

        [Test]
        public void MergePolicy_Safe_SetsManualRequiredAndUserConfirmation()
        {
            var policy = MergePolicy.Safe("test");
            Assert.AreEqual(MergeStrategy.ManualRequired, policy.DefaultStrategy);
            Assert.IsTrue(MergeConflictGate.RequiresConfirmation(policy));
            Assert.AreEqual("test", policy.PolicyDescription);
        }

        [Test]
        public void MergePolicy_Default_IsManualAndRequiresConfirmation()
        {
            var policy = MergePolicy.Default;
            Assert.AreEqual(MergeStrategy.ManualRequired, policy.DefaultStrategy);
            Assert.IsTrue(MergeConflictGate.RequiresConfirmation(policy));
        }

        // --- MergeConflictGate.CanAutoResolve ---

        [Test]
        public void CanAutoResolve_WonderPop_ExactCase_IsIneligible_Guard1()
        {
            var conflict = ConflictRecord.Create("Wonder_Pop", 1, 2);
            var policy = new MergePolicy(MergeStrategy.LocalWins, "allow all");
            var result = MergeConflictGate.CanAutoResolve("Wonder_Pop", conflict, policy);
            Assert.IsFalse(result.IsEligible);
            Assert.AreEqual("WonderPopMergeProhibited", result.Reason);
        }

        [Test]
        public void CanAutoResolve_WonderPop_Lowercase_IsNotBlocked_OrdinalCheck()
        {
            // "wonder_pop" lowercase is NOT blocked — Ordinal comparison is case-sensitive
            var conflict = ConflictRecord.Create("wonder_pop", 1, 2);
            var policy = new MergePolicy(MergeStrategy.LocalWins, "allow all");
            var result = MergeConflictGate.CanAutoResolve("wonder_pop", conflict, policy);
            Assert.AreNotEqual("WonderPopMergeProhibited", result.Reason);
        }

        [Test]
        public void CanAutoResolve_RequireUserConfirmation_IsIneligible_Guard2()
        {
            var conflict = ConflictRecord.Create("GameA", 1, 2);
            var policy = new MergePolicy(MergeStrategy.LocalWins, "needs confirmation");
            var result = MergeConflictGate.CanAutoResolve("GameA", conflict, policy);
            Assert.IsFalse(result.IsEligible);
            Assert.AreEqual("UserConfirmationRequired", result.Reason);
        }

        [Test]
        public void CanAutoResolve_ManualStrategy_IsIneligible_Guard3()
        {
            var conflict = ConflictRecord.Create("GameA", 1, 2);
            var policy = new MergePolicy(MergeStrategy.ManualRequired, "manual only");
            var result = MergeConflictGate.CanAutoResolve("GameA", conflict, policy);
            Assert.IsFalse(result.IsEligible);
            Assert.AreEqual("UserConfirmationRequired", result.Reason);
        }

        [Test]
        public void CanAutoResolve_LocalWins_NoConfirmation_Divergent_NoSilentOverwrite()
        {
            var conflict = ConflictRecord.Create("GameA", 1, 2);
            var policy = new MergePolicy(MergeStrategy.LocalWins, "auto ok");
            var result = MergeConflictGate.CanAutoResolve("GameA", conflict, policy);
            Assert.IsFalse(result.IsEligible);
            Assert.AreEqual("UserConfirmationRequired", result.Reason);
        }

        [Test]
        public void CanAutoResolve_RemoteWins_NoConfirmation_Divergent_NoSilentOverwrite()
        {
            var conflict = ConflictRecord.Create("GameB", 2, 3);
            var policy = new MergePolicy(MergeStrategy.RemoteWins, "auto remote");
            var result = MergeConflictGate.CanAutoResolve("GameB", conflict, policy);
            Assert.IsFalse(result.IsEligible);
            Assert.AreEqual("UserConfirmationRequired", result.Reason);
        }

        [Test]
        public void CanAutoResolve_EqualRevisions_MostRelaxedPolicy_StillRequiresConfirmation()
        {
            // Two devices editing offline from the same base can both reach revision N+1:
            // equal revisions do not prove a lossless merge.
            var conflict = ConflictRecord.Create("GameB", 3, 3);
#pragma warning disable 0618
            var mostRelaxed = new MergePolicy(MergeStrategy.RemoteWins, false, "most relaxed");
#pragma warning restore 0618
            var result = MergeConflictGate.CanAutoResolve("GameB", conflict, mostRelaxed);
            Assert.IsFalse(result.IsEligible);
            Assert.AreEqual("UserConfirmationRequired", result.Reason);
        }

        [Test]
        public void CanAutoResolve_AnyConflict_RequiresConfirmationUnderEveryPolicy()
        {
            foreach (var policy in AllConstructiblePolicies)
                foreach (var conflict in new[] { ConflictRecord.Create("GameA", 0, 0), ConflictRecord.Create("GameA", 5, 5), ConflictRecord.Create("GameA", 4, 7) })
                {
                    var result = MergeConflictGate.CanAutoResolve("GameA", conflict, policy);
                    Assert.IsFalse(result.IsEligible);
                    Assert.AreEqual("UserConfirmationRequired", result.Reason);
                    Assert.IsTrue(MergeConflictGate.RequiresConfirmation(policy));
                }
        }

        [Test]
        public void MergePolicy_ObsoleteRequireUserConfirmation_AlwaysTrue()
        {
#pragma warning disable 0618
            foreach (var policy in AllConstructiblePolicies)
                Assert.IsTrue(policy.RequireUserConfirmation);
#pragma warning restore 0618
        }

        private static readonly MergePolicy[] AllConstructiblePolicies =
        {
            MergePolicy.Default,
            MergePolicy.Safe("safe"),
            new MergePolicy(MergeStrategy.LocalWins, "relaxed local"),
            new MergePolicy(MergeStrategy.RemoteWins, "relaxed remote"),
            new MergePolicy(MergeStrategy.ManualRequired, "manual"),
#pragma warning disable 0618 // obsolete ctor: requireUserConfirmation=false must stay ineffective
            new MergePolicy(MergeStrategy.LocalWins, false, "relaxed via obsolete ctor"),
            new MergePolicy(MergeStrategy.RemoteWins, true, "confirm via obsolete ctor"),
#pragma warning restore 0618
            default(MergePolicy),
        };

        [Test]
        public void CanAutoResolve_Divergent_NeverAutoResolvedUnderAnyPolicy()
        {
            var conflict = ConflictRecord.Create("GameA", 4, 7);
            foreach (var policy in AllConstructiblePolicies)
                Assert.IsFalse(MergeConflictGate.CanAutoResolve("GameA", conflict, policy).IsEligible);
        }

        [Test]
        public void CanAutoResolve_DefaultStructPolicy_FailsClosed()
        {
            var conflict = ConflictRecord.Create("GameA", 0, 0);
            var expected = MergeConflictGate.CanAutoResolve("GameA", conflict, MergePolicy.Default);
            var result = MergeConflictGate.CanAutoResolve("GameA", conflict, default(MergePolicy));
            Assert.IsFalse(result.IsEligible);
            Assert.AreEqual(expected.Reason, result.Reason);
            Assert.AreEqual("UserConfirmationRequired", result.Reason);
            Assert.IsTrue(MergeConflictGate.RequiresConfirmation(default(MergePolicy)));
        }

        [Test]
        public void CanAutoResolve_GameIdMismatch_Blocked()
        {
            var conflict = ConflictRecord.Create("Wonder_Pop", 1, 1);
            var policy = new MergePolicy(MergeStrategy.LocalWins, "allow all");
            var result = MergeConflictGate.CanAutoResolve("Coral_Rescue", conflict, policy);
            Assert.IsFalse(result.IsEligible);
            Assert.AreEqual("GameIdMismatch", result.Reason);
        }

        [Test]
        public void CanAutoResolve_WonderPopParam_RecordDiffers_GameIdMismatch()
        {
            var conflict = ConflictRecord.Create("Coral_Rescue", 1, 1);
            var policy = new MergePolicy(MergeStrategy.LocalWins, "allow all");
            var result = MergeConflictGate.CanAutoResolve("Wonder_Pop", conflict, policy);
            Assert.IsFalse(result.IsEligible);
            Assert.AreEqual("GameIdMismatch", result.Reason);
        }

        [Test]
        public void AuditLog_Record_DefaultRecord_Throws()
        {
            var log = new MergeAuditLog();
            Assert.Throws<ArgumentException>(() => log.Record(default(ConflictRecord), MergeStrategy.ManualRequired, "r"));
        }

        [Test]
        public void CanAutoResolve_WonderPop_Guard1_FiresBeforeGuard2()
        {
            // Even if policy would pass Guard2, Wonder_Pop must be blocked by Guard1
            var conflict = ConflictRecord.Create("Wonder_Pop", 0, 0);
            var policy = new MergePolicy(MergeStrategy.LocalWins, "allow all");
            var result = MergeConflictGate.CanAutoResolve("Wonder_Pop", conflict, policy);
            Assert.AreEqual("WonderPopMergeProhibited", result.Reason);
        }

        // --- RequiresConfirmation ---

        [Test]
        public void RequiresConfirmation_True_ForSafePolicy()
        {
            var policy = new MergePolicy(MergeStrategy.LocalWins, "needs user");
            Assert.IsTrue(MergeConflictGate.RequiresConfirmation(policy));
        }

        [Test]
        public void RequiresConfirmation_True_EvenForRelaxedStrategy()
        {
            var policy = new MergePolicy(MergeStrategy.LocalWins, "auto ok");
            Assert.IsTrue(MergeConflictGate.RequiresConfirmation(policy));
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


        [Test]
        public void NullOrEmptyGameId_DefaultRecord_GameIdMissing()
        {
            foreach (var gameId in new[] { null, string.Empty })
            {
                var result = MergeConflictGate.CanAutoResolve(gameId, default(ConflictRecord), default(MergePolicy));
                Assert.IsFalse(result.IsEligible);
                Assert.AreEqual("GameIdMissing", result.Reason);
            }
        }
    }
}
