using System;
using NUnit.Framework;
using Match3.Engine.Runtime.Identity;

namespace Match3.Engine.Tests.Editor
{
    [TestFixture]
    public class OfflineQueueGateTests
    {
        // --- OfflineEntry validation ---

        [Test]
        public void OfflineEntry_Create_NullGameId_Throws()
        {
            Assert.Throws<ArgumentException>(() =>
                OfflineEntry.Create(null, QueuedOperation.SaveSync, 1));
        }

        [Test]
        public void OfflineEntry_Create_EmptyGameId_Throws()
        {
            Assert.Throws<ArgumentException>(() =>
                OfflineEntry.Create("", QueuedOperation.SaveSync, 1));
        }

        [Test]
        public void OfflineEntry_Create_ZeroSequenceId_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                OfflineEntry.Create("Coral_Rescue", QueuedOperation.SaveSync, 0));
        }

        [Test]
        public void OfflineEntry_Create_NegativeSequenceId_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                OfflineEntry.Create("Coral_Rescue", QueuedOperation.SaveSync, -1));
        }

        [Test]
        public void OfflineEntry_Create_Valid_ReturnsEntry()
        {
            var entry = OfflineEntry.Create("Coral_Rescue", QueuedOperation.ProgressUpdate, 42);
            Assert.AreEqual("Coral_Rescue", entry.GameId);
            Assert.AreEqual(QueuedOperation.ProgressUpdate, entry.Operation);
            Assert.AreEqual(42L, entry.SequenceId);
        }

        [Test]
        public void OfflineEntry_IsProgressUpdate_True_WhenProgressUpdate()
        {
            var entry = OfflineEntry.Create("Coral_Rescue", QueuedOperation.ProgressUpdate, 1);
            Assert.IsTrue(entry.IsProgressUpdate);
        }

        [Test]
        public void OfflineEntry_IsProgressUpdate_False_WhenSaveSync()
        {
            var entry = OfflineEntry.Create("Coral_Rescue", QueuedOperation.SaveSync, 1);
            Assert.IsFalse(entry.IsProgressUpdate);
        }

        // --- OfflineQueuePolicy validation ---

        [Test]
        public void OfflineQueuePolicy_NullDescription_Throws()
        {
            Assert.Throws<ArgumentException>(() =>
                new OfflineQueuePolicy(100, false, null));
        }

        [Test]
        public void OfflineQueuePolicy_EmptyDescription_Throws()
        {
            Assert.Throws<ArgumentException>(() =>
                new OfflineQueuePolicy(100, false, ""));
        }

        [Test]
        public void OfflineQueuePolicy_ZeroMaxQueueDepth_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new OfflineQueuePolicy(0, false, "desc"));
        }

        [Test]
        public void OfflineQueuePolicy_NegativeMaxQueueDepth_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new OfflineQueuePolicy(-1, false, "desc"));
        }

        [Test]
        public void OfflineQueuePolicy_Safe_Returns100DepthFalseEntitlement()
        {
            var policy = OfflineQueuePolicy.Safe("test");
            Assert.AreEqual(100, policy.MaxQueueDepth);
            Assert.IsFalse(policy.AllowEntitlementQueue);
        }

        [Test]
        public void OfflineQueuePolicy_Default_DisablesEntitlementQueue()
        {
            var policy = OfflineQueuePolicy.Default;
            Assert.IsFalse(policy.AllowEntitlementQueue);
            Assert.AreEqual(100, policy.MaxQueueDepth);
        }

        // --- OfflineQueueGate.CanEnqueue ---

        [Test]
        public void CanEnqueue_WonderPop_ExactCase_IsIneligible()
        {
            var entry = OfflineEntry.Create("Wonder_Pop", QueuedOperation.SaveSync, 1);
            var policy = OfflineQueuePolicy.Default;
            var result = OfflineQueueGate.CanEnqueue("Wonder_Pop", entry, policy);
            Assert.IsFalse(result.IsEligible);
            Assert.AreEqual("WonderPopQueueProhibited", result.Reason);
        }

        [Test]
        public void CanEnqueue_WonderPop_LowercaseUnknownGame()
        {
            // Ordinal catalog lookup: a case variant of Wonder_Pop is not a catalog ID, so it fails closed with UnknownGame (never eligible, never the Wonder_Pop reason).
            var entry = OfflineEntry.Create("wonder_pop", QueuedOperation.SaveSync, 1);
            var policy = OfflineQueuePolicy.Default;
            var result = OfflineQueueGate.CanEnqueue("wonder_pop", entry, policy);
            Assert.IsFalse(result.IsEligible);
            Assert.AreEqual("UnknownGame", result.Reason);
        }

        [Test]
        public void CanEnqueue_EntitlementGrant_PolicyDisabled_IsIneligible()
        {
            var entry = OfflineEntry.Create("Coral_Rescue", QueuedOperation.EntitlementGrant, 1);
            var policy = OfflineQueuePolicy.Default; // AllowEntitlementQueue = false
            var result = OfflineQueueGate.CanEnqueue("Coral_Rescue", entry, policy);
            Assert.IsFalse(result.IsEligible);
            Assert.AreEqual("EntitlementQueueDisabled", result.Reason);
        }

        [Test]
        public void CanEnqueue_EntitlementGrant_PolicyEnabled_IsEligible()
        {
            var entry = OfflineEntry.Create("Coral_Rescue", QueuedOperation.EntitlementGrant, 1);
            var policy = new OfflineQueuePolicy(100, true, "entitlement allowed");
            var result = OfflineQueueGate.CanEnqueue("Coral_Rescue", entry, policy);
            Assert.IsTrue(result.IsEligible);
        }

        [Test]
        public void CanEnqueue_SaveSync_AnyGame_IsEligible()
        {
            var entry = OfflineEntry.Create("Garage_Legends", QueuedOperation.SaveSync, 5);
            var policy = OfflineQueuePolicy.Default;
            var result = OfflineQueueGate.CanEnqueue("Garage_Legends", entry, policy);
            Assert.IsTrue(result.IsEligible);
        }

        // --- IsAtCapacity ---

        [Test]
        public void IsAtCapacity_BelowMax_ReturnsFalse()
        {
            var policy = OfflineQueuePolicy.Safe("desc");
            Assert.IsFalse(OfflineQueueGate.IsAtCapacity(99, policy));
        }

        [Test]
        public void IsAtCapacity_AtMax_ReturnsTrue()
        {
            var policy = OfflineQueuePolicy.Safe("desc");
            Assert.IsTrue(OfflineQueueGate.IsAtCapacity(100, policy));
        }

        [Test]
        public void IsAtCapacity_AboveMax_ReturnsTrue()
        {
            var policy = OfflineQueuePolicy.Safe("desc");
            Assert.IsTrue(OfflineQueueGate.IsAtCapacity(101, policy));
        }

        // --- RequiresSync ---

        [Test]
        public void RequiresSync_SaveSync_ReturnsTrue()
        {
            var entry = OfflineEntry.Create("Coral_Rescue", QueuedOperation.SaveSync, 1);
            Assert.IsTrue(OfflineQueueGate.RequiresSync(entry));
        }

        [Test]
        public void RequiresSync_ProgressUpdate_ReturnsFalse()
        {
            var entry = OfflineEntry.Create("Coral_Rescue", QueuedOperation.ProgressUpdate, 1);
            Assert.IsFalse(OfflineQueueGate.RequiresSync(entry));
        }

        [Test]
        public void RequiresSync_EntitlementGrant_ReturnsFalse()
        {
            var entry = OfflineEntry.Create("Coral_Rescue", QueuedOperation.EntitlementGrant, 1);
            Assert.IsFalse(OfflineQueueGate.RequiresSync(entry));
        }

        // --- OfflineQueue ---

        [Test]
        public void OfflineQueue_Enqueue_IncreasesCount()
        {
            var q = new OfflineQueue();
            q.Enqueue(OfflineEntry.Create("Coral_Rescue", QueuedOperation.SaveSync, 1));
            Assert.AreEqual(1, q.Count);
        }

        [Test]
        public void OfflineQueue_Dequeue_ReturnsInOrder()
        {
            var q = new OfflineQueue();
            var e1 = OfflineEntry.Create("Coral_Rescue", QueuedOperation.SaveSync, 1);
            var e2 = OfflineEntry.Create("Coral_Rescue", QueuedOperation.ProgressUpdate, 2);
            q.Enqueue(e1);
            q.Enqueue(e2);
            var d = q.Dequeue();
            Assert.AreEqual(1L, d.SequenceId);
            Assert.AreEqual(1, q.Count);
        }

        [Test]
        public void OfflineQueue_Dequeue_Empty_Throws()
        {
            var q = new OfflineQueue();
            Assert.Throws<InvalidOperationException>(() => q.Dequeue());
        }

        [Test]
        public void OfflineQueue_Peek_ReturnsWithoutRemoving()
        {
            var q = new OfflineQueue();
            q.Enqueue(OfflineEntry.Create("Coral_Rescue", QueuedOperation.SaveSync, 10));
            var p = q.Peek();
            Assert.AreEqual(10L, p.SequenceId);
            Assert.AreEqual(1, q.Count);
        }

        [Test]
        public void OfflineQueue_Peek_Empty_Throws()
        {
            var q = new OfflineQueue();
            Assert.Throws<InvalidOperationException>(() => q.Peek());
        }

        [Test]
        public void OfflineQueue_Clear_ResetsCount()
        {
            var q = new OfflineQueue();
            q.Enqueue(OfflineEntry.Create("Coral_Rescue", QueuedOperation.SaveSync, 1));
            q.Enqueue(OfflineEntry.Create("Coral_Rescue", QueuedOperation.ProgressUpdate, 2));
            q.Clear();
            Assert.AreEqual(0, q.Count);
        }

        [Test]
        public void OfflineQueue_Count_StartsAtZero()
        {
            var q = new OfflineQueue();
            Assert.AreEqual(0, q.Count);
        }


        [Test]
        public void DefaultStructPolicy_FailsClosed()
        {
            var entry = OfflineEntry.Create("Coral_Rescue", QueuedOperation.EntitlementGrant, 1);
            var expected = OfflineQueueGate.CanEnqueue("Coral_Rescue", entry, OfflineQueuePolicy.Default);
            var result = OfflineQueueGate.CanEnqueue("Coral_Rescue", entry, default(OfflineQueuePolicy));
            Assert.IsFalse(result.IsEligible);
            Assert.AreEqual(expected.Reason, result.Reason);
            Assert.AreEqual("EntitlementQueueDisabled", result.Reason);
        }

        [Test]
        public void GameIdMismatch_Blocked()
        {
            var entry = OfflineEntry.Create("Wonder_Pop", QueuedOperation.SaveSync, 1);
            var result = OfflineQueueGate.CanEnqueue("Coral_Rescue", entry, OfflineQueuePolicy.Default);
            Assert.IsFalse(result.IsEligible);
            Assert.AreEqual("GameIdMismatch", result.Reason);
        }

        [Test]
        public void WonderPopParam_RecordDiffers_GameIdMismatch()
        {
            var entry = OfflineEntry.Create("Coral_Rescue", QueuedOperation.SaveSync, 1);
            var result = OfflineQueueGate.CanEnqueue("Wonder_Pop", entry, OfflineQueuePolicy.Default);
            Assert.IsFalse(result.IsEligible);
            Assert.AreEqual("GameIdMismatch", result.Reason);
        }


        [Test]
        public void NullOrEmptyGameId_DefaultRecord_GameIdMissing()
        {
            foreach (var gameId in new[] { null, string.Empty })
            {
                var result = OfflineQueueGate.CanEnqueue(gameId, default(OfflineEntry), default(OfflineQueuePolicy));
                Assert.IsFalse(result.IsEligible);
                Assert.AreEqual("GameIdMissing", result.Reason);
            }
        }

        [Test]
        public void UnknownOrCaseVariantGameId_DefaultRecord_UnknownGame()
        {
            foreach (var gameId in new[] { "Unknown_Game", "coral_rescue", "CORAL_RESCUE", " Coral_Rescue", "Coral_Rescue ", "wonder_pop", "WONDER_POP" })
            {
                var result = OfflineQueueGate.CanEnqueue(gameId, default(OfflineEntry), default(OfflineQueuePolicy));
                Assert.IsFalse(result.IsEligible);
                Assert.AreEqual("UnknownGame", result.Reason);
            }
        }
    }
}
