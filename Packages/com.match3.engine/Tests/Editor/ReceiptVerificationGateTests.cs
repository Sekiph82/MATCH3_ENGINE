using System;
using NUnit.Framework;
using Match3.Engine.Runtime.Commerce;

namespace Match3.Engine.Tests.Editor
{
    public class ReceiptVerificationGateTests
    {
        // ReceiptVerificationPolicy tests

        [Test]
        public void Policy_NullDescription_Throws()
        {
            Assert.Throws<ArgumentException>(() =>
                new ReceiptVerificationPolicy(true, null));
        }

        [Test]
        public void Policy_EmptyDescription_Throws()
        {
            Assert.Throws<ArgumentException>(() =>
                new ReceiptVerificationPolicy(true, ""));
        }

        [Test]
        public void Policy_Default_IsServerVerificationRequired()
        {
            Assert.IsTrue(ReceiptVerificationPolicy.Default.ServerVerificationRequired);
        }

        [Test]
        public void Policy_Required_SetsServerVerificationRequired()
        {
            var policy = ReceiptVerificationPolicy.Required("desc");
            Assert.IsTrue(policy.ServerVerificationRequired);
        }

        // VerificationRecord tests

        [Test]
        public void Record_NullReceiptId_Throws()
        {
            Assert.Throws<ArgumentException>(() =>
                new VerificationRecord(null, "game1", ReceiptVerificationStatus.Unverified));
        }

        [Test]
        public void Record_EmptyReceiptId_Throws()
        {
            Assert.Throws<ArgumentException>(() =>
                new VerificationRecord("", "game1", ReceiptVerificationStatus.Unverified));
        }

        [Test]
        public void Record_NullGameId_Throws()
        {
            Assert.Throws<ArgumentException>(() =>
                new VerificationRecord("r1", null, ReceiptVerificationStatus.Unverified));
        }

        [Test]
        public void Record_EmptyGameId_Throws()
        {
            Assert.Throws<ArgumentException>(() =>
                new VerificationRecord("r1", "", ReceiptVerificationStatus.Unverified));
        }

        [Test]
        public void Record_Pending_HasUnverifiedStatus()
        {
            var r = VerificationRecord.Pending("r1", "game1");
            Assert.AreEqual(ReceiptVerificationStatus.Unverified, r.Status);
        }

        [Test]
        public void Record_Valid_HasVerifiedValidStatus()
        {
            var r = VerificationRecord.Valid("r1", "game1");
            Assert.AreEqual(ReceiptVerificationStatus.VerifiedValid, r.Status);
        }

        [Test]
        public void Record_Invalid_HasVerifiedInvalidStatus()
        {
            var r = VerificationRecord.Invalid("r1", "game1");
            Assert.AreEqual(ReceiptVerificationStatus.VerifiedInvalid, r.Status);
        }

        // CanGrantAfterVerification tests

        [Test]
        public void CanGrant_WonderPop_VerifiedValid_ReturnsFalse()
        {
            Assert.IsFalse(ReceiptVerificationGate.CanGrantAfterVerification(
                "Wonder_Pop", ReceiptVerificationStatus.VerifiedValid));
        }

        [Test]
        public void CanGrant_WonderPop_Unverified_ReturnsFalse()
        {
            Assert.IsFalse(ReceiptVerificationGate.CanGrantAfterVerification(
                "Wonder_Pop", ReceiptVerificationStatus.Unverified));
        }

        [Test]
        public void CanGrant_NonWonderPop_Unverified_ReturnsFalse()
        {
            Assert.IsFalse(ReceiptVerificationGate.CanGrantAfterVerification(
                "Coral_Rescue", ReceiptVerificationStatus.Unverified));
        }

        [Test]
        public void CanGrant_NonWonderPop_VerifiedInvalid_ReturnsFalse()
        {
            Assert.IsFalse(ReceiptVerificationGate.CanGrantAfterVerification(
                "Coral_Rescue", ReceiptVerificationStatus.VerifiedInvalid));
        }

        [Test]
        public void CanGrant_NonWonderPop_VerificationFailed_ReturnsFalse()
        {
            Assert.IsFalse(ReceiptVerificationGate.CanGrantAfterVerification(
                "Coral_Rescue", ReceiptVerificationStatus.VerificationFailed));
        }

        [Test]
        public void CanGrant_NonWonderPop_Expired_ReturnsFalse()
        {
            Assert.IsFalse(ReceiptVerificationGate.CanGrantAfterVerification(
                "Coral_Rescue", ReceiptVerificationStatus.Expired));
        }

        [Test]
        public void CanGrant_NonWonderPop_Refunded_ReturnsFalse()
        {
            Assert.IsFalse(ReceiptVerificationGate.CanGrantAfterVerification(
                "Coral_Rescue", ReceiptVerificationStatus.Refunded));
        }

        [Test]
        public void CanGrant_NonWonderPop_VerifiedValid_ReturnsTrue()
        {
            Assert.IsTrue(ReceiptVerificationGate.CanGrantAfterVerification(
                "Coral_Rescue", ReceiptVerificationStatus.VerifiedValid));
        }

        // IsClientAssertionAcceptable test

        [Test]
        public void IsClientAssertionAcceptable_AlwaysFalse()
        {
            Assert.IsFalse(ReceiptVerificationGate.IsClientAssertionAcceptable());
        }

        // AssertServerVerified tests

        [Test]
        public void AssertServerVerified_VerifiedValid_NoThrow()
        {
            Assert.DoesNotThrow(() =>
                ReceiptVerificationGate.AssertServerVerified(ReceiptVerificationStatus.VerifiedValid));
        }

        [Test]
        public void AssertServerVerified_Unverified_Throws()
        {
            Assert.Throws<InvalidOperationException>(() =>
                ReceiptVerificationGate.AssertServerVerified(ReceiptVerificationStatus.Unverified));
        }

        [Test]
        public void AssertServerVerified_VerifiedInvalid_Throws()
        {
            Assert.Throws<InvalidOperationException>(() =>
                ReceiptVerificationGate.AssertServerVerified(ReceiptVerificationStatus.VerifiedInvalid));
        }

        // RequiresServerCall test

        [Test]
        public void RequiresServerCall_AlwaysTrue()
        {
            Assert.IsTrue(ReceiptVerificationGate.RequiresServerCall(ReceiptVerificationPolicy.Default));
        }


        [Test]
        public void CanGrantAfterVerification_NullOrEmptyGameId_False()
        {
            foreach (var gameId in new[] { null, string.Empty })
            {
                Assert.IsFalse(ReceiptVerificationGate.CanGrantAfterVerification(gameId, ReceiptVerificationStatus.VerifiedValid));
            }
        }


        [Test]
        public void CanGrantAfterVerification_NonCatalogGameId_UnknownGame()
        {
            foreach (var unknownId in new[] { "Unknown_Game", "coral_rescue", "CORAL_RESCUE", " Coral_Rescue", "Coral_Rescue " })
            {
                Assert.IsFalse(ReceiptVerificationGate.CanGrantAfterVerification(unknownId, ReceiptVerificationStatus.VerifiedValid), unknownId);
            }
        }
    }
}
