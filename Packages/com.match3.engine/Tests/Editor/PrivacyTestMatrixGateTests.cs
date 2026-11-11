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
            var r = MatrixTestRecord.Create("GameA", PrivacyTestProfile.StandardUser, false, true);
            Assert.AreEqual("GameA", r.GameId);
            Assert.AreEqual(PrivacyTestProfile.StandardUser, r.Profile);
            Assert.IsFalse(r.HasUnexpectedTransfer);
            Assert.IsTrue(r.IsNetworkCallInstrumented);
        }

        [Test]
        public void MatrixTestRecord_IsRestrictedProfile_ChildProfile_True()
        {
            var r = MatrixTestRecord.Create("G", PrivacyTestProfile.ChildProfile, false, true);
            Assert.IsTrue(r.IsRestrictedProfile);
        }

        [Test]
        public void MatrixTestRecord_IsRestrictedProfile_AdDisabled_True()
        {
            var r = MatrixTestRecord.Create("G", PrivacyTestProfile.AdDisabled, false, true);
            Assert.IsTrue(r.IsRestrictedProfile);
        }

        [Test]
        public void MatrixTestRecord_IsRestrictedProfile_ConsentDenied_True()
        {
            var r = MatrixTestRecord.Create("G", PrivacyTestProfile.ConsentDenied, false, true);
            Assert.IsTrue(r.IsRestrictedProfile);
        }

        [Test]
        public void MatrixTestRecord_IsRestrictedProfile_ConsentGranted_False()
        {
            var r = MatrixTestRecord.Create("G", PrivacyTestProfile.ConsentGranted, false, true);
            Assert.IsFalse(r.IsRestrictedProfile);
        }

        [Test]
        public void MatrixTestRecord_IsRestrictedProfile_StandardUser_False()
        {
            var r = MatrixTestRecord.Create("G", PrivacyTestProfile.StandardUser, false, true);
            Assert.IsFalse(r.IsRestrictedProfile);
        }

        [Test]
        public void MatrixTestRecord_RequiresInstrumentation_NotInstrumented_True()
        {
            var r = MatrixTestRecord.Create("G", PrivacyTestProfile.StandardUser, false, false);
            Assert.IsTrue(r.RequiresInstrumentation);
        }

        [Test]
        public void MatrixTestRecord_RequiresInstrumentation_Instrumented_False()
        {
            var r = MatrixTestRecord.Create("G", PrivacyTestProfile.StandardUser, false, true);
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
        public void CanPassTestMatrix_WonderPop_Lowercase_NotBlocked()
        {
            var record = MatrixTestRecord.Create("wonder_pop", PrivacyTestProfile.ChildProfile, true, true);
            var policy = PrivacyTestMatrixPolicy.Safe("test");
            // Ordinal mismatch: "wonder_pop" != "Wonder_Pop"
            var result = PrivacyTestMatrixGate.CanPassTestMatrix("wonder_pop", record, policy, out var reason);
            // Guard 1 not triggered; Guard 2 blocks due to unexpected transfer
            Assert.IsFalse(result);
            Assert.AreEqual("UnexpectedTransferDetected", reason);
        }

        [Test]
        public void CanPassTestMatrix_UnexpectedTransfer_SafePolicy_Blocked()
        {
            var record = MatrixTestRecord.Create("OtherGame", PrivacyTestProfile.StandardUser, true, true);
            var policy = PrivacyTestMatrixPolicy.Safe("test");
            var result = PrivacyTestMatrixGate.CanPassTestMatrix("OtherGame", record, policy, out var reason);
            Assert.IsFalse(result);
            Assert.AreEqual("UnexpectedTransferDetected", reason);
        }

        [Test]
        public void CanPassTestMatrix_NoTransfer_Instrumented_Eligible()
        {
            var record = MatrixTestRecord.Create("OtherGame", PrivacyTestProfile.StandardUser, false, true);
            var policy = PrivacyTestMatrixPolicy.Safe("test");
            var result = PrivacyTestMatrixGate.CanPassTestMatrix("OtherGame", record, policy, out var reason);
            Assert.IsTrue(result);
            Assert.AreEqual(string.Empty, reason);
        }

        [Test]
        public void CanPassTestMatrix_NotInstrumented_Blocked()
        {
            var record = MatrixTestRecord.Create("OtherGame", PrivacyTestProfile.StandardUser, false, false);
            var policy = PrivacyTestMatrixPolicy.Safe("test");
            var result = PrivacyTestMatrixGate.CanPassTestMatrix("OtherGame", record, policy, out var reason);
            Assert.IsFalse(result);
            Assert.AreEqual("NetworkInstrumentationRequired", reason);
        }

        [Test]
        public void CanPassTestMatrix_GdprSubject_Instrumented_NoTransfer_Eligible()
        {
            var record = MatrixTestRecord.Create("OtherGame", PrivacyTestProfile.GdprSubject, false, true);
            var policy = PrivacyTestMatrixPolicy.Safe("test");
            var result = PrivacyTestMatrixGate.CanPassTestMatrix("OtherGame", record, policy, out var reason);
            Assert.IsTrue(result);
            Assert.AreEqual(string.Empty, reason);
        }

        [Test]
        public void IsRestrictedProfile_Helper_AdDisabled_True()
        {
            var r = MatrixTestRecord.Create("G", PrivacyTestProfile.AdDisabled, false, true);
            Assert.IsTrue(PrivacyTestMatrixGate.IsRestrictedProfile(r));
        }

        [Test]
        public void IsRestrictedProfile_Helper_GdprSubject_False()
        {
            var r = MatrixTestRecord.Create("G", PrivacyTestProfile.GdprSubject, false, true);
            Assert.IsFalse(PrivacyTestMatrixGate.IsRestrictedProfile(r));
        }

        [Test]
        public void RequiresInstrumentation_Helper_NotInstrumented_True()
        {
            var r = MatrixTestRecord.Create("G", PrivacyTestProfile.StandardUser, false, false);
            Assert.IsTrue(PrivacyTestMatrixGate.RequiresInstrumentation(r));
        }

        [Test]
        public void RequiresInstrumentation_Helper_Instrumented_False()
        {
            var r = MatrixTestRecord.Create("G", PrivacyTestProfile.StandardUser, false, true);
            Assert.IsFalse(PrivacyTestMatrixGate.RequiresInstrumentation(r));
        }

        [Test]
        public void PrivacyTestMatrixLog_Record_And_Count()
        {
            var log = new PrivacyTestMatrixLog();
            log.Record("GameA", "ChildProfile");
            log.Record("GameB", "StandardUser");
            Assert.AreEqual(2, log.Count);
        }

        [Test]
        public void PrivacyTestMatrixLog_HasEntry_True()
        {
            var log = new PrivacyTestMatrixLog();
            log.Record("GameA", "ChildProfile");
            Assert.IsTrue(log.HasEntry("GameA"));
            Assert.IsFalse(log.HasEntry("GameB"));
        }

        [Test]
        public void PrivacyTestMatrixLog_Clear_ResetsCount()
        {
            var log = new PrivacyTestMatrixLog();
            log.Record("GameA", "ChildProfile");
            log.Clear();
            Assert.AreEqual(0, log.Count);
        }

        // QA remediation: fail-closed default policy, GameId mismatch guard, log argument validation
        [Test]
        public void CanPassTestMatrix_DefaultStructPolicy_FailsClosed()
        {
            var record = MatrixTestRecord.Create("GameA", PrivacyTestProfile.StandardUser, true, true);
            var expected = PrivacyTestMatrixGate.CanPassTestMatrix("GameA", record, PrivacyTestMatrixPolicy.Default, out var expectedReason);
            var result = PrivacyTestMatrixGate.CanPassTestMatrix("GameA", record, default(PrivacyTestMatrixPolicy), out var reason);
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
            Assert.Throws<System.ArgumentException>(() => log.Record("GameA", null));
            Assert.Throws<System.ArgumentException>(() => log.Record("GameA", string.Empty));
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
            log.Record("Game\u00ADA", "evt");
            Assert.IsTrue(log.HasEntry("Game\u00ADA"));
            Assert.IsFalse(log.HasEntry("GameA"));
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
    }
}
