// ENG-M15-001 | Shared product catalog and SKU mapper
using System;
using System.Collections.Generic;
using System.Linq;

namespace Match3.Engine.Runtime.Commerce
{
    public enum StoreKind
    {
        AppleAppStore,
        GooglePlayStore,
        NoStore
    }

    public enum ProductType
    {
        Consumable,
        NonConsumable,
        Subscription
    }

    public readonly struct SkuEntry
    {
        public string SkuId { get; }
        public string GameId { get; }
        public StoreKind Store { get; }
        public ProductType Type { get; }
        public bool IsAvailable { get; }

        private SkuEntry(string skuId, string gameId, StoreKind store, ProductType type, bool isAvailable)
        {
            if (string.IsNullOrEmpty(skuId))
                throw new ArgumentException("SkuId must not be null or empty.", nameof(skuId));
            if (string.IsNullOrEmpty(gameId))
                throw new ArgumentException("GameId must not be null or empty.", nameof(gameId));

            SkuId = skuId;
            GameId = gameId;
            Store = store;
            Type = type;
            // ponytail: Wonder_Pop IAP forced off — owner policy review required before enabling
            IsAvailable = isAvailable && !string.Equals(gameId, "Wonder_Pop", StringComparison.Ordinal);
        }

        public static SkuEntry Disabled(string skuId, string gameId, StoreKind store, ProductType type)
            => new SkuEntry(skuId, gameId, store, type, false);

        public static SkuEntry Available(string skuId, string gameId, StoreKind store, ProductType type)
            => new SkuEntry(skuId, gameId, store, type, true);
    }

    public sealed class ProductCatalog
    {
        private readonly List<SkuEntry> _entries;

        public int Count => _entries.Count;

        public ProductCatalog(IEnumerable<SkuEntry> entries)
        {
            if (entries == null)
                throw new ArgumentNullException(nameof(entries));

            _entries = entries.ToList();
            var seen = new HashSet<(string, StoreKind)>();
            foreach (var e in _entries)
            {
                if (!seen.Add((e.SkuId, e.Store)))
                    throw new ArgumentException(
                        $"Duplicate SkuId+Store pair: '{e.SkuId}' / {e.Store}.", nameof(entries));
            }
        }

        public SkuEntry? Find(string skuId, StoreKind store)
        {
            foreach (var e in _entries)
                if (string.Equals(e.SkuId, skuId, StringComparison.Ordinal) && e.Store == store)
                    return e;
            return null;
        }

        public IReadOnlyList<SkuEntry> GetAvailableForGame(string gameId)
        {
            if (string.IsNullOrEmpty(gameId))
                throw new ArgumentException("GameId must not be null or empty.", nameof(gameId));

            var result = new List<SkuEntry>();
            foreach (var e in _entries)
                if (e.IsAvailable && string.Equals(e.GameId, gameId, StringComparison.Ordinal))
                    result.Add(e);
            return result.AsReadOnly();
        }
    }

    public static class SkuMapper
    {
        public static string ToAppleSku(string gameId, string baseSku)
        {
            if (string.IsNullOrEmpty(gameId))
                throw new ArgumentException("gameId must not be null or empty.", nameof(gameId));
            if (string.IsNullOrEmpty(baseSku))
                throw new ArgumentException("baseSku must not be null or empty.", nameof(baseSku));
            return gameId + "." + baseSku;
        }

        public static string ToGoogleSku(string gameId, string baseSku)
        {
            if (string.IsNullOrEmpty(gameId))
                throw new ArgumentException("gameId must not be null or empty.", nameof(gameId));
            if (string.IsNullOrEmpty(baseSku))
                throw new ArgumentException("baseSku must not be null or empty.", nameof(baseSku));
            return gameId.ToLowerInvariant() + "." + baseSku;
        }

        public static bool IsWonderPopSku(string skuId)
        {
            if (string.IsNullOrEmpty(skuId))
                throw new ArgumentException("skuId must not be null or empty.", nameof(skuId));
            return skuId.StartsWith("Wonder_Pop.", StringComparison.Ordinal);
        }
    }
}
