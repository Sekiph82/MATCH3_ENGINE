// ENG-M15-006 | SubscriptionGate NUnit tests
using NUnit.Framework;
using Match3.Engine.Runtime.Commerce;

namespace Match3.Engine.Tests.Editor
{
    [TestFixture]
    public class SubscriptionGateTests
    {
        [Test]
        public void Policy_NullDescription_Throws()
        {
            Assert.Throws<System.ArgumentException>(() =>
                new SubscriptionPolicy(true, false, null));
        }

        [Test]
        public void Policy_EmptyDescription_Throws()
        {
            Assert.Throws<System.ArgumentException>(() =>
                new SubscriptionPolicy(false, false, string.Empty));
        }

        [Test]
        public void Policy_Default_IsDisabled()
        {
            var p = SubscriptionPolicy.Default;
            Assert.IsFalse(p.SubscriptionsEnabled);
            Assert.IsFalse(p.AllowPromotionalTrials);
            Assert.IsFalse(string.IsNullOrEmpty(p.PolicyDescription));
        }

        [Test]
        public void Enum_AllEightValuesPresent()
        {
            Assert.IsTrue(System.Enum.IsDefined(typeof(SubscriptionStatus), 0));
            Assert.IsTrue(System.Enum.IsDefined(typeof(SubscriptionStatus), 1));
            Assert.IsTrue(System.Enum.IsDefined(typeof(SubscriptionStatus), 2));
            Assert.IsTrue(System.Enum.IsDefined(typeof(SubscriptionStatus), 3));
            Assert.IsTrue(System.Enum.IsDefined(typeof(SubscriptionStatus), 4));
            Assert.IsTrue(System.Enum.IsDefined(typeof(SubscriptionStatus), 5));
            Assert.IsTrue(System.Enum.IsDefined(typeof(SubscriptionStatus), 6));
            Assert.IsTrue(System.Enum.IsDefined(typeof(SubscriptionStatus), 7));
            Assert.AreEqual(8, System.Enum.GetValues(typeof(SubscriptionStatus)).Length);
        }

        [Test]
        public void CheckEligibility_WonderPop_AlwaysIneligible_EvenWithEnabledPolicy()
        {
            var policy = SubscriptionPolicy.Enabled("All enabled");
            var kid = KidProfilePolicy.Unrestricted("Not a kid");
            var result = SubscriptionGate.CheckSubscriptionEligibility("Wonder_Pop", policy, kid);
            Assert.IsFalse(result.IsEligible);
            Assert.AreEqual("WonderPopSubscriptionProhibited", result.Reason);
        }

        [Test]
        public void CheckEligibility_WonderPop_AlwaysIneligible_WithKidPolicy()
        {
            var policy = SubscriptionPolicy.Enabled("All enabled");
            var kid = KidProfilePolicy.Restricted("Kid");
            var result = SubscriptionGate.CheckSubscriptionEligibility("Wonder_Pop", policy, kid);
            Assert.IsFalse(result.IsEligible);
            Assert.AreEqual("WonderPopSubscriptionProhibited", result.Reason);
        }

        [Test]
        public void CheckEligibility_DisabledPolicy_Ineligible()
        {
            var policy = SubscriptionPolicy.Default;
            var kid = KidProfilePolicy.Unrestricted("Not a kid");
            var result = SubscriptionGate.CheckSubscriptionEligibility("SomeGame", policy, kid);
            Assert.IsFalse(result.IsEligible);
            Assert.AreEqual("SubscriptionsDisabled", result.Reason);
        }

        [Test]
        public void CheckEligibility_KidProfile_Ineligible()
        {
            var policy = SubscriptionPolicy.Enabled("Enabled");
            var kid = KidProfilePolicy.Restricted("Child user");
            var result = SubscriptionGate.CheckSubscriptionEligibility("SomeGame", policy, kid);
            Assert.IsFalse(result.IsEligible);
            Assert.AreEqual("KidProfileSubscriptionProhibited", result.Reason);
        }

        [Test]
        public void CheckEligibility_AllPass_Eligible()
        {
            var policy = SubscriptionPolicy.Enabled("Enabled");
            var kid = KidProfilePolicy.Unrestricted("Adult");
            var result = SubscriptionGate.CheckSubscriptionEligibility("SomeGame", policy, kid);
            Assert.IsTrue(result.IsEligible);
        }

        [Test]
        public void IsAccessGranted_ActiveStatuses_ReturnTrue()
        {
            Assert.IsTrue(SubscriptionGate.IsAccessGranted(SubscriptionStatus.Active));
            Assert.IsTrue(SubscriptionGate.IsAccessGranted(SubscriptionStatus.GracePeriod));
            Assert.IsTrue(SubscriptionGate.IsAccessGranted(SubscriptionStatus.PendingRenewal));
            Assert.IsTrue(SubscriptionGate.IsAccessGranted(SubscriptionStatus.PromotionalTrial));
            Assert.IsTrue(SubscriptionGate.IsAccessGranted(SubscriptionStatus.Restored));
        }

        [Test]
        public void IsAccessGranted_InactiveStatuses_ReturnFalse()
        {
            Assert.IsFalse(SubscriptionGate.IsAccessGranted(SubscriptionStatus.Expired));
            Assert.IsFalse(SubscriptionGate.IsAccessGranted(SubscriptionStatus.BillingRetry));
            Assert.IsFalse(SubscriptionGate.IsAccessGranted(SubscriptionStatus.Canceled));
        }

        [Test]
        public void RequiresUserAction_BillingRetryAndCanceled_True()
        {
            Assert.IsTrue(SubscriptionGate.RequiresUserAction(SubscriptionStatus.BillingRetry));
            Assert.IsTrue(SubscriptionGate.RequiresUserAction(SubscriptionStatus.Canceled));
        }

        [Test]
        public void RequiresUserAction_Active_False()
        {
            Assert.IsFalse(SubscriptionGate.RequiresUserAction(SubscriptionStatus.Active));
        }

        [Test]
        public void CanShowPromotionalTrial_WonderPop_False()
        {
            var policy = new SubscriptionPolicy(true, true, "All allowed");
            Assert.IsFalse(SubscriptionGate.CanShowPromotionalTrial("Wonder_Pop", policy));
        }

        [Test]
        public void CanShowPromotionalTrial_TrialsDisabled_False()
        {
            var policy = SubscriptionPolicy.Enabled("Enabled but no trials");
            Assert.IsFalse(SubscriptionGate.CanShowPromotionalTrial("SomeGame", policy));
        }

        [Test]
        public void CanShowPromotionalTrial_EnabledAndAllowed_True()
        {
            var policy = new SubscriptionPolicy(true, true, "Full access");
            Assert.IsTrue(SubscriptionGate.CanShowPromotionalTrial("SomeGame", policy));
        }

        [Test]
        public void SessionState_StartsExpired_FailClosed()
        {
            var state = new SubscriptionSessionState();
            Assert.AreEqual(SubscriptionStatus.Expired, state.CurrentStatus);
            Assert.IsFalse(state.IsEligibleForContent());
        }

        [Test]
        public void SessionState_TransitionToActive_EligibleForContent()
        {
            var state = new SubscriptionSessionState();
            state.TransitionTo(SubscriptionStatus.Active);
            Assert.AreEqual(SubscriptionStatus.Active, state.CurrentStatus);
            Assert.IsTrue(state.IsEligibleForContent());
        }

        [Test]
        public void SessionState_Reset_RestoresExpired()
        {
            var state = new SubscriptionSessionState();
            state.TransitionTo(SubscriptionStatus.Active);
            state.Reset();
            Assert.AreEqual(SubscriptionStatus.Expired, state.CurrentStatus);
            Assert.IsFalse(state.IsEligibleForContent());
        }
    }
}
