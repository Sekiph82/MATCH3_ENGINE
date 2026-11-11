using System;
using NUnit.Framework;
using Match3.Engine.Runtime.LiveOps;

namespace Match3.Engine.Tests.Editor
{
    public class RemoteConfigVersionGateTests
    {
        // RemoteConfigRecord tests

        [Test]
        public void RemoteConfigRecord_Create_NullGameId_Throws()
        {
            Assert.Throws<ArgumentException>(() =>
                RemoteConfigRecord.Create(null, RemoteConfigKey.GameplayDifficulty, "1", 1, true, true));
        }

        [Test]
        public void RemoteConfigRecord_Create_EmptyGameId_Throws()
        {
            Assert.Throws<ArgumentException>(() =>
                RemoteConfigRecord.Create("", RemoteConfigKey.GameplayDifficulty, "1", 1, true, true));
        }

        [Test]
        public void RemoteConfigRecord_Create_NullValue_Throws()
        {
            Assert.Throws<ArgumentException>(() =>
                RemoteConfigRecord.Create("GameA", RemoteConfigKey.GameplayDifficulty, null, 1, true, true));
        }

        [Test]
        public void RemoteConfigRecord_Create_Valid_Succeeds()
        {
            var r = RemoteConfigRecord.Create("GameA", RemoteConfigKey.CoinRewardAmount, "100", 1, true, true);
            Assert.AreEqual("GameA", r.GameId);
            Assert.AreEqual(RemoteConfigKey.CoinRewardAmount, r.Key);
            Assert.AreEqual("100", r.Value);
            Assert.AreEqual(1, r.SchemaVersion);
            Assert.IsTrue(r.IsSigned);
            Assert.IsTrue(r.HasFallback);
        }

        [Test]
        public void RemoteConfigRecord_IsGameplaySafe_GameplayDifficulty_True()
        {
            var r = RemoteConfigRecord.Create("G", RemoteConfigKey.GameplayDifficulty, "v", 1, true, true);
            Assert.IsTrue(r.IsGameplaySafe);
        }

        [Test]
        public void RemoteConfigRecord_IsGameplaySafe_CoinRewardAmount_True()
        {
            var r = RemoteConfigRecord.Create("G", RemoteConfigKey.CoinRewardAmount, "v", 1, true, true);
            Assert.IsTrue(r.IsGameplaySafe);
        }

        [Test]
        public void RemoteConfigRecord_IsGameplaySafe_SessionTimeLimit_True()
        {
            var r = RemoteConfigRecord.Create("G", RemoteConfigKey.SessionTimeLimit, "v", 1, true, true);
            Assert.IsTrue(r.IsGameplaySafe);
        }

        [Test]
        public void RemoteConfigRecord_IsGameplaySafe_EventCalendarId_False()
        {
            var r = RemoteConfigRecord.Create("G", RemoteConfigKey.EventCalendarId, "v", 1, true, true);
            Assert.IsFalse(r.IsGameplaySafe);
        }

        [Test]
        public void RemoteConfigRecord_RequiresFallback_NoFallbackNoSign_True()
        {
            var r = RemoteConfigRecord.Create("G", RemoteConfigKey.GameplayDifficulty, "v", 1, false, false);
            Assert.IsTrue(r.RequiresFallback);
        }

        [Test]
        public void RemoteConfigRecord_RequiresFallback_HasFallback_False()
        {
            var r = RemoteConfigRecord.Create("G", RemoteConfigKey.GameplayDifficulty, "v", 1, false, true);
            Assert.IsFalse(r.RequiresFallback);
        }

        // RemoteConfigPolicy tests

        [Test]
        public void RemoteConfigPolicy_Safe_Values_Correct()
        {
            var p = RemoteConfigPolicy.Safe("test policy");
            Assert.IsTrue(p.RequireSignature);
            Assert.AreEqual(1, p.MinSchemaVersion);
            Assert.AreEqual("test policy", p.PolicyDescription);
        }

        [Test]
        public void RemoteConfigPolicy_Default_NotNull()
        {
            var p = RemoteConfigPolicy.Default;
            Assert.IsNotNull(p.PolicyDescription);
            Assert.IsTrue(p.RequireSignature);
        }

        [Test]
        public void RemoteConfigPolicy_NullDescription_Throws()
        {
            Assert.Throws<ArgumentException>(() => RemoteConfigPolicy.Safe(null));
        }

        // CanApplyConfig tests

        [Test]
        public void CanApplyConfig_WonderPop_Unsigned_Blocked_Guard1()
        {
            var record = RemoteConfigRecord.Create("Wonder_Pop", RemoteConfigKey.GameplayDifficulty, "v", 1, false, true);
            var policy = RemoteConfigPolicy.Default;
            var result = RemoteConfigVersionGate.CanApplyConfig("Wonder_Pop", record, policy, out string reason);
            Assert.IsFalse(result);
            Assert.AreEqual("WonderPopUnsignedConfigProhibited", reason);
        }

        [Test]
        public void CanApplyConfig_WonderPop_Signed_OldSchema_Blocked_Guard3()
        {
            var record = RemoteConfigRecord.Create("Wonder_Pop", RemoteConfigKey.GameplayDifficulty, "v", 0, true, true);
            var policy = RemoteConfigPolicy.Default;
            var result = RemoteConfigVersionGate.CanApplyConfig("Wonder_Pop", record, policy, out string reason);
            Assert.IsFalse(result);
            Assert.AreEqual("SchemaVersionTooOld", reason);
        }

        [Test]
        public void CanApplyConfig_WonderPop_Signed_ValidSchema_Eligible()
        {
            var record = RemoteConfigRecord.Create("Wonder_Pop", RemoteConfigKey.GameplayDifficulty, "v", 1, true, true);
            var policy = RemoteConfigPolicy.Default;
            var result = RemoteConfigVersionGate.CanApplyConfig("Wonder_Pop", record, policy, out string reason);
            Assert.IsTrue(result);
            Assert.AreEqual(string.Empty, reason);
        }

        [Test]
        public void CanApplyConfig_WonderPop_Lowercase_Unsigned_NotBlocked()
        {
            // "wonder_pop" (lowercase) does NOT match "Wonder_Pop" with Ordinal comparison
            // Falls through Guard 1, hits Guard 2 (RequireSignature=true, !IsSigned)
            var record = RemoteConfigRecord.Create("wonder_pop", RemoteConfigKey.GameplayDifficulty, "v", 1, false, true);
            var policy = RemoteConfigPolicy.Default;
            var result = RemoteConfigVersionGate.CanApplyConfig("wonder_pop", record, policy, out string reason);
            Assert.IsFalse(result);
            Assert.AreEqual("UnsignedConfigRejected", reason);
        }

        [Test]
        public void CanApplyConfig_NonWonderPop_Unsigned_RequireSignature_Blocked()
        {
            var record = RemoteConfigRecord.Create("GameB", RemoteConfigKey.GameplayDifficulty, "v", 1, false, true);
            var policy = RemoteConfigPolicy.Default;
            var result = RemoteConfigVersionGate.CanApplyConfig("GameB", record, policy, out string reason);
            Assert.IsFalse(result);
            Assert.AreEqual("UnsignedConfigRejected", reason);
        }

        [Test]
        public void CanApplyConfig_NonWonderPop_Signed_OldSchema_Blocked()
        {
            var record = RemoteConfigRecord.Create("GameB", RemoteConfigKey.GameplayDifficulty, "v", 0, true, true);
            var policy = RemoteConfigPolicy.Default;
            var result = RemoteConfigVersionGate.CanApplyConfig("GameB", record, policy, out string reason);
            Assert.IsFalse(result);
            Assert.AreEqual("SchemaVersionTooOld", reason);
        }

        [Test]
        public void CanApplyConfig_NonWonderPop_Valid_Eligible()
        {
            var record = RemoteConfigRecord.Create("GameB", RemoteConfigKey.GameplayDifficulty, "v", 1, true, true);
            var policy = RemoteConfigPolicy.Default;
            var result = RemoteConfigVersionGate.CanApplyConfig("GameB", record, policy, out string reason);
            Assert.IsTrue(result);
            Assert.AreEqual(string.Empty, reason);
        }

        [Test]
        public void CanApplyConfig_SafePolicy_Signed_SchemaV0_Blocked()
        {
            var record = RemoteConfigRecord.Create("GameC", RemoteConfigKey.GameplayDifficulty, "v", 0, true, true);
            var policy = RemoteConfigPolicy.Safe("safe policy");
            var result = RemoteConfigVersionGate.CanApplyConfig("GameC", record, policy, out string reason);
            Assert.IsFalse(result);
            Assert.AreEqual("SchemaVersionTooOld", reason);
        }

        [Test]
        public void CanApplyConfig_SafePolicy_Signed_SchemaV2_Eligible()
        {
            var record = RemoteConfigRecord.Create("GameD", RemoteConfigKey.CoinRewardAmount, "50", 2, true, true);
            var policy = RemoteConfigPolicy.Safe("custom policy");
            var result = RemoteConfigVersionGate.CanApplyConfig("GameD", record, policy, out string reason);
            Assert.IsTrue(result);
            Assert.AreEqual(string.Empty, reason);
        }

        // Helper method tests

        [Test]
        public void IsGameplaySafe_Helper_GameplayDifficulty_True()
        {
            var r = RemoteConfigRecord.Create("G", RemoteConfigKey.GameplayDifficulty, "v", 1, true, true);
            Assert.IsTrue(RemoteConfigVersionGate.IsGameplaySafe(r));
        }

        [Test]
        public void IsGameplaySafe_Helper_OfferPresentation_False()
        {
            var r = RemoteConfigRecord.Create("G", RemoteConfigKey.OfferPresentation, "v", 1, true, true);
            Assert.IsFalse(RemoteConfigVersionGate.IsGameplaySafe(r));
        }

        [Test]
        public void RequiresFallback_Helper_True()
        {
            var r = RemoteConfigRecord.Create("G", RemoteConfigKey.GameplayDifficulty, "v", 1, false, false);
            Assert.IsTrue(RemoteConfigVersionGate.RequiresFallback(r));
        }

        [Test]
        public void RequiresFallback_Helper_False()
        {
            var r = RemoteConfigRecord.Create("G", RemoteConfigKey.GameplayDifficulty, "v", 1, true, false);
            Assert.IsFalse(RemoteConfigVersionGate.RequiresFallback(r));
        }

        // RemoteConfigVersionLog tests

        [Test]
        public void RemoteConfigVersionLog_Record_And_Count()
        {
            var log = new RemoteConfigVersionLog();
            log.Record("GameA", "key1");
            log.Record("GameA", "key2");
            Assert.AreEqual(2, log.Count);
        }

        [Test]
        public void RemoteConfigVersionLog_HasEntry_True()
        {
            var log = new RemoteConfigVersionLog();
            log.Record("GameA", "key1");
            Assert.IsTrue(log.HasEntry("GameA"));
        }

        [Test]
        public void RemoteConfigVersionLog_Clear_ResetsCount()
        {
            var log = new RemoteConfigVersionLog();
            log.Record("GameA", "key1");
            log.Clear();
            Assert.AreEqual(0, log.Count);
        }

        [Test]
        public void RemoteConfigVersionLog_Record_NullGameId_Throws()
        {
            var log = new RemoteConfigVersionLog();
            Assert.Throws<ArgumentException>(() => log.Record(null, "key1"));
        }

        [Test]
        public void RemoteConfigVersionLog_Record_NullOrEmptyConfigKey_Throws()
        {
            var log = new RemoteConfigVersionLog();
            Assert.Throws<ArgumentException>(() => log.Record("GameA", null));
            Assert.Throws<ArgumentException>(() => log.Record("GameA", ""));
        }

        [Test]
        public void RemoteConfigVersionLog_HasEntry_IsOrdinal()
        {
            var log = new RemoteConfigVersionLog();
            log.Record("GameA", "key1");
            Assert.IsFalse(log.HasEntry("gamea"));
            Assert.IsFalse(log.HasEntry("Game"));
        }

        [Test]
        public void RemoteConfigRecord_Create_NegativeSchemaVersion_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                RemoteConfigRecord.Create("GameA", RemoteConfigKey.GameplayDifficulty, "v", -1, true, true));
        }

        [Test]
        public void CanApplyConfig_DefaultStructPolicy_FailsClosed()
        {
            var record = RemoteConfigRecord.Create("GameB", RemoteConfigKey.GameplayDifficulty, "v", 1, false, true);
            Assert.IsFalse(RemoteConfigVersionGate.CanApplyConfig("GameB", record, RemoteConfigPolicy.Default, out string expected));
            Assert.IsFalse(RemoteConfigVersionGate.CanApplyConfig("GameB", record, default(RemoteConfigPolicy), out string reason));
            Assert.AreEqual(expected, reason);
            Assert.AreEqual("UnsignedConfigRejected", reason);
        }

        [Test]
        public void CanApplyConfig_GameIdMismatch_Blocked()
        {
            var record = RemoteConfigRecord.Create("Wonder_Pop", RemoteConfigKey.GameplayDifficulty, "v", 1, true, true);
            Assert.IsFalse(RemoteConfigVersionGate.CanApplyConfig("Coral_Rescue", record, RemoteConfigPolicy.Default, out string reason));
            Assert.AreEqual("GameIdMismatch", reason);
        }

        [Test]
        public void CanApplyConfig_WonderPopParam_RecordDiffers_GameIdMismatch()
        {
            var record = RemoteConfigRecord.Create("Coral_Rescue", RemoteConfigKey.GameplayDifficulty, "v", 1, false, false);
            Assert.IsFalse(RemoteConfigVersionGate.CanApplyConfig("Wonder_Pop", record, RemoteConfigPolicy.Default, out string reason));
            Assert.AreEqual("GameIdMismatch", reason);
        }

        // Signature-optional policy: fallback for unsigned configs is still mandatory.
        private static readonly RemoteConfigPolicy NoSignature = new RemoteConfigPolicy(false, 1, "signature-optional test policy");

        [Test]
        public void CanApplyConfig_NoSignatureRequired_Unsigned_NoFallback_FallbackRequired()
        {
            var record = RemoteConfigRecord.Create("GameC", RemoteConfigKey.GameplayDifficulty, "v", 1, false, false);
            Assert.IsFalse(RemoteConfigVersionGate.CanApplyConfig("GameC", record, NoSignature, out string reason));
            Assert.AreEqual("FallbackRequired", reason);
        }

        [Test]
        public void CanApplyConfig_NoSignatureRequired_Unsigned_WithFallback_Eligible()
        {
            var record = RemoteConfigRecord.Create("GameC", RemoteConfigKey.GameplayDifficulty, "v", 1, false, true);
            Assert.IsTrue(RemoteConfigVersionGate.CanApplyConfig("GameC", record, NoSignature, out string reason));
            Assert.AreEqual(string.Empty, reason);
        }

        [Test]
        public void CanApplyConfig_NoSignatureRequired_Signed_NoFallback_Eligible()
        {
            var record = RemoteConfigRecord.Create("GameC", RemoteConfigKey.GameplayDifficulty, "v", 1, true, false);
            Assert.IsTrue(RemoteConfigVersionGate.CanApplyConfig("GameC", record, NoSignature, out string reason));
            Assert.AreEqual(string.Empty, reason);
        }

        [Test]
        public void CanApplyConfig_NoSignatureRequired_Unsigned_WithFallback_OldSchema_Blocked()
        {
            var record = RemoteConfigRecord.Create("GameC", RemoteConfigKey.GameplayDifficulty, "v", 0, false, true);
            Assert.IsFalse(RemoteConfigVersionGate.CanApplyConfig("GameC", record, NoSignature, out string reason));
            Assert.AreEqual("SchemaVersionTooOld", reason);
        }

        [Test]
        public void CanApplyConfig_WonderPop_Unsigned_SignatureOptionalPolicy_StillBlocked()
        {
            var record = RemoteConfigRecord.Create("Wonder_Pop", RemoteConfigKey.GameplayDifficulty, "v", 1, false, true);
            Assert.IsFalse(RemoteConfigVersionGate.CanApplyConfig("Wonder_Pop", record, NoSignature, out string reason));
            Assert.AreEqual("WonderPopUnsignedConfigProhibited", reason);
        }

        [Test]
        public void CanApplyConfig_RaisedMinSchema_Blocked()
        {
            var policy = new RemoteConfigPolicy(true, 3, "min schema v3");
            var record = RemoteConfigRecord.Create("GameC", RemoteConfigKey.GameplayDifficulty, "v", 2, true, true);
            Assert.IsFalse(RemoteConfigVersionGate.CanApplyConfig("GameC", record, policy, out string reason));
            Assert.AreEqual("SchemaVersionTooOld", reason);
        }

        [Test]
        public void RemoteConfigPolicy_Ctor_InvalidArgs_Throw()
        {
            Assert.Throws<ArgumentException>(() => new RemoteConfigPolicy(true, 1, null));
            Assert.Throws<ArgumentException>(() => new RemoteConfigPolicy(true, 1, ""));
            Assert.Throws<ArgumentOutOfRangeException>(() => new RemoteConfigPolicy(true, 0, "below floor"));
        }
    }
}
