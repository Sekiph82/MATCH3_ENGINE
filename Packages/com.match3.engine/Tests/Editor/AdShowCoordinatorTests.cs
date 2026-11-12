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
            var coordinator = new AdShowCoordinator(new FailingProvider(), suspension);
            var lifecycle = new RewardedAdLifecycle();

            var result = coordinator.TryShowRewardedAd(EnabledPlacement(), lifecycle, AdConsentStatus.Granted, false);

            Assert.That(result, Is.True);
            // FailingProvider immediately moves lifecycle through Loading->Failed
            Assert.That(lifecycle.State, Is.EqualTo(RewardedAdState.Failed));
            // Gameplay stays suspended until OnAdSessionEnded resumes it.
            Assert.That(suspension.IsSuspended, Is.True);
        }

        [Test]
        public void UnavailableProvider_NoSuspend_NoRequest_ReturnsFalse()
        {
            var suspension = new CountingSuspension();
            var provider = new NoOpAdapter { Available = false };
            var coordinator = new AdShowCoordinator(provider, suspension);
            var lifecycle = new RewardedAdLifecycle();

            Assert.That(coordinator.TryShowRewardedAd(EnabledPlacement(), lifecycle, AdConsentStatus.Granted, false), Is.False);
            Assert.That(suspension.SuspendCalls, Is.EqualTo(0));
            Assert.That(provider.RequestCalls, Is.EqualTo(0));
            Assert.That(coordinator.IsTracking(lifecycle), Is.False);

            // The default NullAdMediationProvider is unavailable too.
            var nullCoordinator = new AdShowCoordinator(NullAdMediationProvider.Instance, suspension);
            Assert.That(nullCoordinator.TryShowRewardedAd(EnabledPlacement(), new RewardedAdLifecycle(), AdConsentStatus.Granted, false), Is.False);
            Assert.That(suspension.SuspendCalls, Is.EqualTo(0));
        }

        [Test]
        public void AvailableNoOpAdapter_StaysUninitialized_TickReleasesAfterLoadTimeout()
        {
            var suspension = new CountingSuspension();
            var coordinator = new AdShowCoordinator(new NoOpAdapter(), suspension, loadTimeoutMs: 1000, readyTimeoutMs: 5000);
            var lifecycle = new RewardedAdLifecycle();
            Assert.That(coordinator.TryShowRewardedAd(EnabledPlacement(), lifecycle, AdConsentStatus.Granted, false), Is.True);
            Assert.That(lifecycle.State, Is.EqualTo(RewardedAdState.Uninitialized));
            Assert.That(suspension.IsSuspended, Is.True);

            coordinator.Tick(100);
            coordinator.Tick(1099);
            Assert.That(suspension.ResumeCalls, Is.EqualTo(0));
            coordinator.Tick(1100);
            Assert.That(suspension.ResumeCalls, Is.EqualTo(1));
            Assert.That(suspension.IsSuspended, Is.False);
            Assert.That(lifecycle.State, Is.EqualTo(RewardedAdState.Uninitialized));
            Assert.That(coordinator.IsTracking(lifecycle), Is.False);
        }

        [Test]
        public void Background_ReleasesUninitializedAndTerminal()
        {
            var suspension = new CountingSuspension();
            var coordinator = new AdShowCoordinator(new NoOpAdapter(), suspension);
            var uninitialized = new RewardedAdLifecycle();
            Assert.That(coordinator.TryShowRewardedAd(EnabledPlacement(), uninitialized, AdConsentStatus.Granted, false), Is.True);
            coordinator.OnApplicationPaused(true);
            Assert.That(suspension.ResumeCalls, Is.EqualTo(1));
            Assert.That(coordinator.IsTracking(uninitialized), Is.False);

            // Terminal (Completed, Skipped, Failed) ads not yet ended by the game are released on pause too.
            var suspension2 = new CountingSuspension();
            var coordinator2 = new AdShowCoordinator(new CountingProvider(), suspension2);
            var completed = new RewardedAdLifecycle();
            var skipped = new RewardedAdLifecycle();
            Assert.That(coordinator2.TryShowRewardedAd(EnabledPlacement(), completed, AdConsentStatus.Granted, false), Is.True);
            Assert.That(coordinator2.TryShowRewardedAd(EnabledPlacement(), skipped, AdConsentStatus.Granted, false), Is.True);
            Assert.That(coordinator2.TryShowLoadedRewardedAd(EnabledPlacement(), completed, AdConsentStatus.Granted, false), Is.True);
            skipped.NotifyShow();
            skipped.NotifySkipped();
            var coordinator3 = new AdShowCoordinator(new FailingProvider(), suspension2);
            var failed = new RewardedAdLifecycle();
            Assert.That(coordinator3.TryShowRewardedAd(EnabledPlacement(), failed, AdConsentStatus.Granted, false), Is.True);

            coordinator2.OnApplicationPaused(true);
            coordinator3.OnApplicationPaused(true);
            Assert.That(coordinator2.IsTracking(completed), Is.False);
            Assert.That(coordinator2.IsTracking(skipped), Is.False);
            Assert.That(coordinator3.IsTracking(failed), Is.False);
            Assert.That(suspension2.ResumeCalls, Is.EqualTo(2)); // one per coordinator that emptied
            Assert.That(completed.State, Is.EqualTo(RewardedAdState.Completed));
            Assert.That(skipped.State, Is.EqualTo(RewardedAdState.Skipped));
        }

        [Test]
        public void RepeatRequestWhileHandedToSdk_LenientAdapter_KeepsShowTracking()
        {
            var suspension = new CountingSuspension();
            var provider = new LenientDeferredShowProvider();
            var coordinator = new AdShowCoordinator(provider, suspension);
            var lifecycle = new RewardedAdLifecycle();
            Assert.That(coordinator.TryShowRewardedAd(EnabledPlacement(), lifecycle, AdConsentStatus.Granted, false), Is.True);
            Assert.That(coordinator.TryShowLoadedRewardedAd(EnabledPlacement(), lifecycle, AdConsentStatus.Granted, false), Is.True);
            Assert.That(provider.ShowCalls, Is.EqualTo(1));

            // Double-tap while the SDK owns the screen: the lenient adapter no-ops the already-loaded request.
            Assert.That(coordinator.TryShowRewardedAd(EnabledPlacement(), lifecycle, AdConsentStatus.Granted, false), Is.True);
            Assert.That(lifecycle.State, Is.EqualTo(RewardedAdState.Ready));

            coordinator.OnApplicationPaused(true);
            Assert.That(coordinator.IsTracking(lifecycle), Is.True);
            Assert.That(suspension.ResumeCalls, Is.EqualTo(0));
            Assert.That(coordinator.TryShowLoadedRewardedAd(EnabledPlacement(), lifecycle, AdConsentStatus.Granted, false), Is.False);
            Assert.That(provider.ShowCalls, Is.EqualTo(1));
        }

        [Test]
        public void RepeatRequestAfterFailure_StartsFreshTracking()
        {
            var suspension = new CountingSuspension();
            var coordinator = new AdShowCoordinator(new FailingProvider(), suspension, loadTimeoutMs: 1000, readyTimeoutMs: 5000);
            var lifecycle = new RewardedAdLifecycle();
            Assert.That(coordinator.TryShowRewardedAd(EnabledPlacement(), lifecycle, AdConsentStatus.Granted, false), Is.True);
            Assert.That(coordinator.TryShowRewardedAd(EnabledPlacement(), lifecycle, AdConsentStatus.Granted, false), Is.True);
            Assert.That(lifecycle.State, Is.EqualTo(RewardedAdState.Failed));
            Assert.That(suspension.SuspendCalls, Is.EqualTo(1));
            coordinator.OnAdSessionEnded(lifecycle);
            Assert.That(suspension.ResumeCalls, Is.EqualTo(1));
        }

        [Test]
        public void Tick_And_Pause_WithNothingTracked_AreNoOps()
        {
            var suspension = new CountingSuspension();
            var coordinator = new AdShowCoordinator(new CountingProvider(), suspension);
            coordinator.Tick(0);
            coordinator.Tick(long.MaxValue);
            coordinator.OnApplicationPaused(true);
            Assert.That(suspension.SuspendCalls, Is.EqualTo(0));
            Assert.That(suspension.ResumeCalls, Is.EqualTo(0));
            Assert.That(coordinator.IsTracking(null), Is.False);
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
            public bool ThrowOnSuspend { get; set; }
            public bool ThrowOnResume { get; set; }

            public void Suspend()
            {
                SuspendCalls++;
                if (ThrowOnSuspend) throw new InvalidOperationException("suspend failure");
                IsSuspended = true;
            }

            public void Resume()
            {
                ResumeCalls++;
                if (ThrowOnResume) throw new InvalidOperationException("resume failure");
                IsSuspended = false;
            }
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
            var coordinator = new AdShowCoordinator(new FailingProvider(), suspension);
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
            var suspension = new NullGameplaySuspension();
            var coordinator = AdShowCoordinator.CreateFromRegistry(suspension);
            // NullAdMediationProvider is unavailable: no request, no suspension.
            Assert.That(coordinator.TryShowRewardedAd(EnabledPlacement(), lifecycle, AdConsentStatus.Granted, false), Is.False);
            Assert.That(lifecycle.State, Is.EqualTo(RewardedAdState.Uninitialized));
            Assert.That(suspension.IsSuspended, Is.False);
            Assert.Throws<ArgumentNullException>(() => AdShowCoordinator.CreateFromRegistry(null));
        }

        [Test]
        public void ProviderTakingConstructor_IsNotPublic()
        {
            Assert.That(typeof(AdShowCoordinator).GetConstructors(), Is.Empty, "No public constructor may take the provider.");
            Assert.That(typeof(AdShowCoordinator).GetMethod("CreateFromRegistry").IsPublic, Is.True);
        }

        [Test]
        public void RepeatRequestForLoadedAd_ProviderThrows_DoesNotResume()
        {
            var suspension = new CountingSuspension();
            var coordinator = new AdShowCoordinator(new CountingProvider(), suspension);
            var a = new RewardedAdLifecycle();
            Assert.That(coordinator.TryShowRewardedAd(EnabledPlacement(), a, AdConsentStatus.Granted, false), Is.True);
            Assert.That(a.State, Is.EqualTo(RewardedAdState.Ready));

            // Double-tap: RequestLoad from Ready throws inside the provider.
            Assert.Throws<InvalidOperationException>(() =>
                coordinator.TryShowRewardedAd(EnabledPlacement(), a, AdConsentStatus.Granted, false));
            Assert.That(suspension.SuspendCalls, Is.EqualTo(1));
            Assert.That(suspension.ResumeCalls, Is.EqualTo(0));
            Assert.That(suspension.IsSuspended, Is.True);

            // a is still tracked: it can still be shown and its end resumes gameplay.
            Assert.That(coordinator.TryShowLoadedRewardedAd(EnabledPlacement(), a, AdConsentStatus.Granted, false), Is.True);
            coordinator.OnAdSessionEnded(a);
            Assert.That(suspension.ResumeCalls, Is.EqualTo(1));
        }

        [Test]
        public void ProviderThrowsOnShow_LifecycleFailed_SuspensionReleased()
        {
            foreach (var throwAfterNotifyShow in new[] { true, false })
            {
                var suspension = new CountingSuspension();
                var coordinator = new AdShowCoordinator(new ShowThrowingProvider(throwAfterNotifyShow), suspension);
                var lifecycle = new RewardedAdLifecycle();
                Assert.That(coordinator.TryShowRewardedAd(EnabledPlacement(), lifecycle, AdConsentStatus.Granted, false), Is.True);

                Assert.Throws<InvalidOperationException>(() =>
                    coordinator.TryShowLoadedRewardedAd(EnabledPlacement(), lifecycle, AdConsentStatus.Granted, false));
                Assert.That(suspension.ResumeCalls, Is.EqualTo(1), throwAfterNotifyShow.ToString());
                Assert.That(suspension.IsSuspended, Is.False, throwAfterNotifyShow.ToString());
                Assert.That(lifecycle.State,
                    Is.EqualTo(throwAfterNotifyShow ? RewardedAdState.Failed : RewardedAdState.Ready), throwAfterNotifyShow.ToString());

                // Released: a stale Ready lifecycle cannot be shown again without a fresh, suspending request.
                Assert.That(coordinator.TryShowLoadedRewardedAd(EnabledPlacement(), lifecycle, AdConsentStatus.Granted, false), Is.False);
                Assert.That(suspension.ResumeCalls, Is.EqualTo(1), throwAfterNotifyShow.ToString());
            }
        }

        [Test]
        public void ShowStep_UnrequestedReadyLifecycle_NotShown()
        {
            var provider = new CountingProvider();
            var coordinator = new AdShowCoordinator(provider, new NullGameplaySuspension());
            var lifecycle = new RewardedAdLifecycle();
            lifecycle.RequestLoad();
            lifecycle.NotifyReady();

            Assert.That(coordinator.TryShowLoadedRewardedAd(EnabledPlacement(), lifecycle, AdConsentStatus.Granted, false), Is.False);
            Assert.That(provider.ShowCalls, Is.EqualTo(0));
        }

        [Test]
        public void ConsentRevokedWhileShowing_DoesNotResumeUntilAdEnds()
        {
            var suspension = new CountingSuspension();
            var coordinator = new AdShowCoordinator(new CountingProvider(), suspension);
            var lifecycle = new RewardedAdLifecycle();
            Assert.That(coordinator.TryShowRewardedAd(EnabledPlacement(), lifecycle, AdConsentStatus.Granted, false), Is.True);
            lifecycle.NotifyShow(); // ad is on screen

            Assert.That(coordinator.TryShowLoadedRewardedAd(EnabledPlacement(), lifecycle, AdConsentStatus.Denied, false), Is.False);
            Assert.That(suspension.ResumeCalls, Is.EqualTo(0));
            Assert.That(suspension.IsSuspended, Is.True);

            lifecycle.NotifySkipped();
            coordinator.OnAdSessionEnded(lifecycle);
            Assert.That(suspension.ResumeCalls, Is.EqualTo(1));
            Assert.That(suspension.IsSuspended, Is.False);
        }

        // ---- ENG-M14-006 background interruption ----

        [Test]
        public void Background_WhileLoading_FailsAndRestoresGameplay()
        {
            var suspension = new CountingSuspension();
            var coordinator = new AdShowCoordinator(new LoadingOnlyProvider(), suspension);
            var lifecycle = new RewardedAdLifecycle();
            Assert.That(coordinator.TryShowRewardedAd(EnabledPlacement(), lifecycle, AdConsentStatus.Granted, false), Is.True);
            Assert.That(lifecycle.State, Is.EqualTo(RewardedAdState.Loading));

            coordinator.OnApplicationPaused(false); // unpause is a no-op
            Assert.That(suspension.ResumeCalls, Is.EqualTo(0));

            coordinator.OnApplicationPaused(true);
            Assert.That(lifecycle.State, Is.EqualTo(RewardedAdState.Failed));
            Assert.That(suspension.ResumeCalls, Is.EqualTo(1));
            Assert.That(suspension.IsSuspended, Is.False);

            // Already released: ending it later resumes nothing more.
            coordinator.OnAdSessionEnded(lifecycle);
            Assert.That(suspension.ResumeCalls, Is.EqualTo(1));
        }

        [Test]
        public void Background_WhileReadyUnshown_ReleasesAndLifecycleUnusable()
        {
            var suspension = new CountingSuspension();
            var provider = new CountingProvider();
            var coordinator = new AdShowCoordinator(provider, suspension);
            var lifecycle = new RewardedAdLifecycle();
            Assert.That(coordinator.TryShowRewardedAd(EnabledPlacement(), lifecycle, AdConsentStatus.Granted, false), Is.True);

            coordinator.OnApplicationPaused(true);
            Assert.That(lifecycle.State, Is.EqualTo(RewardedAdState.Ready));
            Assert.That(suspension.ResumeCalls, Is.EqualTo(1));
            Assert.That(coordinator.TryShowLoadedRewardedAd(EnabledPlacement(), lifecycle, AdConsentStatus.Granted, false), Is.False);
            Assert.That(provider.ShowCalls, Is.EqualTo(0));
        }

        [Test]
        public void Background_WhileShowingOrHandedToSdk_StaysSuspendedUntilAdEnds()
        {
            // Showing
            var suspension = new CountingSuspension();
            var coordinator = new AdShowCoordinator(new CountingProvider(), suspension);
            var showing = new RewardedAdLifecycle();
            Assert.That(coordinator.TryShowRewardedAd(EnabledPlacement(), showing, AdConsentStatus.Granted, false), Is.True);
            showing.NotifyShow();
            coordinator.OnApplicationPaused(true);
            Assert.That(suspension.ResumeCalls, Is.EqualTo(0));
            Assert.That(showing.State, Is.EqualTo(RewardedAdState.Showing));
            showing.NotifyCompleted();
            coordinator.OnAdSessionEnded(showing);
            Assert.That(suspension.ResumeCalls, Is.EqualTo(1));

            // Show handed to the SDK, NotifyShow not yet received (the ad activity itself pauses the app).
            var suspension2 = new CountingSuspension();
            var coordinator2 = new AdShowCoordinator(new DeferredShowProvider(), suspension2);
            var handed = new RewardedAdLifecycle();
            Assert.That(coordinator2.TryShowRewardedAd(EnabledPlacement(), handed, AdConsentStatus.Granted, false), Is.True);
            Assert.That(coordinator2.TryShowLoadedRewardedAd(EnabledPlacement(), handed, AdConsentStatus.Granted, false), Is.True);
            coordinator2.OnApplicationPaused(true);
            Assert.That(suspension2.ResumeCalls, Is.EqualTo(0));
            // Not shown twice.
            Assert.That(coordinator2.TryShowLoadedRewardedAd(EnabledPlacement(), handed, AdConsentStatus.Granted, false), Is.False);
            // Consent revoked while handed to the SDK: stays suspended too.
            Assert.That(coordinator2.TryShowLoadedRewardedAd(EnabledPlacement(), handed, AdConsentStatus.Denied, false), Is.False);
            Assert.That(suspension2.ResumeCalls, Is.EqualTo(0));
        }

        // ---- ENG-M14-006 stuck-ad timeout (fake monotonic clock) ----

        [Test]
        public void Tick_LoadingPastTimeout_FailsAndRestores()
        {
            var suspension = new CountingSuspension();
            var coordinator = new AdShowCoordinator(new LoadingOnlyProvider(), suspension, loadTimeoutMs: 1000, readyTimeoutMs: 5000);
            var lifecycle = new RewardedAdLifecycle();
            Assert.That(coordinator.TryShowRewardedAd(EnabledPlacement(), lifecycle, AdConsentStatus.Granted, false), Is.True);

            coordinator.Tick(10_000);
            coordinator.Tick(10_999);
            Assert.That(suspension.ResumeCalls, Is.EqualTo(0));
            Assert.That(lifecycle.State, Is.EqualTo(RewardedAdState.Loading));

            coordinator.Tick(11_000);
            Assert.That(lifecycle.State, Is.EqualTo(RewardedAdState.Failed));
            Assert.That(suspension.ResumeCalls, Is.EqualTo(1));
            Assert.That(suspension.IsSuspended, Is.False);
        }

        [Test]
        public void Tick_ReadyUnshownPastTimeout_Releases_StateChangeRestartsTimer()
        {
            var suspension = new CountingSuspension();
            var coordinator = new AdShowCoordinator(new LoadingOnlyProvider(), suspension, loadTimeoutMs: 1000, readyTimeoutMs: 5000);
            var lifecycle = new RewardedAdLifecycle();
            Assert.That(coordinator.TryShowRewardedAd(EnabledPlacement(), lifecycle, AdConsentStatus.Granted, false), Is.True);

            coordinator.Tick(0);
            lifecycle.NotifyReady(); // SDK callback at 900 ms
            coordinator.Tick(900);
            coordinator.Tick(1500);  // would exceed the load timeout if the timer had not restarted
            coordinator.Tick(5899);
            Assert.That(suspension.ResumeCalls, Is.EqualTo(0));

            coordinator.Tick(5900);
            Assert.That(suspension.ResumeCalls, Is.EqualTo(1));
            Assert.That(lifecycle.State, Is.EqualTo(RewardedAdState.Ready));
        }

        [Test]
        public void Tick_HandedToSdkButNeverShowing_ReleasesAfterReadyTimeoutFromShowCall()
        {
            var suspension = new CountingSuspension();
            var coordinator = new AdShowCoordinator(new DeferredShowProvider(), suspension, loadTimeoutMs: 1000, readyTimeoutMs: 5000);
            var lifecycle = new RewardedAdLifecycle();
            Assert.That(coordinator.TryShowRewardedAd(EnabledPlacement(), lifecycle, AdConsentStatus.Granted, false), Is.True);
            coordinator.Tick(0);
            coordinator.Tick(4000);
            Assert.That(coordinator.TryShowLoadedRewardedAd(EnabledPlacement(), lifecycle, AdConsentStatus.Granted, false), Is.True);

            coordinator.Tick(4500); // show-start timer starts here
            coordinator.Tick(9499);
            Assert.That(suspension.ResumeCalls, Is.EqualTo(0));
            coordinator.Tick(9500);
            Assert.That(suspension.ResumeCalls, Is.EqualTo(1));
        }

        [Test]
        public void Tick_ShowingNeverTimesOut_ClockBackwardsNeverReleases()
        {
            var suspension = new CountingSuspension();
            var coordinator = new AdShowCoordinator(new CountingProvider(), suspension, loadTimeoutMs: 1000, readyTimeoutMs: 5000);
            var showing = new RewardedAdLifecycle();
            Assert.That(coordinator.TryShowRewardedAd(EnabledPlacement(), showing, AdConsentStatus.Granted, false), Is.True);
            showing.NotifyShow();
            coordinator.Tick(0);
            coordinator.Tick(long.MaxValue / 2);
            Assert.That(suspension.ResumeCalls, Is.EqualTo(0));

            var coordinator2 = new AdShowCoordinator(new LoadingOnlyProvider(), suspension, loadTimeoutMs: 1000, readyTimeoutMs: 5000);
            var loading = new RewardedAdLifecycle();
            Assert.That(coordinator2.TryShowRewardedAd(EnabledPlacement(), loading, AdConsentStatus.Granted, false), Is.True);
            coordinator2.Tick(50_000);
            coordinator2.Tick(0);
            Assert.That(loading.State, Is.EqualTo(RewardedAdState.Loading));
        }

        [Test]
        public void Timeouts_MustBePositive()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new AdShowCoordinator(new CountingProvider(), new NullGameplaySuspension(), loadTimeoutMs: 0));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new AdShowCoordinator(new CountingProvider(), new NullGameplaySuspension(), readyTimeoutMs: -1));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                AdShowCoordinator.CreateFromRegistry(new NullGameplaySuspension(), 0, 1));
        }

        // ---- ENG-M14-006 handler-exception hardening ----

        [Test]
        public void ThrowingStateHandler_OnShowFailure_StillReleases()
        {
            var suspension = new CountingSuspension();
            var coordinator = new AdShowCoordinator(new ShowThrowingProvider(true), suspension);
            var lifecycle = new RewardedAdLifecycle();
            Assert.That(coordinator.TryShowRewardedAd(EnabledPlacement(), lifecycle, AdConsentStatus.Granted, false), Is.True);
            lifecycle.OnStateChanged += (from, to) =>
            {
                if (to == RewardedAdState.Failed) throw new ArgumentException("handler failure");
            };

            Assert.Throws<ArgumentException>(() =>
                coordinator.TryShowLoadedRewardedAd(EnabledPlacement(), lifecycle, AdConsentStatus.Granted, false));
            Assert.That(lifecycle.State, Is.EqualTo(RewardedAdState.Failed));
            Assert.That(suspension.ResumeCalls, Is.EqualTo(1));
            Assert.That(suspension.IsSuspended, Is.False);
        }

        [Test]
        public void ThrowingStateHandler_OnBackground_ReleasesEveryAdThenRethrows()
        {
            var suspension = new CountingSuspension();
            var coordinator = new AdShowCoordinator(new LoadingOnlyProvider(), suspension);
            var a = new RewardedAdLifecycle();
            var b = new RewardedAdLifecycle();
            Assert.That(coordinator.TryShowRewardedAd(EnabledPlacement(), a, AdConsentStatus.Granted, false), Is.True);
            Assert.That(coordinator.TryShowRewardedAd(EnabledPlacement(), b, AdConsentStatus.Granted, false), Is.True);
            a.OnStateChanged += (from, to) => throw new ArgumentException("handler failure a");
            b.OnStateChanged += (from, to) => throw new ArgumentException("handler failure b");

            Assert.Throws<ArgumentException>(() => coordinator.OnApplicationPaused(true));
            Assert.That(a.State, Is.EqualTo(RewardedAdState.Failed));
            Assert.That(b.State, Is.EqualTo(RewardedAdState.Failed));
            Assert.That(suspension.ResumeCalls, Is.EqualTo(1));
            Assert.That(suspension.IsSuspended, Is.False);
        }

        [Test]
        public void SuspendThrows_LifecycleNotTracked_ProviderNotCalled()
        {
            var suspension = new CountingSuspension { ThrowOnSuspend = true };
            var provider = new CountingProvider();
            var coordinator = new AdShowCoordinator(provider, suspension);
            var lifecycle = new RewardedAdLifecycle();

            Assert.Throws<InvalidOperationException>(() =>
                coordinator.TryShowRewardedAd(EnabledPlacement(), lifecycle, AdConsentStatus.Granted, false));
            Assert.That(provider.RequestCalls, Is.EqualTo(0));
            Assert.That(lifecycle.State, Is.EqualTo(RewardedAdState.Uninitialized));

            // Untracked: nothing to release, and the next request suspends again.
            coordinator.OnApplicationPaused(true);
            Assert.That(suspension.ResumeCalls, Is.EqualTo(0));
            suspension.ThrowOnSuspend = false;
            Assert.That(coordinator.TryShowRewardedAd(EnabledPlacement(), lifecycle, AdConsentStatus.Granted, false), Is.True);
            Assert.That(suspension.SuspendCalls, Is.EqualTo(2));
            Assert.That(suspension.IsSuspended, Is.True);
        }

        [Test]
        public void ResumeThrows_LifecycleLeftUntracked_NotRetried()
        {
            var suspension = new CountingSuspension();
            var coordinator = new AdShowCoordinator(new CountingProvider(), suspension);
            var lifecycle = new RewardedAdLifecycle();
            Assert.That(coordinator.TryShowRewardedAd(EnabledPlacement(), lifecycle, AdConsentStatus.Granted, false), Is.True);
            Assert.That(coordinator.TryShowLoadedRewardedAd(EnabledPlacement(), lifecycle, AdConsentStatus.Granted, false), Is.True);

            suspension.ThrowOnResume = true;
            Assert.Throws<InvalidOperationException>(() => coordinator.OnAdSessionEnded(lifecycle));
            Assert.That(suspension.ResumeCalls, Is.EqualTo(1));

            coordinator.OnAdSessionEnded(lifecycle);
            Assert.That(suspension.ResumeCalls, Is.EqualTo(1));
        }

        private sealed class FailingProvider : IAdMediationProvider
        {
            public string ProviderId => "Failing";
            public bool IsAvailable => true;
            public void Initialize(string gameId) { }

            public void RequestRewardedAd(string placementId, RewardedAdLifecycle lifecycle)
            {
                lifecycle.RequestLoad();
                lifecycle.NotifyFailed("no fill");
            }

            public void ShowRewardedAd(string placementId, RewardedAdLifecycle lifecycle) { }
        }

        // Available adapter that silently no-ops requests (e.g. SDK not ready yet).
        private sealed class NoOpAdapter : IAdMediationProvider
        {
            public bool Available { get; set; } = true;
            public int RequestCalls { get; private set; }
            public string ProviderId => "NoOp";
            public bool IsAvailable => Available;
            public void Initialize(string gameId) { }
            public void RequestRewardedAd(string placementId, RewardedAdLifecycle lifecycle) => RequestCalls++;
            public void ShowRewardedAd(string placementId, RewardedAdLifecycle lifecycle) { }
        }

        // Loads synchronously, no-ops a request for an already loaded ad, and defers NotifyShow.
        private sealed class LenientDeferredShowProvider : IAdMediationProvider
        {
            public int ShowCalls { get; private set; }
            public string ProviderId => "LenientDeferredShow";
            public bool IsAvailable => true;
            public void Initialize(string gameId) { }

            public void RequestRewardedAd(string placementId, RewardedAdLifecycle lifecycle)
            {
                if (lifecycle.State != RewardedAdState.Uninitialized && lifecycle.State != RewardedAdState.Failed)
                    return;
                lifecycle.RequestLoad();
                lifecycle.NotifyReady();
            }

            public void ShowRewardedAd(string placementId, RewardedAdLifecycle lifecycle) => ShowCalls++;
        }

        private sealed class LoadingOnlyProvider : IAdMediationProvider
        {
            public string ProviderId => "LoadingOnly";
            public bool IsAvailable => true;
            public void Initialize(string gameId) { }
            public void RequestRewardedAd(string placementId, RewardedAdLifecycle lifecycle) => lifecycle.RequestLoad();
            public void ShowRewardedAd(string placementId, RewardedAdLifecycle lifecycle) { }
        }

        private sealed class DeferredShowProvider : IAdMediationProvider
        {
            public string ProviderId => "DeferredShow";
            public bool IsAvailable => true;
            public void Initialize(string gameId) { }

            public void RequestRewardedAd(string placementId, RewardedAdLifecycle lifecycle)
            {
                lifecycle.RequestLoad();
                lifecycle.NotifyReady();
            }

            public void ShowRewardedAd(string placementId, RewardedAdLifecycle lifecycle) { } // NotifyShow arrives later
        }

        private sealed class ShowThrowingProvider : IAdMediationProvider
        {
            private readonly bool _throwAfterNotifyShow;
            public ShowThrowingProvider(bool throwAfterNotifyShow) { _throwAfterNotifyShow = throwAfterNotifyShow; }
            public string ProviderId => "ShowThrowing";
            public bool IsAvailable => true;
            public void Initialize(string gameId) { }

            public void RequestRewardedAd(string placementId, RewardedAdLifecycle lifecycle)
            {
                lifecycle.RequestLoad();
                lifecycle.NotifyReady();
            }

            public void ShowRewardedAd(string placementId, RewardedAdLifecycle lifecycle)
            {
                if (_throwAfterNotifyShow)
                    lifecycle.NotifyShow();
                throw new InvalidOperationException("sdk show failure");
            }
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
