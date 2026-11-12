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
        public void Current_IsNotPublic_GameCodeCannotBypassGate()
        {
            var prop = typeof(AdNetworkInitializerRegistry).GetProperty("Current",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
            Assert.IsNotNull(prop);
            Assert.IsFalse(prop.GetMethod.IsPublic, "AdNetworkInitializerRegistry.Current must be engine-internal.");
            Assert.IsTrue(prop.GetMethod.IsAssembly, "AdNetworkInitializerRegistry.Current must be internal.");
            Assert.IsTrue(typeof(AdNetworkInitializerRegistry).GetMethod("Register").IsPublic, "Register stays public.");
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
            // Even an ads-enabled config for Wonder_Pop never initializes.
            foreach (var cfg in new[] { new AdNetworkConfig("Wonder_Pop", string.Empty, false), new AdNetworkConfig("Wonder_Pop", "app-wp", true) })
            {
                var result = AdNetworkInitializerRegistry.TryInitialize(cfg, AdConsentStatus.Granted, isKidProfile: false);
                Assert.IsFalse(result.IsEligible);
                Assert.AreEqual("WonderPopExcluded", result.Reason);
            }
            Assert.AreEqual(0, stub.InitializeCalls, "Initialize must never be called for Wonder_Pop.");
        }

        [Test]
        public void TryInitialize_KidProfile_NeverInitializes()
        {
            var stub = new StubInitializer();
            AdNetworkInitializerRegistry.Register(stub);
            foreach (var consent in new[] { AdConsentStatus.Granted, AdConsentStatus.NotRequired, AdConsentStatus.Unknown, AdConsentStatus.Denied })
            {
                var result = AdNetworkInitializerRegistry.TryInitialize(new AdNetworkConfig("Garage_Legends", "app-456", true), consent, isKidProfile: true);
                Assert.IsFalse(result.IsEligible, consent.ToString());
                Assert.AreEqual("KidProfileAdsProhibited", result.Reason, consent.ToString());
            }
            Assert.AreEqual(0, stub.InitializeCalls);
        }

        [Test]
        public void TryInitialize_ConsentUnknownOrDenied_NeverInitializes()
        {
            var stub = new StubInitializer();
            AdNetworkInitializerRegistry.Register(stub);
            foreach (var consent in new[] { AdConsentStatus.Unknown, AdConsentStatus.Denied })
            {
                var result = AdNetworkInitializerRegistry.TryInitialize(new AdNetworkConfig("Garage_Legends", "app-456", true), consent, isKidProfile: false);
                Assert.IsFalse(result.IsEligible, consent.ToString());
                Assert.AreEqual("ConsentNotGranted", result.Reason, consent.ToString());
            }
            Assert.AreEqual(0, stub.InitializeCalls);
        }

        [Test]
        public void TryInitialize_UnknownOrCaseVariantGame_NeverInitializes()
        {
            var stub = new StubInitializer();
            AdNetworkInitializerRegistry.Register(stub);
            foreach (var gameId in new[] { "Unknown_Game", "garage_legends", "GARAGE_LEGENDS", " Garage_Legends", "Garage_Legends ", "wonder_pop" })
            {
                var result = AdNetworkInitializerRegistry.TryInitialize(new AdNetworkConfig(gameId, "app-456", true), AdConsentStatus.Granted, isKidProfile: false);
                Assert.IsFalse(result.IsEligible, gameId);
                Assert.AreEqual("UnknownGame", result.Reason, gameId);
            }
            Assert.AreEqual(0, stub.InitializeCalls);
        }

        [Test]
        public void TryInitialize_MissingGameId_DefaultConfig_NeverInitializes()
        {
            var stub = new StubInitializer();
            AdNetworkInitializerRegistry.Register(stub);
            // default(AdNetworkConfig) carries a null GameId (the constructor rejects null/""), the only way to reach it.
            var result = AdNetworkInitializerRegistry.TryInitialize(default(AdNetworkConfig), AdConsentStatus.Granted, isKidProfile: false);
            Assert.IsFalse(result.IsEligible);
            Assert.AreEqual("GameIdMissing", result.Reason);
            Assert.AreEqual(0, stub.InitializeCalls);
            Assert.Throws<ArgumentException>(() => new AdNetworkConfig(null, "app", true));
            Assert.Throws<ArgumentException>(() => new AdNetworkConfig(string.Empty, "app", true));
        }

        [Test]
        public void TryInitialize_Eligible_InitializesExactlyOnce()
        {
            var stub = new StubInitializer();
            AdNetworkInitializerRegistry.Register(stub);
            var cfg = new AdNetworkConfig("Garage_Legends", "app-456", true);

            var first = AdNetworkInitializerRegistry.TryInitialize(cfg, AdConsentStatus.Granted, isKidProfile: false);
            Assert.IsTrue(first.IsEligible);
            Assert.IsTrue(stub.IsInitialized);

            var second = AdNetworkInitializerRegistry.TryInitialize(cfg, AdConsentStatus.Granted, isKidProfile: false);
            Assert.IsFalse(second.IsEligible);
            Assert.AreEqual("AlreadyInitialized", second.Reason);
            Assert.AreEqual(1, stub.InitializeCalls);
        }

        [Test]
        public void TryInitialize_NotRequiredConsent_EligibleGame_Initializes()
        {
            var stub = new StubInitializer();
            AdNetworkInitializerRegistry.Register(stub);
            var result = AdNetworkInitializerRegistry.TryInitialize(new AdNetworkConfig("Coral_Rescue", "app-1", true), AdConsentStatus.NotRequired, isKidProfile: false);
            Assert.IsTrue(result.IsEligible);
            Assert.AreEqual(1, stub.InitializeCalls);
        }

        [Test]
        public void TryInitialize_EligibleButAdsDisabledConfig_NeverInitializes()
        {
            var stub = new StubInitializer();
            AdNetworkInitializerRegistry.Register(stub);
            var result = AdNetworkInitializerRegistry.TryInitialize(new AdNetworkConfig("Coral_Rescue", string.Empty, false), AdConsentStatus.Granted, isKidProfile: false);
            Assert.IsFalse(result.IsEligible);
            Assert.AreEqual("AdsDisabledInConfig", result.Reason);
            Assert.AreEqual(0, stub.InitializeCalls);
        }

        [Test]
        public void TryInitialize_AlreadyInitialized_ReturnsFalse()
        {
            var stub = new StubInitializer { ForceInitialized = true };
            AdNetworkInitializerRegistry.Register(stub);
            var cfg = new AdNetworkConfig("Dino_Islands", "app-789", true);
            var result = AdNetworkInitializerRegistry.TryInitialize(cfg, AdConsentStatus.Granted, isKidProfile: false);
            Assert.IsFalse(result.IsEligible, "Should skip when already initialized.");
            Assert.AreEqual("AlreadyInitialized", result.Reason);
            Assert.AreEqual(0, stub.InitializeCalls);
        }

        [Test]
        public void ObsoleteTryInitialize_ConsentUnknown_AlwaysFailsClosed()
        {
            var stub = new StubInitializer();
            AdNetworkInitializerRegistry.Register(stub);
#pragma warning disable CS0618
            foreach (var cfg in new[] { new AdNetworkConfig("Garage_Legends", "app-456", true), new AdNetworkConfig("Wonder_Pop", string.Empty, false), new AdNetworkConfig("Unknown_Game", "x", true) })
                Assert.IsFalse(AdNetworkInitializerRegistry.TryInitialize(cfg), cfg.GameId);
#pragma warning restore CS0618
            Assert.AreEqual(0, stub.InitializeCalls);
            Assert.IsFalse(stub.IsInitialized);
        }

        // ---- test double ----

        private sealed class StubInitializer : IAdNetworkInitializer
        {
            public string NetworkName => "Stub";
            public bool ForceInitialized { get; set; }
            public int InitializeCalls { get; private set; }
            private bool _initialized;
            public bool IsInitialized => ForceInitialized || _initialized;

            public void Initialize(AdNetworkConfig config)
            {
                InitializeCalls++;
                if (config.AdsEnabled == false)
                    throw new InvalidOperationException("Ads disabled.");
                _initialized = true;
            }
        }
    }
}
