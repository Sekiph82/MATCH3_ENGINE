// ENG-M15-006 | Subscription lifecycle state machine contracts
using System;

namespace Match3.Engine.Runtime.Commerce
{
    public enum SubscriptionStatus
    {
        Active,
        Expired,
        GracePeriod,
        BillingRetry,
        Canceled,
        PendingRenewal,
        PromotionalTrial,
        Restored
    }

    public readonly struct SubscriptionPolicy
    {
        public bool SubscriptionsEnabled { get; }
        public bool AllowPromotionalTrials { get; }
        public string PolicyDescription { get; }

        public SubscriptionPolicy(bool subscriptionsEnabled, bool allowPromotionalTrials, string policyDescription)
        {
            if (string.IsNullOrEmpty(policyDescription))
                throw new ArgumentException("PolicyDescription must not be null or empty.", nameof(policyDescription));
            SubscriptionsEnabled = subscriptionsEnabled;
            AllowPromotionalTrials = allowPromotionalTrials;
            PolicyDescription = policyDescription;
        }

        public static SubscriptionPolicy Enabled(string desc) =>
            new SubscriptionPolicy(true, false, desc);

        public static SubscriptionPolicy Disabled(string desc) =>
            new SubscriptionPolicy(false, false, desc);

        public static SubscriptionPolicy Default =>
            Disabled("Subscriptions disabled; fail-closed by default");
    }

    public static class SubscriptionGate
    {
        private static readonly StringComparer Ordinal = StringComparer.Ordinal;

        public static AdEligibilityResult CheckSubscriptionEligibility(
            string gameId, SubscriptionPolicy policy, bool isKidProfile)
        {
            // Fail closed: a missing game context is never eligible
            if (string.IsNullOrEmpty(gameId))
                return AdEligibilityResult.Ineligible("GameIdMissing");

            // Guard 1: Wonder_Pop always prohibited, checked first
            if (Ordinal.Equals(gameId, "Wonder_Pop"))
                return AdEligibilityResult.Ineligible("WonderPopSubscriptionProhibited");

            // Guard 2: subscriptions disabled (fail-closed default)
            if (!policy.SubscriptionsEnabled)
                return AdEligibilityResult.Ineligible("SubscriptionsDisabled");

            // Guard 3 (unconditional): kid profiles are never IAP/subscription-eligible
            if (isKidProfile)
                return AdEligibilityResult.Ineligible("KidProfileSubscriptionProhibited");

            return AdEligibilityResult.Eligible();
        }

        [Obsolete("Kid-profile flags are ignored: pass isKidProfile explicitly. This overload treats the profile as a kid profile (fail-closed).")]
        public static AdEligibilityResult CheckSubscriptionEligibility(
            string gameId, SubscriptionPolicy policy, KidProfilePolicy kidPolicy) =>
            CheckSubscriptionEligibility(gameId, policy, isKidProfile: true);

        public static bool IsAccessGranted(SubscriptionStatus status)
        {
            switch (status)
            {
                case SubscriptionStatus.Active:
                case SubscriptionStatus.GracePeriod:
                case SubscriptionStatus.PendingRenewal:
                case SubscriptionStatus.PromotionalTrial:
                case SubscriptionStatus.Restored:
                    return true;
                default:
                    return false;
            }
        }

        public static bool RequiresUserAction(SubscriptionStatus status) =>
            status == SubscriptionStatus.BillingRetry || status == SubscriptionStatus.Canceled;

        public static bool CanShowPromotionalTrial(string gameId, SubscriptionPolicy policy, bool isKidProfile)
        {
            // Fail closed: a missing game context is never eligible
            if (string.IsNullOrEmpty(gameId))
                return false;

            if (Ordinal.Equals(gameId, "Wonder_Pop"))
                return false;
            // Unconditional: kid profiles are never shown subscription offers
            if (isKidProfile)
                return false;
            if (!policy.SubscriptionsEnabled)
                return false;
            if (!policy.AllowPromotionalTrials)
                return false;
            return true;
        }

        [Obsolete("Kid status unknown: always false (fail-closed). Use the overload taking isKidProfile.")]
        public static bool CanShowPromotionalTrial(string gameId, SubscriptionPolicy policy) =>
            CanShowPromotionalTrial(gameId, policy, isKidProfile: true);
    }

    public sealed class SubscriptionSessionState
    {
        // fail-closed: starts Expired
        public SubscriptionStatus CurrentStatus { get; private set; } = SubscriptionStatus.Expired;

        public void TransitionTo(SubscriptionStatus status) => CurrentStatus = status;

        public void Reset() => CurrentStatus = SubscriptionStatus.Expired;

        public bool IsEligibleForContent() => SubscriptionGate.IsAccessGranted(CurrentStatus);
    }
}
