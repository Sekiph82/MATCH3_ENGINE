using System;
using NUnit.Framework;
using Match3.Engine.Runtime.Analytics;

namespace Match3.Engine.Tests.Editor
{
    [TestFixture]
    public class TrackingDenialGateTests
    {
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
            var r = DenialRecord.Create("TestGame", DenialReason.UserOptOut, false);
            Assert.AreEqual("TestGame", r.GameId);
        }

        [Test]
        public void DenialRecord_IsNetworkDenied_AttDenied_True()
        {
            var r = DenialRecord.Create("G", DenialReason.AttDenied, false);
            Assert.IsTrue(r.IsNetworkDenied);
        }

        [Test]
        public void DenialRecord_IsNetworkDenied_OfflineMode_True()
        {
            var r = DenialRecord.Create("G", DenialReason.OfflineMode, false);
            Assert.IsTrue(r.IsNetworkDenied);
        }

        [Test]
        public void DenialRecord_IsNetworkDenied_UserOptOut_False()
        {
            var r = DenialRecord.Create("G", DenialReason.UserOptOut, false);
            Assert.IsFalse(r.IsNetworkDenied);
        }

        [Test]
        public void DenialRecord_IsPermanentDenial_WhenNotTemporary_True()
        {
            var r = DenialRecord.Create("G", DenialReason.UserOptOut, false);
            Assert.IsTrue(r.IsPermanentDenial);
        }

        [Test]
        public void DenialRecord_IsPermanentDenial_WhenTemporary_False()
        {
            var r = DenialRecord.Create("G", DenialReason.UserOptOut, true);
            Assert.IsFalse(r.IsPermanentDenial);
        }

        [Test]
        public void DenialPolicy_Safe_Values_Correct()
        {
            var p = DenialPolicy.Safe("test");
            Assert.IsTrue(p.BlockOnAnyDenial);
            Assert.IsTrue(p.RequireExplicitUnblock);
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
        public void CanTrack_WonderPop_Lowercase_NotBlocked()
        {
            var record = DenialRecord.Create("wonder_pop", DenialReason.PolicyDefault, false);
            var policy = new DenialPolicy(); // BlockOnAnyDenial=false by default
            var result = TrackingDenialGate.CanTrack("wonder_pop", record, policy, out var reason);
            Assert.IsTrue(result);
            Assert.AreEqual(string.Empty, reason);
        }

        [Test]
        public void CanTrack_BlockOnAnyDenial_UserOptOut_Blocked()
        {
            var record = DenialRecord.Create("GameA", DenialReason.UserOptOut, false);
            var policy = DenialPolicy.Safe("block all");
            var result = TrackingDenialGate.CanTrack("GameA", record, policy, out var reason);
            Assert.IsFalse(result);
            Assert.AreEqual("TrackingDeniedByPolicy", reason);
        }

        [Test]
        public void CanTrack_BlockOnAnyDenial_AttDenied_Blocked()
        {
            var record = DenialRecord.Create("GameA", DenialReason.AttDenied, false);
            var policy = DenialPolicy.Safe("block all");
            var result = TrackingDenialGate.CanTrack("GameA", record, policy, out var reason);
            Assert.IsFalse(result);
            Assert.AreEqual("TrackingDeniedByPolicy", reason);
        }

        [Test]
        public void CanTrack_PolicyDefault_Reason_Eligible_WithSafePolicy()
        {
            var record = DenialRecord.Create("GameA", DenialReason.PolicyDefault, false);
            var policy = DenialPolicy.Safe("block all");
            // Guard 2 only blocks non-PolicyDefault, so PolicyDefault passes Guard 2
            // Guard 3: PolicyDefault is not network denied
            var result = TrackingDenialGate.CanTrack("GameA", record, policy, out var reason);
            Assert.IsTrue(result);
            Assert.AreEqual(string.Empty, reason);
        }

        [Test]
        public void CanTrack_NetworkDenied_AttDenied_Blocked_ByGuard3()
        {
            var record = DenialRecord.Create("GameA", DenialReason.AttDenied, false);
            // BlockOnAnyDenial=false so Guard 2 doesn't trigger; Guard 3 triggers
            var policy = DenialPolicy.Safe("desc");
            // Need BlockOnAnyDenial=false - use PolicyDefault record to pass Guard 2, but AttDenied IS network denied
            // Actually let's use a policy with BlockOnAnyDenial=false
            // DenialPolicy has no public ctor with false, so create via reflection or use PolicyDefault reason to bypass guard 2
            // Guard 2: policy.BlockOnAnyDenial && record.Reason != PolicyDefault
            // If BlockOnAnyDenial=false → guard 2 skipped. Use default struct:
            var policyNoBlock = new DenialPolicy(); // default struct: BlockOnAnyDenial=false
            var result = TrackingDenialGate.CanTrack("GameA", record, policyNoBlock, out var reason);
            Assert.IsFalse(result);
            Assert.AreEqual("NetworkDenied", reason);
        }

        [Test]
        public void CanTrack_NetworkDenied_OfflineMode_Blocked_ByGuard3()
        {
            var record = DenialRecord.Create("GameA", DenialReason.OfflineMode, false);
            var policyNoBlock = new DenialPolicy(); // BlockOnAnyDenial=false
            var result = TrackingDenialGate.CanTrack("GameA", record, policyNoBlock, out var reason);
            Assert.IsFalse(result);
            Assert.AreEqual("NetworkDenied", reason);
        }

        [Test]
        public void CanTrack_UserOptOut_BlockOnAnyDenialFalse_NetworkDeniedFalse_Eligible()
        {
            var record = DenialRecord.Create("GameA", DenialReason.UserOptOut, false);
            var policyNoBlock = new DenialPolicy(); // BlockOnAnyDenial=false
            var result = TrackingDenialGate.CanTrack("GameA", record, policyNoBlock, out var reason);
            Assert.IsTrue(result);
            Assert.AreEqual(string.Empty, reason);
        }

        [Test]
        public void IsNetworkDenied_Helper_AttDenied_True()
        {
            var r = DenialRecord.Create("G", DenialReason.AttDenied, false);
            Assert.IsTrue(TrackingDenialGate.IsNetworkDenied(r));
        }

        [Test]
        public void IsNetworkDenied_Helper_UserOptOut_False()
        {
            var r = DenialRecord.Create("G", DenialReason.UserOptOut, false);
            Assert.IsFalse(TrackingDenialGate.IsNetworkDenied(r));
        }

        [Test]
        public void RequiresExplicitUnblock_Helper_Safe_True()
        {
            var p = DenialPolicy.Safe("desc");
            Assert.IsTrue(TrackingDenialGate.RequiresExplicitUnblock(p));
        }

        [Test]
        public void DenialLog_Record_And_Count()
        {
            var log = new DenialLog();
            log.Record("GameA", "reason1");
            Assert.AreEqual(1, log.Count);
        }

        [Test]
        public void DenialLog_HasEntry_True()
        {
            var log = new DenialLog();
            log.Record("GameA", "reason1");
            Assert.IsTrue(log.HasEntry("GameA"));
        }

        [Test]
        public void DenialLog_Clear_ResetsCount()
        {
            var log = new DenialLog();
            log.Record("GameA", "reason1");
            log.Clear();
            Assert.AreEqual(0, log.Count);
        }

        [Test]
        public void DenialLog_Record_NullGameId_Throws()
        {
            var log = new DenialLog();
            Assert.Throws<ArgumentException>(() => log.Record(null, "reason"));
        }
    }
}
