using System;
using System.Collections.Generic;

namespace Match3.Engine.Runtime.Privacy
{
    public enum WellbeingFeature
    {
        PlaytimeReminder,
        PressureLoopDisable,
        AgeAppropriateOffer,
        DarkPatternBlock,
        AccessibilityControl,
        ContentRatingFilter
    }

    public readonly struct WellbeingRecord
    {
        public string GameId { get; }
        public WellbeingFeature Feature { get; }
        public bool IsOptional { get; }
        public bool IsUserRequested { get; }

        private WellbeingRecord(string gameId, WellbeingFeature feature, bool isOptional, bool isUserRequested)
        {
            GameId = gameId;
            Feature = feature;
            IsOptional = isOptional;
            IsUserRequested = isUserRequested;
        }

        public static WellbeingRecord Create(string gameId, WellbeingFeature feature, bool isOptional, bool isUserRequested)
        {
            if (string.IsNullOrEmpty(gameId))
                throw new ArgumentException("gameId must not be null or empty.", nameof(gameId));
            return new WellbeingRecord(gameId, feature, isOptional, isUserRequested);
        }

        public bool IsProtectiveFeature =>
            Feature == WellbeingFeature.PressureLoopDisable ||
            Feature == WellbeingFeature.DarkPatternBlock ||
            Feature == WellbeingFeature.ContentRatingFilter;

        public bool RequiresUserConsent => IsOptional && !IsUserRequested;
    }

    public readonly struct WellbeingPolicy
    {
        public bool EnforceWellbeingFeatures { get; }
        public bool BlockDarkPatterns { get; }
        public string PolicyDescription { get; }

        private WellbeingPolicy(bool enforceWellbeingFeatures, bool blockDarkPatterns, string policyDescription)
        {
            EnforceWellbeingFeatures = enforceWellbeingFeatures;
            BlockDarkPatterns = blockDarkPatterns;
            PolicyDescription = policyDescription;
        }

        public static WellbeingPolicy Safe(string desc)
        {
            if (string.IsNullOrEmpty(desc))
                throw new ArgumentException("PolicyDescription must not be null or empty.", nameof(desc));
            return new WellbeingPolicy(true, true, desc);
        }

        public static readonly WellbeingPolicy Default = Safe("Default wellbeing policy - enforce wellbeing features, block dark patterns");
    }

    public static class AccessibilityWellbeingGate
    {
        public static bool CanEnableWellbeingFeature(string gameId, WellbeingRecord record, WellbeingPolicy policy, out string reason)
        {
            if (policy.PolicyDescription == null) policy = WellbeingPolicy.Default;

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

            // Guard 1: Wonder Pop pressure loop disable must be optional
            if (string.Equals(gameId, "Wonder_Pop", StringComparison.Ordinal) &&
                record.Feature == WellbeingFeature.PressureLoopDisable &&
                !record.IsOptional)
            {
                reason = "WonderPopPressureLoopMustBeOptional";
                return false;
            }

            // Guard 2: Block dark patterns that are not user-requested
            if (policy.BlockDarkPatterns &&
                record.Feature == WellbeingFeature.DarkPatternBlock &&
                !record.IsUserRequested)
            {
                reason = "DarkPatternBlockRequired";
                return false;
            }

            // Guard 3: User consent required for optional non-user-requested features
            if (policy.EnforceWellbeingFeatures && record.RequiresUserConsent)
            {
                reason = "UserConsentRequired";
                return false;
            }

            reason = string.Empty;
            return true;
        }

        public static bool IsProtectiveFeature(WellbeingRecord record) => record.IsProtectiveFeature;

        public static bool RequiresUserConsent(WellbeingRecord record) => record.RequiresUserConsent;
    }

    public sealed class WellbeingLog
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

        public bool HasEntry(string gameId) => _entries.Exists(e => e.StartsWith(gameId + ":", StringComparison.Ordinal));
    }
}
