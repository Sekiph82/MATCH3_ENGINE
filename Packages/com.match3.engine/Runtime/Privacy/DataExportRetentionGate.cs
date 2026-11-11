using System;
using System.Collections.Generic;

namespace Match3.Engine.Runtime.Privacy
{
    public enum DataRetentionAction
    {
        ExportRequest,
        DeletionRequest,
        RetentionCheck,
        BackupPurge,
        SdkDataPurge,
        AuditLog
    }

    public readonly struct DataRetentionRecord
    {
        public string GameId { get; }
        public DataRetentionAction Action { get; }
        public bool IsUserInitiated { get; }
        public bool HasPendingPurge { get; }

        private DataRetentionRecord(string gameId, DataRetentionAction action, bool isUserInitiated, bool hasPendingPurge)
        {
            GameId = gameId;
            Action = action;
            IsUserInitiated = isUserInitiated;
            HasPendingPurge = hasPendingPurge;
        }

        public static DataRetentionRecord Create(string gameId, DataRetentionAction action, bool isUserInitiated, bool hasPendingPurge)
        {
            if (string.IsNullOrEmpty(gameId))
                throw new ArgumentException("gameId must not be null or empty.", nameof(gameId));
            return new DataRetentionRecord(gameId, action, isUserInitiated, hasPendingPurge);
        }

        public bool RequiresPurge =>
            Action == DataRetentionAction.DeletionRequest ||
            Action == DataRetentionAction.BackupPurge ||
            Action == DataRetentionAction.SdkDataPurge;

        public bool IsAuditAction => Action == DataRetentionAction.AuditLog;
    }

    public readonly struct DataRetentionPolicy
    {
        public bool EnforceRetentionSchedule { get; }
        public bool HonorDeletionRequests { get; }
        public string PolicyDescription { get; }

        private DataRetentionPolicy(bool enforceRetentionSchedule, bool honorDeletionRequests, string policyDescription)
        {
            if (string.IsNullOrEmpty(policyDescription))
                throw new ArgumentException("PolicyDescription must not be null or empty.", nameof(policyDescription));
            EnforceRetentionSchedule = enforceRetentionSchedule;
            HonorDeletionRequests = honorDeletionRequests;
            PolicyDescription = policyDescription;
        }

        public static DataRetentionPolicy Safe(string desc) =>
            new DataRetentionPolicy(true, true, desc);

        public static readonly DataRetentionPolicy Default =
            Safe("Default data retention policy - enforce retention schedule, honor deletion requests");
    }

    public static class DataExportRetentionGate
    {
        public static bool CanProcessDataAction(string gameId, DataRetentionRecord record, DataRetentionPolicy policy, out string reason)
        {
            if (policy.PolicyDescription == null) policy = DataRetentionPolicy.Default;

            if (string.IsNullOrEmpty(gameId))
            {
                reason = "GameIdMissing";
                return false;
            }

            if (!string.Equals(gameId, record.GameId, StringComparison.Ordinal))
            {
                reason = "GameIdMismatch";
                return false;
            }

            if (string.Equals(gameId, "Wonder_Pop", StringComparison.Ordinal) &&
                record.Action == DataRetentionAction.AuditLog &&
                !record.IsUserInitiated)
            {
                reason = "WonderPopNonUserAuditProhibited";
                return false;
            }

            if (policy.HonorDeletionRequests && record.RequiresPurge && !record.HasPendingPurge)
            {
                reason = "PurgeNotScheduled";
                return false;
            }

            if (policy.EnforceRetentionSchedule && record.Action == DataRetentionAction.RetentionCheck && !record.IsUserInitiated)
            {
                reason = "RetentionCheckRequiresUserContext";
                return false;
            }

            reason = string.Empty;
            return true;
        }

        public static bool RequiresPurge(DataRetentionRecord record) => record.RequiresPurge;

        public static bool IsAuditAction(DataRetentionRecord record) => record.IsAuditAction;
    }

    public sealed class DataRetentionLog
    {
        private readonly List<string> _entries = new List<string>();

        public void Record(string gameId, string actionName)
        {
            if (string.IsNullOrEmpty(gameId))
                throw new ArgumentException("gameId must not be null or empty.", nameof(gameId));
            if (string.IsNullOrEmpty(actionName))
                throw new ArgumentException("actionName must not be null or empty.", nameof(actionName));
            _entries.Add(gameId + ":" + actionName);
        }

        public int Count => _entries.Count;

        public void Clear() => _entries.Clear();

        public bool HasEntry(string gameId) => _entries.Exists(e => e.StartsWith(gameId + ":", StringComparison.Ordinal));
    }
}
