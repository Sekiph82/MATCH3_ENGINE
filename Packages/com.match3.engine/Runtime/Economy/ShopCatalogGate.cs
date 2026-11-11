using System;
using System.Collections.Generic;
using System.Linq;

namespace Match3.Engine.Runtime.Economy
{
    public enum OfferType { CoinPack, BoosterPack, HeartBundle, NoAdsPermanent, PremiumSubscription }

    public readonly struct ShopOffer
    {
        public string OfferId { get; }
        public string GameId { get; }
        public OfferType Type { get; }
        public bool IsEnabled { get; }

        private ShopOffer(string offerId, string gameId, OfferType type, bool isEnabled)
        {
            if (string.IsNullOrEmpty(offerId)) throw new ArgumentException("OfferId must not be null or empty.", nameof(offerId));
            if (string.IsNullOrEmpty(gameId)) throw new ArgumentException("GameId must not be null or empty.", nameof(gameId));
            OfferId = offerId;
            GameId = gameId;
            Type = type;
            IsEnabled = isEnabled;
        }

        public static ShopOffer Active(string offerId, string gameId, OfferType type) =>
            new ShopOffer(offerId, gameId, type, true);

        public static ShopOffer Disabled(string offerId, string gameId, OfferType type) =>
            new ShopOffer(offerId, gameId, type, false);
    }

    public readonly struct ShopCatalogPolicy
    {
        public bool OffersEnabled { get; }
        public bool PlatformTaxHandledByStore { get; }
        public string PolicyDescription { get; }

        private ShopCatalogPolicy(bool offersEnabled, string policyDescription)
        {
            if (string.IsNullOrEmpty(policyDescription)) throw new ArgumentException("PolicyDescription must not be null or empty.", nameof(policyDescription));
            OffersEnabled = offersEnabled;
            PlatformTaxHandledByStore = true;
            PolicyDescription = policyDescription;
        }

        public static ShopCatalogPolicy Enabled(string description) => new ShopCatalogPolicy(true, description);
        public static ShopCatalogPolicy Disabled(string description) => new ShopCatalogPolicy(false, description);

        public static readonly ShopCatalogPolicy Default =
            new ShopCatalogPolicy(false, "No offers active until owner approval and store setup complete");
    }

    public static class ShopCatalogGate
    {
        private const string WonderPopId = "Wonder_Pop";

        public readonly struct EligibilityResult
        {
            public bool IsEligible { get; }
            public string Reason { get; }

            private EligibilityResult(bool eligible, string reason) { IsEligible = eligible; Reason = reason; }
            public static EligibilityResult Eligible() => new EligibilityResult(true, string.Empty);
            public static EligibilityResult Ineligible(string reason) => new EligibilityResult(false, reason);
        }

        public static EligibilityResult CanShowOffer(string gameId, ShopOffer offer, ShopCatalogPolicy policy)
        {
            // Fail closed: a missing or non-catalog game context is never eligible
            if (string.IsNullOrEmpty(gameId))
                return EligibilityResult.Ineligible("GameIdMissing");
            if (!GameProfileCatalog.IsKnownId(gameId))
                return EligibilityResult.Ineligible("UnknownGame");

            if (!string.Equals(gameId, offer.GameId, StringComparison.Ordinal))
                return EligibilityResult.Ineligible("GameIdMismatch");

            if (string.Equals(gameId, WonderPopId, StringComparison.Ordinal))
                return EligibilityResult.Ineligible("WonderPopShopProhibited");
            if (!policy.OffersEnabled)
                return EligibilityResult.Ineligible("ShopOffersDisabled");
            if (!offer.IsEnabled)
                return EligibilityResult.Ineligible("OfferDisabled");
            return EligibilityResult.Eligible();
        }

        // default(ShopCatalogPolicy) (no description) is treated as Default: the store price is authoritative.
        public static bool IsPlatformPriceAuthoritative(ShopCatalogPolicy policy) =>
            policy.PolicyDescription == null || policy.PlatformTaxHandledByStore;

        public static bool CanShowNoAds(string gameId, ShopCatalogPolicy policy)
        {
            // Fail closed: a missing or non-catalog game context is never eligible
            if (string.IsNullOrEmpty(gameId))
                return false;
            if (!GameProfileCatalog.IsKnownId(gameId))
                return false;

            if (string.Equals(gameId, WonderPopId, StringComparison.Ordinal))
                return false;
            if (!policy.OffersEnabled)
                return false;
            return true;
        }
    }

    public sealed class ShopCatalog
    {
        private readonly Dictionary<string, ShopOffer> _offers = new Dictionary<string, ShopOffer>();

        public int Count => _offers.Count;

        public void AddOrUpdate(ShopOffer offer)
        {
            if (string.IsNullOrEmpty(offer.OfferId))
                throw new ArgumentException("offer.OfferId must not be null or empty.");
            _offers[offer.OfferId] = offer;
        }

        public bool TryGet(string offerId, out ShopOffer offer) => _offers.TryGetValue(offerId, out offer);

        public IReadOnlyList<ShopOffer> GetForGame(string gameId) =>
            _offers.Values.Where(o => o.GameId == gameId && o.IsEnabled).ToList();

        public void Clear() => _offers.Clear();
    }
}
