using System;
using System.Collections.Generic;

namespace Match3.Engine.Runtime.Privacy
{
    public enum PrivacyDataCategory
    {
        NoDataCollected,
        DeviceIdentifiers,
        UsageData,
        DiagnosticData,
        LocationData,
        ContactInfo
    }

    public readonly struct PrivacyManifestRecord
    {
        public string GameId { get; }
        public PrivacyDataCategory DataCategory { get; }
        public bool HasSdkDisclosure { get; }
        public bool IsRequiredReasonApi { get; }

        private PrivacyManifestRecord(string gameId, PrivacyDataCategory dataCategory, bool hasSdkDisclosure, bool isRequiredReasonApi)
        {
            GameId = gameId;
            DataCategory = dataCategory;
            HasSdkDisclosure = hasSdkDisclosure;
            IsRequiredReasonApi = isRequiredReasonApi;
        }

        public static PrivacyManifestRecord Create(string gameId, PrivacyDataCategory dataCategory, bool hasSdkDisclosure, bool isRequiredReasonApi)
        {
            if (string.IsNullOrEmpty(gameId))
                throw new ArgumentException("gameId cannot be null or empty.", nameof(gameId));
            return new PrivacyManifestRecord(gameId, dataCategory, hasSdkDisclosure, isRequiredReasonApi);
        }

        public bool RequiresDisclosure => DataCategory != PrivacyDataCategory.NoDataCollected;

        public bool IsSdkCompliant => HasSdkDisclosure || DataCategory == PrivacyDataCategory.NoDataCollected;
    }

    public readonly struct PrivacyManifestPolicy
    {
        public bool RequireSdkDisclosure { get; }
        public bool BlockUndisclosedTracking { get; }
        public string PolicyDescription { get; }

        private PrivacyManifestPolicy(bool requireSdkDisclosure, bool blockUndisclosedTracking, string policyDescription)
        {
            RequireSdkDisclosure = requireSdkDisclosure;
            BlockUndisclosedTracking = blockUndisclosedTracking;
            PolicyDescription = policyDescription;
        }

        public static PrivacyManifestPolicy Safe(string desc)
        {
            if (string.IsNullOrEmpty(desc))
                throw new ArgumentException("PolicyDescription cannot be null or empty.", nameof(desc));
            return new PrivacyManifestPolicy(true, true, desc);
        }

        public static readonly PrivacyManifestPolicy Default = Safe("Default privacy manifest policy - require SDK disclosure, block undisclosed tracking");
    }

    public static class PrivacyManifestGate
    {
        public static bool CanDeclareDataUsage(string gameId, PrivacyManifestRecord record, PrivacyManifestPolicy policy, out string reason)
        {
            if (policy.PolicyDescription == null) policy = PrivacyManifestPolicy.Default;

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

            if (string.Equals(gameId, "Wonder_Pop", StringComparison.Ordinal))
            {
                reason = "WonderPopManifestProhibited";
                return false;
            }

            if (policy.BlockUndisclosedTracking && record.RequiresDisclosure && !record.HasSdkDisclosure)
            {
                reason = "UndisclosedTrackingBlocked";
                return false;
            }

            if (policy.RequireSdkDisclosure && record.IsRequiredReasonApi && !record.HasSdkDisclosure)
            {
                reason = "SdkDisclosureRequired";
                return false;
            }

            reason = string.Empty;
            return true;
        }

        public static bool RequiresDisclosure(PrivacyManifestRecord record) => record.RequiresDisclosure;

        public static bool IsSdkCompliant(PrivacyManifestRecord record) => record.IsSdkCompliant;
    }

    public sealed class PrivacyManifestLog
    {
        private readonly List<string> _entries = new List<string>();

        public void Record(string gameId, string category)
        {
            if (string.IsNullOrEmpty(gameId))
                throw new ArgumentException("gameId cannot be null or empty.", nameof(gameId));
            if (string.IsNullOrEmpty(category))
                throw new ArgumentException("category cannot be null or empty.", nameof(category));
            _entries.Add(gameId + ":" + category);
        }

        public int Count => _entries.Count;

        public void Clear() => _entries.Clear();

        public bool HasEntry(string gameId) => _entries.Exists(e => e.StartsWith(gameId + ":", StringComparison.Ordinal));
    }
}
