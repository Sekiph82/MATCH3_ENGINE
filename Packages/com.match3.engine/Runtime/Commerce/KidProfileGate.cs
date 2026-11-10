using System;

namespace Match3.Engine.Runtime.Commerce
{
    public readonly struct KidProfilePolicy
    {
        public bool AdsProhibited { get; }
        public bool IapProhibited { get; }
        public string PolicyJustification { get; }

        public KidProfilePolicy(bool adsProhibited, bool iapProhibited, string policyJustification)
        {
            if (string.IsNullOrEmpty(policyJustification))
                throw new ArgumentException("PolicyJustification must not be null or empty.", nameof(policyJustification));
            AdsProhibited = adsProhibited;
            IapProhibited = iapProhibited;
            PolicyJustification = policyJustification;
        }

        public static KidProfilePolicy Restricted(string justification) =>
            new KidProfilePolicy(true, true, justification);

        public static KidProfilePolicy Unrestricted(string justification) =>
            new KidProfilePolicy(false, false, justification);

        public static KidProfilePolicy Default =>
            Restricted("No expert review completed; fail-closed by default");
    }

    public static class KidProfileGate
    {
        private const string WonderPopId = "Wonder_Pop";

        public static AdEligibilityResult CheckAdEligibility(string gameId, KidProfilePolicy policy)
        {
            if (policy.AdsProhibited)
                return AdEligibilityResult.Ineligible("KidProfileAdsProhibited");

            if (string.Equals(gameId, WonderPopId, StringComparison.Ordinal))
                return AdEligibilityResult.Ineligible("WonderPopKidProfile");

            return AdEligibilityResult.Eligible();
        }

        public static bool IsSdkLinkingAllowed(string gameId, KidProfilePolicy policy)
        {
            if (policy.AdsProhibited)
                return false;

            if (string.Equals(gameId, WonderPopId, StringComparison.Ordinal))
                return false;

            return true;
        }
    }
}
