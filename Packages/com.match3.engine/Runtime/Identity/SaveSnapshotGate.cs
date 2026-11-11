using System;
using System.Collections.Generic;

namespace Match3.Engine.Runtime.Identity
{
    public enum SaveField
    {
        World,
        Progression,
        Stars,
        Hearts,
        Inventory,
        Bookmark
    }

    public readonly struct SaveSnapshot
    {
        public string GameId { get; }
        public int SchemaVersion { get; }
        public long Revision { get; }
        public bool IsEntitlementAuthoritative { get; }

        public SaveSnapshot(string gameId, int schemaVersion, long revision, bool isEntitlementAuthoritative)
        {
            if (string.IsNullOrEmpty(gameId))
                throw new ArgumentException("GameId must not be null or empty.", nameof(gameId));
            if (schemaVersion < 1)
                throw new ArgumentOutOfRangeException(nameof(schemaVersion), "SchemaVersion must be >= 1.");
            if (revision < 0)
                throw new ArgumentOutOfRangeException(nameof(revision), "Revision must be >= 0.");
            GameId = gameId;
            SchemaVersion = schemaVersion;
            Revision = revision;
            IsEntitlementAuthoritative = isEntitlementAuthoritative;
        }

        public static SaveSnapshot Create(string gameId, int schemaVersion, long revision) =>
            new SaveSnapshot(gameId, schemaVersion, revision, false);

        public static SaveSnapshot WithRevision(SaveSnapshot original, long newRevision)
        {
            if (newRevision < original.Revision)
                throw new ArgumentOutOfRangeException(nameof(newRevision), "newRevision must be >= original.Revision.");
            return new SaveSnapshot(original.GameId, original.SchemaVersion, newRevision, original.IsEntitlementAuthoritative);
        }
    }

    public readonly struct SaveSchemaPolicy
    {
        public int CurrentSchemaVersion { get; }
        public bool EntitlementsServerOnly { get; }
        public string PolicyDescription { get; }

        public SaveSchemaPolicy(int currentSchemaVersion, bool entitlementsServerOnly, string policyDescription)
        {
            if (string.IsNullOrEmpty(policyDescription))
                throw new ArgumentException("PolicyDescription must not be null or empty.", nameof(policyDescription));
            if (currentSchemaVersion < 1)
                throw new ArgumentOutOfRangeException(nameof(currentSchemaVersion), "CurrentSchemaVersion must be >= 1.");
            CurrentSchemaVersion = currentSchemaVersion;
            EntitlementsServerOnly = entitlementsServerOnly;
            PolicyDescription = policyDescription;
        }

        public static SaveSchemaPolicy V1(string desc) =>
            new SaveSchemaPolicy(1, true, desc);

        public static SaveSchemaPolicy Default =>
            V1("Save schema v1; entitlements are server-authoritative only");
    }

    public static class SaveSnapshotGate
    {
        private static SaveSchemaPolicy Normalize(SaveSchemaPolicy policy) =>
            policy.PolicyDescription == null ? SaveSchemaPolicy.Default : policy;

        public readonly struct EligibilityResult
        {
            public bool IsEligible { get; }
            public string Reason { get; }

            public EligibilityResult(bool isEligible, string reason)
            {
                IsEligible = isEligible;
                Reason = reason;
            }
        }

        public static EligibilityResult CanSaveLocally(string gameId, SaveField field, SaveSchemaPolicy policy)
        {
            // Guard 1 FIRST (CRITICAL): Wonder_Pop is prohibited from local save
            if (string.Equals(gameId, "Wonder_Pop", StringComparison.Ordinal))
                return new EligibilityResult(false, "WonderPopSaveProhibited");

            // All defined SaveFields are client-safe; return eligible
            return new EligibilityResult(true, "Eligible");
        }

        public static bool RequiresServerEntitlements(SaveSchemaPolicy policy) =>
            Normalize(policy).EntitlementsServerOnly;

        public static bool IsSchemaCompatible(SaveSnapshot snapshot, SaveSchemaPolicy policy) =>
            snapshot.SchemaVersion <= Normalize(policy).CurrentSchemaVersion;
    }

    public sealed class SaveRevisionLog
    {
        private readonly Dictionary<string, SaveSnapshot> _log = new Dictionary<string, SaveSnapshot>();

        public void Record(SaveSnapshot snapshot)
        {
            _log[snapshot.GameId] = snapshot;
        }

        public bool TryGet(string gameId, out SaveSnapshot snapshot) =>
            _log.TryGetValue(gameId, out snapshot);

        public void Clear() => _log.Clear();

        public int Count => _log.Count;
    }
}
