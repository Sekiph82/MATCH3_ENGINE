using System;
using NUnit.Framework;
using Match3.Engine.Runtime.Commerce;

namespace Match3.Engine.Tests.Editor
{
    public sealed class ChildIapGateTests
    {
        private const bool ChildProfile = true;
        private const bool NonChildProfile = false;

        private static readonly IapRestrictionPolicy[] AllConstructiblePolicies =
        {
            IapRestrictionPolicy.Default,
            IapRestrictionPolicy.Restricted("restricted"),
            IapRestrictionPolicy.Unrestricted("approved"),
            new IapRestrictionPolicy(true, false, "relaxed via ctor"),
            default(IapRestrictionPolicy),
        };

        // --- IapRestrictionPolicy ---

        [Test]
        public void Policy_NullDescription_Throws()
        {
            Assert.Throws<ArgumentException>(() =>
                new IapRestrictionPolicy(false, true, null));
        }

        [Test]
        public void Policy_EmptyDescription_Throws()
        {
            Assert.Throws<ArgumentException>(() =>
                new IapRestrictionPolicy(false, true, string.Empty));
        }

        [Test]
        public void Policy_Default_IsRestricted()
        {
            var p = IapRestrictionPolicy.Default;
            Assert.IsFalse(p.PurchasesAllowed);
            Assert.IsTrue(p.ParentalGateRequired);
            Assert.IsFalse(string.IsNullOrEmpty(p.PolicyDescription));
        }

        [Test]
        public void Policy_Restricted_PurchasesNotAllowed()
        {
            var p = IapRestrictionPolicy.Restricted("test reason");
            Assert.IsFalse(p.PurchasesAllowed);
            Assert.IsTrue(p.ParentalGateRequired);
        }

        [Test]
        public void Policy_Unrestricted_PurchasesAllowed()
        {
            var p = IapRestrictionPolicy.Unrestricted("approved");
            Assert.IsTrue(p.PurchasesAllowed);
            Assert.IsFalse(p.ParentalGateRequired);
        }

        // --- CheckIapEligibility ---

        [Test]
        public void ChildGate_PolicyRestricted_Ineligible()
        {
            var policy = IapRestrictionPolicy.Restricted("restricted");
            var result = ChildIapGate.CheckIapEligibility("SomeGame", policy, NonChildProfile);
            Assert.IsFalse(result.IsEligible);
            Assert.AreEqual("IapPurchasesRestricted", result.Reason);
        }

        [Test]
        public void ChildGate_WonderPop_AlwaysIneligible_EvenWithUnrestricted()
        {
            var policy = IapRestrictionPolicy.Unrestricted("approved");
            var result = ChildIapGate.CheckIapEligibility("Wonder_Pop", policy, NonChildProfile);
            Assert.IsFalse(result.IsEligible);
            Assert.AreEqual("WonderPopIapProhibited", result.Reason);
        }

        [Test]
        public void ChildGate_ChildProfile_Ineligible()
        {
            var policy = IapRestrictionPolicy.Unrestricted("approved");
            var result = ChildIapGate.CheckIapEligibility("SomeGame", policy, ChildProfile);
            Assert.IsFalse(result.IsEligible);
            Assert.AreEqual("KidProfileIapProhibited", result.Reason);
        }

        [Test]
        public void ChildGate_ChildProfile_IneligibleUnderEveryPolicy()
        {
            foreach (var policy in AllConstructiblePolicies)
            {
                Assert.IsFalse(ChildIapGate.CheckIapEligibility("SomeGame", policy, ChildProfile).IsEligible);
                Assert.IsFalse(ChildIapGate.CanShowPurchaseUi("SomeGame", policy, ChildProfile));
            }
        }

        [Test]
        public void ChildGate_AllPass_Eligible()
        {
            var policy = IapRestrictionPolicy.Unrestricted("approved");
            var result = ChildIapGate.CheckIapEligibility("SomeGame", policy, NonChildProfile);
            Assert.IsTrue(result.IsEligible);
        }

        [Test]
        public void ChildGate_DefaultStructPolicy_FailsClosed()
        {
            var expected = ChildIapGate.CheckIapEligibility("SomeGame", IapRestrictionPolicy.Default, NonChildProfile);
            var result = ChildIapGate.CheckIapEligibility("SomeGame", default(IapRestrictionPolicy), NonChildProfile);
            Assert.IsFalse(result.IsEligible);
            Assert.AreEqual(expected.Reason, result.Reason);
            Assert.AreEqual("IapPurchasesRestricted", result.Reason);
        }

        [Test]
        public void ChildGate_ObsoleteKidPolicyOverload_FailsClosed_EvenWithUnrestricted()
        {
#pragma warning disable 0618
            var kid = KidProfilePolicy.Unrestricted("ok");
            var result = ChildIapGate.CheckIapEligibility("SomeGame", IapRestrictionPolicy.Unrestricted("approved"), kid);
            Assert.IsFalse(result.IsEligible);
            Assert.AreEqual("KidProfileIapProhibited", result.Reason);
            Assert.IsFalse(ChildIapGate.CanShowPurchaseUi("SomeGame", IapRestrictionPolicy.Unrestricted("approved"), kid));
#pragma warning restore 0618
        }

        // --- IsParentalGateRequired ---

        [Test]
        public void ParentalGate_WonderPop_AlwaysRequired()
        {
            var policy = IapRestrictionPolicy.Unrestricted("approved");
            Assert.IsTrue(ChildIapGate.IsParentalGateRequired("Wonder_Pop", policy, NonChildProfile));
        }

        [Test]
        public void ParentalGate_RestrictedPolicy_Required()
        {
            var policy = IapRestrictionPolicy.Restricted("restricted");
            Assert.IsTrue(ChildIapGate.IsParentalGateRequired("SomeGame", policy, NonChildProfile));
        }

        [Test]
        public void ParentalGate_Unrestricted_NonWonderPop_NonChild_NotRequired()
        {
            var policy = IapRestrictionPolicy.Unrestricted("approved");
            Assert.IsFalse(ChildIapGate.IsParentalGateRequired("SomeGame", policy, NonChildProfile));
        }

        [Test]
        public void ParentalGate_ChildProfile_RequiredUnderEveryPolicy()
        {
            foreach (var policy in AllConstructiblePolicies)
                Assert.IsTrue(ChildIapGate.IsParentalGateRequired("SomeGame", policy, ChildProfile));
        }

        [Test]
        public void ParentalGate_DefaultStructPolicy_Required()
        {
            Assert.IsTrue(ChildIapGate.IsParentalGateRequired("SomeGame", default(IapRestrictionPolicy), NonChildProfile));
        }

        [Test]
        public void ParentalGate_ObsoleteOverload_ChildStatusUnknown_Required()
        {
#pragma warning disable 0618
            foreach (var policy in AllConstructiblePolicies)
                Assert.IsTrue(ChildIapGate.IsParentalGateRequired("SomeGame", policy));
#pragma warning restore 0618
        }

        // --- CanShowPurchaseUi ---

        [Test]
        public void CanShowPurchaseUi_WonderPop_False()
        {
            var policy = IapRestrictionPolicy.Unrestricted("approved");
            Assert.IsFalse(ChildIapGate.CanShowPurchaseUi("Wonder_Pop", policy, NonChildProfile));
        }

        [Test]
        public void CanShowPurchaseUi_AllPass_True()
        {
            var policy = IapRestrictionPolicy.Unrestricted("approved");
            Assert.IsTrue(ChildIapGate.CanShowPurchaseUi("SomeGame", policy, NonChildProfile));
        }

        [Test]
        public void CanShowPurchaseUi_PolicyRestricted_False()
        {
            var policy = IapRestrictionPolicy.Restricted("restricted");
            Assert.IsFalse(ChildIapGate.CanShowPurchaseUi("SomeGame", policy, NonChildProfile));
        }

        [Test]
        public void CanShowPurchaseUi_ChildProfile_False()
        {
            var policy = IapRestrictionPolicy.Unrestricted("approved");
            Assert.IsFalse(ChildIapGate.CanShowPurchaseUi("SomeGame", policy, ChildProfile));
        }
    }
}
