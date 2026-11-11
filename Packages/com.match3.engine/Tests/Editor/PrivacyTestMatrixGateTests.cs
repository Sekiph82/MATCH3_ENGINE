using System;
using NUnit.Framework;
using Match3.Engine.Runtime.Privacy;

namespace Match3.Engine.Tests.Editor
{
    [TestFixture]
    public class PrivacyTestMatrixGateTests
    {
        [Test]
        public void MatrixTestRecord_Create_NullGameId_Throws()
        {
            Assert.Throws<ArgumentException>(() => MatrixTestRecord.Create(null, PrivacyTestProfile.StandardUser, false, true));
        }

        [Test]
        public void MatrixTestRecord_Create_EmptyGameId_Throws()
        {
            Assert.Throws<ArgumentException>(() => MatrixTestRecord.Create("", PrivacyTestProfile.StandardUser, false, true));
        }

        [Test]
        public void MatrixTestRecord_Create_Valid_Succeeds()
        {
            var r = MatrixTestRecord.Create("Coral_Rescue", PrivacyTestProfile.StandardUser, false, true);
            Assert.AreEqual("Coral_Rescue", r.GameId);
            Assert.AreEqual(PrivacyTestProfile.StandardUser, r.Profile);
            Assert.IsFalse(r.HasUnexpectedTransfer);
            Assert.IsTrue(r.IsNetworkCallInstrumented);
        }

        [Test]
        public void MatrixTestRecord_IsRestrictedProfile_ChildProfile_True()
        {
            var r = MatrixTestRecord.Create("Coral_Rescue", PrivacyTestProfile.ChildProfile, false, true);
            Assert.IsTrue(r.IsRestrictedProfile);
        }

        [Test]
        public void MatrixTestRecord_IsRestrictedProfile_AdDisabled_True()
        {
            var r = MatrixTestRecord.Create("Coral_Rescue", PrivacyTestProfile.AdDisabled, false, true);
            Assert.IsTrue(r.IsRestrictedProfile);
        }

        [Test]
        public void MatrixTestRecord_IsRestrictedProfile_ConsentDenied_True()
        {
            var r = MatrixTestRecord.Create("Coral_Rescue", PrivacyTestProfile.ConsentDenied, false, true);
            Assert.IsTrue(r.IsRestrictedProfile);
        }

        [Test]
        public void MatrixTestRecord_IsRestrictedProfile_ConsentGranted_False()
        {
            var r = MatrixTestRecord.Create("Coral_Rescue", PrivacyTestProfile.ConsentGranted, false, true);
            Assert.IsFalse(r.IsRestrictedProfile);
        }

        [Test]
        public void MatrixTestRecord_IsRestrictedProfile_StandardUser_False()
        {
            var r = MatrixTestRecord.Create("Coral_Rescue", PrivacyTestProfile.StandardUser, false, true);
            Assert.IsFalse(r.IsRestrictedProfile);
        }

        [Test]
        public void MatrixTestRecord_RequiresInstrumentation_NotInstrumented_True()
        {
            var r = MatrixTestRecord.Create("Coral_Rescue", PrivacyTestProfile.StandardUser, false, false);
            Assert.IsTrue(r.RequiresInstrumentation);
        }

        [Test]
        public void MatrixTestRecord_RequiresInstrumentation_Instrumented_False()
        {
            var r = MatrixTestRecord.Create("Coral_Rescue", PrivacyTestProfile.StandardUser, false, true);
            Assert.IsFalse(r.RequiresInstrumentation);
        }

        [Test]
        public void PrivacyTestMatrixPolicy_Safe_Values_Correct()
        {
            var p = PrivacyTestMatrixPolicy.Safe("desc");
            Assert.IsTrue(p.BlockUnexpectedTransfers);
            Assert.IsTrue(p.RequireInstrumentation);
            Assert.AreEqual("desc", p.PolicyDescription);
        }

        [Test]
        public void PrivacyTestMatrixPolicy_Default_NotNull()
        {
            var p = PrivacyTestMatrixPolicy.Default;
            Assert.IsTrue(p.BlockUnexpectedTransfers);
            Assert.IsTrue(p.RequireInstrumentation);
            Assert.IsFalse(string.IsNullOrEmpty(p.PolicyDescription));
        }

        [Test]
        public void PrivacyTestMatrixPolicy_NullDescription_Throws()
        {
            Assert.Throws<ArgumentException>(() => PrivacyTestMatrixPolicy.Safe(null));
        }

        [Test]
        public void CanPassTestMatrix_WonderPop_RestrictedProfile_UnexpectedTransfer_Blocked()
        {
            var record = MatrixTestRecord.Create("Wonder_Pop", PrivacyTestProfile.ChildProfile, true, true);
            var policy = PrivacyTestMatrixPolicy.Default;
            var result = PrivacyTestMatrixGate.CanPassTestMatrix("Wonder_Pop", record, policy, out var reason);
            Assert.IsFalse(result);
            Assert.AreEqual("WonderPopUnexpectedTransferBlocked", reason);
        }

        [Test]
        public void CanPassTestMatrix_WonderPop_RestrictedProfile_NoTransfer_NotBlocked()
        {
            var record = MatrixTestRecord.Create("Wonder_Pop", PrivacyTestProfile.ChildProfile, false, true);
            var policy = PrivacyTestMatrixPolicy.Safe("test");
            var result = PrivacyTestMatrixGate.CanPassTestMatrix("Wonder_Pop", record, policy, out var reason);
            // Guard 1 not triggered (no unexpected transfer); no transfer, so Guard 2 also passes; instrumented so Guard 3 passes
            Assert.IsTrue(result);
            Assert.AreEqual(string.Empty, reason);
        }

        [Test]
        public void CanPassTestMatrix_WonderPop_StandardUser_UnexpectedTransfer_NotBlockedByGuard1()
        {
            var record = MatrixTestRecord.Create("Wonder_Pop", PrivacyTestProfile.StandardUser, true, true);
            var policy = PrivacyTestMatrixPolicy.Safe("test");
            var result = PrivacyTestMatrixGate.CanPassTestMatrix("Wonder_Pop", record, policy, out var reason);
            // Guard 1 not triggered (StandardUser is not restricted); Guard 2 blocks
            Assert.IsFalse(result);
            Assert.AreEqual("UnexpectedTransferDetected", reason);
        }

        [Test]
        public void CanPassTestMatrix_WonderPop_Lowercase_UnknownGame()
        {
            // Ordinal catalog lookup: a case variant of Wonder_Pop is not a catalog ID, so it fails closed with UnknownGame (never eligible, never the Wonder_Pop reason).
            var record = MatrixTestRecord.Create("wonder_pop", PrivacyTestProfile.ChildProfile, true, true);
            var policy = PrivacyTestMatrixPolicy.Safe("test");
            var result = PrivacyTestMatrixGate.CanPassTestMatrix("wonder_pop", record, policy, out var reason);
            Assert.IsFalse(result);
            Assert.AreEqual("UnknownGame", reason);
        }

        [Test]
        public void CanPassTestMatrix_UnexpectedTransfer_SafePolicy_Blocked()
        {
            var record = MatrixTestRecord.Create("Garage_Legends", PrivacyTestProfile.StandardUser, true, true);
            var policy = PrivacyTestMatrixPolicy.Safe("test");
            var result = PrivacyTestMatrixGate.CanPassTestMatrix("Garage_Legends", record, policy, out var reason);
            Assert.IsFalse(result);
            Assert.AreEqual("UnexpectedTransferDetected", reason);
        }

        [Test]
        public void CanPassTestMatrix_NoTransfer_Instrumented_Eligible()
        {
            var record = MatrixTestRecord.Create("Garage_Legends", PrivacyTestProfile.StandardUser, false, true);
            var policy = PrivacyTestMatrixPolicy.Safe("test");
            var result = PrivacyTestMatrixGate.CanPassTestMatrix("Garage_Legends", record, policy, out var reason);
            Assert.IsTrue(result);
            Assert.AreEqual(string.Empty, reason);
        }

        [Test]
        public void CanPassTestMatrix_NotInstrumented_Blocked()
        {
            var record = MatrixTestRecord.Create("Garage_Legends", PrivacyTestProfile.StandardUser, false, false);
            var policy = PrivacyTestMatrixPolicy.Safe("test");
            var result = PrivacyTestMatrixGate.CanPassTestMatrix("Garage_Legends", record, policy, out var reason);
            Assert.IsFalse(result);
            Assert.AreEqual("NetworkInstrumentationRequired", reason);
        }

        [Test]
        public void CanPassTestMatrix_GdprSubject_Instrumented_NoTransfer_Eligible()
        {
            var record = MatrixTestRecord.Create("Garage_Legends", PrivacyTestProfile.GdprSubject, false, true);
            var policy = PrivacyTestMatrixPolicy.Safe("test");
            var result = PrivacyTestMatrixGate.CanPassTestMatrix("Garage_Legends", record, policy, out var reason);
            Assert.IsTrue(result);
            Assert.AreEqual(string.Empty, reason);
        }

        [Test]
        public void IsRestrictedProfile_Helper_AdDisabled_True()
        {
            var r = MatrixTestRecord.Create("Coral_Rescue", PrivacyTestProfile.AdDisabled, false, true);
            Assert.IsTrue(PrivacyTestMatrixGate.IsRestrictedProfile(r));
        }

        [Test]
        public void IsRestrictedProfile_Helper_GdprSubject_False()
        {
            var r = MatrixTestRecord.Create("Coral_Rescue", PrivacyTestProfile.GdprSubject, false, true);
            Assert.IsFalse(PrivacyTestMatrixGate.IsRestrictedProfile(r));
        }

        [Test]
        public void RequiresInstrumentation_Helper_NotInstrumented_True()
        {
            var r = MatrixTestRecord.Create("Coral_Rescue", PrivacyTestProfile.StandardUser, false, false);
            Assert.IsTrue(PrivacyTestMatrixGate.RequiresInstrumentation(r));
        }

        [Test]
        public void RequiresInstrumentation_Helper_Instrumented_False()
        {
            var r = MatrixTestRecord.Create("Coral_Rescue", PrivacyTestProfile.StandardUser, false, true);
            Assert.IsFalse(PrivacyTestMatrixGate.RequiresInstrumentation(r));
        }

        [Test]
        public void PrivacyTestMatrixLog_Record_And_Count()
        {
            var log = new PrivacyTestMatrixLog();
            log.Record("Coral_Rescue", "ChildProfile");
            log.Record("Garage_Legends", "StandardUser");
            Assert.AreEqual(2, log.Count);
        }

        [Test]
        public void PrivacyTestMatrixLog_HasEntry_True()
        {
            var log = new PrivacyTestMatrixLog();
            log.Record("Coral_Rescue", "ChildProfile");
            Assert.IsTrue(log.HasEntry("Coral_Rescue"));
            Assert.IsFalse(log.HasEntry("Garage_Legends"));
        }

        [Test]
        public void PrivacyTestMatrixLog_Clear_ResetsCount()
        {
            var log = new PrivacyTestMatrixLog();
            log.Record("Coral_Rescue", "ChildProfile");
            log.Clear();
            Assert.AreEqual(0, log.Count);
        }

        // QA remediation: fail-closed default policy, GameId mismatch guard, log argument validation
        [Test]
        public void CanPassTestMatrix_DefaultStructPolicy_FailsClosed()
        {
            var record = MatrixTestRecord.Create("Coral_Rescue", PrivacyTestProfile.StandardUser, true, true);
            var expected = PrivacyTestMatrixGate.CanPassTestMatrix("Coral_Rescue", record, PrivacyTestMatrixPolicy.Default, out var expectedReason);
            var result = PrivacyTestMatrixGate.CanPassTestMatrix("Coral_Rescue", record, default(PrivacyTestMatrixPolicy), out var reason);
            Assert.IsFalse(expected);
            Assert.IsFalse(result);
            Assert.AreEqual("UnexpectedTransferDetected", expectedReason);
            Assert.AreEqual(expectedReason, reason);
        }

        [Test]
        public void CanPassTestMatrix_GameIdMismatch_Blocked()
        {
            var record = MatrixTestRecord.Create("Wonder_Pop", PrivacyTestProfile.ChildProfile, true, true);
            var result = PrivacyTestMatrixGate.CanPassTestMatrix("Coral_Rescue", record, PrivacyTestMatrixPolicy.Default, out var reason);
            Assert.IsFalse(result);
            Assert.AreEqual("GameIdMismatch", reason);
        }

        [Test]
        public void PrivacyTestMatrixLog_Record_NullOrEmptySecondArg_Throws()
        {
            var log = new PrivacyTestMatrixLog();
            Assert.Throws<System.ArgumentException>(() => log.Record("Coral_Rescue", null));
            Assert.Throws<System.ArgumentException>(() => log.Record("Coral_Rescue", string.Empty));
            Assert.AreEqual(0, log.Count);
        }

        [Test]
        public void PrivacyTestMatrixLog_HasEntry_IsOrdinal()
        {
            var log = new PrivacyTestMatrixLog();
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
                Assert.IsFalse(PrivacyTestMatrixGate.CanPassTestMatrix(gameId, default(MatrixTestRecord), default(PrivacyTestMatrixPolicy), out var reason));
                Assert.AreEqual("GameIdMissing", reason);
            }
        }

        [Test]
        public void UnknownOrCaseVariantGameId_DefaultRecord_UnknownGame()
        {
            foreach (var gameId in new[] { "Unknown_Game", "coral_rescue", "CORAL_RESCUE", " Coral_Rescue", "Coral_Rescue ", "wonder_pop", "WONDER_POP" })
            {
                Assert.IsFalse(PrivacyTestMatrixGate.CanPassTestMatrix(gameId, default(MatrixTestRecord), default(PrivacyTestMatrixPolicy), out var reason));
                Assert.AreEqual("UnknownGame", reason);
            }
        }
    }
}
