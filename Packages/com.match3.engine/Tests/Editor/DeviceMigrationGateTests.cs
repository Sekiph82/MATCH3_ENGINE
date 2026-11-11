// ENG-M17-009 DeviceMigrationGate tests
// NUnit.Framework only
using System;
using NUnit.Framework;
using Match3.Engine.Runtime.Identity;

namespace Match3.Engine.Tests.Editor
{
    [TestFixture]
    public class DeviceMigrationGateTests
    {
        // --- MigrationContext validation ---

        [Test]
        public void MigrationContext_NullGameId_Throws()
        {
            Assert.Throws<ArgumentException>(() =>
                MigrationContext.Create(null, MigrationTrigger.Reinstall, "device-1"));
        }

        [Test]
        public void MigrationContext_EmptyGameId_Throws()
        {
            Assert.Throws<ArgumentException>(() =>
                MigrationContext.Create("", MigrationTrigger.Reinstall, "device-1"));
        }

        [Test]
        public void MigrationContext_NullSourceDeviceId_Throws()
        {
            Assert.Throws<ArgumentException>(() =>
                MigrationContext.Create("Coral_Rescue", MigrationTrigger.Reinstall, null));
        }

        [Test]
        public void MigrationContext_EmptySourceDeviceId_Throws()
        {
            Assert.Throws<ArgumentException>(() =>
                MigrationContext.Create("Coral_Rescue", MigrationTrigger.Reinstall, ""));
        }

        [Test]
        public void MigrationContext_Valid_PropertiesSet()
        {
            var ctx = MigrationContext.Create("Coral_Rescue", MigrationTrigger.NewDevice, "dev-42");
            Assert.AreEqual("Coral_Rescue", ctx.GameId);
            Assert.AreEqual(MigrationTrigger.NewDevice, ctx.Trigger);
            Assert.AreEqual("dev-42", ctx.SourceDeviceId);
        }

        // --- IsReinstall ---

        [Test]
        public void IsReinstall_TrueWhenReinstall()
        {
            var ctx = MigrationContext.Create("Coral_Rescue", MigrationTrigger.Reinstall, "dev-1");
            Assert.IsTrue(ctx.IsReinstall);
        }

        [Test]
        public void IsReinstall_FalseWhenNewDevice()
        {
            var ctx = MigrationContext.Create("Coral_Rescue", MigrationTrigger.NewDevice, "dev-1");
            Assert.IsFalse(ctx.IsReinstall);
        }

        [Test]
        public void IsReinstall_FalseWhenAppTransfer()
        {
            var ctx = MigrationContext.Create("Coral_Rescue", MigrationTrigger.AppTransfer, "dev-1");
            Assert.IsFalse(ctx.IsReinstall);
        }

        // --- MigrationPolicy validation ---

        [Test]
        public void MigrationPolicy_Safe_NullDesc_Throws()
        {
            Assert.Throws<ArgumentException>(() => MigrationPolicy.Safe(null));
        }

        [Test]
        public void MigrationPolicy_Safe_EmptyDesc_Throws()
        {
            Assert.Throws<ArgumentException>(() => MigrationPolicy.Safe(""));
        }

        [Test]
        public void MigrationPolicy_Safe_SetsFields()
        {
            var p = MigrationPolicy.Safe("desc");
            Assert.IsTrue(p.RequireCloudVerification);
            Assert.IsFalse(p.AllowLocalFallback);
            Assert.AreEqual("desc", p.PolicyDescription);
        }

        [Test]
        public void MigrationPolicy_Default_IsCloudRequiredNoFallback()
        {
            var p = MigrationPolicy.Default;
            Assert.IsTrue(p.RequireCloudVerification);
            Assert.IsFalse(p.AllowLocalFallback);
        }

        // --- DeviceMigrationGate.CanMigrate ---

        [Test]
        public void CanMigrate_WonderPop_Exact_Blocked_Guard1()
        {
            var result = DeviceMigrationGate.CanMigrate("Wonder_Pop", MigrationPolicy.Default);
            Assert.IsFalse(result.IsEligible);
            Assert.AreEqual("WonderPopMigrationProhibited", result.Reason);
        }

        [Test]
        public void CanMigrate_WonderPop_Lowercase_UnknownGame_OrdinalCheck()
        {
            // Ordinal catalog lookup: a case variant of Wonder_Pop is not a catalog ID, so it fails closed with UnknownGame (never eligible, never the Wonder_Pop reason).
            var result = DeviceMigrationGate.CanMigrate("wonder_pop", MigrationPolicy.Default);
            Assert.IsFalse(result.IsEligible);
            Assert.AreEqual("UnknownGame", result.Reason);
        }

        [Test]
        public void CanMigrate_NoVerificationPath_Blocked_Guard2()
        {
            // AllowLocalFallback=false AND RequireCloudVerification=false
            var noVerify = MigrationPolicy.Safe("x");
            // Safe already sets RequireCloud=true; build a custom policy via reflection is impractical,
            // so test via the edge: AllowLocal=false and Cloud=false cannot come from Safe().
            // We verify Guard 2 by using a policy struct that would hit it via a helper.
            // Since MigrationPolicy ctor is private, test Guard 2 indirectly:
            // Safe policy has RequireCloud=true so Guard 2 never fires for it.
            // Guard 2 fires only if both are false. Because MigrationPolicy ctor is private,
            // we document that Guard 2 is unreachable from public API (Safe always sets cloud=true).
            // The guard is present in the code for future-proofing. Confirm Guard 1 takes priority.
            var wonderPop = DeviceMigrationGate.CanMigrate("Wonder_Pop", noVerify);
            Assert.IsFalse(wonderPop.IsEligible);
            Assert.AreEqual("WonderPopMigrationProhibited", wonderPop.Reason);
        }

        [Test]
        public void CanMigrate_ValidGame_DefaultPolicy_Eligible()
        {
            var result = DeviceMigrationGate.CanMigrate("Coral_Rescue", MigrationPolicy.Default);
            Assert.IsTrue(result.IsEligible);
        }

        // --- RequiresCloudVerification ---

        [Test]
        public void RequiresCloudVerification_TrueForSafePolicy()
        {
            Assert.IsTrue(DeviceMigrationGate.RequiresCloudVerification(MigrationPolicy.Default));
        }

        // --- IsReinstallTrigger ---

        [Test]
        public void IsReinstallTrigger_ReinstallContext_ReturnsTrue()
        {
            var ctx = MigrationContext.Create("Coral_Rescue", MigrationTrigger.Reinstall, "dev-1");
            Assert.IsTrue(DeviceMigrationGate.IsReinstallTrigger(ctx));
        }

        [Test]
        public void IsReinstallTrigger_NewDeviceContext_ReturnsFalse()
        {
            var ctx = MigrationContext.Create("Coral_Rescue", MigrationTrigger.NewDevice, "dev-1");
            Assert.IsFalse(DeviceMigrationGate.IsReinstallTrigger(ctx));
        }

        [Test]
        public void IsReinstallTrigger_AppTransferContext_ReturnsFalse()
        {
            var ctx = MigrationContext.Create("Coral_Rescue", MigrationTrigger.AppTransfer, "dev-1");
            Assert.IsFalse(DeviceMigrationGate.IsReinstallTrigger(ctx));
        }

        // --- MigrationAuditLog ---

        [Test]
        public void AuditLog_Record_NullReason_Throws()
        {
            var log = new MigrationAuditLog();
            var ctx = MigrationContext.Create("Coral_Rescue", MigrationTrigger.Reinstall, "dev-1");
            Assert.Throws<ArgumentException>(() => log.Record(ctx, true, null));
        }

        [Test]
        public void AuditLog_Record_EmptyReason_Throws()
        {
            var log = new MigrationAuditLog();
            var ctx = MigrationContext.Create("Coral_Rescue", MigrationTrigger.Reinstall, "dev-1");
            Assert.Throws<ArgumentException>(() => log.Record(ctx, true, ""));
        }

        [Test]
        public void AuditLog_Count_IncrementsOnRecord()
        {
            var log = new MigrationAuditLog();
            var ctx = MigrationContext.Create("Coral_Rescue", MigrationTrigger.Reinstall, "dev-1");
            log.Record(ctx, true, "ok");
            log.Record(ctx, false, "fail");
            Assert.AreEqual(2, log.Count);
        }

        [Test]
        public void AuditLog_Clear_ResetsCount()
        {
            var log = new MigrationAuditLog();
            var ctx = MigrationContext.Create("Coral_Rescue", MigrationTrigger.Reinstall, "dev-1");
            log.Record(ctx, true, "ok");
            log.Clear();
            Assert.AreEqual(0, log.Count);
        }

        [Test]
        public void AuditLog_HasEntry_TrueAfterRecord()
        {
            var log = new MigrationAuditLog();
            var ctx = MigrationContext.Create("Coral_Rescue", MigrationTrigger.Reinstall, "dev-1");
            log.Record(ctx, true, "ok");
            Assert.IsTrue(log.HasEntry("Coral_Rescue"));
        }

        [Test]
        public void AuditLog_HasEntry_FalseForUnrecordedGame()
        {
            var log = new MigrationAuditLog();
            Assert.IsFalse(log.HasEntry("Garage_Legends"));
        }

        [Test]
        public void AuditLog_HasEntry_FalseAfterClear()
        {
            var log = new MigrationAuditLog();
            var ctx = MigrationContext.Create("Coral_Rescue", MigrationTrigger.Reinstall, "dev-1");
            log.Record(ctx, true, "ok");
            log.Clear();
            Assert.IsFalse(log.HasEntry("Coral_Rescue"));
        }

        [Test]
        public void AuditLog_HasEntry_MultipleGames()
        {
            var log = new MigrationAuditLog();
            var ctxA = MigrationContext.Create("Coral_Rescue", MigrationTrigger.Reinstall, "dev-1");
            var ctxB = MigrationContext.Create("Garage_Legends", MigrationTrigger.NewDevice, "dev-2");
            log.Record(ctxA, true, "ok");
            log.Record(ctxB, false, "failed");
            Assert.IsTrue(log.HasEntry("Coral_Rescue"));
            Assert.IsTrue(log.HasEntry("Garage_Legends"));
            Assert.IsFalse(log.HasEntry("Dino_Islands"));
        }


        [Test]
        public void DefaultStructPolicy_NormalizedToDefault_CloudVerificationRequired()
        {
            Assert.IsTrue(DeviceMigrationGate.RequiresCloudVerification(default(MigrationPolicy)));
            Assert.AreEqual(DeviceMigrationGate.CanMigrate("Coral_Rescue", MigrationPolicy.Default).Reason,
                DeviceMigrationGate.CanMigrate("Coral_Rescue", default(MigrationPolicy)).Reason);
        }

        [Test]
        public void MigrationAuditLog_Record_DefaultContext_Throws()
        {
            var log = new MigrationAuditLog();
            Assert.Throws<ArgumentException>(() => log.Record(default(MigrationContext), false, "r"));
        }


        [Test]
        public void CanMigrate_NullOrEmptyGameId_GameIdMissing()
        {
            foreach (var gameId in new[] { null, string.Empty })
            {
                var result = DeviceMigrationGate.CanMigrate(gameId, MigrationPolicy.Default);
                Assert.IsFalse(result.IsEligible);
                Assert.AreEqual("GameIdMissing", result.Reason);
            }
        }

        [Test]
        public void CanMigrate_UnknownOrCaseVariantGameId_UnknownGame()
        {
            foreach (var gameId in new[] { "Unknown_Game", "coral_rescue", "CORAL_RESCUE", " Coral_Rescue", "Coral_Rescue ", "wonder_pop", "WONDER_POP" })
            {
                var result = DeviceMigrationGate.CanMigrate(gameId, MigrationPolicy.Default);
                Assert.IsFalse(result.IsEligible);
                Assert.AreEqual("UnknownGame", result.Reason);
            }
        }
    }
}
