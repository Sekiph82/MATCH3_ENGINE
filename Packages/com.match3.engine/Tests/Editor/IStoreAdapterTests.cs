using System;
using NUnit.Framework;
using Match3.Engine.Runtime.Commerce;

namespace Match3.Engine.Tests.Editor
{
    public sealed class IStoreAdapterTests
    {
        [SetUp]
        public void SetUp() => StoreAdapterRegistry.Reset();

        [Test]
        public void PurchaseRequestResult_NullReason_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() => new PurchaseRequestResult(null));
        }

        [Test]
        public void PurchaseRequestResult_EmptyReason_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() => new PurchaseRequestResult(string.Empty));
        }

        [Test]
        public void Succeeded_SetsSuccessTrue_AndOrderId()
        {
            var r = PurchaseRequestResult.Succeeded("order-001", "PurchaseOk");
            Assert.That(r.Success, Is.True);
            Assert.That(r.OrderId, Is.EqualTo("order-001"));
            Assert.That(r.Reason, Is.EqualTo("PurchaseOk"));
        }

        [Test]
        public void Succeeded_NullOrderId_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() => PurchaseRequestResult.Succeeded(null, "ok"));
        }

        [Test]
        public void Succeeded_EmptyOrderId_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() => PurchaseRequestResult.Succeeded(string.Empty, "ok"));
        }

        [Test]
        public void Succeeded_NullReason_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() => PurchaseRequestResult.Succeeded("order-x", null));
        }

        [Test]
        public void Failed_SetsSuccessFalse_AndNullOrderId()
        {
            var r = PurchaseRequestResult.Failed("store error");
            Assert.That(r.Success, Is.False);
            Assert.That(r.OrderId, Is.Null);
            Assert.That(r.Reason, Is.EqualTo("store error"));
        }

        [Test]
        public void NotSupported_PrefixesReason()
        {
            var r = PurchaseRequestResult.NotSupported("No store configured");
            Assert.That(r.Success, Is.False);
            Assert.That(r.OrderId, Is.Null);
            Assert.That(r.Reason, Does.StartWith("NotSupported: "));
        }

        [Test]
        public void NullStoreAdapter_IsStoreAvailable_IsFalse()
        {
            var adapter = new NullStoreAdapter();
            Assert.That(adapter.IsStoreAvailable, Is.False);
        }

        [Test]
        public void NullStoreAdapter_InitiatePurchase_WonderPop_ReturnsFailedWonderPopProhibited()
        {
            var adapter = new NullStoreAdapter();
            var r = adapter.InitiatePurchase("Wonder_Pop", "gems_100");
            Assert.That(r.Success, Is.False);
            Assert.That(r.Reason, Is.EqualTo("WonderPopStoreProhibited"));
        }

        [Test]
        public void NullStoreAdapter_InitiatePurchase_NonWonderPop_ReturnsNotSupported()
        {
            var adapter = new NullStoreAdapter();
            var r = adapter.InitiatePurchase("Coral_Rescue", "gems_100");
            Assert.That(r.Success, Is.False);
            Assert.That(r.Reason, Does.StartWith("NotSupported: "));
        }

        [Test]
        public void NullStoreAdapter_RestorePurchases_WonderPop_ReturnsFailedWonderPopProhibited()
        {
            var adapter = new NullStoreAdapter();
            var r = adapter.RestorePurchases("Wonder_Pop");
            Assert.That(r.Success, Is.False);
            Assert.That(r.Reason, Is.EqualTo("WonderPopStoreProhibited"));
        }

        [Test]
        public void NullStoreAdapter_RestorePurchases_NonWonderPop_ReturnsNotSupported()
        {
            var adapter = new NullStoreAdapter();
            var r = adapter.RestorePurchases("Garage_Legends");
            Assert.That(r.Success, Is.False);
            Assert.That(r.Reason, Does.StartWith("NotSupported: "));
        }

        [Test]
        public void NullStoreAdapter_GetOwnedSkus_ReturnsEmptyList()
        {
            var adapter = new NullStoreAdapter();
            var skus = adapter.GetOwnedSkus("Dino_Islands");
            Assert.That(skus, Is.Not.Null);
            Assert.That(skus.Count, Is.EqualTo(0));
        }

        [Test]
        public void Registry_Current_DefaultsToNullStoreAdapter_IsStoreAvailableFalse()
        {
            Assert.That(StoreAdapterRegistry.Current.IsStoreAvailable, Is.False);
        }

        [Test]
        public void Registry_Register_Null_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => StoreAdapterRegistry.Register(null));
        }

        [Test]
        public void Registry_Register_SetsCurrentAdapter()
        {
            var fake = new FakeStoreAdapter();
            StoreAdapterRegistry.Register(fake);
            Assert.That(StoreAdapterRegistry.Current, Is.SameAs(fake));
        }

        [Test]
        public void Registry_Reset_RestoresNullAdapter()
        {
            StoreAdapterRegistry.Register(new FakeStoreAdapter());
            StoreAdapterRegistry.Reset();
            Assert.That(StoreAdapterRegistry.Current.IsStoreAvailable, Is.False);
        }

        [Test]
        public void WonderPop_OrdinalGuard_CaseSensitive_NonWonderPop_IsNotBlocked()
        {
            var adapter = new NullStoreAdapter();
            // "wonder_pop" (lowercase) must NOT trigger Wonder_Pop Ordinal block
            var r = adapter.InitiatePurchase("wonder_pop", "sku");
            Assert.That(r.Reason, Does.StartWith("NotSupported: "),
                "Ordinal comparison must be case-sensitive; lowercase must not trigger the Wonder_Pop block.");
        }

        private sealed class FakeStoreAdapter : IStoreAdapter
        {
            public bool IsStoreAvailable => true;
            public PurchaseRequestResult InitiatePurchase(string gameId, string skuId) =>
                PurchaseRequestResult.Succeeded("fake-order", "FakeOk");
            public PurchaseRequestResult RestorePurchases(string gameId) =>
                PurchaseRequestResult.Succeeded("fake-order", "FakeRestored");
            public System.Collections.Generic.IReadOnlyList<string> GetOwnedSkus(string gameId) =>
                new[] { "sku1" };
        }
    }
}
