using System;
using System.Collections.Generic;

namespace Match3.Engine.Runtime.Privacy
{
    public enum FamilyControlFeature
    {
        ParentalGateExternalLink,
        ParentalGatePurchase,
        ThirdPartyAdSdk,
        ThirdPartyAnalyticsSdk,
        ChatFeature,
        SocialFeature,
        LocationFeature
    }

    public readonly struct FamilyControlRecord
    {
        public string GameId { get; }
        public FamilyControlFeature Feature { get; }
        public bool HasParentalConsent { get; }
        public bool IsKidsCategoryEnrolled { get; }

        private FamilyControlRecord(string gameId, FamilyControlFeature feature, bool hasParentalConsent, bool isKidsCategoryEnrolled)
        {
            GameId = gameId;
            Feature = feature;
            HasParentalConsent = hasParentalConsent;
            IsKidsCategoryEnrolled = isKidsCategoryEnrolled;
        }

        public static FamilyControlRecord Create(string gameId, FamilyControlFeature feature, bool hasParentalConsent, bool isKidsCategoryEnrolled)
        {
            if (string.IsNullOrEmpty(gameId))
                throw new ArgumentException("gameId cannot be null or empty", nameof(gameId));
            return new FamilyControlRecord(gameId, feature, hasParentalConsent, isKidsCategoryEnrolled);
        }

        public bool RequiresParentalGate =>
            Feature == FamilyControlFeature.ParentalGateExternalLink ||
            Feature == FamilyControlFeature.ParentalGatePurchase;

        public bool IsThirdPartySdk =>
            Feature == FamilyControlFeature.ThirdPartyAdSdk ||
            Feature == FamilyControlFeature.ThirdPartyAnalyticsSdk;
    }

    public readonly struct FamilyFriendlyPolicy
    {
        public bool AllowThirdPartySdks { get; }
        public bool RequireParentalGateForExternalLinks { get; }
        public bool BlockSocialInKidsCategory { get; }
        public string PolicyDescription { get; }

        private FamilyFriendlyPolicy(bool allowThirdPartySdks, bool requireParentalGateForExternalLinks, bool blockSocialInKidsCategory, string policyDescription)
        {
            if (string.IsNullOrEmpty(policyDescription))
                throw new ArgumentException("PolicyDescription cannot be null or empty", nameof(policyDescription));
            AllowThirdPartySdks = allowThirdPartySdks;
            RequireParentalGateForExternalLinks = requireParentalGateForExternalLinks;
            BlockSocialInKidsCategory = blockSocialInKidsCategory;
            PolicyDescription = policyDescription;
        }

        public static FamilyFriendlyPolicy Safe(string desc)
        {
            return new FamilyFriendlyPolicy(false, true, true, desc);
        }

        public static FamilyFriendlyPolicy Create(bool allowThirdPartySdks, bool requireParentalGateForExternalLinks, bool blockSocialInKidsCategory, string policyDescription)
        {
            return new FamilyFriendlyPolicy(allowThirdPartySdks, requireParentalGateForExternalLinks, blockSocialInKidsCategory, policyDescription);
        }

        public static readonly FamilyFriendlyPolicy Default = Safe("Default family policy - no third-party SDKs, require parental gate, block social in kids category");
    }

    public static class FamilyFriendlyControlsGate
    {
        public static bool CanEnableFeature(string gameId, FamilyControlRecord record, FamilyFriendlyPolicy policy, out string reason)
        {
            if (policy.PolicyDescription == null) policy = FamilyFriendlyPolicy.Default;

            if (string.IsNullOrEmpty(gameId))
            {
                reason = "GameIdMissing";
                return false;
            }
            if (!GameProfileCatalog.IsKnownId(gameId))
            {
                reason = "UnknownGame";
                return false;
            }

            if (!string.Equals(gameId, record.GameId, StringComparison.Ordinal))
            {
                reason = "GameIdMismatch";
                return false;
            }

            // Guard 1: Wonder_Pop blocks third-party ad/analytics SDKs by default
            // Wonder_Pop is GENERAL/FAMILY-FRIENDLY, not children-only
            if (string.Equals(gameId, "Wonder_Pop", StringComparison.Ordinal) && record.IsThirdPartySdk)
            {
                reason = "WonderPopThirdPartySdkProhibited";
                return false;
            }

            // Guard 2: policy-level third-party SDK block
            if (!policy.AllowThirdPartySdks && record.IsThirdPartySdk)
            {
                reason = "ThirdPartySdkDisabled";
                return false;
            }

            // Guard 3: parental gate required for external links/purchases
            if (policy.RequireParentalGateForExternalLinks && record.RequiresParentalGate && !record.HasParentalConsent)
            {
                reason = "ParentalGateRequired";
                return false;
            }

            // Guard 4: social/chat blocked in kids category enrollment
            if (policy.BlockSocialInKidsCategory && record.IsKidsCategoryEnrolled &&
                (record.Feature == FamilyControlFeature.SocialFeature || record.Feature == FamilyControlFeature.ChatFeature))
            {
                reason = "SocialBlockedInKidsCategory";
                return false;
            }

            reason = string.Empty;
            return true;
        }

        public static bool RequiresParentalGate(FamilyControlRecord record) => record.RequiresParentalGate;

        public static bool IsThirdPartySdk(FamilyControlRecord record) => record.IsThirdPartySdk;
    }

    public sealed class FamilyControlLog
    {
        private readonly List<string> _entries = new List<string>();

        public void Record(string gameId, string featureName)
        {
            if (string.IsNullOrEmpty(gameId))
                throw new ArgumentException("gameId cannot be null or empty", nameof(gameId));
            if (string.IsNullOrEmpty(featureName))
                throw new ArgumentException("featureName cannot be null or empty", nameof(featureName));
            _entries.Add(gameId + ":" + featureName);
        }

        public int Count => _entries.Count;

        public void Clear() => _entries.Clear();

        public bool HasEntry(string gameId) => _entries.Exists(e => e.StartsWith(gameId + ":", StringComparison.Ordinal));
    }
}
