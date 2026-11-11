// ENG-M15-003: Tests for LocalizedPriceDisplay contracts
using System;
using NUnit.Framework;
using Match3.Engine.Runtime.Commerce;

namespace Match3.Engine.Tests.Editor.Commerce
{
    [TestFixture]
    public class LocalizedPriceDisplayTests
    {
        // ---- LocalizedPrice.FromStore ----

        [Test]
        public void FromStore_ValidArgs_ReturnsAvailablePrice()
        {
            var p = LocalizedPrice.FromStore("$1.99", "USD", "Coral_Rescue", "sku_001");
            Assert.IsTrue(p.IsAvailable);
            Assert.AreEqual("$1.99", p.FormattedPrice);
            Assert.AreEqual("USD", p.CurrencyCode);
            Assert.AreEqual("Coral_Rescue", p.GameId);
            Assert.AreEqual("sku_001", p.SkuId);
        }

        [Test]
        public void FromStore_NullFormattedPrice_Throws()
        {
            Assert.Throws<ArgumentException>(() =>
                LocalizedPrice.FromStore(null, "USD", "Coral_Rescue", "sku_001"));
        }

        [Test]
        public void FromStore_EmptyFormattedPrice_Throws()
        {
            Assert.Throws<ArgumentException>(() =>
                LocalizedPrice.FromStore(string.Empty, "USD", "Coral_Rescue", "sku_001"));
        }

        [Test]
        public void FromStore_NullCurrencyCode_Throws()
        {
            Assert.Throws<ArgumentException>(() =>
                LocalizedPrice.FromStore("$1.99", null, "Coral_Rescue", "sku_001"));
        }

        [Test]
        public void FromStore_NullGameId_Throws()
        {
            Assert.Throws<ArgumentException>(() =>
                LocalizedPrice.FromStore("$1.99", "USD", null, "sku_001"));
        }

        [Test]
        public void FromStore_NullSkuId_Throws()
        {
            Assert.Throws<ArgumentException>(() =>
                LocalizedPrice.FromStore("$1.99", "USD", "Coral_Rescue", null));
        }

        // ---- LocalizedPrice.Unavailable ----

        [Test]
        public void Unavailable_IsNotAvailable()
        {
            var p = LocalizedPrice.Unavailable("Coral_Rescue", "sku_001");
            Assert.IsFalse(p.IsAvailable);
            Assert.AreEqual(string.Empty, p.FormattedPrice);
            Assert.AreEqual(string.Empty, p.CurrencyCode);
        }

        [Test]
        public void Unavailable_PreservesGameIdAndSkuId()
        {
            var p = LocalizedPrice.Unavailable("Garage_Legends", "sku_xyz");
            Assert.AreEqual("Garage_Legends", p.GameId);
            Assert.AreEqual("sku_xyz", p.SkuId);
        }

        // ---- PriceDisplayPolicy ----

        [Test]
        public void StoreOwned_AllowFabricatedPricesIsFalse()
        {
            var pol = PriceDisplayPolicy.StoreOwned("No fabrication");
            Assert.IsFalse(pol.AllowFabricatedPrices);
        }

        [Test]
        public void StoreOwned_NullDescription_Throws()
        {
            Assert.Throws<ArgumentException>(() =>
                PriceDisplayPolicy.StoreOwned(null));
        }

        [Test]
        public void DefaultPolicy_AllowFabricatedPricesIsFalse()
        {
            Assert.IsFalse(PriceDisplayPolicy.Default.AllowFabricatedPrices);
            Assert.IsFalse(string.IsNullOrEmpty(PriceDisplayPolicy.Default.PolicyDescription));
        }

        // ---- LocalizedPriceDisplayGate.CanDisplayPrice ----

        [Test]
        public void CanDisplayPrice_WonderPop_AlwaysIneligible_EvenWhenPriceIsAvailable()
        {
            var price = LocalizedPrice.FromStore("$1.99", "USD", "Wonder_Pop", "sku_001");
            var result = LocalizedPriceDisplayGate.CanDisplayPrice(
                "Wonder_Pop", price, PriceDisplayPolicy.Default);
            Assert.IsFalse(result.IsEligible);
            Assert.AreEqual("WonderPopPriceDisplayProhibited", result.Reason);
        }

        [Test]
        public void CanDisplayPrice_WonderPop_CaseSensitive_OtherCaseIsUnknownGame()
        {
            // Ordinal catalog lookup: a case variant of Wonder_Pop is not a catalog ID, so it fails closed with UnknownGame (never eligible, never the Wonder_Pop reason).
            var price = LocalizedPrice.FromStore("$1.99", "USD", "wonder_pop", "sku_001");
            var result = LocalizedPriceDisplayGate.CanDisplayPrice(
                "wonder_pop", price, PriceDisplayPolicy.Default);
            Assert.IsFalse(result.IsEligible);
            Assert.AreEqual("UnknownGame", result.Reason);
        }

        [Test]
        public void CanDisplayPrice_UnavailablePrice_Ineligible()
        {
            var price = LocalizedPrice.Unavailable("Coral_Rescue", "sku_001");
            var result = LocalizedPriceDisplayGate.CanDisplayPrice(
                "Coral_Rescue", price, PriceDisplayPolicy.Default);
            Assert.IsFalse(result.IsEligible);
            Assert.AreEqual("PriceUnavailableFromStore", result.Reason);
        }

        [Test]
        public void CanDisplayPrice_ValidGame_ValidPrice_IsEligible()
        {
            var price = LocalizedPrice.FromStore("£1.49", "GBP", "Coral_Rescue", "sku_uk");
            var result = LocalizedPriceDisplayGate.CanDisplayPrice(
                "Coral_Rescue", price, PriceDisplayPolicy.Default);
            Assert.IsTrue(result.IsEligible);
        }

        // ---- LocalizedPriceDisplayGate.IsStoreOwnedDisplay ----

        [Test]
        public void IsStoreOwnedDisplay_DefaultPolicy_ReturnsTrue()
        {
            Assert.IsTrue(LocalizedPriceDisplayGate.IsStoreOwnedDisplay(PriceDisplayPolicy.Default));
        }

        // ---- LocalizedPriceDisplayGate.AssertNoFabricatedPrice ----

        [Test]
        public void AssertNoFabricatedPrice_StoreOwnedPolicy_DoesNotThrow()
        {
            Assert.DoesNotThrow(() =>
                LocalizedPriceDisplayGate.AssertNoFabricatedPrice(PriceDisplayPolicy.Default));
        }

        // ---- PriceCache ----

        [Test]
        public void PriceCache_StoreAndRetrieve()
        {
            var cache = new PriceCache();
            var price = LocalizedPrice.FromStore("€2.99", "EUR", "Coral_Rescue", "sku_eu");
            cache.Store(price);
            Assert.AreEqual(1, cache.Count);
            Assert.IsTrue(cache.TryGet("sku_eu", out var retrieved));
            Assert.AreEqual("€2.99", retrieved.FormattedPrice);
        }

        [Test]
        public void PriceCache_MissingKey_ReturnsFalse()
        {
            var cache = new PriceCache();
            Assert.IsFalse(cache.TryGet("nonexistent_sku", out _));
        }

        [Test]
        public void PriceCache_Store_OverwritesExistingKey()
        {
            var cache = new PriceCache();
            cache.Store(LocalizedPrice.FromStore("$1.99", "USD", "Coral_Rescue", "sku_001"));
            cache.Store(LocalizedPrice.FromStore("$2.99", "USD", "Coral_Rescue", "sku_001"));
            Assert.AreEqual(1, cache.Count);
            cache.TryGet("sku_001", out var p);
            Assert.AreEqual("$2.99", p.FormattedPrice);
        }

        [Test]
        public void PriceCache_Clear_ResetsCount()
        {
            var cache = new PriceCache();
            cache.Store(LocalizedPrice.FromStore("$1.99", "USD", "Coral_Rescue", "sku_001"));
            cache.Store(LocalizedPrice.FromStore("$2.99", "USD", "Coral_Rescue", "sku_002"));
            cache.Clear();
            Assert.AreEqual(0, cache.Count);
            Assert.IsFalse(cache.TryGet("sku_001", out _));
        }

        [Test]
        public void PriceCache_MultipleEntries_AllRetrievable()
        {
            var cache = new PriceCache();
            cache.Store(LocalizedPrice.FromStore("$1.99", "USD", "Coral_Rescue", "sku_a"));
            cache.Store(LocalizedPrice.FromStore("£1.49", "GBP", "Coral_Rescue", "sku_b"));
            cache.Store(LocalizedPrice.FromStore("€1.79", "EUR", "Coral_Rescue", "sku_c"));
            Assert.AreEqual(3, cache.Count);
            Assert.IsTrue(cache.TryGet("sku_a", out _));
            Assert.IsTrue(cache.TryGet("sku_b", out _));
            Assert.IsTrue(cache.TryGet("sku_c", out _));
        }


        [Test]
        public void DefaultStructPolicy_BehavesAsDefault()
        {
            // PriceDisplayPolicy is store-owned only (AllowFabricatedPrices is always false); default(struct) is normalized.
            var price = LocalizedPrice.FromStore("$0.99", "USD", "Coral_Rescue", "sku");
            var expected = LocalizedPriceDisplayGate.CanDisplayPrice("Coral_Rescue", price, PriceDisplayPolicy.Default);
            var result = LocalizedPriceDisplayGate.CanDisplayPrice("Coral_Rescue", price, default(PriceDisplayPolicy));
            Assert.AreEqual(expected.IsEligible, result.IsEligible);
            Assert.AreEqual(expected.Reason, result.Reason);
            Assert.IsTrue(LocalizedPriceDisplayGate.IsStoreOwnedDisplay(default(PriceDisplayPolicy)));
        }

        [Test]
        public void GameIdMismatch_Blocked()
        {
            var price = LocalizedPrice.FromStore("$0.99", "USD", "Wonder_Pop", "sku");
            var result = LocalizedPriceDisplayGate.CanDisplayPrice("Coral_Rescue", price, PriceDisplayPolicy.Default);
            Assert.IsFalse(result.IsEligible);
            Assert.AreEqual("GameIdMismatch", result.Reason);
        }

        [Test]
        public void WonderPopParam_RecordDiffers_GameIdMismatch()
        {
            var price = LocalizedPrice.FromStore("$0.99", "USD", "Coral_Rescue", "sku");
            var result = LocalizedPriceDisplayGate.CanDisplayPrice("Wonder_Pop", price, PriceDisplayPolicy.Default);
            Assert.IsFalse(result.IsEligible);
            Assert.AreEqual("GameIdMismatch", result.Reason);
        }


        [Test]
        public void NullOrEmptyGameId_DefaultRecord_GameIdMissing()
        {
            foreach (var gameId in new[] { null, string.Empty })
            {
                var result = LocalizedPriceDisplayGate.CanDisplayPrice(gameId, default(LocalizedPrice), default(PriceDisplayPolicy));
                Assert.IsFalse(result.IsEligible);
                Assert.AreEqual("GameIdMissing", result.Reason);
            }
        }

        [Test]
        public void UnknownOrCaseVariantGameId_DefaultRecord_UnknownGame()
        {
            foreach (var gameId in new[] { "Unknown_Game", "coral_rescue", "CORAL_RESCUE", " Coral_Rescue", "Coral_Rescue ", "wonder_pop", "WONDER_POP" })
            {
                var result = LocalizedPriceDisplayGate.CanDisplayPrice(gameId, default(LocalizedPrice), default(PriceDisplayPolicy));
                Assert.IsFalse(result.IsEligible);
                Assert.AreEqual("UnknownGame", result.Reason);
            }
        }
    }
}
