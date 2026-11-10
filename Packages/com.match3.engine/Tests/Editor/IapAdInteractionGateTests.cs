using System;
using NUnit.Framework;
using Match3.Engine.Runtime.Commerce;

namespace Match3.Engine.Tests.Editor
{
    public sealed class IapAdInteractionGateTests
    {
        [Test]
        public void NoAdsPurchaseState_NullGameId_Throws()
        {
            Assert.Throws<ArgumentException>(() => new NoAdsPurchaseState(null, NoAdsScope.None));
            Assert.Throws<ArgumentException>(() => new NoAdsPurchaseState(string.Empty, NoAdsScope.None));
        }

        [Test]
        public void NoPurchase_RewardedAvailable()
        {
            var state = NoAdsPurchaseState.NoPurchase("TestGame");
            Assert.IsTrue(state.RewardedAdStillAvailable);
            Assert.AreEqual(NoAdsScope.None, state.Scope);
        }

        [Test]
        public void ForcedAdsDisabled_RewardedStillAvailable()
        {
            var state = NoAdsPurchaseState.ForcedAdsDisabled("TestGame");
            Assert.IsTrue(state.RewardedAdStillAvailable);
            Assert.AreEqual(NoAdsScope.DisablesForcedAds, state.Scope);
        }

        [Test]
        public void AllAdsDisabled_RewardedNotAvailable()
        {
            var state = NoAdsPurchaseState.AllAdsDisabled("TestGame");
            Assert.IsFalse(state.RewardedAdStillAvailable);
            Assert.AreEqual(NoAdsScope.DisablesAllAds, state.Scope);
        }

        [Test]
        public void CheckRewarded_BaseIneligible_PreservesReason()
        {
            var state = NoAdsPurchaseState.ForcedAdsDisabled("TestGame");
            var baseIneligible = AdEligibilityResult.Ineligible("ConsentNotGranted");
            var result = IapAdInteractionGate.CheckRewardedEligibility(state, baseIneligible);
            Assert.IsFalse(result.IsEligible);
            Assert.AreEqual("ConsentNotGranted", result.Reason);
        }

        [Test]
        public void CheckRewarded_AllAdsDisabled_ReturnsDisabledReason()
        {
            var state = NoAdsPurchaseState.AllAdsDisabled("TestGame");
            var baseEligible = AdEligibilityResult.Eligible();
            var result = IapAdInteractionGate.CheckRewardedEligibility(state, baseEligible);
            Assert.IsFalse(result.IsEligible);
            Assert.AreEqual("RewardedAdDisabledByNoAdsPurchase", result.Reason);
        }

        [Test]
        public void CheckRewarded_NoPurchase_ReturnsBaseEligible()
        {
            var state = NoAdsPurchaseState.NoPurchase("TestGame");
            var baseEligible = AdEligibilityResult.Eligible();
            var result = IapAdInteractionGate.CheckRewardedEligibility(state, baseEligible);
            Assert.IsTrue(result.IsEligible);
        }

        [Test]
        public void IsFormatSuppressed_Rewarded_DisablesForcedAds_NotSuppressed()
        {
            var state = NoAdsPurchaseState.ForcedAdsDisabled("TestGame");
            Assert.IsFalse(IapAdInteractionGate.IsFormatSuppressed(AdFormatKind.Rewarded, state));
        }

        [Test]
        public void IsFormatSuppressed_Rewarded_DisablesAllAds_Suppressed()
        {
            var state = NoAdsPurchaseState.AllAdsDisabled("TestGame");
            Assert.IsTrue(IapAdInteractionGate.IsFormatSuppressed(AdFormatKind.Rewarded, state));
        }

        [Test]
        public void IsFormatSuppressed_Interstitial_DisablesForcedAds_Suppressed()
        {
            var state = NoAdsPurchaseState.ForcedAdsDisabled("TestGame");
            Assert.IsTrue(IapAdInteractionGate.IsFormatSuppressed(AdFormatKind.Interstitial, state));
        }

        [Test]
        public void IsFormatSuppressed_Banner_None_NotSuppressed()
        {
            var state = NoAdsPurchaseState.NoPurchase("TestGame");
            Assert.IsFalse(IapAdInteractionGate.IsFormatSuppressed(AdFormatKind.Banner, state));
        }
    }
}
