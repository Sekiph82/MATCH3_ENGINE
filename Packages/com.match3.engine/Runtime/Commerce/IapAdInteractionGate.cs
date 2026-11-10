using System;

namespace Match3.Engine.Runtime.Commerce
{
    public enum NoAdsScope
    {
        None = 0,
        DisablesForcedAds = 1,
        DisablesAllAds = 2
    }

    public readonly struct NoAdsPurchaseState
    {
        public string GameId { get; }
        public NoAdsScope Scope { get; }

        public NoAdsPurchaseState(string gameId, NoAdsScope scope)
        {
            if (string.IsNullOrEmpty(gameId))
                throw new ArgumentException("GameId must not be null or empty.", nameof(gameId));
            GameId = gameId;
            Scope = scope;
        }

        public bool RewardedAdStillAvailable =>
            Scope == NoAdsScope.None || Scope == NoAdsScope.DisablesForcedAds;

        public static NoAdsPurchaseState NoPurchase(string gameId) =>
            new NoAdsPurchaseState(gameId, NoAdsScope.None);

        public static NoAdsPurchaseState ForcedAdsDisabled(string gameId) =>
            new NoAdsPurchaseState(gameId, NoAdsScope.DisablesForcedAds);

        public static NoAdsPurchaseState AllAdsDisabled(string gameId) =>
            new NoAdsPurchaseState(gameId, NoAdsScope.DisablesAllAds);
    }

    public static class IapAdInteractionGate
    {
        public static AdEligibilityResult CheckRewardedEligibility(
            NoAdsPurchaseState purchaseState, AdEligibilityResult baseEligibility)
        {
            if (!baseEligibility.IsEligible)
                return baseEligibility;

            if (!purchaseState.RewardedAdStillAvailable)
                return AdEligibilityResult.Ineligible("RewardedAdDisabledByNoAdsPurchase");

            return baseEligibility;
        }

        public static bool IsFormatSuppressed(AdFormatKind format, NoAdsPurchaseState purchaseState)
        {
            switch (purchaseState.Scope)
            {
                case NoAdsScope.None:
                    return false;
                case NoAdsScope.DisablesForcedAds:
                    return format == AdFormatKind.Interstitial || format == AdFormatKind.Banner;
                case NoAdsScope.DisablesAllAds:
                    return true;
                default:
                    return false;
            }
        }
    }
}
