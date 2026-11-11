using System;
using System.Collections.Generic;

namespace Match3.Engine.Runtime.Economy
{
    public readonly struct ReconciliationEvent
    {
        public string GameId { get; }
        public string TransactionId { get; }
        public string EventType { get; }
        public long Timestamp { get; }
        public bool IsResolved { get; }

        public ReconciliationEvent(string gameId, string transactionId, string eventType, long timestamp, bool isResolved)
        {
            if (string.IsNullOrEmpty(gameId)) throw new ArgumentException("GameId must not be null or empty.", nameof(gameId));
            if (string.IsNullOrEmpty(transactionId)) throw new ArgumentException("TransactionId must not be null or empty.", nameof(transactionId));
            if (string.IsNullOrEmpty(eventType)) throw new ArgumentException("EventType must not be null or empty.", nameof(eventType));
            if (timestamp <= 0) throw new ArgumentOutOfRangeException(nameof(timestamp), "Timestamp must be positive Unix ms.");
            GameId = gameId;
            TransactionId = transactionId;
            EventType = eventType;
            Timestamp = timestamp;
            IsResolved = isResolved;
        }

        public static ReconciliationEvent FailedGrant(string gameId, string transactionId, long timestamp)
            => new ReconciliationEvent(gameId, transactionId, "FailedGrant", timestamp, false);

        public static ReconciliationEvent Duplicate(string gameId, string transactionId, long timestamp)
            => new ReconciliationEvent(gameId, transactionId, "Duplicate", timestamp, false);

        public static ReconciliationEvent Resolved(ReconciliationEvent original)
            => new ReconciliationEvent(original.GameId, original.TransactionId, original.EventType, original.Timestamp, true);
    }

    public readonly struct ReconciliationPolicy
    {
        public bool RequireOwnerAuthorization { get; }
        public bool LeastPrivilegeAccess { get; }
        public string PolicyDescription { get; }

        public ReconciliationPolicy(bool requireOwnerAuthorization, bool leastPrivilegeAccess, string policyDescription)
        {
            if (string.IsNullOrEmpty(policyDescription)) throw new ArgumentException("PolicyDescription must not be null or empty.", nameof(policyDescription));
            RequireOwnerAuthorization = requireOwnerAuthorization;
            LeastPrivilegeAccess = leastPrivilegeAccess;
            PolicyDescription = policyDescription;
        }

        public static ReconciliationPolicy Standard(string desc)
            => new ReconciliationPolicy(true, true, desc);

        public static ReconciliationPolicy Default
            => Standard("Backoffice corrections require owner authorization with least-privilege access and full audit trail");
    }

    public static class ReconciliationGate
    {
        public readonly struct GateResult
        {
            public bool IsEligible { get; }
            public string Reason { get; }
            public GateResult(bool isEligible, string reason) { IsEligible = isEligible; Reason = reason; }
        }

        public static GateResult CanRunReport(string gameId, ReconciliationPolicy policy)
        {
            // Guard 1 FIRST (CRITICAL): Wonder_Pop is always ineligible
            if (string.Equals(gameId, "Wonder_Pop", StringComparison.Ordinal))
                return new GateResult(false, "WonderPopReconciliationProhibited");

            if (!policy.RequireOwnerAuthorization)
                return new GateResult(false, "OwnerAuthorizationDisabled");

            return new GateResult(true, "Eligible");
        }

        public static GateResult CanApplyCorrection(string gameId, ReconciliationPolicy policy)
        {
            // Guard 1 FIRST (CRITICAL): Wonder_Pop is always ineligible
            if (string.Equals(gameId, "Wonder_Pop", StringComparison.Ordinal))
                return new GateResult(false, "WonderPopReconciliationProhibited");

            if (!policy.RequireOwnerAuthorization)
                return new GateResult(false, "OwnerAuthorizationDisabled");

            return new GateResult(true, "Eligible");
        }

        public static bool RequiresAuditTrail(ReconciliationPolicy policy) => true;
    }

    public sealed class ReconciliationLog
    {
        private readonly Dictionary<string, ReconciliationEvent> _store = new Dictionary<string, ReconciliationEvent>();

        public void Record(ReconciliationEvent ev)
        {
            _store[ev.TransactionId] = ev;
        }

        public bool TryGet(string transactionId, out ReconciliationEvent ev)
            => _store.TryGetValue(transactionId, out ev);

        public IReadOnlyList<ReconciliationEvent> GetUnresolved()
        {
            var list = new List<ReconciliationEvent>();
            foreach (var ev in _store.Values)
                if (!ev.IsResolved)
                    list.Add(ev);
            return list;
        }

        public void Clear() => _store.Clear();

        public int Count => _store.Count;
    }
}
