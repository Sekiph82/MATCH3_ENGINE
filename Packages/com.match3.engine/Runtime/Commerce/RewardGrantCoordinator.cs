using System;
using System.Collections.Generic;

namespace Match3.Engine.Runtime.Commerce
{
    public enum RewardGrantStatus
    {
        Pending,
        Granted,
        Skipped,
        Failed,
        Duplicate
    }

    public readonly struct RewardTransaction
    {
        public string TransactionId { get; }
        public string PlacementId { get; }
        public string GameId { get; }
        public RewardGrantStatus Status { get; }
        public DateTime GrantedAtUtc { get; }

        public RewardTransaction(string transactionId, string placementId, string gameId,
            RewardGrantStatus status = RewardGrantStatus.Pending, DateTime grantedAtUtc = default)
        {
            if (string.IsNullOrEmpty(transactionId)) throw new ArgumentException("TransactionId required.", nameof(transactionId));
            if (string.IsNullOrEmpty(placementId)) throw new ArgumentException("PlacementId required.", nameof(placementId));
            if (string.IsNullOrEmpty(gameId)) throw new ArgumentException("GameId required.", nameof(gameId));
            TransactionId = transactionId;
            PlacementId = placementId;
            GameId = gameId;
            Status = status;
            GrantedAtUtc = grantedAtUtc;
        }

        public RewardTransaction MarkGranted(DateTime utc) =>
            new RewardTransaction(TransactionId, PlacementId, GameId, RewardGrantStatus.Granted, utc);

        public RewardTransaction MarkSkipped() =>
            new RewardTransaction(TransactionId, PlacementId, GameId, RewardGrantStatus.Skipped, GrantedAtUtc);

        public RewardTransaction MarkFailed() =>
            new RewardTransaction(TransactionId, PlacementId, GameId, RewardGrantStatus.Failed, GrantedAtUtc);

        public RewardTransaction MarkDuplicate() =>
            new RewardTransaction(TransactionId, PlacementId, GameId, RewardGrantStatus.Duplicate, GrantedAtUtc);
    }

    public interface IRewardLedger
    {
        bool TryRecord(RewardTransaction transaction);
        RewardTransaction? Find(string transactionId);
    }

    public sealed class InMemoryRewardLedger : IRewardLedger
    {
        private readonly Dictionary<string, RewardTransaction> _store = new Dictionary<string, RewardTransaction>();

        public bool TryRecord(RewardTransaction transaction)
        {
            if (_store.ContainsKey(transaction.TransactionId)) return false;
            _store[transaction.TransactionId] = transaction;
            return true;
        }

        public RewardTransaction? Find(string transactionId)
        {
            if (_store.TryGetValue(transactionId, out var t)) return t;
            return null;
        }
    }

    public sealed class RewardGrantCoordinator
    {
        private readonly IRewardLedger _ledger;

        public RewardGrantCoordinator(IRewardLedger ledger)
        {
            _ledger = ledger ?? throw new ArgumentNullException(nameof(ledger));
        }

        public RewardGrantStatus TryGrant(RewardTransaction transaction, RewardedAdLifecycle lifecycle)
        {
            if (lifecycle.State != RewardedAdState.Completed) return RewardGrantStatus.Skipped;
            if (!_ledger.TryRecord(transaction)) return RewardGrantStatus.Duplicate;
            return RewardGrantStatus.Granted;
        }

        public RewardTransaction CreateTransaction(string placementId, string gameId) =>
            new RewardTransaction(Guid.NewGuid().ToString(), placementId, gameId);
    }
}
