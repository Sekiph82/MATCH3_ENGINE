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

        public static AdEligibilityResult Check(string gameId, AdConsentStatus consent)
        {
            if (Ordinal.Equals(gameId, "Wonder_Pop"))
                return AdEligibilityResult.Ineligible("WonderPopExcluded");

            if (consent == AdConsentStatus.Unknown || consent == AdConsentStatus.Denied)
                return AdEligibilityResult.Ineligible("ConsentNotGranted");

            return AdEligibilityResult.Eligible();
        }

        public static AdEligibilityResult CheckWithProfile(
            GameBusinessProfile profile, AdConsentStatus consent)
        {
            if (profile == null)
                throw new ArgumentNullException(nameof(profile));

            if (!profile.AdsEnabled || !profile.MonetizationEnabled)
                return AdEligibilityResult.Ineligible("AdsNotApproved");

            return Check(profile.GameId, consent);
        }
    }
}
