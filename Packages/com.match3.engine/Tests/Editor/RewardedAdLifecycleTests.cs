using System;
using Match3.Engine.Runtime.Commerce;
using NUnit.Framework;

namespace Match3.Engine.Tests.Editor
{
    public sealed class RewardedAdLifecycleTests
    {
        [Test]
        public void HappyPath_UninitializedToCompleted()
        {
            var lc = new RewardedAdLifecycle();
            lc.RequestLoad();
            lc.NotifyReady();
            lc.NotifyShow();
            lc.NotifyCompleted();
            Assert.AreEqual(RewardedAdState.Completed, lc.State);
        }

        [Test]
        public void SkipPath_ShowingToSkipped()
        {
            var lc = new RewardedAdLifecycle();
            lc.RequestLoad();
            lc.NotifyReady();
            lc.NotifyShow();
            lc.NotifySkipped();
            Assert.AreEqual(RewardedAdState.Skipped, lc.State);
        }

        [Test]
        public void FailFromLoading()
        {
            var lc = new RewardedAdLifecycle();
            lc.RequestLoad();
            lc.NotifyFailed("network");
            Assert.AreEqual(RewardedAdState.Failed, lc.State);
        }

        [Test]
        public void FailFromShowing()
        {
            var lc = new RewardedAdLifecycle();
            lc.RequestLoad();
            lc.NotifyReady();
            lc.NotifyShow();
            lc.NotifyFailed("dismissed");
            Assert.AreEqual(RewardedAdState.Failed, lc.State);
        }

        [Test]
        public void ResetReturnsToUninitialized()
        {
            var lc = new RewardedAdLifecycle();
            lc.RequestLoad();
            lc.NotifyReady();
            lc.NotifyShow();
            lc.NotifyCompleted();
            lc.Reset();
            Assert.AreEqual(RewardedAdState.Uninitialized, lc.State);
        }

        [Test]
        public void InvalidTransition_ShowFromLoading_Throws()
        {
            var lc = new RewardedAdLifecycle();
            lc.RequestLoad();
            Assert.Throws<InvalidOperationException>(() => lc.NotifyShow());
        }

        [Test]
        public void OnStateChanged_FiresWithCorrectOldAndNew()
        {
            var lc = new RewardedAdLifecycle();
            RewardedAdState capturedOld = RewardedAdState.Uninitialized;
            RewardedAdState capturedNew = RewardedAdState.Uninitialized;
            int callCount = 0;
            lc.OnStateChanged += (old, next) => { capturedOld = old; capturedNew = next; callCount++; };
            lc.RequestLoad();
            Assert.AreEqual(1, callCount);
            Assert.AreEqual(RewardedAdState.Uninitialized, capturedOld);
            Assert.AreEqual(RewardedAdState.Loading, capturedNew);
        }

        [Test]
        public void ResetFromCompleted_Succeeds_ResetFromReady_Throws()
        {
            var lc = new RewardedAdLifecycle();
            lc.RequestLoad();
            lc.NotifyReady();
            lc.NotifyShow();
            lc.NotifyCompleted();
            // Reset from Completed succeeds
            lc.Reset();
            Assert.AreEqual(RewardedAdState.Uninitialized, lc.State);

            // Now reach Ready and confirm Reset throws (Ready is not terminal)
            lc.RequestLoad();
            lc.NotifyReady();
            Assert.Throws<InvalidOperationException>(() => lc.Reset());
        }
    }
}
