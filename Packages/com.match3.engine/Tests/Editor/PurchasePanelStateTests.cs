// ENG-M15-015 | PurchasePanelState tests
using System;
using NUnit.Framework;
using Match3.Engine.Runtime.Commerce;

namespace Match3.Engine.Tests.Editor
{
    [TestFixture]
    public class PurchasePanelStateTests
    {
        // ---- PurchasePanelResult construction ----

        [Test]
        public void Result_NullReason_Throws()
        {
            Assert.Throws<ArgumentException>(() =>
                new PurchasePanelResult(PurchasePanelPhase.Completed, null));
        }

        [Test]
        public void Result_EmptyReason_Throws()
        {
            Assert.Throws<ArgumentException>(() =>
                new PurchasePanelResult(PurchasePanelPhase.Canceled, ""));
        }

        // ---- ShouldGrantReward ----

        [Test]
        public void ShouldGrantReward_Completed_True()
        {
            var r = PurchasePanelResult.Success("ok");
            Assert.IsTrue(r.ShouldGrantReward);
        }

        [Test]
        public void ShouldGrantReward_Restored_True()
        {
            var r = PurchasePanelResult.Restored("ok");
            Assert.IsTrue(r.ShouldGrantReward);
        }

        [Test]
        public void ShouldGrantReward_Canceled_False()
            => Assert.IsFalse(PurchasePanelResult.Canceled("x").ShouldGrantReward);

        [Test]
        public void ShouldGrantReward_Refunded_False()
            => Assert.IsFalse(PurchasePanelResult.Refunded("x").ShouldGrantReward);

        [Test]
        public void ShouldGrantReward_ValidationFailed_False()
            => Assert.IsFalse(PurchasePanelResult.ValidationFailed("x").ShouldGrantReward);

        [Test]
        public void ShouldGrantReward_AlreadyOwned_False()
            => Assert.IsFalse(PurchasePanelResult.AlreadyOwned("x").ShouldGrantReward);

        // ---- CanClose ----

        [Test]
        public void CanClose_TerminalStates_True()
        {
            Assert.IsTrue(PurchasePanelResult.Success("ok").CanClose);
            Assert.IsTrue(PurchasePanelResult.Canceled("ok").CanClose);
            Assert.IsTrue(PurchasePanelResult.AlreadyOwned("ok").CanClose);
            Assert.IsTrue(PurchasePanelResult.Refunded("ok").CanClose);
            Assert.IsTrue(PurchasePanelResult.ValidationFailed("ok").CanClose);
            Assert.IsTrue(PurchasePanelResult.Restored("ok").CanClose);
        }

        [Test]
        public void CanClose_NonTerminalPendingApproval_False()
            => Assert.IsFalse(PurchasePanelResult.PendingApproval("waiting").CanClose);

        [Test]
        public void CanClose_AskToBuy_False()
            => Assert.IsFalse(PurchasePanelResult.AskToBuy("ask").CanClose);

        // ---- StateMachine initial state ----

        [Test]
        public void StateMachine_StartsIdle()
        {
            var sm = new PurchasePanelStateMachine();
            Assert.AreEqual(PurchasePanelPhase.Idle, sm.CurrentPhase);
        }

        // ---- StartPurchase ----

        [Test]
        public void StartPurchase_FromIdle_TransitionsToPurchasing()
        {
            var sm = new PurchasePanelStateMachine();
            sm.StartPurchase();
            Assert.AreEqual(PurchasePanelPhase.Purchasing, sm.CurrentPhase);
        }

        [Test]
        public void StartPurchase_WhileInFlight_Throws()
        {
            var sm = new PurchasePanelStateMachine();
            sm.StartPurchase();
            Assert.Throws<InvalidOperationException>(() => sm.StartPurchase());
        }

        // ---- TransitionTo ----

        [Test]
        public void TransitionTo_PurchasingToCompleted_Valid()
        {
            var sm = new PurchasePanelStateMachine();
            sm.StartPurchase();
            sm.TransitionTo(PurchasePanelPhase.Completed);
            Assert.AreEqual(PurchasePanelPhase.Completed, sm.CurrentPhase);
        }

        [Test]
        public void TransitionTo_CompletedToPurchasing_ThrowsDuplicatePrevention()
        {
            var sm = new PurchasePanelStateMachine();
            sm.StartPurchase();
            sm.TransitionTo(PurchasePanelPhase.Completed);
            Assert.Throws<InvalidOperationException>(() =>
                sm.TransitionTo(PurchasePanelPhase.Purchasing));
        }

        [Test]
        public void TransitionTo_RestoredToPurchasing_Throws()
        {
            var sm = new PurchasePanelStateMachine();
            sm.TransitionTo(PurchasePanelPhase.Restored);
            Assert.Throws<InvalidOperationException>(() =>
                sm.TransitionTo(PurchasePanelPhase.Purchasing));
        }

        // ---- Reset ----

        [Test]
        public void Reset_FromCompleted_ReturnsIdle()
        {
            var sm = new PurchasePanelStateMachine();
            sm.StartPurchase();
            sm.TransitionTo(PurchasePanelPhase.Completed);
            sm.Reset();
            Assert.AreEqual(PurchasePanelPhase.Idle, sm.CurrentPhase);
        }

        [Test]
        public void Reset_FromPurchasing_ReturnsIdle()
        {
            var sm = new PurchasePanelStateMachine();
            sm.StartPurchase();
            sm.Reset();
            Assert.AreEqual(PurchasePanelPhase.Idle, sm.CurrentPhase);
        }

        // ---- GetResult ----

        [Test]
        public void GetResult_Completed_ReturnsSuccessResult()
        {
            var sm = new PurchasePanelStateMachine();
            sm.TransitionTo(PurchasePanelPhase.Completed);
            var result = sm.GetResult();
            Assert.AreEqual(PurchasePanelPhase.Completed, result.Phase);
            Assert.IsTrue(result.ShouldGrantReward);
        }

        [Test]
        public void GetResult_Canceled_ReturnsCanceledResult()
        {
            var sm = new PurchasePanelStateMachine();
            sm.TransitionTo(PurchasePanelPhase.Canceled);
            var result = sm.GetResult();
            Assert.AreEqual(PurchasePanelPhase.Canceled, result.Phase);
            Assert.IsFalse(result.ShouldGrantReward);
        }

        [Test]
        public void GetResult_NonTerminal_Throws()
        {
            var sm = new PurchasePanelStateMachine();
            sm.StartPurchase();
            Assert.Throws<InvalidOperationException>(() => sm.GetResult());
        }

        [Test]
        public void GetResult_Idle_Throws()
        {
            var sm = new PurchasePanelStateMachine();
            Assert.Throws<InvalidOperationException>(() => sm.GetResult());
        }

        // ---- IsPurchaseInFlight ----

        [Test]
        public void IsPurchaseInFlight_Purchasing_True()
        {
            var sm = new PurchasePanelStateMachine();
            sm.StartPurchase();
            Assert.IsTrue(sm.IsPurchaseInFlight);
        }

        [Test]
        public void IsPurchaseInFlight_PendingApproval_True()
        {
            var sm = new PurchasePanelStateMachine();
            sm.TransitionTo(PurchasePanelPhase.PendingApproval);
            Assert.IsTrue(sm.IsPurchaseInFlight);
        }

        [Test]
        public void IsPurchaseInFlight_Idle_False()
        {
            var sm = new PurchasePanelStateMachine();
            Assert.IsFalse(sm.IsPurchaseInFlight);
        }

        [Test]
        public void IsPurchaseInFlight_Completed_False()
        {
            var sm = new PurchasePanelStateMachine();
            sm.TransitionTo(PurchasePanelPhase.Completed);
            Assert.IsFalse(sm.IsPurchaseInFlight);
        }

        // ---- Static factory phases ----

        [Test]
        public void StaticFactories_ProduceCorrectPhases()
        {
            Assert.AreEqual(PurchasePanelPhase.Completed,        PurchasePanelResult.Success("x").Phase);
            Assert.AreEqual(PurchasePanelPhase.Canceled,         PurchasePanelResult.Canceled("x").Phase);
            Assert.AreEqual(PurchasePanelPhase.AlreadyOwned,     PurchasePanelResult.AlreadyOwned("x").Phase);
            Assert.AreEqual(PurchasePanelPhase.Refunded,         PurchasePanelResult.Refunded("x").Phase);
            Assert.AreEqual(PurchasePanelPhase.ValidationFailed, PurchasePanelResult.ValidationFailed("x").Phase);
            Assert.AreEqual(PurchasePanelPhase.PendingApproval,  PurchasePanelResult.PendingApproval("x").Phase);
            Assert.AreEqual(PurchasePanelPhase.AskToBuy,         PurchasePanelResult.AskToBuy("x").Phase);
            Assert.AreEqual(PurchasePanelPhase.Restored,         PurchasePanelResult.Restored("x").Phase);
        }
    }
}
