using System;

namespace Match3.Engine.Runtime.Commerce
{
    public readonly struct IapRestrictionPolicy
    {
        public bool PurchasesAllowed { get; }
        public bool ParentalGateRequired { get; }
        public string PolicyDescription { get; }

        public IapRestrictionPolicy(bool purchasesAllowed, bool parentalGateRequired, string policyDescription)
        {
            if (string.IsNullOrEmpty(policyDescription))
                throw new ArgumentException("PolicyDescription must not be null or empty.", nameof(policyDescription));
            PurchasesAllowed = purchasesAllowed;
            ParentalGateRequired = parentalGateRequired;
            PolicyDescription = policyDescription;
        }

        public static IapRestrictionPolicy Restricted(string description) =>
            new IapRestrictionPolicy(false, true, description);

        public static IapRestrictionPolicy Unrestricted(string description) =>
            new IapRestrictionPolicy(true, false, description);

        public static IapRestrictionPolicy Default =>
            Restricted("No IAP until explicit owner policy review; fail-closed by default");
    }

    public static class ChildIapGate
    {
        private const string WonderPopId = "Wonder_Pop";

        private static IapRestrictionPolicy Normalize(IapRestrictionPolicy policy) =>
            policy.PolicyDescription == null ? IapRestrictionPolicy.Default : policy;

        public static AdEligibilityResult CheckIapEligibility(
            string gameId, IapRestrictionPolicy policy, bool isChildProfile)
        {
            policy = Normalize(policy);

            if (!policy.PurchasesAllowed)
                return AdEligibilityResult.Ineligible("IapPurchasesRestricted");

            if (string.Equals(gameId, WonderPopId, StringComparison.Ordinal))
                return AdEligibilityResult.Ineligible("WonderPopIapProhibited");

            // Unconditional: child/kid profiles are never IAP-eligible
            if (isChildProfile)
                return AdEligibilityResult.Ineligible("KidProfileIapProhibited");

            return AdEligibilityResult.Eligible();
        }

        [Obsolete("Kid-profile flags are ignored: pass isKidProfile explicitly. This overload treats the profile as a kid profile (fail-closed).")]
        public static AdEligibilityResult CheckIapEligibility(
            string gameId, IapRestrictionPolicy policy, KidProfilePolicy kidPolicy) =>
            CheckIapEligibility(gameId, policy, isChildProfile: true);

        public static bool IsParentalGateRequired(string gameId, IapRestrictionPolicy policy, bool isChildProfile)
        {
            // Unconditional: every child/kid purchase path requires a parental gate
            if (isChildProfile)
                return true;

            if (Normalize(policy).ParentalGateRequired)
                return true;

            if (string.Equals(gameId, WonderPopId, StringComparison.Ordinal))
                return true;

            return false;
        }

        [Obsolete("Child status unknown: always returns true (fail-closed). Use the overload taking isChildProfile.")]
        public static bool IsParentalGateRequired(string gameId, IapRestrictionPolicy policy) =>
            IsParentalGateRequired(gameId, policy, isChildProfile: true);

        public static bool CanShowPurchaseUi(
            string gameId, IapRestrictionPolicy policy, bool isChildProfile) =>
            CheckIapEligibility(gameId, policy, isChildProfile).IsEligible;

        [Obsolete("Kid-profile flags are ignored: pass isKidProfile explicitly. This overload treats the profile as a kid profile (fail-closed).")]
        public static bool CanShowPurchaseUi(
            string gameId, IapRestrictionPolicy policy, KidProfilePolicy kidPolicy) =>
            CanShowPurchaseUi(gameId, policy, isChildProfile: true);
    }
}
