using System;
using NUnit.Framework;
using Match3.Engine.Runtime.Analytics;

namespace Match3.Engine.Tests.Editor
{
    [TestFixture]
    public class TrackingDenialGateTests
    {
        private static readonly DenialPolicy NoBlock = new DenialPolicy(false, "No-block test policy");

        [Test]
        public void DenialRecord_Create_NullGameId_Throws()
        {
            Assert.Throws<ArgumentException>(() => DenialRecord.Create(null, DenialReason.UserOptOut, false));
        }

        [Test]
        public void DenialRecord_Create_EmptyGameId_Throws()
        {
            Assert.Throws<ArgumentException>(() => DenialRecord.Create("", DenialReason.UserOptOut, false));
        }

        [Test]
        public void DenialRecord_Create_ValidGameId_Succeeds()
        {
            var r = DenialRecord.Create("Coral_Rescue", DenialReason.UserOptOut, false);
            Assert.AreEqual("Coral_Rescue", r.GameId);
        }

        [Test]
        public void DenialRecord_IsNetworkDenied_AttDenied_True()
        {
            var r = DenialRecord.Create("Coral_Rescue", DenialReason.AttDenied, false);
            Assert.IsTrue(r.IsNetworkDenied);
        }

        [Test]
        public void DenialRecord_IsNetworkDenied_OfflineMode_True()
        {
            var r = DenialRecord.Create("Coral_Rescue", DenialReason.OfflineMode, false);
            Assert.IsTrue(r.IsNetworkDenied);
        }

        [Test]
        public void DenialRecord_IsNetworkDenied_UserOptOut_False()
        {
            var r = DenialRecord.Create("Coral_Rescue", DenialReason.UserOptOut, false);
            Assert.IsFalse(r.IsNetworkDenied);
        }

        [Test]
        public void DenialRecord_IsPermanentDenial_WhenNotTemporary_True()
        {
            var r = DenialRecord.Create("Coral_Rescue", DenialReason.UserOptOut, false);
            Assert.IsTrue(r.IsPermanentDenial);
        }

        [Test]
        public void DenialRecord_IsPermanentDenial_WhenTemporary_False()
        {
            var r = DenialRecord.Create("Coral_Rescue", DenialReason.UserOptOut, true);
            Assert.IsFalse(r.IsPermanentDenial);
        }

        [Test]
        public void DenialPolicy_Safe_Values_Correct()
        {
            var p = DenialPolicy.Safe("test");
            Assert.IsTrue(p.BlockOnAnyDenial);
            Assert.AreEqual("test", p.PolicyDescription);
        }

        [Test]
        public void DenialPolicy_Default_NotNull()
        {
            var p = DenialPolicy.Default;
            Assert.IsNotNull(p.PolicyDescription);
            Assert.IsTrue(p.BlockOnAnyDenial);
        }

        [Test]
        public void DenialPolicy_NullDescription_Throws()
        {
            Assert.Throws<ArgumentException>(() => DenialPolicy.Safe(null));
        }

        [Test]
        public void CanTrack_WonderPop_Blocked()
        {
            var record = DenialRecord.Create("Wonder_Pop", DenialReason.UserOptOut, false);
            var policy = DenialPolicy.Default;
            var result = TrackingDenialGate.CanTrack("Wonder_Pop", record, policy, out var reason);
            Assert.IsFalse(result);
            Assert.AreEqual("WonderPopTrackingProhibited", reason);
        }

        [Test]
        public void CanTrack_WonderPop_Lowercase_UnknownGame()
        {
            // Ordinal catalog lookup: a case variant of Wonder_Pop is not a catalog ID, so it fails closed with UnknownGame (never eligible, never the Wonder_Pop reason).
            var record = DenialRecord.Create("wonder_pop", DenialReason.PolicyDefault, false);
            var policy = NoBlock;
            var result = TrackingDenialGate.CanTrack("wonder_pop", record, policy, out var reason);
            Assert.IsFalse(result);
            Assert.AreEqual("UnknownGame", reason);
        }

        [Test]
        public void CanTrack_UserOptOut_SafePolicy_Blocked()
        {
            var record = DenialRecord.Create("Coral_Rescue", DenialReason.UserOptOut, false);
            var policy = DenialPolicy.Safe("block all");
            var result = TrackingDenialGate.CanTrack("Coral_Rescue", record, policy, out var reason);
            Assert.IsFalse(result);
            Assert.AreEqual("UserOptedOut", reason);
        }

        [Test]
        public void CanTrack_BlockOnAnyDenial_AttDenied_Blocked()
        {
            var record = DenialRecord.Create("Coral_Rescue", DenialReason.AttDenied, false);
            var policy = DenialPolicy.Safe("block all");
            var result = TrackingDenialGate.CanTrack("Coral_Rescue", record, policy, out var reason);
            Assert.IsFalse(result);
            Assert.AreEqual("TrackingDeniedByPolicy", reason);
        }

        [Test]
        public void CanTrack_PolicyDefault_Reason_Eligible_WithSafePolicy()
        {
            var record = DenialRecord.Create("Coral_Rescue", DenialReason.PolicyDefault, false);
            var policy = DenialPolicy.Safe("block all");
            // BlockOnAnyDenial does not apply to PolicyDefault records, and PolicyDefault is not a network denial.
            var result = TrackingDenialGate.CanTrack("Coral_Rescue", record, policy, out var reason);
            Assert.IsTrue(result);
            Assert.AreEqual(string.Empty, reason);
        }

        [Test]
        public void CanTrack_AttDenied_NoBlockPolicy_NetworkDenied()
        {
            var record = DenialRecord.Create("Coral_Rescue", DenialReason.AttDenied, false);
            // With BlockOnAnyDenial=false the policy rule does not fire; the network-denial rule still blocks.
            var policyNoBlock = NoBlock;
            var result = TrackingDenialGate.CanTrack("Coral_Rescue", record, policyNoBlock, out var reason);
            Assert.IsFalse(result);
            Assert.AreEqual("NetworkDenied", reason);
        }

        [Test]
        public void CanTrack_OfflineMode_NoBlockPolicy_NetworkDenied()
        {
            var record = DenialRecord.Create("Coral_Rescue", DenialReason.OfflineMode, false);
            var policyNoBlock = NoBlock;
            var result = TrackingDenialGate.CanTrack("Coral_Rescue", record, policyNoBlock, out var reason);
            Assert.IsFalse(result);
            Assert.AreEqual("NetworkDenied", reason);
        }

        [Test]
        public void CanTrack_UserOptOut_RelaxedPolicy_StillBlocked()
        {
            // Most relaxed constructible policy (BlockOnAnyDenial=false) cannot override an opt-out,
            // permanent or temporary.
            var permanent = DenialRecord.Create("Coral_Rescue", DenialReason.UserOptOut, false);
            Assert.IsFalse(TrackingDenialGate.CanTrack("Coral_Rescue", permanent, NoBlock, out var reason));
            Assert.AreEqual("UserOptedOut", reason);
            var temporary = DenialRecord.Create("Coral_Rescue", DenialReason.UserOptOut, true);
            Assert.IsFalse(TrackingDenialGate.CanTrack("Coral_Rescue", temporary, NoBlock, out reason));
            Assert.AreEqual("UserOptedOut", reason);
        }

        [Test]
        public void CanTrack_PolicyDefault_RelaxedPolicy_Eligible()
        {
            var record = DenialRecord.Create("Coral_Rescue", DenialReason.PolicyDefault, false);
            Assert.IsTrue(TrackingDenialGate.CanTrack("Coral_Rescue", record, NoBlock, out var reason));
            Assert.AreEqual(string.Empty, reason);
        }

        [Test]
        public void CanTrack_WonderPopParam_RecordDiffers_GameIdMismatch()
        {
            var record = DenialRecord.Create("Coral_Rescue", DenialReason.PolicyDefault, false);
            Assert.IsFalse(TrackingDenialGate.CanTrack("Wonder_Pop", record, DenialPolicy.Default, out var reason));
            Assert.AreEqual("GameIdMismatch", reason);
        }

        [Test]
        public void IsNetworkDenied_Helper_AttDenied_True()
        {
            var r = DenialRecord.Create("Coral_Rescue", DenialReason.AttDenied, false);
            Assert.IsTrue(TrackingDenialGate.IsNetworkDenied(r));
        }

        [Test]
        public void IsNetworkDenied_Helper_UserOptOut_False()
        {
            var r = DenialRecord.Create("Coral_Rescue", DenialReason.UserOptOut, false);
            Assert.IsFalse(TrackingDenialGate.IsNetworkDenied(r));
        }

        [Test]
        public void DenialLog_Record_And_Count()
        {
            var log = new DenialLog();
            log.Record("Coral_Rescue", "reason1");
            Assert.AreEqual(1, log.Count);
        }

        [Test]
        public void DenialLog_HasEntry_True()
        {
            var log = new DenialLog();
            log.Record("Coral_Rescue", "reason1");
            Assert.IsTrue(log.HasEntry("Coral_Rescue"));
        }

        [Test]
        public void DenialLog_Clear_ResetsCount()
        {
            var log = new DenialLog();
            log.Record("Coral_Rescue", "reason1");
            log.Clear();
            Assert.AreEqual(0, log.Count);
        }

        [Test]
        public void DenialLog_Record_NullGameId_Throws()
        {
            var log = new DenialLog();
            Assert.Throws<ArgumentException>(() => log.Record(null, "reason"));
        }

        [Test]
        public void DenialLog_Record_NullOrEmptyReason_Throws()
        {
            var log = new DenialLog();
            Assert.Throws<ArgumentException>(() => log.Record("Coral_Rescue", null));
            Assert.Throws<ArgumentException>(() => log.Record("Coral_Rescue", ""));
        }

        [Test]
        public void DenialLog_HasEntry_IsOrdinal()
        {
            var log = new DenialLog();
            log.Record("Coral_Rescue", "reason1");
            Assert.IsFalse(log.HasEntry("coral_rescue"));
            Assert.IsFalse(log.HasEntry("Coral"));
            // Soft hyphen (U+00AD) is ignorable under culture-sensitive comparison; Ordinal must not match it.
            var softHyphenLog = new DenialLog();
            softHyphenLog.Record("Coral\u00AD_Rescue", "evt");
            Assert.IsFalse(softHyphenLog.HasEntry("Coral_Rescue"));
        }

        [Test]
        public void DenialPolicy_Ctor_NullOrEmptyDescription_Throws()
        {
            Assert.Throws<ArgumentException>(() => new DenialPolicy(false, null));
            Assert.Throws<ArgumentException>(() => new DenialPolicy(false, ""));
        }

        [Test]
        public void CanTrack_DefaultStructPolicy_FailsClosed()
        {
            var record = DenialRecord.Create("Coral_Rescue", DenialReason.AttDenied, false);
            Assert.IsFalse(TrackingDenialGate.CanTrack("Coral_Rescue", record, DenialPolicy.Default, out var expected));
            Assert.IsFalse(TrackingDenialGate.CanTrack("Coral_Rescue", record, default(DenialPolicy), out var reason));
            Assert.AreEqual(expected, reason);
            Assert.AreEqual("TrackingDeniedByPolicy", reason);
        }

        [Test]
        public void CanTrack_GameIdMismatch_Blocked()
        {
            var record = DenialRecord.Create("Wonder_Pop", DenialReason.PolicyDefault, false);
            Assert.IsFalse(TrackingDenialGate.CanTrack("Coral_Rescue", record, NoBlock, out var reason));
            Assert.AreEqual("GameIdMismatch", reason);
        }


        [Test]
        public void NullOrEmptyGameId_DefaultRecord_GameIdMissing()
        {
            foreach (var gameId in new[] { null, string.Empty })
            {
                Assert.IsFalse(TrackingDenialGate.CanTrack(gameId, default(DenialRecord), default(DenialPolicy), out var reason));
                Assert.AreEqual("GameIdMissing", reason);
            }
        }

        [Test]
        public void UnknownOrCaseVariantGameId_DefaultRecord_UnknownGame()
        {
            foreach (var gameId in new[] { "Unknown_Game", "coral_rescue", "CORAL_RESCUE", " Coral_Rescue", "Coral_Rescue ", "wonder_pop", "WONDER_POP" })
            {
                Assert.IsFalse(TrackingDenialGate.CanTrack(gameId, default(DenialRecord), default(DenialPolicy), out var reason));
                Assert.AreEqual("UnknownGame", reason);
            }
        }
    }
}
