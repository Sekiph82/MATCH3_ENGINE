using System;
using System.Collections.Generic;

namespace Match3.Engine.Runtime.LiveOps
{
    public enum CalendarEventType
    {
        DailyChallenge,
        WeeklyChallenge,
        ClaimReset,
        EventProgress,
        OfflineRecovery,
        AntiDuplicateCheck
    }

    public readonly struct CalendarEventRecord
    {
        public string GameId { get; }
        public CalendarEventType EventType { get; }
        public string WeekId { get; }
        public bool IsAlreadyClaimed { get; }
        public bool IsOfflineRecovery { get; }
        public bool HasUtcAnchor { get; }

        private CalendarEventRecord(string gameId, CalendarEventType eventType, string weekId, bool isAlreadyClaimed, bool isOfflineRecovery, bool hasUtcAnchor)
        {
            GameId = gameId;
            EventType = eventType;
            WeekId = weekId;
            IsAlreadyClaimed = isAlreadyClaimed;
            IsOfflineRecovery = isOfflineRecovery;
            HasUtcAnchor = hasUtcAnchor;
        }

        public static CalendarEventRecord Create(string gameId, CalendarEventType eventType, string weekId, bool isAlreadyClaimed, bool isOfflineRecovery, bool hasUtcAnchor)
        {
            if (string.IsNullOrEmpty(gameId))
                throw new ArgumentException("gameId must not be null or empty.", nameof(gameId));
            if (string.IsNullOrEmpty(weekId))
                throw new ArgumentException("weekId must not be null or empty.", nameof(weekId));
            return new CalendarEventRecord(gameId, eventType, weekId, isAlreadyClaimed, isOfflineRecovery, hasUtcAnchor);
        }

        public bool IsDuplicateClaim => IsAlreadyClaimed;

        public bool RequiresUtcAnchor => !HasUtcAnchor && !IsOfflineRecovery;
    }

    public readonly struct CalendarEventPolicy
    {
        public bool PreventDuplicateClaims { get; }
        public bool RequireUtcAnchor { get; }
        public string PolicyDescription { get; }

        private CalendarEventPolicy(bool preventDuplicateClaims, bool requireUtcAnchor, string policyDescription)
        {
            PreventDuplicateClaims = preventDuplicateClaims;
            RequireUtcAnchor = requireUtcAnchor;
            PolicyDescription = policyDescription;
        }

        public static CalendarEventPolicy Safe(string desc)
        {
            if (string.IsNullOrEmpty(desc))
                throw new ArgumentException("PolicyDescription must not be null or empty.", nameof(desc));
            return new CalendarEventPolicy(true, true, desc);
        }

        public static readonly CalendarEventPolicy Default = Safe(
            "Default calendar policy - prevent duplicate claims, require UTC anchor for week IDs");
    }

    public static class WeeklyChallengeCalendarGate
    {
        public static bool CanClaimReward(string gameId, CalendarEventRecord record, CalendarEventPolicy policy, out string reason)
        {
            // Guard 1: Wonder Pop explicitly prohibits duplicate claims (Ordinal match)
            if (string.Equals(gameId, "Wonder_Pop", StringComparison.Ordinal) && record.IsAlreadyClaimed)
            {
                reason = "WonderPopDuplicateClaimProhibited";
                return false;
            }

            // Guard 2: policy prevents duplicate claims
            if (policy.PreventDuplicateClaims && record.IsAlreadyClaimed)
            {
                reason = "DuplicateClaimDetected";
                return false;
            }

            // Guard 3: UTC anchor required but absent
            if (policy.RequireUtcAnchor && record.RequiresUtcAnchor)
            {
                reason = "UtcAnchorRequired";
                return false;
            }

            reason = string.Empty;
            return true;
        }

        public static bool IsDuplicateClaim(CalendarEventRecord record) => record.IsDuplicateClaim;

        public static bool RequiresUtcAnchor(CalendarEventRecord record) => record.RequiresUtcAnchor;
    }

    public sealed class CalendarEventLog
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
