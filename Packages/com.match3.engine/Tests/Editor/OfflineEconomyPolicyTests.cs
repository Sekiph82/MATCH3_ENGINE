using NUnit.Framework;
using System;
using Match3.Engine.Runtime.Economy;

namespace Match3.Engine.Tests.Editor
{
    [TestFixture]
    public class OfflineEconomyPolicyTests
    {
        // OfflinePolicy construction

        [Test]
        public void OfflinePolicy_NullDescription_Throws()
        {
            Assert.Throws<ArgumentException>(() => new OfflinePolicy(true, false, null));
        }

        [Test]
        public void OfflinePolicy_EmptyDescription_Throws()
        {
            Assert.Throws<ArgumentException>(() => new OfflinePolicy(true, false, string.Empty));
        }

        [Test]
        public void OfflinePolicy_Standard_AllowsFreeDisablesPremium()
        {
            var p = OfflinePolicy.Standard("test");
            Assert.IsTrue(p.AllowOfflineFreeSpend);
            Assert.IsFalse(p.AllowOfflinePremiumSpend);
        }

        [Test]
        public void OfflinePolicy_Default_HasExpectedDescription()
        {
            var p = OfflinePolicy.Default;
            Assert.IsTrue(p.PolicyDescription.Contains("premium"));
        }

        [Test]
        public void OfflinePolicy_Default_AllowsFreeDisablesPremium()
        {
            var p = OfflinePolicy.Default;
            Assert.IsTrue(p.AllowOfflineFreeSpend);
            Assert.IsFalse(p.AllowOfflinePremiumSpend);
        }

        // OfflineEconomyGate — Wonder_Pop guard (Guard 1 always first)

        [Test]
        public void WonderPop_FreeSpend_IsIneligible()
        {
            var result = OfflineEconomyGate.CanSpendOffline("Wonder_Pop", ResourceTier.Free, OfflinePolicy.Default);
            Assert.IsFalse(result.IsEligible);
            Assert.AreEqual("WonderPopOfflineSpendProhibited", result.Reason);
        }

        [Test]
        public void WonderPop_PremiumSpend_IsIneligible()
        {
            var result = OfflineEconomyGate.CanSpendOffline("Wonder_Pop", ResourceTier.Premium, OfflinePolicy.Default);
            Assert.IsFalse(result.IsEligible);
            Assert.AreEqual("WonderPopOfflineSpendProhibited", result.Reason);
        }

        [Test]
        public void WonderPop_Guard1_FiresBeforePremiumGuard()
        {
            // Even if a permissive policy allows premium, Wonder_Pop is still blocked
            var permissivePolicy = new OfflinePolicy(true, true, "permissive");
            var result = OfflineEconomyGate.CanSpendOffline("Wonder_Pop", ResourceTier.Premium, permissivePolicy);
            Assert.AreEqual("WonderPopOfflineSpendProhibited", result.Reason);
        }

        [Test]
        public void WonderPop_OtherCase_IsUnknownGame()
        {
            // Ordinal: "wonder_pop" is not a catalog ID, so it fails closed as UnknownGame
            var result = OfflineEconomyGate.CanSpendOffline("wonder_pop", ResourceTier.Free, OfflinePolicy.Default);
            Assert.IsFalse(result.IsEligible);
            Assert.AreEqual("UnknownGame", result.Reason);
        }

        // OfflineEconomyGate — Premium guard (Guard 2)

        [Test]
        public void PremiumTier_DefaultPolicy_IsIneligible()
        {
            var result = OfflineEconomyGate.CanSpendOffline("Coral_Rescue", ResourceTier.Premium, OfflinePolicy.Default);
            Assert.IsFalse(result.IsEligible);
            Assert.AreEqual("PremiumRequiresServerSync", result.Reason);
        }

        [Test]
        public void PremiumTier_PermissivePolicy_IsEligible()
        {
            var policy = new OfflinePolicy(true, true, "allow both");
            var result = OfflineEconomyGate.CanSpendOffline("Coral_Rescue", ResourceTier.Premium, policy);
            Assert.IsTrue(result.IsEligible);
        }

        // OfflineEconomyGate — Free guard (Guard 3)

        [Test]
        public void FreeTier_DefaultPolicy_IsEligible()
        {
            var result = OfflineEconomyGate.CanSpendOffline("Coral_Rescue", ResourceTier.Free, OfflinePolicy.Default);
            Assert.IsTrue(result.IsEligible);
        }

        [Test]
        public void FreeTier_DisabledFreePolicy_IsIneligible()
        {
            var policy = new OfflinePolicy(false, false, "nothing offline");
            var result = OfflineEconomyGate.CanSpendOffline("Coral_Rescue", ResourceTier.Free, policy);
            Assert.IsFalse(result.IsEligible);
            Assert.AreEqual("FreeSpendDisabled", result.Reason);
        }

        // RequiresReconciliationOnReconnect

        [Test]
        public void PremiumTier_RequiresReconciliation()
        {
            Assert.IsTrue(OfflineEconomyGate.RequiresReconciliationOnReconnect(ResourceTier.Premium));
        }

        [Test]
        public void FreeTier_NoReconciliationRequired()
        {
            Assert.IsFalse(OfflineEconomyGate.RequiresReconciliationOnReconnect(ResourceTier.Free));
        }

        // OfflinePendingQueue

        [Test]
        public void Queue_NullTransactionId_Throws()
        {
            var q = new OfflinePendingQueue();
            Assert.Throws<ArgumentException>(() => q.Enqueue(null));
        }

        [Test]
        public void Queue_EmptyTransactionId_Throws()
        {
            var q = new OfflinePendingQueue();
            Assert.Throws<ArgumentException>(() => q.Enqueue(string.Empty));
        }

        [Test]
        public void Queue_EnqueueAndCount()
        {
            var q = new OfflinePendingQueue();
            q.Enqueue("tx-1");
            q.Enqueue("tx-2");
            Assert.AreEqual(2, q.Count);
        }

        [Test]
        public void Queue_IsEmpty_WhenNew()
        {
            var q = new OfflinePendingQueue();
            Assert.IsTrue(q.IsEmpty);
        }

        [Test]
        public void Queue_DrainAll_ReturnsCopyAndClearsQueue()
        {
            var q = new OfflinePendingQueue();
            q.Enqueue("tx-a");
            q.Enqueue("tx-b");
            var drained = q.DrainAll();
            Assert.AreEqual(2, drained.Count);
            Assert.AreEqual("tx-a", drained[0]);
            Assert.AreEqual("tx-b", drained[1]);
            Assert.IsTrue(q.IsEmpty);
        }

        [Test]
        public void Queue_DrainAll_SecondDrainIsEmpty()
        {
            var q = new OfflinePendingQueue();
            q.Enqueue("tx-1");
            q.DrainAll();
            var second = q.DrainAll();
            Assert.AreEqual(0, second.Count);
        }

        [Test]
        public void Queue_DrainAll_ReturnsCopyNotReference()
        {
            var q = new OfflinePendingQueue();
            q.Enqueue("tx-x");
            var drained = q.DrainAll();
            q.Enqueue("tx-y");
            // drained should still have only tx-x
            Assert.AreEqual(1, drained.Count);
        }


        [Test]
        public void CanSpendOffline_NullOrEmptyGameId_FailsClosed()
        {
            foreach (var gameId in new[] { null, string.Empty })
            {
                var result = OfflineEconomyGate.CanSpendOffline(gameId, ResourceTier.Free, new OfflinePolicy(true, true, "all offline"));
                Assert.IsFalse(result.IsEligible, gameId);
                Assert.AreEqual("GameIdMissing", result.Reason);
            }
        }

        [Test]
        public void CanSpendOffline_NonCatalogGameId_UnknownGame()
        {
            foreach (var unknownId in new[] { "Unknown_Game", "coral_rescue", "CORAL_RESCUE", " Coral_Rescue", "Coral_Rescue " })
            {
                var result = OfflineEconomyGate.CanSpendOffline(unknownId, ResourceTier.Free, new OfflinePolicy(true, true, "all offline"));
                Assert.IsFalse(result.IsEligible, unknownId);
                Assert.AreEqual("UnknownGame", result.Reason);
            }
        }
    }
}
