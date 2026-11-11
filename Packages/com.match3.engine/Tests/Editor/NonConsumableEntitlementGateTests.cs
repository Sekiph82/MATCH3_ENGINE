using System;
using NUnit.Framework;
using Match3.Engine.Runtime.Commerce;

namespace Match3.Engine.Tests.Editor
{
    public class NonConsumableEntitlementGateTests
    {
        // ---- EntitlementPolicy construction ----

        [Test]
        public void Policy_NullJustification_Throws()
        {
            Assert.Throws<ArgumentException>(() =>
                new EntitlementPolicy(true, true, null));
        }

        [Test]
        public void Policy_EmptyJustification_Throws()
        {
            Assert.Throws<ArgumentException>(() =>
                new EntitlementPolicy(true, true, string.Empty));
        }

        [Test]
        public void DefaultPolicy_IsRestricted()
        {
            var p = EntitlementPolicy.Default;
            Assert.IsFalse(p.PurchasesAllowed);
            Assert.IsTrue(p.RequireServerVerification);
            Assert.IsFalse(string.IsNullOrEmpty(p.PolicyJustification));
        }

        [Test]
        public void RestrictedFactory_SetsCorrectFlags()
        {
            var p = EntitlementPolicy.Restricted("test");
            Assert.IsFalse(p.PurchasesAllowed);
            Assert.IsTrue(p.RequireServerVerification);
        }

        [Test]
        public void UnrestrictedFactory_AllowsPurchasesWithServerVerification()
        {
            var p = EntitlementPolicy.Unrestricted("test");
            Assert.IsTrue(p.PurchasesAllowed);
            Assert.IsTrue(p.RequireServerVerification);
        }

        // ---- CheckEntitlementEligibility ----

        [Test]
        public void RestrictedPolicy_ReturnsIneligible()
        {
            var policy = EntitlementPolicy.Restricted("test");
            var kid = false; // non-kid profile
            var result = NonConsumableEntitlementGate.CheckEntitlementEligibility("SomeGame", policy, kid);
            Assert.IsFalse(result.IsEligible);
            Assert.AreEqual("EntitlementPurchasesRestricted", result.Reason);
        }

        [Test]
        public void WonderPop_AlwaysIneligible_EvenWithUnrestrictedPolicy()
        {
            var policy = EntitlementPolicy.Unrestricted("ok");
            var kid = false; // non-kid profile
            var result = NonConsumableEntitlementGate.CheckEntitlementEligibility("Wonder_Pop", policy, kid);
            Assert.IsFalse(result.IsEligible);
            Assert.AreEqual("WonderPopEntitlementProhibited", result.Reason);
        }

        [Test]
        public void WonderPop_BlockedBeforeKidCheck()
        {
            // Even if kid policy would also block, Wonder_Pop guard fires independently
            var policy = EntitlementPolicy.Unrestricted("ok");
            var kid = true; // kid profile
            var result = NonConsumableEntitlementGate.CheckEntitlementEligibility("Wonder_Pop", policy, kid);
            Assert.AreEqual("WonderPopEntitlementProhibited", result.Reason);
        }

        [Test]
        public void KidProfileIapProhibited_ReturnsIneligible()
        {
            var policy = EntitlementPolicy.Unrestricted("ok");
            var kid = true; // kid profile
            var result = NonConsumableEntitlementGate.CheckEntitlementEligibility("OtherGame", policy, kid);
            Assert.IsFalse(result.IsEligible);
            Assert.AreEqual("KidProfileEntitlementProhibited", result.Reason);
        }

        [Test]
        public void AllGuardsPass_ReturnsEligible()
        {
            var policy = EntitlementPolicy.Unrestricted("ok");
            var kid = false; // non-kid profile
            var result = NonConsumableEntitlementGate.CheckEntitlementEligibility("SomeGame", policy, kid);
            Assert.IsTrue(result.IsEligible);
        }

        [Test]
        public void WonderPop_WithRestrictedPolicy_ReturnsWonderPopReason()
        {
            var policy = EntitlementPolicy.Restricted("test");
            var kid = false; // non-kid profile
            var result = NonConsumableEntitlementGate.CheckEntitlementEligibility("Wonder_Pop", policy, kid);
            Assert.IsFalse(result.IsEligible);
            Assert.AreEqual("WonderPopEntitlementProhibited", result.Reason);
        }

        [Test]
        public void KidProfile_IneligibleForEveryGame()
        {
            var policy = EntitlementPolicy.Unrestricted("ok");
            foreach (var gameId in new[] { "Coral_Rescue", "Dino_Islands", "Garage_Legends", "The_Fractured_Realms" })
            {
                var result = NonConsumableEntitlementGate.CheckEntitlementEligibility(gameId, policy, true);
                Assert.IsFalse(result.IsEligible);
                Assert.AreEqual("KidProfileEntitlementProhibited", result.Reason);
            }
        }

        [Test]
        public void ObsoleteKidPolicyOverload_FailsClosed_EvenWithUnrestricted()
        {
#pragma warning disable 0618
            var result = NonConsumableEntitlementGate.CheckEntitlementEligibility(
                "Coral_Rescue", EntitlementPolicy.Unrestricted("ok"), KidProfilePolicy.Unrestricted("relaxed"));
#pragma warning restore 0618
            Assert.IsFalse(result.IsEligible);
            Assert.AreEqual("KidProfileEntitlementProhibited", result.Reason);
        }

        // ---- IsServerVerificationRequired ----

        [Test]
        public void IsServerVerificationRequired_AlwaysTrue_ForRestricted()
        {
            Assert.IsTrue(NonConsumableEntitlementGate.IsServerVerificationRequired(
                EntitlementPolicy.Restricted("test")));
        }

        [Test]
        public void IsServerVerificationRequired_AlwaysTrue_ForUnrestricted()
        {
            Assert.IsTrue(NonConsumableEntitlementGate.IsServerVerificationRequired(
                EntitlementPolicy.Unrestricted("test")));
        }

        // ---- CanGrantEntitlement ----

        [Test]
        public void CanGrantEntitlement_WonderPop_AlwaysFalse()
        {
            Assert.IsFalse(NonConsumableEntitlementGate.CanGrantEntitlement(
                "Wonder_Pop", EntitlementPolicy.Unrestricted("ok"), ReceiptVerificationStatus.VerifiedValid));
        }

        [Test]
        public void CanGrantEntitlement_RestrictedPolicy_False()
        {
            Assert.IsFalse(NonConsumableEntitlementGate.CanGrantEntitlement(
                "SomeGame", EntitlementPolicy.Restricted("test"), ReceiptVerificationStatus.VerifiedValid));
        }

        [Test]
        public void CanGrantEntitlement_Unrestricted_NonWonderPop_VerifiedValid_True()
        {
            Assert.IsTrue(NonConsumableEntitlementGate.CanGrantEntitlement(
                "SomeGame", EntitlementPolicy.Unrestricted("ok"), ReceiptVerificationStatus.VerifiedValid));
        }

        [Test]
        public void CanGrantEntitlement_NotVerifiedValid_AlwaysFalse()
        {
            foreach (ReceiptVerificationStatus status in Enum.GetValues(typeof(ReceiptVerificationStatus)))
            {
                if (status == ReceiptVerificationStatus.VerifiedValid) continue;
                Assert.IsFalse(NonConsumableEntitlementGate.CanGrantEntitlement(
                    "SomeGame", EntitlementPolicy.Unrestricted("ok"), status), status.ToString());
            }
        }

        [Test]
        public void CanGrantEntitlement_ObsoleteOverload_FailsClosed()
        {
#pragma warning disable 0618
            Assert.IsFalse(NonConsumableEntitlementGate.CanGrantEntitlement("SomeGame", EntitlementPolicy.Unrestricted("ok")));
#pragma warning restore 0618
        }


        [Test]
        public void NullOrEmptyGameId_PermissivePolicy_FailsClosed()
        {
            foreach (var gameId in new[] { null, string.Empty })
            {
                var result = NonConsumableEntitlementGate.CheckEntitlementEligibility(gameId, EntitlementPolicy.Unrestricted("ok"), false);
                Assert.IsFalse(result.IsEligible);
                Assert.AreEqual("GameIdMissing", result.Reason);
                Assert.IsFalse(NonConsumableEntitlementGate.CanGrantEntitlement(gameId, EntitlementPolicy.Unrestricted("ok"), ReceiptVerificationStatus.VerifiedValid));
            }
        }
    }
}
