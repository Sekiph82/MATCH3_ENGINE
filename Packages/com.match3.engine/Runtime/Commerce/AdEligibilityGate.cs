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

        // Fail closed on missing/unknown game context; known games are evaluated against their
        // catalog business profile exactly as CheckWithProfile does.
        public static AdEligibilityResult Check(string gameId, AdConsentStatus consent, bool isKidProfile)
        {
            if (string.IsNullOrEmpty(gameId))
                return AdEligibilityResult.Ineligible("GameIdMissing");

            if (Ordinal.Equals(gameId, "Wonder_Pop"))
                return AdEligibilityResult.Ineligible("WonderPopExcluded");

            if (!GameProfileCatalog.IsKnownId(gameId))
                return AdEligibilityResult.Ineligible("UnknownGame");

            return CheckWithProfile(GameBusinessProfileCatalog.Require(gameId), consent, isKidProfile);
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

            if (Ordinal.Equals(profile.GameId, "Wonder_Pop"))
                return AdEligibilityResult.Ineligible("WonderPopExcluded");

            // Unconditional: kid profiles are never ad-eligible, for any game
            if (isKidProfile)
                return AdEligibilityResult.Ineligible("KidProfileAdsProhibited");

            if (consent == AdConsentStatus.Unknown || consent == AdConsentStatus.Denied)
                return AdEligibilityResult.Ineligible("ConsentNotGranted");

            return AdEligibilityResult.Eligible();
        }

        [Obsolete("Kid status unknown: treated as a kid profile (fail-closed). Use the overload taking isKidProfile.")]
        public static AdEligibilityResult CheckWithProfile(
            GameBusinessProfile profile, AdConsentStatus consent) =>
            CheckWithProfile(profile, consent, isKidProfile: true);
    }
}
