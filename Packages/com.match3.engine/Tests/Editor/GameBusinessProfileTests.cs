// ENG-M13-005 | Five game business profiles — EditMode tests
// Owner decisions recorded 2026-10-23; no paid accounts activated here.
using System.Linq;
using Match3.Engine.Runtime;
using NUnit.Framework;

namespace Match3.Engine.Tests.Editor
{
    public sealed class GameBusinessProfileTests
    {
        [Test]
        public void CatalogReturnsFiveDistinctProfiles()
        {
            Assert.That(GameBusinessProfileCatalog.All.Count, Is.EqualTo(5));
            var ids = GameBusinessProfileCatalog.All.Select(p => p.GameId).ToArray();
            Assert.That(ids.Distinct().Count(), Is.EqualTo(5));
            CollectionAssert.AreEquivalent(GameProfileCatalog.Ids, ids);
        }

        [Test]
        public void FourGeneralTitlesHaveAdsMonetizationAndTelemetryEnabled()
        {
            var generalIds = new[] { "Coral_Rescue", "Garage_Legends", "Dino_Islands", "The_Fractured_Realms" };
            foreach (var id in generalIds)
            {
                var bp = GameBusinessProfileCatalog.Require(id);
                Assert.That(bp.AgeAudience, Is.EqualTo(BusinessAgeAudience.GeneralAudience), id);
                Assert.That(bp.AdsEnabled, Is.True, id + " ads");
                Assert.That(bp.MonetizationEnabled, Is.True, id + " monetization");
                Assert.That(bp.TelemetryEnabled, Is.True, id + " telemetry");
                Assert.That(bp.AgeAudienceDecision, Is.EqualTo(BusinessDecisionState.OwnerApproved), id);
            }
        }

        [Test]
        public void WonderPopHasNoAdsNoMonetizationNoTelemetryAndGeneralAudience()
        {
            // Owner directive 2026-11-10 V04: Wonder Pop is GeneralAudience; ads/monetization/telemetry remain OFF (Category C reserved).
            var wp = GameBusinessProfileCatalog.Require("Wonder_Pop");
            Assert.That(wp.AgeAudience, Is.EqualTo(BusinessAgeAudience.GeneralAudience));
            Assert.That(wp.AdsEnabled, Is.False, "ads must be OFF for Wonder Pop");
            Assert.That(wp.MonetizationEnabled, Is.False, "monetization must be OFF");
            Assert.That(wp.TelemetryEnabled, Is.False, "telemetry must be OFF");
            Assert.That(wp.AllowedAdFormats.Count, Is.EqualTo(0));
        }

        [Test]
        public void AllFiveProfilesTargetWorldwideDistribution()
        {
            foreach (var bp in GameBusinessProfileCatalog.All)
            {
                Assert.That(bp.DistributionScope, Is.EqualTo(DistributionMarketScope.Worldwide),
                    bp.GameId);
                Assert.That(bp.DistributionScopeDecision,
                    Is.EqualTo(BusinessDecisionState.OwnerApproved), bp.GameId);
            }
        }

        [Test]
        public void CrossTitleIsolation_ExactlyFourTitlesHaveAdsOnAndOneOff()
        {
            var all = GameBusinessProfileCatalog.All;
            Assert.That(all.Count(p => p.AdsEnabled), Is.EqualTo(4),
                "exactly 4 general-audience titles may have ads ON");
            Assert.That(all.Count(p => !p.AdsEnabled), Is.EqualTo(1),
                "exactly 1 title (Wonder Pop) has ads OFF");
        }

        [Test]
        public void AllDecisionsAreOwnerApprovedExceptSkuNamespaceAndJurisdiction()
        {
            foreach (var bp in GameBusinessProfileCatalog.All)
            {
                Assert.That(bp.AgeAudienceDecision, Is.EqualTo(BusinessDecisionState.OwnerApproved), bp.GameId);
                Assert.That(bp.AdFormatDecision, Is.EqualTo(BusinessDecisionState.OwnerApproved), bp.GameId);
                Assert.That(bp.TelemetryDecision, Is.EqualTo(BusinessDecisionState.OwnerApproved), bp.GameId);
                Assert.That(bp.MonetizationDecision, Is.EqualTo(BusinessDecisionState.OwnerApproved), bp.GameId);
                Assert.That(bp.DistributionScopeDecision, Is.EqualTo(BusinessDecisionState.OwnerApproved), bp.GameId);
                // Jurisdiction and SKU namespace remain pending per original owner scoping.
                Assert.That(bp.LegalJurisdictionDecision,
                    Is.EqualTo(BusinessDecisionState.PendingOwnerInput), bp.GameId);
                Assert.That(bp.SkuNamespaceDecision,
                    Is.EqualTo(BusinessDecisionState.PendingOwnerInput), bp.GameId);
            }
        }
    }
}
