using NUnit.Framework;
using Match3.Engine.Runtime.Commerce;

namespace Match3.Engine.Tests.Editor
{
    [TestFixture]
    public class RecoveryAdGateTests
    {
        private static KidProfilePolicy UnrestrictedPolicy =>
            KidProfilePolicy.Unrestricted("test-unrestricted");

        private static KidProfilePolicy RestrictedPolicy =>
            KidProfilePolicy.Restricted("test-restricted");

        [Test]
        public void Policy_NullDescription_Throws()
        {
            Assert.Throws<System.ArgumentException>(() =>
                RecoveryOfferPolicy.Disabled(null));
        }

        [Test]
        public void Policy_Default_IsDisabled()
        {
            var policy = RecoveryOfferPolicy.Default;
            Assert.IsFalse(policy.IsEnabled);
            Assert.IsNotNull(policy.PolicyDescription);
        }

        [Test]
        public void Policy_Enabled_IsEnabled()
        {
            var policy = RecoveryOfferPolicy.Enabled(RewardPlacementKind.ExtraHeart, "Recovery enabled");
            Assert.IsTrue(policy.IsEnabled);
            Assert.AreEqual(RewardPlacementKind.ExtraHeart, policy.RecoveryRewardKind);
        }

        [Test]
        public void RecoveryGate_PolicyDisabled_Ineligible()
        {
            var policy = RecoveryOfferPolicy.Default;
            var result = RecoveryAdGate.CheckRecoveryEligibility(
                "Some_Game", AdConsentStatus.Granted, policy, UnrestrictedPolicy);
            Assert.IsFalse(result.IsEligible);
            Assert.AreEqual("RecoveryOfferDisabled", result.Reason);
        }

        [Test]
        public void RecoveryGate_WonderPop_Prohibited()
        {
            var policy = RecoveryOfferPolicy.Enabled(RewardPlacementKind.ExtraHeart, "enabled");
            var result = RecoveryAdGate.CheckRecoveryEligibility(
                "Wonder_Pop", AdConsentStatus.Granted, policy, UnrestrictedPolicy);
            Assert.IsFalse(result.IsEligible);
            Assert.AreEqual("WonderPopRecoveryProhibited", result.Reason);
        }

        [Test]
        public void RecoveryGate_KidPolicyProhibited()
        {
            var policy = RecoveryOfferPolicy.Enabled(RewardPlacementKind.ExtraHeart, "enabled");
            var result = RecoveryAdGate.CheckRecoveryEligibility(
                "Coral_Rescue", AdConsentStatus.Granted, policy, RestrictedPolicy);
            Assert.IsFalse(result.IsEligible);
            Assert.AreEqual("KidProfileAdsProhibited", result.Reason);
        }

        [Test]
        public void RecoveryGate_ConsentDenied_Ineligible()
        {
            var policy = RecoveryOfferPolicy.Enabled(RewardPlacementKind.ExtraHeart, "enabled");
            var result = RecoveryAdGate.CheckRecoveryEligibility(
                "Coral_Rescue", AdConsentStatus.Denied, policy, UnrestrictedPolicy);
            Assert.IsFalse(result.IsEligible);
        }

        [Test]
        public void RecoveryGate_AllPass_Eligible()
        {
            var policy = RecoveryOfferPolicy.Enabled(RewardPlacementKind.ExtraHeart, "enabled");
            var result = RecoveryAdGate.CheckRecoveryEligibility(
                "Coral_Rescue", AdConsentStatus.Granted, policy, UnrestrictedPolicy);
            Assert.IsTrue(result.IsEligible);
        }

        [Test]
        public void CanGrantReward_Granted_True()
        {
            Assert.IsTrue(RecoveryAdGate.CanGrantRecoveryReward(RewardGrantStatus.Granted));
        }

        [Test]
        public void CanGrantReward_Skipped_False()
        {
            Assert.IsFalse(RecoveryAdGate.CanGrantRecoveryReward(RewardGrantStatus.Skipped));
        }

        [Test]
        public void CanGrantReward_Failed_False()
        {
            Assert.IsFalse(RecoveryAdGate.CanGrantRecoveryReward(RewardGrantStatus.Failed));
        }

        [Test]
        public void SessionState_PresentOffer_SetsOfferPending()
        {
            var state = new RecoverySessionState();
            state.PresentOffer();
            Assert.IsTrue(state.OfferPending);
            Assert.IsFalse(state.RewardGranted);
        }

        [Test]
        public void SessionState_DismissOffer_ClearsOfferPending()
        {
            var state = new RecoverySessionState();
            state.PresentOffer();
            state.DismissOffer();
            Assert.IsFalse(state.OfferPending);
        }

        [Test]
        public void SessionState_MarkRewardGranted_RequiresOfferPending()
        {
            var state = new RecoverySessionState();
            Assert.Throws<System.InvalidOperationException>(() => state.MarkRewardGranted());
        }

        [Test]
        public void SessionState_Reset_ClearsBoth()
        {
            var state = new RecoverySessionState();
            state.PresentOffer();
            state.MarkRewardGranted();
            state.Reset();
            Assert.IsFalse(state.OfferPending);
            Assert.IsFalse(state.RewardGranted);
        }
    }
}
