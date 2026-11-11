using System;
using System.Collections.Generic;

namespace Match3.Engine.Runtime.Identity
{
    public enum CloudSaveSlot
    {
        LocalOnly,
        CloudPrimary,
        Mirror
    }

    public readonly struct CloudSaveToken
    {
        public string GameId { get; }
        public CloudSaveSlot SlotType { get; }
        public string TokenHash { get; }

        private CloudSaveToken(string gameId, CloudSaveSlot slotType, string tokenHash)
        {
            GameId = gameId;
            SlotType = slotType;
            TokenHash = tokenHash;
        }

        public bool IsCloudBacked => SlotType != CloudSaveSlot.LocalOnly;

        public static CloudSaveToken Create(string gameId, CloudSaveSlot slotType, string tokenHash)
        {
            if (string.IsNullOrEmpty(gameId))
                throw new ArgumentException("GameId must not be null or empty.", nameof(gameId));
            if (string.IsNullOrEmpty(tokenHash))
                throw new ArgumentException("TokenHash must not be null or empty.", nameof(tokenHash));
            return new CloudSaveToken(gameId, slotType, tokenHash);
        }
    }

    public readonly struct CloudSavePolicy
    {
        public bool AllowCloudWrite { get; }
        public bool RequireTokenValidation { get; }
        public string PolicyDescription { get; }

        public CloudSavePolicy(bool allowCloudWrite, bool requireTokenValidation, string policyDescription)
        {
            if (string.IsNullOrEmpty(policyDescription))
                throw new ArgumentException("PolicyDescription must not be null or empty.", nameof(policyDescription));
            AllowCloudWrite = allowCloudWrite;
            RequireTokenValidation = requireTokenValidation;
            PolicyDescription = policyDescription;
        }

        public static CloudSavePolicy Safe(string desc) =>
            new CloudSavePolicy(false, true, desc);

        public static CloudSavePolicy Default =>
            Safe("Cloud write disabled until backend provisioned");
    }

    public readonly struct CloudSaveEligibilityResult
    {
        public bool IsEligible { get; }
        public string Reason { get; }

        public CloudSaveEligibilityResult(bool isEligible, string reason)
        {
            IsEligible = isEligible;
            Reason = reason;
        }
    }

    public static class CloudSaveAdapterGate
    {
        private static CloudSavePolicy Normalize(CloudSavePolicy policy) =>
            policy.PolicyDescription == null ? CloudSavePolicy.Default : policy;

        public static CloudSaveEligibilityResult CanWriteToCloud(string gameId, CloudSavePolicy policy)
        {
            // Fail closed: a missing or non-catalog game context is never eligible
            if (string.IsNullOrEmpty(gameId))
                return new CloudSaveEligibilityResult(false, "GameIdMissing");
            if (!GameProfileCatalog.IsKnownId(gameId))
                return new CloudSaveEligibilityResult(false, "UnknownGame");

            policy = Normalize(policy);

            // Guard 1 FIRST (CRITICAL): Wonder_Pop is prohibited
            if (string.Equals(gameId, "Wonder_Pop", StringComparison.Ordinal))
                return new CloudSaveEligibilityResult(false, "WonderPopCloudWriteProhibited");

            // Guard 2: cloud write disabled by policy
            if (!policy.AllowCloudWrite)
                return new CloudSaveEligibilityResult(false, "CloudWriteDisabled");

            return new CloudSaveEligibilityResult(true, "Eligible");
        }

        public static bool RequiresTokenValidation(CloudSavePolicy policy) =>
            Normalize(policy).RequireTokenValidation;

        public static bool IsCloudSlot(CloudSaveToken token) =>
            token.IsCloudBacked;
    }

    public sealed class CloudSaveLog
    {
        private readonly List<(CloudSaveToken token, bool succeeded, string reason)> _entries =
            new List<(CloudSaveToken token, bool succeeded, string reason)>();

        public void Record(CloudSaveToken token, bool succeeded, string reason)
        {
            if (string.IsNullOrEmpty(token.GameId))
                throw new ArgumentException("Token GameId must not be null or empty.", nameof(token));
            if (string.IsNullOrEmpty(reason))
                throw new ArgumentException("Reason must not be null or empty.", nameof(reason));
            _entries.Add((token, succeeded, reason));
        }

        public int Count => _entries.Count;

        public void Clear() => _entries.Clear();

        public bool HasEntry(string gameId)
        {
            foreach (var entry in _entries)
                if (string.Equals(entry.token.GameId, gameId, StringComparison.Ordinal))
                    return true;
            return false;
        }
    }
}
