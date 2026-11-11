using System;
using System.Collections.Generic;

namespace Match3.Engine.Runtime.Economy
{
    public readonly struct SpendRequest
    {
        public string GameId { get; }
        public CurrencyType Currency { get; }
        public int Amount { get; }
        public string TransactionId { get; }

        private SpendRequest(string gameId, CurrencyType currency, int amount, string transactionId)
        {
            if (string.IsNullOrEmpty(gameId)) throw new ArgumentException("GameId cannot be null or empty.", nameof(gameId));
            if (string.IsNullOrEmpty(transactionId)) throw new ArgumentException("TransactionId cannot be null or empty.", nameof(transactionId));
            if (amount <= 0) throw new ArgumentOutOfRangeException(nameof(amount), "Amount must be > 0.");
            GameId = gameId;
            Currency = currency;
            Amount = amount;
            TransactionId = transactionId;
        }

        public static SpendRequest Create(string gameId, CurrencyType currency, int amount, string transactionId)
            => new SpendRequest(gameId, currency, amount, transactionId);
    }

    public readonly struct GrantRequest
    {
        public string GameId { get; }
        public CurrencyType Currency { get; }
        public int Amount { get; }
        public string Source { get; }
        public string TransactionId { get; }

        private GrantRequest(string gameId, CurrencyType currency, int amount, string source, string transactionId)
        {
            if (string.IsNullOrEmpty(gameId)) throw new ArgumentException("GameId cannot be null or empty.", nameof(gameId));
            if (string.IsNullOrEmpty(source)) throw new ArgumentException("Source cannot be null or empty.", nameof(source));
            if (string.IsNullOrEmpty(transactionId)) throw new ArgumentException("TransactionId cannot be null or empty.", nameof(transactionId));
            if (amount <= 0) throw new ArgumentOutOfRangeException(nameof(amount), "Amount must be > 0.");
            GameId = gameId;
            Currency = currency;
            Amount = amount;
            Source = source;
            TransactionId = transactionId;
        }

        public static GrantRequest Create(string gameId, CurrencyType currency, int amount, string source, string transactionId)
            => new GrantRequest(gameId, currency, amount, source, transactionId);
    }

    public readonly struct SpendGrantPolicy
    {
        public bool RequireServerAuthorization { get; }
        public bool AllowPartialFulfillment { get; }
        public string PolicyDescription { get; }

        private SpendGrantPolicy(bool requireServerAuthorization, bool allowPartialFulfillment, string policyDescription)
        {
            if (string.IsNullOrEmpty(policyDescription)) throw new ArgumentException("PolicyDescription cannot be null or empty.", nameof(policyDescription));
            RequireServerAuthorization = requireServerAuthorization;
            AllowPartialFulfillment = allowPartialFulfillment;
            PolicyDescription = policyDescription;
        }

        public static SpendGrantPolicy Standard(string desc)
            => new SpendGrantPolicy(true, false, desc);

        public static readonly SpendGrantPolicy Default =
            Standard("All virtual good transactions require server authorization; partial fulfillment not permitted");
    }

    public static class VirtualGoodsGate
    {
        private const string WonderPopId = "Wonder_Pop";

        public static SpendEligibility CanSpend(string gameId, SpendGrantPolicy policy)
        {
            // Fail closed: a missing or non-catalog game context is never eligible
            if (string.IsNullOrEmpty(gameId))
                return SpendEligibility.Ineligible("GameIdMissing");
            if (!GameProfileCatalog.IsKnownId(gameId))
                return SpendEligibility.Ineligible("UnknownGame");

            // Guard 1 FIRST: Wonder_Pop is never eligible
            if (string.Equals(gameId, WonderPopId, StringComparison.Ordinal))
                return SpendEligibility.Ineligible("WonderPopSpendProhibited");

            // Guard 2: server authorization must be required
            if (!policy.RequireServerAuthorization)
                return SpendEligibility.Ineligible("ServerAuthorizationDisabled");

            return SpendEligibility.Eligible();
        }

        public static SpendEligibility CanGrant(string gameId, SpendGrantPolicy policy)
        {
            // Fail closed: a missing or non-catalog game context is never eligible
            if (string.IsNullOrEmpty(gameId))
                return SpendEligibility.Ineligible("GameIdMissing");
            if (!GameProfileCatalog.IsKnownId(gameId))
                return SpendEligibility.Ineligible("UnknownGame");

            // Guard 1 FIRST: Wonder_Pop is never eligible
            if (string.Equals(gameId, WonderPopId, StringComparison.Ordinal))
                return SpendEligibility.Ineligible("WonderPopGrantProhibited");

            // Guard 2: server authorization must be required
            if (!policy.RequireServerAuthorization)
                return SpendEligibility.Ineligible("ServerAuthorizationDisabled");

            return SpendEligibility.Eligible();
        }

        public static bool RequiresAtomicFulfillment(SpendGrantPolicy policy) => true;
    }

    public sealed class CompensationLog
    {
        private readonly Dictionary<string, (string gameId, string reason)> _entries =
            new Dictionary<string, (string, string)>();

        public int Count => _entries.Count;

        public void Record(string gameId, string transactionId, string reason)
        {
            if (string.IsNullOrEmpty(gameId)) throw new ArgumentException("gameId cannot be null or empty.", nameof(gameId));
            if (string.IsNullOrEmpty(transactionId)) throw new ArgumentException("transactionId cannot be null or empty.", nameof(transactionId));
            if (string.IsNullOrEmpty(reason)) throw new ArgumentException("reason cannot be null or empty.", nameof(reason));
            _entries[transactionId] = (gameId, reason);
        }

        public bool HasEntry(string transactionId) => _entries.ContainsKey(transactionId);

        public void Clear() => _entries.Clear();
    }
}
