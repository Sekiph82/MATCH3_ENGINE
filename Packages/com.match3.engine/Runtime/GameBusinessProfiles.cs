using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Match3.Engine.Runtime
{
    public static class GameBusinessAdFormats
    {
        public static IReadOnlyList<string> StandardFormats { get; } =
            Array.AsReadOnly(new[]
            {
                "banner",
                "interstitial",
                "rewarded",
                "native",
                "app_open"
            });
    }

    public enum BusinessAgeAudience
    {
        Unspecified = 0,
        GeneralAudience = 1,
        FamilyNinePlus = 2
    }

    public enum BusinessDecisionState
    {
        PendingOwnerInput = 0,
        OwnerApproved = 1
    }

    public enum DistributionMarketScope
    {
        PendingOwnerInput = 0,
        Worldwide = 1
    }

    public sealed class GameBusinessProfile
    {
        private readonly ReadOnlyCollection<string> _allowedAdFormats;

        public string GameId { get; }
        public BusinessAgeAudience AgeAudience { get; }
        public BusinessDecisionState AgeAudienceDecision { get; }
        public string LegalJurisdiction { get; }
        public BusinessDecisionState LegalJurisdictionDecision { get; }
        public DistributionMarketScope DistributionScope { get; }
        public BusinessDecisionState DistributionScopeDecision { get; }
        public bool AdsEnabled { get; }
        public IReadOnlyList<string> AllowedAdFormats => _allowedAdFormats;
        public BusinessDecisionState AdFormatDecision { get; }
        public string ProposedSkuNamespace { get; }
        public BusinessDecisionState SkuNamespaceDecision { get; }
        public bool TelemetryEnabled { get; }
        public BusinessDecisionState TelemetryDecision { get; }
        public ProfileContentChannel ContentChannel { get; }
        public bool MonetizationEnabled { get; }
        public BusinessDecisionState MonetizationDecision { get; }

        internal GameBusinessProfile(string gameId)
        {
            if (!GameProfileCatalog.IsKnownId(gameId))
                throw new ArgumentException("Unknown game business profile ID.", nameof(gameId));
            GameId = gameId;
            // Owner directive 2026-11-10 V04: Wonder Pop audience updated to GeneralAudience (OwnerApproved)
            AgeAudience = BusinessAgeAudience.GeneralAudience;
            AgeAudienceDecision = BusinessDecisionState.OwnerApproved;
            LegalJurisdiction = null;
            LegalJurisdictionDecision = BusinessDecisionState.PendingOwnerInput;
            var adsApproved = !StringComparer.Ordinal.Equals(gameId, "Wonder_Pop");
            DistributionScope = DistributionMarketScope.Worldwide;
            DistributionScopeDecision = BusinessDecisionState.OwnerApproved;
            AdsEnabled = adsApproved;
            _allowedAdFormats = adsApproved
                ? Array.AsReadOnly(GameBusinessAdFormats.StandardFormats.ToArray())
                : Array.AsReadOnly(Array.Empty<string>());
            AdFormatDecision = BusinessDecisionState.OwnerApproved;
            ProposedSkuNamespace = "match3." + gameId.ToLowerInvariant().Replace('_', '.') + ".";
            SkuNamespaceDecision = BusinessDecisionState.PendingOwnerInput;
            TelemetryEnabled = adsApproved;
            TelemetryDecision = BusinessDecisionState.OwnerApproved;
            ContentChannel = ProfileContentChannel.Development;
            MonetizationEnabled = adsApproved;
            MonetizationDecision = BusinessDecisionState.OwnerApproved;
        }
    }

    public static class GameBusinessProfileCatalog
    {
        private static readonly ReadOnlyCollection<GameBusinessProfile> Profiles =
            Array.AsReadOnly(GameProfileCatalog.Ids
                .Select(id => new GameBusinessProfile(id)).ToArray());

        public static IReadOnlyList<GameBusinessProfile> All => Profiles;

        public static GameBusinessProfile Require(string gameId)
        {
            if (!GameProfileCatalog.IsKnownId(gameId))
                throw new ArgumentException("Unknown game business profile ID.", nameof(gameId));
            return Profiles.Single(profile =>
                StringComparer.Ordinal.Equals(profile.GameId, gameId));
        }
    }
}
