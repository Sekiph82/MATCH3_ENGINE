using System;
using System.Collections.Generic;

namespace Match3.Engine.Runtime.Commerce
{
    public interface IGameplaySuspension
    {
        void Suspend();
        void Resume();
        bool IsSuspended { get; }
    }

    public sealed class NullGameplaySuspension : IGameplaySuspension
    {
        public bool IsSuspended { get; private set; }

        public void Suspend()
        {
            IsSuspended = true;
        }

        public void Resume()
        {
            IsSuspended = false;
        }
    }

    /// <summary>
    /// Eligibility-gated rewarded-ad flow that suspends gameplay while ads it requested are pending.
    /// MAIN-THREAD ONLY: not thread-safe. Call every member from the Unity main thread; SDK adapter callbacks
    /// that drive <see cref="RewardedAdLifecycle"/> or lead to <see cref="OnAdSessionEnded"/> must be marshalled
    /// to the main thread first.
    /// </summary>
    public sealed class AdShowCoordinator
    {
        private readonly IAdMediationProvider _provider;
        private readonly IGameplaySuspension _suspension;
        // Lifecycles this coordinator suspended gameplay for. Gameplay is suspended when the first one is added
        // and resumed only when the last one is released (ENG-M14-006), so one ad ending never resumes another's.
        private readonly HashSet<RewardedAdLifecycle> _suspendedFor = new HashSet<RewardedAdLifecycle>();

        private static readonly RewardedAdState[] TerminalStates =
        {
            RewardedAdState.Completed,
            RewardedAdState.Skipped,
            RewardedAdState.Failed
        };

        /// <summary>
        /// Game-facing factory (ENG-M14-002): binds the provider registered via
        /// <see cref="AdMediationProviderRegistry.Register"/> (register before creating) without ever exposing it,
        /// so Request/Show are reachable only through this coordinator's eligibility-gated methods.
        /// </summary>
        public static AdShowCoordinator CreateFromRegistry(IGameplaySuspension suspension) =>
            new AdShowCoordinator(AdMediationProviderRegistry.Current, suspension);

        // Engine-internal: a public provider-taking constructor would require game code to hold the provider.
        internal AdShowCoordinator(IAdMediationProvider provider, IGameplaySuspension suspension)
        {
            _provider = provider ?? throw new ArgumentNullException(nameof(provider));
            _suspension = suspension ?? throw new ArgumentNullException(nameof(suspension));
        }

        /// <summary>
        /// Evaluates <see cref="AdEligibilityGate.Check(string, AdConsentStatus, bool)"/> itself for the
        /// placement's game; a caller-supplied eligibility result is never trusted.
        /// </summary>
        public bool TryShowRewardedAd(RewardPlacement placement, RewardedAdLifecycle lifecycle,
            AdConsentStatus consent, bool isKidProfile)
        {
            if (lifecycle == null) throw new ArgumentNullException(nameof(lifecycle));
            if (!AdEligibilityGate.Check(placement.GameId, consent, isKidProfile).IsEligible)
                return false;
            if (!placement.IsEnabled)
                return false;

            bool added = _suspendedFor.Add(lifecycle);
            if (added && _suspendedFor.Count == 1)
                _suspension.Suspend();
            try
            {
                _provider.RequestRewardedAd(placement.PlacementId, lifecycle);
            }
            catch
            {
                // Release only what this call added: a repeat request for an ad that is already loaded or
                // on screen must not resume gameplay.
                if (added)
                    Release(lifecycle);
                throw;
            }
            return true;
        }

        /// <summary>
        /// Show step for an ad loaded via <see cref="TryShowRewardedAd(RewardPlacement, RewardedAdLifecycle, AdConsentStatus, bool)"/>.
        /// Re-runs <see cref="AdEligibilityGate.Check(string, AdConsentStatus, bool)"/> at show time with the CURRENT
        /// consent and kid flag (consent can be revoked between request and show) and re-checks the placement;
        /// only then calls <see cref="IAdMediationProvider.ShowRewardedAd"/>. When eligibility no longer holds the
        /// ad is not shown and this lifecycle's suspension is released (gameplay resumes once no other ad this
        /// coordinator suspended for is pending; ENG-M14-006) unless the ad is already Showing, in which case it stays
        /// tracked and is released by <see cref="OnAdSessionEnded"/>. Returns false (no show) unless the lifecycle is
        /// Ready and was requested through this coordinator. If the provider throws while showing, the lifecycle is
        /// marked Failed when still Showing, its suspension is released, and the exception is rethrown.
        /// </summary>
        public bool TryShowLoadedRewardedAd(RewardPlacement placement, RewardedAdLifecycle lifecycle,
            AdConsentStatus consent, bool isKidProfile)
        {
            if (lifecycle == null) throw new ArgumentNullException(nameof(lifecycle));

            if (!AdEligibilityGate.Check(placement.GameId, consent, isKidProfile).IsEligible || !placement.IsEnabled)
            {
                if (lifecycle.State != RewardedAdState.Showing)
                    Release(lifecycle);
                return false;
            }
            // Only show ads this coordinator requested (and therefore suspended gameplay for).
            if (lifecycle.State != RewardedAdState.Ready || !_suspendedFor.Contains(lifecycle))
                return false;

            try
            {
                _provider.ShowRewardedAd(placement.PlacementId, lifecycle);
            }
            catch
            {
                if (lifecycle.State == RewardedAdState.Showing)
                    lifecycle.NotifyFailed("ProviderShowException");
                Release(lifecycle);
                throw;
            }
            return true;
        }

        [Obsolete("Caller-built eligibility is not trusted: always returns false. Use TryShowRewardedAd(placement, lifecycle, consent, isKidProfile).")]
        public bool TryShowRewardedAd(RewardPlacement placement, RewardedAdLifecycle lifecycle, AdEligibilityResult eligibility) =>
            false;

        /// <summary>
        /// Call when a lifecycle this coordinator requested reaches a terminal state. Releases that lifecycle's
        /// suspension; gameplay resumes only if this coordinator suspended it and no other tracked ad is pending.
        /// </summary>
        public void OnAdSessionEnded(RewardedAdLifecycle lifecycle)
        {
            if (lifecycle == null) throw new ArgumentNullException(nameof(lifecycle));
            var state = lifecycle.State;
            bool isTerminal = false;
            foreach (var t in TerminalStates)
                if (state == t) { isTerminal = true; break; }

            if (!isTerminal)
                throw new InvalidOperationException(
                    $"OnAdSessionEnded called with non-terminal lifecycle state: {state}");

            Release(lifecycle);
        }

        // Resumes only what this coordinator suspended, and only when no other tracked ad is still pending.
        private void Release(RewardedAdLifecycle lifecycle)
        {
            if (_suspendedFor.Remove(lifecycle) && _suspendedFor.Count == 0)
                _suspension.Resume();
        }
    }
}
