using System;
using System.Collections.Generic;

namespace Match3.Engine.Runtime.Identity
{
    public enum QueuedOperation
    {
        SaveSync,
        EntitlementGrant,
        ProgressUpdate
    }

    public readonly struct OfflineEntry
    {
        public string GameId { get; }
        public QueuedOperation Operation { get; }
        public long SequenceId { get; }

        private OfflineEntry(string gameId, QueuedOperation operation, long sequenceId)
        {
            GameId = gameId;
            Operation = operation;
            SequenceId = sequenceId;
        }

        public bool IsProgressUpdate => Operation == QueuedOperation.ProgressUpdate;

        public static OfflineEntry Create(string gameId, QueuedOperation operation, long sequenceId)
        {
            if (string.IsNullOrEmpty(gameId))
                throw new ArgumentException("GameId must not be null or empty.", nameof(gameId));
            if (sequenceId <= 0)
                throw new ArgumentOutOfRangeException(nameof(sequenceId), "SequenceId must be greater than zero.");
            return new OfflineEntry(gameId, operation, sequenceId);
        }
    }

    public readonly struct OfflineQueuePolicy
    {
        public int MaxQueueDepth { get; }
        public bool AllowEntitlementQueue { get; }
        public string PolicyDescription { get; }

        public OfflineQueuePolicy(int maxQueueDepth, bool allowEntitlementQueue, string policyDescription)
        {
            if (string.IsNullOrEmpty(policyDescription))
                throw new ArgumentException("PolicyDescription must not be null or empty.", nameof(policyDescription));
            if (maxQueueDepth <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxQueueDepth), "MaxQueueDepth must be greater than zero.");
            MaxQueueDepth = maxQueueDepth;
            AllowEntitlementQueue = allowEntitlementQueue;
            PolicyDescription = policyDescription;
        }

        public static OfflineQueuePolicy Safe(string desc) =>
            new OfflineQueuePolicy(100, false, desc);

        public static OfflineQueuePolicy Default =>
            Safe("Entitlement queuing disabled; use server-authoritative path");
    }

    public readonly struct QueueEligibilityResult
    {
        public bool IsEligible { get; }
        public string Reason { get; }

        public QueueEligibilityResult(bool isEligible, string reason)
        {
            IsEligible = isEligible;
            Reason = reason;
        }
    }

    public static class OfflineQueueGate
    {
        private static OfflineQueuePolicy Normalize(OfflineQueuePolicy policy) =>
            policy.PolicyDescription == null ? OfflineQueuePolicy.Default : policy;

        public static QueueEligibilityResult CanEnqueue(string gameId, OfflineEntry entry, OfflineQueuePolicy policy)
        {
            policy = Normalize(policy);

            if (!string.Equals(gameId, entry.GameId, StringComparison.Ordinal))
                return new QueueEligibilityResult(false, "GameIdMismatch");

            // Guard 1 (after GameIdMismatch) (CRITICAL): Wonder_Pop is prohibited — Ordinal exact match
            if (string.Equals(gameId, "Wonder_Pop", StringComparison.Ordinal))
                return new QueueEligibilityResult(false, "WonderPopQueueProhibited");

            // Guard 2: entitlement queuing disabled by policy
            if (entry.Operation == QueuedOperation.EntitlementGrant && !policy.AllowEntitlementQueue)
                return new QueueEligibilityResult(false, "EntitlementQueueDisabled");

            return new QueueEligibilityResult(true, "Eligible");
        }

        public static bool IsAtCapacity(int currentDepth, OfflineQueuePolicy policy) =>
            currentDepth >= Normalize(policy).MaxQueueDepth;

        public static bool RequiresSync(OfflineEntry entry) =>
            entry.Operation == QueuedOperation.SaveSync;
    }

    public sealed class OfflineQueue
    {
        private readonly Queue<OfflineEntry> _queue = new Queue<OfflineEntry>();

        public int Count => _queue.Count;

        public void Enqueue(OfflineEntry entry) => _queue.Enqueue(entry);

        public OfflineEntry Dequeue()
        {
            if (_queue.Count == 0)
                throw new InvalidOperationException("Queue is empty.");
            return _queue.Dequeue();
        }

        public OfflineEntry Peek()
        {
            if (_queue.Count == 0)
                throw new InvalidOperationException("Queue is empty.");
            return _queue.Peek();
        }

        public void Clear() => _queue.Clear();
    }
}
