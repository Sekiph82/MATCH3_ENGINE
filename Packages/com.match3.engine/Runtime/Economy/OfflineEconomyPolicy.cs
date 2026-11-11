using System;
using System.Collections.Generic;

namespace Match3.Engine.Runtime.Economy
{
    public enum ResourceTier { Free, Premium }

    public readonly struct OfflinePolicy
    {
        public bool AllowOfflineFreeSpend { get; }
        public bool AllowOfflinePremiumSpend { get; }
        public string PolicyDescription { get; }

        public OfflinePolicy(bool allowFree, bool allowPremium, string description)
        {
            if (string.IsNullOrEmpty(description))
                throw new ArgumentException("PolicyDescription must not be null or empty.", nameof(description));
            AllowOfflineFreeSpend = allowFree;
            AllowOfflinePremiumSpend = allowPremium;
            PolicyDescription = description;
        }

        public static OfflinePolicy Standard(string description) =>
            new OfflinePolicy(true, false, description);

        public static OfflinePolicy Default =>
            Standard("Free resources spendable offline; premium requires server sync");
    }

    public readonly struct SpendEligibility
    {
        public bool IsEligible { get; }
        public string Reason { get; }

        private SpendEligibility(bool eligible, string reason) { IsEligible = eligible; Reason = reason; }

        public static SpendEligibility Eligible() => new SpendEligibility(true, string.Empty);
        public static SpendEligibility Ineligible(string reason) => new SpendEligibility(false, reason);
    }

    public static class OfflineEconomyGate
    {
        private const string WonderPopId = "Wonder_Pop";

        public static SpendEligibility CanSpendOffline(string gameId, ResourceTier tier, OfflinePolicy policy)
        {
            // Fail closed: a missing or non-catalog game context is never eligible
            if (string.IsNullOrEmpty(gameId))
                return SpendEligibility.Ineligible("GameIdMissing");
            if (!GameProfileCatalog.IsKnownId(gameId))
                return SpendEligibility.Ineligible("UnknownGame");

            // Guard 1 (FIRST): Wonder_Pop is never eligible for offline spend
            if (string.Equals(gameId, WonderPopId, StringComparison.Ordinal))
                return SpendEligibility.Ineligible("WonderPopOfflineSpendProhibited");

            // Guard 2: premium tier blocked unless policy explicitly allows it
            if (tier == ResourceTier.Premium && !policy.AllowOfflinePremiumSpend)
                return SpendEligibility.Ineligible("PremiumRequiresServerSync");

            // Guard 3: free tier blocked if policy disables it
            if (tier == ResourceTier.Free && !policy.AllowOfflineFreeSpend)
                return SpendEligibility.Ineligible("FreeSpendDisabled");

            return SpendEligibility.Eligible();
        }

        public static bool RequiresReconciliationOnReconnect(ResourceTier tier) =>
            tier == ResourceTier.Premium;
    }

    public sealed class OfflinePendingQueue
    {
        private readonly List<string> _items = new List<string>();

        public int Count => _items.Count;
        public bool IsEmpty => _items.Count == 0;

        public void Enqueue(string transactionId)
        {
            if (string.IsNullOrEmpty(transactionId))
                throw new ArgumentException("transactionId must not be null or empty.", nameof(transactionId));
            _items.Add(transactionId);
        }

        public IReadOnlyList<string> DrainAll()
        {
            var copy = new List<string>(_items);
            _items.Clear();
            return copy;
        }
    }
}
