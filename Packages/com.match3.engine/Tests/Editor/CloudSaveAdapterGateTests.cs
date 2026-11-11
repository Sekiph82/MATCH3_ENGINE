using System;
using NUnit.Framework;
using Match3.Engine.Runtime.Identity;

namespace Match3.Engine.Tests.Editor
{
    [TestFixture]
    public class CloudSaveAdapterGateTests
    {
        // --- CloudSaveToken ---

        [Test]
        public void CloudSaveToken_Create_Valid_ReturnsToken()
        {
            var token = CloudSaveToken.Create("Coral_Rescue", CloudSaveSlot.CloudPrimary, "hash123");
            Assert.AreEqual("Coral_Rescue", token.GameId);
            Assert.AreEqual(CloudSaveSlot.CloudPrimary, token.SlotType);
            Assert.AreEqual("hash123", token.TokenHash);
        }

        [Test]
        public void CloudSaveToken_Create_NullGameId_Throws()
        {
            Assert.Throws<ArgumentException>(() =>
                CloudSaveToken.Create(null, CloudSaveSlot.CloudPrimary, "hash"));
        }

        [Test]
        public void CloudSaveToken_Create_EmptyGameId_Throws()
        {
            Assert.Throws<ArgumentException>(() =>
                CloudSaveToken.Create("", CloudSaveSlot.CloudPrimary, "hash"));
        }

        [Test]
        public void CloudSaveToken_Create_NullTokenHash_Throws()
        {
            Assert.Throws<ArgumentException>(() =>
                CloudSaveToken.Create("Coral_Rescue", CloudSaveSlot.CloudPrimary, null));
        }

        [Test]
        public void CloudSaveToken_Create_EmptyTokenHash_Throws()
        {
            Assert.Throws<ArgumentException>(() =>
                CloudSaveToken.Create("Coral_Rescue", CloudSaveSlot.CloudPrimary, ""));
        }

        [Test]
        public void CloudSaveToken_IsCloudBacked_LocalOnly_False()
        {
            var token = CloudSaveToken.Create("Coral_Rescue", CloudSaveSlot.LocalOnly, "hash");
            Assert.IsFalse(token.IsCloudBacked);
        }

        [Test]
        public void CloudSaveToken_IsCloudBacked_CloudPrimary_True()
        {
            var token = CloudSaveToken.Create("Coral_Rescue", CloudSaveSlot.CloudPrimary, "hash");
            Assert.IsTrue(token.IsCloudBacked);
        }

        [Test]
        public void CloudSaveToken_IsCloudBacked_Mirror_True()
        {
            var token = CloudSaveToken.Create("Coral_Rescue", CloudSaveSlot.Mirror, "hash");
            Assert.IsTrue(token.IsCloudBacked);
        }

        // --- CloudSavePolicy ---

        [Test]
        public void CloudSavePolicy_Safe_AllowCloudWrite_False()
        {
            var policy = CloudSavePolicy.Safe("desc");
            Assert.IsFalse(policy.AllowCloudWrite);
        }

        [Test]
        public void CloudSavePolicy_Safe_RequireTokenValidation_True()
        {
            var policy = CloudSavePolicy.Safe("desc");
            Assert.IsTrue(policy.RequireTokenValidation);
        }

        [Test]
        public void CloudSavePolicy_Default_AllowCloudWrite_False()
        {
            Assert.IsFalse(CloudSavePolicy.Default.AllowCloudWrite);
        }

        [Test]
        public void CloudSavePolicy_NullDescription_Throws()
        {
            Assert.Throws<ArgumentException>(() =>
                new CloudSavePolicy(false, true, null));
        }

        [Test]
        public void CloudSavePolicy_EmptyDescription_Throws()
        {
            Assert.Throws<ArgumentException>(() =>
                new CloudSavePolicy(false, true, ""));
        }

        // --- CloudSaveAdapterGate.CanWriteToCloud ---

        [Test]
        public void CanWriteToCloud_WonderPop_IneligibleFirst_RegardlessOfPolicy()
        {
            // Guard 1 must fire even when policy allows cloud write
            var policy = new CloudSavePolicy(true, false, "permissive");
            var result = CloudSaveAdapterGate.CanWriteToCloud("Wonder_Pop", policy);
            Assert.IsFalse(result.IsEligible);
            Assert.AreEqual("WonderPopCloudWriteProhibited", result.Reason);
        }

        [Test]
        public void CanWriteToCloud_WonderPop_OrdinalCaseSensitive_LowercaseUnknownGame()
        {
            // Ordinal catalog lookup: a case variant of Wonder_Pop is not a catalog ID, so it fails closed with UnknownGame (never eligible, never the Wonder_Pop reason).
            var policy = new CloudSavePolicy(true, false, "permissive");
            var result = CloudSaveAdapterGate.CanWriteToCloud("wonder_pop", policy);
            Assert.IsFalse(result.IsEligible);
            Assert.AreEqual("UnknownGame", result.Reason);
        }

        [Test]
        public void CanWriteToCloud_CloudWriteDisabled_IneligibleWithReason()
        {
            var policy = CloudSavePolicy.Default; // AllowCloudWrite = false
            var result = CloudSaveAdapterGate.CanWriteToCloud("Coral_Rescue", policy);
            Assert.IsFalse(result.IsEligible);
            Assert.AreEqual("CloudWriteDisabled", result.Reason);
        }

        [Test]
        public void CanWriteToCloud_AllowedPolicy_Eligible()
        {
            var policy = new CloudSavePolicy(true, true, "enabled");
            var result = CloudSaveAdapterGate.CanWriteToCloud("Coral_Rescue", policy);
            Assert.IsTrue(result.IsEligible);
            Assert.AreEqual("Eligible", result.Reason);
        }

        // --- CloudSaveAdapterGate.RequiresTokenValidation ---

        [Test]
        public void RequiresTokenValidation_True_WhenPolicyRequires()
        {
            var policy = new CloudSavePolicy(true, true, "desc");
            Assert.IsTrue(CloudSaveAdapterGate.RequiresTokenValidation(policy));
        }

        [Test]
        public void RequiresTokenValidation_False_WhenPolicyDoesNotRequire()
        {
            var policy = new CloudSavePolicy(true, false, "desc");
            Assert.IsFalse(CloudSaveAdapterGate.RequiresTokenValidation(policy));
        }

        // --- CloudSaveAdapterGate.IsCloudSlot ---

        [Test]
        public void IsCloudSlot_LocalOnly_False()
        {
            var token = CloudSaveToken.Create("Coral_Rescue", CloudSaveSlot.LocalOnly, "hash");
            Assert.IsFalse(CloudSaveAdapterGate.IsCloudSlot(token));
        }

        [Test]
        public void IsCloudSlot_CloudPrimary_True()
        {
            var token = CloudSaveToken.Create("Coral_Rescue", CloudSaveSlot.CloudPrimary, "hash");
            Assert.IsTrue(CloudSaveAdapterGate.IsCloudSlot(token));
        }

        [Test]
        public void IsCloudSlot_Mirror_True()
        {
            var token = CloudSaveToken.Create("Coral_Rescue", CloudSaveSlot.Mirror, "hash");
            Assert.IsTrue(CloudSaveAdapterGate.IsCloudSlot(token));
        }

        // --- CloudSaveLog ---

        [Test]
        public void CloudSaveLog_Record_NullReason_Throws()
        {
            var log = new CloudSaveLog();
            var token = CloudSaveToken.Create("Coral_Rescue", CloudSaveSlot.LocalOnly, "hash");
            Assert.Throws<ArgumentException>(() => log.Record(token, true, null));
        }

        [Test]
        public void CloudSaveLog_Record_EmptyReason_Throws()
        {
            var log = new CloudSaveLog();
            var token = CloudSaveToken.Create("Coral_Rescue", CloudSaveSlot.LocalOnly, "hash");
            Assert.Throws<ArgumentException>(() => log.Record(token, true, ""));
        }

        [Test]
        public void CloudSaveLog_Count_IncreasesOnRecord()
        {
            var log = new CloudSaveLog();
            Assert.AreEqual(0, log.Count);
            log.Record(CloudSaveToken.Create("Coral_Rescue", CloudSaveSlot.LocalOnly, "h"), true, "ok");
            Assert.AreEqual(1, log.Count);
            log.Record(CloudSaveToken.Create("Garage_Legends", CloudSaveSlot.CloudPrimary, "h2"), false, "fail");
            Assert.AreEqual(2, log.Count);
        }

        [Test]
        public void CloudSaveLog_Clear_ResetsCount()
        {
            var log = new CloudSaveLog();
            log.Record(CloudSaveToken.Create("Coral_Rescue", CloudSaveSlot.LocalOnly, "h"), true, "ok");
            log.Clear();
            Assert.AreEqual(0, log.Count);
        }

        [Test]
        public void CloudSaveLog_HasEntry_TrueAfterRecord()
        {
            var log = new CloudSaveLog();
            log.Record(CloudSaveToken.Create("Garage_Legends", CloudSaveSlot.Mirror, "h"), false, "reason");
            Assert.IsTrue(log.HasEntry("Garage_Legends"));
        }

        [Test]
        public void CloudSaveLog_HasEntry_FalseWhenNotRecorded()
        {
            var log = new CloudSaveLog();
            Assert.IsFalse(log.HasEntry("Garage_Legends"));
        }

        [Test]
        public void CloudSaveLog_HasEntry_FalseAfterClear()
        {
            var log = new CloudSaveLog();
            log.Record(CloudSaveToken.Create("Dino_Islands", CloudSaveSlot.CloudPrimary, "h"), true, "ok");
            log.Clear();
            Assert.IsFalse(log.HasEntry("Dino_Islands"));
        }


        [Test]
        public void DefaultStructPolicy_FailsClosed()
        {
            var result = CloudSaveAdapterGate.CanWriteToCloud("Coral_Rescue", default(CloudSavePolicy));
            Assert.IsFalse(result.IsEligible);
            Assert.AreEqual(CloudSaveAdapterGate.CanWriteToCloud("Coral_Rescue", CloudSavePolicy.Default).Reason, result.Reason);
            Assert.IsTrue(CloudSaveAdapterGate.RequiresTokenValidation(default(CloudSavePolicy)));
        }

        [Test]
        public void CloudSaveLog_Record_DefaultToken_Throws()
        {
            var log = new CloudSaveLog();
            Assert.Throws<ArgumentException>(() => log.Record(default(CloudSaveToken), false, "r"));
        }


        [Test]
        public void CanWriteToCloud_NullOrEmptyGameId_PermissivePolicy_GameIdMissing()
        {
            foreach (var gameId in new[] { null, string.Empty })
            {
                var result = CloudSaveAdapterGate.CanWriteToCloud(gameId, new CloudSavePolicy(true, true, "write enabled"));
                Assert.IsFalse(result.IsEligible);
                Assert.AreEqual("GameIdMissing", result.Reason);
            }
        }

        [Test]
        public void CanWriteToCloud_UnknownOrCaseVariantGameId_PermissivePolicy_UnknownGame()
        {
            foreach (var gameId in new[] { "Unknown_Game", "coral_rescue", "CORAL_RESCUE", " Coral_Rescue", "Coral_Rescue ", "wonder_pop", "WONDER_POP" })
            {
                var result = CloudSaveAdapterGate.CanWriteToCloud(gameId, new CloudSavePolicy(true, true, "write enabled"));
                Assert.IsFalse(result.IsEligible);
                Assert.AreEqual("UnknownGame", result.Reason);
            }
        }
    }
}
