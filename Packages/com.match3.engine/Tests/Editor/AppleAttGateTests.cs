using NUnit.Framework;
using Match3.Engine.Runtime.Privacy;

namespace Match3.Engine.Tests.Editor
{
    public class AppleAttGateTests
    {
        // AttRecord tests
        [Test]
        public void AttRecord_Create_NullGameId_Throws()
        {
            Assert.Throws<System.ArgumentException>(() =>
                AttRecord.Create(null, AttAuthorizationStatus.NotDetermined, false, false));
        }

        [Test]
        public void AttRecord_Create_EmptyGameId_Throws()
        {
            Assert.Throws<System.ArgumentException>(() =>
                AttRecord.Create(string.Empty, AttAuthorizationStatus.NotDetermined, false, false));
        }

        [Test]
        public void AttRecord_Create_Valid_Succeeds()
        {
            var r = AttRecord.Create("Coral_Rescue", AttAuthorizationStatus.Authorized, false, true);
            Assert.AreEqual("Coral_Rescue", r.GameId);
            Assert.AreEqual(AttAuthorizationStatus.Authorized, r.Status);
        }

        [Test]
        public void AttRecord_IsTrackingAuthorized_Authorized_NotChild_True()
        {
            var r = AttRecord.Create("Coral_Rescue", AttAuthorizationStatus.Authorized, false, true);
            Assert.IsTrue(r.IsTrackingAuthorized);
        }

        [Test]
        public void AttRecord_IsTrackingAuthorized_Authorized_IsChild_False()
        {
            var r = AttRecord.Create("Coral_Rescue", AttAuthorizationStatus.Authorized, true, true);
            Assert.IsFalse(r.IsTrackingAuthorized);
        }

        [Test]
        public void AttRecord_IsTrackingAuthorized_Denied_False()
        {
            var r = AttRecord.Create("Coral_Rescue", AttAuthorizationStatus.Denied, false, true);
            Assert.IsFalse(r.IsTrackingAuthorized);
        }

        [Test]
        public void AttRecord_RequiresAttRequest_NotDetermined_CrossAppProposed_NotChild_True()
        {
            var r = AttRecord.Create("Coral_Rescue", AttAuthorizationStatus.NotDetermined, false, true);
            Assert.IsTrue(r.RequiresAttRequest);
        }

        [Test]
        public void AttRecord_RequiresAttRequest_Authorized_False()
        {
            var r = AttRecord.Create("Coral_Rescue", AttAuthorizationStatus.Authorized, false, true);
            Assert.IsFalse(r.RequiresAttRequest);
        }

        [Test]
        public void AttRecord_RequiresAttRequest_Child_False()
        {
            var r = AttRecord.Create("Coral_Rescue", AttAuthorizationStatus.NotDetermined, true, true);
            Assert.IsFalse(r.RequiresAttRequest);
        }

        // AttPolicy tests
        [Test]
        public void AttPolicy_Safe_Values_Correct()
        {
            var p = AttPolicy.Safe("Test policy");
            Assert.IsFalse(p.AllowCrossAppTracking);
#pragma warning disable CS0618 // BlockChildProfiles is obsolete (ignored); asserting the retained value only
            Assert.IsTrue(p.BlockChildProfiles);
#pragma warning restore CS0618
        }

        [Test]
        public void AttPolicy_Default_NotNull()
        {
            var p = AttPolicy.Default;
            Assert.IsNotNull(p.PolicyDescription);
            Assert.IsFalse(string.IsNullOrEmpty(p.PolicyDescription));
        }

        [Test]
        public void AttPolicy_NullDescription_Throws()
        {
            Assert.Throws<System.ArgumentException>(() => AttPolicy.Safe(null));
        }

        // CanRequestAttPermission tests
        [Test]
        public void CanRequestAttPermission_WonderPop_Blocked()
        {
            var record = AttRecord.Create("Wonder_Pop", AttAuthorizationStatus.NotDetermined, false, true);
            // Most relaxed constructible policy, to prove the Wonder_Pop guard is not policy-relaxable
            var policy = MakeAllowingPolicy(blockChild: false);
            var result = AppleAttGate.CanRequestAttPermission("Wonder_Pop", record, policy, out var reason);
            Assert.IsFalse(result);
            Assert.AreEqual("WonderPopAttProhibited", reason);
        }

        [Test]
        public void CanRequestAttPermission_WonderPop_Lowercase_UnknownGame()
        {
            // "wonder_pop" (lowercase) should NOT be blocked by Wonder_Pop guard (Ordinal comparison)
            var record = AttRecord.Create("wonder_pop", AttAuthorizationStatus.NotDetermined, false, true);
            var policy = AttPolicy.Safe("test");
            var result = AppleAttGate.CanRequestAttPermission("wonder_pop", record, policy, out var reason);
            // Safe policy blocks cross-app tracking, so result is false but for different reason
            Assert.IsFalse(result);
            Assert.AreNotEqual("WonderPopAttProhibited", reason);
            // Ordinal catalog lookup: the case variant is not a catalog ID, so it fails closed with UnknownGame.
            Assert.IsFalse(result);
            Assert.AreEqual("UnknownGame", reason);
        }

        [Test]
        public void CanRequestAttPermission_SafePolicy_CrossAppTrackingDisabled_Blocked()
        {
            var record = AttRecord.Create("Garage_Legends", AttAuthorizationStatus.NotDetermined, false, true);
            var policy = AttPolicy.Safe("test"); // AllowCrossAppTracking=false
            var result = AppleAttGate.CanRequestAttPermission("Garage_Legends", record, policy, out var reason);
            Assert.IsFalse(result);
            Assert.AreEqual("CrossAppTrackingDisabled", reason);
        }

        private static AttPolicy MakeAllowingPolicy(bool blockChild = true)
        {
            // Validated public constructor; never rely on default(AttPolicy).
            return new AttPolicy(true, blockChild, "Allow policy for tests");
        }

        [Test]
        public void CanRequestAttPermission_AllowTracking_ChildProfile_Blocked()
        {
            var record = AttRecord.Create("Garage_Legends", AttAuthorizationStatus.NotDetermined, true, true);
            var policy = MakeAllowingPolicy(blockChild: true);
            var result = AppleAttGate.CanRequestAttPermission("Garage_Legends", record, policy, out var reason);
            Assert.IsFalse(result);
            Assert.AreEqual("ChildProfileBlocked", reason);
        }

        [Test]
        public void CanRequestAttPermission_AllowTracking_NotProposed_Blocked()
        {
            var record = AttRecord.Create("Garage_Legends", AttAuthorizationStatus.NotDetermined, false, false);
            var policy = MakeAllowingPolicy(blockChild: true);
            var result = AppleAttGate.CanRequestAttPermission("Garage_Legends", record, policy, out var reason);
            Assert.IsFalse(result);
            Assert.AreEqual("CrossAppTrackingNotProposed", reason);
        }

        [Test]
        public void CanRequestAttPermission_AllowTracking_Adult_Proposed_Eligible()
        {
            var record = AttRecord.Create("Garage_Legends", AttAuthorizationStatus.NotDetermined, false, true);
            var policy = MakeAllowingPolicy(blockChild: true);
            var result = AppleAttGate.CanRequestAttPermission("Garage_Legends", record, policy, out var reason);
            Assert.IsTrue(result);
            Assert.AreEqual(string.Empty, reason);
        }

        [Test]
        public void CanRequestAttPermission_MostRelaxedPolicy_ChildProfile_StillBlocked()
        {
            // Child-profile protection is unconditional: even AllowCrossAppTracking=true, BlockChildProfiles=false blocks.
            var record = AttRecord.Create("Garage_Legends", AttAuthorizationStatus.NotDetermined, true, true);
            var policy = MakeAllowingPolicy(blockChild: false);
            var result = AppleAttGate.CanRequestAttPermission("Garage_Legends", record, policy, out var reason);
            Assert.IsFalse(result);
            Assert.AreEqual("ChildProfileBlocked", reason);
        }

        // Helper method tests
        [Test]
        public void IsTrackingAuthorized_Helper_Authorized_NotChild_True()
        {
            var r = AttRecord.Create("Coral_Rescue", AttAuthorizationStatus.Authorized, false, false);
            Assert.IsTrue(AppleAttGate.IsTrackingAuthorized(r));
        }

        [Test]
        public void IsTrackingAuthorized_Helper_Child_False()
        {
            var r = AttRecord.Create("Coral_Rescue", AttAuthorizationStatus.Authorized, true, false);
            Assert.IsFalse(AppleAttGate.IsTrackingAuthorized(r));
        }

        [Test]
        public void RequiresAttRequest_Helper_NotDetermined_Proposed_NotChild_True()
        {
            var r = AttRecord.Create("Coral_Rescue", AttAuthorizationStatus.NotDetermined, false, true);
            Assert.IsTrue(AppleAttGate.RequiresAttRequest(r));
        }

        [Test]
        public void RequiresAttRequest_Helper_Authorized_False()
        {
            var r = AttRecord.Create("Coral_Rescue", AttAuthorizationStatus.Authorized, false, true);
            Assert.IsFalse(AppleAttGate.RequiresAttRequest(r));
        }

        // AttLog tests
        [Test]
        public void AttLog_Record_And_Count()
        {
            var log = new AttLog();
            log.Record("Coral_Rescue", "Authorized");
            log.Record("Garage_Legends", "Denied");
            Assert.AreEqual(2, log.Count);
        }

        [Test]
        public void AttLog_HasEntry_True()
        {
            var log = new AttLog();
            log.Record("Coral_Rescue", "Authorized");
            Assert.IsTrue(log.HasEntry("Coral_Rescue"));
        }

        [Test]
        public void AttLog_Clear_ResetsCount()
        {
            var log = new AttLog();
            log.Record("Coral_Rescue", "Authorized");
            log.Clear();
            Assert.AreEqual(0, log.Count);
        }

        [Test]
        public void AttLog_Record_NullGameId_Throws()
        {
            var log = new AttLog();
            Assert.Throws<System.ArgumentException>(() => log.Record(null, "event"));
        }

        // QA remediation: fail-closed default policy, GameId mismatch guard, log argument validation
        [Test]
        public void CanRequestAttPermission_DefaultStructPolicy_FailsClosed()
        {
            var record = AttRecord.Create("Coral_Rescue", AttAuthorizationStatus.NotDetermined, false, true);
            var expected = AppleAttGate.CanRequestAttPermission("Coral_Rescue", record, AttPolicy.Default, out var expectedReason);
            var result = AppleAttGate.CanRequestAttPermission("Coral_Rescue", record, default(AttPolicy), out var reason);
            Assert.IsFalse(expected);
            Assert.IsFalse(result);
            Assert.AreEqual("CrossAppTrackingDisabled", expectedReason);
            Assert.AreEqual(expectedReason, reason);
        }

        [Test]
        public void CanRequestAttPermission_GameIdMismatch_Blocked()
        {
            var record = AttRecord.Create("Wonder_Pop", AttAuthorizationStatus.NotDetermined, false, true);
            var result = AppleAttGate.CanRequestAttPermission("Coral_Rescue", record, AttPolicy.Default, out var reason);
            Assert.IsFalse(result);
            Assert.AreEqual("GameIdMismatch", reason);
        }

        [Test]
        public void AttLog_Record_NullOrEmptySecondArg_Throws()
        {
            var log = new AttLog();
            Assert.Throws<System.ArgumentException>(() => log.Record("Coral_Rescue", null));
            Assert.Throws<System.ArgumentException>(() => log.Record("Coral_Rescue", string.Empty));
            Assert.AreEqual(0, log.Count);
        }

        [Test]
        public void AttPolicy_PublicCtor_NullOrEmptyDescription_Throws()
        {
            Assert.Throws<System.ArgumentException>(() => new AttPolicy(true, false, null));
            Assert.Throws<System.ArgumentException>(() => new AttPolicy(true, false, string.Empty));
        }

        [Test]
        public void AttLog_HasEntry_IsOrdinal()
        {
            var log = new AttLog();
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
                Assert.IsFalse(AppleAttGate.CanRequestAttPermission(gameId, default(AttRecord), default(AttPolicy), out var reason));
                Assert.AreEqual("GameIdMissing", reason);
            }
        }

        [Test]
        public void UnknownOrCaseVariantGameId_DefaultRecord_UnknownGame()
        {
            foreach (var gameId in new[] { "Unknown_Game", "coral_rescue", "CORAL_RESCUE", " Coral_Rescue", "Coral_Rescue ", "wonder_pop", "WONDER_POP" })
            {
                Assert.IsFalse(AppleAttGate.CanRequestAttPermission(gameId, default(AttRecord), default(AttPolicy), out var reason));
                Assert.AreEqual("UnknownGame", reason);
            }
        }
    }
}
