// ENG-M18-010 TelemetryDebugGate stdlib contract
// stdlib only: System, System.Collections.Generic
using System;
using System.Collections.Generic;

namespace Match3.Engine.Runtime.Analytics
{
    public enum DebugMode
    {
        Disabled,
        EventInspector,
        SchemaValidator
    }

    public readonly struct DebugSession
    {
        public string GameId { get; }
        public DebugMode Mode { get; }
        public bool IsTestDevice { get; }

        private DebugSession(string gameId, DebugMode mode, bool isTestDevice)
        {
            GameId = gameId;
            Mode = mode;
            IsTestDevice = isTestDevice;
        }

        public static DebugSession Create(string gameId, DebugMode mode, bool isTestDevice)
        {
            if (string.IsNullOrEmpty(gameId))
                throw new ArgumentException("GameId must not be null or empty.", nameof(gameId));
            return new DebugSession(gameId, mode, isTestDevice);
        }

        public bool IsActive => Mode != DebugMode.Disabled;

        public bool CanInspect => Mode == DebugMode.EventInspector || Mode == DebugMode.SchemaValidator;
    }

    public readonly struct DebugPolicy
    {
        public bool AllowDebugInProduction { get; }
        public bool RequireTestDeviceFlag { get; }
        public string PolicyDescription { get; }

        private DebugPolicy(bool allowDebugInProduction, bool requireTestDeviceFlag, string policyDescription)
        {
            AllowDebugInProduction = allowDebugInProduction;
            RequireTestDeviceFlag = requireTestDeviceFlag;
            PolicyDescription = policyDescription;
        }

        public static DebugPolicy Safe(string desc)
        {
            if (string.IsNullOrEmpty(desc))
                throw new ArgumentException("PolicyDescription must not be null or empty.", nameof(desc));
            return new DebugPolicy(false, true, desc);
        }

        public static readonly DebugPolicy Default = Safe("Debug tooling disabled in production; test device flag required");
    }

    public static class TelemetryDebugGate
    {
        public static (bool eligible, string reason) CanActivateDebug(string gameId, DebugSession session, DebugPolicy policy)
        {
            // Guard 1: Wonder_Pop ordinal check — structurally first
            if (string.Equals(gameId, "Wonder_Pop", StringComparison.Ordinal))
                return (false, "WonderPopDebugProhibited");

            // Guard 2: debug mode must be active
            if (!session.IsActive)
                return (false, "DebugModeDisabled");

            // Guard 3: production debug not allowed when no test device
            if (!policy.AllowDebugInProduction && !session.IsTestDevice)
                return (false, "ProductionDebugNotAllowed");

            // Guard 4: test device flag required
            if (policy.RequireTestDeviceFlag && !session.IsTestDevice)
                return (false, "TestDeviceFlagRequired");

            return (true, "eligible");
        }

        public static bool IsDebugActive(DebugSession session) => session.IsActive;

        public static bool CanInspectEvents(DebugSession session) => session.CanInspect;
    }

    public sealed class DebugEventLog
    {
        private readonly List<(string gameId, string eventName)> _entries = new List<(string, string)>();

        public int Count => _entries.Count;

        public void Record(string gameId, string eventName)
        {
            if (string.IsNullOrEmpty(gameId))
                throw new ArgumentException("gameId must not be null or empty.", nameof(gameId));
            if (string.IsNullOrEmpty(eventName))
                throw new ArgumentException("eventName must not be null or empty.", nameof(eventName));
            _entries.Add((gameId, eventName));
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
