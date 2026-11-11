using System;

namespace Match3.Engine.Runtime.Commerce
{
    public readonly struct RecoveryOfferPolicy
    {
        public bool IsEnabled { get; }
        public RewardPlacementKind RecoveryRewardKind { get; }
        public string PolicyDescription { get; }

        public RecoveryOfferPolicy(bool isEnabled, RewardPlacementKind recoveryRewardKind, string policyDescription)
        {
            if (string.IsNullOrEmpty(policyDescription))
                throw new ArgumentException("PolicyDescription must not be null or empty.", nameof(policyDescription));
            IsEnabled = isEnabled;
            RecoveryRewardKind = recoveryRewardKind;
            PolicyDescription = policyDescription;
        }

        public static RecoveryOfferPolicy Enabled(RewardPlacementKind rewardKind, string description) =>
            new RecoveryOfferPolicy(true, rewardKind, description);

        public static RecoveryOfferPolicy Disabled(string description) =>
            new RecoveryOfferPolicy(false, default, description);

        public static RecoveryOfferPolicy Default =>
            Disabled("No recovery offer configured; fail-closed by default");
    }

    public static class RecoveryAdGate
    {
        public static AdEligibilityResult CheckRecoveryEligibility(
            string gameId,
            AdConsentStatus consent,
            RecoveryOfferPolicy policy,
            bool isKidProfile)
        {
            if (!policy.IsEnabled)
                return AdEligibilityResult.Ineligible("RecoveryOfferDisabled");

            if (string.Equals(gameId, "Wonder_Pop", StringComparison.Ordinal))
                return AdEligibilityResult.Ineligible("WonderPopRecoveryProhibited");

            // Unconditional: kid profiles are never shown ads
            if (isKidProfile)
                return AdEligibilityResult.Ineligible("KidProfileAdsProhibited");

            return AdEligibilityGate.Check(gameId, consent, isKidProfile);
        }

        [Obsolete("Kid-profile flags are ignored: pass isKidProfile explicitly. This overload treats the profile as a kid profile (fail-closed).")]
        public static AdEligibilityResult CheckRecoveryEligibility(
            string gameId,
            AdConsentStatus consent,
            RecoveryOfferPolicy policy,
            KidProfilePolicy kidPolicy) =>
            CheckRecoveryEligibility(gameId, consent, policy, isKidProfile: true);

        public static bool CanGrantRecoveryReward(RewardGrantStatus grantStatus) =>
            grantStatus == RewardGrantStatus.Granted;
    }

    public sealed class RecoverySessionState
    {
        public bool OfferPending { get; private set; }
        public bool RewardGranted { get; private set; }

        public void PresentOffer()
        {
            if (RewardGranted)
                throw new InvalidOperationException("Cannot present recovery offer after reward already granted.");
            OfferPending = true;
        }

        public void DismissOffer()
        {
            OfferPending = false;
        }

        public void MarkRewardGranted()
        {
            if (!OfferPending)
                throw new InvalidOperationException("Cannot grant reward when no offer is pending.");
            RewardGranted = true;
            OfferPending = false;
        }

        public void Reset()
        {
            OfferPending = false;
            RewardGranted = false;
        }
    }
}
