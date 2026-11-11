// ENG-M16-007 EntitlementRestoreGate — stdlib only (System, System.Collections.Generic)
using System;
using System.Collections.Generic;

namespace Match3.Engine.Runtime.Commerce
{
    public readonly struct EntitlementRecord
    {
        public string GameId { get; }
        public string SkuId { get; }
        public string OrderId { get; }
        public bool IsActive { get; }

        private EntitlementRecord(string gameId, string skuId, string orderId, bool isActive)
        {
            if (string.IsNullOrEmpty(gameId))  throw new ArgumentException("GameId required.", nameof(gameId));
            if (string.IsNullOrEmpty(skuId))   throw new ArgumentException("SkuId required.", nameof(skuId));
            if (string.IsNullOrEmpty(orderId)) throw new ArgumentException("OrderId required.", nameof(orderId));
            GameId   = gameId;
            SkuId    = skuId;
            OrderId  = orderId;
            IsActive = isActive;
        }

        public static EntitlementRecord Active(string gameId, string skuId, string orderId)
            => new EntitlementRecord(gameId, skuId, orderId, true);

        public static EntitlementRecord Revoked(string gameId, string skuId, string orderId)
            => new EntitlementRecord(gameId, skuId, orderId, false);
    }

    public readonly struct RestoreEntitlementPolicy
    {
        public bool RequireServerReceipt      { get; }
        public int  OfflineGracePeriodDays    { get; }
        public string PolicyDescription       { get; }

        private RestoreEntitlementPolicy(bool requireServerReceipt, int offlineGracePeriodDays, string policyDescription)
        {
            if (string.IsNullOrEmpty(policyDescription))
                throw new ArgumentException("PolicyDescription required.", nameof(policyDescription));
            if (offlineGracePeriodDays < 0)
                throw new ArgumentOutOfRangeException(nameof(offlineGracePeriodDays), "Must be >= 0.");
            RequireServerReceipt   = requireServerReceipt;
            OfflineGracePeriodDays = offlineGracePeriodDays;
            PolicyDescription      = policyDescription;
        }

        public static RestoreEntitlementPolicy Standard(string desc)
            => new RestoreEntitlementPolicy(true, 3, desc);

        public static readonly RestoreEntitlementPolicy Default =
            Standard("Premium entitlements require server receipt validation; 3-day offline grace period");
    }

    public readonly struct RestoreEligibility
    {
        public bool   IsEligible { get; }
        public string Reason     { get; }
        private RestoreEligibility(bool eligible, string reason) { IsEligible = eligible; Reason = reason; }
        internal static RestoreEligibility Eligible()                  => new RestoreEligibility(true,  string.Empty);
        internal static RestoreEligibility Ineligible(string reason)   => new RestoreEligibility(false, reason);
    }

    public static class EntitlementRestoreGate
    {
        public static RestoreEligibility CanRestore(string gameId, RestoreEntitlementPolicy policy)
        {
            // Fail closed: a missing game context is never eligible
            if (string.IsNullOrEmpty(gameId))
                return RestoreEligibility.Ineligible("GameIdMissing");

            // Fail closed: only catalog game IDs (Ordinal) are eligible
            if (!GameProfileCatalog.IsKnownId(gameId))
                return RestoreEligibility.Ineligible("UnknownGame");

            // Guard 1 FIRST — Wonder_Pop is structurally prohibited
            if (string.Equals(gameId, "Wonder_Pop", StringComparison.Ordinal))
                return RestoreEligibility.Ineligible("WonderPopRestoreProhibited");

            // Guard 2 — server receipt must be required
            if (!policy.RequireServerReceipt)
                return RestoreEligibility.Ineligible("ServerReceiptDisabled");

            return RestoreEligibility.Eligible();
        }

        public static bool IsWithinGracePeriod(int daysSinceLastSync, RestoreEntitlementPolicy policy)
            => daysSinceLastSync <= policy.OfflineGracePeriodDays;

        public static bool RequiresCloudBackup(RestoreEntitlementPolicy policy)
            => true;
    }

    public sealed class EntitlementVault
    {
        // keyed [gameId][skuId]
        private readonly Dictionary<string, Dictionary<string, EntitlementRecord>> _store
            = new Dictionary<string, Dictionary<string, EntitlementRecord>>();

        public int Count { get; private set; }

        public void Store(EntitlementRecord record)
        {
            if (!_store.TryGetValue(record.GameId, out var inner))
            {
                inner = new Dictionary<string, EntitlementRecord>();
                _store[record.GameId] = inner;
            }
            if (!inner.ContainsKey(record.SkuId))
                Count++;
            inner[record.SkuId] = record;
        }

        public bool TryGet(string gameId, string skuId, out EntitlementRecord record)
        {
            record = default;
            return _store.TryGetValue(gameId, out var inner)
                && inner.TryGetValue(skuId, out record);
        }

        public IReadOnlyList<EntitlementRecord> GetAllForGame(string gameId)
        {
            if (!_store.TryGetValue(gameId, out var inner))
                return new List<EntitlementRecord>();
            return new List<EntitlementRecord>(inner.Values);
        }

        public void Clear()
        {
            _store.Clear();
            Count = 0;
        }
    }
}
