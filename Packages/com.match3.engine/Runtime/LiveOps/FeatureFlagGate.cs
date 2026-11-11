using System;
using System.Collections.Generic;

namespace Match3.Engine.Runtime.LiveOps
{
    public enum LiveOpsFeature
    {
        TimedMode,
        EndlessMode,
        WeeklyPersonalBest,
        SeasonalEvent,
        CatchUpReward,
        TournamentMode
    }

    public readonly struct FeatureFlagRecord
    {
        public string GameId { get; }
        public LiveOpsFeature Feature { get; }
        public int FlagVersion { get; }
        public bool IsEnabled { get; }
        public bool HasConsentCheck { get; }

        private FeatureFlagRecord(string gameId, LiveOpsFeature feature, int flagVersion, bool isEnabled, bool hasConsentCheck)
        {
            GameId = gameId;
            Feature = feature;
            FlagVersion = flagVersion;
            IsEnabled = isEnabled;
            HasConsentCheck = hasConsentCheck;
        }

        public static FeatureFlagRecord Create(string gameId, LiveOpsFeature feature, int flagVersion, bool isEnabled, bool hasConsentCheck)
        {
            if (string.IsNullOrEmpty(gameId))
                throw new ArgumentException("gameId must not be null or empty.", nameof(gameId));
            if (flagVersion < 1)
                throw new ArgumentOutOfRangeException(nameof(flagVersion), "flagVersion must be >= 1.");
            return new FeatureFlagRecord(gameId, feature, flagVersion, isEnabled, hasConsentCheck);
        }

        public bool IsRestrictedFeature =>
            Feature == LiveOpsFeature.TournamentMode || Feature == LiveOpsFeature.WeeklyPersonalBest;

        public bool IsDefaultOff => !IsEnabled || IsRestrictedFeature;
    }

    public readonly struct FeatureFlagPolicy
    {
        public bool BlockRestrictedWithoutConsent { get; }
        public string PolicyDescription { get; }

        private FeatureFlagPolicy(bool blockRestrictedWithoutConsent, string policyDescription)
        {
            if (string.IsNullOrEmpty(policyDescription))
                throw new ArgumentException("PolicyDescription must not be null or empty.", nameof(policyDescription));
            BlockRestrictedWithoutConsent = blockRestrictedWithoutConsent;
            PolicyDescription = policyDescription;
        }

        public static FeatureFlagPolicy Safe(string desc) => new FeatureFlagPolicy(true, desc);

        public static FeatureFlagPolicy Permissive(string desc) => new FeatureFlagPolicy(false, desc);

        public static readonly FeatureFlagPolicy Default = Safe(
            "Default feature flag policy - enforce consent checks, block restricted features without consent");
    }

    public static class FeatureFlagGate
    {
        public static bool CanEnableFlag(string gameId, FeatureFlagRecord record, FeatureFlagPolicy policy, out string reason)
        {
            // Fail closed: an uninitialized default(FeatureFlagPolicy) is treated as the safe Default policy.
            if (policy.PolicyDescription == null)
                policy = FeatureFlagPolicy.Default;

            // Guard 0: the record must belong to the game being evaluated (prevents Wonder_Pop guard bypass).
            if (!string.Equals(gameId, record.GameId, StringComparison.Ordinal))
            {
                reason = "GameIdMismatch";
                return false;
            }

            // Guard 1: Wonder Pop explicitly prohibits child tournaments (canonical ID, ordinal).
            if (string.Equals(gameId, "Wonder_Pop", StringComparison.Ordinal) &&
                record.Feature == LiveOpsFeature.TournamentMode)
            {
                reason = "WonderPopTournamentProhibited";
                return false;
            }

            // Guard 2: kill switch - flag is explicitly disabled.
            if (!record.IsEnabled)
            {
                reason = "FeatureFlagDisabled";
                return false;
            }

            // Guard 3: restricted feature requires consent check.
            if (policy.BlockRestrictedWithoutConsent && record.IsRestrictedFeature && !record.HasConsentCheck)
            {
                reason = "RestrictedFeatureRequiresConsent";
                return false;
            }

            reason = string.Empty;
            return true;
        }

        public static bool IsRestrictedFeature(FeatureFlagRecord record) => record.IsRestrictedFeature;

        public static bool IsDefaultOff(FeatureFlagRecord record) => record.IsDefaultOff;
    }

    public sealed class FeatureFlagLog
    {
        private readonly List<string> _entries = new List<string>();

        public void Record(string gameId, string featureName)
        {
            if (string.IsNullOrEmpty(gameId))
                throw new ArgumentException("gameId must not be null or empty.", nameof(gameId));
            if (string.IsNullOrEmpty(featureName))
                throw new ArgumentException("featureName must not be null or empty.", nameof(featureName));
            _entries.Add(gameId + ":" + featureName);
        }

        public int Count => _entries.Count;

        public void Clear() => _entries.Clear();

        public bool HasEntry(string gameId) =>
            _entries.Exists(e => e.StartsWith(gameId + ":", StringComparison.Ordinal));
    }
}
