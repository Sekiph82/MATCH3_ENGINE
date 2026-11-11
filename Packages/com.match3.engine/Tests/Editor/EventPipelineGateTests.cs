// ENG-M18-002 EventPipelineGate tests
using System;
using NUnit.Framework;
using Match3.Engine.Runtime.Analytics;

namespace Match3.Engine.Tests.Editor
{
    [TestFixture]
    public class EventPipelineGateTests
    {
        // ---- PipelineBatch validation ----

        [Test]
        public void PipelineBatch_NullGameId_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() => PipelineBatch.Create(null, PipelineBackend.NoOp, 1));
        }

        [Test]
        public void PipelineBatch_EmptyGameId_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() => PipelineBatch.Create("", PipelineBackend.NoOp, 1));
        }

        [Test]
        public void PipelineBatch_ZeroBatchSize_ThrowsArgumentOutOfRangeException()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => PipelineBatch.Create("GameA", PipelineBackend.NoOp, 0));
        }

        [Test]
        public void PipelineBatch_NegativeBatchSize_ThrowsArgumentOutOfRangeException()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => PipelineBatch.Create("GameA", PipelineBackend.NoOp, -5));
        }

        // ---- PipelinePolicy validation ----

        [Test]
        public void PipelinePolicy_NullDescription_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() => PipelinePolicy.Safe(null));
        }

        [Test]
        public void PipelinePolicy_EmptyDescription_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() => PipelinePolicy.Safe(""));
        }

        [Test]
        public void PipelinePolicy_Create_ZeroMaxBatchSize_ThrowsArgumentOutOfRangeException()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => PipelinePolicy.Create(0, false, "desc"));
        }

        [Test]
        public void PipelinePolicy_Create_NegativeMaxBatchSize_ThrowsArgumentOutOfRangeException()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => PipelinePolicy.Create(-1, false, "desc"));
        }

        // ---- Guard 1: Wonder_Pop blocked (Ordinal) ----

        [Test]
        public void CanFlushBatch_WonderPop_IsIneligible()
        {
            var batch = PipelineBatch.Create("Wonder_Pop", PipelineBackend.LocalBuffer, 10);
            var policy = PipelinePolicy.Default;
            var result = EventPipelineGate.CanFlushBatch("Wonder_Pop", batch, policy);
            Assert.IsFalse(result.IsEligible);
            Assert.AreEqual("WonderPopPipelineProhibited", result.Reason);
        }

        [Test]
        public void CanFlushBatch_WonderPop_CaseDifferent_IsNotBlockedByGuard1()
        {
            // "wonder_pop" lowercase must NOT be blocked by Wonder_Pop Ordinal guard
            var batch = PipelineBatch.Create("wonder_pop", PipelineBackend.LocalBuffer, 10);
            var policy = PipelinePolicy.Default;
            var result = EventPipelineGate.CanFlushBatch("wonder_pop", batch, policy);
            // Guard 1 must not fire; it may still fail Guard 2 (remote) or be eligible
            Assert.AreNotEqual("WonderPopPipelineProhibited", result.Reason);
        }

        [Test]
        public void CanFlushBatch_WonderPop_RemoteBackend_Guard1FiresFirst()
        {
            // Even with remote backend, Guard 1 must be reason (not RemoteFlushDisabled)
            var batch = PipelineBatch.Create("Wonder_Pop", PipelineBackend.Remote, 1);
            var policy = PipelinePolicy.Default;
            var result = EventPipelineGate.CanFlushBatch("Wonder_Pop", batch, policy);
            Assert.AreEqual("WonderPopPipelineProhibited", result.Reason);
        }

        // ---- Guard 2: RemoteFlushDisabled ----

        [Test]
        public void CanFlushBatch_Remote_AllowRemoteFalse_IsIneligible()
        {
            var batch = PipelineBatch.Create("GameA", PipelineBackend.Remote, 1);
            var policy = PipelinePolicy.Default; // AllowRemoteFlush = false
            var result = EventPipelineGate.CanFlushBatch("GameA", batch, policy);
            Assert.IsFalse(result.IsEligible);
            Assert.AreEqual("RemoteFlushDisabled", result.Reason);
        }

        [Test]
        public void CanFlushBatch_Remote_AllowRemoteTrue_PassesGuard2()
        {
            var batch = PipelineBatch.Create("GameA", PipelineBackend.Remote, 5);
            var policy = PipelinePolicy.Create(50, true, "remote enabled");
            var result = EventPipelineGate.CanFlushBatch("GameA", batch, policy);
            Assert.IsTrue(result.IsEligible);
        }

        // ---- Guard 3: BatchSizeExceeded ----

        [Test]
        public void CanFlushBatch_BatchSizeExceedsMax_IsIneligible()
        {
            var batch = PipelineBatch.Create("GameA", PipelineBackend.LocalBuffer, 51);
            var policy = PipelinePolicy.Default; // MaxBatchSize = 50
            var result = EventPipelineGate.CanFlushBatch("GameA", batch, policy);
            Assert.IsFalse(result.IsEligible);
            Assert.AreEqual("BatchSizeExceeded", result.Reason);
        }

        [Test]
        public void CanFlushBatch_BatchSizeEqualsMax_IsEligible()
        {
            var batch = PipelineBatch.Create("GameA", PipelineBackend.LocalBuffer, 50);
            var policy = PipelinePolicy.Default;
            var result = EventPipelineGate.CanFlushBatch("GameA", batch, policy);
            Assert.IsTrue(result.IsEligible);
        }

        // ---- IsWithinBatchLimit boundary ----

        [Test]
        public void IsWithinBatchLimit_EqualToMax_ReturnsTrue()
        {
            var batch = PipelineBatch.Create("GameA", PipelineBackend.NoOp, 50);
            Assert.IsTrue(EventPipelineGate.IsWithinBatchLimit(batch, PipelinePolicy.Default));
        }

        [Test]
        public void IsWithinBatchLimit_ExceedsMax_ReturnsFalse()
        {
            var batch = PipelineBatch.Create("GameA", PipelineBackend.NoOp, 51);
            Assert.IsFalse(EventPipelineGate.IsWithinBatchLimit(batch, PipelinePolicy.Default));
        }

        [Test]
        public void IsWithinBatchLimit_BelowMax_ReturnsTrue()
        {
            var batch = PipelineBatch.Create("GameA", PipelineBackend.NoOp, 1);
            Assert.IsTrue(EventPipelineGate.IsWithinBatchLimit(batch, PipelinePolicy.Default));
        }

        // ---- RequiresRemoteProvider for all backends ----

        [Test]
        public void RequiresRemoteProvider_Remote_ReturnsTrue()
        {
            var batch = PipelineBatch.Create("GameA", PipelineBackend.Remote, 1);
            Assert.IsTrue(EventPipelineGate.RequiresRemoteProvider(batch));
        }

        [Test]
        public void RequiresRemoteProvider_LocalBuffer_ReturnsFalse()
        {
            var batch = PipelineBatch.Create("GameA", PipelineBackend.LocalBuffer, 1);
            Assert.IsFalse(EventPipelineGate.RequiresRemoteProvider(batch));
        }

        [Test]
        public void RequiresRemoteProvider_NoOp_ReturnsFalse()
        {
            var batch = PipelineBatch.Create("GameA", PipelineBackend.NoOp, 1);
            Assert.IsFalse(EventPipelineGate.RequiresRemoteProvider(batch));
        }

        // ---- PipelineQueue enqueue/flush/count/clear ----

        [Test]
        public void PipelineQueue_EnqueueAndCount()
        {
            var q = new PipelineQueue();
            q.Enqueue("GameA", "LevelStart");
            q.Enqueue("GameA", "LevelEnd");
            Assert.AreEqual(2, q.Count);
        }

        [Test]
        public void PipelineQueue_Flush_ReturnsItemsAndClears()
        {
            var q = new PipelineQueue();
            q.Enqueue("GameA", "evt1");
            q.Enqueue("GameA", "evt2");
            var items = q.Flush();
            Assert.AreEqual(2, items.Count);
            Assert.AreEqual(0, q.Count);
        }

        [Test]
        public void PipelineQueue_Clear_ResetsCount()
        {
            var q = new PipelineQueue();
            q.Enqueue("GameA", "evt1");
            q.Clear();
            Assert.AreEqual(0, q.Count);
        }

        [Test]
        public void PipelineQueue_NullGameId_ThrowsArgumentException()
        {
            var q = new PipelineQueue();
            Assert.Throws<ArgumentException>(() => q.Enqueue(null, "evt"));
        }

        [Test]
        public void PipelineQueue_EmptyGameId_ThrowsArgumentException()
        {
            var q = new PipelineQueue();
            Assert.Throws<ArgumentException>(() => q.Enqueue("", "evt"));
        }

        [Test]
        public void PipelineQueue_NullEventName_ThrowsArgumentException()
        {
            var q = new PipelineQueue();
            Assert.Throws<ArgumentException>(() => q.Enqueue("GameA", null));
        }

        [Test]
        public void PipelineQueue_EmptyEventName_ThrowsArgumentException()
        {
            var q = new PipelineQueue();
            Assert.Throws<ArgumentException>(() => q.Enqueue("GameA", ""));
        }

        [Test]
        public void PipelinePolicy_Default_HasExpectedValues()
        {
            var p = PipelinePolicy.Default;
            Assert.AreEqual(50, p.MaxBatchSize);
            Assert.IsFalse(p.AllowRemoteFlush);
            Assert.IsNotEmpty(p.PolicyDescription);
        }
    }
}
