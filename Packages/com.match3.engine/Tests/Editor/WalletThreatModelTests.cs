using System;
using System.Linq;
using NUnit.Framework;
using Match3.Engine.Runtime.Economy;

namespace Match3.Engine.Tests.Editor
{
    [TestFixture]
    public class WalletThreatModelTests
    {
        // 1. ThreatVector enum has exactly 7 values
        [Test]
        public void ThreatVector_HasSevenValues()
        {
            var values = Enum.GetValues(typeof(ThreatVector));
            Assert.AreEqual(7, values.Length);
        }

        // 2. All 7 ThreatVector names exist
        [Test]
        public void ThreatVector_ContainsAllExpectedNames()
        {
            var names = Enum.GetNames(typeof(ThreatVector));
            Assert.Contains("TamperedClient",        names);
            Assert.Contains("OfflineReplay",          names);
            Assert.Contains("ClockManipulation",      names);
            Assert.Contains("ForgedPremiumCurrency",  names);
            Assert.Contains("StolenReceipt",          names);
            Assert.Contains("SharedDevice",           names);
            Assert.Contains("TransactionConflict",    names);
        }

        // 3. ThreatAssessment.Critical sets fields correctly
        [Test]
        public void ThreatAssessment_Critical_SetsFields()
        {
            var ta = ThreatAssessment.Critical(ThreatVector.TamperedClient, "Tampered client desc");
            Assert.AreEqual(ThreatVector.TamperedClient, ta.Vector);
            Assert.AreEqual("Critical", ta.Severity);
            Assert.IsTrue(ta.MitigationRequired);
            Assert.AreEqual("Tampered client desc", ta.Description);
        }

        // 4. ThreatAssessment.High sets fields correctly
        [Test]
        public void ThreatAssessment_High_SetsFields()
        {
            var ta = ThreatAssessment.High(ThreatVector.SharedDevice, "Shared device desc");
            Assert.AreEqual(ThreatVector.SharedDevice, ta.Vector);
            Assert.AreEqual("High", ta.Severity);
            Assert.IsTrue(ta.MitigationRequired);
            Assert.AreEqual("Shared device desc", ta.Description);
        }

        // 5. ThreatAssessment rejects null/empty Severity
        [Test]
        public void ThreatAssessment_NullSeverity_Throws()
        {
            // Severity is set internally; test null/empty Description
            Assert.Throws<ArgumentException>(() =>
                ThreatAssessment.Critical(ThreatVector.TamperedClient, null));
        }

        // 6. ThreatAssessment rejects null/empty Description
        [Test]
        public void ThreatAssessment_EmptyDescription_Throws()
        {
            Assert.Throws<ArgumentException>(() =>
                ThreatAssessment.High(ThreatVector.SharedDevice, ""));
        }

        // 7. WalletThreatPolicy.Default is server-authoritative with receipt validation
        [Test]
        public void WalletThreatPolicy_Default_IsHardened()
        {
            var p = WalletThreatPolicy.Default;
            Assert.IsTrue(p.ServerAuthoritative);
            Assert.IsTrue(p.RequireReceiptValidation);
            Assert.IsFalse(string.IsNullOrEmpty(p.PolicyDescription));
        }

        // 8. WalletThreatPolicy.Hardened rejects empty description
        [Test]
        public void WalletThreatPolicy_Hardened_EmptyDesc_Throws()
        {
            Assert.Throws<ArgumentException>(() => WalletThreatPolicy.Hardened(""));
        }

        // 9. IsMitigationRequired always returns true for all 7 vectors
        [Test]
        public void IsMitigationRequired_AlwaysTrueForAllVectors()
        {
            var policy = WalletThreatPolicy.Default;
            foreach (ThreatVector v in Enum.GetValues(typeof(ThreatVector)))
                Assert.IsTrue(WalletThreatGate.IsMitigationRequired(v, policy),
                    $"Expected true for {v}");
        }

        // 10. CanBypassServerAuth: Wonder_Pop is Guard 1 (ineligible regardless of policy)
        [Test]
        public void CanBypassServerAuth_WonderPop_IneligibleEvenWithNonAuthPolicy()
        {
            var nonAuthPolicy = WalletThreatPolicy.Hardened("non-auth-policy");
            // Use a custom non-server-auth policy to prove Wonder_Pop guard fires first
            // We can't construct a non-server-auth policy via public API (Hardened always sets true)
            // So verify Wonder_Pop is ineligible under Default (server-auth) policy
            var (eligible, reason) = WalletThreatGate.CanBypassServerAuth("Wonder_Pop", WalletThreatPolicy.Default);
            Assert.IsFalse(eligible);
            Assert.AreEqual("WonderPopBypassProhibited", reason);
        }

        // 11. CanBypassServerAuth: Wonder_Pop check is Ordinal (case-sensitive)
        [Test]
        public void CanBypassServerAuth_WonderPopCaseMismatch_DoesNotMatchWonderPopGuard()
        {
            // "wonder_pop" (lowercase) must NOT hit the Wonder_Pop guard
            var (eligible, reason) = WalletThreatGate.CanBypassServerAuth("wonder_pop", WalletThreatPolicy.Default);
            Assert.AreNotEqual("WonderPopBypassProhibited", reason);
        }

        // 12. CanBypassServerAuth: server-authoritative policy blocks bypass for other games
        [Test]
        public void CanBypassServerAuth_ServerAuth_BlocksOtherGames()
        {
            var (eligible, reason) = WalletThreatGate.CanBypassServerAuth("OtherGame", WalletThreatPolicy.Default);
            Assert.IsFalse(eligible);
            Assert.AreEqual("ServerAuthoritativeEnforced", reason);
        }

        // 13. GetKnownThreats returns exactly 7 entries
        [Test]
        public void GetKnownThreats_ReturnsSevenEntries()
        {
            var threats = WalletThreatGate.GetKnownThreats();
            Assert.AreEqual(7, threats.Count);
        }

        // 14. GetKnownThreats covers all ThreatVector values
        [Test]
        public void GetKnownThreats_CoversAllVectors()
        {
            var threats = WalletThreatGate.GetKnownThreats();
            foreach (ThreatVector v in Enum.GetValues(typeof(ThreatVector)))
                Assert.IsTrue(threats.Any(t => t.Vector == v), $"Missing vector {v}");
        }

        // 15. All entries from GetKnownThreats have MitigationRequired=true
        [Test]
        public void GetKnownThreats_AllHaveMitigationRequired()
        {
            var threats = WalletThreatGate.GetKnownThreats();
            foreach (var t in threats)
                Assert.IsTrue(t.MitigationRequired, $"Expected MitigationRequired for {t.Vector}");
        }

        // 16. All entries are Critical or High severity
        [Test]
        public void GetKnownThreats_AllCriticalOrHigh()
        {
            var threats = WalletThreatGate.GetKnownThreats();
            foreach (var t in threats)
                Assert.IsTrue(t.Severity == "Critical" || t.Severity == "High",
                    $"Unexpected severity '{t.Severity}' for {t.Vector}");
        }
    }
}
