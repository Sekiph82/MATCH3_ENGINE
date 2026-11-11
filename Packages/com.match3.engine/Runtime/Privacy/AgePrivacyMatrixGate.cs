using System;
using System.Collections.Generic;

namespace Match3.Engine.Runtime.Privacy
{
    public enum AgeClassification
    {
        AllAges,
        FamilyFriendly,
        Teen,
        MatureOnly
    }

    public readonly struct TitleAgeRecord
    {
        public string GameId { get; }
        public AgeClassification Classification { get; }

        private TitleAgeRecord(string gameId, AgeClassification classification)
        {
            GameId = gameId;
            Classification = classification;
        }

        public static TitleAgeRecord Create(string gameId, AgeClassification classification)
        {
            if (string.IsNullOrEmpty(gameId))
                throw new ArgumentException("gameId must not be null or empty.", nameof(gameId));
            return new TitleAgeRecord(gameId, classification);
        }

        public bool IsFamilyAppropriate =>
            Classification == AgeClassification.AllAges || Classification == AgeClassification.FamilyFriendly;

        public bool IsMatureContent => Classification == AgeClassification.MatureOnly;
    }

    public readonly struct AgePrivacyPolicy
    {
        public bool AllowBehavioralProfiling { get; }
        public bool BlockMatureContent { get; }
        public string PolicyDescription { get; }

        private AgePrivacyPolicy(bool allowBehavioralProfiling, bool blockMatureContent, string policyDescription)
        {
            if (string.IsNullOrEmpty(policyDescription))
                throw new ArgumentException("PolicyDescription must not be null or empty.", nameof(policyDescription));
            AllowBehavioralProfiling = allowBehavioralProfiling;
            BlockMatureContent = blockMatureContent;
            PolicyDescription = policyDescription;
        }

        public static AgePrivacyPolicy Safe(string desc)
        {
            return new AgePrivacyPolicy(false, true, desc);
        }

        public static AgePrivacyPolicy Permissive(string desc, bool blockMatureContent = false)
        {
            return new AgePrivacyPolicy(true, blockMatureContent, desc);
        }

        public static readonly AgePrivacyPolicy Default =
            Safe("Default age policy - no behavioral profiling, block mature content");
    }

    public static class AgePrivacyMatrixGate
    {
        public static bool CanEnableFeature(
            string gameId,
            TitleAgeRecord record,
            AgePrivacyPolicy policy,
            out string reason)
        {
            if (policy.PolicyDescription == null) policy = AgePrivacyPolicy.Default;

            if (string.IsNullOrEmpty(gameId))
            {
                reason = "GameIdMissing";
                return false;
            }
            if (!GameProfileCatalog.IsKnownId(gameId))
            {
                reason = "UnknownGame";
                return false;
            }

            if (!string.Equals(gameId, record.GameId, StringComparison.Ordinal))
            {
                reason = "GameIdMismatch";
                return false;
            }

            if (string.Equals(gameId, "Wonder_Pop", StringComparison.Ordinal))
            {
                reason = "WonderPopFeatureRestricted";
                return false;
            }

            if (!policy.AllowBehavioralProfiling)
            {
                reason = "BehavioralProfilingDisabled";
                return false;
            }

            if (policy.BlockMatureContent && record.IsMatureContent)
            {
                reason = "MatureContentBlocked";
                return false;
            }

            reason = string.Empty;
            return true;
        }

        public static bool IsFamilyAppropriate(TitleAgeRecord record) => record.IsFamilyAppropriate;

        public static bool IsMatureContent(TitleAgeRecord record) => record.IsMatureContent;
    }

    public sealed class AgePrivacyLog
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
