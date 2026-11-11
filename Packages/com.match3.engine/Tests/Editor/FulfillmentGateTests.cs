using System;
using NUnit.Framework;
using Match3.Engine.Runtime.Commerce;

namespace Match3.Engine.Tests.Editor
{
    [TestFixture]
    public class FulfillmentGateTests
    {
        // --- FulfillmentRecord construction ---

        [Test]
        public void Record_NullOrderId_Throws()
        {
            Assert.Throws<ArgumentException>(() => FulfillmentRecord.Pending(null, "GameA"));
        }

        [Test]
        public void Record_EmptyOrderId_Throws()
        {
            Assert.Throws<ArgumentException>(() => FulfillmentRecord.Pending("", "GameA"));
        }

        [Test]
        public void Record_NullGameId_Throws()
        {
            Assert.Throws<ArgumentException>(() => FulfillmentRecord.Pending("order-1", null));
        }

        [Test]
        public void Record_EmptyGameId_Throws()
        {
            Assert.Throws<ArgumentException>(() => FulfillmentRecord.Pending("order-1", ""));
        }

        // --- FulfillmentRecord factories ---

        [Test]
        public void PendingFactory_IsGrantedFalse_IsAcknowledgedFalse()
        {
            var r = FulfillmentRecord.Pending("order-1", "GameA");
            Assert.IsFalse(r.IsGranted);
            Assert.IsFalse(r.IsAcknowledged);
        }

        [Test]
        public void GrantedFactory_IsGrantedTrue_IsAcknowledgedFalse()
        {
            var r = FulfillmentRecord.Granted("order-1", "GameA");
            Assert.IsTrue(r.IsGranted);
            Assert.IsFalse(r.IsAcknowledged);
        }

        [Test]
        public void AcknowledgedFactory_BothTrue()
        {
            var r = FulfillmentRecord.Acknowledged("order-1", "GameA");
            Assert.IsTrue(r.IsGranted);
            Assert.IsTrue(r.IsAcknowledged);
        }

        // --- FulfillmentGate.CanGrantReward ---

        [Test]
        public void CanGrantReward_WonderPop_ReturnsFalse()
        {
            var r = FulfillmentRecord.Pending("order-1", "Wonder_Pop");
            Assert.IsFalse(FulfillmentGate.CanGrantReward("Wonder_Pop", r));
        }

        [Test]
        public void CanGrantReward_AlreadyGranted_ReturnsFalse()
        {
            var r = FulfillmentRecord.Granted("order-1", "GameA");
            Assert.IsFalse(FulfillmentGate.CanGrantReward("GameA", r));
        }

        [Test]
        public void CanGrantReward_AlreadyAcknowledged_ReturnsFalse()
        {
            var r = FulfillmentRecord.Acknowledged("order-1", "GameA");
            Assert.IsFalse(FulfillmentGate.CanGrantReward("GameA", r));
        }

        [Test]
        public void CanGrantReward_FreshPendingRecord_ReturnsTrue()
        {
            var r = FulfillmentRecord.Pending("order-1", "GameA");
            Assert.IsTrue(FulfillmentGate.CanGrantReward("GameA", r));
        }

        // --- FulfillmentGate.ShouldAcknowledge ---

        [Test]
        public void ShouldAcknowledge_GrantedNotAcknowledged_ReturnsTrue()
        {
            var r = FulfillmentRecord.Granted("order-1", "GameA");
            Assert.IsTrue(FulfillmentGate.ShouldAcknowledge(r));
        }

        [Test]
        public void ShouldAcknowledge_Acknowledged_ReturnsFalse()
        {
            var r = FulfillmentRecord.Acknowledged("order-1", "GameA");
            Assert.IsFalse(FulfillmentGate.ShouldAcknowledge(r));
        }

        [Test]
        public void ShouldAcknowledge_Pending_ReturnsFalse()
        {
            var r = FulfillmentRecord.Pending("order-1", "GameA");
            Assert.IsFalse(FulfillmentGate.ShouldAcknowledge(r));
        }

        // --- FulfillmentGate.IsAlreadyFulfilled ---

        [Test]
        public void IsAlreadyFulfilled_Acknowledged_ReturnsTrue()
        {
            var r = FulfillmentRecord.Acknowledged("order-1", "GameA");
            Assert.IsTrue(FulfillmentGate.IsAlreadyFulfilled(r));
        }

        [Test]
        public void IsAlreadyFulfilled_Granted_ReturnsFalse()
        {
            var r = FulfillmentRecord.Granted("order-1", "GameA");
            Assert.IsFalse(FulfillmentGate.IsAlreadyFulfilled(r));
        }

        [Test]
        public void IsAlreadyFulfilled_Pending_ReturnsFalse()
        {
            var r = FulfillmentRecord.Pending("order-1", "GameA");
            Assert.IsFalse(FulfillmentGate.IsAlreadyFulfilled(r));
        }

        // --- FulfillmentGate.AssertGrantedBeforeAcknowledge ---

        [Test]
        public void AssertGrantedBeforeAcknowledge_PendingRecord_Throws()
        {
            var r = FulfillmentRecord.Pending("order-1", "GameA");
            Assert.Throws<InvalidOperationException>(() => FulfillmentGate.AssertGrantedBeforeAcknowledge(r));
        }

        [Test]
        public void AssertGrantedBeforeAcknowledge_GrantedRecord_DoesNotThrow()
        {
            var r = FulfillmentRecord.Granted("order-1", "GameA");
            Assert.DoesNotThrow(() => FulfillmentGate.AssertGrantedBeforeAcknowledge(r));
        }

        // --- FulfillmentLedger ---

        [Test]
        public void Ledger_TryGetRecord_NullOrderId_Throws()
        {
            var ledger = new FulfillmentLedger();
            Assert.Throws<ArgumentException>(() => ledger.TryGetRecord(null, out _));
        }

        [Test]
        public void Ledger_TryGetRecord_Miss_ReturnsFalse()
        {
            var ledger = new FulfillmentLedger();
            Assert.IsFalse(ledger.TryGetRecord("missing-order", out _));
        }

        [Test]
        public void Ledger_RecordPending_ThenTryGetRecord_ReturnsFound()
        {
            var ledger = new FulfillmentLedger();
            var r = FulfillmentRecord.Pending("order-1", "GameA");
            ledger.RecordPending(r);
            Assert.IsTrue(ledger.TryGetRecord("order-1", out var found));
            Assert.AreEqual("order-1", found.OrderId);
        }

        [Test]
        public void Ledger_RecordGranted_IncreasesCount()
        {
            var ledger = new FulfillmentLedger();
            ledger.RecordGranted(FulfillmentRecord.Granted("order-1", "GameA"));
            ledger.RecordGranted(FulfillmentRecord.Granted("order-2", "GameA"));
            Assert.AreEqual(2, ledger.Count);
        }

        [Test]
        public void Ledger_Clear_ResetsCount()
        {
            var ledger = new FulfillmentLedger();
            ledger.RecordPending(FulfillmentRecord.Pending("order-1", "GameA"));
            ledger.Clear();
            Assert.AreEqual(0, ledger.Count);
        }

        [Test]
        public void Ledger_RecordPending_AlreadyGrantedOrder_Throws()
        {
            var ledger = new FulfillmentLedger();
            ledger.RecordGranted(FulfillmentRecord.Granted("order-1", "GameA"));
            Assert.Throws<ArgumentException>(() =>
                ledger.RecordPending(FulfillmentRecord.Pending("order-1", "GameA")));
        }


        // FulfillmentGate has no policy (record-only gate): no DefaultStructPolicy test applies.

        [Test]
        public void GameIdMismatch_Blocked()
        {
            var record = FulfillmentRecord.Pending("order-1", "Wonder_Pop");
            Assert.IsFalse(FulfillmentGate.CanGrantReward("Coral_Rescue", record));
        }

        [Test]
        public void WonderPopParam_RecordDiffers_GameIdMismatch()
        {
            var record = FulfillmentRecord.Pending("order-1", "Coral_Rescue");
            Assert.IsFalse(FulfillmentGate.CanGrantReward("Wonder_Pop", record));
            // Control: matching game id with the same pending record is grantable
            Assert.IsTrue(FulfillmentGate.CanGrantReward("Coral_Rescue", record));
        }


        [Test]
        public void NullOrEmptyGameId_DefaultRecord_GameIdMissing()
        {
            foreach (var gameId in new[] { null, string.Empty })
            {
                Assert.IsFalse(FulfillmentGate.CanGrantReward(gameId, default(FulfillmentRecord)));
            }
        }
    }
}
