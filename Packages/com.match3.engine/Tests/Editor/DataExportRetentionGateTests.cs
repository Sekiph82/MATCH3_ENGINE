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
            var r = DataRetentionRecord.Create("Game1", DataRetentionAction.ExportRequest, true, false);
            Assert.AreEqual("Game1", r.GameId);
            Assert.AreEqual(DataRetentionAction.ExportRequest, r.Action);
            Assert.IsTrue(r.IsUserInitiated);
            Assert.IsFalse(r.HasPendingPurge);
        }

        [Test]
        public void DataRetentionRecord_RequiresPurge_DeletionRequest_True()
        {
            var r = DataRetentionRecord.Create("G", DataRetentionAction.DeletionRequest, false, false);
            Assert.IsTrue(r.RequiresPurge);
        }

        [Test]
        public void DataRetentionRecord_RequiresPurge_BackupPurge_True()
        {
            var r = DataRetentionRecord.Create("G", DataRetentionAction.BackupPurge, false, false);
            Assert.IsTrue(r.RequiresPurge);
        }

        [Test]
        public void DataRetentionRecord_RequiresPurge_SdkDataPurge_True()
        {
            var r = DataRetentionRecord.Create("G", DataRetentionAction.SdkDataPurge, false, false);
            Assert.IsTrue(r.RequiresPurge);
        }

        [Test]
        public void DataRetentionRecord_RequiresPurge_ExportRequest_False()
        {
            var r = DataRetentionRecord.Create("G", DataRetentionAction.ExportRequest, false, false);
            Assert.IsFalse(r.RequiresPurge);
        }

        [Test]
        public void DataRetentionRecord_RequiresPurge_RetentionCheck_False()
        {
            var r = DataRetentionRecord.Create("G", DataRetentionAction.RetentionCheck, false, false);
            Assert.IsFalse(r.RequiresPurge);
        }

        [Test]
        public void DataRetentionRecord_IsAuditAction_AuditLog_True()
        {
            var r = DataRetentionRecord.Create("G", DataRetentionAction.AuditLog, false, false);
            Assert.IsTrue(r.IsAuditAction);
        }

        [Test]
        public void DataRetentionRecord_IsAuditAction_ExportRequest_False()
        {
            var r = DataRetentionRecord.Create("G", DataRetentionAction.ExportRequest, false, false);
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
        public void CanProcessDataAction_WonderPop_Lowercase_NotBlocked()
        {
            var record = DataRetentionRecord.Create("wonder_pop", DataRetentionAction.AuditLog, false, false);
            var policy = DataRetentionPolicy.Default;
            // Ordinal mismatch: "wonder_pop" != "Wonder_Pop"
            // AuditLog not RequiresPurge; AuditLog != RetentionCheck => eligible
            var result = DataExportRetentionGate.CanProcessDataAction("wonder_pop", record, policy, out string reason);
            Assert.IsTrue(result);
        }

        [Test]
        public void CanProcessDataAction_PurgeRequired_NoPendingPurge_Blocked()
        {
            var record = DataRetentionRecord.Create("Game1", DataRetentionAction.DeletionRequest, false, false);
            var policy = DataRetentionPolicy.Default;
            var result = DataExportRetentionGate.CanProcessDataAction("Game1", record, policy, out string reason);
            Assert.IsFalse(result);
            Assert.AreEqual("PurgeNotScheduled", reason);
        }

        [Test]
        public void CanProcessDataAction_PurgeRequired_HasPendingPurge_Eligible()
        {
            var record = DataRetentionRecord.Create("Game1", DataRetentionAction.DeletionRequest, false, true);
            var policy = DataRetentionPolicy.Default;
            var result = DataExportRetentionGate.CanProcessDataAction("Game1", record, policy, out string reason);
            Assert.IsTrue(result);
            Assert.AreEqual(string.Empty, reason);
        }

        [Test]
        public void CanProcessDataAction_RetentionCheck_NonUserInitiated_Blocked()
        {
            var record = DataRetentionRecord.Create("Game1", DataRetentionAction.RetentionCheck, false, false);
            var policy = DataRetentionPolicy.Default;
            var result = DataExportRetentionGate.CanProcessDataAction("Game1", record, policy, out string reason);
            Assert.IsFalse(result);
            Assert.AreEqual("RetentionCheckRequiresUserContext", reason);
        }

        [Test]
        public void CanProcessDataAction_RetentionCheck_UserInitiated_Eligible()
        {
            var record = DataRetentionRecord.Create("Game1", DataRetentionAction.RetentionCheck, true, false);
            var policy = DataRetentionPolicy.Default;
            var result = DataExportRetentionGate.CanProcessDataAction("Game1", record, policy, out string reason);
            Assert.IsTrue(result);
            Assert.AreEqual(string.Empty, reason);
        }

        [Test]
        public void CanProcessDataAction_ExportRequest_SafePolicy_Eligible()
        {
            var record = DataRetentionRecord.Create("Game1", DataRetentionAction.ExportRequest, true, false);
            var policy = DataRetentionPolicy.Default;
            var result = DataExportRetentionGate.CanProcessDataAction("Game1", record, policy, out string reason);
            Assert.IsTrue(result);
            Assert.AreEqual(string.Empty, reason);
        }

        // Helper method tests
        [Test]
        public void RequiresPurge_Helper_SdkDataPurge_True()
        {
            var r = DataRetentionRecord.Create("G", DataRetentionAction.SdkDataPurge, false, false);
            Assert.IsTrue(DataExportRetentionGate.RequiresPurge(r));
        }

        [Test]
        public void RequiresPurge_Helper_AuditLog_False()
        {
            var r = DataRetentionRecord.Create("G", DataRetentionAction.AuditLog, false, false);
            Assert.IsFalse(DataExportRetentionGate.RequiresPurge(r));
        }

        [Test]
        public void IsAuditAction_Helper_AuditLog_True()
        {
            var r = DataRetentionRecord.Create("G", DataRetentionAction.AuditLog, false, false);
            Assert.IsTrue(DataExportRetentionGate.IsAuditAction(r));
        }

        [Test]
        public void IsAuditAction_Helper_DeletionRequest_False()
        {
            var r = DataRetentionRecord.Create("G", DataRetentionAction.DeletionRequest, false, false);
            Assert.IsFalse(DataExportRetentionGate.IsAuditAction(r));
        }

        // DataRetentionLog tests
        [Test]
        public void DataRetentionLog_Record_And_Count()
        {
            var log = new DataRetentionLog();
            Assert.AreEqual(0, log.Count);
            log.Record("Game1", "ExportRequest");
            Assert.AreEqual(1, log.Count);
            log.Record("Game1", "DeletionRequest");
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
            log.Record("G", "X");
            log.Record("G", "Y");
            log.Clear();
            Assert.AreEqual(0, log.Count);
        }
    }
}
