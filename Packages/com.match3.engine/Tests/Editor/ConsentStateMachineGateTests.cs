using NUnit.Framework;
using Match3.Engine.Runtime.Privacy;
using System;

namespace Match3.Engine.Tests.Editor
{
    [TestFixture]
    public class ConsentStateMachineGateTests
    {
        [Test]
        public void ConsentRecord_Create_NullGameId_Throws()
        {
            Assert.Throws<ArgumentException>(() => ConsentRecord.Create(null, ConsentState.NotRequested, false, false));
        }

        [Test]
        public void ConsentRecord_Create_EmptyGameId_Throws()
        {
            Assert.Throws<ArgumentException>(() => ConsentRecord.Create("", ConsentState.NotRequested, false, false));
        }

        [Test]
        public void ConsentRecord_Create_Valid_Succeeds()
        {
            var record = ConsentRecord.Create("Coral_Rescue", ConsentState.Pending, false, false);
            Assert.AreEqual("Coral_Rescue", record.GameId);
            Assert.AreEqual(ConsentState.Pending, record.State);
        }

        [Test]
        public void ConsentRecord_RequiresParentalGate_IsMinor_True()
        {
            var record = ConsentRecord.Create("Coral_Rescue", ConsentState.NotRequested, true, false);
            Assert.IsTrue(record.RequiresParentalGate);
        }

        [Test]
        public void ConsentRecord_RequiresParentalGate_NotMinor_False()
        {
            var record = ConsentRecord.Create("Coral_Rescue", ConsentState.NotRequested, false, false);
            Assert.IsFalse(record.RequiresParentalGate);
        }

        [Test]
        public void ConsentRecord_IsConsentActive_Granted_True()
        {
            var record = ConsentRecord.Create("Coral_Rescue", ConsentState.Granted, false, false);
            Assert.IsTrue(record.IsConsentActive);
        }

        [Test]
        public void ConsentRecord_IsConsentActive_Denied_False()
        {
            var record = ConsentRecord.Create("Coral_Rescue", ConsentState.Denied, false, false);
            Assert.IsFalse(record.IsConsentActive);
        }

        [Test]
        public void ConsentRecord_IsConsentActive_Pending_False()
        {
            var record = ConsentRecord.Create("Coral_Rescue", ConsentState.Pending, false, false);
            Assert.IsFalse(record.IsConsentActive);
        }

        [Test]
        public void ConsentPolicy_Safe_Values_Correct()
        {
            var policy = ConsentPolicy.Safe("Test policy");
            Assert.IsTrue(policy.RequireExplicitConsent);
            Assert.IsTrue(policy.BlockMinorsWithoutParentalGate);
            Assert.IsTrue(policy.BlockRestrictedRegions);
            Assert.AreEqual("Test policy", policy.PolicyDescription);
        }

        [Test]
        public void ConsentPolicy_Default_NotNull()
        {
            var policy = ConsentPolicy.Default;
            Assert.IsNotNull(policy.PolicyDescription);
            Assert.IsTrue(policy.RequireExplicitConsent);
        }

        [Test]
        public void ConsentPolicy_NullDescription_Throws()
        {
            Assert.Throws<ArgumentException>(() => ConsentPolicy.Safe(null));
        }

        [Test]
        public void CanTransitionState_WonderPop_Blocked()
        {
            var record = ConsentRecord.Create("Wonder_Pop", ConsentState.Pending, false, false);
            var policy = ConsentPolicy.Default;
            var result = ConsentStateMachineGate.CanTransitionState("Wonder_Pop", record, policy, ConsentState.Granted, out string reason);
            Assert.IsFalse(result);
            Assert.AreEqual("WonderPopConsentProhibited", reason);
        }

        [Test]
        public void CanTransitionState_WonderPop_Lowercase_UnknownGame()
        {
            // Ordinal catalog lookup: a case variant of Wonder_Pop is not a catalog ID, so it fails closed with UnknownGame (never eligible, never the Wonder_Pop reason).
            var record = ConsentRecord.Create("wonder_pop", ConsentState.Pending, false, false);
            var policy = ConsentPolicy.Safe("permissive");
            var result = ConsentStateMachineGate.CanTransitionState("wonder_pop", record, policy, ConsentState.Granted, out string reason);
            Assert.IsFalse(result);
            Assert.AreEqual("UnknownGame", reason);
        }

        [Test]
        public void CanTransitionState_Minor_SafePolicy_Blocked()
        {
            var record = ConsentRecord.Create("Coral_Rescue", ConsentState.Pending, true, false);
            var policy = ConsentPolicy.Default;
            var result = ConsentStateMachineGate.CanTransitionState("Coral_Rescue", record, policy, ConsentState.Granted, out string reason);
            Assert.IsFalse(result);
            Assert.AreEqual("MinorRequiresParentalGate", reason);
        }

        [Test]
        public void CanTransitionState_RestrictedRegion_SafePolicy_Blocked()
        {
            var record = ConsentRecord.Create("Coral_Rescue", ConsentState.Pending, false, true);
            var policy = ConsentPolicy.Default;
            var result = ConsentStateMachineGate.CanTransitionState("Coral_Rescue", record, policy, ConsentState.Granted, out string reason);
            Assert.IsFalse(result);
            Assert.AreEqual("RestrictedRegionBlocked", reason);
        }

        [Test]
        public void CanTransitionState_ExplicitConsentRequired_NotRequestedToGranted_Blocked()
        {
            var record = ConsentRecord.Create("Coral_Rescue", ConsentState.NotRequested, false, false);
            var policy = ConsentPolicy.Default;
            var result = ConsentStateMachineGate.CanTransitionState("Coral_Rescue", record, policy, ConsentState.Granted, out string reason);
            Assert.IsFalse(result);
            Assert.AreEqual("ExplicitConsentRequired", reason);
        }

        [Test]
        public void CanTransitionState_ExplicitConsentRequired_PendingToGranted_Eligible()
        {
            var record = ConsentRecord.Create("Coral_Rescue", ConsentState.Pending, false, false);
            var policy = ConsentPolicy.Default;
            var result = ConsentStateMachineGate.CanTransitionState("Coral_Rescue", record, policy, ConsentState.Granted, out string reason);
            Assert.IsTrue(result);
            Assert.AreEqual(string.Empty, reason);
        }

        [Test]
        public void CanTransitionState_NoRestrictions_Adult_NonRestricted_Granted_Eligible()
        {
            var record = ConsentRecord.Create("Coral_Rescue", ConsentState.Pending, false, false);
            var policy = ConsentPolicy.Safe("permissive");
            // All-true policy but adult, non-restricted, state=Pending -> only ExplicitConsent guard applies for Granted, and state != NotRequested
            var result = ConsentStateMachineGate.CanTransitionState("Coral_Rescue", record, policy, ConsentState.Granted, out string reason);
            Assert.IsTrue(result);
        }

        [Test]
        public void CanTransitionState_RevokeTransition_Eligible()
        {
            var record = ConsentRecord.Create("Coral_Rescue", ConsentState.Granted, false, false);
            var policy = ConsentPolicy.Default;
            var result = ConsentStateMachineGate.CanTransitionState("Coral_Rescue", record, policy, ConsentState.Revoked, out string reason);
            Assert.IsTrue(result);
            Assert.AreEqual(string.Empty, reason);
        }

        [Test]
        public void CanTransitionState_DenyTransition_Eligible()
        {
            var record = ConsentRecord.Create("Coral_Rescue", ConsentState.Pending, false, false);
            var policy = ConsentPolicy.Default;
            var result = ConsentStateMachineGate.CanTransitionState("Coral_Rescue", record, policy, ConsentState.Denied, out string reason);
            Assert.IsTrue(result);
            Assert.AreEqual(string.Empty, reason);
        }

        [Test]
        public void RequiresParentalGate_Helper_Minor_True()
        {
            var record = ConsentRecord.Create("Coral_Rescue", ConsentState.NotRequested, true, false);
            Assert.IsTrue(ConsentStateMachineGate.RequiresParentalGate(record));
        }

        [Test]
        public void RequiresParentalGate_Helper_Adult_False()
        {
            var record = ConsentRecord.Create("Coral_Rescue", ConsentState.NotRequested, false, false);
            Assert.IsFalse(ConsentStateMachineGate.RequiresParentalGate(record));
        }

        [Test]
        public void IsConsentActive_Helper_Granted_True()
        {
            var record = ConsentRecord.Create("Coral_Rescue", ConsentState.Granted, false, false);
            Assert.IsTrue(ConsentStateMachineGate.IsConsentActive(record));
        }

        [Test]
        public void IsConsentActive_Helper_Denied_False()
        {
            var record = ConsentRecord.Create("Coral_Rescue", ConsentState.Denied, false, false);
            Assert.IsFalse(ConsentStateMachineGate.IsConsentActive(record));
        }

        [Test]
        public void ConsentLog_Record_And_Count()
        {
            var log = new ConsentLog();
            log.Record("Coral_Rescue", "NotRequested->Pending");
            log.Record("Coral_Rescue", "Pending->Granted");
            Assert.AreEqual(2, log.Count);
        }

        [Test]
        public void ConsentLog_HasEntry_True()
        {
            var log = new ConsentLog();
            log.Record("Coral_Rescue", "Pending->Granted");
            Assert.IsTrue(log.HasEntry("Coral_Rescue"));
        }

        [Test]
        public void ConsentLog_Clear_ResetsCount()
        {
            var log = new ConsentLog();
            log.Record("Coral_Rescue", "Pending->Granted");
            log.Clear();
            Assert.AreEqual(0, log.Count);
        }

        [Test]
        public void ConsentLog_Record_NullGameId_Throws()
        {
            var log = new ConsentLog();
            Assert.Throws<ArgumentException>(() => log.Record(null, "some-transition"));
        }

        // QA remediation: fail-closed default policy, GameId mismatch guard, log argument validation
        [Test]
        public void CanTransitionState_DefaultStructPolicy_FailsClosed()
        {
            var record = ConsentRecord.Create("Coral_Rescue", ConsentState.Pending, true, false);
            var expected = ConsentStateMachineGate.CanTransitionState("Coral_Rescue", record, ConsentPolicy.Default, ConsentState.Granted, out var expectedReason);
            var result = ConsentStateMachineGate.CanTransitionState("Coral_Rescue", record, default(ConsentPolicy), ConsentState.Granted, out var reason);
            Assert.IsFalse(expected);
            Assert.IsFalse(result);
            Assert.AreEqual("MinorRequiresParentalGate", expectedReason);
            Assert.AreEqual(expectedReason, reason);
        }

        [Test]
        public void CanTransitionState_GameIdMismatch_Blocked()
        {
            var record = ConsentRecord.Create("Wonder_Pop", ConsentState.Pending, false, false);
            var result = ConsentStateMachineGate.CanTransitionState("Coral_Rescue", record, ConsentPolicy.Default, ConsentState.Granted, out var reason);
            Assert.IsFalse(result);
            Assert.AreEqual("GameIdMismatch", reason);
        }

        [Test]
        public void ConsentLog_Record_NullOrEmptySecondArg_Throws()
        {
            var log = new ConsentLog();
            Assert.Throws<System.ArgumentException>(() => log.Record("Coral_Rescue", null));
            Assert.Throws<System.ArgumentException>(() => log.Record("Coral_Rescue", string.Empty));
            Assert.AreEqual(0, log.Count);
        }

        [Test]
        public void ConsentLog_HasEntry_IsOrdinal()
        {
            var log = new ConsentLog();
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
                Assert.IsFalse(ConsentStateMachineGate.CanTransitionState(gameId, default(ConsentRecord), default(ConsentPolicy), default(ConsentState), out var reason));
                Assert.AreEqual("GameIdMissing", reason);
            }
        }

        [Test]
        public void UnknownOrCaseVariantGameId_DefaultRecord_UnknownGame()
        {
            foreach (var gameId in new[] { "Unknown_Game", "coral_rescue", "CORAL_RESCUE", " Coral_Rescue", "Coral_Rescue ", "wonder_pop", "WONDER_POP" })
            {
                Assert.IsFalse(ConsentStateMachineGate.CanTransitionState(gameId, default(ConsentRecord), default(ConsentPolicy), default(ConsentState), out var reason));
                Assert.AreEqual("UnknownGame", reason);
            }
        }
    }
}
