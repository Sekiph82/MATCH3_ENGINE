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
            return new FeatureFlagRecord(gameId, feature, flagVersion, isEnabled, hasConsentCheck);
        }

        public bool IsRestrictedFeature =>
            Feature == LiveOpsFeature.TournamentMode || Feature == LiveOpsFeature.WeeklyPersonalBest;

        public bool IsDefaultOff => !IsEnabled || IsRestrictedFeature;
    }

    public readonly struct FeatureFlagPolicy
    {
        public bool EnforceConsentCheck { get; }
        public bool BlockRestrictedWithoutConsent { get; }
        public string PolicyDescription { get; }

        private FeatureFlagPolicy(bool enforceConsentCheck, bool blockRestrictedWithoutConsent, string policyDescription)
        {
            EnforceConsentCheck = enforceConsentCheck;
            BlockRestrictedWithoutConsent = blockRestrictedWithoutConsent;
            PolicyDescription = policyDescription;
        }

        public static FeatureFlagPolicy Safe(string desc)
        {
            if (string.IsNullOrEmpty(desc))
                throw new ArgumentException("PolicyDescription must not be null or empty.", nameof(desc));
            return new FeatureFlagPolicy(true, true, desc);
        }

        public static readonly FeatureFlagPolicy Default = Safe(
            "Default feature flag policy - enforce consent checks, block restricted features without consent");
    }

    public static class FeatureFlagGate
    {
        public static bool CanEnableFlag(string gameId, FeatureFlagRecord record, FeatureFlagPolicy policy, out string reason)
        {
            // Guard 1: Wonder Pop explicitly prohibits child tournaments
            if (string.Equals(gameId, "Wonder_Pop", StringComparison.Ordinal) &&
                record.Feature == LiveOpsFeature.TournamentMode)
            {
                reason = "WonderPopTournamentProhibited";
                return false;
            }

            // Guard 2: kill switch — flag is explicitly disabled
            if (!record.IsEnabled)
            {
                reason = "FeatureFlagDisabled";
                return false;
            }

            // Guard 3: restricted feature requires consent check
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
            _entries.Add(gameId + ":" + featureName);
        }

        public int Count => _entries.Count;

        public void Clear() => _entries.Clear();

        public bool HasEntry(string gameId) => _entries.Exists(e => e.StartsWith(gameId + ":"));
    }
}
