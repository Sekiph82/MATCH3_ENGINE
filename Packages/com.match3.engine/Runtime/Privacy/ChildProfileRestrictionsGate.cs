using System;
using System.Collections.Generic;

namespace Match3.Engine.Runtime.Privacy
{
    public enum ChildProfileFeature
    {
        BehavioralProfiling,
        RandomizedLootPack,
        SocialRank,
        ForcedStorePrompt,
        SocialInvite,
        TournamentEntry,
        RemoteConfigOverride
    }

    public readonly struct ChildRestrictionRecord
    {
        public string GameId { get; }
        public ChildProfileFeature Feature { get; }
        public bool HasParentalApproval { get; }
        public bool IsOwnerApproved { get; }

        private ChildRestrictionRecord(string gameId, ChildProfileFeature feature, bool hasParentalApproval, bool isOwnerApproved)
        {
            GameId = gameId;
            Feature = feature;
            HasParentalApproval = hasParentalApproval;
            IsOwnerApproved = isOwnerApproved;
        }

        public static ChildRestrictionRecord Create(string gameId, ChildProfileFeature feature, bool hasParentalApproval, bool isOwnerApproved)
        {
            if (string.IsNullOrEmpty(gameId))
                throw new ArgumentException("gameId cannot be null or empty", nameof(gameId));
            return new ChildRestrictionRecord(gameId, feature, hasParentalApproval, isOwnerApproved);
        }

        public bool IsOwnerLockedFeature =>
            Feature == ChildProfileFeature.SocialInvite ||
            Feature == ChildProfileFeature.TournamentEntry ||
            Feature == ChildProfileFeature.RemoteConfigOverride;

        public bool IsDefaultRestricted => true;
    }

    public readonly struct ChildProfilePolicy
    {
        public bool EnforceChildRestrictions { get; }
        public bool BlockOwnerLockedFeatures { get; }
        public string PolicyDescription { get; }

        private ChildProfilePolicy(bool enforceChildRestrictions, bool blockOwnerLockedFeatures, string policyDescription)
        {
            EnforceChildRestrictions = enforceChildRestrictions;
            BlockOwnerLockedFeatures = blockOwnerLockedFeatures;
            PolicyDescription = policyDescription;
        }

        public static ChildProfilePolicy Safe(string desc)
        {
            if (string.IsNullOrEmpty(desc))
                throw new ArgumentException("PolicyDescription cannot be null or empty", nameof(desc));
            return new ChildProfilePolicy(true, true, desc);
        }

        public static readonly ChildProfilePolicy Default = Safe("Default child profile policy - enforce child restrictions, block owner-locked features");
    }

    public static class ChildProfileRestrictionsGate
    {
        public static bool CanEnableFeature(string gameId, ChildRestrictionRecord record, ChildProfilePolicy policy, out string reason)
        {
            // Guard 1: Wonder Pop + RemoteConfigOverride always blocked
            if (string.Equals(gameId, "Wonder_Pop", StringComparison.Ordinal) &&
                record.Feature == ChildProfileFeature.RemoteConfigOverride)
            {
                reason = "WonderPopRemoteConfigOverrideProhibited";
                return false;
            }

            // Guard 2: Wonder Pop + owner-locked + no owner approval
            if (string.Equals(gameId, "Wonder_Pop", StringComparison.Ordinal) &&
                record.IsOwnerLockedFeature && !record.IsOwnerApproved)
            {
                reason = "WonderPopOwnerApprovalRequired";
                return false;
            }

            // Guard 3: Wonder Pop + enforce child restrictions + no parental approval
            if (policy.EnforceChildRestrictions &&
                string.Equals(gameId, "Wonder_Pop", StringComparison.Ordinal) &&
                !record.HasParentalApproval)
            {
                reason = "WonderPopParentalApprovalRequired";
                return false;
            }

            // Guard 4: non-Wonder_Pop + block owner-locked + owner-locked + no owner approval
            if (policy.BlockOwnerLockedFeatures && record.IsOwnerLockedFeature && !record.IsOwnerApproved)
            {
                reason = "OwnerApprovalRequired";
                return false;
            }

            reason = string.Empty;
            return true;
        }

        public static bool IsOwnerLockedFeature(ChildRestrictionRecord record) => record.IsOwnerLockedFeature;

        public static bool IsDefaultRestricted(ChildRestrictionRecord record) => record.IsDefaultRestricted;
    }

    public sealed class ChildRestrictionLog
    {
        private readonly List<string> _entries = new List<string>();

        public void Record(string gameId, string featureName)
        {
            if (string.IsNullOrEmpty(gameId))
                throw new ArgumentException("gameId cannot be null or empty", nameof(gameId));
            _entries.Add(gameId + ":" + featureName);
        }

        public int Count => _entries.Count;

        public void Clear() => _entries.Clear();

        public bool HasEntry(string gameId) => _entries.Exists(e => e.StartsWith(gameId + ":"));
    }
}
