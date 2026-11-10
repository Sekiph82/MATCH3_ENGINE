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

        public bool TryShowRewardedAd(RewardPlacement placement, RewardedAdLifecycle lifecycle, AdEligibilityResult eligibility)
        {
            if (!eligibility.IsEligible)
                return false;
            if (!placement.IsEnabled)
                return false;

            _suspension.Suspend();
            _provider.RequestRewardedAd(placement.PlacementId, lifecycle);
            return true;
        }

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
