// ENG-M18-002 EventPipelineGate stdlib contract
// stdlib only: System, System.Collections.Generic
using System;
using System.Collections.Generic;

namespace Match3.Engine.Runtime.Analytics
{
    public enum PipelineBackend
    {
        NoOp,
        LocalBuffer,
        Remote
    }

    public readonly struct PipelineBatch
    {
        public string GameId { get; }
        public PipelineBackend Backend { get; }
        public int BatchSize { get; }
        public bool IsRemote => Backend == PipelineBackend.Remote;

        private PipelineBatch(string gameId, PipelineBackend backend, int batchSize)
        {
            GameId = gameId;
            Backend = backend;
            BatchSize = batchSize;
        }

        public static PipelineBatch Create(string gameId, PipelineBackend backend, int batchSize)
        {
            if (string.IsNullOrEmpty(gameId))
                throw new ArgumentException("GameId must not be null or empty.", nameof(gameId));
            if (batchSize <= 0)
                throw new ArgumentOutOfRangeException(nameof(batchSize), "BatchSize must be greater than zero.");
            return new PipelineBatch(gameId, backend, batchSize);
        }
    }

    public readonly struct PipelinePolicy
    {
        public int MaxBatchSize { get; }
        public bool AllowRemoteFlush { get; }
        public string PolicyDescription { get; }

        private PipelinePolicy(int maxBatchSize, bool allowRemoteFlush, string policyDescription)
        {
            MaxBatchSize = maxBatchSize;
            AllowRemoteFlush = allowRemoteFlush;
            PolicyDescription = policyDescription;
        }

        public static PipelinePolicy Safe(string desc)
        {
            if (string.IsNullOrEmpty(desc))
                throw new ArgumentException("PolicyDescription must not be null or empty.", nameof(desc));
            return new PipelinePolicy(50, false, desc);
        }

        public static readonly PipelinePolicy Default = Safe(
            "Remote flush disabled; use NoOp or local buffer until provider authorized");

        public static PipelinePolicy Create(int maxBatchSize, bool allowRemoteFlush, string policyDescription)
        {
            if (string.IsNullOrEmpty(policyDescription))
                throw new ArgumentException("PolicyDescription must not be null or empty.", nameof(policyDescription));
            if (maxBatchSize <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxBatchSize), "MaxBatchSize must be greater than zero.");
            return new PipelinePolicy(maxBatchSize, allowRemoteFlush, policyDescription);
        }
    }

    public sealed class PipelineGateResult
    {
        public bool IsEligible { get; }
        public string Reason { get; }

        private PipelineGateResult(bool eligible, string reason)
        {
            IsEligible = eligible;
            Reason = reason;
        }

        internal static PipelineGateResult Eligible() => new PipelineGateResult(true, string.Empty);
        internal static PipelineGateResult Ineligible(string reason) => new PipelineGateResult(false, reason);
    }

    public static class EventPipelineGate
    {
        private static PipelinePolicy Normalize(PipelinePolicy policy) =>
            policy.PolicyDescription == null ? PipelinePolicy.Default : policy;

        public static PipelineGateResult CanFlushBatch(string gameId, PipelineBatch batch, PipelinePolicy policy)
        {
            policy = Normalize(policy);

            if (string.IsNullOrEmpty(gameId))
                return PipelineGateResult.Ineligible("GameIdMissing");
            if (!GameProfileCatalog.IsKnownId(gameId))
                return PipelineGateResult.Ineligible("UnknownGame");

            if (!string.Equals(gameId, batch.GameId, StringComparison.Ordinal))
                return PipelineGateResult.Ineligible("GameIdMismatch");

            // Guard 1 (after GameIdMismatch): Wonder_Pop is structurally prohibited
            if (string.Equals(gameId, "Wonder_Pop", StringComparison.Ordinal))
                return PipelineGateResult.Ineligible("WonderPopPipelineProhibited");

            // Guard 2: remote flush requires policy authorization
            if (batch.IsRemote && !policy.AllowRemoteFlush)
                return PipelineGateResult.Ineligible("RemoteFlushDisabled");

            // Guard 3: batch size must not exceed policy maximum
            if (batch.BatchSize > policy.MaxBatchSize)
                return PipelineGateResult.Ineligible("BatchSizeExceeded");

            return PipelineGateResult.Eligible();
        }

        public static bool IsWithinBatchLimit(PipelineBatch batch, PipelinePolicy policy)
            => batch.BatchSize <= Normalize(policy).MaxBatchSize;

        public static bool RequiresRemoteProvider(PipelineBatch batch)
            => batch.IsRemote;
    }

    public sealed class PipelineQueue
    {
        private readonly List<string> _items = new List<string>();

        public int Count => _items.Count;

        public void Enqueue(string gameId, string eventName)
        {
            if (string.IsNullOrEmpty(gameId))
                throw new ArgumentException("gameId must not be null or empty.", nameof(gameId));
            if (string.IsNullOrEmpty(eventName))
                throw new ArgumentException("eventName must not be null or empty.", nameof(eventName));
            _items.Add($"{gameId}:{eventName}");
        }

        public IReadOnlyList<string> Flush()
        {
            var snapshot = new List<string>(_items);
            _items.Clear();
            return snapshot;
        }

        public void Clear() => _items.Clear();
    }
}
