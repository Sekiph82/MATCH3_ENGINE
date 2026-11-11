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
        // Strategy proposed to the user at confirmation time; it never auto-applies.
        public MergeStrategy DefaultStrategy { get; }
        [Obsolete("Ignored: every save conflict requires user confirmation, under every policy.")]
        public bool RequireUserConfirmation => true;
        public string PolicyDescription { get; }

        public MergePolicy(MergeStrategy defaultStrategy, string policyDescription)
        {
            if (string.IsNullOrEmpty(policyDescription))
                throw new ArgumentException("PolicyDescription must not be null or empty.", nameof(policyDescription));
            DefaultStrategy = defaultStrategy;
            PolicyDescription = policyDescription;
        }

        [Obsolete("requireUserConfirmation is ignored: every save conflict requires user confirmation. Use MergePolicy(MergeStrategy, string).")]
        public MergePolicy(MergeStrategy defaultStrategy, bool requireUserConfirmation, string policyDescription)
            : this(defaultStrategy, policyDescription)
        {
        }

        public static MergePolicy Safe(string desc) =>
            new MergePolicy(MergeStrategy.ManualRequired, desc);

        public static MergePolicy Default =>
            Safe("Manual merge with user confirmation required; save conflicts are never auto-resolved (no silent overwrite)");
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
            if (policy.PolicyDescription == null) policy = MergePolicy.Default;

            if (string.IsNullOrEmpty(gameId))
                return new MergeEligibilityResult(false, "GameIdMissing");
            if (!GameProfileCatalog.IsKnownId(gameId))
                return new MergeEligibilityResult(false, "UnknownGame");

            if (!string.Equals(gameId, conflict.GameId, StringComparison.Ordinal))
                return new MergeEligibilityResult(false, "GameIdMismatch");

            // Wonder_Pop is ineligible — Ordinal, exact case
            if (string.Equals(gameId, "Wonder_Pop", StringComparison.Ordinal))
                return new MergeEligibilityResult(false, "WonderPopMergeProhibited");

            // Unconditional (no silent overwrite): equal revision numbers do not prove a lossless merge
            // (two devices can edit offline from the same base), so no conflict is ever auto-resolved,
            // under any policy. Field-level merge (max stars, safe unlock graph) is NOT_IMPLEMENTED.
            return new MergeEligibilityResult(false, "UserConfirmationRequired");
        }

        // Every save conflict requires user confirmation; the policy cannot waive it.
        public static bool RequiresConfirmation(MergePolicy policy) => true;

        public static bool IsDivergent(ConflictRecord conflict) =>
            conflict.HasDivergence;
    }

    public sealed class MergeAuditLog
    {
        private readonly List<(ConflictRecord record, MergeStrategy applied, string reason)> _entries =
            new List<(ConflictRecord, MergeStrategy, string)>();

        public void Record(ConflictRecord record, MergeStrategy applied, string reason)
        {
            if (string.IsNullOrEmpty(record.GameId))
                throw new ArgumentException("Record GameId must not be null or empty.", nameof(record));
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
