using System;
using NUnit.Framework;
using Match3.Engine.Runtime.LiveOps;

namespace Match3.Engine.Tests.Editor
{
    public class FeatureFlagGateTests
    {
        private static bool Eval(string gameId, LiveOpsFeature feature, bool enabled, bool consent, FeatureFlagPolicy policy, out string reason)
        {
            var record = FeatureFlagRecord.Create(gameId, feature, 1, enabled, consent);
            return FeatureFlagGate.CanEnableFlag(gameId, record, policy, out reason);
        }

        // FeatureFlagRecord

        [Test]
        public void FeatureFlagRecord_Create_NullGameId_Throws()
        {
            Assert.Throws<ArgumentException>(() =>
                FeatureFlagRecord.Create(null, LiveOpsFeature.TimedMode, 1, true, false));
        }

        [Test]
        public void FeatureFlagRecord_Create_EmptyGameId_Throws()
        {
            Assert.Throws<ArgumentException>(() =>
                FeatureFlagRecord.Create("", LiveOpsFeature.TimedMode, 1, true, false));
        }

        [Test]
        public void FeatureFlagRecord_Create_FlagVersionZero_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                FeatureFlagRecord.Create("Coral_Rescue", LiveOpsFeature.TimedMode, 0, true, false));
        }

        [Test]
        public void FeatureFlagRecord_Create_FlagVersionNegative_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                FeatureFlagRecord.Create("Coral_Rescue", LiveOpsFeature.TimedMode, -1, true, false));
        }

        [Test]
        public void FeatureFlagRecord_Create_Valid_Succeeds()
        {
            var r = FeatureFlagRecord.Create("Coral_Rescue", LiveOpsFeature.TimedMode, 1, true, false);
            Assert.AreEqual("Coral_Rescue", r.GameId);
            Assert.AreEqual(LiveOpsFeature.TimedMode, r.Feature);
            Assert.AreEqual(1, r.FlagVersion);
            Assert.IsTrue(r.IsEnabled);
            Assert.IsFalse(r.HasConsentCheck);
        }

        [Test]
        public void FeatureFlagRecord_IsRestrictedFeature_TournamentMode_True()
        {
            Assert.IsTrue(FeatureFlagRecord.Create("Coral_Rescue", LiveOpsFeature.TournamentMode, 1, true, true).IsRestrictedFeature);
        }

        [Test]
        public void FeatureFlagRecord_IsRestrictedFeature_WeeklyPersonalBest_True()
        {
            Assert.IsTrue(FeatureFlagRecord.Create("Coral_Rescue", LiveOpsFeature.WeeklyPersonalBest, 1, true, true).IsRestrictedFeature);
        }

        [Test]
        public void FeatureFlagRecord_IsRestrictedFeature_TimedMode_False()
        {
            Assert.IsFalse(FeatureFlagRecord.Create("Coral_Rescue", LiveOpsFeature.TimedMode, 1, true, false).IsRestrictedFeature);
        }

        [Test]
        public void FeatureFlagRecord_IsDefaultOff_Disabled_True()
        {
            Assert.IsTrue(FeatureFlagRecord.Create("Coral_Rescue", LiveOpsFeature.TimedMode, 1, false, false).IsDefaultOff);
        }

        [Test]
        public void FeatureFlagRecord_IsDefaultOff_RestrictedEnabled_True()
        {
            Assert.IsTrue(FeatureFlagRecord.Create("Coral_Rescue", LiveOpsFeature.TournamentMode, 1, true, true).IsDefaultOff);
        }

        [Test]
        public void FeatureFlagRecord_IsDefaultOff_NonRestrictedEnabled_False()
        {
            Assert.IsFalse(FeatureFlagRecord.Create("Coral_Rescue", LiveOpsFeature.TimedMode, 1, true, false).IsDefaultOff);
        }

        // FeatureFlagPolicy

        [Test]
        public void FeatureFlagPolicy_Safe_Values_Correct()
        {
            var p = FeatureFlagPolicy.Safe("test desc");
            Assert.IsTrue(p.BlockRestrictedWithoutConsent);
            Assert.AreEqual("test desc", p.PolicyDescription);
        }

        [Test]
        public void FeatureFlagPolicy_Permissive_Values_Correct()
        {
            var p = FeatureFlagPolicy.Permissive("relaxed");
            Assert.IsFalse(p.BlockRestrictedWithoutConsent);
            Assert.AreEqual("relaxed", p.PolicyDescription);
        }

        [Test]
        public void FeatureFlagPolicy_Default_NotNull()
        {
            var p = FeatureFlagPolicy.Default;
            Assert.IsFalse(string.IsNullOrEmpty(p.PolicyDescription));
            Assert.IsTrue(p.BlockRestrictedWithoutConsent);
        }

        [Test]
        public void FeatureFlagPolicy_NullDescription_Throws()
        {
            Assert.Throws<ArgumentException>(() => FeatureFlagPolicy.Safe(null));
        }

        [Test]
        public void FeatureFlagPolicy_EmptyDescription_Throws()
        {
            Assert.Throws<ArgumentException>(() => FeatureFlagPolicy.Safe(""));
        }

        [Test]
        public void FeatureFlagPolicy_Permissive_NullDescription_Throws()
        {
            Assert.Throws<ArgumentException>(() => FeatureFlagPolicy.Permissive(null));
        }

        [Test]
        public void FeatureFlagPolicy_Permissive_EmptyDescription_Throws()
        {
            Assert.Throws<ArgumentException>(() => FeatureFlagPolicy.Permissive(""));
        }

        // CanEnableFlag

        [Test]
        public void CanEnableFlag_GameIdMismatch_Blocked()
        {
            var record = FeatureFlagRecord.Create("Wonder_Pop", LiveOpsFeature.TournamentMode, 1, true, true);
            Assert.IsFalse(FeatureFlagGate.CanEnableFlag("Coral_Rescue", record, FeatureFlagPolicy.Default, out string reason));
            Assert.AreEqual("GameIdMismatch", reason);
        }

        [Test]
        public void CanEnableFlag_DefaultStructPolicy_FailsClosed()
        {
            Assert.IsFalse(Eval("Garage_Legends", LiveOpsFeature.TournamentMode, true, false, default(FeatureFlagPolicy), out string reason));
            Assert.AreEqual("RestrictedFeatureRequiresConsent", reason);
        }

        [Test]
        public void CanEnableFlag_WonderPop_TournamentMode_AlwaysBlocked_Guard1()
        {
            Assert.IsFalse(Eval("Wonder_Pop", LiveOpsFeature.TournamentMode, true, true, FeatureFlagPolicy.Default, out string reason));
            Assert.AreEqual("WonderPopTournamentProhibited", reason);
        }

        [Test]
        public void CanEnableFlag_WonderPop_TournamentMode_Disabled_StillBlockedByGuard1()
        {
            Assert.IsFalse(Eval("Wonder_Pop", LiveOpsFeature.TournamentMode, false, false, FeatureFlagPolicy.Default, out string reason));
            Assert.AreEqual("WonderPopTournamentProhibited", reason);
        }

        [Test]
        public void CanEnableFlag_WonderPop_TournamentMode_Permissive_StillBlocked()
        {
            Assert.IsFalse(Eval("Wonder_Pop", LiveOpsFeature.TournamentMode, true, false, FeatureFlagPolicy.Permissive("relaxed"), out string reason));
            Assert.AreEqual("WonderPopTournamentProhibited", reason);
        }

        [Test]
        public void CanEnableFlag_WonderPop_EndlessMode_Enabled_HasConsent_Eligible()
        {
            Assert.IsTrue(Eval("Wonder_Pop", LiveOpsFeature.EndlessMode, true, true, FeatureFlagPolicy.Default, out string reason));
            Assert.AreEqual(string.Empty, reason);
        }

        [Test]
        public void CanEnableFlag_WonderPop_WeeklyPersonalBest_Enabled_NoConsent_Blocked()
        {
            Assert.IsFalse(Eval("Wonder_Pop", LiveOpsFeature.WeeklyPersonalBest, true, false, FeatureFlagPolicy.Default, out string reason));
            Assert.AreEqual("RestrictedFeatureRequiresConsent", reason);
        }

        [Test]
        public void CanEnableFlag_WonderPop_WeeklyPersonalBest_Permissive_NoConsent_StillBlocked()
        {
            Assert.IsFalse(Eval("Wonder_Pop", LiveOpsFeature.WeeklyPersonalBest, true, false, FeatureFlagPolicy.Permissive("relaxed"), out string reason));
            Assert.AreEqual("RestrictedFeatureRequiresConsent", reason);
        }

        [Test]
        public void CanEnableFlag_WonderPop_Lowercase_Tournament_UnknownGame()
        {
            // Ordinal catalog lookup: a case variant of Wonder_Pop is not a catalog ID, so it fails closed with UnknownGame (never eligible, never the Wonder_Pop reason).
            Assert.IsFalse(Eval("wonder_pop", LiveOpsFeature.TournamentMode, true, false, FeatureFlagPolicy.Default, out string reason));
            Assert.AreEqual("UnknownGame", reason);
        }

        [Test]
        public void CanEnableFlag_KillSwitch_Disabled_Blocked()
        {
            Assert.IsFalse(Eval("Garage_Legends", LiveOpsFeature.EndlessMode, false, false, FeatureFlagPolicy.Default, out string reason));
            Assert.AreEqual("FeatureFlagDisabled", reason);
        }

        [Test]
        public void CanEnableFlag_GuardOrder_DisabledRestrictedNoConsent_ReportsDisabled()
        {
            Assert.IsFalse(Eval("Garage_Legends", LiveOpsFeature.TournamentMode, false, false, FeatureFlagPolicy.Default, out string reason));
            Assert.AreEqual("FeatureFlagDisabled", reason);
        }

        [Test]
        public void CanEnableFlag_NonWonderPop_Tournament_NoConsent_Blocked()
        {
            Assert.IsFalse(Eval("Garage_Legends", LiveOpsFeature.TournamentMode, true, false, FeatureFlagPolicy.Default, out string reason));
            Assert.AreEqual("RestrictedFeatureRequiresConsent", reason);
        }

        [Test]
        public void CanEnableFlag_NonWonderPop_Tournament_WithConsent_Eligible()
        {
            Assert.IsTrue(Eval("Garage_Legends", LiveOpsFeature.TournamentMode, true, true, FeatureFlagPolicy.Default, out string reason));
            Assert.AreEqual(string.Empty, reason);
        }

        [Test]
        public void CanEnableFlag_WeeklyPersonalBest_WithConsent_Eligible()
        {
            Assert.IsTrue(Eval("Garage_Legends", LiveOpsFeature.WeeklyPersonalBest, true, true, FeatureFlagPolicy.Default, out string reason));
            Assert.AreEqual(string.Empty, reason);
        }

        [Test]
        public void CanEnableFlag_NonWonderPop_TimedMode_Enabled_Eligible()
        {
            Assert.IsTrue(Eval("Garage_Legends", LiveOpsFeature.TimedMode, true, false, FeatureFlagPolicy.Default, out string reason));
            Assert.AreEqual(string.Empty, reason);
        }

        [Test]
        public void CanEnableFlag_NonWonderPop_SeasonalEvent_Enabled_HasConsent_Eligible()
        {
            Assert.IsTrue(Eval("Garage_Legends", LiveOpsFeature.SeasonalEvent, true, true, FeatureFlagPolicy.Default, out string reason));
            Assert.AreEqual(string.Empty, reason);
        }

        [Test]
        public void CanEnableFlag_PermissivePolicy_RestrictedNoConsent_Eligible()
        {
            Assert.IsTrue(Eval("Garage_Legends", LiveOpsFeature.TournamentMode, true, false, FeatureFlagPolicy.Permissive("relaxed"), out string reason));
            Assert.AreEqual(string.Empty, reason);
        }

        // Helpers

        [Test]
        public void IsRestrictedFeature_Helper_TournamentMode_True()
        {
            Assert.IsTrue(FeatureFlagGate.IsRestrictedFeature(FeatureFlagRecord.Create("Coral_Rescue", LiveOpsFeature.TournamentMode, 1, true, true)));
        }

        [Test]
        public void IsRestrictedFeature_Helper_CatchUpReward_False()
        {
            Assert.IsFalse(FeatureFlagGate.IsRestrictedFeature(FeatureFlagRecord.Create("Coral_Rescue", LiveOpsFeature.CatchUpReward, 1, true, false)));
        }

        [Test]
        public void IsDefaultOff_Helper_Restricted_True()
        {
            Assert.IsTrue(FeatureFlagGate.IsDefaultOff(FeatureFlagRecord.Create("Coral_Rescue", LiveOpsFeature.TournamentMode, 1, true, true)));
        }

        [Test]
        public void IsDefaultOff_Helper_Enabled_NonRestricted_False()
        {
            Assert.IsFalse(FeatureFlagGate.IsDefaultOff(FeatureFlagRecord.Create("Coral_Rescue", LiveOpsFeature.TimedMode, 1, true, false)));
        }

        // FeatureFlagLog

        [Test]
        public void FeatureFlagLog_Record_And_Count()
        {
            var log = new FeatureFlagLog();
            log.Record("Coral_Rescue", "TimedMode");
            log.Record("Coral_Rescue", "EndlessMode");
            Assert.AreEqual(2, log.Count);
        }

        [Test]
        public void FeatureFlagLog_HasEntry_True()
        {
            var log = new FeatureFlagLog();
            log.Record("Coral_Rescue", "TimedMode");
            Assert.IsTrue(log.HasEntry("Coral_Rescue"));
        }

        [Test]
        public void FeatureFlagLog_HasEntry_EmptyLog_False()
        {
            Assert.IsFalse(new FeatureFlagLog().HasEntry("Coral_Rescue"));
        }

        [Test]
        public void FeatureFlagLog_Clear_ResetsCount()
        {
            var log = new FeatureFlagLog();
            log.Record("Coral_Rescue", "TimedMode");
            log.Clear();
            Assert.AreEqual(0, log.Count);
        }

        [Test]
        public void FeatureFlagLog_Record_NullGameId_Throws()
        {
            Assert.Throws<ArgumentException>(() => new FeatureFlagLog().Record(null, "TimedMode"));
        }

        [Test]
        public void FeatureFlagLog_Record_EmptyGameId_Throws()
        {
            Assert.Throws<ArgumentException>(() => new FeatureFlagLog().Record("", "x"));
        }

        [Test]
        public void FeatureFlagLog_Record_NullFeatureName_Throws()
        {
            Assert.Throws<ArgumentException>(() => new FeatureFlagLog().Record("Coral_Rescue", null));
        }

        [Test]
        public void FeatureFlagLog_Record_EmptyFeatureName_Throws()
        {
            Assert.Throws<ArgumentException>(() => new FeatureFlagLog().Record("Coral_Rescue", ""));
        }


        [Test]
        public void NullOrEmptyGameId_DefaultRecord_GameIdMissing()
        {
            foreach (var gameId in new[] { null, string.Empty })
            {
                Assert.IsFalse(FeatureFlagGate.CanEnableFlag(gameId, default(FeatureFlagRecord), default(FeatureFlagPolicy), out var reason));
                Assert.AreEqual("GameIdMissing", reason);
            }
        }

        [Test]
        public void UnknownOrCaseVariantGameId_DefaultRecord_UnknownGame()
        {
            foreach (var gameId in new[] { "Unknown_Game", "coral_rescue", "CORAL_RESCUE", " Coral_Rescue", "Coral_Rescue ", "wonder_pop", "WONDER_POP" })
            {
                Assert.IsFalse(FeatureFlagGate.CanEnableFlag(gameId, default(FeatureFlagRecord), default(FeatureFlagPolicy), out var reason));
                Assert.AreEqual("UnknownGame", reason);
            }
        }
    }
}
