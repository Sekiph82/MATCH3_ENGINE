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
            var token = CloudSaveToken.Create("GameA", CloudSaveSlot.CloudPrimary, "hash123");
            Assert.AreEqual("GameA", token.GameId);
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
                CloudSaveToken.Create("GameA", CloudSaveSlot.CloudPrimary, null));
        }

        [Test]
        public void CloudSaveToken_Create_EmptyTokenHash_Throws()
        {
            Assert.Throws<ArgumentException>(() =>
                CloudSaveToken.Create("GameA", CloudSaveSlot.CloudPrimary, ""));
        }

        [Test]
        public void CloudSaveToken_IsCloudBacked_LocalOnly_False()
        {
            var token = CloudSaveToken.Create("GameA", CloudSaveSlot.LocalOnly, "hash");
            Assert.IsFalse(token.IsCloudBacked);
        }

        [Test]
        public void CloudSaveToken_IsCloudBacked_CloudPrimary_True()
        {
            var token = CloudSaveToken.Create("GameA", CloudSaveSlot.CloudPrimary, "hash");
            Assert.IsTrue(token.IsCloudBacked);
        }

        [Test]
        public void CloudSaveToken_IsCloudBacked_Mirror_True()
        {
            var token = CloudSaveToken.Create("GameA", CloudSaveSlot.Mirror, "hash");
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
        public void CanWriteToCloud_WonderPop_OrdinalCaseSensitive_LowercaseNotBlocked()
        {
            // Ordinal: "wonder_pop" is NOT "Wonder_Pop"
            var policy = new CloudSavePolicy(true, false, "permissive");
            var result = CloudSaveAdapterGate.CanWriteToCloud("wonder_pop", policy);
            Assert.IsTrue(result.IsEligible);
        }

        [Test]
        public void CanWriteToCloud_CloudWriteDisabled_IneligibleWithReason()
        {
            var policy = CloudSavePolicy.Default; // AllowCloudWrite = false
            var result = CloudSaveAdapterGate.CanWriteToCloud("GameA", policy);
            Assert.IsFalse(result.IsEligible);
            Assert.AreEqual("CloudWriteDisabled", result.Reason);
        }

        [Test]
        public void CanWriteToCloud_AllowedPolicy_Eligible()
        {
            var policy = new CloudSavePolicy(true, true, "enabled");
            var result = CloudSaveAdapterGate.CanWriteToCloud("GameA", policy);
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
            var token = CloudSaveToken.Create("GameA", CloudSaveSlot.LocalOnly, "hash");
            Assert.IsFalse(CloudSaveAdapterGate.IsCloudSlot(token));
        }

        [Test]
        public void IsCloudSlot_CloudPrimary_True()
        {
            var token = CloudSaveToken.Create("GameA", CloudSaveSlot.CloudPrimary, "hash");
            Assert.IsTrue(CloudSaveAdapterGate.IsCloudSlot(token));
        }

        [Test]
        public void IsCloudSlot_Mirror_True()
        {
            var token = CloudSaveToken.Create("GameA", CloudSaveSlot.Mirror, "hash");
            Assert.IsTrue(CloudSaveAdapterGate.IsCloudSlot(token));
        }

        // --- CloudSaveLog ---

        [Test]
        public void CloudSaveLog_Record_NullReason_Throws()
        {
            var log = new CloudSaveLog();
            var token = CloudSaveToken.Create("GameA", CloudSaveSlot.LocalOnly, "hash");
            Assert.Throws<ArgumentException>(() => log.Record(token, true, null));
        }

        [Test]
        public void CloudSaveLog_Record_EmptyReason_Throws()
        {
            var log = new CloudSaveLog();
            var token = CloudSaveToken.Create("GameA", CloudSaveSlot.LocalOnly, "hash");
            Assert.Throws<ArgumentException>(() => log.Record(token, true, ""));
        }

        [Test]
        public void CloudSaveLog_Count_IncreasesOnRecord()
        {
            var log = new CloudSaveLog();
            Assert.AreEqual(0, log.Count);
            log.Record(CloudSaveToken.Create("GameA", CloudSaveSlot.LocalOnly, "h"), true, "ok");
            Assert.AreEqual(1, log.Count);
            log.Record(CloudSaveToken.Create("GameB", CloudSaveSlot.CloudPrimary, "h2"), false, "fail");
            Assert.AreEqual(2, log.Count);
        }

        [Test]
        public void CloudSaveLog_Clear_ResetsCount()
        {
            var log = new CloudSaveLog();
            log.Record(CloudSaveToken.Create("GameA", CloudSaveSlot.LocalOnly, "h"), true, "ok");
            log.Clear();
            Assert.AreEqual(0, log.Count);
        }

        [Test]
        public void CloudSaveLog_HasEntry_TrueAfterRecord()
        {
            var log = new CloudSaveLog();
            log.Record(CloudSaveToken.Create("GameX", CloudSaveSlot.Mirror, "h"), false, "reason");
            Assert.IsTrue(log.HasEntry("GameX"));
        }

        [Test]
        public void CloudSaveLog_HasEntry_FalseWhenNotRecorded()
        {
            var log = new CloudSaveLog();
            Assert.IsFalse(log.HasEntry("GameX"));
        }

        [Test]
        public void CloudSaveLog_HasEntry_FalseAfterClear()
        {
            var log = new CloudSaveLog();
            log.Record(CloudSaveToken.Create("GameY", CloudSaveSlot.CloudPrimary, "h"), true, "ok");
            log.Clear();
            Assert.IsFalse(log.HasEntry("GameY"));
        }


        [Test]
        public void DefaultStructPolicy_FailsClosed()
        {
            var result = CloudSaveAdapterGate.CanWriteToCloud("GameA", default(CloudSavePolicy));
            Assert.IsFalse(result.IsEligible);
            Assert.AreEqual(CloudSaveAdapterGate.CanWriteToCloud("GameA", CloudSavePolicy.Default).Reason, result.Reason);
            Assert.IsTrue(CloudSaveAdapterGate.RequiresTokenValidation(default(CloudSavePolicy)));
        }

        [Test]
        public void CloudSaveLog_Record_DefaultToken_Throws()
        {
            var log = new CloudSaveLog();
            Assert.Throws<ArgumentException>(() => log.Record(default(CloudSaveToken), false, "r"));
        }
    }
}
