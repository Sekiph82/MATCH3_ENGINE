// ENG-M18-006 RevenueTelemetryGate stdlib contract tests
using System;
using NUnit.Framework;
using Match3.Engine.Runtime.Analytics;

namespace Match3.Engine.Tests.Editor
{
    [TestFixture]
    public class RevenueTelemetryGateTests
    {
        // ---- RevenueEvent ArgumentException ----

        [Test]
        public void RevenueEvent_NullGameId_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() => RevenueEvent.Create(null, RevenueType.IapPurchase, true));
        }

        [Test]
        public void RevenueEvent_EmptyGameId_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() => RevenueEvent.Create("", RevenueType.AdImpression, false));
        }

        // ---- RevenueTelemetryPolicy ArgumentException ----

        [Test]
        public void Policy_NullDescription_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() => RevenueTelemetryPolicy.Safe(null));
        }

        [Test]
        public void Policy_EmptyDescription_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() => RevenueTelemetryPolicy.Safe(""));
        }

        // ---- Create factory and IsAdRevenue/IsIapRevenue for all 4 types ----

        [Test]
        public void IapPurchase_IsIapRevenue_True_IsAdRevenue_False()
        {
            var ev = RevenueEvent.Create("GameA", RevenueType.IapPurchase, true);
            Assert.IsTrue(ev.IsIapRevenue);
            Assert.IsFalse(ev.IsAdRevenue);
        }

        [Test]
        public void IapRestore_IsIapRevenue_True_IsAdRevenue_False()
        {
            var ev = RevenueEvent.Create("GameA", RevenueType.IapRestore, false);
            Assert.IsTrue(ev.IsIapRevenue);
            Assert.IsFalse(ev.IsAdRevenue);
        }

        [Test]
        public void AdImpression_IsAdRevenue_True_IsIapRevenue_False()
        {
            var ev = RevenueEvent.Create("GameA", RevenueType.AdImpression, false);
            Assert.IsTrue(ev.IsAdRevenue);
            Assert.IsFalse(ev.IsIapRevenue);
        }

        [Test]
        public void AdReward_IsAdRevenue_True_IsIapRevenue_False()
        {
            var ev = RevenueEvent.Create("GameA", RevenueType.AdReward, false);
            Assert.IsTrue(ev.IsAdRevenue);
            Assert.IsFalse(ev.IsIapRevenue);
        }

        // ---- Wonder_Pop blocked by Guard 1 (Ordinal) ----

        [Test]
        public void WonderPop_ExactCase_IsIneligible_WonderPopRevenueProhibited()
        {
            var ev = RevenueEvent.Create("Wonder_Pop", RevenueType.AdImpression, false);
            var policy = RevenueTelemetryPolicy.Allow("all allowed");
            var (eligible, reason) = RevenueTelemetryGate.CanRecordRevenue("Wonder_Pop", ev, policy);
            Assert.IsFalse(eligible);
            Assert.AreEqual("WonderPopRevenueProhibited", reason);
        }

        [Test]
        public void WonderPop_ExactCase_IapType_IsIneligible_Guard1()
        {
            var ev = RevenueEvent.Create("Wonder_Pop", RevenueType.IapPurchase, true);
            var policy = RevenueTelemetryPolicy.Allow("all allowed");
            var (eligible, reason) = RevenueTelemetryGate.CanRecordRevenue("Wonder_Pop", ev, policy);
            Assert.IsFalse(eligible);
            Assert.AreEqual("WonderPopRevenueProhibited", reason);
        }

        [Test]
        public void WonderPop_Lowercase_NotBlockedByGuard1()
        {
            // "wonder_pop" is not "Wonder_Pop" under Ordinal comparison; Guard 1 must not fire
            var ev = RevenueEvent.Create("wonder_pop", RevenueType.IapPurchase, true);
            var policy = RevenueTelemetryPolicy.Allow("all allowed");
            var (eligible, reason) = RevenueTelemetryGate.CanRecordRevenue("wonder_pop", ev, policy);
            Assert.AreNotEqual("WonderPopRevenueProhibited", reason);
        }

        // ---- AdRevenueTelemetryDisabled guard (Guard 2) ----

        [Test]
        public void AdImpression_PolicyDisablesAd_AdRevenueTelemetryDisabled()
        {
            var ev = RevenueEvent.Create("GameB", RevenueType.AdImpression, false);
            var policy = RevenueTelemetryPolicy.Safe("both off");
            var (eligible, reason) = RevenueTelemetryGate.CanRecordRevenue("GameB", ev, policy);
            Assert.IsFalse(eligible);
            Assert.AreEqual("AdRevenueTelemetryDisabled", reason);
        }

        [Test]
        public void AdReward_PolicyDisablesAd_AdRevenueTelemetryDisabled()
        {
            var ev = RevenueEvent.Create("GameB", RevenueType.AdReward, false);
            var policy = RevenueTelemetryPolicy.Safe("both off");
            var (eligible, reason) = RevenueTelemetryGate.CanRecordRevenue("GameB", ev, policy);
            Assert.IsFalse(eligible);
            Assert.AreEqual("AdRevenueTelemetryDisabled", reason);
        }

        // ---- IapTelemetryDisabled guard (Guard 3) ----

        [Test]
        public void IapPurchase_PolicyDisablesIap_IapTelemetryDisabled()
        {
            var ev = RevenueEvent.Create("GameC", RevenueType.IapPurchase, true);
            var policy = RevenueTelemetryPolicy.Safe("both off");
            var (eligible, reason) = RevenueTelemetryGate.CanRecordRevenue("GameC", ev, policy);
            Assert.IsFalse(eligible);
            Assert.AreEqual("IapTelemetryDisabled", reason);
        }

        [Test]
        public void IapRestore_PolicyDisablesIap_IapTelemetryDisabled()
        {
            var ev = RevenueEvent.Create("GameC", RevenueType.IapRestore, false);
            var policy = RevenueTelemetryPolicy.Safe("both off");
            var (eligible, reason) = RevenueTelemetryGate.CanRecordRevenue("GameC", ev, policy);
            Assert.IsFalse(eligible);
            Assert.AreEqual("IapTelemetryDisabled", reason);
        }

        // ---- When both disabled, Ad fires Guard 2 not Guard 3 ----

        [Test]
        public void BothDisabled_AdEvent_Guard2FiresNotGuard3()
        {
            var ev = RevenueEvent.Create("GameD", RevenueType.AdImpression, false);
            var policy = RevenueTelemetryPolicy.Safe("both off");
            var (eligible, reason) = RevenueTelemetryGate.CanRecordRevenue("GameD", ev, policy);
            Assert.IsFalse(eligible);
            Assert.AreEqual("AdRevenueTelemetryDisabled", reason);
            Assert.AreNotEqual("IapTelemetryDisabled", reason);
        }

        // ---- IsAdEvent and IsIapEvent helpers ----

        [Test]
        public void IsAdEvent_AdImpression_ReturnsTrue()
        {
            var ev = RevenueEvent.Create("GameE", RevenueType.AdImpression, false);
            Assert.IsTrue(RevenueTelemetryGate.IsAdEvent(ev));
        }

        [Test]
        public void IsAdEvent_IapPurchase_ReturnsFalse()
        {
            var ev = RevenueEvent.Create("GameE", RevenueType.IapPurchase, true);
            Assert.IsFalse(RevenueTelemetryGate.IsAdEvent(ev));
        }

        [Test]
        public void IsIapEvent_IapRestore_ReturnsTrue()
        {
            var ev = RevenueEvent.Create("GameE", RevenueType.IapRestore, false);
            Assert.IsTrue(RevenueTelemetryGate.IsIapEvent(ev));
        }

        [Test]
        public void IsIapEvent_AdReward_ReturnsFalse()
        {
            var ev = RevenueEvent.Create("GameE", RevenueType.AdReward, false);
            Assert.IsFalse(RevenueTelemetryGate.IsIapEvent(ev));
        }

        // ---- Safe() and Default values correct ----

        [Test]
        public void Safe_BothFlagsAreFalse()
        {
            var p = RevenueTelemetryPolicy.Safe("some desc");
            Assert.IsFalse(p.AllowAdRevenueTelemetry);
            Assert.IsFalse(p.AllowIapTelemetry);
        }

        [Test]
        public void Default_BothFlagsAreFalse()
        {
            Assert.IsFalse(RevenueTelemetryPolicy.Default.AllowAdRevenueTelemetry);
            Assert.IsFalse(RevenueTelemetryPolicy.Default.AllowIapTelemetry);
        }

        // ---- Eligible when both allowed and non-Wonder_Pop ----

        [Test]
        public void BothAllowed_NonWonderPop_AdEvent_Eligible()
        {
            var ev = RevenueEvent.Create("GameF", RevenueType.AdImpression, false);
            var policy = RevenueTelemetryPolicy.Allow("fully enabled");
            var (eligible, reason) = RevenueTelemetryGate.CanRecordRevenue("GameF", ev, policy);
            Assert.IsTrue(eligible);
            Assert.AreEqual(string.Empty, reason);
        }

        [Test]
        public void BothAllowed_NonWonderPop_IapEvent_Eligible()
        {
            var ev = RevenueEvent.Create("GameF", RevenueType.IapPurchase, true);
            var policy = RevenueTelemetryPolicy.Allow("fully enabled");
            var (eligible, reason) = RevenueTelemetryGate.CanRecordRevenue("GameF", ev, policy);
            Assert.IsTrue(eligible);
            Assert.AreEqual(string.Empty, reason);
        }

        // ---- RevenueAuditLog ----

        [Test]
        public void AuditLog_NullGameId_ThrowsArgumentException()
        {
            var log = new RevenueAuditLog();
            Assert.Throws<ArgumentException>(() => log.Record(null, RevenueType.AdImpression));
        }

        [Test]
        public void AuditLog_EmptyGameId_ThrowsArgumentException()
        {
            var log = new RevenueAuditLog();
            Assert.Throws<ArgumentException>(() => log.Record("", RevenueType.IapRestore));
        }

        [Test]
        public void AuditLog_Record_IncreasesCount()
        {
            var log = new RevenueAuditLog();
            log.Record("GameG", RevenueType.AdImpression);
            Assert.AreEqual(1, log.Count);
        }

        [Test]
        public void AuditLog_HasEntry_True_AfterRecord()
        {
            var log = new RevenueAuditLog();
            log.Record("GameH", RevenueType.IapPurchase);
            Assert.IsTrue(log.HasEntry("GameH"));
        }

        [Test]
        public void AuditLog_HasEntry_False_ForUnrecorded()
        {
            var log = new RevenueAuditLog();
            Assert.IsFalse(log.HasEntry("NotRecorded"));
        }

        [Test]
        public void AuditLog_Clear_ResetsCount()
        {
            var log = new RevenueAuditLog();
            log.Record("GameI", RevenueType.AdReward);
            log.Record("GameI", RevenueType.IapRestore);
            log.Clear();
            Assert.AreEqual(0, log.Count);
        }

        [Test]
        public void AuditLog_Clear_HasEntry_False_AfterClear()
        {
            var log = new RevenueAuditLog();
            log.Record("GameJ", RevenueType.AdImpression);
            log.Clear();
            Assert.IsFalse(log.HasEntry("GameJ"));
        }


        [Test]
        public void DefaultStructPolicy_FailsClosed()
        {
            var (e1, expected) = RevenueTelemetryGate.CanRecordRevenue("GameA", RevenueEvent.Create("GameA", RevenueType.AdImpression, true), RevenueTelemetryPolicy.Default);
            var (e2, reason) = RevenueTelemetryGate.CanRecordRevenue("GameA", RevenueEvent.Create("GameA", RevenueType.AdImpression, true), default(RevenueTelemetryPolicy));
            Assert.IsFalse(e1);
            Assert.IsFalse(e2);
            Assert.AreEqual(expected, reason);
            Assert.AreEqual("AdRevenueTelemetryDisabled", reason);
        }

        [Test]
        public void GameIdMismatch_Blocked()
        {
            var (eligible, reason) = RevenueTelemetryGate.CanRecordRevenue("Coral_Rescue", RevenueEvent.Create("Wonder_Pop", RevenueType.IapPurchase, true), RevenueTelemetryPolicy.Default);
            Assert.IsFalse(eligible);
            Assert.AreEqual("GameIdMismatch", reason);
        }

        [Test]
        public void WonderPopParam_RecordDiffers_GameIdMismatch()
        {
            var (eligible, reason) = RevenueTelemetryGate.CanRecordRevenue("Wonder_Pop", RevenueEvent.Create("Coral_Rescue", RevenueType.IapPurchase, true), RevenueTelemetryPolicy.Default);
            Assert.IsFalse(eligible);
            Assert.AreEqual("GameIdMismatch", reason);
        }
    }
}
