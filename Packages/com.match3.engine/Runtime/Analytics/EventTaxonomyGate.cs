// ENG-M18-001 EventTaxonomyGate stdlib contract
// stdlib only: System, System.Collections.Generic
using System;
using System.Collections.Generic;

namespace Match3.Engine.Runtime.Analytics
{
    public enum AnalyticsEvent
    {
        Install,
        SessionStart,
        SessionEnd,
        LevelStart,
        LevelFail,
        LevelWin,
        BoosterUse,
        ShopView,
        AdStart,
        AdComplete,
        IapStart,
        IapComplete,
        TutorialStep,
        Error
    }

    public readonly struct EventRecord
    {
        public string GameId { get; }
        public AnalyticsEvent Event { get; }
        public int SchemaVersion { get; }

        private EventRecord(string gameId, AnalyticsEvent analyticsEvent, int schemaVersion)
        {
            GameId = gameId;
            Event = analyticsEvent;
            SchemaVersion = schemaVersion;
        }

        public static EventRecord Create(string gameId, AnalyticsEvent analyticsEvent, int schemaVersion)
        {
            if (string.IsNullOrEmpty(gameId))
                throw new ArgumentException("GameId must not be null or empty.", nameof(gameId));
            if (schemaVersion < 1)
                throw new ArgumentOutOfRangeException(nameof(schemaVersion), "SchemaVersion must be >= 1.");
            return new EventRecord(gameId, analyticsEvent, schemaVersion);
        }

        public bool IsRevenueEvent =>
            Event == AnalyticsEvent.IapStart ||
            Event == AnalyticsEvent.IapComplete ||
            Event == AnalyticsEvent.AdStart ||
            Event == AnalyticsEvent.AdComplete;
    }

    public readonly struct EventTaxonomyPolicy
    {
        public int CurrentSchemaVersion { get; }
        public bool AllowRevenueEvents { get; }
        public string PolicyDescription { get; }

        public EventTaxonomyPolicy(int currentSchemaVersion, bool allowRevenueEvents, string policyDescription)
        {
            if (string.IsNullOrEmpty(policyDescription))
                throw new ArgumentException("PolicyDescription must not be null or empty.", nameof(policyDescription));
            if (currentSchemaVersion < 1)
                throw new ArgumentOutOfRangeException(nameof(currentSchemaVersion), "CurrentSchemaVersion must be >= 1.");
            CurrentSchemaVersion = currentSchemaVersion;
            AllowRevenueEvents = allowRevenueEvents;
            PolicyDescription = policyDescription;
        }

        public static EventTaxonomyPolicy V1(string desc)
        {
            return new EventTaxonomyPolicy(1, false, desc);
        }

        public static EventTaxonomyPolicy Default =>
            V1("Event taxonomy v1; revenue events disabled until monetization authorized");
    }

    public static class EventTaxonomyGate
    {
        private static EventTaxonomyPolicy Normalize(EventTaxonomyPolicy policy) =>
            policy.PolicyDescription == null ? EventTaxonomyPolicy.Default : policy;

        private static (bool eligible, string reason) Ineligible(string reason) =>
            (false, reason);

        private static (bool eligible, string reason) Eligible() =>
            (true, string.Empty);

        public static (bool eligible, string reason) CanRecordEvent(
            string gameId, EventRecord record, EventTaxonomyPolicy policy)
        {
            policy = Normalize(policy);

            if (!string.Equals(gameId, record.GameId, StringComparison.Ordinal))
                return (false, "GameIdMismatch");

            // Guard 1 (after GameIdMismatch): Wonder_Pop analytics are always hard-blocked (Ordinal)
            if (string.Equals(gameId, "Wonder_Pop", StringComparison.Ordinal))
                return Ineligible("WonderPopAnalyticsProhibited");

            // Guard 2: revenue events require policy allowance
            if (record.IsRevenueEvent && !policy.AllowRevenueEvents)
                return Ineligible("RevenueEventsDisabled");

            // Guard 3: schema version must not exceed policy version
            if (record.SchemaVersion > policy.CurrentSchemaVersion)
                return Ineligible("SchemaMismatch");

            return Eligible();
        }

        public static bool IsSchemaCompatible(EventRecord record, EventTaxonomyPolicy policy) =>
            record.SchemaVersion <= Normalize(policy).CurrentSchemaVersion;

        public static bool IsRevenueEvent(EventRecord record) =>
            record.IsRevenueEvent;
    }

    public sealed class EventRegistry
    {
        private readonly Dictionary<(string gameId, AnalyticsEvent analyticsEvent), EventRecord> _records =
            new Dictionary<(string, AnalyticsEvent), EventRecord>();

        public int Count => _records.Count;

        public void Register(EventRecord record)
        {
            var key = (record.GameId, record.Event);
            if (_records.ContainsKey(key))
                throw new ArgumentException(
                    $"Duplicate registration: GameId='{record.GameId}' Event='{record.Event}'.");
            _records[key] = record;
        }

        public void Clear() => _records.Clear();

        public bool Contains(string gameId, AnalyticsEvent analyticsEvent) =>
            _records.ContainsKey((gameId, analyticsEvent));
    }
}
