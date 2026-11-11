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
        public bool RequireLawfulBasis { get; }
        public string PolicyDescription { get; }

        private RetentionPolicy(bool excludeTestDevices, bool requireLawfulBasis, string policyDescription)
        {
            if (string.IsNullOrEmpty(policyDescription))
                throw new ArgumentException("PolicyDescription must not be null or empty.", nameof(policyDescription));
            ExcludeTestDevices = excludeTestDevices;
            RequireLawfulBasis = requireLawfulBasis;
            PolicyDescription = policyDescription;
        }

        public static RetentionPolicy Safe(string desc) => new RetentionPolicy(true, true, desc);

        public static RetentionPolicy Default =>
            Safe("Test devices excluded; lawful basis required for retention KPIs");
    }

    public static class RetentionKPIGate
    {
        public static (bool eligible, string reason) CanRecordRetention(
            string gameId, RetentionRecord record, RetentionPolicy policy)
        {
            // Guard 1 (FIRST): Wonder_Pop is structurally first
            if (string.Equals(gameId, "Wonder_Pop", StringComparison.Ordinal))
                return (false, "WonderPopRetentionProhibited");

            // Guard 2: test device exclusion
            if (policy.ExcludeTestDevices && record.IsTestDevice)
                return (false, "TestDeviceExcluded");

            // Guard 3: lawful basis required but device is not eligible
            // ponytail: fires only when ExcludeTestDevices=false, RequireLawfulBasis=true, IsTestDevice=true
            if (policy.RequireLawfulBasis && !record.IsEligible)
                return (false, "NoLawfulBasis");

            return (true, string.Empty);
        }

        public static bool IsEligibleDevice(RetentionRecord record) => record.IsEligible;

        public static bool RequiresLawfulBasis(RetentionPolicy policy) => policy.RequireLawfulBasis;
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
                if (e.gameId == gameId) return true;
            return false;
        }
    }
}
