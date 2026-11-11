// ENG-M18-001 EventTaxonomyGate tests
using System;
using NUnit.Framework;
using Match3.Engine.Runtime.Analytics;

namespace Match3.Engine.Tests.Editor
{
    public sealed class EventTaxonomyGateTests
    {
        // --- EventRecord validation ---

        [Test]
        public void EventRecord_NullGameId_Throws()
        {
            Assert.Throws<ArgumentException>(() =>
                EventRecord.Create(null, AnalyticsEvent.SessionStart, 1));
        }

        [Test]
        public void EventRecord_EmptyGameId_Throws()
        {
            Assert.Throws<ArgumentException>(() =>
                EventRecord.Create(string.Empty, AnalyticsEvent.SessionStart, 1));
        }

        [Test]
        public void EventRecord_SchemaVersionZero_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                EventRecord.Create("Coral_Rescue", AnalyticsEvent.SessionStart, 0));
        }

        [Test]
        public void EventRecord_SchemaVersionNegative_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                EventRecord.Create("Coral_Rescue", AnalyticsEvent.SessionStart, -1));
        }

        [Test]
        public void EventRecord_ValidArgs_CreatesWithCorrectProperties()
        {
            var r = EventRecord.Create("Coral_Rescue", AnalyticsEvent.LevelWin, 1);
            Assert.AreEqual("Coral_Rescue", r.GameId);
            Assert.AreEqual(AnalyticsEvent.LevelWin, r.Event);
            Assert.AreEqual(1, r.SchemaVersion);
        }

        // --- EventRecord.IsRevenueEvent ---

        [Test]
        public void EventRecord_IsRevenueEvent_AdStart_True()
        {
            var r = EventRecord.Create("Coral_Rescue", AnalyticsEvent.AdStart, 1);
            Assert.IsTrue(r.IsRevenueEvent);
        }

        [Test]
        public void EventRecord_IsRevenueEvent_AdComplete_True()
        {
            var r = EventRecord.Create("Coral_Rescue", AnalyticsEvent.AdComplete, 1);
            Assert.IsTrue(r.IsRevenueEvent);
        }

        [Test]
        public void EventRecord_IsRevenueEvent_IapStart_True()
        {
            var r = EventRecord.Create("Coral_Rescue", AnalyticsEvent.IapStart, 1);
            Assert.IsTrue(r.IsRevenueEvent);
        }

        [Test]
        public void EventRecord_IsRevenueEvent_IapComplete_True()
        {
            var r = EventRecord.Create("Coral_Rescue", AnalyticsEvent.IapComplete, 1);
            Assert.IsTrue(r.IsRevenueEvent);
        }

        [Test]
        public void EventRecord_IsRevenueEvent_SessionStart_False()
        {
            var r = EventRecord.Create("Coral_Rescue", AnalyticsEvent.SessionStart, 1);
            Assert.IsFalse(r.IsRevenueEvent);
        }

        [Test]
        public void EventRecord_IsRevenueEvent_LevelWin_False()
        {
            var r = EventRecord.Create("Coral_Rescue", AnalyticsEvent.LevelWin, 1);
            Assert.IsFalse(r.IsRevenueEvent);
        }

        // --- EventTaxonomyPolicy validation ---

        [Test]
        public void Policy_NullDescription_Throws()
        {
            Assert.Throws<ArgumentException>(() =>
                new EventTaxonomyPolicy(1, false, null));
        }

        [Test]
        public void Policy_EmptyDescription_Throws()
        {
            Assert.Throws<ArgumentException>(() =>
                new EventTaxonomyPolicy(1, false, string.Empty));
        }

        [Test]
        public void Policy_SchemaVersionZero_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new EventTaxonomyPolicy(0, false, "desc"));
        }

        // --- EventTaxonomyGate.CanRecordEvent: Guard 1 Wonder_Pop ---

        [Test]
        public void CanRecordEvent_WonderPop_Exact_Blocked()
        {
            var record = EventRecord.Create("Wonder_Pop", AnalyticsEvent.SessionStart, 1);
            var policy = EventTaxonomyPolicy.Default;
            var (eligible, reason) = EventTaxonomyGate.CanRecordEvent("Wonder_Pop", record, policy);
            Assert.IsFalse(eligible);
            Assert.AreEqual("WonderPopAnalyticsProhibited", reason);
        }

        [Test]
        public void CanRecordEvent_WonderPop_Lowercase_UnknownGame()
        {
            // Ordinal catalog lookup: a case variant of Wonder_Pop is not a catalog ID, so it fails closed with UnknownGame (never eligible, never the Wonder_Pop reason).
            var record = EventRecord.Create("wonder_pop", AnalyticsEvent.SessionStart, 1);
            var policy = EventTaxonomyPolicy.Default;
            var (eligible, reason) = EventTaxonomyGate.CanRecordEvent("wonder_pop", record, policy);
            Assert.IsFalse(eligible);
            Assert.AreEqual("UnknownGame", reason);
        }

        // --- Guard 2: revenue events disabled ---

        [Test]
        public void CanRecordEvent_RevenueEvent_PolicyDisallows_Blocked()
        {
            var record = EventRecord.Create("Coral_Rescue", AnalyticsEvent.AdStart, 1);
            var policy = EventTaxonomyPolicy.Default; // AllowRevenueEvents = false
            var (eligible, reason) = EventTaxonomyGate.CanRecordEvent("Coral_Rescue", record, policy);
            Assert.IsFalse(eligible);
            Assert.AreEqual("RevenueEventsDisabled", reason);
        }

        [Test]
        public void CanRecordEvent_RevenueEvent_PolicyAllows_Eligible()
        {
            var record = EventRecord.Create("Coral_Rescue", AnalyticsEvent.IapComplete, 1);
            var policy = new EventTaxonomyPolicy(1, true, "Revenue allowed");
            var (eligible, _) = EventTaxonomyGate.CanRecordEvent("Coral_Rescue", record, policy);
            Assert.IsTrue(eligible);
        }

        // --- Guard 3: schema mismatch ---

        [Test]
        public void CanRecordEvent_SchemaMismatch_Blocked()
        {
            var record = EventRecord.Create("Coral_Rescue", AnalyticsEvent.LevelStart, 2);
            var policy = EventTaxonomyPolicy.Default; // CurrentSchemaVersion=1
            var (eligible, reason) = EventTaxonomyGate.CanRecordEvent("Coral_Rescue", record, policy);
            Assert.IsFalse(eligible);
            Assert.AreEqual("SchemaMismatch", reason);
        }

        [Test]
        public void CanRecordEvent_SchemaExact_Eligible()
        {
            var record = EventRecord.Create("Coral_Rescue", AnalyticsEvent.LevelStart, 1);
            var policy = EventTaxonomyPolicy.Default;
            var (eligible, _) = EventTaxonomyGate.CanRecordEvent("Coral_Rescue", record, policy);
            Assert.IsTrue(eligible);
        }

        // --- IsSchemaCompatible ---

        [Test]
        public void IsSchemaCompatible_Equal_True()
        {
            var record = EventRecord.Create("Coral_Rescue", AnalyticsEvent.Install, 1);
            var policy = new EventTaxonomyPolicy(1, false, "desc");
            Assert.IsTrue(EventTaxonomyGate.IsSchemaCompatible(record, policy));
        }

        [Test]
        public void IsSchemaCompatible_RecordBelow_True()
        {
            var record = EventRecord.Create("Coral_Rescue", AnalyticsEvent.Install, 1);
            var policy = new EventTaxonomyPolicy(2, false, "desc");
            Assert.IsTrue(EventTaxonomyGate.IsSchemaCompatible(record, policy));
        }

        [Test]
        public void IsSchemaCompatible_RecordAbove_False()
        {
            var record = EventRecord.Create("Coral_Rescue", AnalyticsEvent.Install, 3);
            var policy = new EventTaxonomyPolicy(2, false, "desc");
            Assert.IsFalse(EventTaxonomyGate.IsSchemaCompatible(record, policy));
        }

        // --- IsRevenueEvent via gate ---

        [Test]
        public void GateIsRevenueEvent_AdStart_True()
        {
            var record = EventRecord.Create("Coral_Rescue", AnalyticsEvent.AdStart, 1);
            Assert.IsTrue(EventTaxonomyGate.IsRevenueEvent(record));
        }

        [Test]
        public void GateIsRevenueEvent_BoosterUse_False()
        {
            var record = EventRecord.Create("Coral_Rescue", AnalyticsEvent.BoosterUse, 1);
            Assert.IsFalse(EventTaxonomyGate.IsRevenueEvent(record));
        }

        // --- EventRegistry ---

        [Test]
        public void Registry_Register_CountIncreases()
        {
            var reg = new EventRegistry();
            reg.Register(EventRecord.Create("Coral_Rescue", AnalyticsEvent.Install, 1));
            Assert.AreEqual(1, reg.Count);
        }

        [Test]
        public void Registry_Register_DefaultRecord_Throws()
        {
            var reg = new EventRegistry();
            Assert.Throws<ArgumentException>(() => reg.Register(default(EventRecord)));
            Assert.AreEqual(0, reg.Count);
        }

        [Test]
        public void Registry_Contains_ReturnsTrue()
        {
            var reg = new EventRegistry();
            reg.Register(EventRecord.Create("Coral_Rescue", AnalyticsEvent.Install, 1));
            Assert.IsTrue(reg.Contains("Coral_Rescue", AnalyticsEvent.Install));
        }

        [Test]
        public void Registry_Contains_DifferentGame_ReturnsFalse()
        {
            var reg = new EventRegistry();
            reg.Register(EventRecord.Create("Coral_Rescue", AnalyticsEvent.Install, 1));
            Assert.IsFalse(reg.Contains("Garage_Legends", AnalyticsEvent.Install));
        }

        [Test]
        public void Registry_Clear_ResetsCount()
        {
            var reg = new EventRegistry();
            reg.Register(EventRecord.Create("Coral_Rescue", AnalyticsEvent.Install, 1));
            reg.Clear();
            Assert.AreEqual(0, reg.Count);
        }

        [Test]
        public void Registry_DuplicateRegistration_Throws()
        {
            var reg = new EventRegistry();
            reg.Register(EventRecord.Create("Coral_Rescue", AnalyticsEvent.Install, 1));
            Assert.Throws<ArgumentException>(() =>
                reg.Register(EventRecord.Create("Coral_Rescue", AnalyticsEvent.Install, 1)));
        }

        [Test]
        public void Registry_SameGameDifferentEvent_AllowsBoth()
        {
            var reg = new EventRegistry();
            reg.Register(EventRecord.Create("Coral_Rescue", AnalyticsEvent.Install, 1));
            reg.Register(EventRecord.Create("Coral_Rescue", AnalyticsEvent.SessionStart, 1));
            Assert.AreEqual(2, reg.Count);
        }


        [Test]
        public void DefaultStructPolicy_FailsClosed()
        {
            var (e1, expected) = EventTaxonomyGate.CanRecordEvent("Coral_Rescue", EventRecord.Create("Coral_Rescue", AnalyticsEvent.IapStart, 1), EventTaxonomyPolicy.Default);
            var (e2, reason) = EventTaxonomyGate.CanRecordEvent("Coral_Rescue", EventRecord.Create("Coral_Rescue", AnalyticsEvent.IapStart, 1), default(EventTaxonomyPolicy));
            Assert.IsFalse(e1);
            Assert.IsFalse(e2);
            Assert.AreEqual(expected, reason);
            Assert.AreEqual("RevenueEventsDisabled", reason);
        }

        [Test]
        public void GameIdMismatch_Blocked()
        {
            var (eligible, reason) = EventTaxonomyGate.CanRecordEvent("Coral_Rescue", EventRecord.Create("Wonder_Pop", AnalyticsEvent.LevelStart, 1), EventTaxonomyPolicy.Default);
            Assert.IsFalse(eligible);
            Assert.AreEqual("GameIdMismatch", reason);
        }

        [Test]
        public void WonderPopParam_RecordDiffers_GameIdMismatch()
        {
            var (eligible, reason) = EventTaxonomyGate.CanRecordEvent("Wonder_Pop", EventRecord.Create("Coral_Rescue", AnalyticsEvent.LevelStart, 1), EventTaxonomyPolicy.Default);
            Assert.IsFalse(eligible);
            Assert.AreEqual("GameIdMismatch", reason);
        }


        [Test]
        public void NullOrEmptyGameId_DefaultRecord_GameIdMissing()
        {
            foreach (var gameId in new[] { null, string.Empty })
            {
                var (eligible, reason) = EventTaxonomyGate.CanRecordEvent(gameId, default(EventRecord), default(EventTaxonomyPolicy));
                Assert.IsFalse(eligible);
                Assert.AreEqual("GameIdMissing", reason);
            }
        }

        [Test]
        public void UnknownOrCaseVariantGameId_DefaultRecord_UnknownGame()
        {
            foreach (var gameId in new[] { "Unknown_Game", "coral_rescue", "CORAL_RESCUE", " Coral_Rescue", "Coral_Rescue ", "wonder_pop", "WONDER_POP" })
            {
                var (eligible, reason) = EventTaxonomyGate.CanRecordEvent(gameId, default(EventRecord), default(EventTaxonomyPolicy));
                Assert.IsFalse(eligible);
                Assert.AreEqual("UnknownGame", reason);
            }
        }
    }
}
