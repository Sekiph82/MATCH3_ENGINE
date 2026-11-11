using System;

namespace Match3.Engine.Runtime.Commerce
{
    public enum PurchaseState
    {
        Pending,
        Deferred,
        Completed,
        Canceled,
        Failed,
        Restored
    }

    public readonly struct PendingPurchasePolicy
    {
        public bool AllowDeferredPurchases { get; }
        public string PolicyDescription { get; }

        public PendingPurchasePolicy(bool allowDeferredPurchases, string policyDescription)
        {
            if (string.IsNullOrEmpty(policyDescription))
                throw new ArgumentException("PolicyDescription must not be null or empty.", nameof(policyDescription));
            AllowDeferredPurchases = allowDeferredPurchases;
            PolicyDescription = policyDescription;
        }

        public static PendingPurchasePolicy NoDeferral(string desc) =>
            new PendingPurchasePolicy(false, desc);

        public static PendingPurchasePolicy WithDeferral(string desc) =>
            new PendingPurchasePolicy(true, desc);

        public static PendingPurchasePolicy Default =>
            NoDeferral("Deferred purchases disabled; fail-closed by default");
    }

    public static class PendingPurchaseGate
    {
        public static bool CanFulfillPurchase(string gameId, PurchaseState state, PendingPurchasePolicy policy)
        {
            // Fail closed: a missing game context is never eligible
            if (string.IsNullOrEmpty(gameId))
                return false;

            if (string.Equals(gameId, "Wonder_Pop", StringComparison.Ordinal))
                return false;

            switch (state)
            {
                case PurchaseState.Completed:
                case PurchaseState.Restored:
                    return true;
                default:
                    return false;
            }
        }

        public static bool RequiresUserAction(PurchaseState state) =>
            state == PurchaseState.Pending || state == PurchaseState.Deferred;

        public static bool ShouldAcknowledgePurchase(PurchaseState state) =>
            state == PurchaseState.Completed || state == PurchaseState.Restored;
    }

    public sealed class PendingSessionTracker
    {
        private readonly System.Collections.Generic.HashSet<string> _pending =
            new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);

        public int PendingCount => _pending.Count;

        public void AddPending(string orderId)
        {
            if (string.IsNullOrEmpty(orderId))
                throw new ArgumentException("orderId must not be null or empty.", nameof(orderId));
            _pending.Add(orderId);
        }

        public void RemovePending(string orderId)
        {
            if (string.IsNullOrEmpty(orderId))
                throw new ArgumentException("orderId must not be null or empty.", nameof(orderId));
            _pending.Remove(orderId);
        }

        public bool HasPending(string orderId) => _pending.Contains(orderId);

        public void Clear() => _pending.Clear();
    }
}
