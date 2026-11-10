using System;
using NUnit.Framework;
using Match3.Engine.Runtime.Commerce;

namespace Match3.Engine.Tests.Editor
{
    [TestFixture]
    public class AdNetworkPluggabilityTests
    {
        [SetUp]
        public void ResetRegistry() => AdNetworkInitializerRegistry.Reset();

        [TearDown]
        public void RestoreRegistry() => AdNetworkInitializerRegistry.Reset();

        [Test]
        public void WonderPop_AdNetworkConfig_AdsDisabled()
        {
            var cfg = new AdNetworkConfig("Wonder_Pop", string.Empty, false);
            Assert.IsFalse(cfg.AdsEnabled);
        }

        [Test]
        public void WonderPop_AdNetworkConfig_PlatformAppId_MustBeEmpty()
        {
            // non-empty PlatformAppId with AdsEnabled=false must throw
            Assert.Throws<ArgumentException>(() =>
                new AdNetworkConfig("Wonder_Pop", "some-app-id", false));
        }

        [Test]
        public void ValidGame_AdNetworkConfig_AdsEnabled()
        {
            var cfg = new AdNetworkConfig("Coral_Rescue", "app-123", true);
            Assert.IsTrue(cfg.AdsEnabled);
            Assert.AreEqual("Coral_Rescue", cfg.GameId);
            Assert.AreEqual("app-123", cfg.PlatformAppId);
        }

        [Test]
        public void NullInitializer_Initialize_Throws()
        {
            var cfg = new AdNetworkConfig("Coral_Rescue", "app-123", true);
            Assert.Throws<InvalidOperationException>(() =>
                NullAdNetworkInitializer.Instance.Initialize(cfg));
        }

        [Test]
        public void Register_Null_Throws_ArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() =>
                AdNetworkInitializerRegistry.Register(null));
        }

        [Test]
        public void Reset_Restores_NullInitializer()
        {
            AdNetworkInitializerRegistry.Register(new StubInitializer());
            AdNetworkInitializerRegistry.Reset();
            Assert.IsInstanceOf<NullAdNetworkInitializer>(AdNetworkInitializerRegistry.Current);
        }

        [Test]
        public void TryInitialize_WonderPop_ReturnsFalse_SkipsInit()
        {
            var stub = new StubInitializer();
            AdNetworkInitializerRegistry.Register(stub);
            var cfg = new AdNetworkConfig("Wonder_Pop", string.Empty, false);
            bool result = AdNetworkInitializerRegistry.TryInitialize(cfg);
            Assert.IsFalse(result);
            Assert.IsFalse(stub.IsInitialized, "Initialize must not have been called for ads-disabled config.");
        }

        [Test]
        public void TryInitialize_ValidGame_ReturnsTrueAndCallsInitialize()
        {
            var stub = new StubInitializer();
            AdNetworkInitializerRegistry.Register(stub);
            var cfg = new AdNetworkConfig("Garage_Legends", "app-456", true);
            bool result = AdNetworkInitializerRegistry.TryInitialize(cfg);
            Assert.IsTrue(result);
            Assert.IsTrue(stub.IsInitialized);
        }

        [Test]
        public void TryInitialize_AlreadyInitialized_ReturnsFalse()
        {
            var stub = new StubInitializer { ForceInitialized = true };
            AdNetworkInitializerRegistry.Register(stub);
            var cfg = new AdNetworkConfig("Dino_Islands", "app-789", true);
            bool result = AdNetworkInitializerRegistry.TryInitialize(cfg);
            Assert.IsFalse(result, "Should skip when already initialized.");
        }

        // ---- test double ----

        private sealed class StubInitializer : IAdNetworkInitializer
        {
            public string NetworkName => "Stub";
            public bool ForceInitialized { get; set; }
            private bool _initialized;
            public bool IsInitialized => ForceInitialized || _initialized;

            public void Initialize(AdNetworkConfig config)
            {
                if (config.AdsEnabled == false)
                    throw new InvalidOperationException("Ads disabled.");
                _initialized = true;
            }
        }
    }
}
