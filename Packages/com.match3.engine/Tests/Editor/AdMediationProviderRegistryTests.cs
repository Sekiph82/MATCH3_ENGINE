using System;
using NUnit.Framework;
using Match3.Engine.Runtime.Commerce;

namespace Match3.Engine.Tests.Editor
{
    [TestFixture]
    public class AdMediationProviderRegistryTests
    {
        [SetUp]
        public void ResetRegistry() => AdMediationProviderRegistry.Reset();

        [TearDown]
        public void RestoreRegistry() => AdMediationProviderRegistry.Reset();

        [Test]
        public void DefaultProvider_IsNullAdMediationProvider()
        {
            Assert.IsInstanceOf<NullAdMediationProvider>(AdMediationProviderRegistry.Current);
        }

        [Test]
        public void Register_ReplacesCurrentProvider()
        {
            var fake = new FakeProvider();
            AdMediationProviderRegistry.Register(fake);
            Assert.AreSame(fake, AdMediationProviderRegistry.Current);
        }

        [Test]
        public void Current_IsNotPublic_GameCodeCannotBypassGate()
        {
            var prop = typeof(AdMediationProviderRegistry).GetProperty("Current",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
            Assert.IsNotNull(prop);
            Assert.IsFalse(prop.GetMethod.IsPublic, "AdMediationProviderRegistry.Current must be engine-internal.");
            Assert.IsTrue(prop.GetMethod.IsAssembly, "AdMediationProviderRegistry.Current must be internal.");
            Assert.IsTrue(typeof(AdMediationProviderRegistry).GetMethod("Register").IsPublic, "Register stays public.");
        }

        [Test]
        public void Register_NullThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => AdMediationProviderRegistry.Register(null));
        }

        [Test]
        public void Reset_RestoresNullProvider()
        {
            AdMediationProviderRegistry.Register(new FakeProvider());
            AdMediationProviderRegistry.Reset();
            Assert.IsInstanceOf<NullAdMediationProvider>(AdMediationProviderRegistry.Current);
        }

        [Test]
        public void NullProvider_IsAvailable_IsFalse()
        {
            Assert.IsFalse(NullAdMediationProvider.Instance.IsAvailable);
        }

        [Test]
        public void NullProvider_RequestRewardedAd_DrivesLifecycleToFailed()
        {
            var lifecycle = new RewardedAdLifecycle();
            NullAdMediationProvider.Instance.RequestRewardedAd("test", lifecycle);
            Assert.AreEqual(RewardedAdState.Failed, lifecycle.State);
        }

        [Test]
        public void NullProvider_Initialize_CalledTwiceDoesNotThrow()
        {
            Assert.DoesNotThrow(() =>
            {
                NullAdMediationProvider.Instance.Initialize("game-id");
                NullAdMediationProvider.Instance.Initialize("game-id");
            });
        }

        // ---- helpers ----

        private sealed class FakeProvider : IAdMediationProvider
        {
            public int InitializeCalls;
            public string ProviderId => "Fake";
            public bool IsAvailable => false;
            public void Initialize(string gameId) { InitializeCalls++; }
            public void RequestRewardedAd(string placementId, RewardedAdLifecycle lifecycle) { }
            public void ShowRewardedAd(string placementId, RewardedAdLifecycle lifecycle) { }
        }


        // ---- AdMediationInitializer (gate-enforced initialization) ----

        [Test]
        public void Initializer_EligibleAdultCatalogGame_InitializesOnce()
        {
            var provider = new FakeProvider();
            var result = AdMediationInitializer.TryInitialize("Coral_Rescue", AdConsentStatus.Granted, false, provider);
            Assert.IsTrue(result.IsEligible);
            Assert.AreEqual(1, provider.InitializeCalls);
        }

        [Test]
        public void Initializer_GateFails_ProviderNeverInitialized()
        {
            var cases = new[]
            {
                ("Coral_Rescue", AdConsentStatus.Granted, true, "KidProfileAdsProhibited"),
                ("Coral_Rescue", AdConsentStatus.Denied, false, "ConsentNotGranted"),
                ("Coral_Rescue", AdConsentStatus.Unknown, false, "ConsentNotGranted"),
                ("Wonder_Pop", AdConsentStatus.Granted, false, "WonderPopExcluded"),
                ("coral_rescue", AdConsentStatus.Granted, false, "UnknownGame"),
                ((string)null, AdConsentStatus.Granted, false, "GameIdMissing"),
                ("", AdConsentStatus.Granted, false, "GameIdMissing"),
            };
            foreach (var (gameId, consent, isKid, reason) in cases)
            {
                var provider = new FakeProvider();
                var result = AdMediationInitializer.TryInitialize(gameId, consent, isKid, provider);
                Assert.IsFalse(result.IsEligible, gameId);
                Assert.AreEqual(reason, result.Reason, gameId);
                Assert.AreEqual(0, provider.InitializeCalls, gameId);
            }
        }

        [Test]
        public void Initializer_NullProvider_Throws()
        {
            Assert.Throws<ArgumentNullException>(() =>
                AdMediationInitializer.TryInitialize("Coral_Rescue", AdConsentStatus.Granted, false, null));
        }
    }
}
