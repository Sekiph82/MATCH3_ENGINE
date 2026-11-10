using System;
using NUnit.Framework;
using Match3.Engine.Runtime.Commerce;

namespace Match3.Engine.Editor.Tests
{
    public class AdFrequencyGateTests
    {
        private static readonly DateTime T0 = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

        // --- AdFrequencyPolicy ---

        [Test]
        public void Policy_NegativeInterval_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new AdFrequencyPolicy(1, -1, 0));
        }

        [Test]
        public void Policy_ZeroMaxImpressions_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new AdFrequencyPolicy(0, 60, 0));
        }

        [Test]
        public void Policy_Default_Values()
        {
            var p = AdFrequencyPolicy.Default;
            Assert.AreEqual(5, p.MaxImpressionsPerSession);
            Assert.AreEqual(120, p.MinIntervalSeconds);
            Assert.AreEqual(30, p.AppOpenGraceSeconds);
        }

        // --- AdSessionTracker ---

        [Test]
        public void Tracker_StartsAtZero()
        {
            var t = new AdSessionTracker(AdFrequencyPolicy.Default);
            Assert.AreEqual(0, t.ImpressionsThisSession);
            Assert.IsNull(t.LastImpressionUtc);
        }

        [Test]
        public void Tracker_RecordImpression_Increments()
        {
            var t = new AdSessionTracker(AdFrequencyPolicy.Default);
            t.RecordImpression(T0);
            Assert.AreEqual(1, t.ImpressionsThisSession);
            Assert.AreEqual(T0, t.LastImpressionUtc);
        }

        [Test]
        public void Tracker_ResetSession_ClearsState()
        {
            var t = new AdSessionTracker(AdFrequencyPolicy.Default);
            t.RecordImpression(T0);
            t.ResetSession();
            Assert.AreEqual(0, t.ImpressionsThisSession);
            Assert.IsNull(t.LastImpressionUtc);
        }

        [Test]
        public void Tracker_IsAdAllowed_UnderCap_ReturnsTrue()
        {
            // policy: max=5, interval=120s, grace=30s
            var t = new AdSessionTracker(AdFrequencyPolicy.Default);
            var sessionStart = T0;
            var now = T0.AddSeconds(60); // past grace
            Assert.IsTrue(t.IsAdAllowed(now, sessionStart));
        }

        [Test]
        public void Tracker_IsAdAllowed_AtCap_ReturnsFalse()
        {
            var policy = new AdFrequencyPolicy(2, 0, 0);
            var t = new AdSessionTracker(policy);
            t.RecordImpression(T0);
            t.RecordImpression(T0.AddSeconds(1));
            Assert.IsFalse(t.IsAdAllowed(T0.AddSeconds(2), T0));
        }

        [Test]
        public void Tracker_IsAdAllowed_GracePeriod_ReturnsFalse()
        {
            var t = new AdSessionTracker(AdFrequencyPolicy.Default); // grace=30s
            var sessionStart = T0;
            var now = T0.AddSeconds(10); // still within grace
            Assert.IsFalse(t.IsAdAllowed(now, sessionStart));
        }

        [Test]
        public void Tracker_IsAdAllowed_IntervalNotElapsed_ReturnsFalse()
        {
            var policy = new AdFrequencyPolicy(5, 120, 0); // no grace, 120s interval
            var t = new AdSessionTracker(policy);
            t.RecordImpression(T0);
            var now = T0.AddSeconds(60); // only 60s elapsed
            Assert.IsFalse(t.IsAdAllowed(now, T0));
        }

        // --- AdPlacementBlocklist ---

        [Test]
        public void Blocklist_Block_IsBlocked_ReturnsTrue()
        {
            var bl = new AdPlacementBlocklist();
            bl.Block("mid_board");
            Assert.IsTrue(bl.IsBlocked("mid_board"));
        }

        [Test]
        public void Blocklist_UnknownId_ReturnsFalse()
        {
            var bl = new AdPlacementBlocklist();
            Assert.IsFalse(bl.IsBlocked("some_placement"));
        }

        [Test]
        public void Blocklist_NullOrEmpty_Throws()
        {
            var bl = new AdPlacementBlocklist();
            Assert.Throws<ArgumentException>(() => bl.Block(null));
            Assert.Throws<ArgumentException>(() => bl.Block(string.Empty));
        }

        // --- AdFrequencyGate ---

        [Test]
        public void FrequencyGate_BlockedPlacement_Ineligible()
        {
            var tracker = new AdSessionTracker(AdFrequencyPolicy.Default);
            var blocklist = new AdPlacementBlocklist();
            blocklist.Block("mid_board");

            var result = AdFrequencyGate.Check(tracker, "mid_board", blocklist, T0.AddSeconds(60), T0);
            Assert.IsFalse(result.IsEligible);
            Assert.AreEqual("PlacementBlocked", result.Reason);
        }

        [Test]
        public void FrequencyGate_FrequencyCapped_Ineligible()
        {
            var policy = new AdFrequencyPolicy(1, 0, 0);
            var tracker = new AdSessionTracker(policy);
            tracker.RecordImpression(T0);
            var blocklist = new AdPlacementBlocklist();

            var result = AdFrequencyGate.Check(tracker, "post_level", blocklist, T0.AddSeconds(1), T0);
            Assert.IsFalse(result.IsEligible);
            Assert.AreEqual("FrequencyCapReached", result.Reason);
        }

        [Test]
        public void FrequencyGate_AllClear_Eligible()
        {
            var policy = new AdFrequencyPolicy(5, 0, 0);
            var tracker = new AdSessionTracker(policy);
            var blocklist = new AdPlacementBlocklist();

            var result = AdFrequencyGate.Check(tracker, "post_level", blocklist, T0.AddSeconds(1), T0);
            Assert.IsTrue(result.IsEligible);
        }
    }
}
