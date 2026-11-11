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
            var r = ComplianceRecord.Create("TestGame", StoreRegion.EuropeanUnion, true, false);
            Assert.AreEqual("TestGame", r.GameId);
            Assert.AreEqual(StoreRegion.EuropeanUnion, r.Region);
            Assert.IsTrue(r.HasPolicyDeclaration);
            Assert.IsFalse(r.RequiresLegalReview);
        }

        [Test]
        public void ComplianceRecord_IsCompliant_HasDeclaration_NoLegal_True()
        {
            var r = ComplianceRecord.Create("G1", StoreRegion.GlobalDefault, true, false);
            Assert.IsTrue(r.IsCompliant);
        }

        [Test]
        public void ComplianceRecord_IsCompliant_NoDeclaration_False()
        {
            var r = ComplianceRecord.Create("G1", StoreRegion.GlobalDefault, false, false);
            Assert.IsFalse(r.IsCompliant);
        }

        [Test]
        public void ComplianceRecord_IsCompliant_HasDeclaration_RequiresLegal_False()
        {
            var r = ComplianceRecord.Create("G1", StoreRegion.GlobalDefault, true, true);
            Assert.IsFalse(r.IsCompliant);
        }

        [Test]
        public void ComplianceRecord_NeedsDeclaration_NoDeclaration_True()
        {
            var r = ComplianceRecord.Create("G1", StoreRegion.GlobalDefault, false, false);
            Assert.IsTrue(r.NeedsDeclaration);
        }

        [Test]
        public void ComplianceRecord_NeedsDeclaration_HasDeclaration_False()
        {
            var r = ComplianceRecord.Create("G1", StoreRegion.GlobalDefault, true, false);
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
        public void CanActivateForRegion_WonderPop_Lowercase_NotBlocked()
        {
            var record = ComplianceRecord.Create("wonder_pop", StoreRegion.China, false, false);
            var policy = RegionalCompliancePolicy.Default;
            var result = RegionalStoreComplianceGate.CanActivateForRegion("wonder_pop", record, policy, out var reason);
            Assert.IsFalse(result);
            Assert.AreEqual("PolicyDeclarationMissing", reason);
        }

        [Test]
        public void CanActivateForRegion_NoDeclaration_SafePolicy_Blocked()
        {
            var record = ComplianceRecord.Create("OtherGame", StoreRegion.UnitedStates, false, false);
            var policy = RegionalCompliancePolicy.Default;
            var result = RegionalStoreComplianceGate.CanActivateForRegion("OtherGame", record, policy, out var reason);
            Assert.IsFalse(result);
            Assert.AreEqual("PolicyDeclarationMissing", reason);
        }

        [Test]
        public void CanActivateForRegion_HasDeclaration_NoLegal_Eligible()
        {
            var record = ComplianceRecord.Create("OtherGame", StoreRegion.UnitedStates, true, false);
            var policy = RegionalCompliancePolicy.Default;
            var result = RegionalStoreComplianceGate.CanActivateForRegion("OtherGame", record, policy, out var reason);
            Assert.IsTrue(result);
            Assert.AreEqual(string.Empty, reason);
        }

        [Test]
        public void CanActivateForRegion_LegalReviewPending_NoDeclaration_Blocked()
        {
            var record = ComplianceRecord.Create("OtherGame", StoreRegion.SouthKorea, false, true);
            var policy = RegionalCompliancePolicy.Default;
            var result = RegionalStoreComplianceGate.CanActivateForRegion("OtherGame", record, policy, out var reason);
            Assert.IsFalse(result);
            Assert.IsTrue(reason == "PolicyDeclarationMissing" || reason == "LegalReviewPending");
        }

        [Test]
        public void CanActivateForRegion_LegalReviewPending_HasDeclaration_Eligible()
        {
            var record = ComplianceRecord.Create("OtherGame", StoreRegion.SouthKorea, true, true);
            var policy = RegionalCompliancePolicy.Default;
            var result = RegionalStoreComplianceGate.CanActivateForRegion("OtherGame", record, policy, out var reason);
            Assert.IsTrue(result);
            Assert.AreEqual(string.Empty, reason);
        }

        [Test]
        public void CanActivateForRegion_GlobalDefault_HasDeclaration_Eligible()
        {
            var record = ComplianceRecord.Create("AnyGame", StoreRegion.GlobalDefault, true, false);
            var policy = RegionalCompliancePolicy.Default;
            var result = RegionalStoreComplianceGate.CanActivateForRegion("AnyGame", record, policy, out var reason);
            Assert.IsTrue(result);
            Assert.AreEqual(string.Empty, reason);
        }

        [Test]
        public void CanActivateForRegion_Brazil_HasDeclaration_Eligible()
        {
            var record = ComplianceRecord.Create("AnyGame", StoreRegion.Brazil, true, false);
            var policy = RegionalCompliancePolicy.Default;
            var result = RegionalStoreComplianceGate.CanActivateForRegion("AnyGame", record, policy, out var reason);
            Assert.IsTrue(result);
            Assert.AreEqual(string.Empty, reason);
        }

        [Test]
        public void IsCompliant_Helper_HasDeclaration_NoLegal_True()
        {
            var r = ComplianceRecord.Create("G1", StoreRegion.GlobalDefault, true, false);
            Assert.IsTrue(RegionalStoreComplianceGate.IsCompliant(r));
        }

        [Test]
        public void IsCompliant_Helper_NoDeclaration_False()
        {
            var r = ComplianceRecord.Create("G1", StoreRegion.GlobalDefault, false, false);
            Assert.IsFalse(RegionalStoreComplianceGate.IsCompliant(r));
        }

        [Test]
        public void NeedsDeclaration_Helper_NoDeclaration_True()
        {
            var r = ComplianceRecord.Create("G1", StoreRegion.GlobalDefault, false, false);
            Assert.IsTrue(RegionalStoreComplianceGate.NeedsDeclaration(r));
        }

        [Test]
        public void NeedsDeclaration_Helper_HasDeclaration_False()
        {
            var r = ComplianceRecord.Create("G1", StoreRegion.GlobalDefault, true, false);
            Assert.IsFalse(RegionalStoreComplianceGate.NeedsDeclaration(r));
        }

        [Test]
        public void ComplianceLog_Record_And_Count()
        {
            var log = new ComplianceLog();
            log.Record("GameA", "EU");
            log.Record("GameB", "US");
            Assert.AreEqual(2, log.Count);
        }

        [Test]
        public void ComplianceLog_HasEntry_True()
        {
            var log = new ComplianceLog();
            log.Record("GameA", "EU");
            Assert.IsTrue(log.HasEntry("GameA"));
            Assert.IsFalse(log.HasEntry("GameB"));
        }

        [Test]
        public void ComplianceLog_Clear_ResetsCount()
        {
            var log = new ComplianceLog();
            log.Record("GameA", "EU");
            log.Clear();
            Assert.AreEqual(0, log.Count);
        }
    }
}
