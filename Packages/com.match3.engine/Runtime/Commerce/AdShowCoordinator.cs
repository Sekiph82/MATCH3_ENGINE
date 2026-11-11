using System;

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
            _provider.RequestRewardedAd(placement.PlacementId, lifecycle);
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

            _suspension.Resume();
        }
    }
}
