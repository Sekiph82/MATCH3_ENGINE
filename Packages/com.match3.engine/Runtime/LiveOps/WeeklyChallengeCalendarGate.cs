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

        // Offline recovery does not waive the anchor: offline claims wait for anchored reconciliation.
        public bool RequiresUtcAnchor => !HasUtcAnchor;
    }

    // Duplicate-claim prevention and the UTC anchor are unconditional core invariants, so the policy
    // carries no relaxing flags; default(CalendarEventPolicy) therefore cannot fail open.
    public readonly struct CalendarEventPolicy
    {
        public string PolicyDescription { get; }

        public CalendarEventPolicy(string policyDescription)
        {
            if (string.IsNullOrEmpty(policyDescription))
                throw new ArgumentException("PolicyDescription must not be null or empty.", nameof(policyDescription));
            PolicyDescription = policyDescription;
        }

        public static CalendarEventPolicy Safe(string desc) => new CalendarEventPolicy(desc);

        public static readonly CalendarEventPolicy Default = Safe(
            "Default calendar policy - prevent duplicate claims, require UTC anchor for week IDs");
    }

    public static class WeeklyChallengeCalendarGate
    {
        public static bool CanClaimReward(string gameId, CalendarEventRecord record, CalendarEventPolicy policy, out string reason)
        {
            if (string.IsNullOrEmpty(gameId))
            {
                reason = "GameIdMissing";
                return false;
            }

            if (!string.Equals(gameId, record.GameId, StringComparison.Ordinal))
            {
                reason = "GameIdMismatch";
                return false;
            }

            // Guard 1: Wonder Pop explicitly prohibits duplicate claims (Ordinal match)
            if (string.Equals(gameId, "Wonder_Pop", StringComparison.Ordinal) && record.IsAlreadyClaimed)
            {
                reason = "WonderPopDuplicateClaimProhibited";
                return false;
            }

            // Guard 2 (unconditional): duplicate claims are always blocked
            if (record.IsAlreadyClaimed)
            {
                reason = "DuplicateClaimDetected";
                return false;
            }

            // Guard 3 (unconditional): UTC anchor required, including for offline recovery
            if (record.RequiresUtcAnchor)
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
            if (string.IsNullOrEmpty(eventType))
                throw new ArgumentException("eventType must not be null or empty.", nameof(eventType));
            _entries.Add(gameId + ":" + eventType);
        }

        public int Count => _entries.Count;

        public void Clear() => _entries.Clear();

        public bool HasEntry(string gameId) => _entries.Exists(e => e.StartsWith(gameId + ":", StringComparison.Ordinal));
    }

    // Engine-side idempotency ledger: one reward claim per (game, player, week).
    public sealed class WeeklyClaimLedger
    {
        private readonly HashSet<string> _claims = new HashSet<string>(StringComparer.Ordinal);

        public bool TryClaim(string gameId, string playerId, string weekId)
        {
            if (string.IsNullOrEmpty(gameId))
                throw new ArgumentException("gameId must not be null or empty.", nameof(gameId));
            if (string.IsNullOrEmpty(playerId))
                throw new ArgumentException("playerId must not be null or empty.", nameof(playerId));
            if (string.IsNullOrEmpty(weekId))
                throw new ArgumentException("weekId must not be null or empty.", nameof(weekId));
            // Length-prefixed components keep keys unambiguous even if an id contains '|'.
            return _claims.Add(gameId.Length + ":" + gameId + "|" + playerId.Length + ":" + playerId + "|" + weekId);
        }
    }
}
