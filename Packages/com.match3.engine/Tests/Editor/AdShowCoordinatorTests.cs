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
            // Gameplay stays suspended until OnAdSessionEnded resumes it.
            Assert.That(suspension.IsSuspended, Is.True);
        }

        [Test]
        public void ShowStep_ConsentRevokedBetweenRequestAndShow_NotShown_GameplayResumed()
        {
            var provider = new CountingProvider();
            var suspension = new NullGameplaySuspension();
            var coordinator = new AdShowCoordinator(provider, suspension);
            var lifecycle = new RewardedAdLifecycle();

            Assert.That(coordinator.TryShowRewardedAd(EnabledPlacement(), lifecycle, AdConsentStatus.Granted, false), Is.True);
            Assert.That(lifecycle.State, Is.EqualTo(RewardedAdState.Ready));
            Assert.That(suspension.IsSuspended, Is.True);

            foreach (var revoked in new[] { AdConsentStatus.Denied, AdConsentStatus.Unknown })
            {
                Assert.That(coordinator.TryShowLoadedRewardedAd(EnabledPlacement(), lifecycle, revoked, false), Is.False, revoked.ToString());
                Assert.That(provider.ShowCalls, Is.EqualTo(0), revoked.ToString());
                Assert.That(suspension.IsSuspended, Is.False, revoked.ToString());
            }
            Assert.That(lifecycle.State, Is.EqualTo(RewardedAdState.Ready));
        }

        [Test]
        public void ShowStep_KidProfileAtShowTime_NotShown()
        {
            var provider = new CountingProvider();
            var coordinator = new AdShowCoordinator(provider, new NullGameplaySuspension());
            var lifecycle = new RewardedAdLifecycle();
            Assert.That(coordinator.TryShowRewardedAd(EnabledPlacement(), lifecycle, AdConsentStatus.Granted, false), Is.True);

            Assert.That(coordinator.TryShowLoadedRewardedAd(EnabledPlacement(), lifecycle, AdConsentStatus.Granted, true), Is.False);
            Assert.That(provider.ShowCalls, Is.EqualTo(0));
        }

        [Test]
        public void ShowStep_IneligibleGameOrDisabledPlacement_NotShown()
        {
            foreach (var placement in new[]
            {
                new RewardPlacement("test_id", RewardPlacementKind.ExtraMoves, "Wonder_Pop", 5, true),
                new RewardPlacement("test_id", RewardPlacementKind.ExtraMoves, "coral_rescue", 5, true),
                new RewardPlacement("test_id", RewardPlacementKind.ExtraMoves, "Unknown_Game", 5, true),
                DisabledPlacement(),
                default(RewardPlacement)
            })
            {
                var provider = new CountingProvider();
                var coordinator = new AdShowCoordinator(provider, new NullGameplaySuspension());
                var lifecycle = new RewardedAdLifecycle();
                lifecycle.RequestLoad();
                lifecycle.NotifyReady();
                Assert.That(coordinator.TryShowLoadedRewardedAd(placement, lifecycle, AdConsentStatus.Granted, false), Is.False, placement.GameId);
                Assert.That(provider.ShowCalls, Is.EqualTo(0), placement.GameId);
            }
        }

        [Test]
        public void ShowStep_StillEligibleAndReady_ShowsExactlyOnce()
        {
            var provider = new CountingProvider();
            var suspension = new NullGameplaySuspension();
            var coordinator = new AdShowCoordinator(provider, suspension);
            var lifecycle = new RewardedAdLifecycle();
            Assert.That(coordinator.TryShowRewardedAd(EnabledPlacement(), lifecycle, AdConsentStatus.Granted, false), Is.True);

            Assert.That(coordinator.TryShowLoadedRewardedAd(EnabledPlacement(), lifecycle, AdConsentStatus.Granted, false), Is.True);
            Assert.That(provider.ShowCalls, Is.EqualTo(1));
            Assert.That(lifecycle.State, Is.EqualTo(RewardedAdState.Completed));
            // Not Ready any more: a second show is refused.
            Assert.That(coordinator.TryShowLoadedRewardedAd(EnabledPlacement(), lifecycle, AdConsentStatus.Granted, false), Is.False);
            Assert.That(provider.ShowCalls, Is.EqualTo(1));
        }

        [Test]
        public void ShowStep_NotLoaded_NotShown()
        {
            var provider = new CountingProvider();
            var coordinator = new AdShowCoordinator(provider, new NullGameplaySuspension());
            Assert.That(coordinator.TryShowLoadedRewardedAd(EnabledPlacement(), new RewardedAdLifecycle(), AdConsentStatus.Granted, false), Is.False);
            Assert.That(provider.ShowCalls, Is.EqualTo(0));
            Assert.Throws<ArgumentNullException>(() => coordinator.TryShowLoadedRewardedAd(EnabledPlacement(), null, AdConsentStatus.Granted, false));
        }

        [Test]
        public void ShowStep_IneligibleWithoutPriorSuspend_DoesNotResume()
        {
            var suspension = new CountingSuspension();
            var coordinator = new AdShowCoordinator(new CountingProvider(), suspension);
            var lifecycle = new RewardedAdLifecycle();
            lifecycle.RequestLoad();
            lifecycle.NotifyReady();

            Assert.That(coordinator.TryShowLoadedRewardedAd(EnabledPlacement(), lifecycle, AdConsentStatus.Denied, false), Is.False);
            Assert.That(suspension.SuspendCalls, Is.EqualTo(0));
            Assert.That(suspension.ResumeCalls, Is.EqualTo(0));
        }

        [Test]
        public void ShowStep_IneligibleAfterOwnSuspend_ResumesExactlyOnce()
        {
            var suspension = new CountingSuspension();
            var coordinator = new AdShowCoordinator(new CountingProvider(), suspension);
            var lifecycle = new RewardedAdLifecycle();
            Assert.That(coordinator.TryShowRewardedAd(EnabledPlacement(), lifecycle, AdConsentStatus.Granted, false), Is.True);
            Assert.That(suspension.SuspendCalls, Is.EqualTo(1));

            Assert.That(coordinator.TryShowLoadedRewardedAd(EnabledPlacement(), lifecycle, AdConsentStatus.Denied, false), Is.False);
            Assert.That(coordinator.TryShowLoadedRewardedAd(EnabledPlacement(), lifecycle, AdConsentStatus.Denied, false), Is.False);
            Assert.That(suspension.ResumeCalls, Is.EqualTo(1));

            // A different (never-suspended) lifecycle on the same coordinator must not resume.
            var other = new RewardedAdLifecycle();
            other.RequestLoad();
            other.NotifyReady();
            Assert.That(coordinator.TryShowLoadedRewardedAd(EnabledPlacement(), other, AdConsentStatus.Denied, false), Is.False);
            Assert.That(suspension.ResumeCalls, Is.EqualTo(1));
        }

        private sealed class CountingSuspension : IGameplaySuspension
        {
            public int SuspendCalls { get; private set; }
            public int ResumeCalls { get; private set; }
            public bool IsSuspended { get; private set; }
            public void Suspend() { SuspendCalls++; IsSuspended = true; }
            public void Resume() { ResumeCalls++; IsSuspended = false; }
        }

        private sealed class CountingProvider : IAdMediationProvider
        {
            public int RequestCalls { get; private set; }
            public int ShowCalls { get; private set; }
            public string ProviderId => "Counting";
            public bool IsAvailable => true;
            public void Initialize(string gameId) { }

            public void RequestRewardedAd(string placementId, RewardedAdLifecycle lifecycle)
            {
                RequestCalls++;
                lifecycle.RequestLoad();
                lifecycle.NotifyReady();
            }

            public void ShowRewardedAd(string placementId, RewardedAdLifecycle lifecycle)
            {
                ShowCalls++;
                lifecycle.NotifyShow();
                lifecycle.NotifyCompleted();
            }
        }

        [Test]
        public void OnAdSessionEnded_Completed_ResumesGameplay()
        {
            var suspension = new NullGameplaySuspension();
            var coordinator = new AdShowCoordinator(new CountingProvider(), suspension);
            var lifecycle = new RewardedAdLifecycle();
            Assert.That(coordinator.TryShowRewardedAd(EnabledPlacement(), lifecycle, AdConsentStatus.Granted, false), Is.True);
            Assert.That(coordinator.TryShowLoadedRewardedAd(EnabledPlacement(), lifecycle, AdConsentStatus.Granted, false), Is.True);
            Assert.That(lifecycle.State, Is.EqualTo(RewardedAdState.Completed));
            Assert.That(suspension.IsSuspended, Is.True);

            coordinator.OnAdSessionEnded(lifecycle);

            Assert.That(suspension.IsSuspended, Is.False);
        }

        [Test]
        public void OnAdSessionEnded_Skipped_ResumesGameplay()
        {
            var suspension = new NullGameplaySuspension();
            var coordinator = new AdShowCoordinator(new CountingProvider(), suspension);
            var lifecycle = new RewardedAdLifecycle();
            Assert.That(coordinator.TryShowRewardedAd(EnabledPlacement(), lifecycle, AdConsentStatus.Granted, false), Is.True);
            lifecycle.NotifyShow();
            lifecycle.NotifySkipped();
            Assert.That(suspension.IsSuspended, Is.True);

            coordinator.OnAdSessionEnded(lifecycle);

            Assert.That(suspension.IsSuspended, Is.False);
        }

        [Test]
        public void OnAdSessionEnded_Failed_ResumesGameplay()
        {
            var suspension = new NullGameplaySuspension();
            var coordinator = new AdShowCoordinator(NullAdMediationProvider.Instance, suspension);
            var lifecycle = new RewardedAdLifecycle();
            Assert.That(coordinator.TryShowRewardedAd(EnabledPlacement(), lifecycle, AdConsentStatus.Granted, false), Is.True);
            Assert.That(lifecycle.State, Is.EqualTo(RewardedAdState.Failed));
            Assert.That(suspension.IsSuspended, Is.True);

            coordinator.OnAdSessionEnded(lifecycle);

            Assert.That(suspension.IsSuspended, Is.False);
        }

        [Test]
        public void OnAdSessionEnded_UntrackedLifecycle_DoesNotResume()
        {
            var suspension = new CountingSuspension();
            var coordinator = new AdShowCoordinator(NullAdMediationProvider.Instance, suspension);
            suspension.Suspend(); // suspended by another owner, not this coordinator

            coordinator.OnAdSessionEnded(FailedLifecycle());

            Assert.That(suspension.ResumeCalls, Is.EqualTo(0));
            Assert.That(suspension.IsSuspended, Is.True);
        }

        [Test]
        public void TwoPendingAds_GameplayResumesOnlyWhenLastIsReleased()
        {
            var suspension = new CountingSuspension();
            var coordinator = new AdShowCoordinator(new CountingProvider(), suspension);
            var a = new RewardedAdLifecycle();
            var b = new RewardedAdLifecycle();
            Assert.That(coordinator.TryShowRewardedAd(EnabledPlacement(), a, AdConsentStatus.Granted, false), Is.True);
            Assert.That(coordinator.TryShowRewardedAd(EnabledPlacement(), b, AdConsentStatus.Granted, false), Is.True);
            Assert.That(suspension.SuspendCalls, Is.EqualTo(1));

            // a ends while b is still pending: gameplay stays suspended.
            Assert.That(coordinator.TryShowLoadedRewardedAd(EnabledPlacement(), a, AdConsentStatus.Granted, false), Is.True);
            coordinator.OnAdSessionEnded(a);
            Assert.That(suspension.ResumeCalls, Is.EqualTo(0));
            Assert.That(suspension.IsSuspended, Is.True);

            // b becomes ineligible at show: last tracked ad released, gameplay resumes exactly once.
            Assert.That(coordinator.TryShowLoadedRewardedAd(EnabledPlacement(), b, AdConsentStatus.Denied, false), Is.False);
            Assert.That(suspension.ResumeCalls, Is.EqualTo(1));
            Assert.That(suspension.IsSuspended, Is.False);

            // Stale entries are gone: re-ending a or re-denying b resumes nothing more.
            coordinator.OnAdSessionEnded(a);
            Assert.That(coordinator.TryShowLoadedRewardedAd(EnabledPlacement(), b, AdConsentStatus.Denied, false), Is.False);
            Assert.That(suspension.ResumeCalls, Is.EqualTo(1));

            // A fresh ad suspends again.
            Assert.That(coordinator.TryShowRewardedAd(EnabledPlacement(), new RewardedAdLifecycle(), AdConsentStatus.Granted, false), Is.True);
            Assert.That(suspension.SuspendCalls, Is.EqualTo(2));
        }

        [Test]
        public void ProviderThrowsOnRequest_SuspensionReleased()
        {
            var suspension = new CountingSuspension();
            var coordinator = new AdShowCoordinator(new ThrowingProvider(), suspension);
            var lifecycle = new RewardedAdLifecycle();

            Assert.Throws<InvalidOperationException>(() =>
                coordinator.TryShowRewardedAd(EnabledPlacement(), lifecycle, AdConsentStatus.Granted, false));
            Assert.That(suspension.SuspendCalls, Is.EqualTo(1));
            Assert.That(suspension.ResumeCalls, Is.EqualTo(1));
            // No leaked entry: a later ineligible show for the same lifecycle resumes nothing.
            coordinator.TryShowLoadedRewardedAd(EnabledPlacement(), lifecycle, AdConsentStatus.Denied, false);
            Assert.That(suspension.ResumeCalls, Is.EqualTo(1));
        }

        [Test]
        public void CreateFromRegistry_UsesRegisteredProvider_GateStillApplies()
        {
            var provider = new CountingProvider();
            AdMediationProviderRegistry.Register(provider);
            try
            {
                var suspension = new NullGameplaySuspension();
                var coordinator = AdShowCoordinator.CreateFromRegistry(suspension);
                var lifecycle = new RewardedAdLifecycle();

                Assert.That(coordinator.TryShowRewardedAd(EnabledPlacement(), lifecycle, AdConsentStatus.Denied, false), Is.False);
                Assert.That(coordinator.TryShowRewardedAd(EnabledPlacement(), lifecycle, AdConsentStatus.Granted, true), Is.False);
                Assert.That(provider.RequestCalls, Is.EqualTo(0));

                Assert.That(coordinator.TryShowRewardedAd(EnabledPlacement(), lifecycle, AdConsentStatus.Granted, false), Is.True);
                Assert.That(provider.RequestCalls, Is.EqualTo(1));
                Assert.That(coordinator.TryShowLoadedRewardedAd(EnabledPlacement(), lifecycle, AdConsentStatus.Granted, true), Is.False);
                Assert.That(provider.ShowCalls, Is.EqualTo(0));
                Assert.That(suspension.IsSuspended, Is.False);
            }
            finally
            {
                AdMediationProviderRegistry.Reset();
            }
        }

        [Test]
        public void CreateFromRegistry_DefaultRegistry_UsesNullProvider()
        {
            AdMediationProviderRegistry.Reset();
            var lifecycle = new RewardedAdLifecycle();
            var coordinator = AdShowCoordinator.CreateFromRegistry(new NullGameplaySuspension());
            Assert.That(coordinator.TryShowRewardedAd(EnabledPlacement(), lifecycle, AdConsentStatus.Granted, false), Is.True);
            Assert.That(lifecycle.State, Is.EqualTo(RewardedAdState.Failed));
            Assert.Throws<ArgumentNullException>(() => AdShowCoordinator.CreateFromRegistry(null));
        }

        [Test]
        public void ProviderTakingConstructor_IsNotPublic()
        {
            Assert.That(typeof(AdShowCoordinator).GetConstructors(), Is.Empty, "No public constructor may take the provider.");
            Assert.That(typeof(AdShowCoordinator).GetMethod("CreateFromRegistry").IsPublic, Is.True);
        }

        private sealed class ThrowingProvider : IAdMediationProvider
        {
            public string ProviderId => "Throwing";
            public bool IsAvailable => true;
            public void Initialize(string gameId) { }
            public void RequestRewardedAd(string placementId, RewardedAdLifecycle lifecycle) =>
                throw new InvalidOperationException("sdk failure");
            public void ShowRewardedAd(string placementId, RewardedAdLifecycle lifecycle) { }
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
