using System;
using System.Collections.Generic;

namespace Match3.Engine.Runtime.Privacy
{
    public enum AttAuthorizationStatus
    {
        NotDetermined,
        Restricted,
        Denied,
        Authorized,
        NotApplicable
    }

    public readonly struct AttRecord
    {
        public string GameId { get; }
        public AttAuthorizationStatus Status { get; }
        public bool IsChildProfile { get; }
        public bool IsCrossAppTrackingProposed { get; }

        private AttRecord(string gameId, AttAuthorizationStatus status, bool isChildProfile, bool isCrossAppTrackingProposed)
        {
            GameId = gameId;
            Status = status;
            IsChildProfile = isChildProfile;
            IsCrossAppTrackingProposed = isCrossAppTrackingProposed;
        }

        public static AttRecord Create(string gameId, AttAuthorizationStatus status, bool isChildProfile, bool isCrossAppTrackingProposed)
        {
            if (string.IsNullOrEmpty(gameId))
                throw new ArgumentException("gameId must not be null or empty.", nameof(gameId));
            return new AttRecord(gameId, status, isChildProfile, isCrossAppTrackingProposed);
        }

        public bool IsTrackingAuthorized => Status == AttAuthorizationStatus.Authorized && !IsChildProfile;

        public bool RequiresAttRequest => IsCrossAppTrackingProposed && Status == AttAuthorizationStatus.NotDetermined && !IsChildProfile;
    }

    public readonly struct AttPolicy
    {
        public bool AllowCrossAppTracking { get; }
        public bool BlockChildProfiles { get; }
        public string PolicyDescription { get; }

        private AttPolicy(bool allowCrossAppTracking, bool blockChildProfiles, string policyDescription)
        {
            AllowCrossAppTracking = allowCrossAppTracking;
            BlockChildProfiles = blockChildProfiles;
            PolicyDescription = policyDescription;
        }

        public static AttPolicy Safe(string desc)
        {
            if (string.IsNullOrEmpty(desc))
                throw new ArgumentException("PolicyDescription must not be null or empty.", nameof(desc));
            return new AttPolicy(false, true, desc);
        }

        public static readonly AttPolicy Default = Safe("Default ATT policy - no cross-app tracking, block child profiles");
    }

    public static class AppleAttGate
    {
        public static bool CanRequestAttPermission(string gameId, AttRecord record, AttPolicy policy, out string reason)
        {
            if (string.Equals(gameId, "Wonder_Pop", StringComparison.Ordinal))
            {
                reason = "WonderPopAttProhibited";
                return false;
            }

            if (!policy.AllowCrossAppTracking)
            {
                reason = "CrossAppTrackingDisabled";
                return false;
            }

            if (policy.BlockChildProfiles && record.IsChildProfile)
            {
                reason = "ChildProfileBlocked";
                return false;
            }

            if (!record.IsCrossAppTrackingProposed)
            {
                reason = "CrossAppTrackingNotProposed";
                return false;
            }

            reason = string.Empty;
            return true;
        }

        public static bool IsTrackingAuthorized(AttRecord record) => record.IsTrackingAuthorized;

        public static bool RequiresAttRequest(AttRecord record) => record.RequiresAttRequest;
    }

    public sealed class AttLog
    {
        private readonly List<string> _entries = new List<string>();

        public void Record(string gameId, string statusEvent)
        {
            if (string.IsNullOrEmpty(gameId))
                throw new ArgumentException("gameId must not be null or empty.", nameof(gameId));
            _entries.Add(gameId + ":" + statusEvent);
        }

        public int Count => _entries.Count;

        public void Clear() => _entries.Clear();

        public bool HasEntry(string gameId) => _entries.Exists(e => e.StartsWith(gameId + ":"));
    }
}
