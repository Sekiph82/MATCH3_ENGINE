// KPIDashboardGate.cs — stdlib only: System, System.Collections.Generic
// No Unity, no external SDK dependencies.
using System;
using System.Collections.Generic;

namespace Match3.Engine.Runtime.Analytics
{
    public enum DashboardScope { PerGame, Aggregate, BothPerGameAndAggregate }

    public readonly struct DashboardSpec
    {
        public string GameId { get; }
        public DashboardScope Scope { get; }
        public bool IncludeRevenueMetrics { get; }

        private DashboardSpec(string gameId, DashboardScope scope, bool includeRevenueMetrics)
        {
            if (string.IsNullOrEmpty(gameId))
                throw new ArgumentException("GameId must not be null or empty.", nameof(gameId));
            GameId = gameId;
            Scope = scope;
            IncludeRevenueMetrics = includeRevenueMetrics;
        }

        public static DashboardSpec Create(string gameId, DashboardScope scope, bool includeRevenue)
            => new DashboardSpec(gameId, scope, includeRevenue);

        public bool IsAggregate =>
            Scope == DashboardScope.Aggregate || Scope == DashboardScope.BothPerGameAndAggregate;

        public bool IsPerGame =>
            Scope == DashboardScope.PerGame || Scope == DashboardScope.BothPerGameAndAggregate;
    }

    public readonly struct DashboardPolicy
    {
        public bool AllowRevenueInDashboard { get; }
        public bool AllowAggregateDashboard { get; }
        public string PolicyDescription { get; }

        private DashboardPolicy(bool allowRevenue, bool allowAggregate, string policyDescription)
        {
            if (string.IsNullOrEmpty(policyDescription))
                throw new ArgumentException("PolicyDescription must not be null or empty.", nameof(policyDescription));
            AllowRevenueInDashboard = allowRevenue;
            AllowAggregateDashboard = allowAggregate;
            PolicyDescription = policyDescription;
        }

        public static DashboardPolicy Safe(string desc) => new DashboardPolicy(false, false, desc);

        public static DashboardPolicy Default =>
            Safe("Revenue and aggregate dashboard metrics disabled until authorized");
    }

    public static class KPIDashboardGate
    {
        private static DashboardPolicy Normalize(DashboardPolicy policy) =>
            policy.PolicyDescription == null ? DashboardPolicy.Default : policy;

        public static (bool eligible, string reason) CanPublishDashboard(
            string gameId, DashboardSpec spec, DashboardPolicy policy)
        {
            policy = Normalize(policy);

            if (!string.Equals(gameId, spec.GameId, StringComparison.Ordinal))
                return (false, "GameIdMismatch");

            // Guard 1 (after GameIdMismatch): Wonder_Pop blocked first after the GameIdMismatch guard
            if (string.Equals(gameId, "Wonder_Pop", StringComparison.Ordinal))
                return (false, "WonderPopDashboardProhibited");

            // Guard 2: revenue metrics require policy authorization
            if (spec.IncludeRevenueMetrics && !policy.AllowRevenueInDashboard)
                return (false, "RevenueDashboardDisabled");

            // Guard 3: aggregate scope requires policy authorization
            if (spec.IsAggregate && !policy.AllowAggregateDashboard)
                return (false, "AggregateDashboardDisabled");

            return (true, string.Empty);
        }

        public static bool IsAggregateScope(DashboardSpec spec) => spec.IsAggregate;

        public static bool IsPerGameScope(DashboardSpec spec) => spec.IsPerGame;
    }

    public sealed class DashboardLog
    {
        private readonly List<(string gameId, DashboardScope scope)> _entries =
            new List<(string, DashboardScope)>();

        public int Count => _entries.Count;

        public void Record(string gameId, DashboardScope scope)
        {
            if (string.IsNullOrEmpty(gameId))
                throw new ArgumentException("GameId must not be null or empty.", nameof(gameId));
            _entries.Add((gameId, scope));
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
