using System;
using System.Collections.Generic;

namespace Match3.Engine.Runtime.LiveOps
{
    public enum ChallengeEventType
    {
        ScheduledGoal,
        ThemeCollection,
        EventReward,
        SeasonalContent,
        EventUiState,
        LastKnownGoodFallback
    }

    public readonly struct ChallengeEventRecord
    {
        public string GameId { get; }
        public ChallengeEventType EventType { get; }
        public bool IsWithinWindow { get; }
        public bool HasFallback { get; }
        public bool AffectsPermanentProgress { get; }

        private ChallengeEventRecord(string gameId, ChallengeEventType eventType, bool isWithinWindow, bool hasFallback, bool affectsPermanentProgress)
        {
            GameId = gameId;
            EventType = eventType;
            IsWithinWindow = isWithinWindow;
            HasFallback = hasFallback;
            AffectsPermanentProgress = affectsPermanentProgress;
        }

        public static ChallengeEventRecord Create(string gameId, ChallengeEventType eventType, bool isWithinWindow, bool hasFallback, bool affectsPermanentProgress)
        {
            if (string.IsNullOrEmpty(gameId))
                throw new ArgumentException("gameId must not be null or empty.", nameof(gameId));
            return new ChallengeEventRecord(gameId, eventType, isWithinWindow, hasFallback, affectsPermanentProgress);
        }

        public bool RequiresFallback => !IsWithinWindow && !HasFallback;

        public bool IsExpired => !IsWithinWindow;
    }

    // Permanent-progress protection and never activating an expired pack are unconditional core invariants,
    // so the policy carries no relaxing flags; default(ChallengeEventPolicy) therefore cannot fail open.
    public readonly struct ChallengeEventPolicy
    {
        public string PolicyDescription { get; }

        public ChallengeEventPolicy(string policyDescription)
        {
            if (string.IsNullOrEmpty(policyDescription))
                throw new ArgumentException("PolicyDescription must not be null or empty.", nameof(policyDescription));
            PolicyDescription = policyDescription;
        }

        public static ChallengeEventPolicy Safe(string desc) => new ChallengeEventPolicy(desc);

        public static readonly ChallengeEventPolicy Default = Safe(
            "Default challenge event policy - require fallback on expiry, block permanent progress modification");
    }

    public static class LimitedTimeChallengeGate
    {
        public static bool CanActivateEvent(string gameId, ChallengeEventRecord record, ChallengeEventPolicy policy, out string reason)
        {
            if (!string.Equals(gameId, record.GameId, StringComparison.Ordinal))
            {
                reason = "GameIdMismatch";
                return false;
            }

            // Guard 1: Wonder Pop events may never modify permanent progress (not policy-overridable)
            if (string.Equals(gameId, "Wonder_Pop", StringComparison.Ordinal) && record.AffectsPermanentProgress)
            {
                reason = "WonderPopPermanentProgressModificationProhibited";
                return false;
            }

            // Guard 2 (unconditional): event content must not alter permanent progress for any game
            if (record.AffectsPermanentProgress)
            {
                reason = "PermanentProgressModificationBlocked";
                return false;
            }

            // Guard 3 (unconditional): an expired pack is never activated. Contract: returns false with
            // reason "UseLastKnownGood" when a fallback exists (caller must load last-known-good content),
            // or "ExpiredEventRequiresFallback" when none exists.
            if (record.IsExpired)
            {
                reason = record.HasFallback ? "UseLastKnownGood" : "ExpiredEventRequiresFallback";
                return false;
            }

            reason = string.Empty;
            return true;
        }

        public static bool RequiresFallback(ChallengeEventRecord record) => record.RequiresFallback;

        public static bool IsExpired(ChallengeEventRecord record) => record.IsExpired;
    }

    public sealed class ChallengeEventLog
    {
        private readonly List<string> _entries = new List<string>();

        public void Record(string gameId, string eventType)
        {
            if (string.IsNullOrEmpty(gameId))
                throw new ArgumentException("gameId must not be null or empty.", nameof(gameId));
            if (string.IsNullOrEmpty(eventType))
                throw new ArgumentException("eventType must not be null or empty.", nameof(eventType));
            _entries.Add(gameId + ":" + eventType);
        }

        public int Count => _entries.Count;

        public void Clear() => _entries.Clear();

        public bool HasEntry(string gameId) => _entries.Exists(e => e.StartsWith(gameId + ":", StringComparison.Ordinal));
    }
}
