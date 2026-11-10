using System;
using System.Collections.Generic;

namespace Match3.Engine.Runtime.Commerce
{
    public readonly struct ImpressionRevenueEvent
    {
        public string PlacementId { get; }
        public AdFormatKind Format { get; }
        public string NetworkName { get; }
        public double RevenueUsd { get; }
        public DateTime OccurredAtUtc { get; }

        public ImpressionRevenueEvent(
            string placementId,
            AdFormatKind format,
            string networkName,
            double revenueUsd,
            DateTime occurredAtUtc)
        {
            if (string.IsNullOrEmpty(placementId))
                throw new ArgumentException("PlacementId must not be null or empty.", nameof(placementId));
            if (string.IsNullOrEmpty(networkName))
                throw new ArgumentException("NetworkName must not be null or empty.", nameof(networkName));
            if (revenueUsd < 0)
                throw new ArgumentOutOfRangeException(nameof(revenueUsd), "RevenueUsd must be >= 0.");

            PlacementId = placementId;
            Format = format;
            NetworkName = networkName;
            RevenueUsd = revenueUsd;
            OccurredAtUtc = occurredAtUtc;
        }
    }

    public readonly struct AdDiagnosticEvent
    {
        public string EventName { get; }
        public string PlacementId { get; }
        public string Detail { get; }
        public DateTime OccurredAtUtc { get; }

        public AdDiagnosticEvent(
            string eventName,
            string placementId,
            string detail,
            DateTime occurredAtUtc)
        {
            if (string.IsNullOrEmpty(eventName))
                throw new ArgumentException("EventName must not be null or empty.", nameof(eventName));
            if (string.IsNullOrEmpty(placementId))
                throw new ArgumentException("PlacementId must not be null or empty.", nameof(placementId));

            EventName = eventName;
            PlacementId = placementId;
            Detail = detail;
            OccurredAtUtc = occurredAtUtc;
        }
    }

    public interface IImpressionRevenueObserver
    {
        void OnImpression(ImpressionRevenueEvent impression);
    }

    public interface IAdDiagnosticsObserver
    {
        void OnDiagnosticEvent(AdDiagnosticEvent diagnosticEvent);
    }

    public sealed class AdTelemetryBus
    {
        private readonly List<IImpressionRevenueObserver> _impressionObservers = new List<IImpressionRevenueObserver>();
        private readonly List<IAdDiagnosticsObserver> _diagnosticsObservers = new List<IAdDiagnosticsObserver>();
        // ponytail: HashSet of string keys for dedup; key = placementId + truncated-to-second UTC tick. Per-session memory, not persisted.
        private readonly HashSet<string> _seenImpressions = new HashSet<string>(StringComparer.Ordinal);

        public AdTelemetryBus() { }

        public void RegisterImpressionObserver(IImpressionRevenueObserver observer)
        {
            if (observer == null)
                throw new ArgumentNullException(nameof(observer));
            _impressionObservers.Add(observer);
        }

        public void RegisterDiagnosticsObserver(IAdDiagnosticsObserver observer)
        {
            if (observer == null)
                throw new ArgumentNullException(nameof(observer));
            _diagnosticsObservers.Add(observer);
        }

        public void PublishImpression(ImpressionRevenueEvent impression, AdConsentStatus consent)
        {
            if (consent != AdConsentStatus.Granted && consent != AdConsentStatus.NotRequired)
                return;

            // Dedup: same placement within the same UTC second
            var second = new DateTime(
                impression.OccurredAtUtc.Year,
                impression.OccurredAtUtc.Month,
                impression.OccurredAtUtc.Day,
                impression.OccurredAtUtc.Hour,
                impression.OccurredAtUtc.Minute,
                impression.OccurredAtUtc.Second,
                DateTimeKind.Utc);
            var key = impression.PlacementId + "|" + second.Ticks.ToString();
            if (!_seenImpressions.Add(key))
                return;

            foreach (var obs in _impressionObservers)
                obs.OnImpression(impression);
        }

        public void PublishDiagnosticEvent(AdDiagnosticEvent diagnosticEvent)
        {
            foreach (var obs in _diagnosticsObservers)
                obs.OnDiagnosticEvent(diagnosticEvent);
        }
    }
}
