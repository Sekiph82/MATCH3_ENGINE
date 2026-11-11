// ENG-M15-001 | Product catalog and SKU mapper — EditMode tests
using System;
using System.Collections.Generic;
using Match3.Engine.Runtime.Commerce;
using NUnit.Framework;

namespace Match3.Engine.Tests.Editor
{
    public sealed class ProductCatalogTests
    {
        [Test]
        public void SkuEntry_NullSkuId_Throws()
        {
            Assert.Throws<ArgumentException>(() =>
                SkuEntry.Available(null, "Coral_Rescue", StoreKind.AppleAppStore, ProductType.Consumable));
        }

        [Test]
        public void SkuEntry_EmptySkuId_Throws()
        {
            Assert.Throws<ArgumentException>(() =>
                SkuEntry.Available("", "Coral_Rescue", StoreKind.AppleAppStore, ProductType.Consumable));
        }

        [Test]
        public void SkuEntry_NullGameId_Throws()
        {
            Assert.Throws<ArgumentException>(() =>
                SkuEntry.Available("coins_500", null, StoreKind.AppleAppStore, ProductType.Consumable));
        }

        [Test]
        public void SkuEntry_EmptyGameId_Throws()
        {
            Assert.Throws<ArgumentException>(() =>
                SkuEntry.Available("coins_500", "", StoreKind.AppleAppStore, ProductType.Consumable));
        }

        [Test]
        public void SkuEntry_WonderPop_AlwaysUnavailable_EvenWithAvailableFactory()
        {
            var entry = SkuEntry.Available("coins_500", "Wonder_Pop", StoreKind.AppleAppStore, ProductType.Consumable);
            Assert.That(entry.IsAvailable, Is.False, "Wonder Pop IAP must always be unavailable");
        }

        [Test]
        public void SkuEntry_Available_NonWonderPop_IsAvailable()
        {
            var entry = SkuEntry.Available("coins_500", "Coral_Rescue", StoreKind.AppleAppStore, ProductType.Consumable);
            Assert.That(entry.IsAvailable, Is.True);
        }

        [Test]
        public void SkuEntry_Disabled_NonWonderPop_IsNotAvailable()
        {
            var entry = SkuEntry.Disabled("coins_500", "Coral_Rescue", StoreKind.AppleAppStore, ProductType.Consumable);
            Assert.That(entry.IsAvailable, Is.False);
        }

        [Test]
        public void ProductCatalog_NullEntries_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => new ProductCatalog(null));
        }

        [Test]
        public void ProductCatalog_DuplicateSku_Throws()
        {
            var entries = new[]
            {
                SkuEntry.Available("coins_500", "Coral_Rescue", StoreKind.AppleAppStore, ProductType.Consumable),
                SkuEntry.Available("coins_500", "Garage_Legends", StoreKind.AppleAppStore, ProductType.Consumable)
            };
            Assert.Throws<ArgumentException>(() => new ProductCatalog(entries));
        }

        [Test]
        public void ProductCatalog_SameSkuDifferentStore_IsAllowed()
        {
            var entries = new[]
            {
                SkuEntry.Available("coins_500", "Coral_Rescue", StoreKind.AppleAppStore, ProductType.Consumable),
                SkuEntry.Available("coins_500", "Coral_Rescue", StoreKind.GooglePlayStore, ProductType.Consumable)
            };
            Assert.DoesNotThrow(() => new ProductCatalog(entries));
        }

        [Test]
        public void ProductCatalog_Find_ReturnsNull_WhenNotFound()
        {
            var catalog = new ProductCatalog(new[]
            {
                SkuEntry.Available("coins_500", "Coral_Rescue", StoreKind.AppleAppStore, ProductType.Consumable)
            });
            Assert.That(catalog.Find("coins_999", StoreKind.AppleAppStore), Is.Null);
        }

        [Test]
        public void ProductCatalog_Find_ReturnsEntry_WhenFound()
        {
            var catalog = new ProductCatalog(new[]
            {
                SkuEntry.Available("coins_500", "Coral_Rescue", StoreKind.AppleAppStore, ProductType.Consumable)
            });
            var result = catalog.Find("coins_500", StoreKind.AppleAppStore);
            Assert.That(result.HasValue, Is.True);
            Assert.That(result.Value.GameId, Is.EqualTo("Coral_Rescue"));
        }

        [Test]
        public void ProductCatalog_GetAvailableForGame_FiltersCorrectly()
        {
            var catalog = new ProductCatalog(new[]
            {
                SkuEntry.Available("coins_500", "Coral_Rescue", StoreKind.AppleAppStore, ProductType.Consumable),
                SkuEntry.Disabled("coins_1000", "Coral_Rescue", StoreKind.AppleAppStore, ProductType.Consumable),
                SkuEntry.Available("gems_100", "Garage_Legends", StoreKind.AppleAppStore, ProductType.Consumable)
            });
            var available = catalog.GetAvailableForGame("Coral_Rescue");
            Assert.That(available.Count, Is.EqualTo(1));
            Assert.That(available[0].SkuId, Is.EqualTo("coins_500"));
        }

        [Test]
        public void ProductCatalog_GetAvailableForGame_NullGameId_Throws()
        {
            var catalog = new ProductCatalog(Array.Empty<SkuEntry>());
            Assert.Throws<ArgumentException>(() => catalog.GetAvailableForGame(null));
        }

        [Test]
        public void ProductCatalog_Count_ReflectsTotalEntries()
        {
            var catalog = new ProductCatalog(new[]
            {
                SkuEntry.Available("coins_500", "Coral_Rescue", StoreKind.AppleAppStore, ProductType.Consumable),
                SkuEntry.Available("coins_500", "Coral_Rescue", StoreKind.GooglePlayStore, ProductType.Consumable)
            });
            Assert.That(catalog.Count, Is.EqualTo(2));
        }

        [Test]
        public void SkuMapper_ToAppleSku_Format()
        {
            var sku = SkuMapper.ToAppleSku("Coral_Rescue", "coins_500");
            Assert.That(sku, Is.EqualTo("Coral_Rescue.coins_500"));
        }

        [Test]
        public void SkuMapper_ToGoogleSku_IsLowercase()
        {
            var sku = SkuMapper.ToGoogleSku("Coral_Rescue", "coins_500");
            Assert.That(sku, Is.EqualTo("coral_rescue.coins_500"));
        }

        [Test]
        public void SkuMapper_ToAppleSku_NullGameId_Throws()
        {
            Assert.Throws<ArgumentException>(() => SkuMapper.ToAppleSku(null, "coins_500"));
        }

        [Test]
        public void SkuMapper_ToGoogleSku_NullBaseSku_Throws()
        {
            Assert.Throws<ArgumentException>(() => SkuMapper.ToGoogleSku("Coral_Rescue", null));
        }

        [Test]
        public void SkuMapper_IsWonderPopSku_True()
        {
            Assert.That(SkuMapper.IsWonderPopSku("Wonder_Pop.coins_500"), Is.True);
        }

        [Test]
        public void SkuMapper_IsWonderPopSku_False()
        {
            Assert.That(SkuMapper.IsWonderPopSku("Coral_Rescue.coins_500"), Is.False);
        }

        [Test]
        public void SkuMapper_IsWonderPopSku_NullSkuId_Throws()
        {
            Assert.Throws<ArgumentException>(() => SkuMapper.IsWonderPopSku(null));
        }

        [Test]
        public void SkuMapper_IsWonderPopSku_PrefixOnly_False()
        {
            // "Wonder_Pop" without trailing dot is not a Wonder Pop SKU
            Assert.That(SkuMapper.IsWonderPopSku("Wonder_Pop"), Is.False);
        }
    }
}
