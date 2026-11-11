using System;
using System.Collections.Generic;

namespace Match3.Engine.Runtime.Privacy
{
    public enum RemoteConfigScope
    {
        Anonymous,
        AggregateOnly,
        ConsentedUser,
        PlatformDefault,
        DebugOverride,
        EmergencyKillSwitch
    }

    public readonly struct RemoteConfigRecord
    {
        public string GameId { get; }
        public RemoteConfigScope Scope { get; }
        public bool HasUserConsent { get; }
        public bool ContainsUserIdentifier { get; }

        private RemoteConfigRecord(string gameId, RemoteConfigScope scope, bool hasUserConsent, bool containsUserIdentifier)
        {
            GameId = gameId;
            Scope = scope;
            HasUserConsent = hasUserConsent;
            ContainsUserIdentifier = containsUserIdentifier;
        }

        public static RemoteConfigRecord Create(string gameId, RemoteConfigScope scope, bool hasUserConsent, bool containsUserIdentifier)
        {
            if (string.IsNullOrEmpty(gameId))
                throw new ArgumentException("gameId must not be null or empty.", nameof(gameId));
            return new RemoteConfigRecord(gameId, scope, hasUserConsent, containsUserIdentifier);
        }

        public bool IsConsentSafe => !ContainsUserIdentifier || HasUserConsent;
        public bool IsAnonymous => Scope == RemoteConfigScope.Anonymous || Scope == RemoteConfigScope.AggregateOnly;
    }

    public readonly struct ConsentSafeRemoteConfigPolicy
    {
        public bool RequireConsentForUserConfig { get; }
        public bool BlockUserIdentifiersWithoutConsent { get; }
        public string PolicyDescription { get; }

        private ConsentSafeRemoteConfigPolicy(bool requireConsentForUserConfig, bool blockUserIdentifiersWithoutConsent, string policyDescription)
        {
            RequireConsentForUserConfig = requireConsentForUserConfig;
            BlockUserIdentifiersWithoutConsent = blockUserIdentifiersWithoutConsent;
            PolicyDescription = policyDescription;
        }

        public static ConsentSafeRemoteConfigPolicy Safe(string desc)
        {
            if (string.IsNullOrEmpty(desc))
                throw new ArgumentException("PolicyDescription must not be null or empty.", nameof(desc));
            return new ConsentSafeRemoteConfigPolicy(true, true, desc);
        }

        public static readonly ConsentSafeRemoteConfigPolicy Default = Safe("Default consent-safe remote config policy - require consent for user config, block user identifiers without consent");
    }

    public static class ConsentSafeRemoteConfigGate
    {
        public static bool CanFetchRemoteConfig(string gameId, RemoteConfigRecord record, ConsentSafeRemoteConfigPolicy policy, out string reason)
        {
            if (policy.PolicyDescription == null) policy = ConsentSafeRemoteConfigPolicy.Default;

            if (!string.Equals(gameId, record.GameId, StringComparison.Ordinal))
            {
                reason = "GameIdMismatch";
                return false;
            }

            if (string.Equals(gameId, "Wonder_Pop", StringComparison.Ordinal) && record.ContainsUserIdentifier)
            {
                reason = "WonderPopUserIdentifierProhibited";
                return false;
            }

            if (policy.BlockUserIdentifiersWithoutConsent && record.ContainsUserIdentifier && !record.HasUserConsent)
            {
                reason = "UserIdentifierRequiresConsent";
                return false;
            }

            if (policy.RequireConsentForUserConfig && record.Scope == RemoteConfigScope.ConsentedUser && !record.HasUserConsent)
            {
                reason = "ConsentedScopeRequiresConsent";
                return false;
            }

            reason = string.Empty;
            return true;
        }

        public static bool IsConsentSafe(RemoteConfigRecord record) => record.IsConsentSafe;
        public static bool IsAnonymous(RemoteConfigRecord record) => record.IsAnonymous;
    }

    public sealed class RemoteConfigLog
    {
        private readonly List<string> _entries = new List<string>();

        public void Record(string gameId, string scopeName)
        {
            if (string.IsNullOrEmpty(gameId))
                throw new ArgumentException("gameId must not be null or empty.", nameof(gameId));
            if (string.IsNullOrEmpty(scopeName))
                throw new ArgumentException("scopeName must not be null or empty.", nameof(scopeName));
            _entries.Add(gameId + ":" + scopeName);
        }

        public int Count => _entries.Count;
        public void Clear() => _entries.Clear();
        public bool HasEntry(string gameId) => _entries.Exists(e => e.StartsWith(gameId + ":", StringComparison.Ordinal));
    }
}
