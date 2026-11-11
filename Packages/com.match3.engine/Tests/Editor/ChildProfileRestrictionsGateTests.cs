using System;
using NUnit.Framework;
using Match3.Engine.Runtime.Privacy;

namespace Match3.Engine.Tests.Editor
{
    [TestFixture]
    public class ChildProfileRestrictionsGateTests
    {
        // ChildRestrictionRecord tests

        [Test]
        public void ChildRestrictionRecord_Create_NullGameId_Throws()
        {
            Assert.Throws<ArgumentException>(() =>
                ChildRestrictionRecord.Create(null, ChildProfileFeature.BehavioralProfiling, false, false));
        }

        [Test]
        public void ChildRestrictionRecord_Create_EmptyGameId_Throws()
        {
            Assert.Throws<ArgumentException>(() =>
                ChildRestrictionRecord.Create(string.Empty, ChildProfileFeature.BehavioralProfiling, false, false));
        }

        [Test]
        public void ChildRestrictionRecord_Create_Valid_Succeeds()
        {
            var record = ChildRestrictionRecord.Create("Wonder_Pop", ChildProfileFeature.SocialRank, true, false);
            Assert.AreEqual("Wonder_Pop", record.GameId);
            Assert.AreEqual(ChildProfileFeature.SocialRank, record.Feature);
            Assert.IsTrue(record.HasParentalApproval);
            Assert.IsFalse(record.IsOwnerApproved);
        }

        [Test]
        public void ChildRestrictionRecord_IsOwnerLockedFeature_SocialInvite_True()
        {
            var record = ChildRestrictionRecord.Create("Wonder_Pop", ChildProfileFeature.SocialInvite, false, false);
            Assert.IsTrue(record.IsOwnerLockedFeature);
        }

        [Test]
        public void ChildRestrictionRecord_IsOwnerLockedFeature_TournamentEntry_True()
        {
            var record = ChildRestrictionRecord.Create("Wonder_Pop", ChildProfileFeature.TournamentEntry, false, false);
            Assert.IsTrue(record.IsOwnerLockedFeature);
        }

        [Test]
        public void ChildRestrictionRecord_IsOwnerLockedFeature_RemoteConfigOverride_True()
        {
            var record = ChildRestrictionRecord.Create("Wonder_Pop", ChildProfileFeature.RemoteConfigOverride, false, false);
            Assert.IsTrue(record.IsOwnerLockedFeature);
        }

        [Test]
        public void ChildRestrictionRecord_IsOwnerLockedFeature_BehavioralProfiling_False()
        {
            var record = ChildRestrictionRecord.Create("Wonder_Pop", ChildProfileFeature.BehavioralProfiling, false, false);
            Assert.IsFalse(record.IsOwnerLockedFeature);
        }

        [Test]
        public void ChildRestrictionRecord_IsOwnerLockedFeature_RandomizedLootPack_False()
        {
            var record = ChildRestrictionRecord.Create("Wonder_Pop", ChildProfileFeature.RandomizedLootPack, false, false);
            Assert.IsFalse(record.IsOwnerLockedFeature);
        }

        [Test]
        public void ChildRestrictionRecord_IsDefaultRestricted_AllFeatures_True()
        {
            foreach (ChildProfileFeature feature in Enum.GetValues(typeof(ChildProfileFeature)))
            {
                var record = ChildRestrictionRecord.Create("Coral_Rescue", feature, true, true);
                Assert.IsTrue(record.IsDefaultRestricted, $"Expected IsDefaultRestricted=true for {feature}");
            }
        }

        // ChildProfilePolicy tests

        [Test]
        public void ChildProfilePolicy_Safe_Values_Correct()
        {
            var policy = ChildProfilePolicy.Safe("Test policy");
            Assert.IsTrue(policy.EnforceChildRestrictions);
            Assert.IsTrue(policy.BlockOwnerLockedFeatures);
            Assert.AreEqual("Test policy", policy.PolicyDescription);
        }

        [Test]
        public void ChildProfilePolicy_Default_NotNull()
        {
            var policy = ChildProfilePolicy.Default;
            Assert.IsTrue(policy.EnforceChildRestrictions);
            Assert.IsTrue(policy.BlockOwnerLockedFeatures);
            Assert.IsFalse(string.IsNullOrEmpty(policy.PolicyDescription));
        }

        [Test]
        public void ChildProfilePolicy_NullDescription_Throws()
        {
            Assert.Throws<ArgumentException>(() => ChildProfilePolicy.Safe(null));
        }

        // CanEnableFeature tests

        [Test]
        public void CanEnableFeature_WonderPop_RemoteConfigOverride_AlwaysBlocked()
        {
            // Guard 1: even with full approval, RemoteConfigOverride blocked for Wonder_Pop
            var record = ChildRestrictionRecord.Create("Wonder_Pop", ChildProfileFeature.RemoteConfigOverride, true, true);
            var policy = ChildProfilePolicy.Default;
            bool result = ChildProfileRestrictionsGate.CanEnableFeature("Wonder_Pop", record, policy, out string reason);
            Assert.IsFalse(result);
            Assert.AreEqual("WonderPopRemoteConfigOverrideProhibited", reason);
        }

        [Test]
        public void CanEnableFeature_WonderPop_SocialInvite_NoOwnerApproval_Blocked()
        {
            var record = ChildRestrictionRecord.Create("Wonder_Pop", ChildProfileFeature.SocialInvite, true, false);
            var policy = ChildProfilePolicy.Default;
            bool result = ChildProfileRestrictionsGate.CanEnableFeature("Wonder_Pop", record, policy, out string reason);
            Assert.IsFalse(result);
            Assert.AreEqual("WonderPopOwnerApprovalRequired", reason);
        }

        [Test]
        public void CanEnableFeature_WonderPop_TournamentEntry_NoOwnerApproval_Blocked()
        {
            var record = ChildRestrictionRecord.Create("Wonder_Pop", ChildProfileFeature.TournamentEntry, true, false);
            var policy = ChildProfilePolicy.Default;
            bool result = ChildProfileRestrictionsGate.CanEnableFeature("Wonder_Pop", record, policy, out string reason);
            Assert.IsFalse(result);
            Assert.AreEqual("WonderPopOwnerApprovalRequired", reason);
        }

        [Test]
        public void CanEnableFeature_WonderPop_SocialInvite_OwnerApproved_NoParental_Blocked()
        {
            var record = ChildRestrictionRecord.Create("Wonder_Pop", ChildProfileFeature.SocialInvite, false, true);
            var policy = ChildProfilePolicy.Default;
            bool result = ChildProfileRestrictionsGate.CanEnableFeature("Wonder_Pop", record, policy, out string reason);
            Assert.IsFalse(result);
            Assert.AreEqual("WonderPopParentalApprovalRequired", reason);
        }

        [Test]
        public void CanEnableFeature_WonderPop_BehavioralProfiling_NoParental_Blocked()
        {
            var record = ChildRestrictionRecord.Create("Wonder_Pop", ChildProfileFeature.BehavioralProfiling, false, false);
            var policy = ChildProfilePolicy.Default;
            bool result = ChildProfileRestrictionsGate.CanEnableFeature("Wonder_Pop", record, policy, out string reason);
            Assert.IsFalse(result);
            Assert.AreEqual("WonderPopParentalApprovalRequired", reason);
        }

        [Test]
        public void CanEnableFeature_WonderPop_BehavioralProfiling_HasParental_Eligible()
        {
            var record = ChildRestrictionRecord.Create("Wonder_Pop", ChildProfileFeature.BehavioralProfiling, true, false);
            var policy = ChildProfilePolicy.Default;
            bool result = ChildProfileRestrictionsGate.CanEnableFeature("Wonder_Pop", record, policy, out string reason);
            Assert.IsTrue(result);
            Assert.AreEqual(string.Empty, reason);
        }

        [Test]
        public void CanEnableFeature_WonderPop_ForcedStorePrompt_HasParental_Eligible()
        {
            var record = ChildRestrictionRecord.Create("Wonder_Pop", ChildProfileFeature.ForcedStorePrompt, true, false);
            var policy = ChildProfilePolicy.Default;
            bool result = ChildProfileRestrictionsGate.CanEnableFeature("Wonder_Pop", record, policy, out string reason);
            Assert.IsTrue(result);
            Assert.AreEqual(string.Empty, reason);
        }

        [Test]
        public void CanEnableFeature_WonderPop_Lowercase_RemoteConfig_UnknownGame()
        {
            // Ordinal catalog lookup: a case variant of Wonder_Pop is not a catalog ID, so it fails closed with UnknownGame (never eligible, never the Wonder_Pop reason).
            var record = ChildRestrictionRecord.Create("wonder_pop", ChildProfileFeature.RemoteConfigOverride, true, true);
            var policy = ChildProfilePolicy.Default;
            bool result = ChildProfileRestrictionsGate.CanEnableFeature("wonder_pop", record, policy, out string reason);
            Assert.IsFalse(result);
            Assert.AreEqual("UnknownGame", reason);
        }

        [Test]
        public void CanEnableFeature_NonWonderPop_OwnerLocked_NoOwnerApproval_Blocked()
        {
            var record = ChildRestrictionRecord.Create("Garage_Legends", ChildProfileFeature.SocialInvite, true, false);
            var policy = ChildProfilePolicy.Default;
            bool result = ChildProfileRestrictionsGate.CanEnableFeature("Garage_Legends", record, policy, out string reason);
            Assert.IsFalse(result);
            Assert.AreEqual("OwnerApprovalRequired", reason);
        }

        [Test]
        public void CanEnableFeature_NonWonderPop_OwnerLocked_OwnerApproved_Eligible()
        {
            var record = ChildRestrictionRecord.Create("Garage_Legends", ChildProfileFeature.TournamentEntry, false, true);
            var policy = ChildProfilePolicy.Default;
            bool result = ChildProfileRestrictionsGate.CanEnableFeature("Garage_Legends", record, policy, out string reason);
            Assert.IsTrue(result);
            Assert.AreEqual(string.Empty, reason);
        }

        [Test]
        public void CanEnableFeature_NonWonderPop_NonLocked_Eligible()
        {
            var record = ChildRestrictionRecord.Create("Garage_Legends", ChildProfileFeature.BehavioralProfiling, false, false);
            var policy = ChildProfilePolicy.Default;
            bool result = ChildProfileRestrictionsGate.CanEnableFeature("Garage_Legends", record, policy, out string reason);
            Assert.IsTrue(result);
            Assert.AreEqual(string.Empty, reason);
        }

        // Helper method tests

        [Test]
        public void IsOwnerLockedFeature_Helper_RemoteConfigOverride_True()
        {
            var record = ChildRestrictionRecord.Create("Coral_Rescue", ChildProfileFeature.RemoteConfigOverride, false, false);
            Assert.IsTrue(ChildProfileRestrictionsGate.IsOwnerLockedFeature(record));
        }

        [Test]
        public void IsOwnerLockedFeature_Helper_SocialRank_False()
        {
            var record = ChildRestrictionRecord.Create("Coral_Rescue", ChildProfileFeature.SocialRank, false, false);
            Assert.IsFalse(ChildProfileRestrictionsGate.IsOwnerLockedFeature(record));
        }

        [Test]
        public void IsDefaultRestricted_Helper_AnyFeature_True()
        {
            var record = ChildRestrictionRecord.Create("Coral_Rescue", ChildProfileFeature.ForcedStorePrompt, false, false);
            Assert.IsTrue(ChildProfileRestrictionsGate.IsDefaultRestricted(record));
        }

        // ChildRestrictionLog tests

        [Test]
        public void ChildRestrictionLog_Record_And_Count()
        {
            var log = new ChildRestrictionLog();
            log.Record("Wonder_Pop", "BehavioralProfiling");
            log.Record("Wonder_Pop", "SocialRank");
            Assert.AreEqual(2, log.Count);
        }

        [Test]
        public void ChildRestrictionLog_HasEntry_True()
        {
            var log = new ChildRestrictionLog();
            log.Record("Wonder_Pop", "BehavioralProfiling");
            Assert.IsTrue(log.HasEntry("Wonder_Pop"));
            Assert.IsFalse(log.HasEntry("Garage_Legends"));
        }

        [Test]
        public void ChildRestrictionLog_Clear_ResetsCount()
        {
            var log = new ChildRestrictionLog();
            log.Record("Wonder_Pop", "SocialRank");
            log.Clear();
            Assert.AreEqual(0, log.Count);
        }

        [Test]
        public void ChildRestrictionLog_Record_NullGameId_Throws()
        {
            var log = new ChildRestrictionLog();
            Assert.Throws<ArgumentException>(() => log.Record(null, "BehavioralProfiling"));
        }

        // QA remediation: fail-closed default policy, GameId mismatch guard, log argument validation
        [Test]
        public void CanEnableFeature_DefaultStructPolicy_FailsClosed()
        {
            var record = ChildRestrictionRecord.Create("Coral_Rescue", ChildProfileFeature.SocialInvite, true, false);
            var expected = ChildProfileRestrictionsGate.CanEnableFeature("Coral_Rescue", record, ChildProfilePolicy.Default, out var expectedReason);
            var result = ChildProfileRestrictionsGate.CanEnableFeature("Coral_Rescue", record, default(ChildProfilePolicy), out var reason);
            Assert.IsFalse(expected);
            Assert.IsFalse(result);
            Assert.AreEqual("OwnerApprovalRequired", expectedReason);
            Assert.AreEqual(expectedReason, reason);
        }

        [Test]
        public void CanEnableFeature_GameIdMismatch_Blocked()
        {
            var record = ChildRestrictionRecord.Create("Wonder_Pop", ChildProfileFeature.RemoteConfigOverride, false, false);
            var result = ChildProfileRestrictionsGate.CanEnableFeature("Coral_Rescue", record, ChildProfilePolicy.Default, out var reason);
            Assert.IsFalse(result);
            Assert.AreEqual("GameIdMismatch", reason);
        }

        [Test]
        public void ChildRestrictionLog_Record_NullOrEmptySecondArg_Throws()
        {
            var log = new ChildRestrictionLog();
            Assert.Throws<System.ArgumentException>(() => log.Record("Coral_Rescue", null));
            Assert.Throws<System.ArgumentException>(() => log.Record("Coral_Rescue", string.Empty));
            Assert.AreEqual(0, log.Count);
        }

        [Test]
        public void ChildRestrictionLog_HasEntry_IsOrdinal()
        {
            var log = new ChildRestrictionLog();
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
                Assert.IsFalse(ChildProfileRestrictionsGate.CanEnableFeature(gameId, default(ChildRestrictionRecord), default(ChildProfilePolicy), out var reason));
                Assert.AreEqual("GameIdMissing", reason);
            }
        }

        [Test]
        public void UnknownOrCaseVariantGameId_DefaultRecord_UnknownGame()
        {
            foreach (var gameId in new[] { "Unknown_Game", "coral_rescue", "CORAL_RESCUE", " Coral_Rescue", "Coral_Rescue ", "wonder_pop", "WONDER_POP" })
            {
                Assert.IsFalse(ChildProfileRestrictionsGate.CanEnableFeature(gameId, default(ChildRestrictionRecord), default(ChildProfilePolicy), out var reason));
                Assert.AreEqual("UnknownGame", reason);
            }
        }
    }
}
