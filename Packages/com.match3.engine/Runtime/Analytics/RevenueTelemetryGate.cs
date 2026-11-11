// ENG-M18-006 RevenueTelemetryGate stdlib contract
// stdlib only: System, System.Collections.Generic
using System;
using System.Collections.Generic;

namespace Match3.Engine.Runtime.Analytics
{
    public enum RevenueType
    {
        IapPurchase,
        IapRestore,
        AdImpression,
        AdReward
    }

    public readonly struct RevenueEvent
    {
        public string GameId { get; }
        public RevenueType Type { get; }
        public bool IsGross { get; }

        private RevenueEvent(string gameId, RevenueType type, bool isGross)
        {
            GameId = gameId;
            Type = type;
            IsGross = isGross;
        }

        public static RevenueEvent Create(string gameId, RevenueType type, bool isGross)
        {
            if (string.IsNullOrEmpty(gameId))
                throw new ArgumentException("GameId must not be null or empty.", nameof(gameId));
            return new RevenueEvent(gameId, type, isGross);
        }

        public bool IsAdRevenue => Type == RevenueType.AdImpression || Type == RevenueType.AdReward;
        public bool IsIapRevenue => Type == RevenueType.IapPurchase || Type == RevenueType.IapRestore;
    }

    public readonly struct RevenueTelemetryPolicy
    {
        public bool AllowAdRevenueTelemetry { get; }
        public bool AllowIapTelemetry { get; }
        public string PolicyDescription { get; }

        private RevenueTelemetryPolicy(bool allowAd, bool allowIap, string description)
        {
            AllowAdRevenueTelemetry = allowAd;
            AllowIapTelemetry = allowIap;
            PolicyDescription = description;
        }

        private static void ValidateDescription(string desc)
        {
            if (string.IsNullOrEmpty(desc))
                throw new ArgumentException("PolicyDescription must not be null or empty.", nameof(desc));
        }

        public static RevenueTelemetryPolicy Safe(string desc)
        {
            ValidateDescription(desc);
            return new RevenueTelemetryPolicy(false, false, desc);
        }

        public static RevenueTelemetryPolicy Allow(string desc)
        {
            ValidateDescription(desc);
            return new RevenueTelemetryPolicy(true, true, desc);
        }

        public static readonly RevenueTelemetryPolicy Default =
            Safe("Ad and IAP revenue telemetry disabled until monetization authorized");
    }

    public static class RevenueTelemetryGate
    {
        public static (bool eligible, string reason) CanRecordRevenue(
            string gameId, RevenueEvent revenueEvent, RevenueTelemetryPolicy policy)
        {
            // Guard 1: Wonder_Pop is always ineligible (Ordinal, structurally first)
            if (string.Equals(gameId, "Wonder_Pop", StringComparison.Ordinal))
                return (false, "WonderPopRevenueProhibited");

            // Guard 2: Ad revenue blocked by policy
            if (revenueEvent.IsAdRevenue && !policy.AllowAdRevenueTelemetry)
                return (false, "AdRevenueTelemetryDisabled");

            // Guard 3: IAP blocked by policy
            if (revenueEvent.IsIapRevenue && !policy.AllowIapTelemetry)
                return (false, "IapTelemetryDisabled");

            return (true, string.Empty);
        }

        public static bool IsAdEvent(RevenueEvent revenueEvent) => revenueEvent.IsAdRevenue;
        public static bool IsIapEvent(RevenueEvent revenueEvent) => revenueEvent.IsIapRevenue;
    }

    public sealed class RevenueAuditLog
    {
        private readonly List<(string gameId, RevenueType type)> _entries =
            new List<(string, RevenueType)>();

        public int Count => _entries.Count;

        public void Record(string gameId, RevenueType type)
        {
            if (string.IsNullOrEmpty(gameId))
                throw new ArgumentException("GameId must not be null or empty.", nameof(gameId));
            _entries.Add((gameId, type));
        }

        public bool HasEntry(string gameId) =>
            _entries.Exists(e => string.Equals(e.gameId, gameId, StringComparison.Ordinal));

        public void Clear() => _entries.Clear();
    }
}
