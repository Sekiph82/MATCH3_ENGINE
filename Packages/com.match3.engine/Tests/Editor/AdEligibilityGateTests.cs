using System;
using NUnit.Framework;
using Match3.Engine.Runtime;
using Match3.Engine.Runtime.Commerce;

namespace Match3.Engine.Tests.Editor
{
    public sealed class AdEligibilityGateTests
    {
        [Test]
        public void WonderPop_IsAlwaysIneligible_WhenConsentGranted()
        {
            var result = AdEligibilityGate.Check("Wonder_Pop", AdConsentStatus.Granted, false);
            Assert.That(result.IsEligible, Is.False);
            Assert.That(result.Reason, Is.EqualTo("WonderPopExcluded"));
        }

        [Test]
        public void WonderPop_IsAlwaysIneligible_WhenConsentNotRequired()
        {
            var result = AdEligibilityGate.Check("Wonder_Pop", AdConsentStatus.NotRequired, false);
            Assert.That(result.IsEligible, Is.False);
            Assert.That(result.Reason, Is.EqualTo("WonderPopExcluded"));
        }

        [Test]
        public void ConsentDenied_IsIneligible()
        {
            var result = AdEligibilityGate.Check("Coral_Rescue", AdConsentStatus.Denied, false);
            Assert.That(result.IsEligible, Is.False);
            Assert.That(result.Reason, Is.EqualTo("ConsentNotGranted"));
        }

        [Test]
        public void ConsentUnknown_IsIneligible()
        {
            var result = AdEligibilityGate.Check("Coral_Rescue", AdConsentStatus.Unknown, false);
            Assert.That(result.IsEligible, Is.False);
            Assert.That(result.Reason, Is.EqualTo("ConsentNotGranted"));
        }

        [Test]
        public void ConsentGranted_ValidGame_IsEligible()
        {
            var result = AdEligibilityGate.Check("Garage_Legends", AdConsentStatus.Granted, false);
            Assert.That(result.IsEligible, Is.True);
            Assert.That(result.Reason, Is.EqualTo(string.Empty));
        }

        [Test]
        public void ConsentNotRequired_ValidGame_IsEligible()
        {
            var result = AdEligibilityGate.Check("Dino_Islands", AdConsentStatus.NotRequired, false);
            Assert.That(result.IsEligible, Is.True);
            Assert.That(result.Reason, Is.EqualTo(string.Empty));
        }

        [Test]
        public void CheckWithProfile_AdsNotApproved_IsIneligible()
        {
            // Wonder_Pop has AdsEnabled=false from catalog
            var profile = GameBusinessProfileCatalog.Require("Wonder_Pop");
            Assert.That(profile.AdsEnabled, Is.False, "Precondition: Wonder_Pop must have AdsEnabled=false");
            var result = AdEligibilityGate.CheckWithProfile(profile, AdConsentStatus.Granted, false);
            Assert.That(result.IsEligible, Is.False);
            Assert.That(result.Reason, Is.EqualTo("AdsNotApproved"));
        }

        [Test]
        public void CheckWithProfile_MonetizationDisabled_IsIneligible()
        {
            // Wonder_Pop has MonetizationEnabled=false from catalog
            var profile = GameBusinessProfileCatalog.Require("Wonder_Pop");
            Assert.That(profile.MonetizationEnabled, Is.False, "Precondition: Wonder_Pop must have MonetizationEnabled=false");
            var result = AdEligibilityGate.CheckWithProfile(profile, AdConsentStatus.NotRequired, false);
            Assert.That(result.IsEligible, Is.False);
        }

        [Test]
        public void CheckWithProfile_WonderPop_IsIneligible_ViaCheckWithProfile()
        {
            var profile = GameBusinessProfileCatalog.Require("Wonder_Pop");
            var result = AdEligibilityGate.CheckWithProfile(profile, AdConsentStatus.Granted, false);
            Assert.That(result.IsEligible, Is.False);
        }

        [Test]
        public void CheckWithProfile_ApprovedGame_ConsentGranted_IsEligible()
        {
            var profile = GameBusinessProfileCatalog.Require("Coral_Rescue");
            Assert.That(profile.AdsEnabled, Is.True, "Precondition: Coral_Rescue must have AdsEnabled=true");
            Assert.That(profile.MonetizationEnabled, Is.True, "Precondition: Coral_Rescue must have MonetizationEnabled=true");
            var result = AdEligibilityGate.CheckWithProfile(profile, AdConsentStatus.Granted, false);
            Assert.That(result.IsEligible, Is.True);
        }

        [Test]
        public void Ineligible_NullReason_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => AdEligibilityResult.Ineligible(null));
        }

        [Test]
        public void KidProfile_IneligibleForEveryGame_EvenWithConsent()
        {
            foreach (var gameId in new[] { "Coral_Rescue", "Dino_Islands", "Garage_Legends", "The_Fractured_Realms" })
                foreach (var consent in new[] { AdConsentStatus.Granted, AdConsentStatus.NotRequired })
                {
                    var result = AdEligibilityGate.Check(gameId, consent, true);
                    Assert.That(result.IsEligible, Is.False);
                    Assert.That(result.Reason, Is.EqualTo("KidProfileAdsProhibited"));
                }
        }

        [Test]
        public void CheckWithProfile_KidProfile_ApprovedGame_Ineligible()
        {
            var profile = GameBusinessProfileCatalog.Require("Coral_Rescue");
            var result = AdEligibilityGate.CheckWithProfile(profile, AdConsentStatus.Granted, true);
            Assert.That(result.IsEligible, Is.False);
            Assert.That(result.Reason, Is.EqualTo("KidProfileAdsProhibited"));
        }

        [Test]
        public void ObsoleteOverloads_KidStatusUnknown_FailClosed()
        {
#pragma warning disable 0618
            var r1 = AdEligibilityGate.Check("Coral_Rescue", AdConsentStatus.Granted);
            var r2 = AdEligibilityGate.CheckWithProfile(GameBusinessProfileCatalog.Require("Coral_Rescue"), AdConsentStatus.Granted);
#pragma warning restore 0618
            Assert.That(r1.IsEligible, Is.False);
            Assert.That(r1.Reason, Is.EqualTo("KidProfileAdsProhibited"));
            Assert.That(r2.IsEligible, Is.False);
            Assert.That(r2.Reason, Is.EqualTo("KidProfileAdsProhibited"));
        }
    }
}
