// ENG-M18-008 CrashTelemetryGate stdlib contract
// stdlib only: System, System.Collections.Generic
using System;
using System.Collections.Generic;

namespace Match3.Engine.Runtime.Analytics
{
    public enum CrashSeverity
    {
        Warning,
        Error,
        Fatal
    }

    public readonly struct CrashReport
    {
        public string GameId { get; }
        public CrashSeverity Severity { get; }
        public bool IsSymbolicated { get; }

        private CrashReport(string gameId, CrashSeverity severity, bool isSymbolicated)
        {
            GameId = gameId;
            Severity = severity;
            IsSymbolicated = isSymbolicated;
        }

        public static CrashReport Create(string gameId, CrashSeverity severity, bool isSymbolicated)
        {
            if (string.IsNullOrEmpty(gameId))
                throw new ArgumentException("GameId must not be null or empty.", nameof(gameId));
            return new CrashReport(gameId, severity, isSymbolicated);
        }

        public bool IsFatal => Severity == CrashSeverity.Fatal;
        public bool RequiresUpload => IsFatal || !IsSymbolicated;
    }

    public readonly struct CrashTelemetryPolicy
    {
        public bool AllowCrashReporting { get; }
        public bool RequireSymbolication { get; }
        public string PolicyDescription { get; }

        private CrashTelemetryPolicy(bool allowCrashReporting, bool requireSymbolication, string description)
        {
            AllowCrashReporting = allowCrashReporting;
            RequireSymbolication = requireSymbolication;
            PolicyDescription = description;
        }

        public static CrashTelemetryPolicy Safe(string desc)
        {
            if (string.IsNullOrEmpty(desc))
                throw new ArgumentException("PolicyDescription must not be null or empty.", nameof(desc));
            return new CrashTelemetryPolicy(false, true, desc);
        }

        public static CrashTelemetryPolicy Allow(string desc)
        {
            if (string.IsNullOrEmpty(desc))
                throw new ArgumentException("PolicyDescription must not be null or empty.", nameof(desc));
            return new CrashTelemetryPolicy(true, true, desc);
        }

        public static readonly CrashTelemetryPolicy Default =
            Safe("Crash reporting disabled until provider authorized; symbolication required");
    }

    public static class CrashTelemetryGate
    {
        private static CrashTelemetryPolicy Normalize(CrashTelemetryPolicy policy) =>
            policy.PolicyDescription == null ? CrashTelemetryPolicy.Default : policy;

        public static (bool eligible, string reason) CanReportCrash(
            string gameId, CrashReport report, CrashTelemetryPolicy policy)
        {
            policy = Normalize(policy);

            if (string.IsNullOrEmpty(gameId))
                return (false, "GameIdMissing");
            if (!GameProfileCatalog.IsKnownId(gameId))
                return (false, "UnknownGame");

            if (!string.Equals(gameId, report.GameId, StringComparison.Ordinal))
                return (false, "GameIdMismatch");

            // Guard 1: Wonder_Pop is always ineligible (Ordinal, first after the GameIdMismatch guard)
            if (string.Equals(gameId, "Wonder_Pop", StringComparison.Ordinal))
                return (false, "WonderPopCrashProhibited");

            // Guard 2: Crash reporting disabled by policy
            if (!policy.AllowCrashReporting)
                return (false, "CrashReportingDisabled");

            // Guard 3: Symbolication required but report is not symbolicated
            if (policy.RequireSymbolication && !report.IsSymbolicated)
                return (false, "SymbolicationRequired");

            return (true, string.Empty);
        }

        public static bool IsFatalReport(CrashReport report) => report.IsFatal;
        public static bool RequiresUpload(CrashReport report) => report.RequiresUpload;
    }

    public sealed class CrashLog
    {
        private readonly List<(string gameId, CrashSeverity severity)> _entries =
            new List<(string, CrashSeverity)>();

        public int Count => _entries.Count;

        public void Record(string gameId, CrashSeverity severity)
        {
            if (string.IsNullOrEmpty(gameId))
                throw new ArgumentException("GameId must not be null or empty.", nameof(gameId));
            _entries.Add((gameId, severity));
        }

        public bool HasEntry(string gameId) =>
            _entries.Exists(e => string.Equals(e.gameId, gameId, StringComparison.Ordinal));

        public void Clear() => _entries.Clear();
    }
}
