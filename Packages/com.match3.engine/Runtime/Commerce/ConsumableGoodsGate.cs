using System;
using System.Collections.Generic;

namespace Match3.Engine.Runtime.Commerce
{
    public readonly struct ConsumablePolicy
    {
        public bool ConsumablesEnabled { get; }
        public bool RequireServerLedger { get; }
        public string PolicyDescription { get; }

        public ConsumablePolicy(bool consumablesEnabled, string policyDescription)
        {
            if (string.IsNullOrEmpty(policyDescription))
                throw new ArgumentException("PolicyDescription must not be null or empty.", nameof(policyDescription));
            ConsumablesEnabled = consumablesEnabled;
            RequireServerLedger = true; // non-negotiable
            PolicyDescription = policyDescription;
        }

        public static ConsumablePolicy Enabled(string desc) =>
            new ConsumablePolicy(true, desc);

        public static ConsumablePolicy Disabled(string desc) =>
            new ConsumablePolicy(false, desc);

        public static ConsumablePolicy Default =>
            Disabled("No consumables until explicit policy review; fail-closed by default");
    }

    public readonly struct ConsumableItem
    {
        public string ItemId { get; }
        public string GameId { get; }
        public int Quantity { get; }

        public ConsumableItem(string itemId, string gameId, int quantity)
        {
            if (string.IsNullOrEmpty(itemId))
                throw new ArgumentException("ItemId must not be null or empty.", nameof(itemId));
            if (string.IsNullOrEmpty(gameId))
                throw new ArgumentException("GameId must not be null or empty.", nameof(gameId));
            if (quantity < 1)
                throw new ArgumentOutOfRangeException(nameof(quantity), "Quantity must be at least 1.");
            ItemId = itemId;
            GameId = gameId;
            Quantity = quantity;
        }

        public static ConsumableItem Bundle(string itemId, string gameId, int quantity) =>
            new ConsumableItem(itemId, gameId, quantity);

        public static ConsumableItem Single(string itemId, string gameId) =>
            new ConsumableItem(itemId, gameId, 1);
    }

    public static class ConsumableGoodsGate
    {
        public static AdEligibilityResult CheckPurchaseEligibility(
            string gameId, ConsumablePolicy policy, bool isKidProfile)
        {
            // Guard 1: Wonder_Pop is independently blocked first, always
            if (string.Equals(gameId, "Wonder_Pop", StringComparison.Ordinal))
                return AdEligibilityResult.Ineligible("WonderPopConsumablesProhibited");

            // Guard 2: policy must enable consumables
            if (!policy.ConsumablesEnabled)
                return AdEligibilityResult.Ineligible("ConsumablesDisabled");

            // Guard 3: kid profile may prohibit IAP
            // Unconditional: kid profiles are never IAP-eligible
            if (isKidProfile)
                return AdEligibilityResult.Ineligible("KidProfileConsumablesProhibited");

            return AdEligibilityResult.Eligible();
        }

        [Obsolete("Kid-profile flags are ignored: pass isKidProfile explicitly. This overload treats the profile as a kid profile (fail-closed).")]
        public static AdEligibilityResult CheckPurchaseEligibility(
            string gameId, ConsumablePolicy policy, KidProfilePolicy kidPolicy) =>
            CheckPurchaseEligibility(gameId, policy, isKidProfile: true);

        public static bool IsServerLedgerRequired(ConsumablePolicy policy) => true;

        public static bool ValidateConsumableItem(ConsumableItem item) => item.Quantity >= 1;
    }

    public sealed class ConsumptionTracker
    {
        private readonly HashSet<string> _consumed = new HashSet<string>(StringComparer.Ordinal);

        public int Count => _consumed.Count;

        public bool IsConsumed(string itemId)
        {
            if (string.IsNullOrEmpty(itemId))
                throw new ArgumentException("itemId must not be null or empty.", nameof(itemId));
            return _consumed.Contains(itemId);
        }

        public void MarkConsumed(string itemId)
        {
            if (string.IsNullOrEmpty(itemId))
                throw new ArgumentException("itemId must not be null or empty.", nameof(itemId));
            _consumed.Add(itemId); // no-op if already present
        }

        public void Reset() => _consumed.Clear();
    }
}
