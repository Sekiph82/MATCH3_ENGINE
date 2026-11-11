using System;

namespace Match3.Engine.Runtime.Commerce
{
    public enum AdConsentStatus
    {
        Unknown = 0,
        Denied = 1,
        Granted = 2,
        NotRequired = 3
    }

    public readonly struct AdEligibilityResult
    {
        public bool IsEligible { get; }
        public string Reason { get; }

        private AdEligibilityResult(bool isEligible, string reason)
        {
            IsEligible = isEligible;
            Reason = reason;
        }

        public static AdEligibilityResult Eligible() =>
            new AdEligibilityResult(true, string.Empty);

        public static AdEligibilityResult Ineligible(string reason)
        {
            if (reason == null)
                throw new ArgumentNullException(nameof(reason));
            return new AdEligibilityResult(false, reason);
        }
    }

    public static class AdEligibilityGate
    {
        private static readonly StringComparer Ordinal = StringComparer.Ordinal;

        public static AdEligibilityResult Check(string gameId, AdConsentStatus consent, bool isKidProfile)
        {
            if (Ordinal.Equals(gameId, "Wonder_Pop"))
                return AdEligibilityResult.Ineligible("WonderPopExcluded");

            // Unconditional: kid profiles are never ad-eligible, for any game
            if (isKidProfile)
                return AdEligibilityResult.Ineligible("KidProfileAdsProhibited");

            if (consent == AdConsentStatus.Unknown || consent == AdConsentStatus.Denied)
                return AdEligibilityResult.Ineligible("ConsentNotGranted");

            return AdEligibilityResult.Eligible();
        }

        [Obsolete("Kid status unknown: treated as a kid profile (KidProfileAdsProhibited, fail-closed). Use the overload taking isKidProfile.")]
        public static AdEligibilityResult Check(string gameId, AdConsentStatus consent) =>
            Check(gameId, consent, isKidProfile: true);

        public static AdEligibilityResult CheckWithProfile(
            GameBusinessProfile profile, AdConsentStatus consent, bool isKidProfile)
        {
            if (profile == null)
                throw new ArgumentNullException(nameof(profile));

            if (!profile.AdsEnabled || !profile.MonetizationEnabled)
                return AdEligibilityResult.Ineligible("AdsNotApproved");

            return Check(profile.GameId, consent, isKidProfile);
        }

        [Obsolete("Kid status unknown: treated as a kid profile (fail-closed). Use the overload taking isKidProfile.")]
        public static AdEligibilityResult CheckWithProfile(
            GameBusinessProfile profile, AdConsentStatus consent) =>
            CheckWithProfile(profile, consent, isKidProfile: true);
    }
}
