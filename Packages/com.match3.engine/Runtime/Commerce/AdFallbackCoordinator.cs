using System;

namespace Match3.Engine.Runtime.Commerce
{
    public enum AdLoadFailureReason
    {
        NoFill,
        NetworkUnavailable,
        ProviderError,
        Timeout,
        Unknown
    }

    public static class AdAvailabilityChecker
    {
        public static bool IsAdReady(RewardedAdLifecycle lifecycle) =>
            lifecycle.State == RewardedAdState.Ready;

        public static bool CanRequestLoad(RewardedAdLifecycle lifecycle) =>
            lifecycle.State == RewardedAdState.Uninitialized ||
            lifecycle.State == RewardedAdState.Failed;
    }

    public sealed class AdFallbackPolicy
    {
        public int MaxLoadAttempts { get; }

        public AdFallbackPolicy(int maxLoadAttempts)
        {
            if (maxLoadAttempts < 1)
                throw new ArgumentOutOfRangeException(nameof(maxLoadAttempts), "Must be at least 1.");
            MaxLoadAttempts = maxLoadAttempts;
        }

        public bool ShouldRetry(int attemptsMade, AdLoadFailureReason reason)
        {
            if (attemptsMade >= MaxLoadAttempts) return false;
            if (reason == AdLoadFailureReason.NoFill) return false;
            return true;
        }

        public static AdFallbackPolicy Default { get; } = new AdFallbackPolicy(3);
    }

    public readonly struct AdLoadResult
    {
        public bool Success { get; }
        public AdLoadFailureReason? FailureReason { get; }

        private AdLoadResult(bool success, AdLoadFailureReason? reason)
        {
            Success = success;
            FailureReason = reason;
        }

        public static AdLoadResult Succeeded() => new AdLoadResult(true, null);
        public static AdLoadResult Failed(AdLoadFailureReason reason) => new AdLoadResult(false, reason);
    }

    public sealed class AdFallbackCoordinator
    {
        private readonly AdFallbackPolicy _policy;

        public AdFallbackCoordinator(AdFallbackPolicy policy)
        {
            _policy = policy ?? throw new ArgumentNullException(nameof(policy));
        }

        public AdLoadResult HandleLoadFailure(int attemptsMade, AdLoadFailureReason reason) =>
            AdLoadResult.Failed(reason);

        // Invariant: no-fill/failure never freezes UI.
        public bool IsUiBlocked => false;
    }
}
