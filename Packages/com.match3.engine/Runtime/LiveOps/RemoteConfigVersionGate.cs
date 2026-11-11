using System;
using System.Collections.Generic;

namespace Match3.Engine.Runtime.LiveOps
{
    public enum RemoteConfigKey
    {
        GameplayDifficulty,
        CoinRewardAmount,
        EventCalendarId,
        OfferPresentation,
        GateFeatureFlag,
        SessionTimeLimit
    }

    public readonly struct RemoteConfigRecord
    {
        public string GameId { get; }
        public RemoteConfigKey Key { get; }
        public string Value { get; }
        public int SchemaVersion { get; }
        public bool IsSigned { get; }
        public bool HasFallback { get; }

        private RemoteConfigRecord(string gameId, RemoteConfigKey key, string value, int schemaVersion, bool isSigned, bool hasFallback)
        {
            GameId = gameId;
            Key = key;
            Value = value;
            SchemaVersion = schemaVersion;
            IsSigned = isSigned;
            HasFallback = hasFallback;
        }

        public static RemoteConfigRecord Create(string gameId, RemoteConfigKey key, string value, int schemaVersion, bool isSigned, bool hasFallback)
        {
            if (string.IsNullOrEmpty(gameId))
                throw new ArgumentException("gameId cannot be null or empty", nameof(gameId));
            if (value == null)
                throw new ArgumentException("value cannot be null", nameof(value));
            if (schemaVersion < 0)
                throw new ArgumentOutOfRangeException(nameof(schemaVersion), "schemaVersion cannot be negative");
            return new RemoteConfigRecord(gameId, key, value, schemaVersion, isSigned, hasFallback);
        }

        public bool IsGameplaySafe =>
            Key == RemoteConfigKey.GameplayDifficulty ||
            Key == RemoteConfigKey.CoinRewardAmount ||
            Key == RemoteConfigKey.SessionTimeLimit;

        public bool RequiresFallback => !HasFallback && !IsSigned;
    }

    public readonly struct RemoteConfigPolicy
    {
        public bool RequireSignature { get; }
        public bool RequireFallback { get; }
        public int MinSchemaVersion { get; }
        public string PolicyDescription { get; }

        private RemoteConfigPolicy(bool requireSignature, bool requireFallback, int minSchemaVersion, string policyDescription)
        {
            RequireSignature = requireSignature;
            RequireFallback = requireFallback;
            MinSchemaVersion = minSchemaVersion;
            PolicyDescription = policyDescription;
        }

        public static RemoteConfigPolicy Safe(string desc)
        {
            if (string.IsNullOrEmpty(desc))
                throw new ArgumentException("PolicyDescription cannot be null or empty", nameof(desc));
            return new RemoteConfigPolicy(true, true, 1, desc);
        }

        public static readonly RemoteConfigPolicy Default = Safe("Default remote config policy - require signed configs with fallback, min schema v1");
    }

    public static class RemoteConfigVersionGate
    {
        public static bool CanApplyConfig(string gameId, RemoteConfigRecord record, RemoteConfigPolicy policy, out string reason)
        {
            if (policy.PolicyDescription == null) policy = RemoteConfigPolicy.Default;

            if (!string.Equals(gameId, record.GameId, StringComparison.Ordinal))
            {
                reason = "GameIdMismatch";
                return false;
            }

            if (string.Equals(gameId, "Wonder_Pop", StringComparison.Ordinal) && !record.IsSigned)
            {
                reason = "WonderPopUnsignedConfigProhibited";
                return false;
            }

            if (policy.RequireSignature && !record.IsSigned)
            {
                reason = "UnsignedConfigRejected";
                return false;
            }

            if (record.SchemaVersion < policy.MinSchemaVersion)
            {
                reason = "SchemaVersionTooOld";
                return false;
            }

            reason = string.Empty;
            return true;
        }

        public static bool IsGameplaySafe(RemoteConfigRecord record) => record.IsGameplaySafe;

        public static bool RequiresFallback(RemoteConfigRecord record) => record.RequiresFallback;
    }

    public sealed class RemoteConfigVersionLog
    {
        private readonly List<string> _entries = new List<string>();

        public void Record(string gameId, string configKey)
        {
            if (string.IsNullOrEmpty(gameId))
                throw new ArgumentException("gameId cannot be null or empty", nameof(gameId));
            if (string.IsNullOrEmpty(configKey))
                throw new ArgumentException("configKey cannot be null or empty", nameof(configKey));
            _entries.Add(gameId + ":" + configKey);
        }

        public int Count => _entries.Count;

        public void Clear() => _entries.Clear();

        public bool HasEntry(string gameId) => _entries.Exists(e => e.StartsWith(gameId + ":", StringComparison.Ordinal));
    }
}
