using System;
using NUnit.Framework;
using Match3.Engine.Runtime.Privacy;

namespace Match3.Engine.Tests.Editor
{
    [TestFixture]
    public class RegionalStoreComplianceGateTests
    {
        [Test]
        public void ComplianceRecord_Create_NullGameId_Throws()
        {
            Assert.Throws<ArgumentException>(() => ComplianceRecord.Create(null, StoreRegion.GlobalDefault, false, false));
        }

        [Test]
        public void ComplianceRecord_Create_EmptyGameId_Throws()
        {
            Assert.Throws<ArgumentException>(() => ComplianceRecord.Create(string.Empty, StoreRegion.GlobalDefault, false, false));
        }

        [Test]
        public void ComplianceRecord_Create_Valid_Succeeds()
        {
            var r = ComplianceRecord.Create("Coral_Rescue", StoreRegion.EuropeanUnion, true, false);
            Assert.AreEqual("Coral_Rescue", r.GameId);
            Assert.AreEqual(StoreRegion.EuropeanUnion, r.Region);
            Assert.IsTrue(r.HasPolicyDeclaration);
            Assert.IsFalse(r.RequiresLegalReview);
        }

        [Test]
        public void ComplianceRecord_IsCompliant_HasDeclaration_NoLegal_True()
        {
            var r = ComplianceRecord.Create("Coral_Rescue", StoreRegion.GlobalDefault, true, false);
            Assert.IsTrue(r.IsCompliant);
        }

        [Test]
        public void ComplianceRecord_IsCompliant_NoDeclaration_False()
        {
            var r = ComplianceRecord.Create("Coral_Rescue", StoreRegion.GlobalDefault, false, false);
            Assert.IsFalse(r.IsCompliant);
        }

        [Test]
        public void ComplianceRecord_IsCompliant_HasDeclaration_RequiresLegal_False()
        {
            var r = ComplianceRecord.Create("Coral_Rescue", StoreRegion.GlobalDefault, true, true);
            Assert.IsFalse(r.IsCompliant);
        }

        [Test]
        public void ComplianceRecord_NeedsDeclaration_NoDeclaration_True()
        {
            var r = ComplianceRecord.Create("Coral_Rescue", StoreRegion.GlobalDefault, false, false);
            Assert.IsTrue(r.NeedsDeclaration);
        }

        [Test]
        public void ComplianceRecord_NeedsDeclaration_HasDeclaration_False()
        {
            var r = ComplianceRecord.Create("Coral_Rescue", StoreRegion.GlobalDefault, true, false);
            Assert.IsFalse(r.NeedsDeclaration);
        }

        [Test]
        public void RegionalCompliancePolicy_Safe_Values_Correct()
        {
            var p = RegionalCompliancePolicy.Safe("Test policy");
            Assert.IsTrue(p.RequirePolicyDeclaration);
            Assert.IsTrue(p.BlockWithoutDeclaration);
            Assert.AreEqual("Test policy", p.PolicyDescription);
        }

        [Test]
        public void RegionalCompliancePolicy_Default_NotNull()
        {
            var p = RegionalCompliancePolicy.Default;
            Assert.IsNotNull(p.PolicyDescription);
            Assert.IsTrue(p.RequirePolicyDeclaration);
            Assert.IsTrue(p.BlockWithoutDeclaration);
        }

        [Test]
        public void RegionalCompliancePolicy_NullDescription_Throws()
        {
            Assert.Throws<ArgumentException>(() => RegionalCompliancePolicy.Safe(null));
        }

        [Test]
        public void CanActivateForRegion_WonderPop_China_NoDeclaration_Blocked()
        {
            var record = ComplianceRecord.Create("Wonder_Pop", StoreRegion.China, false, false);
            var policy = RegionalCompliancePolicy.Default;
            var result = RegionalStoreComplianceGate.CanActivateForRegion("Wonder_Pop", record, policy, out var reason);
            Assert.IsFalse(result);
            Assert.AreEqual("WonderPopChinaDeclarationRequired", reason);
        }

        [Test]
        public void CanActivateForRegion_WonderPop_China_HasDeclaration_NotBlocked()
        {
            var record = ComplianceRecord.Create("Wonder_Pop", StoreRegion.China, true, false);
            var policy = RegionalCompliancePolicy.Default;
            var result = RegionalStoreComplianceGate.CanActivateForRegion("Wonder_Pop", record, policy, out var reason);
            Assert.IsTrue(result);
            Assert.AreEqual(string.Empty, reason);
        }

        [Test]
        public void CanActivateForRegion_WonderPop_EU_NoDeclaration_NotBlockedByGuard1()
        {
            var record = ComplianceRecord.Create("Wonder_Pop", StoreRegion.EuropeanUnion, false, false);
            var policy = RegionalCompliancePolicy.Default;
            var result = RegionalStoreComplianceGate.CanActivateForRegion("Wonder_Pop", record, policy, out var reason);
            Assert.IsFalse(result);
            Assert.AreEqual("PolicyDeclarationMissing", reason);
        }

        [Test]
        public void CanActivateForRegion_WonderPop_Lowercase_UnknownGame()
        {
            // Ordinal catalog lookup: a case variant of Wonder_Pop is not a catalog ID, so it fails closed with UnknownGame (never eligible, never the Wonder_Pop reason).
            var record = ComplianceRecord.Create("wonder_pop", StoreRegion.China, false, false);
            var policy = RegionalCompliancePolicy.Default;
            var result = RegionalStoreComplianceGate.CanActivateForRegion("wonder_pop", record, policy, out var reason);
            Assert.IsFalse(result);
            Assert.AreEqual("UnknownGame", reason);
        }

        [Test]
        public void CanActivateForRegion_NoDeclaration_SafePolicy_Blocked()
        {
            var record = ComplianceRecord.Create("Garage_Legends", StoreRegion.UnitedStates, false, false);
            var policy = RegionalCompliancePolicy.Default;
            var result = RegionalStoreComplianceGate.CanActivateForRegion("Garage_Legends", record, policy, out var reason);
            Assert.IsFalse(result);
            Assert.AreEqual("PolicyDeclarationMissing", reason);
        }

        [Test]
        public void CanActivateForRegion_HasDeclaration_NoLegal_Eligible()
        {
            var record = ComplianceRecord.Create("Garage_Legends", StoreRegion.UnitedStates, true, false);
            var policy = RegionalCompliancePolicy.Default;
            var result = RegionalStoreComplianceGate.CanActivateForRegion("Garage_Legends", record, policy, out var reason);
            Assert.IsTrue(result);
            Assert.AreEqual(string.Empty, reason);
        }

        [Test]
        public void CanActivateForRegion_LegalReviewPending_NoDeclaration_Blocked()
        {
            var record = ComplianceRecord.Create("Garage_Legends", StoreRegion.SouthKorea, false, true);
            var policy = RegionalCompliancePolicy.Default;
            var result = RegionalStoreComplianceGate.CanActivateForRegion("Garage_Legends", record, policy, out var reason);
            Assert.IsFalse(result);
            Assert.IsTrue(reason == "PolicyDeclarationMissing" || reason == "LegalReviewPending");
        }

        [Test]
        public void CanActivateForRegion_LegalReviewPending_HasDeclaration_Eligible()
        {
            var record = ComplianceRecord.Create("Garage_Legends", StoreRegion.SouthKorea, true, true);
            var policy = RegionalCompliancePolicy.Default;
            var result = RegionalStoreComplianceGate.CanActivateForRegion("Garage_Legends", record, policy, out var reason);
            Assert.IsTrue(result);
            Assert.AreEqual(string.Empty, reason);
        }

        [Test]
        public void CanActivateForRegion_GlobalDefault_HasDeclaration_Eligible()
        {
            var record = ComplianceRecord.Create("Coral_Rescue", StoreRegion.GlobalDefault, true, false);
            var policy = RegionalCompliancePolicy.Default;
            var result = RegionalStoreComplianceGate.CanActivateForRegion("Coral_Rescue", record, policy, out var reason);
            Assert.IsTrue(result);
            Assert.AreEqual(string.Empty, reason);
        }

        [Test]
        public void CanActivateForRegion_Brazil_HasDeclaration_Eligible()
        {
            var record = ComplianceRecord.Create("Coral_Rescue", StoreRegion.Brazil, true, false);
            var policy = RegionalCompliancePolicy.Default;
            var result = RegionalStoreComplianceGate.CanActivateForRegion("Coral_Rescue", record, policy, out var reason);
            Assert.IsTrue(result);
            Assert.AreEqual(string.Empty, reason);
        }

        [Test]
        public void IsCompliant_Helper_HasDeclaration_NoLegal_True()
        {
            var r = ComplianceRecord.Create("Coral_Rescue", StoreRegion.GlobalDefault, true, false);
            Assert.IsTrue(RegionalStoreComplianceGate.IsCompliant(r));
        }

        [Test]
        public void IsCompliant_Helper_NoDeclaration_False()
        {
            var r = ComplianceRecord.Create("Coral_Rescue", StoreRegion.GlobalDefault, false, false);
            Assert.IsFalse(RegionalStoreComplianceGate.IsCompliant(r));
        }

        [Test]
        public void NeedsDeclaration_Helper_NoDeclaration_True()
        {
            var r = ComplianceRecord.Create("Coral_Rescue", StoreRegion.GlobalDefault, false, false);
            Assert.IsTrue(RegionalStoreComplianceGate.NeedsDeclaration(r));
        }

        [Test]
        public void NeedsDeclaration_Helper_HasDeclaration_False()
        {
            var r = ComplianceRecord.Create("Coral_Rescue", StoreRegion.GlobalDefault, true, false);
            Assert.IsFalse(RegionalStoreComplianceGate.NeedsDeclaration(r));
        }

        [Test]
        public void ComplianceLog_Record_And_Count()
        {
            var log = new ComplianceLog();
            log.Record("Coral_Rescue", "EU");
            log.Record("Garage_Legends", "US");
            Assert.AreEqual(2, log.Count);
        }

        [Test]
        public void ComplianceLog_HasEntry_True()
        {
            var log = new ComplianceLog();
            log.Record("Coral_Rescue", "EU");
            Assert.IsTrue(log.HasEntry("Coral_Rescue"));
            Assert.IsFalse(log.HasEntry("Garage_Legends"));
        }

        [Test]
        public void ComplianceLog_Clear_ResetsCount()
        {
            var log = new ComplianceLog();
            log.Record("Coral_Rescue", "EU");
            log.Clear();
            Assert.AreEqual(0, log.Count);
        }

        // QA remediation: fail-closed default policy, GameId mismatch guard, log argument validation
        [Test]
        public void CanActivateForRegion_DefaultStructPolicy_FailsClosed()
        {
            var record = ComplianceRecord.Create("Coral_Rescue", StoreRegion.EuropeanUnion, false, false);
            var expected = RegionalStoreComplianceGate.CanActivateForRegion("Coral_Rescue", record, RegionalCompliancePolicy.Default, out var expectedReason);
            var result = RegionalStoreComplianceGate.CanActivateForRegion("Coral_Rescue", record, default(RegionalCompliancePolicy), out var reason);
            Assert.IsFalse(expected);
            Assert.IsFalse(result);
            Assert.AreEqual("PolicyDeclarationMissing", expectedReason);
            Assert.AreEqual(expectedReason, reason);
        }

        [Test]
        public void CanActivateForRegion_GameIdMismatch_Blocked()
        {
            var record = ComplianceRecord.Create("Wonder_Pop", StoreRegion.China, false, false);
            var result = RegionalStoreComplianceGate.CanActivateForRegion("Coral_Rescue", record, RegionalCompliancePolicy.Default, out var reason);
            Assert.IsFalse(result);
            Assert.AreEqual("GameIdMismatch", reason);
        }

        [Test]
        public void ComplianceLog_Record_NullOrEmptySecondArg_Throws()
        {
            var log = new ComplianceLog();
            Assert.Throws<System.ArgumentException>(() => log.Record("Coral_Rescue", null));
            Assert.Throws<System.ArgumentException>(() => log.Record("Coral_Rescue", string.Empty));
            Assert.AreEqual(0, log.Count);
        }

        [Test]
        public void ComplianceLog_HasEntry_IsOrdinal()
        {
            var log = new ComplianceLog();
            log.Record("Wonder_Pop", "evt");
            Assert.IsTrue(log.HasEntry("Wonder_Pop"));
            Assert.IsFalse(log.HasEntry("wonder_pop"));
            // Soft hyphen (U+00AD) is ignorable under culture-sensitive comparison; Ordinal must not match it.
            log.Record("Coral\u00AD_Rescue", "evt");
            Assert.IsTrue(log.HasEntry("Coral\u00AD_Rescue"));
            Assert.IsFalse(log.HasEntry("Coral_Rescue"));
        }


        [Test]
        public void NullOrEmptyGameId_DefaultRecord_GameIdMissing()
        {
            foreach (var gameId in new[] { null, string.Empty })
            {
                Assert.IsFalse(RegionalStoreComplianceGate.CanActivateForRegion(gameId, default(ComplianceRecord), default(RegionalCompliancePolicy), out var reason));
                Assert.AreEqual("GameIdMissing", reason);
            }
        }

        [Test]
        public void UnknownOrCaseVariantGameId_DefaultRecord_UnknownGame()
        {
            foreach (var gameId in new[] { "Unknown_Game", "coral_rescue", "CORAL_RESCUE", " Coral_Rescue", "Coral_Rescue ", "wonder_pop", "WONDER_POP" })
            {
                Assert.IsFalse(RegionalStoreComplianceGate.CanActivateForRegion(gameId, default(ComplianceRecord), default(RegionalCompliancePolicy), out var reason));
                Assert.AreEqual("UnknownGame", reason);
            }
        }
    }
}
