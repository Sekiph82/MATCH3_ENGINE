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

    public readonly struct ChallengeEventPolicy
    {
        public bool RequireFallbackOnExpiry { get; }
        public bool BlockPermanentProgressModification { get; }
        public string PolicyDescription { get; }

        public ChallengeEventPolicy(bool requireFallbackOnExpiry, bool blockPermanentProgressModification, string policyDescription)
        {
            if (string.IsNullOrEmpty(policyDescription))
                throw new ArgumentException("PolicyDescription must not be null or empty.", nameof(policyDescription));
            RequireFallbackOnExpiry = requireFallbackOnExpiry;
            BlockPermanentProgressModification = blockPermanentProgressModification;
            PolicyDescription = policyDescription;
        }

        public static ChallengeEventPolicy Safe(string desc) => new ChallengeEventPolicy(true, true, desc);

        public static readonly ChallengeEventPolicy Default = Safe(
            "Default challenge event policy - require fallback on expiry, block permanent progress modification");
    }

    public static class LimitedTimeChallengeGate
    {
        public static bool CanActivateEvent(string gameId, ChallengeEventRecord record, ChallengeEventPolicy policy, out string reason)
        {
            // Guard 1: Wonder Pop events may never modify permanent progress (not policy-overridable)
            if (string.Equals(gameId, "Wonder_Pop", StringComparison.Ordinal) && record.AffectsPermanentProgress)
            {
                reason = "WonderPopPermanentProgressModificationProhibited";
                return false;
            }

            // Guard 2: event content must not alter permanent progress
            if (policy.BlockPermanentProgressModification && record.AffectsPermanentProgress)
            {
                reason = "PermanentProgressModificationBlocked";
                return false;
            }

            // Guard 3: expired/bad pack must have a last-known-good fallback
            if (policy.RequireFallbackOnExpiry && record.IsExpired && !record.HasFallback)
            {
                reason = "ExpiredEventRequiresFallback";
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
            _entries.Add(gameId + ":" + eventType);
        }

        public int Count => _entries.Count;

        public void Clear() => _entries.Clear();

        public bool HasEntry(string gameId) => _entries.Exists(e => e.StartsWith(gameId + ":"));
    }
}
