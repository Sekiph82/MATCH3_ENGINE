using System;
using NUnit.Framework;
using Match3.Engine.Runtime.Commerce;

namespace Match3.Engine.Tests.Editor
{
    public sealed class AdTelemetryBusTests
    {
        // --- helpers ---

        private static ImpressionRevenueEvent MakeImpression(
            string placementId = "placement_1",
            string networkName = "AdMob",
            double revenueUsd = 0.01,
            DateTime? at = null) =>
            new ImpressionRevenueEvent(
                placementId, AdFormatKind.Rewarded, networkName, revenueUsd,
                at ?? new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc));

        private sealed class CountingImpressionObserver : IImpressionRevenueObserver
        {
            public int Count;
            public void OnImpression(ImpressionRevenueEvent impression) => Count++;
        }

        private sealed class CountingDiagnosticsObserver : IAdDiagnosticsObserver
        {
            public int Count;
            public void OnDiagnosticEvent(AdDiagnosticEvent diagnosticEvent) => Count++;
        }

        // --- ImpressionRevenueEvent ---

        [Test]
        public void ImpressionEvent_NegativeRevenue_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new ImpressionRevenueEvent("p1", AdFormatKind.Rewarded, "Net", -0.001,
                    DateTime.UtcNow));
        }

        [Test]
        public void ImpressionEvent_EmptyPlacementId_Throws()
        {
            Assert.Throws<ArgumentException>(() =>
                new ImpressionRevenueEvent("", AdFormatKind.Rewarded, "Net", 0.01,
                    DateTime.UtcNow));
        }

        [Test]
        public void ImpressionEvent_NullNetworkName_Throws()
        {
            Assert.Throws<ArgumentException>(() =>
                new ImpressionRevenueEvent("p1", AdFormatKind.Rewarded, null, 0.01,
                    DateTime.UtcNow));
        }

        // --- AdDiagnosticEvent ---

        [Test]
        public void DiagnosticEvent_EmptyEventName_Throws()
        {
            Assert.Throws<ArgumentException>(() =>
                new AdDiagnosticEvent("", "p1", null, DateTime.UtcNow));
        }

        [Test]
        public void DiagnosticEvent_EmptyPlacementId_Throws()
        {
            Assert.Throws<ArgumentException>(() =>
                new AdDiagnosticEvent("load_failed", "", null, DateTime.UtcNow));
        }

        [Test]
        public void DiagnosticEvent_NullDetail_IsAllowed()
        {
            var ev = new AdDiagnosticEvent("load_failed", "p1", null, DateTime.UtcNow);
            Assert.That(ev.Detail, Is.Null);
        }

        // --- AdTelemetryBus registration ---

        [Test]
        public void TelemetryBus_NullImpressionObserver_Throws()
        {
            var bus = new AdTelemetryBus();
            Assert.Throws<ArgumentNullException>(() =>
                bus.RegisterImpressionObserver(null));
        }

        [Test]
        public void TelemetryBus_NullDiagnosticsObserver_Throws()
        {
            var bus = new AdTelemetryBus();
            Assert.Throws<ArgumentNullException>(() =>
                bus.RegisterDiagnosticsObserver(null));
        }

        // --- Consent gating ---

        [Test]
        public void TelemetryBus_PublishImpression_ConsentDenied_NoObserverCalled()
        {
            var bus = new AdTelemetryBus();
            var obs = new CountingImpressionObserver();
            bus.RegisterImpressionObserver(obs);
            bus.PublishImpression(MakeImpression(), AdConsentStatus.Denied);
            Assert.That(obs.Count, Is.EqualTo(0));
        }

        [Test]
        public void TelemetryBus_PublishImpression_ConsentUnknown_NoObserverCalled()
        {
            var bus = new AdTelemetryBus();
            var obs = new CountingImpressionObserver();
            bus.RegisterImpressionObserver(obs);
            bus.PublishImpression(MakeImpression(), AdConsentStatus.Unknown);
            Assert.That(obs.Count, Is.EqualTo(0));
        }

        [Test]
        public void TelemetryBus_PublishImpression_ConsentGranted_ObserverCalled()
        {
            var bus = new AdTelemetryBus();
            var obs = new CountingImpressionObserver();
            bus.RegisterImpressionObserver(obs);
            bus.PublishImpression(MakeImpression(), AdConsentStatus.Granted);
            Assert.That(obs.Count, Is.EqualTo(1));
        }

        [Test]
        public void TelemetryBus_PublishImpression_ConsentNotRequired_ObserverCalled()
        {
            var bus = new AdTelemetryBus();
            var obs = new CountingImpressionObserver();
            bus.RegisterImpressionObserver(obs);
            bus.PublishImpression(MakeImpression(), AdConsentStatus.NotRequired);
            Assert.That(obs.Count, Is.EqualTo(1));
        }

        // --- Dedup ---

        [Test]
        public void TelemetryBus_PublishImpression_Dedup_SameSecond_ObserverCalledOnce()
        {
            var bus = new AdTelemetryBus();
            var obs = new CountingImpressionObserver();
            bus.RegisterImpressionObserver(obs);
            var t = new DateTime(2026, 6, 1, 10, 0, 0, DateTimeKind.Utc);
            // same placement, different sub-second offset — both still within same second
            bus.PublishImpression(MakeImpression(at: t), AdConsentStatus.Granted);
            bus.PublishImpression(MakeImpression(at: t.AddMilliseconds(500)), AdConsentStatus.Granted);
            Assert.That(obs.Count, Is.EqualTo(1));
        }

        [Test]
        public void TelemetryBus_PublishImpression_Dedup_DifferentSecond_ObserverCalledTwice()
        {
            var bus = new AdTelemetryBus();
            var obs = new CountingImpressionObserver();
            bus.RegisterImpressionObserver(obs);
            var t = new DateTime(2026, 6, 1, 10, 0, 0, DateTimeKind.Utc);
            bus.PublishImpression(MakeImpression(at: t), AdConsentStatus.Granted);
            bus.PublishImpression(MakeImpression(at: t.AddSeconds(1)), AdConsentStatus.Granted);
            Assert.That(obs.Count, Is.EqualTo(2));
        }

        // --- Diagnostics ---

        [Test]
        public void TelemetryBus_PublishDiagnostic_ObserverCalled()
        {
            var bus = new AdTelemetryBus();
            var obs = new CountingDiagnosticsObserver();
            bus.RegisterDiagnosticsObserver(obs);
            var ev = new AdDiagnosticEvent("load_failed", "p1", null, DateTime.UtcNow);
            bus.PublishDiagnosticEvent(ev);
            Assert.That(obs.Count, Is.EqualTo(1));
        }

        [Test]
        public void TelemetryBus_PublishDiagnostic_NoConsentGate()
        {
            // Diagnostics must fire regardless of consent (consent-scoped separately)
            var bus = new AdTelemetryBus();
            var obs = new CountingDiagnosticsObserver();
            bus.RegisterDiagnosticsObserver(obs);
            var ev = new AdDiagnosticEvent("timeout", "p1", "30s", DateTime.UtcNow);
            bus.PublishDiagnosticEvent(ev);
            Assert.That(obs.Count, Is.EqualTo(1));
        }
    }
}
