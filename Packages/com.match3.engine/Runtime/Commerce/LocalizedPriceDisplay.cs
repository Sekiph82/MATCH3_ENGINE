// ENG-M15-003: Store-owned localized price UI contracts
// No Unity or external SDK namespaces. Pure stdlib only.
using System;
using System.Collections.Generic;

namespace Match3.Engine.Runtime.Commerce
{
    /// <summary>
    /// Holds store-provided localized price string. Never fabricate currency values.
    /// </summary>
    public readonly struct LocalizedPrice
    {
        public string FormattedPrice { get; }
        public string CurrencyCode { get; }
        public string GameId { get; }
        public string SkuId { get; }

        private LocalizedPrice(string formattedPrice, string currencyCode, string gameId, string skuId)
        {
            FormattedPrice = formattedPrice;
            CurrencyCode = currencyCode;
            GameId = gameId;
            SkuId = skuId;
        }

        public bool IsAvailable => !string.IsNullOrEmpty(FormattedPrice);

        /// <summary>
        /// Create a price from store metadata. All fields must be non-null/non-empty.
        /// </summary>
        public static LocalizedPrice FromStore(string formattedPrice, string currencyCode, string gameId, string skuId)
        {
            if (string.IsNullOrEmpty(formattedPrice))
                throw new ArgumentException("FormattedPrice must not be null or empty.", nameof(formattedPrice));
            if (string.IsNullOrEmpty(currencyCode))
                throw new ArgumentException("CurrencyCode must not be null or empty.", nameof(currencyCode));
            if (string.IsNullOrEmpty(gameId))
                throw new ArgumentException("GameId must not be null or empty.", nameof(gameId));
            if (string.IsNullOrEmpty(skuId))
                throw new ArgumentException("SkuId must not be null or empty.", nameof(skuId));

            return new LocalizedPrice(formattedPrice, currencyCode, gameId, skuId);
        }

        /// <summary>
        /// Sentinel for a price that is not yet available from the store.
        /// IsAvailable returns false.
        /// </summary>
        public static LocalizedPrice Unavailable(string gameId, string skuId)
        {
            if (string.IsNullOrEmpty(gameId))
                throw new ArgumentException("GameId must not be null or empty.", nameof(gameId));
            if (string.IsNullOrEmpty(skuId))
                throw new ArgumentException("SkuId must not be null or empty.", nameof(skuId));

            return new LocalizedPrice(string.Empty, string.Empty, gameId, skuId);
        }
    }

    /// <summary>
    /// Policy: display only store-provided price strings. Never fabricate or convert.
    /// </summary>
    public readonly struct PriceDisplayPolicy
    {
        public bool AllowFabricatedPrices { get; }
        public string PolicyDescription { get; }

        private PriceDisplayPolicy(bool allowFabricated, string policyDescription)
        {
            if (string.IsNullOrEmpty(policyDescription))
                throw new ArgumentException("PolicyDescription must not be null or empty.", nameof(policyDescription));
            AllowFabricatedPrices = allowFabricated;
            PolicyDescription = policyDescription;
        }

        /// <summary>
        /// Factory: store-owned policy, AllowFabricatedPrices is always false.
        /// </summary>
        public static PriceDisplayPolicy StoreOwned(string description) =>
            new PriceDisplayPolicy(false, description);

        /// <summary>
        /// Default store-owned policy with canonical description.
        /// </summary>
        public static readonly PriceDisplayPolicy Default =
            StoreOwned("Price must come from store; never hard-code or convert currency");
    }

    /// <summary>
    /// Result of a price-display eligibility check.
    /// </summary>
    public readonly struct AdEligibilityResult
    {
        public bool IsEligible { get; }
        public string Reason { get; }

        private AdEligibilityResult(bool eligible, string reason)
        {
            IsEligible = eligible;
            Reason = reason;
        }

        public static AdEligibilityResult Eligible() =>
            new AdEligibilityResult(true, string.Empty);

        public static AdEligibilityResult Ineligible(string reason) =>
            new AdEligibilityResult(false, reason ?? string.Empty);
    }

    /// <summary>
    /// Gate: enforces store-owned price display policy, including per-game prohibitions.
    /// Wonder_Pop price display is always prohibited (guard 1, structurally first).
    /// </summary>
    public static class LocalizedPriceDisplayGate
    {
        private const string WonderPopId = "Wonder_Pop";

        /// <summary>
        /// Returns Eligible only when all guards pass.
        /// Guard 1 (FIRST): Wonder_Pop -> always Ineligible("WonderPopPriceDisplayProhibited").
        /// Guard 2: price not available -> Ineligible("PriceUnavailableFromStore").
        /// Guard 3: currency code missing -> Ineligible("NoCurrencyCode").
        /// </summary>
        public static AdEligibilityResult CanDisplayPrice(
            string gameId,
            LocalizedPrice price,
            PriceDisplayPolicy policy)
        {
            // Guard 1 — Wonder_Pop always prohibited, checked first regardless of other state
            if (string.Equals(gameId, WonderPopId, StringComparison.Ordinal))
                return AdEligibilityResult.Ineligible("WonderPopPriceDisplayProhibited");

            // Guard 2 — price must have come from the store
            if (!price.IsAvailable)
                return AdEligibilityResult.Ineligible("PriceUnavailableFromStore");

            // Guard 3 — currency code must be present
            if (string.IsNullOrEmpty(price.CurrencyCode))
                return AdEligibilityResult.Ineligible("NoCurrencyCode");

            return AdEligibilityResult.Eligible();
        }

        /// <summary>
        /// Returns true when the policy forbids fabricated prices (always true for StoreOwned).
        /// </summary>
        public static bool IsStoreOwnedDisplay(PriceDisplayPolicy policy) =>
            !policy.AllowFabricatedPrices;

        /// <summary>
        /// Throws InvalidOperationException if the policy allows fabricated prices.
        /// </summary>
        public static void AssertNoFabricatedPrice(PriceDisplayPolicy policy)
        {
            if (policy.AllowFabricatedPrices)
                throw new InvalidOperationException(
                    "Policy allows fabricated prices, which violates store-owned price display requirements.");
        }
    }

    /// <summary>
    /// Tracks which SKUs have store-provided prices loaded (keyed by skuId).
    /// </summary>
    public sealed class PriceCache
    {
        private readonly Dictionary<string, LocalizedPrice> _store =
            new Dictionary<string, LocalizedPrice>(StringComparer.Ordinal);

        public int Count => _store.Count;

        /// <summary>Stores or overwrites a price entry keyed by skuId.</summary>
        public void Store(LocalizedPrice price) =>
            _store[price.SkuId] = price;

        /// <summary>Attempts to retrieve a cached price by skuId.</summary>
        public bool TryGet(string skuId, out LocalizedPrice price) =>
            _store.TryGetValue(skuId, out price);

        public void Clear() => _store.Clear();
    }
}
