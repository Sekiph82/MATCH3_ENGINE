using System;
using NUnit.Framework;
using Match3.Engine.Runtime.Privacy;

namespace Match3.Engine.Tests.Editor
{
    [TestFixture]
    public class DataExportRetentionGateTests
    {
        // DataRetentionRecord tests
        [Test]
        public void DataRetentionRecord_Create_NullGameId_Throws()
        {
            Assert.Throws<ArgumentException>(() =>
                DataRetentionRecord.Create(null, DataRetentionAction.ExportRequest, true, false));
        }

        [Test]
        public void DataRetentionRecord_Create_EmptyGameId_Throws()
        {
            Assert.Throws<ArgumentException>(() =>
                DataRetentionRecord.Create("", DataRetentionAction.ExportRequest, true, false));
        }

        [Test]
        public void DataRetentionRecord_Create_Valid_Succeeds()
        {
            var r = DataRetentionRecord.Create("Coral_Rescue", DataRetentionAction.ExportRequest, true, false);
            Assert.AreEqual("Coral_Rescue", r.GameId);
            Assert.AreEqual(DataRetentionAction.ExportRequest, r.Action);
            Assert.IsTrue(r.IsUserInitiated);
            Assert.IsFalse(r.HasPendingPurge);
        }

        [Test]
        public void DataRetentionRecord_RequiresPurge_DeletionRequest_True()
        {
            var r = DataRetentionRecord.Create("Coral_Rescue", DataRetentionAction.DeletionRequest, false, false);
            Assert.IsTrue(r.RequiresPurge);
        }

        [Test]
        public void DataRetentionRecord_RequiresPurge_BackupPurge_True()
        {
            var r = DataRetentionRecord.Create("Coral_Rescue", DataRetentionAction.BackupPurge, false, false);
            Assert.IsTrue(r.RequiresPurge);
        }

        [Test]
        public void DataRetentionRecord_RequiresPurge_SdkDataPurge_True()
        {
            var r = DataRetentionRecord.Create("Coral_Rescue", DataRetentionAction.SdkDataPurge, false, false);
            Assert.IsTrue(r.RequiresPurge);
        }

        [Test]
        public void DataRetentionRecord_RequiresPurge_ExportRequest_False()
        {
            var r = DataRetentionRecord.Create("Coral_Rescue", DataRetentionAction.ExportRequest, false, false);
            Assert.IsFalse(r.RequiresPurge);
        }

        [Test]
        public void DataRetentionRecord_RequiresPurge_RetentionCheck_False()
        {
            var r = DataRetentionRecord.Create("Coral_Rescue", DataRetentionAction.RetentionCheck, false, false);
            Assert.IsFalse(r.RequiresPurge);
        }

        [Test]
        public void DataRetentionRecord_IsAuditAction_AuditLog_True()
        {
            var r = DataRetentionRecord.Create("Coral_Rescue", DataRetentionAction.AuditLog, false, false);
            Assert.IsTrue(r.IsAuditAction);
        }

        [Test]
        public void DataRetentionRecord_IsAuditAction_ExportRequest_False()
        {
            var r = DataRetentionRecord.Create("Coral_Rescue", DataRetentionAction.ExportRequest, false, false);
            Assert.IsFalse(r.IsAuditAction);
        }

        // DataRetentionPolicy tests
        [Test]
        public void DataRetentionPolicy_Safe_Values_Correct()
        {
            var p = DataRetentionPolicy.Safe("Test policy");
            Assert.IsTrue(p.EnforceRetentionSchedule);
            Assert.IsTrue(p.HonorDeletionRequests);
            Assert.AreEqual("Test policy", p.PolicyDescription);
        }

        [Test]
        public void DataRetentionPolicy_Default_NotNull()
        {
            var p = DataRetentionPolicy.Default;
            Assert.IsNotNull(p.PolicyDescription);
            Assert.IsTrue(p.EnforceRetentionSchedule);
            Assert.IsTrue(p.HonorDeletionRequests);
        }

        [Test]
        public void DataRetentionPolicy_NullDescription_Throws()
        {
            Assert.Throws<ArgumentException>(() => DataRetentionPolicy.Safe(null));
        }

        // CanProcessDataAction tests
        [Test]
        public void CanProcessDataAction_WonderPop_AuditLog_NonUserInitiated_Blocked()
        {
            var record = DataRetentionRecord.Create("Wonder_Pop", DataRetentionAction.AuditLog, false, false);
            var policy = DataRetentionPolicy.Default;
            var result = DataExportRetentionGate.CanProcessDataAction("Wonder_Pop", record, policy, out string reason);
            Assert.IsFalse(result);
            Assert.AreEqual("WonderPopNonUserAuditProhibited", reason);
        }

        [Test]
        public void CanProcessDataAction_WonderPop_AuditLog_UserInitiated_NotBlocked()
        {
            var record = DataRetentionRecord.Create("Wonder_Pop", DataRetentionAction.AuditLog, true, false);
            var policy = DataRetentionPolicy.Default;
            var result = DataExportRetentionGate.CanProcessDataAction("Wonder_Pop", record, policy, out string reason);
            // Guard 1 not triggered; AuditLog not RequiresPurge; AuditLog != RetentionCheck => eligible
            Assert.IsTrue(result);
            Assert.AreEqual(string.Empty, reason);
        }

        [Test]
        public void CanProcessDataAction_WonderPop_Lowercase_UnknownGame()
        {
            // Ordinal catalog lookup: a case variant of Wonder_Pop is not a catalog ID, so it fails closed with UnknownGame (never eligible, never the Wonder_Pop reason).
            var record = DataRetentionRecord.Create("wonder_pop", DataRetentionAction.AuditLog, false, false);
            var policy = DataRetentionPolicy.Default;
            var result = DataExportRetentionGate.CanProcessDataAction("wonder_pop", record, policy, out string reason);
            Assert.IsFalse(result);
            Assert.AreEqual("UnknownGame", reason);
        }

        [Test]
        public void CanProcessDataAction_PurgeRequired_NoPendingPurge_Blocked()
        {
            var record = DataRetentionRecord.Create("Coral_Rescue", DataRetentionAction.DeletionRequest, false, false);
            var policy = DataRetentionPolicy.Default;
            var result = DataExportRetentionGate.CanProcessDataAction("Coral_Rescue", record, policy, out string reason);
            Assert.IsFalse(result);
            Assert.AreEqual("PurgeNotScheduled", reason);
        }

        [Test]
        public void CanProcessDataAction_PurgeRequired_HasPendingPurge_Eligible()
        {
            var record = DataRetentionRecord.Create("Coral_Rescue", DataRetentionAction.DeletionRequest, false, true);
            var policy = DataRetentionPolicy.Default;
            var result = DataExportRetentionGate.CanProcessDataAction("Coral_Rescue", record, policy, out string reason);
            Assert.IsTrue(result);
            Assert.AreEqual(string.Empty, reason);
        }

        [Test]
        public void CanProcessDataAction_RetentionCheck_NonUserInitiated_Blocked()
        {
            var record = DataRetentionRecord.Create("Coral_Rescue", DataRetentionAction.RetentionCheck, false, false);
            var policy = DataRetentionPolicy.Default;
            var result = DataExportRetentionGate.CanProcessDataAction("Coral_Rescue", record, policy, out string reason);
            Assert.IsFalse(result);
            Assert.AreEqual("RetentionCheckRequiresUserContext", reason);
        }

        [Test]
        public void CanProcessDataAction_RetentionCheck_UserInitiated_Eligible()
        {
            var record = DataRetentionRecord.Create("Coral_Rescue", DataRetentionAction.RetentionCheck, true, false);
            var policy = DataRetentionPolicy.Default;
            var result = DataExportRetentionGate.CanProcessDataAction("Coral_Rescue", record, policy, out string reason);
            Assert.IsTrue(result);
            Assert.AreEqual(string.Empty, reason);
        }

        [Test]
        public void CanProcessDataAction_ExportRequest_SafePolicy_Eligible()
        {
            var record = DataRetentionRecord.Create("Coral_Rescue", DataRetentionAction.ExportRequest, true, false);
            var policy = DataRetentionPolicy.Default;
            var result = DataExportRetentionGate.CanProcessDataAction("Coral_Rescue", record, policy, out string reason);
            Assert.IsTrue(result);
            Assert.AreEqual(string.Empty, reason);
        }

        // Helper method tests
        [Test]
        public void RequiresPurge_Helper_SdkDataPurge_True()
        {
            var r = DataRetentionRecord.Create("Coral_Rescue", DataRetentionAction.SdkDataPurge, false, false);
            Assert.IsTrue(DataExportRetentionGate.RequiresPurge(r));
        }

        [Test]
        public void RequiresPurge_Helper_AuditLog_False()
        {
            var r = DataRetentionRecord.Create("Coral_Rescue", DataRetentionAction.AuditLog, false, false);
            Assert.IsFalse(DataExportRetentionGate.RequiresPurge(r));
        }

        [Test]
        public void IsAuditAction_Helper_AuditLog_True()
        {
            var r = DataRetentionRecord.Create("Coral_Rescue", DataRetentionAction.AuditLog, false, false);
            Assert.IsTrue(DataExportRetentionGate.IsAuditAction(r));
        }

        [Test]
        public void IsAuditAction_Helper_DeletionRequest_False()
        {
            var r = DataRetentionRecord.Create("Coral_Rescue", DataRetentionAction.DeletionRequest, false, false);
            Assert.IsFalse(DataExportRetentionGate.IsAuditAction(r));
        }

        // DataRetentionLog tests
        [Test]
        public void DataRetentionLog_Record_And_Count()
        {
            var log = new DataRetentionLog();
            Assert.AreEqual(0, log.Count);
            log.Record("Coral_Rescue", "ExportRequest");
            Assert.AreEqual(1, log.Count);
            log.Record("Coral_Rescue", "DeletionRequest");
            Assert.AreEqual(2, log.Count);
        }

        [Test]
        public void DataRetentionLog_HasEntry_True()
        {
            var log = new DataRetentionLog();
            log.Record("Wonder_Pop", "AuditLog");
            Assert.IsTrue(log.HasEntry("Wonder_Pop"));
            Assert.IsFalse(log.HasEntry("Other"));
        }

        [Test]
        public void DataRetentionLog_Clear_ResetsCount()
        {
            var log = new DataRetentionLog();
            log.Record("Coral_Rescue", "X");
            log.Record("Coral_Rescue", "Y");
            log.Clear();
            Assert.AreEqual(0, log.Count);
        }

        // QA remediation: fail-closed default policy, GameId mismatch guard, log argument validation
        [Test]
        public void CanProcessDataAction_DefaultStructPolicy_FailsClosed()
        {
            var record = DataRetentionRecord.Create("Coral_Rescue", DataRetentionAction.DeletionRequest, true, false);
            var expected = DataExportRetentionGate.CanProcessDataAction("Coral_Rescue", record, DataRetentionPolicy.Default, out var expectedReason);
            var result = DataExportRetentionGate.CanProcessDataAction("Coral_Rescue", record, default(DataRetentionPolicy), out var reason);
            Assert.IsFalse(expected);
            Assert.IsFalse(result);
            Assert.AreEqual("PurgeNotScheduled", expectedReason);
            Assert.AreEqual(expectedReason, reason);
        }

        [Test]
        public void CanProcessDataAction_GameIdMismatch_Blocked()
        {
            var record = DataRetentionRecord.Create("Wonder_Pop", DataRetentionAction.AuditLog, false, false);
            var result = DataExportRetentionGate.CanProcessDataAction("Coral_Rescue", record, DataRetentionPolicy.Default, out var reason);
            Assert.IsFalse(result);
            Assert.AreEqual("GameIdMismatch", reason);
        }

        [Test]
        public void DataRetentionLog_Record_NullOrEmptySecondArg_Throws()
        {
            var log = new DataRetentionLog();
            Assert.Throws<System.ArgumentException>(() => log.Record("Coral_Rescue", null));
            Assert.Throws<System.ArgumentException>(() => log.Record("Coral_Rescue", string.Empty));
            Assert.AreEqual(0, log.Count);
        }

        [Test]
        public void DataRetentionLog_HasEntry_IsOrdinal()
        {
            var log = new DataRetentionLog();
            log.Record("Wonder_Pop", "evt");
            Assert.IsTrue(log.HasEntry("Wonder_Pop"));
            Assert.IsFalse(log.HasEntry("wonder_pop"));
            // Soft hyphen (U+00AD) is ignorable under culture-sensitive comparison; Ordinal must not match it.
            log.Record("Coral\u00AD_Rescue", "evt");
            Assert.IsTrue(log.HasEntry("Coral\u00AD_Rescue"));
            Assert.IsFalse(log.HasEntry("Coral_Rescue"));
        }


        [Test]
        public void NullOrEmptyGameId_DefaultRecord_GameIdMissing()
        {
            foreach (var gameId in new[] { null, string.Empty })
            {
                Assert.IsFalse(DataExportRetentionGate.CanProcessDataAction(gameId, default(DataRetentionRecord), default(DataRetentionPolicy), out var reason));
                Assert.AreEqual("GameIdMissing", reason);
            }
        }

        [Test]
        public void UnknownOrCaseVariantGameId_DefaultRecord_UnknownGame()
        {
            foreach (var gameId in new[] { "Unknown_Game", "coral_rescue", "CORAL_RESCUE", " Coral_Rescue", "Coral_Rescue ", "wonder_pop", "WONDER_POP" })
            {
                Assert.IsFalse(DataExportRetentionGate.CanProcessDataAction(gameId, default(DataRetentionRecord), default(DataRetentionPolicy), out var reason));
                Assert.AreEqual("UnknownGame", reason);
            }
        }
    }
}
