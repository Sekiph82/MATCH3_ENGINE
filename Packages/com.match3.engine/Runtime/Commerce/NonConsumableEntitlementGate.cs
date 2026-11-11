using System;

namespace Match3.Engine.Runtime.Commerce
{
    public readonly struct EntitlementPolicy
    {
        public bool PurchasesAllowed { get; }
        public bool RequireServerVerification { get; }
        public string PolicyJustification { get; }

        public EntitlementPolicy(bool purchasesAllowed, bool requireServerVerification, string policyJustification)
        {
            if (string.IsNullOrEmpty(policyJustification))
                throw new ArgumentException("PolicyJustification must not be null or empty.", nameof(policyJustification));
            PurchasesAllowed = purchasesAllowed;
            RequireServerVerification = requireServerVerification;
            PolicyJustification = policyJustification;
        }

        public static EntitlementPolicy Restricted(string justification) =>
            new EntitlementPolicy(false, true, justification);

        public static EntitlementPolicy Unrestricted(string justification) =>
            new EntitlementPolicy(true, true, justification);

        public static EntitlementPolicy Default =>
            Restricted("No non-consumable purchases until explicit owner policy review; fail-closed by default");
    }

    public static class NonConsumableEntitlementGate
    {
        private const string WonderPopId = "Wonder_Pop";

        public static AdEligibilityResult CheckEntitlementEligibility(
            string gameId, EntitlementPolicy policy, KidProfilePolicy kidPolicy)
        {
            if (string.Equals(gameId, WonderPopId, StringComparison.Ordinal))
                return AdEligibilityResult.Ineligible("WonderPopEntitlementProhibited");

            if (!policy.PurchasesAllowed)
                return AdEligibilityResult.Ineligible("EntitlementPurchasesRestricted");

            if (kidPolicy.IapProhibited)
                return AdEligibilityResult.Ineligible("KidProfileEntitlementProhibited");

            return AdEligibilityResult.Eligible();
        }

        // Non-consumables always need server verification regardless of policy hint.
        public static bool IsServerVerificationRequired(EntitlementPolicy policy) => true;

        public static bool CanGrantEntitlement(string gameId, EntitlementPolicy policy)
        {
            if (!policy.PurchasesAllowed)
                return false;

            if (string.Equals(gameId, WonderPopId, StringComparison.Ordinal))
                return false;

            return true;
        }
    }
}
