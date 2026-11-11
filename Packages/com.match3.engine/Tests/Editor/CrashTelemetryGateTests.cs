// ENG-M18-008 CrashTelemetryGate tests
using System;
using NUnit.Framework;
using Match3.Engine.Runtime.Analytics;

namespace Match3.Engine.Tests.Editor
{
    [TestFixture]
    public class CrashTelemetryGateTests
    {
        // --- CrashReport construction ---

        [Test]
        public void CrashReport_NullGameId_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() => CrashReport.Create(null, CrashSeverity.Warning, true));
        }

        [Test]
        public void CrashReport_EmptyGameId_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() => CrashReport.Create(string.Empty, CrashSeverity.Error, true));
        }

        [Test]
        public void CrashReport_Create_SetsProperties()
        {
            var r = CrashReport.Create("GameA", CrashSeverity.Error, true);
            Assert.AreEqual("GameA", r.GameId);
            Assert.AreEqual(CrashSeverity.Error, r.Severity);
            Assert.IsTrue(r.IsSymbolicated);
        }

        // --- IsFatal ---

        [Test]
        public void CrashReport_IsFatal_Warning_False()
        {
            var r = CrashReport.Create("GameA", CrashSeverity.Warning, true);
            Assert.IsFalse(r.IsFatal);
        }

        [Test]
        public void CrashReport_IsFatal_Error_False()
        {
            var r = CrashReport.Create("GameA", CrashSeverity.Error, true);
            Assert.IsFalse(r.IsFatal);
        }

        [Test]
        public void CrashReport_IsFatal_Fatal_True()
        {
            var r = CrashReport.Create("GameA", CrashSeverity.Fatal, true);
            Assert.IsTrue(r.IsFatal);
        }

        // --- RequiresUpload ---

        [Test]
        public void CrashReport_RequiresUpload_FatalSymbolicated_True()
        {
            var r = CrashReport.Create("GameA", CrashSeverity.Fatal, true);
            Assert.IsTrue(r.RequiresUpload);
        }

        [Test]
        public void CrashReport_RequiresUpload_NonFatalNotSymbolicated_True()
        {
            var r = CrashReport.Create("GameA", CrashSeverity.Warning, false);
            Assert.IsTrue(r.RequiresUpload);
        }

        [Test]
        public void CrashReport_RequiresUpload_NonFatalSymbolicated_False()
        {
            var r = CrashReport.Create("GameA", CrashSeverity.Warning, true);
            Assert.IsFalse(r.RequiresUpload);
        }

        // --- CrashTelemetryPolicy ---

        [Test]
        public void CrashTelemetryPolicy_Safe_NullDesc_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() => CrashTelemetryPolicy.Safe(null));
        }

        [Test]
        public void CrashTelemetryPolicy_Safe_EmptyDesc_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() => CrashTelemetryPolicy.Safe(string.Empty));
        }

        [Test]
        public void CrashTelemetryPolicy_Safe_AllowCrashReporting_False()
        {
            var p = CrashTelemetryPolicy.Safe("desc");
            Assert.IsFalse(p.AllowCrashReporting);
        }

        [Test]
        public void CrashTelemetryPolicy_Safe_RequireSymbolication_True()
        {
            var p = CrashTelemetryPolicy.Safe("desc");
            Assert.IsTrue(p.RequireSymbolication);
        }

        [Test]
        public void CrashTelemetryPolicy_Default_IsDisabledAndRequiresSymbolication()
        {
            var d = CrashTelemetryPolicy.Default;
            Assert.IsFalse(d.AllowCrashReporting);
            Assert.IsTrue(d.RequireSymbolication);
            Assert.IsFalse(string.IsNullOrEmpty(d.PolicyDescription));
        }

        // --- CrashTelemetryGate Guards ---

        [Test]
        public void CanReportCrash_WonderPop_Exact_Blocked_Guard1()
        {
            var report = CrashReport.Create("Wonder_Pop", CrashSeverity.Fatal, true);
            var policy = CrashTelemetryPolicy.Allow("allowed");
            var (eligible, reason) = CrashTelemetryGate.CanReportCrash("Wonder_Pop", report, policy);
            Assert.IsFalse(eligible);
            Assert.AreEqual("WonderPopCrashProhibited", reason);
        }

        [Test]
        public void CanReportCrash_WonderPop_Lowercase_NotBlocked_ByGuard1()
        {
            // "wonder_pop" (lowercase) must NOT be blocked by Guard 1 (Ordinal comparison)
            var report = CrashReport.Create("wonder_pop", CrashSeverity.Warning, true);
            var policy = CrashTelemetryPolicy.Allow("allowed");
            var (eligible, reason) = CrashTelemetryGate.CanReportCrash("wonder_pop", report, policy);
            Assert.AreNotEqual("WonderPopCrashProhibited", reason);
        }

        [Test]
        public void CanReportCrash_CrashReportingDisabled_Guard2()
        {
            var report = CrashReport.Create("GameB", CrashSeverity.Error, true);
            var policy = CrashTelemetryPolicy.Safe("disabled");
            var (eligible, reason) = CrashTelemetryGate.CanReportCrash("GameB", report, policy);
            Assert.IsFalse(eligible);
            Assert.AreEqual("CrashReportingDisabled", reason);
        }

        [Test]
        public void CanReportCrash_SymbolicationRequired_NotSymbolicated_Guard3()
        {
            var report = CrashReport.Create("GameC", CrashSeverity.Warning, false);
            var policy = CrashTelemetryPolicy.Allow("require sym");
            var (eligible, reason) = CrashTelemetryGate.CanReportCrash("GameC", report, policy);
            Assert.IsFalse(eligible);
            Assert.AreEqual("SymbolicationRequired", reason);
        }

        [Test]
        public void CanReportCrash_Eligible_WhenAllowedAndSymbolicated()
        {
            var report = CrashReport.Create("GameD", CrashSeverity.Warning, true);
            var policy = CrashTelemetryPolicy.Allow("allowed");
            var (eligible, reason) = CrashTelemetryGate.CanReportCrash("GameD", report, policy);
            Assert.IsTrue(eligible);
            Assert.AreEqual(string.Empty, reason);
        }

        // --- IsFatalReport and RequiresUpload helpers ---

        [Test]
        public void IsFatalReport_FatalSeverity_True()
        {
            var r = CrashReport.Create("GameE", CrashSeverity.Fatal, true);
            Assert.IsTrue(CrashTelemetryGate.IsFatalReport(r));
        }

        [Test]
        public void IsFatalReport_ErrorSeverity_False()
        {
            var r = CrashReport.Create("GameE", CrashSeverity.Error, true);
            Assert.IsFalse(CrashTelemetryGate.IsFatalReport(r));
        }

        [Test]
        public void RequiresUpload_FatalReport_True()
        {
            var r = CrashReport.Create("GameF", CrashSeverity.Fatal, true);
            Assert.IsTrue(CrashTelemetryGate.RequiresUpload(r));
        }

        [Test]
        public void RequiresUpload_NonFatalNotSymbolicated_True()
        {
            var r = CrashReport.Create("GameF", CrashSeverity.Warning, false);
            Assert.IsTrue(CrashTelemetryGate.RequiresUpload(r));
        }

        [Test]
        public void RequiresUpload_NonFatalSymbolicated_False()
        {
            var r = CrashReport.Create("GameF", CrashSeverity.Error, true);
            Assert.IsFalse(CrashTelemetryGate.RequiresUpload(r));
        }

        // --- CrashLog ---

        [Test]
        public void CrashLog_Record_NullGameId_ThrowsArgumentException()
        {
            var log = new CrashLog();
            Assert.Throws<ArgumentException>(() => log.Record(null, CrashSeverity.Warning));
        }

        [Test]
        public void CrashLog_Record_EmptyGameId_ThrowsArgumentException()
        {
            var log = new CrashLog();
            Assert.Throws<ArgumentException>(() => log.Record(string.Empty, CrashSeverity.Fatal));
        }

        [Test]
        public void CrashLog_Record_IncreasesCount()
        {
            var log = new CrashLog();
            log.Record("GameG", CrashSeverity.Error);
            log.Record("GameG", CrashSeverity.Fatal);
            Assert.AreEqual(2, log.Count);
        }

        [Test]
        public void CrashLog_HasEntry_AfterRecord_True()
        {
            var log = new CrashLog();
            log.Record("GameH", CrashSeverity.Warning);
            Assert.IsTrue(log.HasEntry("GameH"));
        }

        [Test]
        public void CrashLog_HasEntry_NotRecorded_False()
        {
            var log = new CrashLog();
            Assert.IsFalse(log.HasEntry("NotRecorded"));
        }

        [Test]
        public void CrashLog_Clear_ResetsCount()
        {
            var log = new CrashLog();
            log.Record("GameI", CrashSeverity.Error);
            log.Clear();
            Assert.AreEqual(0, log.Count);
        }

        [Test]
        public void CrashLog_HasEntry_AfterClear_False()
        {
            var log = new CrashLog();
            log.Record("GameJ", CrashSeverity.Fatal);
            log.Clear();
            Assert.IsFalse(log.HasEntry("GameJ"));
        }
    }
}
