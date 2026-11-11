using System;

namespace Match3.Engine.Runtime.Commerce
{
    /// <summary>
    /// Kid profiles are ALWAYS ads- and IAP-prohibited for every game. No policy value can relax this;
    /// the flags below are retained for API compatibility only and always report "prohibited".
    /// </summary>
    public readonly struct KidProfilePolicy
    {
        [Obsolete("Ignored: kid profiles are always ads-prohibited, under every policy.")]
        public bool AdsProhibited => true;
        [Obsolete("Ignored: kid profiles are always IAP-prohibited, under every policy.")]
        public bool IapProhibited => true;
        public string PolicyJustification { get; }

        private KidProfilePolicy(string policyJustification)
        {
            if (string.IsNullOrEmpty(policyJustification))
                throw new ArgumentException("PolicyJustification must not be null or empty.", nameof(policyJustification));
            PolicyJustification = policyJustification;
        }

        [Obsolete("Ignored flags: kid profiles are always ads- and IAP-prohibited. Use KidProfilePolicy.Restricted.")]
        public KidProfilePolicy(bool adsProhibited, bool iapProhibited, string policyJustification)
            : this(policyJustification)
        {
        }

        public static KidProfilePolicy Restricted(string justification) =>
            new KidProfilePolicy(justification);

        [Obsolete("Ignored: kid profiles are always ads- and IAP-prohibited; this returns a restricted policy. For non-kid profiles pass isKidProfile: false to the commerce gates.")]
        public static KidProfilePolicy Unrestricted(string justification) =>
            new KidProfilePolicy(justification);

        public static KidProfilePolicy Default =>
            Restricted("No expert review completed; fail-closed by default");
    }

    public static class KidProfileGate
    {
        // Unconditional: a kid profile is never ad-eligible, for any game, under any policy.
        public static AdEligibilityResult CheckAdEligibility(string gameId, KidProfilePolicy policy) =>
            AdEligibilityResult.Ineligible("KidProfileAdsProhibited");

        // Unconditional: ad SDKs are never linked for a kid profile, for any game, under any policy.
        public static bool IsSdkLinkingAllowed(string gameId, KidProfilePolicy policy) => false;
    }
}
