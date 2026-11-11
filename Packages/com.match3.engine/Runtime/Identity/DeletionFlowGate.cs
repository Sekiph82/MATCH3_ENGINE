// ENG-M17-008 DeletionFlowGate stdlib contract
// stdlib only: System, System.Collections.Generic
using System;
using System.Collections.Generic;

namespace Match3.Engine.Runtime.Identity
{
    public enum DeletionScope
    {
        AccountOnly,
        AccountAndCloudSave,
        Full
    }

    public readonly struct DeletionRequest
    {
        public string GameId { get; }
        public DeletionScope Scope { get; }
        public string RequestorId { get; }

        private DeletionRequest(string gameId, DeletionScope scope, string requestorId)
        {
            GameId = gameId;
            Scope = scope;
            RequestorId = requestorId;
        }

        public static DeletionRequest Create(string gameId, DeletionScope scope, string requestorId)
        {
            if (string.IsNullOrEmpty(gameId))
                throw new ArgumentException("GameId must not be null or empty.", nameof(gameId));
            if (string.IsNullOrEmpty(requestorId))
                throw new ArgumentException("RequestorId must not be null or empty.", nameof(requestorId));
            return new DeletionRequest(gameId, scope, requestorId);
        }

        public bool IsFullDeletion => Scope == DeletionScope.Full;
    }

    public readonly struct DeletionPolicy
    {
        public bool RequireExplicitConfirmation { get; }
        public bool AllowSilentDelete { get; }
        public string PolicyDescription { get; }

        private DeletionPolicy(bool requireExplicitConfirmation, bool allowSilentDelete, string policyDescription)
        {
            if (string.IsNullOrEmpty(policyDescription))
                throw new ArgumentException("PolicyDescription must not be null or empty.", nameof(policyDescription));
            RequireExplicitConfirmation = requireExplicitConfirmation;
            AllowSilentDelete = allowSilentDelete;
            PolicyDescription = policyDescription;
        }

        public static DeletionPolicy Safe(string desc)
        {
            return new DeletionPolicy(true, false, desc);
        }

        public static DeletionPolicy Default =>
            Safe("Explicit confirmation required; silent deletion prohibited");
    }

    public static class DeletionFlowGate
    {
        public static (bool eligible, string reason) CanInitiateDeletion(string gameId, DeletionPolicy policy)
        {
            // Guard 1 (FIRST): Wonder_Pop Ordinal check
            if (string.Equals(gameId, "Wonder_Pop", StringComparison.Ordinal))
                return (false, "WonderPopDeletionProhibited");

            // Guard 2: silent deletion check
            if (policy.AllowSilentDelete && !policy.RequireExplicitConfirmation)
                return (false, "SilentDeletionProhibited");

            return (true, string.Empty);
        }

        public static bool RequiresConfirmation(DeletionPolicy policy) =>
            policy.RequireExplicitConfirmation;

        public static bool IsFullScopeDeletion(DeletionRequest request) =>
            request.IsFullDeletion;
    }

    public sealed class DeletionAuditLog
    {
        private readonly List<(DeletionRequest request, bool confirmed, string reason)> _entries =
            new List<(DeletionRequest, bool, string)>();

        public int Count => _entries.Count;

        public void Record(DeletionRequest request, bool confirmed, string reason)
        {
            if (string.IsNullOrEmpty(reason))
                throw new ArgumentException("reason must not be null or empty.", nameof(reason));
            _entries.Add((request, confirmed, reason));
        }

        public void Clear() => _entries.Clear();

        public bool HasEntry(string gameId)
        {
            foreach (var entry in _entries)
                if (string.Equals(entry.request.GameId, gameId, StringComparison.Ordinal))
                    return true;
            return false;
        }
    }
}
