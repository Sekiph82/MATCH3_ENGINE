using System;
using System.Collections.Generic;

namespace Match3.Engine.Runtime.Identity
{
    public enum MergeStrategy
    {
        LocalWins,
        RemoteWins,
        ManualRequired
    }

    public readonly struct ConflictRecord
    {
        public string GameId { get; }
        public int LocalRevision { get; }
        public int RemoteRevision { get; }

        private ConflictRecord(string gameId, int localRevision, int remoteRevision)
        {
            GameId = gameId;
            LocalRevision = localRevision;
            RemoteRevision = remoteRevision;
        }

        public bool HasDivergence => LocalRevision != RemoteRevision;

        public static ConflictRecord Create(string gameId, int localRevision, int remoteRevision)
        {
            if (string.IsNullOrEmpty(gameId))
                throw new ArgumentException("GameId must not be null or empty.", nameof(gameId));
            if (localRevision < 0)
                throw new ArgumentOutOfRangeException(nameof(localRevision), "LocalRevision must be >= 0.");
            if (remoteRevision < 0)
                throw new ArgumentOutOfRangeException(nameof(remoteRevision), "RemoteRevision must be >= 0.");
            return new ConflictRecord(gameId, localRevision, remoteRevision);
        }
    }

    public readonly struct MergePolicy
    {
        public MergeStrategy DefaultStrategy { get; }
        public bool RequireUserConfirmation { get; }
        public string PolicyDescription { get; }

        public MergePolicy(MergeStrategy defaultStrategy, bool requireUserConfirmation, string policyDescription)
        {
            if (string.IsNullOrEmpty(policyDescription))
                throw new ArgumentException("PolicyDescription must not be null or empty.", nameof(policyDescription));
            DefaultStrategy = defaultStrategy;
            RequireUserConfirmation = requireUserConfirmation;
            PolicyDescription = policyDescription;
        }

        public static MergePolicy Safe(string desc) =>
            new MergePolicy(MergeStrategy.ManualRequired, true, desc);

        public static MergePolicy Default =>
            Safe("Manual merge required; no silent data overwrite");
    }

    public readonly struct MergeEligibilityResult
    {
        public bool IsEligible { get; }
        public string Reason { get; }

        public MergeEligibilityResult(bool isEligible, string reason)
        {
            IsEligible = isEligible;
            Reason = reason;
        }
    }

    public static class MergeConflictGate
    {
        public static MergeEligibilityResult CanAutoResolve(
            string gameId,
            ConflictRecord conflict,
            MergePolicy policy)
        {
            // Guard 1 (FIRST): Wonder_Pop is ineligible — Ordinal, exact case
            if (string.Equals(gameId, "Wonder_Pop", StringComparison.Ordinal))
                return new MergeEligibilityResult(false, "WonderPopMergeProhibited");

            // Guard 2: user confirmation required
            if (policy.RequireUserConfirmation)
                return new MergeEligibilityResult(false, "UserConfirmationRequired");

            // Guard 3: manual strategy
            if (policy.DefaultStrategy == MergeStrategy.ManualRequired)
                return new MergeEligibilityResult(false, "ManualStrategyRequired");

            return new MergeEligibilityResult(true, "Eligible");
        }

        public static bool RequiresConfirmation(MergePolicy policy) =>
            policy.RequireUserConfirmation;

        public static bool IsDivergent(ConflictRecord conflict) =>
            conflict.HasDivergence;
    }

    public sealed class MergeAuditLog
    {
        private readonly List<(ConflictRecord record, MergeStrategy applied, string reason)> _entries =
            new List<(ConflictRecord, MergeStrategy, string)>();

        public void Record(ConflictRecord record, MergeStrategy applied, string reason)
        {
            if (string.IsNullOrEmpty(reason))
                throw new ArgumentException("Reason must not be null or empty.", nameof(reason));
            _entries.Add((record, applied, reason));
        }

        public int Count => _entries.Count;

        public void Clear() => _entries.Clear();

        public bool HasEntry(string gameId)
        {
            foreach (var entry in _entries)
                if (string.Equals(entry.record.GameId, gameId, StringComparison.Ordinal))
                    return true;
            return false;
        }
    }
}
