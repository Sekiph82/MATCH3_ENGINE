using System;
using NUnit.Framework;
using Match3.Engine.Runtime.Commerce;

namespace Match3.Engine.Tests.Editor
{
    public sealed class ChildIapGateTests
    {
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
            var kid = KidProfilePolicy.Unrestricted("ok");
            var result = ChildIapGate.CheckIapEligibility("SomeGame", policy, kid);
            Assert.IsFalse(result.IsEligible);
            Assert.AreEqual("IapPurchasesRestricted", result.Reason);
        }

        [Test]
        public void ChildGate_WonderPop_AlwaysIneligible_EvenWithUnrestricted()
        {
            var policy = IapRestrictionPolicy.Unrestricted("approved");
            var kid = KidProfilePolicy.Unrestricted("ok");
            var result = ChildIapGate.CheckIapEligibility("Wonder_Pop", policy, kid);
            Assert.IsFalse(result.IsEligible);
            Assert.AreEqual("WonderPopIapProhibited", result.Reason);
        }

        [Test]
        public void ChildGate_KidProfileIapProhibited_Ineligible()
        {
            var policy = IapRestrictionPolicy.Unrestricted("approved");
            var kid = KidProfilePolicy.Restricted("kid restricted");
            var result = ChildIapGate.CheckIapEligibility("SomeGame", policy, kid);
            Assert.IsFalse(result.IsEligible);
            Assert.AreEqual("KidProfileIapProhibited", result.Reason);
        }

        [Test]
        public void ChildGate_AllPass_Eligible()
        {
            var policy = IapRestrictionPolicy.Unrestricted("approved");
            var kid = KidProfilePolicy.Unrestricted("ok");
            var result = ChildIapGate.CheckIapEligibility("SomeGame", policy, kid);
            Assert.IsTrue(result.IsEligible);
        }

        // --- IsParentalGateRequired ---

        [Test]
        public void ParentalGate_WonderPop_AlwaysRequired()
        {
            var policy = IapRestrictionPolicy.Unrestricted("approved");
            Assert.IsTrue(ChildIapGate.IsParentalGateRequired("Wonder_Pop", policy));
        }

        [Test]
        public void ParentalGate_KidPolicy_Required()
        {
            var policy = IapRestrictionPolicy.Restricted("restricted");
            Assert.IsTrue(ChildIapGate.IsParentalGateRequired("SomeGame", policy));
        }

        [Test]
        public void ParentalGate_Unrestricted_NonWonderPop_NotRequired()
        {
            var policy = IapRestrictionPolicy.Unrestricted("approved");
            Assert.IsFalse(ChildIapGate.IsParentalGateRequired("SomeGame", policy));
        }

        // --- CanShowPurchaseUi ---

        [Test]
        public void CanShowPurchaseUi_WonderPop_False()
        {
            var policy = IapRestrictionPolicy.Unrestricted("approved");
            var kid = KidProfilePolicy.Unrestricted("ok");
            Assert.IsFalse(ChildIapGate.CanShowPurchaseUi("Wonder_Pop", policy, kid));
        }

        [Test]
        public void CanShowPurchaseUi_AllPass_True()
        {
            var policy = IapRestrictionPolicy.Unrestricted("approved");
            var kid = KidProfilePolicy.Unrestricted("ok");
            Assert.IsTrue(ChildIapGate.CanShowPurchaseUi("SomeGame", policy, kid));
        }

        [Test]
        public void CanShowPurchaseUi_PolicyRestricted_False()
        {
            var policy = IapRestrictionPolicy.Restricted("restricted");
            var kid = KidProfilePolicy.Unrestricted("ok");
            Assert.IsFalse(ChildIapGate.CanShowPurchaseUi("SomeGame", policy, kid));
        }

        [Test]
        public void CanShowPurchaseUi_KidIapProhibited_False()
        {
            var policy = IapRestrictionPolicy.Unrestricted("approved");
            var kid = KidProfilePolicy.Restricted("kid restricted");
            Assert.IsFalse(ChildIapGate.CanShowPurchaseUi("SomeGame", policy, kid));
        }
    }
}
