using System;

namespace Match3.Engine.Runtime.Commerce
{
    public enum RestorePhase
    {
        Idle,
        Restoring,
        Completed,
        Failed,
        NothingToRestore
    }

    public readonly struct RestorePolicy
    {
        public bool RestoreAllowed { get; }
        public bool RequireUserInitiated => true;
        public string PolicyDescription { get; }

        private RestorePolicy(bool restoreAllowed, string description)
        {
            if (string.IsNullOrEmpty(description))
                throw new ArgumentException("PolicyDescription must not be null or empty.", nameof(description));
            RestoreAllowed = restoreAllowed;
            PolicyDescription = description;
        }

        public static RestorePolicy Enabled(string description) =>
            new RestorePolicy(true, description);

        public static RestorePolicy Disabled(string description) =>
            new RestorePolicy(false, description);

        public static RestorePolicy Default =>
            Enabled("User-initiated restore required; never auto-restore without user action");
    }

    public readonly struct RestoreResult
    {
        public int RestoredCount { get; }
        public bool HasErrors { get; }
        public string ErrorReason { get; }

        private RestoreResult(int restoredCount, bool hasErrors, string errorReason)
        {
            RestoredCount = restoredCount;
            HasErrors = hasErrors;
            ErrorReason = errorReason;
        }

        public static RestoreResult Success(int count) =>
            new RestoreResult(count, false, null);

        public static RestoreResult Failure(string reason)
        {
            if (string.IsNullOrEmpty(reason))
                throw new ArgumentException("ErrorReason must not be null or empty.", nameof(reason));
            return new RestoreResult(0, true, reason);
        }

        public static RestoreResult Empty() =>
            new RestoreResult(0, false, null);
    }

    public static class RestorePurchasesGate
    {
        private const string WonderPopId = "Wonder_Pop";

        public static AdEligibilityResult CanInitiateRestore(string gameId, RestorePolicy policy)
        {
            // Fail closed: a missing game context is never eligible
            if (string.IsNullOrEmpty(gameId))
                return AdEligibilityResult.Ineligible("GameIdMissing");

            if (StringComparer.Ordinal.Equals(gameId, WonderPopId))
                return AdEligibilityResult.Ineligible("WonderPopRestoreProhibited");

            if (!policy.RestoreAllowed)
                return AdEligibilityResult.Ineligible("RestoreNotAllowed");

            return AdEligibilityResult.Eligible();
        }

        public static bool IsTerminalPhase(RestorePhase phase) =>
            phase == RestorePhase.Completed ||
            phase == RestorePhase.Failed ||
            phase == RestorePhase.NothingToRestore;

        public static bool ShouldShowRestoreButton(string gameId, RestorePolicy policy)
        {
            // Fail closed: a missing game context is never eligible
            if (string.IsNullOrEmpty(gameId))
                return false;

            if (StringComparer.Ordinal.Equals(gameId, WonderPopId))
                return false;

            if (!policy.RestoreAllowed)
                return false;

            return true;
        }

        public static bool RequiresUserAction(RestorePhase phase) =>
            phase == RestorePhase.Idle;
    }

    public sealed class RestoreSessionState
    {
        public RestorePhase CurrentPhase { get; private set; } = RestorePhase.Idle;
        public RestoreResult? LastResult { get; private set; }

        public void TransitionTo(RestorePhase phase)
        {
            CurrentPhase = phase;
        }

        public void Reset()
        {
            CurrentPhase = RestorePhase.Idle;
            LastResult = null;
        }

        public void SetResult(RestoreResult result)
        {
            if (!RestorePurchasesGate.IsTerminalPhase(CurrentPhase))
                throw new InvalidOperationException(
                    $"Cannot set result when phase is {CurrentPhase}; must be a terminal phase.");
            LastResult = result;
        }
    }
}
