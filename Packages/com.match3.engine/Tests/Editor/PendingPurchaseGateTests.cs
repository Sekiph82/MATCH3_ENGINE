using System;
using NUnit.Framework;
using Match3.Engine.Runtime.Commerce;

namespace Match3.Engine.Tests.Editor
{
    public class PendingPurchaseGateTests
    {
        // --- PendingPurchasePolicy ---

        [Test]
        public void Policy_NullDescription_Throws()
        {
            Assert.Throws<ArgumentException>(() => new PendingPurchasePolicy(false, null));
        }

        [Test]
        public void Policy_EmptyDescription_Throws()
        {
            Assert.Throws<ArgumentException>(() => new PendingPurchasePolicy(false, ""));
        }

        [Test]
        public void Policy_Default_IsNoDeferral()
        {
            var p = PendingPurchasePolicy.Default;
            Assert.IsFalse(p.AllowDeferredPurchases);
            Assert.AreEqual("Deferred purchases disabled; fail-closed by default", p.PolicyDescription);
        }

        [Test]
        public void Policy_NoDeferral_SetsAllowFalse()
        {
            var p = PendingPurchasePolicy.NoDeferral("desc");
            Assert.IsFalse(p.AllowDeferredPurchases);
        }

        [Test]
        public void Policy_WithDeferral_SetsAllowTrue()
        {
            var p = PendingPurchasePolicy.WithDeferral("desc");
            Assert.IsTrue(p.AllowDeferredPurchases);
        }

        // --- CanFulfillPurchase ---

        [Test]
        public void CanFulfill_Pending_ReturnsFalse()
        {
            var p = PendingPurchasePolicy.Default;
            Assert.IsFalse(PendingPurchaseGate.CanFulfillPurchase("GameA", PurchaseState.Pending, p));
        }

        [Test]
        public void CanFulfill_Deferred_ReturnsFalse()
        {
            var p = PendingPurchasePolicy.Default;
            Assert.IsFalse(PendingPurchaseGate.CanFulfillPurchase("GameA", PurchaseState.Deferred, p));
        }

        [Test]
        public void CanFulfill_Canceled_ReturnsFalse()
        {
            var p = PendingPurchasePolicy.Default;
            Assert.IsFalse(PendingPurchaseGate.CanFulfillPurchase("GameA", PurchaseState.Canceled, p));
        }

        [Test]
        public void CanFulfill_Failed_ReturnsFalse()
        {
            var p = PendingPurchasePolicy.Default;
            Assert.IsFalse(PendingPurchaseGate.CanFulfillPurchase("GameA", PurchaseState.Failed, p));
        }

        [Test]
        public void CanFulfill_Completed_NonWonderPop_ReturnsTrue()
        {
            var p = PendingPurchasePolicy.Default;
            Assert.IsTrue(PendingPurchaseGate.CanFulfillPurchase("GameA", PurchaseState.Completed, p));
        }

        [Test]
        public void CanFulfill_Restored_NonWonderPop_ReturnsTrue()
        {
            var p = PendingPurchasePolicy.Default;
            Assert.IsTrue(PendingPurchaseGate.CanFulfillPurchase("GameA", PurchaseState.Restored, p));
        }

        [Test]
        public void CanFulfill_WonderPop_Completed_ReturnsFalse()
        {
            var p = PendingPurchasePolicy.Default;
            Assert.IsFalse(PendingPurchaseGate.CanFulfillPurchase("Wonder_Pop", PurchaseState.Completed, p));
        }

        [Test]
        public void CanFulfill_WonderPop_Restored_ReturnsFalse()
        {
            var p = PendingPurchasePolicy.Default;
            Assert.IsFalse(PendingPurchaseGate.CanFulfillPurchase("Wonder_Pop", PurchaseState.Restored, p));
        }

        [Test]
        public void CanFulfill_WonderPop_Pending_ReturnsFalse()
        {
            var p = PendingPurchasePolicy.Default;
            Assert.IsFalse(PendingPurchaseGate.CanFulfillPurchase("Wonder_Pop", PurchaseState.Pending, p));
        }

        [Test]
        public void CanFulfill_WonderPop_CaseSensitive_OrdinalCheck()
        {
            // "wonder_pop" (lowercase) is NOT Wonder_Pop; should not be blocked by ordinal check
            var p = PendingPurchasePolicy.Default;
            Assert.IsTrue(PendingPurchaseGate.CanFulfillPurchase("wonder_pop", PurchaseState.Completed, p));
        }

        // --- RequiresUserAction ---

        [Test]
        public void RequiresUserAction_Pending_ReturnsTrue()
        {
            Assert.IsTrue(PendingPurchaseGate.RequiresUserAction(PurchaseState.Pending));
        }

        [Test]
        public void RequiresUserAction_Deferred_ReturnsTrue()
        {
            Assert.IsTrue(PendingPurchaseGate.RequiresUserAction(PurchaseState.Deferred));
        }

        [Test]
        public void RequiresUserAction_Completed_ReturnsFalse()
        {
            Assert.IsFalse(PendingPurchaseGate.RequiresUserAction(PurchaseState.Completed));
        }

        // --- ShouldAcknowledgePurchase ---

        [Test]
        public void ShouldAcknowledge_Completed_ReturnsTrue()
        {
            Assert.IsTrue(PendingPurchaseGate.ShouldAcknowledgePurchase(PurchaseState.Completed));
        }

        [Test]
        public void ShouldAcknowledge_Restored_ReturnsTrue()
        {
            Assert.IsTrue(PendingPurchaseGate.ShouldAcknowledgePurchase(PurchaseState.Restored));
        }

        [Test]
        public void ShouldAcknowledge_Pending_ReturnsFalse()
        {
            Assert.IsFalse(PendingPurchaseGate.ShouldAcknowledgePurchase(PurchaseState.Pending));
        }

        [Test]
        public void ShouldAcknowledge_Deferred_ReturnsFalse()
        {
            Assert.IsFalse(PendingPurchaseGate.ShouldAcknowledgePurchase(PurchaseState.Deferred));
        }

        [Test]
        public void ShouldAcknowledge_Failed_ReturnsFalse()
        {
            Assert.IsFalse(PendingPurchaseGate.ShouldAcknowledgePurchase(PurchaseState.Failed));
        }

        // --- PendingSessionTracker ---

        [Test]
        public void Tracker_AddNull_Throws()
        {
            var t = new PendingSessionTracker();
            Assert.Throws<ArgumentException>(() => t.AddPending(null));
        }

        [Test]
        public void Tracker_AddEmpty_Throws()
        {
            var t = new PendingSessionTracker();
            Assert.Throws<ArgumentException>(() => t.AddPending(""));
        }

        [Test]
        public void Tracker_RemoveNull_Throws()
        {
            var t = new PendingSessionTracker();
            Assert.Throws<ArgumentException>(() => t.RemovePending(null));
        }

        [Test]
        public void Tracker_AddAndHas()
        {
            var t = new PendingSessionTracker();
            t.AddPending("order-1");
            Assert.IsTrue(t.HasPending("order-1"));
            Assert.AreEqual(1, t.PendingCount);
        }

        [Test]
        public void Tracker_Remove_Decrements()
        {
            var t = new PendingSessionTracker();
            t.AddPending("order-1");
            t.RemovePending("order-1");
            Assert.IsFalse(t.HasPending("order-1"));
            Assert.AreEqual(0, t.PendingCount);
        }

        [Test]
        public void Tracker_Remove_NonExistent_NoOp()
        {
            var t = new PendingSessionTracker();
            Assert.DoesNotThrow(() => t.RemovePending("nonexistent"));
            Assert.AreEqual(0, t.PendingCount);
        }

        [Test]
        public void Tracker_Clear_ResetsToEmpty()
        {
            var t = new PendingSessionTracker();
            t.AddPending("order-1");
            t.AddPending("order-2");
            t.Clear();
            Assert.AreEqual(0, t.PendingCount);
            Assert.IsFalse(t.HasPending("order-1"));
        }
    }
}
