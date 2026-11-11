// ENG-M17-011 ChildAccountGate stdlib contract
// stdlib only: System, System.Collections.Generic
using System;
using System.Collections.Generic;

namespace Match3.Engine.Runtime.Identity
{
    public enum AgeCategory
    {
        Adult,
        MinorWithParent,
        MinorUnverified
    }

    public readonly struct ChildProfile
    {
        public string GameId { get; }
        public AgeCategory Category { get; }
        public bool LocalOnly { get; }

        private ChildProfile(string gameId, AgeCategory category, bool localOnly)
        {
            GameId = gameId;
            Category = category;
            LocalOnly = localOnly;
        }

        public static ChildProfile Create(string gameId, AgeCategory category, bool localOnly)
        {
            if (string.IsNullOrEmpty(gameId))
                throw new ArgumentException("GameId must not be null or empty.", nameof(gameId));
            return new ChildProfile(gameId, category, localOnly);
        }

        public bool IsRestricted => Category != AgeCategory.Adult || LocalOnly;
    }

    public readonly struct ChildAccountPolicy
    {
        public bool AllowSocialFeatures { get; }
        public bool AllowPublicHandle { get; }
        public string PolicyDescription { get; }

        public ChildAccountPolicy(bool allowSocialFeatures, bool allowPublicHandle, string policyDescription)
        {
            if (string.IsNullOrEmpty(policyDescription))
                throw new ArgumentException("PolicyDescription must not be null or empty.", nameof(policyDescription));
            AllowSocialFeatures = allowSocialFeatures;
            AllowPublicHandle = allowPublicHandle;
            PolicyDescription = policyDescription;
        }

        public static ChildAccountPolicy Safe(string desc)
        {
            return new ChildAccountPolicy(false, false, desc);
        }

        public static ChildAccountPolicy Default =>
            Safe("No social features or public handles for child or unverified accounts");
    }

    public static class ChildAccountGate
    {
        private static (bool eligible, string reason) Ineligible(string reason) =>
            (false, reason);

        private static (bool eligible, string reason) Eligible() =>
            (true, string.Empty);

        private static ChildAccountPolicy Normalize(ChildAccountPolicy policy) =>
            policy.PolicyDescription == null ? ChildAccountPolicy.Default : policy;

        public static (bool eligible, string reason) CanUseSocialFeatures(
            string gameId, ChildProfile profile, ChildAccountPolicy policy)
        {
            policy = Normalize(policy);

            if (!string.Equals(gameId, profile.GameId, StringComparison.Ordinal))
                return Ineligible("GameIdMismatch");

            // Wonder_Pop is always blocked (Ordinal)
            if (string.Equals(gameId, "Wonder_Pop", StringComparison.Ordinal))
                return Ineligible("WonderPopSocialProhibited");

            // Unconditional: child, unverified-minor and local-only profiles never get social features
            if (profile.IsRestricted)
                return Ineligible("RestrictedProfileProhibited");

            // Policy may only add restriction
            if (!policy.AllowSocialFeatures)
                return Ineligible("SocialFeaturesDisabled");

            return Eligible();
        }

        public static (bool eligible, string reason) CanUsePublicHandle(
            string gameId, ChildProfile profile, ChildAccountPolicy policy)
        {
            policy = Normalize(policy);

            if (!string.Equals(gameId, profile.GameId, StringComparison.Ordinal))
                return Ineligible("GameIdMismatch");

            // Wonder_Pop is always blocked (Ordinal)
            if (string.Equals(gameId, "Wonder_Pop", StringComparison.Ordinal))
                return Ineligible("WonderPopHandleProhibited");

            // Unconditional: child (MinorWithParent), MinorUnverified and local-only profiles never get a public handle
            if (profile.IsRestricted)
                return Ineligible("RestrictedProfileProhibited");

            // Policy may only add restriction
            if (!policy.AllowPublicHandle)
                return Ineligible("PublicHandleDisabled");

            return Eligible();
        }

        /// <summary>Profile unknown: fails closed with "RestrictedProfileProhibited".</summary>
        [Obsolete("Profile unknown: always ineligible (RestrictedProfileProhibited). Use the overload taking a ChildProfile.")]
        public static (bool eligible, string reason) CanUsePublicHandle(
            string gameId, ChildAccountPolicy policy)
        {
            if (string.Equals(gameId, "Wonder_Pop", StringComparison.Ordinal))
                return Ineligible("WonderPopHandleProhibited");

            return Ineligible("RestrictedProfileProhibited");
        }

        public static bool IsChildRestricted(ChildProfile profile) => profile.IsRestricted;
    }

    public sealed class ChildAuditLog
    {
        private readonly List<(ChildProfile profile, bool allowed, string reason)> _entries =
            new List<(ChildProfile, bool, string)>();

        public int Count => _entries.Count;

        public void Record(ChildProfile profile, bool allowed, string reason)
        {
            if (string.IsNullOrEmpty(profile.GameId))
                throw new ArgumentException("Profile GameId must not be null or empty.", nameof(profile));
            if (string.IsNullOrEmpty(reason))
                throw new ArgumentException("Reason must not be null or empty.", nameof(reason));
            _entries.Add((profile, allowed, reason));
        }

        public void Clear() => _entries.Clear();

        public bool HasEntry(string gameId)
        {
            foreach (var e in _entries)
                if (string.Equals(e.profile.GameId, gameId, StringComparison.Ordinal)) return true;
            return false;
        }
    }
}
