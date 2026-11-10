using System;

namespace Match3.Engine.Runtime.Commerce
{
    public enum RewardedAdState
    {
        Uninitialized,
        Loading,
        Ready,
        Showing,
        Completed,
        Skipped,
        Failed
    }

    public sealed class RewardedAdLifecycle
    {
        public RewardedAdState State { get; private set; } = RewardedAdState.Uninitialized;

        // Action<oldState, newState>
        public event Action<RewardedAdState, RewardedAdState> OnStateChanged;

        public void RequestLoad()
        {
            AssertState(RewardedAdState.Uninitialized, RewardedAdState.Failed);
            Transition(RewardedAdState.Loading);
        }

        public void NotifyReady()
        {
            AssertState(RewardedAdState.Loading);
            Transition(RewardedAdState.Ready);
        }

        public void NotifyShow()
        {
            AssertState(RewardedAdState.Ready);
            Transition(RewardedAdState.Showing);
        }

        public void NotifyCompleted()
        {
            AssertState(RewardedAdState.Showing);
            Transition(RewardedAdState.Completed);
        }

        public void NotifySkipped()
        {
            AssertState(RewardedAdState.Showing);
            Transition(RewardedAdState.Skipped);
        }

        public void NotifyFailed(string reason)
        {
            AssertState(RewardedAdState.Loading, RewardedAdState.Showing);
            Transition(RewardedAdState.Failed);
        }

        public void Reset()
        {
            AssertState(RewardedAdState.Completed, RewardedAdState.Skipped, RewardedAdState.Failed);
            Transition(RewardedAdState.Uninitialized);
        }

        private void Transition(RewardedAdState next)
        {
            var old = State;
            State = next;
            OnStateChanged?.Invoke(old, next);
        }

        private void AssertState(params RewardedAdState[] allowed)
        {
            foreach (var s in allowed)
                if (State == s) return;
            throw new InvalidOperationException(
                $"Cannot transition from {State}. Allowed sources: {string.Join(", ", allowed)}");
        }
    }
}
