using System;
using System.Collections.Generic;

namespace Match3.Engine.Runtime.Economy
{
    public enum CurrencyType { Coins, PremiumGems, Hearts, Boosters }

    public readonly struct LedgerEntry
    {
        public string GameId { get; }
        public CurrencyType Currency { get; }
        public long Amount { get; }
        public string TransactionId { get; }

        private LedgerEntry(string gameId, CurrencyType currency, long amount, string transactionId)
        {
            if (string.IsNullOrEmpty(gameId)) throw new ArgumentException("GameId cannot be null or empty.", nameof(gameId));
            if (string.IsNullOrEmpty(transactionId)) throw new ArgumentException("TransactionId cannot be null or empty.", nameof(transactionId));
            if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount), "Amount must be >= 0.");
            GameId = gameId;
            Currency = currency;
            Amount = amount;
            TransactionId = transactionId;
        }

        public static LedgerEntry Credit(string gameId, CurrencyType currency, long amount, string txId)
            => new LedgerEntry(gameId, currency, amount, txId);

        public static LedgerEntry Debit(string gameId, CurrencyType currency, long amount, string txId)
            => new LedgerEntry(gameId, currency, amount, txId);
    }

    public readonly struct LedgerPolicy
    {
        public bool ServerAuthoritative { get; }
        public bool AllowOfflineNonPremium { get; }
        public string PolicyDescription { get; }

        private LedgerPolicy(bool serverAuthoritative, bool allowOfflineNonPremium, string policyDescription)
        {
            if (string.IsNullOrEmpty(policyDescription)) throw new ArgumentException("PolicyDescription cannot be null or empty.", nameof(policyDescription));
            ServerAuthoritative = serverAuthoritative;
            AllowOfflineNonPremium = allowOfflineNonPremium;
            PolicyDescription = policyDescription;
        }

        public static LedgerPolicy Standard(string desc) => new LedgerPolicy(true, true, desc);

        public static readonly LedgerPolicy Default = Standard("All premium rewards require server authorization; free play allowed offline");
    }

    public static class PlayerLedgerGate
    {
        private const string WonderPopId = "Wonder_Pop";

        public static (bool eligible, string reason) CanCreditPremium(string gameId, LedgerPolicy policy)
        {
            if (string.Equals(gameId, WonderPopId, StringComparison.Ordinal))
                return (false, "WonderPopPremiumProhibited");
            if (!policy.ServerAuthoritative)
                return (false, "ServerAuthorizationRequired");
            return (true, null);
        }

        public static bool IsOfflinePlayAllowed(string gameId, LedgerPolicy policy)
        {
            if (string.Equals(gameId, WonderPopId, StringComparison.Ordinal))
                return false;
            return policy.AllowOfflineNonPremium;
        }

        public static bool RequiresServerSync(LedgerPolicy policy) => policy.ServerAuthoritative;
    }

    public sealed class PlayerWallet
    {
        private readonly Dictionary<CurrencyType, long> _balances = new Dictionary<CurrencyType, long>();

        public void Credit(CurrencyType currency, long amount)
        {
            if (amount <= 0) throw new ArgumentOutOfRangeException(nameof(amount), "Credit amount must be > 0.");
            if (!_balances.ContainsKey(currency)) _balances[currency] = 0;
            _balances[currency] += amount;
        }

        public bool TryDebit(CurrencyType currency, long amount)
        {
            if (amount <= 0) throw new ArgumentOutOfRangeException(nameof(amount), "Debit amount must be > 0.");
            if (!_balances.ContainsKey(currency) || _balances[currency] < amount) return false;
            _balances[currency] -= amount;
            return true;
        }

        public long GetBalance(CurrencyType currency)
            => _balances.TryGetValue(currency, out var v) ? v : 0L;

        public void Reset() => _balances.Clear();
    }
}
