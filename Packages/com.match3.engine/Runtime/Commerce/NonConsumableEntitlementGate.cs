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
            string gameId, EntitlementPolicy policy, bool isKidProfile)
        {
            // Fail closed: a missing game context is never eligible
            if (string.IsNullOrEmpty(gameId))
                return AdEligibilityResult.Ineligible("GameIdMissing");

            // Fail closed: only catalog game IDs (Ordinal) are eligible
            if (!GameProfileCatalog.IsKnownId(gameId))
                return AdEligibilityResult.Ineligible("UnknownGame");

            if (string.Equals(gameId, WonderPopId, StringComparison.Ordinal))
                return AdEligibilityResult.Ineligible("WonderPopEntitlementProhibited");

            if (!policy.PurchasesAllowed)
                return AdEligibilityResult.Ineligible("EntitlementPurchasesRestricted");

            // Unconditional: kid profiles are never IAP-eligible
            if (isKidProfile)
                return AdEligibilityResult.Ineligible("KidProfileEntitlementProhibited");

            return AdEligibilityResult.Eligible();
        }

        [Obsolete("Kid-profile flags are ignored: pass isKidProfile explicitly. This overload treats the profile as a kid profile (fail-closed).")]
        public static AdEligibilityResult CheckEntitlementEligibility(
            string gameId, EntitlementPolicy policy, KidProfilePolicy kidPolicy) =>
            CheckEntitlementEligibility(gameId, policy, isKidProfile: true);

        // Non-consumables always need server verification regardless of policy hint.
        public static bool IsServerVerificationRequired(EntitlementPolicy policy) => true;

        // Grant requires a server-verified valid receipt. Restoring owned content is not gated on kid status.
        public static bool CanGrantEntitlement(
            string gameId, EntitlementPolicy policy, ReceiptVerificationStatus verification)
        {
            // Fail closed: a missing game context is never eligible
            if (string.IsNullOrEmpty(gameId))
                return false;

            // Fail closed: only catalog game IDs (Ordinal) are eligible
            if (!GameProfileCatalog.IsKnownId(gameId))
                return false;

            if (!policy.PurchasesAllowed)
                return false;

            if (string.Equals(gameId, WonderPopId, StringComparison.Ordinal))
                return false;

            // Unconditional: only a server-verified valid receipt grants a non-consumable
            if (verification != ReceiptVerificationStatus.VerifiedValid)
                return false;

            return true;
        }

        [Obsolete("Receipt verification unknown: always false (fail-closed). Use the overload taking ReceiptVerificationStatus.")]
        public static bool CanGrantEntitlement(string gameId, EntitlementPolicy policy) =>
            CanGrantEntitlement(gameId, policy, ReceiptVerificationStatus.Unverified);
    }
}
