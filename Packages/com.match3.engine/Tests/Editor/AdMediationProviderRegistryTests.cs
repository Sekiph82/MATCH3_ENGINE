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
            public string ProviderId => "Fake";
            public bool IsAvailable => false;
            public void Initialize(string gameId) { }
            public void RequestRewardedAd(string placementId, RewardedAdLifecycle lifecycle) { }
            public void ShowRewardedAd(string placementId, RewardedAdLifecycle lifecycle) { }
        }
    }
}
