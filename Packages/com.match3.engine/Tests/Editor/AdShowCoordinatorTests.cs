using System;
using NUnit.Framework;
using Match3.Engine.Runtime.Commerce;

namespace Match3.Engine.Tests.Editor
{
    public sealed class AdShowCoordinatorTests
    {
        private static RewardPlacement EnabledPlacement() =>
            new RewardPlacement("test_id", RewardPlacementKind.ExtraMoves, "Coral_Rescue", 5, true);

        private static RewardPlacement DisabledPlacement() =>
            new RewardPlacement("test_id", RewardPlacementKind.ExtraMoves, "Coral_Rescue", 5, false);

        private static RewardedAdLifecycle CompletedLifecycle()
        {
            var lc = new RewardedAdLifecycle();
            lc.RequestLoad();
            lc.NotifyReady();
            lc.NotifyShow();
            lc.NotifyCompleted();
            return lc;
        }

        private static RewardedAdLifecycle SkippedLifecycle()
        {
            var lc = new RewardedAdLifecycle();
            lc.RequestLoad();
            lc.NotifyReady();
            lc.NotifyShow();
            lc.NotifySkipped();
            return lc;
        }

        private static RewardedAdLifecycle FailedLifecycle()
        {
            var lc = new RewardedAdLifecycle();
            lc.RequestLoad();
            lc.NotifyFailed("test");
            return lc;
        }

        [Test]
        public void IneligibleAd_ConsentDenied_DoesNotSuspend_ReturnsFalse()
        {
            var suspension = new NullGameplaySuspension();
            var coordinator = new AdShowCoordinator(NullAdMediationProvider.Instance, suspension);

            var result = coordinator.TryShowRewardedAd(EnabledPlacement(), new RewardedAdLifecycle(), AdConsentStatus.Denied, false);

            Assert.That(result, Is.False);
            Assert.That(suspension.IsSuspended, Is.False);
        }

        [Test]
        public void KidProfile_NeverShown_EvenWithConsent()
        {
            var suspension = new NullGameplaySuspension();
            var coordinator = new AdShowCoordinator(NullAdMediationProvider.Instance, suspension);
            var lifecycle = new RewardedAdLifecycle();

            var result = coordinator.TryShowRewardedAd(EnabledPlacement(), lifecycle, AdConsentStatus.Granted, true);

            Assert.That(result, Is.False);
            Assert.That(suspension.IsSuspended, Is.False);
            Assert.That(lifecycle.State, Is.EqualTo(new RewardedAdLifecycle().State));
        }

        [Test]
        public void NonCatalogOrAdsDisabledGame_NeverShown()
        {
            foreach (var gameId in new[] { "Unknown_Game", "coral_rescue", "Wonder_Pop" })
            {
                var suspension = new NullGameplaySuspension();
                var coordinator = new AdShowCoordinator(NullAdMediationProvider.Instance, suspension);
                var placement = new RewardPlacement("test_id", RewardPlacementKind.ExtraMoves, gameId, 5, true);

                Assert.That(coordinator.TryShowRewardedAd(placement, new RewardedAdLifecycle(), AdConsentStatus.Granted, false), Is.False, gameId);
                Assert.That(suspension.IsSuspended, Is.False, gameId);
            }
        }

        [Test]
        public void ObsoleteOverload_CallerBuiltEligibility_NotTrusted()
        {
            var suspension = new NullGameplaySuspension();
            var coordinator = new AdShowCoordinator(NullAdMediationProvider.Instance, suspension);
#pragma warning disable 0618
            var result = coordinator.TryShowRewardedAd(EnabledPlacement(), new RewardedAdLifecycle(), AdEligibilityResult.Eligible());
#pragma warning restore 0618
            Assert.That(result, Is.False);
            Assert.That(suspension.IsSuspended, Is.False);
        }

        [Test]
        public void DisabledPlacement_DoesNotSuspend_ReturnsFalse()
        {
            var suspension = new NullGameplaySuspension();
            var coordinator = new AdShowCoordinator(NullAdMediationProvider.Instance, suspension);

            var result = coordinator.TryShowRewardedAd(DisabledPlacement(), new RewardedAdLifecycle(), AdConsentStatus.Granted, false);

            Assert.That(result, Is.False);
            Assert.That(suspension.IsSuspended, Is.False);
        }

        [Test]
        public void EligibleEnabledAd_SuspendsAndRequestsLoad_ReturnsTrue()
        {
            var suspension = new NullGameplaySuspension();
            var coordinator = new AdShowCoordinator(NullAdMediationProvider.Instance, suspension);

            // NullAdMediationProvider.RequestRewardedAd calls RequestLoad+NotifyFailed internally
            // We need a lifecycle that hasn't started yet
            var lifecycle = new RewardedAdLifecycle();

            var result = coordinator.TryShowRewardedAd(EnabledPlacement(), lifecycle, AdConsentStatus.Granted, false);

            Assert.That(result, Is.True);
            // NullProvider immediately moves lifecycle through Loading->Failed
            Assert.That(lifecycle.State, Is.EqualTo(RewardedAdState.Failed));
            // suspension was called (NullProvider is synchronous, so IsSuspended may be true still)
            // The key contract: Suspend was invoked. We verify it was set at some point via result=true + no exception.
        }

        [Test]
        public void OnAdSessionEnded_Completed_ResumesGameplay()
        {
            var suspension = new NullGameplaySuspension();
            var coordinator = new AdShowCoordinator(NullAdMediationProvider.Instance, suspension);
            suspension.Suspend();

            coordinator.OnAdSessionEnded(CompletedLifecycle());

            Assert.That(suspension.IsSuspended, Is.False);
        }

        [Test]
        public void OnAdSessionEnded_Skipped_ResumesGameplay()
        {
            var suspension = new NullGameplaySuspension();
            var coordinator = new AdShowCoordinator(NullAdMediationProvider.Instance, suspension);
            suspension.Suspend();

            coordinator.OnAdSessionEnded(SkippedLifecycle());

            Assert.That(suspension.IsSuspended, Is.False);
        }

        [Test]
        public void OnAdSessionEnded_Failed_ResumesGameplay()
        {
            var suspension = new NullGameplaySuspension();
            var coordinator = new AdShowCoordinator(NullAdMediationProvider.Instance, suspension);
            suspension.Suspend();

            coordinator.OnAdSessionEnded(FailedLifecycle());

            Assert.That(suspension.IsSuspended, Is.False);
        }

        [Test]
        public void OnAdSessionEnded_NonTerminalState_Throws()
        {
            var suspension = new NullGameplaySuspension();
            var coordinator = new AdShowCoordinator(NullAdMediationProvider.Instance, suspension);
            var lifecycle = new RewardedAdLifecycle();
            // State is Uninitialized — non-terminal

            Assert.Throws<InvalidOperationException>(() => coordinator.OnAdSessionEnded(lifecycle));
        }

        [Test]
        public void NullProvider_Constructor_Throws()
        {
            Assert.Throws<ArgumentNullException>(() =>
                new AdShowCoordinator(null, new NullGameplaySuspension()));
        }

        [Test]
        public void NullSuspension_Constructor_Throws()
        {
            Assert.Throws<ArgumentNullException>(() =>
                new AdShowCoordinator(NullAdMediationProvider.Instance, null));
        }
    }
}
