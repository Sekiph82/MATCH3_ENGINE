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

    public sealed class AdShowCoordinator
    {
        private readonly IAdMediationProvider _provider;
        private readonly IGameplaySuspension _suspension;
        // Lifecycles this coordinator suspended gameplay for; the ineligible-at-show path resumes only these.
        private readonly HashSet<RewardedAdLifecycle> _suspendedFor = new HashSet<RewardedAdLifecycle>();

        private static readonly RewardedAdState[] TerminalStates =
        {
            RewardedAdState.Completed,
            RewardedAdState.Skipped,
            RewardedAdState.Failed
        };

        public AdShowCoordinator(IAdMediationProvider provider, IGameplaySuspension suspension)
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
            if (!AdEligibilityGate.Check(placement.GameId, consent, isKidProfile).IsEligible)
                return false;
            if (!placement.IsEnabled)
                return false;

            _suspension.Suspend();
            _suspendedFor.Add(lifecycle);
            _provider.RequestRewardedAd(placement.PlacementId, lifecycle);
            return true;
        }

        /// <summary>
        /// Show step for an ad loaded via <see cref="TryShowRewardedAd(RewardPlacement, RewardedAdLifecycle, AdConsentStatus, bool)"/>.
        /// Re-runs <see cref="AdEligibilityGate.Check(string, AdConsentStatus, bool)"/> at show time with the CURRENT
        /// consent and kid flag (consent can be revoked between request and show) and re-checks the placement;
        /// only then calls <see cref="IAdMediationProvider.ShowRewardedAd"/>. When eligibility no longer holds the
        /// ad is not shown and gameplay is resumed only if this coordinator suspended it for this lifecycle
        /// (ENG-M14-006). Returns false (no show) unless the lifecycle is Ready.
        /// </summary>
        public bool TryShowLoadedRewardedAd(RewardPlacement placement, RewardedAdLifecycle lifecycle,
            AdConsentStatus consent, bool isKidProfile)
        {
            if (lifecycle == null) throw new ArgumentNullException(nameof(lifecycle));

            if (!AdEligibilityGate.Check(placement.GameId, consent, isKidProfile).IsEligible || !placement.IsEnabled)
            {
                if (_suspendedFor.Remove(lifecycle))
                    _suspension.Resume();
                return false;
            }
            if (lifecycle.State != RewardedAdState.Ready)
                return false;

            _provider.ShowRewardedAd(placement.PlacementId, lifecycle);
            return true;
        }

        [Obsolete("Caller-built eligibility is not trusted: always returns false. Use TryShowRewardedAd(placement, lifecycle, consent, isKidProfile).")]
        public bool TryShowRewardedAd(RewardPlacement placement, RewardedAdLifecycle lifecycle, AdEligibilityResult eligibility) =>
            false;

        public void OnAdSessionEnded(RewardedAdLifecycle lifecycle)
        {
            var state = lifecycle.State;
            bool isTerminal = false;
            foreach (var t in TerminalStates)
                if (state == t) { isTerminal = true; break; }

            if (!isTerminal)
                throw new InvalidOperationException(
                    $"OnAdSessionEnded called with non-terminal lifecycle state: {state}");

            _suspendedFor.Remove(lifecycle);
            _suspension.Resume();
        }
    }
}
