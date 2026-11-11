// ENG-M18-009 RevenueReconciliationGate stdlib contract
// stdlib only: System, System.Collections.Generic
using System;
using System.Collections.Generic;

namespace Match3.Engine.Runtime.Analytics
{
    public enum ReconciliationSource
    {
        StoreReceipt,
        MediationReport,
        InternalEvent
    }

    public readonly struct ReconciliationEntry
    {
        public string GameId { get; }
        public ReconciliationSource Source { get; }
        public bool IsRefund { get; }

        private ReconciliationEntry(string gameId, ReconciliationSource source, bool isRefund)
        {
            GameId = gameId;
            Source = source;
            IsRefund = isRefund;
        }

        public static ReconciliationEntry Create(string gameId, ReconciliationSource source, bool isRefund)
        {
            if (string.IsNullOrEmpty(gameId))
                throw new ArgumentException("GameId must not be null or empty.", nameof(gameId));
            return new ReconciliationEntry(gameId, source, isRefund);
        }

        public bool IsStoreAuthoritative => Source == ReconciliationSource.StoreReceipt;
        public bool RequiresDedup => !IsStoreAuthoritative;
    }

    public readonly struct ReconciliationPolicy
    {
        public bool AllowMediationReconciliation { get; }
        public bool RequireStoreAuthority { get; }
        public string PolicyDescription { get; }

        private ReconciliationPolicy(bool allowMediation, bool requireStore, string desc)
        {
            AllowMediationReconciliation = allowMediation;
            RequireStoreAuthority = requireStore;
            PolicyDescription = desc;
        }

        public static ReconciliationPolicy Safe(string desc)
        {
            if (string.IsNullOrEmpty(desc))
                throw new ArgumentException("PolicyDescription must not be null or empty.", nameof(desc));
            return new ReconciliationPolicy(false, true, desc);
        }

        public static ReconciliationPolicy Default =>
            Safe("Mediation reconciliation disabled; store receipt authority required");
    }

    public static class RevenueReconciliationGate
    {
        private const string WonderPopId = "Wonder_Pop";

        public static (bool eligible, string reason) CanReconcile(
            string gameId,
            ReconciliationEntry entry,
            ReconciliationPolicy policy)
        {
            // Guard 1 (FIRST): Wonder_Pop ordinal block
            if (string.Equals(gameId, WonderPopId, StringComparison.Ordinal))
                return (false, "WonderPopReconciliationProhibited");

            // Guard 2: mediation disabled
            if (entry.Source == ReconciliationSource.MediationReport && !policy.AllowMediationReconciliation)
                return (false, "MediationReconciliationDisabled");

            // Guard 3: store authority required
            if (policy.RequireStoreAuthority && !entry.IsStoreAuthoritative)
                return (false, "StoreAuthorityRequired");

            return (true, string.Empty);
        }

        public static bool IsStoreSource(ReconciliationEntry entry) => entry.IsStoreAuthoritative;

        public static bool RequiresDedup(ReconciliationEntry entry) => entry.RequiresDedup;
    }

    public sealed class ReconciliationLog
    {
        private readonly List<(string gameId, ReconciliationSource source)> _entries =
            new List<(string, ReconciliationSource)>();

        public int Count => _entries.Count;

        public void Record(string gameId, ReconciliationSource source)
        {
            if (string.IsNullOrEmpty(gameId))
                throw new ArgumentException("GameId must not be null or empty.", nameof(gameId));
            _entries.Add((gameId, source));
        }

        public bool HasEntry(string gameId) =>
            _entries.Exists(e => string.Equals(e.gameId, gameId, StringComparison.Ordinal));

        public void Clear() => _entries.Clear();
    }
}
