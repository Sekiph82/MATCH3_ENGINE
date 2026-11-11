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
            var r = CrashReport.Create("Coral_Rescue", CrashSeverity.Error, true);
            Assert.AreEqual("Coral_Rescue", r.GameId);
            Assert.AreEqual(CrashSeverity.Error, r.Severity);
            Assert.IsTrue(r.IsSymbolicated);
        }

        // --- IsFatal ---

        [Test]
        public void CrashReport_IsFatal_Warning_False()
        {
            var r = CrashReport.Create("Coral_Rescue", CrashSeverity.Warning, true);
            Assert.IsFalse(r.IsFatal);
        }

        [Test]
        public void CrashReport_IsFatal_Error_False()
        {
            var r = CrashReport.Create("Coral_Rescue", CrashSeverity.Error, true);
            Assert.IsFalse(r.IsFatal);
        }

        [Test]
        public void CrashReport_IsFatal_Fatal_True()
        {
            var r = CrashReport.Create("Coral_Rescue", CrashSeverity.Fatal, true);
            Assert.IsTrue(r.IsFatal);
        }

        // --- RequiresUpload ---

        [Test]
        public void CrashReport_RequiresUpload_FatalSymbolicated_True()
        {
            var r = CrashReport.Create("Coral_Rescue", CrashSeverity.Fatal, true);
            Assert.IsTrue(r.RequiresUpload);
        }

        [Test]
        public void CrashReport_RequiresUpload_NonFatalNotSymbolicated_True()
        {
            var r = CrashReport.Create("Coral_Rescue", CrashSeverity.Warning, false);
            Assert.IsTrue(r.RequiresUpload);
        }

        [Test]
        public void CrashReport_RequiresUpload_NonFatalSymbolicated_False()
        {
            var r = CrashReport.Create("Coral_Rescue", CrashSeverity.Warning, true);
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
            // Ordinal catalog lookup: the case variant is not a catalog ID, so it fails closed with UnknownGame.
            Assert.IsFalse(eligible);
            Assert.AreEqual("UnknownGame", reason);
        }

        [Test]
        public void CanReportCrash_CrashReportingDisabled_Guard2()
        {
            var report = CrashReport.Create("Garage_Legends", CrashSeverity.Error, true);
            var policy = CrashTelemetryPolicy.Safe("disabled");
            var (eligible, reason) = CrashTelemetryGate.CanReportCrash("Garage_Legends", report, policy);
            Assert.IsFalse(eligible);
            Assert.AreEqual("CrashReportingDisabled", reason);
        }

        [Test]
        public void CanReportCrash_SymbolicationRequired_NotSymbolicated_Guard3()
        {
            var report = CrashReport.Create("Dino_Islands", CrashSeverity.Warning, false);
            var policy = CrashTelemetryPolicy.Allow("require sym");
            var (eligible, reason) = CrashTelemetryGate.CanReportCrash("Dino_Islands", report, policy);
            Assert.IsFalse(eligible);
            Assert.AreEqual("SymbolicationRequired", reason);
        }

        [Test]
        public void CanReportCrash_Eligible_WhenAllowedAndSymbolicated()
        {
            var report = CrashReport.Create("The_Fractured_Realms", CrashSeverity.Warning, true);
            var policy = CrashTelemetryPolicy.Allow("allowed");
            var (eligible, reason) = CrashTelemetryGate.CanReportCrash("The_Fractured_Realms", report, policy);
            Assert.IsTrue(eligible);
            Assert.AreEqual(string.Empty, reason);
        }

        // --- IsFatalReport and RequiresUpload helpers ---

        [Test]
        public void IsFatalReport_FatalSeverity_True()
        {
            var r = CrashReport.Create("The_Fractured_Realms", CrashSeverity.Fatal, true);
            Assert.IsTrue(CrashTelemetryGate.IsFatalReport(r));
        }

        [Test]
        public void IsFatalReport_ErrorSeverity_False()
        {
            var r = CrashReport.Create("The_Fractured_Realms", CrashSeverity.Error, true);
            Assert.IsFalse(CrashTelemetryGate.IsFatalReport(r));
        }

        [Test]
        public void RequiresUpload_FatalReport_True()
        {
            var r = CrashReport.Create("Dino_Islands", CrashSeverity.Fatal, true);
            Assert.IsTrue(CrashTelemetryGate.RequiresUpload(r));
        }

        [Test]
        public void RequiresUpload_NonFatalNotSymbolicated_True()
        {
            var r = CrashReport.Create("Dino_Islands", CrashSeverity.Warning, false);
            Assert.IsTrue(CrashTelemetryGate.RequiresUpload(r));
        }

        [Test]
        public void RequiresUpload_NonFatalSymbolicated_False()
        {
            var r = CrashReport.Create("Dino_Islands", CrashSeverity.Error, true);
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
            log.Record("Garage_Legends", CrashSeverity.Error);
            log.Record("Garage_Legends", CrashSeverity.Fatal);
            Assert.AreEqual(2, log.Count);
        }

        [Test]
        public void CrashLog_HasEntry_AfterRecord_True()
        {
            var log = new CrashLog();
            log.Record("Coral_Rescue", CrashSeverity.Warning);
            Assert.IsTrue(log.HasEntry("Coral_Rescue"));
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
            log.Record("The_Fractured_Realms", CrashSeverity.Error);
            log.Clear();
            Assert.AreEqual(0, log.Count);
        }

        [Test]
        public void CrashLog_HasEntry_AfterClear_False()
        {
            var log = new CrashLog();
            log.Record("Dino_Islands", CrashSeverity.Fatal);
            log.Clear();
            Assert.IsFalse(log.HasEntry("Dino_Islands"));
        }


        [Test]
        public void DefaultStructPolicy_FailsClosed()
        {
            var (e1, expected) = CrashTelemetryGate.CanReportCrash("Coral_Rescue", CrashReport.Create("Coral_Rescue", CrashSeverity.Fatal, true), CrashTelemetryPolicy.Default);
            var (e2, reason) = CrashTelemetryGate.CanReportCrash("Coral_Rescue", CrashReport.Create("Coral_Rescue", CrashSeverity.Fatal, true), default(CrashTelemetryPolicy));
            Assert.IsFalse(e1);
            Assert.IsFalse(e2);
            Assert.AreEqual(expected, reason);
            Assert.AreEqual("CrashReportingDisabled", reason);
        }

        [Test]
        public void GameIdMismatch_Blocked()
        {
            var (eligible, reason) = CrashTelemetryGate.CanReportCrash("Coral_Rescue", CrashReport.Create("Wonder_Pop", CrashSeverity.Fatal, true), CrashTelemetryPolicy.Default);
            Assert.IsFalse(eligible);
            Assert.AreEqual("GameIdMismatch", reason);
        }

        [Test]
        public void WonderPopParam_RecordDiffers_GameIdMismatch()
        {
            var (eligible, reason) = CrashTelemetryGate.CanReportCrash("Wonder_Pop", CrashReport.Create("Coral_Rescue", CrashSeverity.Fatal, true), CrashTelemetryPolicy.Default);
            Assert.IsFalse(eligible);
            Assert.AreEqual("GameIdMismatch", reason);
        }


        [Test]
        public void NullOrEmptyGameId_DefaultRecord_GameIdMissing()
        {
            foreach (var gameId in new[] { null, string.Empty })
            {
                var (eligible, reason) = CrashTelemetryGate.CanReportCrash(gameId, default(CrashReport), default(CrashTelemetryPolicy));
                Assert.IsFalse(eligible);
                Assert.AreEqual("GameIdMissing", reason);
            }
        }

        [Test]
        public void UnknownOrCaseVariantGameId_DefaultRecord_UnknownGame()
        {
            foreach (var gameId in new[] { "Unknown_Game", "coral_rescue", "CORAL_RESCUE", " Coral_Rescue", "Coral_Rescue ", "wonder_pop", "WONDER_POP" })
            {
                var (eligible, reason) = CrashTelemetryGate.CanReportCrash(gameId, default(CrashReport), default(CrashTelemetryPolicy));
                Assert.IsFalse(eligible);
                Assert.AreEqual("UnknownGame", reason);
            }
        }
    }
}
