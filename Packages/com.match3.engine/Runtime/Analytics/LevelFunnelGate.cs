// LevelFunnelGate.cs — ENG-M18-004
// stdlib only: System, System.Collections.Generic
using System;
using System.Collections.Generic;

namespace Match3.Engine.Runtime.Analytics
{
    public enum FailCause
    {
        OutOfMoves,
        TimeExpired,
        ObjectiveFailed,
        PlayerQuit
    }

    public readonly struct LevelAttempt
    {
        public string GameId { get; }
        public int LevelId { get; }
        public int AttemptNumber { get; }
        public FailCause Cause { get; }
        public bool WasCompleted { get; }

        // ponytail: IsBoosterDependent placeholder — returns false; wire to booster data when struct is extended
        public bool IsBoosterDependent => false;

        private LevelAttempt(string gameId, int levelId, int attemptNumber, FailCause cause, bool wasCompleted)
        {
            if (string.IsNullOrEmpty(gameId))
                throw new ArgumentException("GameId must not be null or empty.", nameof(gameId));
            if (levelId < 0)
                throw new ArgumentOutOfRangeException(nameof(levelId), "LevelId must be >= 0.");
            if (attemptNumber < 1)
                throw new ArgumentOutOfRangeException(nameof(attemptNumber), "AttemptNumber must be >= 1.");

            GameId = gameId;
            LevelId = levelId;
            AttemptNumber = attemptNumber;
            Cause = cause;
            WasCompleted = wasCompleted;
        }

        public static LevelAttempt CreateFail(string gameId, int levelId, int attemptNumber, FailCause cause)
            => new LevelAttempt(gameId, levelId, attemptNumber, cause, false);

        public static LevelAttempt CreateComplete(string gameId, int levelId, int attemptNumber)
            => new LevelAttempt(gameId, levelId, attemptNumber, FailCause.ObjectiveFailed, true);
    }

    public readonly struct FunnelPolicy
    {
        public int MaxAttemptsToTrack { get; }
        public bool IncludeBoosterMetrics { get; }
        public string PolicyDescription { get; }

        public FunnelPolicy(int maxAttemptsToTrack, bool includeBoosterMetrics, string policyDescription)
        {
            if (string.IsNullOrEmpty(policyDescription))
                throw new ArgumentException("PolicyDescription must not be null or empty.", nameof(policyDescription));
            if (maxAttemptsToTrack <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxAttemptsToTrack), "MaxAttemptsToTrack must be > 0.");

            MaxAttemptsToTrack = maxAttemptsToTrack;
            IncludeBoosterMetrics = includeBoosterMetrics;
            PolicyDescription = policyDescription;
        }

        public static FunnelPolicy Safe(string desc)
            => new FunnelPolicy(50, false, desc);

        public static readonly FunnelPolicy Default =
            Safe("Level funnel tracking; booster metrics disabled until authorized");
    }

    public static class LevelFunnelGate
    {
        private static FunnelPolicy Normalize(FunnelPolicy policy) =>
            policy.PolicyDescription == null ? FunnelPolicy.Default : policy;

        public static (bool eligible, string reason) CanTrackAttempt(
            string gameId, LevelAttempt attempt, FunnelPolicy policy)
        {
            policy = Normalize(policy);

            if (!string.Equals(gameId, attempt.GameId, StringComparison.Ordinal))
                return (false, "GameIdMismatch");

            // Guard 1 (after GameIdMismatch): Wonder_Pop ordinal check
            if (string.Equals(gameId, "Wonder_Pop", StringComparison.Ordinal))
                return (false, "WonderPopFunnelProhibited");

            // Guard 2: attempt exceeds tracking limit
            if (attempt.AttemptNumber > policy.MaxAttemptsToTrack)
                return (false, "ExceedsTrackingLimit");

            return (true, string.Empty);
        }

        public static bool IsCompletionAttempt(LevelAttempt attempt) => attempt.WasCompleted;

        public static bool RequiresBoosterMetrics(FunnelPolicy policy) => Normalize(policy).IncludeBoosterMetrics;
    }

    public sealed class FunnelAccumulator
    {
        private readonly Dictionary<string, int> _attempts = new Dictionary<string, int>();
        private readonly Dictionary<string, int> _completions = new Dictionary<string, int>();

        public void RecordAttempt(int levelId, bool completed)
        {
            if (levelId < 0)
                throw new ArgumentOutOfRangeException(nameof(levelId), "levelId must be >= 0.");
            string key = levelId.ToString();
            _attempts.TryGetValue(key, out int a);
            _attempts[key] = a + 1;
            if (completed)
            {
                _completions.TryGetValue(key, out int c);
                _completions[key] = c + 1;
            }
        }

        public int GetAttemptCount(int levelId)
        {
            _attempts.TryGetValue(levelId.ToString(), out int v);
            return v;
        }

        public int GetCompletionCount(int levelId)
        {
            _completions.TryGetValue(levelId.ToString(), out int v);
            return v;
        }

        public double GetCompletionRate(int levelId)
        {
            int attempts = GetAttemptCount(levelId);
            if (attempts == 0) return 0.0;
            return (double)GetCompletionCount(levelId) / attempts;
        }

        public void Clear()
        {
            _attempts.Clear();
            _completions.Clear();
        }
    }
}
