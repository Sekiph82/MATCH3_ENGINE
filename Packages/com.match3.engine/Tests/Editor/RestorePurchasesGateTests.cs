using System;
using NUnit.Framework;
using Match3.Engine.Runtime.Commerce;

namespace Match3.Engine.Tests.Editor
{
    public sealed class RestorePurchasesGateTests
    {
        // --- RestorePolicy ---

        [Test]
        public void RestorePolicy_Default_IsEnabled_AndRequiresUserInitiated()
        {
            var p = RestorePolicy.Default;
            Assert.That(p.RestoreAllowed, Is.True);
            Assert.That(p.RequireUserInitiated, Is.True);
            Assert.That(p.PolicyDescription, Is.Not.Null.And.Not.Empty);
        }

        [Test]
        public void RestorePolicy_Enabled_SetsRestoreAllowedTrue()
        {
            var p = RestorePolicy.Enabled("Allow restore");
            Assert.That(p.RestoreAllowed, Is.True);
            Assert.That(p.RequireUserInitiated, Is.True);
        }

        [Test]
        public void RestorePolicy_Disabled_SetsRestoreAllowedFalse()
        {
            var p = RestorePolicy.Disabled("No restore");
            Assert.That(p.RestoreAllowed, Is.False);
            Assert.That(p.RequireUserInitiated, Is.True);
        }

        [Test]
        public void RestorePolicy_NullDescription_Throws()
        {
            Assert.Throws<ArgumentException>(() => RestorePolicy.Enabled(null));
        }

        [Test]
        public void RestorePolicy_EmptyDescription_Throws()
        {
            Assert.Throws<ArgumentException>(() => RestorePolicy.Disabled(string.Empty));
        }

        // --- RestoreResult ---

        [Test]
        public void RestoreResult_Success_HasNoErrors()
        {
            var r = RestoreResult.Success(3);
            Assert.That(r.RestoredCount, Is.EqualTo(3));
            Assert.That(r.HasErrors, Is.False);
            Assert.That(r.ErrorReason, Is.Null);
        }

        [Test]
        public void RestoreResult_Failure_HasErrors()
        {
            var r = RestoreResult.Failure("NetworkTimeout");
            Assert.That(r.RestoredCount, Is.EqualTo(0));
            Assert.That(r.HasErrors, Is.True);
            Assert.That(r.ErrorReason, Is.EqualTo("NetworkTimeout"));
        }

        [Test]
        public void RestoreResult_Failure_NullReason_Throws()
        {
            Assert.Throws<ArgumentException>(() => RestoreResult.Failure(null));
        }

        [Test]
        public void RestoreResult_Empty_HasNoErrorsAndZeroCount()
        {
            var r = RestoreResult.Empty();
            Assert.That(r.RestoredCount, Is.EqualTo(0));
            Assert.That(r.HasErrors, Is.False);
            Assert.That(r.ErrorReason, Is.Null);
        }

        // --- CanInitiateRestore ---

        [Test]
        public void CanInitiateRestore_WonderPop_IsIneligible_EvenWhenPolicyAllows()
        {
            var result = RestorePurchasesGate.CanInitiateRestore("Wonder_Pop", RestorePolicy.Default);
            Assert.That(result.IsEligible, Is.False);
            Assert.That(result.Reason, Is.EqualTo("WonderPopRestoreProhibited"));
        }

        [Test]
        public void CanInitiateRestore_WonderPop_IsIneligible_WhenPolicyDisabled()
        {
            var result = RestorePurchasesGate.CanInitiateRestore("Wonder_Pop", RestorePolicy.Disabled("off"));
            Assert.That(result.IsEligible, Is.False);
            Assert.That(result.Reason, Is.EqualTo("WonderPopRestoreProhibited"));
        }

        [Test]
        public void CanInitiateRestore_PolicyDisabled_OtherGame_IsIneligible()
        {
            var result = RestorePurchasesGate.CanInitiateRestore("Coral_Rescue", RestorePolicy.Disabled("off"));
            Assert.That(result.IsEligible, Is.False);
            Assert.That(result.Reason, Is.EqualTo("RestoreNotAllowed"));
        }

        [Test]
        public void CanInitiateRestore_PolicyEnabled_OtherGame_IsEligible()
        {
            var result = RestorePurchasesGate.CanInitiateRestore("Coral_Rescue", RestorePolicy.Default);
            Assert.That(result.IsEligible, Is.True);
        }

        // --- IsTerminalPhase ---

        [Test]
        public void IsTerminalPhase_Completed_True()
        {
            Assert.That(RestorePurchasesGate.IsTerminalPhase(RestorePhase.Completed), Is.True);
        }

        [Test]
        public void IsTerminalPhase_Failed_True()
        {
            Assert.That(RestorePurchasesGate.IsTerminalPhase(RestorePhase.Failed), Is.True);
        }

        [Test]
        public void IsTerminalPhase_NothingToRestore_True()
        {
            Assert.That(RestorePurchasesGate.IsTerminalPhase(RestorePhase.NothingToRestore), Is.True);
        }

        [Test]
        public void IsTerminalPhase_Idle_False()
        {
            Assert.That(RestorePurchasesGate.IsTerminalPhase(RestorePhase.Idle), Is.False);
        }

        [Test]
        public void IsTerminalPhase_Restoring_False()
        {
            Assert.That(RestorePurchasesGate.IsTerminalPhase(RestorePhase.Restoring), Is.False);
        }

        // --- ShouldShowRestoreButton ---

        [Test]
        public void ShouldShowRestoreButton_WonderPop_False_EvenWhenEnabled()
        {
            Assert.That(RestorePurchasesGate.ShouldShowRestoreButton("Wonder_Pop", RestorePolicy.Default), Is.False);
        }

        [Test]
        public void ShouldShowRestoreButton_DisabledPolicy_False()
        {
            Assert.That(RestorePurchasesGate.ShouldShowRestoreButton("Coral_Rescue", RestorePolicy.Disabled("off")), Is.False);
        }

        [Test]
        public void ShouldShowRestoreButton_EnabledPolicy_OtherGame_True()
        {
            Assert.That(RestorePurchasesGate.ShouldShowRestoreButton("Dino_Islands", RestorePolicy.Default), Is.True);
        }

        // --- RequiresUserAction ---

        [Test]
        public void RequiresUserAction_Idle_True()
        {
            Assert.That(RestorePurchasesGate.RequiresUserAction(RestorePhase.Idle), Is.True);
        }

        [Test]
        public void RequiresUserAction_Restoring_False()
        {
            Assert.That(RestorePurchasesGate.RequiresUserAction(RestorePhase.Restoring), Is.False);
        }

        // --- RestoreSessionState ---

        [Test]
        public void RestoreSessionState_StartsIdle()
        {
            var state = new RestoreSessionState();
            Assert.That(state.CurrentPhase, Is.EqualTo(RestorePhase.Idle));
            Assert.That(state.LastResult, Is.Null);
        }

        [Test]
        public void RestoreSessionState_TransitionTo_UpdatesPhase()
        {
            var state = new RestoreSessionState();
            state.TransitionTo(RestorePhase.Restoring);
            Assert.That(state.CurrentPhase, Is.EqualTo(RestorePhase.Restoring));
        }

        [Test]
        public void RestoreSessionState_SetResult_OnTerminalPhase_Succeeds()
        {
            var state = new RestoreSessionState();
            state.TransitionTo(RestorePhase.Completed);
            state.SetResult(RestoreResult.Success(2));
            Assert.That(state.LastResult.HasValue, Is.True);
            Assert.That(state.LastResult.Value.RestoredCount, Is.EqualTo(2));
        }

        [Test]
        public void RestoreSessionState_SetResult_OnNonTerminalPhase_Throws()
        {
            var state = new RestoreSessionState();
            state.TransitionTo(RestorePhase.Restoring);
            Assert.Throws<InvalidOperationException>(() => state.SetResult(RestoreResult.Success(1)));
        }

        [Test]
        public void RestoreSessionState_Reset_ReturnsToIdle()
        {
            var state = new RestoreSessionState();
            state.TransitionTo(RestorePhase.Completed);
            state.SetResult(RestoreResult.Success(1));
            state.Reset();
            Assert.That(state.CurrentPhase, Is.EqualTo(RestorePhase.Idle));
            Assert.That(state.LastResult, Is.Null);
        }

        [Test]
        public void RestoreSessionState_SetResult_NothingToRestore_Succeeds()
        {
            var state = new RestoreSessionState();
            state.TransitionTo(RestorePhase.NothingToRestore);
            state.SetResult(RestoreResult.Empty());
            Assert.That(state.LastResult.HasValue, Is.True);
            Assert.That(state.LastResult.Value.RestoredCount, Is.EqualTo(0));
            Assert.That(state.LastResult.Value.HasErrors, Is.False);
        }

        [Test]
        public void RestoreSessionState_SetResult_Failed_WithFailureResult()
        {
            var state = new RestoreSessionState();
            state.TransitionTo(RestorePhase.Failed);
            state.SetResult(RestoreResult.Failure("StoreUnavailable"));
            Assert.That(state.LastResult.Value.HasErrors, Is.True);
            Assert.That(state.LastResult.Value.ErrorReason, Is.EqualTo("StoreUnavailable"));
        }
    }
}
