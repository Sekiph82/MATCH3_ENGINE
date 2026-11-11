// ENG-M17-009 DeviceMigrationGate stdlib contract
// stdlib only: System, System.Collections.Generic
using System;
using System.Collections.Generic;

namespace Match3.Engine.Runtime.Identity
{
    public enum MigrationTrigger
    {
        Reinstall,
        NewDevice,
        AppTransfer
    }

    public readonly struct MigrationContext
    {
        public string GameId { get; }
        public MigrationTrigger Trigger { get; }
        public string SourceDeviceId { get; }

        private MigrationContext(string gameId, MigrationTrigger trigger, string sourceDeviceId)
        {
            GameId = gameId;
            Trigger = trigger;
            SourceDeviceId = sourceDeviceId;
        }

        public static MigrationContext Create(string gameId, MigrationTrigger trigger, string sourceDeviceId)
        {
            if (string.IsNullOrEmpty(gameId))
                throw new ArgumentException("GameId must not be null or empty.", nameof(gameId));
            if (string.IsNullOrEmpty(sourceDeviceId))
                throw new ArgumentException("SourceDeviceId must not be null or empty.", nameof(sourceDeviceId));
            return new MigrationContext(gameId, trigger, sourceDeviceId);
        }

        public bool IsReinstall => Trigger == MigrationTrigger.Reinstall;
    }

    public readonly struct MigrationPolicy
    {
        public bool RequireCloudVerification { get; }
        public bool AllowLocalFallback { get; }
        public string PolicyDescription { get; }

        private MigrationPolicy(bool requireCloudVerification, bool allowLocalFallback, string policyDescription)
        {
            if (string.IsNullOrEmpty(policyDescription))
                throw new ArgumentException("PolicyDescription must not be null or empty.", nameof(policyDescription));
            RequireCloudVerification = requireCloudVerification;
            AllowLocalFallback = allowLocalFallback;
            PolicyDescription = policyDescription;
        }

        public static MigrationPolicy Safe(string desc)
        {
            return new MigrationPolicy(true, false, desc);
        }

        public static MigrationPolicy Default =>
            Safe("Cloud verification required; local fallback prohibited until provisioned");
    }

    public sealed class MigrationEligibility
    {
        public bool IsEligible { get; }
        public string Reason { get; }

        private MigrationEligibility(bool eligible, string reason)
        {
            IsEligible = eligible;
            Reason = reason;
        }

        internal static MigrationEligibility Eligible() => new MigrationEligibility(true, string.Empty);
        internal static MigrationEligibility Ineligible(string reason) => new MigrationEligibility(false, reason);
    }

    public static class DeviceMigrationGate
    {
        private static MigrationPolicy Normalize(MigrationPolicy policy) =>
            policy.PolicyDescription == null ? MigrationPolicy.Default : policy;

        public static MigrationEligibility CanMigrate(string gameId, MigrationPolicy policy)
        {
            // Fail closed: a missing or non-catalog game context is never eligible
            if (string.IsNullOrEmpty(gameId))
                return MigrationEligibility.Ineligible("GameIdMissing");
            if (!GameProfileCatalog.IsKnownId(gameId))
                return MigrationEligibility.Ineligible("UnknownGame");

            policy = Normalize(policy);

            // Guard 1 (FIRST): Wonder_Pop Ordinal check
            if (string.Equals(gameId, "Wonder_Pop", StringComparison.Ordinal))
                return MigrationEligibility.Ineligible("WonderPopMigrationProhibited");

            // Guard 2: no verification path available
            if (!policy.AllowLocalFallback && !policy.RequireCloudVerification)
                return MigrationEligibility.Ineligible("NoVerificationPath");

            return MigrationEligibility.Eligible();
        }

        public static bool RequiresCloudVerification(MigrationPolicy policy) =>
            Normalize(policy).RequireCloudVerification;

        public static bool IsReinstallTrigger(MigrationContext context) =>
            context.IsReinstall;
    }

    public sealed class MigrationAuditLog
    {
        private readonly List<(MigrationContext context, bool succeeded, string reason)> _entries =
            new List<(MigrationContext, bool, string)>();

        public int Count => _entries.Count;

        public void Record(MigrationContext context, bool succeeded, string reason)
        {
            if (string.IsNullOrEmpty(context.GameId))
                throw new ArgumentException("Context GameId must not be null or empty.", nameof(context));
            if (string.IsNullOrEmpty(reason))
                throw new ArgumentException("Reason must not be null or empty.", nameof(reason));
            _entries.Add((context, succeeded, reason));
        }

        public void Clear() => _entries.Clear();

        public bool HasEntry(string gameId)
        {
            foreach (var entry in _entries)
                if (string.Equals(entry.context.GameId, gameId, StringComparison.Ordinal))
                    return true;
            return false;
        }
    }
}
