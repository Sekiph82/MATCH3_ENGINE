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

        public static AdEligibilityResult CheckIapEligibility(
            string gameId, IapRestrictionPolicy policy, KidProfilePolicy kidPolicy)
        {
            if (!policy.PurchasesAllowed)
                return AdEligibilityResult.Ineligible("IapPurchasesRestricted");

            if (string.Equals(gameId, WonderPopId, StringComparison.Ordinal))
                return AdEligibilityResult.Ineligible("WonderPopIapProhibited");

            if (kidPolicy.IapProhibited)
                return AdEligibilityResult.Ineligible("KidProfileIapProhibited");

            return AdEligibilityResult.Eligible();
        }

        public static bool IsParentalGateRequired(string gameId, IapRestrictionPolicy policy)
        {
            if (policy.ParentalGateRequired)
                return true;

            if (string.Equals(gameId, WonderPopId, StringComparison.Ordinal))
                return true;

            return false;
        }

        public static bool CanShowPurchaseUi(
            string gameId, IapRestrictionPolicy policy, KidProfilePolicy kidPolicy)
        {
            if (!policy.PurchasesAllowed)
                return false;

            if (string.Equals(gameId, WonderPopId, StringComparison.Ordinal))
                return false;

            if (kidPolicy.IapProhibited)
                return false;

            return true;
        }
    }
}
