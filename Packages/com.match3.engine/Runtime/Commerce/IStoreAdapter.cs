using System;
using System.Collections.Generic;

namespace Match3.Engine.Runtime.Commerce
{
    public readonly struct PurchaseRequestResult
    {
        public bool Success { get; }
        public string Reason { get; }
        public string OrderId { get; }

        private PurchaseRequestResult(bool success, string reason, string orderId)
        {
            Success = success;
            Reason = reason;
            OrderId = orderId;
        }

        public PurchaseRequestResult(string reason)
        {
            if (string.IsNullOrEmpty(reason))
                throw new ArgumentException("Reason is required.", nameof(reason));
            Success = false;
            Reason = reason;
            OrderId = null;
        }

        public static PurchaseRequestResult Succeeded(string orderId, string reason)
        {
            if (string.IsNullOrEmpty(orderId))
                throw new ArgumentException("OrderId is required.", nameof(orderId));
            if (string.IsNullOrEmpty(reason))
                throw new ArgumentException("Reason is required.", nameof(reason));
            return new PurchaseRequestResult(true, reason, orderId);
        }

        public static PurchaseRequestResult Failed(string reason)
        {
            if (string.IsNullOrEmpty(reason))
                throw new ArgumentException("Reason is required.", nameof(reason));
            return new PurchaseRequestResult(false, reason, null);
        }

        public static PurchaseRequestResult NotSupported(string reason)
        {
            if (string.IsNullOrEmpty(reason))
                throw new ArgumentException("Reason is required.", nameof(reason));
            return new PurchaseRequestResult(false, "NotSupported: " + reason, null);
        }
    }

    public interface IStoreAdapter
    {
        bool IsStoreAvailable { get; }
        PurchaseRequestResult InitiatePurchase(string gameId, string skuId);
        PurchaseRequestResult RestorePurchases(string gameId);
        IReadOnlyList<string> GetOwnedSkus(string gameId);
    }

    public sealed class NullStoreAdapter : IStoreAdapter
    {
        private static readonly StringComparer Ordinal = StringComparer.Ordinal;

        public bool IsStoreAvailable => false;

        public PurchaseRequestResult InitiatePurchase(string gameId, string skuId)
        {
            if (Ordinal.Equals(gameId, "Wonder_Pop"))
                return PurchaseRequestResult.Failed("WonderPopStoreProhibited");
            return PurchaseRequestResult.NotSupported("No store configured");
        }

        public PurchaseRequestResult RestorePurchases(string gameId)
        {
            if (Ordinal.Equals(gameId, "Wonder_Pop"))
                return PurchaseRequestResult.Failed("WonderPopStoreProhibited");
            return PurchaseRequestResult.NotSupported("No store configured");
        }

        public IReadOnlyList<string> GetOwnedSkus(string gameId) =>
            (IReadOnlyList<string>)Array.Empty<string>();
    }

    public static class StoreAdapterRegistry
    {
        private static IStoreAdapter _current = new NullStoreAdapter();

        public static IStoreAdapter Current => _current;

        public static void Register(IStoreAdapter adapter)
        {
            if (adapter == null)
                throw new ArgumentNullException(nameof(adapter));
            _current = adapter;
        }

        public static void Reset() => _current = new NullStoreAdapter();
    }
}
