// ENG-M15-010: Refunds/revocations/reconciliation contracts
// Pure contract layer — no store SDK, no Unity namespaces, fail-closed.

using System;
using System.Collections.Generic;
using System.Linq;

namespace Match3.Engine.Runtime.Commerce
{
    public enum RefundStatus
    {
        None,        // No refund action
        Requested,   // Refund request submitted
        Approved,    // Store/server approved
        Rejected,    // Store/server rejected
        Revoked,     // Entitlement revoked (chargeback/fraud)
        Reconciled   // Ledger reconciled after refund
    }

    public readonly struct RefundPolicy
    {
        public bool RevocationAllowed { get; }
        public bool RequireServerReconciliation { get; }
        public string PolicyDescription { get; }

        private RefundPolicy(bool revocationAllowed, bool requireServer, string description)
        {
            if (string.IsNullOrEmpty(description))
                throw new ArgumentException("PolicyDescription must not be null or empty.", nameof(description));
            RevocationAllowed = revocationAllowed;
            RequireServerReconciliation = requireServer;
            PolicyDescription = description;
        }

        /// <summary>RevocationAllowed=true, RequireServerReconciliation=true.</summary>
        public static RefundPolicy Active(string description) =>
            new RefundPolicy(true, true, description);

        /// <summary>RevocationAllowed=false, RequireServerReconciliation=true.</summary>
        public static RefundPolicy Disabled(string description) =>
            new RefundPolicy(false, true, description);

        /// <summary>Default fail-closed policy.</summary>
        public static RefundPolicy Default =>
            Disabled("All refunds require server reconciliation; fail-closed by default");
    }

    public readonly struct RefundRecord
    {
        public string OrderId { get; }
        public string GameId { get; }
        public RefundStatus Status { get; }

        private RefundRecord(string orderId, string gameId, RefundStatus status)
        {
            if (string.IsNullOrEmpty(orderId))
                throw new ArgumentException("OrderId must not be null or empty.", nameof(orderId));
            if (string.IsNullOrEmpty(gameId))
                throw new ArgumentException("GameId must not be null or empty.", nameof(gameId));
            OrderId = orderId;
            GameId = gameId;
            Status = status;
        }

        public static RefundRecord Pending(string orderId, string gameId) =>
            new RefundRecord(orderId, gameId, RefundStatus.Requested);

        public static RefundRecord Approved(string orderId, string gameId) =>
            new RefundRecord(orderId, gameId, RefundStatus.Approved);

        public static RefundRecord Revoked(string orderId, string gameId) =>
            new RefundRecord(orderId, gameId, RefundStatus.Revoked);

        public static RefundRecord Reconciled(string orderId, string gameId) =>
            new RefundRecord(orderId, gameId, RefundStatus.Reconciled);
    }

    public static class RefundGate
    {
        private static RefundPolicy Normalize(RefundPolicy policy) =>
            policy.PolicyDescription == null ? RefundPolicy.Default : policy;

        // ponytail: ordinal string compare; switch to hash-set if game list grows beyond ~20
        private const string WonderPopId = "Wonder_Pop";

        /// <summary>
        /// Determines whether an entitlement should be revoked.
        /// Guard order is structurally enforced:
        ///   0. gameId must be non-empty, a catalog ID (GameProfileCatalog, Ordinal) and equal
        ///      record.GameId (Ordinal), else never revoke. Revoke-safe: an unknown or case-variant ID
        ///      cannot hold an entitlement (every grant gate rejects it), so returning false revokes
        ///      nothing that exists; it never revokes another game's entitlement.
        ///   1. Wonder_Pop never revokes (child-safety / policy).
        ///   2. Policy must permit revocation.
        ///   3. Record must be in an actionable state.
        /// </summary>
        public static bool ShouldRevokeEntitlement(string gameId, RefundRecord record, RefundPolicy policy)
        {
            policy = Normalize(policy);

            if (string.IsNullOrEmpty(gameId))
                return false;
            if (!GameProfileCatalog.IsKnownId(gameId))
                return false;

            if (!string.Equals(gameId, record.GameId, StringComparison.Ordinal))
                return false;

            // Guard 1 (after GameIdMismatch): Wonder_Pop never has entitlements to revoke
            if (string.Equals(gameId, WonderPopId, StringComparison.Ordinal))
                return false;

            // Guard 2: policy must permit revocation
            if (!policy.RevocationAllowed)
                return false;

            // Guard 3: record must be in an actionable state
            if (record.Status != RefundStatus.Approved && record.Status != RefundStatus.Revoked)
                return false;

            return true;
        }

        /// <summary>Always returns policy.RequireServerReconciliation (always true from factory).</summary>
        public static bool RequiresServerReconciliation(RefundPolicy policy) =>
            Normalize(policy).RequireServerReconciliation;

        /// <summary>Rejected or Reconciled are terminal states.</summary>
        public static bool IsRefundTerminal(RefundStatus status) =>
            status == RefundStatus.Rejected || status == RefundStatus.Reconciled;

        /// <summary>
        /// Throws InvalidOperationException if RequireServerReconciliation is false.
        /// Never fires with factory-constructed policies; gates are explicit for auditability.
        /// </summary>
        public static void AssertReconciliationRequired(RefundPolicy policy)
        {
            policy = Normalize(policy);

            if (!policy.RequireServerReconciliation)
                throw new InvalidOperationException(
                    "Policy does not require server reconciliation; this violates the fail-closed contract.");
        }
    }

    /// <summary>Audit ledger for refund events, keyed by orderId.</summary>
    public sealed class RefundLedger
    {
        private readonly Dictionary<string, RefundRecord> _records = new Dictionary<string, RefundRecord>();

        /// <summary>Records or overwrites a refund record by its OrderId.</summary>
        public void Record(RefundRecord record) =>
            _records[record.OrderId] = record;

        public bool TryGet(string orderId, out RefundRecord record) =>
            _records.TryGetValue(orderId, out record);

        public IReadOnlyList<RefundRecord> GetByStatus(RefundStatus status) =>
            _records.Values.Where(r => r.Status == status).ToList();

        public void Clear() => _records.Clear();

        public int Count => _records.Count;
    }
}
