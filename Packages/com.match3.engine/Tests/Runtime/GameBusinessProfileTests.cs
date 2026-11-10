using System;
using System.Linq;
using Match3.Engine.Runtime;
using NUnit.Framework;

namespace Match3.Engine.Tests
{
    public sealed class GameBusinessProfileTests
    {
        [Test]
        public void CatalogContainsExactlyFiveDistinctKnownGameProfiles()
        {
            Assert.That(GameBusinessProfileCatalog.All.Count, Is.EqualTo(5));
            Assert.That(GameBusinessProfileCatalog.All.Select(x => x.GameId).Distinct().Count(),
                Is.EqualTo(5));
            CollectionAssert.AreEquivalent(GameProfileCatalog.Ids,
                GameBusinessProfileCatalog.All.Select(x => x.GameId));
        }

        [Test]
        public void OwnerApprovedBusinessPoliciesAreScopedPerTitle()
        {
            foreach (var profile in GameBusinessProfileCatalog.All)
            {
                // Owner directive 2026-11-10 V04: Wonder Pop is now GeneralAudience (OwnerApproved)
                Assert.That(profile.AgeAudience, Is.EqualTo(BusinessAgeAudience.GeneralAudience));
                Assert.That(profile.AgeAudienceDecision,
                    Is.EqualTo(BusinessDecisionState.OwnerApproved));
                var adsApproved = profile.GameId != "Wonder_Pop";
                Assert.That(profile.AllowedAdFormats.Count,
                    Is.EqualTo(adsApproved
                        ? GameBusinessAdFormats.StandardFormats.Count
                        : 0));
                Assert.That(profile.AdFormatDecision,
                    Is.EqualTo(BusinessDecisionState.OwnerApproved));
                Assert.That(profile.LegalJurisdiction, Is.Null);
                Assert.That(profile.LegalJurisdictionDecision,
                    Is.EqualTo(BusinessDecisionState.PendingOwnerInput));
                Assert.That(profile.DistributionScope,
                    Is.EqualTo(DistributionMarketScope.Worldwide));
                Assert.That(profile.DistributionScopeDecision,
                    Is.EqualTo(BusinessDecisionState.OwnerApproved));
                Assert.That(profile.AdsEnabled, Is.EqualTo(adsApproved));
                Assert.That(profile.TelemetryEnabled, Is.EqualTo(adsApproved));
                Assert.That(profile.TelemetryDecision,
                    Is.EqualTo(BusinessDecisionState.OwnerApproved));
                Assert.That(profile.MonetizationEnabled, Is.EqualTo(adsApproved));
                Assert.That(profile.MonetizationDecision,
                    Is.EqualTo(BusinessDecisionState.OwnerApproved));
                Assert.That(profile.ContentChannel,
                    Is.EqualTo(ProfileContentChannel.Development));
            }
        }

        [Test]
        public void ProposedSkuNamespacesAreUniqueButRemainPendingOwnerApproval()
        {
            Assert.That(GameBusinessProfileCatalog.All
                .Select(x => x.ProposedSkuNamespace).Distinct(StringComparer.Ordinal).Count(),
                Is.EqualTo(5));
            Assert.That(GameBusinessProfileCatalog.All.All(x =>
                x.SkuNamespaceDecision == BusinessDecisionState.PendingOwnerInput), Is.True);
        }

        [Test]
        public void LookupIsTitleScopedAndUnknownGameIsRejected()
        {
            Assert.That(GameBusinessProfileCatalog.Require("Coral_Rescue").GameId,
                Is.EqualTo("Coral_Rescue"));
            Assert.That(GameBusinessProfileCatalog.Require("Wonder_Pop").GameId,
                Is.EqualTo("Wonder_Pop"));
            Assert.Throws<ArgumentException>(() =>
                GameBusinessProfileCatalog.Require("unknown.game"));
        }
    }
}
