// RetentionKPIGate.cs — stdlib only: System, System.Collections.Generic
// No Unity, no external SDK dependencies.
using System;
using System.Collections.Generic;

namespace Match3.Engine.Runtime.Analytics
{
    public enum RetentionWindow { Day1, Day7, Day30 }

    public readonly struct RetentionRecord
    {
        public string GameId { get; }
        public RetentionWindow Window { get; }
        public bool IsTestDevice { get; }

        private RetentionRecord(string gameId, RetentionWindow window, bool isTestDevice)
        {
            if (string.IsNullOrEmpty(gameId))
                throw new ArgumentException("GameId must not be null or empty.", nameof(gameId));
            GameId = gameId;
            Window = window;
            IsTestDevice = isTestDevice;
        }

        public static RetentionRecord Create(string gameId, RetentionWindow window)
            => new RetentionRecord(gameId, window, false);

        public static RetentionRecord CreateTestDevice(string gameId, RetentionWindow window)
            => new RetentionRecord(gameId, window, true);

        public bool IsEligible => !IsTestDevice;
    }

    public readonly struct RetentionPolicy
    {
        public bool ExcludeTestDevices { get; }
        [Obsolete("Ignored: analytics consent (lawful basis) is always required for retention KPIs, under every policy.")]
        public bool RequireLawfulBasis => true;
        public string PolicyDescription { get; }

        private RetentionPolicy(bool excludeTestDevices, string policyDescription)
        {
            if (string.IsNullOrEmpty(policyDescription))
                throw new ArgumentException("PolicyDescription must not be null or empty.", nameof(policyDescription));
            ExcludeTestDevices = excludeTestDevices;
            PolicyDescription = policyDescription;
        }

        public static RetentionPolicy Safe(string desc) => new RetentionPolicy(true, desc);

        public static RetentionPolicy Default =>
            Safe("Test devices excluded; explicit analytics consent required for retention KPIs");
    }

    public static class RetentionKPIGate
    {
        /// <summary>
        /// Consent-aware retention gate. Analytics consent is evaluated by
        /// <see cref="ConsentAnalyticsGate"/> under <see cref="ConsentPolicy.Default"/> (explicit consent,
        /// never ChildSafe/NoneRequired/revoked) and is required unconditionally.
        /// </summary>
        public static (bool eligible, string reason) CanRecordRetention(
            string gameId, RetentionRecord record, ConsentRecord consent, RetentionPolicy policy)
        {
            if (policy.PolicyDescription == null) policy = RetentionPolicy.Default;

            if (string.IsNullOrEmpty(gameId))
                return (false, "GameIdMissing");
            if (!GameProfileCatalog.IsKnownId(gameId))
                return (false, "UnknownGame");

            if (!string.Equals(gameId, record.GameId, StringComparison.Ordinal) ||
                !string.Equals(gameId, consent.GameId, StringComparison.Ordinal))
                return (false, "GameIdMismatch");

            if (string.Equals(gameId, "Wonder_Pop", StringComparison.Ordinal))
                return (false, "WonderPopRetentionProhibited");

            // Unconditional: explicit analytics consent is required under every policy
            if (!ConsentAnalyticsGate.CanCollectAnalytics(gameId, consent, ConsentPolicy.Default, out _))
                return (false, "AnalyticsConsentRequired");

            if (policy.ExcludeTestDevices && record.IsTestDevice)
                return (false, "TestDeviceExcluded");

            return (true, string.Empty);
        }

        /// <summary>Consent unknown: fails closed with "AnalyticsConsentRequired".</summary>
        [Obsolete("Consent unknown: always ineligible (AnalyticsConsentRequired). Use the overload taking a ConsentRecord.")]
        public static (bool eligible, string reason) CanRecordRetention(
            string gameId, RetentionRecord record, RetentionPolicy policy)
        {
            if (string.IsNullOrEmpty(gameId))
                return (false, "GameIdMissing");
            if (!GameProfileCatalog.IsKnownId(gameId))
                return (false, "UnknownGame");

            if (!string.Equals(gameId, record.GameId, StringComparison.Ordinal))
                return (false, "GameIdMismatch");

            if (string.Equals(gameId, "Wonder_Pop", StringComparison.Ordinal))
                return (false, "WonderPopRetentionProhibited");

            return (false, "AnalyticsConsentRequired");
        }

        public static bool IsEligibleDevice(RetentionRecord record) => record.IsEligible;

        // Analytics consent (lawful basis) is unconditional; the policy cannot waive it.
        public static bool RequiresLawfulBasis(RetentionPolicy policy) => true;
    }

    public sealed class RetentionLog
    {
        private readonly List<(string gameId, RetentionWindow window)> _entries = new List<(string, RetentionWindow)>();

        public int Count => _entries.Count;

        public void Record(string gameId, RetentionWindow window)
        {
            if (string.IsNullOrEmpty(gameId))
                throw new ArgumentException("GameId must not be null or empty.", nameof(gameId));
            _entries.Add((gameId, window));
        }

        public void Clear() => _entries.Clear();

        public bool HasEntry(string gameId)
        {
            foreach (var e in _entries)
                if (string.Equals(e.gameId, gameId, StringComparison.Ordinal)) return true;
            return false;
        }
    }
}
