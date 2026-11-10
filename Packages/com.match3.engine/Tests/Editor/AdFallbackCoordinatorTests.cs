using System;
using Match3.Engine.Runtime.Commerce;
using NUnit.Framework;

namespace Match3.Engine.Tests.Editor
{
    public sealed class AdFallbackCoordinatorTests
    {
        [Test]
        public void IsAdReady_ReadyState_ReturnsTrue()
        {
            var lc = new RewardedAdLifecycle();
            lc.RequestLoad();
            lc.NotifyReady();
            Assert.IsTrue(AdAvailabilityChecker.IsAdReady(lc));
        }

        [Test]
        public void IsAdReady_LoadingState_ReturnsFalse()
        {
            var lc = new RewardedAdLifecycle();
            lc.RequestLoad();
            Assert.IsFalse(AdAvailabilityChecker.IsAdReady(lc));
        }

        [Test]
        public void CanRequestLoad_Uninitialized_ReturnsTrue()
        {
            var lc = new RewardedAdLifecycle();
            Assert.IsTrue(AdAvailabilityChecker.CanRequestLoad(lc));
        }

        [Test]
        public void CanRequestLoad_Failed_ReturnsTrue()
        {
            var lc = new RewardedAdLifecycle();
            lc.RequestLoad();
            lc.NotifyFailed("test");
            Assert.IsTrue(AdAvailabilityChecker.CanRequestLoad(lc));
        }

        [Test]
        public void CanRequestLoad_Loading_ReturnsFalse()
        {
            var lc = new RewardedAdLifecycle();
            lc.RequestLoad();
            Assert.IsFalse(AdAvailabilityChecker.CanRequestLoad(lc));
        }

        [Test]
        public void Policy_MaxAttempts_Zero_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new AdFallbackPolicy(0));
        }

        [Test]
        public void Policy_ShouldRetry_UnderCap_ReturnsTrue()
        {
            var policy = new AdFallbackPolicy(3);
            Assert.IsTrue(policy.ShouldRetry(1, AdLoadFailureReason.NetworkUnavailable));
        }

        [Test]
        public void Policy_ShouldRetry_AtCap_ReturnsFalse()
        {
            var policy = new AdFallbackPolicy(3);
            Assert.IsFalse(policy.ShouldRetry(3, AdLoadFailureReason.NetworkUnavailable));
        }

        [Test]
        public void Policy_ShouldRetry_NoFill_AlwaysFalse()
        {
            var policy = new AdFallbackPolicy(3);
            Assert.IsFalse(policy.ShouldRetry(0, AdLoadFailureReason.NoFill));
            Assert.IsFalse(policy.ShouldRetry(1, AdLoadFailureReason.NoFill));
        }

        [Test]
        public void Coordinator_NullPolicy_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => new AdFallbackCoordinator(null));
        }

        [Test]
        public void Coordinator_IsUiBlocked_AlwaysFalse()
        {
            var c = new AdFallbackCoordinator(AdFallbackPolicy.Default);
            Assert.IsFalse(c.IsUiBlocked);
        }

        [Test]
        public void LoadResult_Succeeded_NullReason()
        {
            var r = AdLoadResult.Succeeded();
            Assert.IsTrue(r.Success);
            Assert.IsNull(r.FailureReason);
        }
    }
}
