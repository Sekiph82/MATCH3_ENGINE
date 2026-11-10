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
            var result = AdEligibilityGate.Check("Wonder_Pop", AdConsentStatus.Granted);
            Assert.That(result.IsEligible, Is.False);
            Assert.That(result.Reason, Is.EqualTo("WonderPopExcluded"));
        }

        [Test]
        public void WonderPop_IsAlwaysIneligible_WhenConsentNotRequired()
        {
            var result = AdEligibilityGate.Check("Wonder_Pop", AdConsentStatus.NotRequired);
            Assert.That(result.IsEligible, Is.False);
            Assert.That(result.Reason, Is.EqualTo("WonderPopExcluded"));
        }

        [Test]
        public void ConsentDenied_IsIneligible()
        {
            var result = AdEligibilityGate.Check("Coral_Rescue", AdConsentStatus.Denied);
            Assert.That(result.IsEligible, Is.False);
            Assert.That(result.Reason, Is.EqualTo("ConsentNotGranted"));
        }

        [Test]
        public void ConsentUnknown_IsIneligible()
        {
            var result = AdEligibilityGate.Check("Coral_Rescue", AdConsentStatus.Unknown);
            Assert.That(result.IsEligible, Is.False);
            Assert.That(result.Reason, Is.EqualTo("ConsentNotGranted"));
        }

        [Test]
        public void ConsentGranted_ValidGame_IsEligible()
        {
            var result = AdEligibilityGate.Check("Garage_Legends", AdConsentStatus.Granted);
            Assert.That(result.IsEligible, Is.True);
            Assert.That(result.Reason, Is.EqualTo(string.Empty));
        }

        [Test]
        public void ConsentNotRequired_ValidGame_IsEligible()
        {
            var result = AdEligibilityGate.Check("Dino_Islands", AdConsentStatus.NotRequired);
            Assert.That(result.IsEligible, Is.True);
            Assert.That(result.Reason, Is.EqualTo(string.Empty));
        }

        [Test]
        public void CheckWithProfile_AdsNotApproved_IsIneligible()
        {
            // Wonder_Pop has AdsEnabled=false from catalog
            var profile = GameBusinessProfileCatalog.Require("Wonder_Pop");
            Assert.That(profile.AdsEnabled, Is.False, "Precondition: Wonder_Pop must have AdsEnabled=false");
            var result = AdEligibilityGate.CheckWithProfile(profile, AdConsentStatus.Granted);
            Assert.That(result.IsEligible, Is.False);
            Assert.That(result.Reason, Is.EqualTo("AdsNotApproved"));
        }

        [Test]
        public void CheckWithProfile_MonetizationDisabled_IsIneligible()
        {
            // Wonder_Pop has MonetizationEnabled=false from catalog
            var profile = GameBusinessProfileCatalog.Require("Wonder_Pop");
            Assert.That(profile.MonetizationEnabled, Is.False, "Precondition: Wonder_Pop must have MonetizationEnabled=false");
            var result = AdEligibilityGate.CheckWithProfile(profile, AdConsentStatus.NotRequired);
            Assert.That(result.IsEligible, Is.False);
        }

        [Test]
        public void CheckWithProfile_WonderPop_IsIneligible_ViaCheckWithProfile()
        {
            var profile = GameBusinessProfileCatalog.Require("Wonder_Pop");
            var result = AdEligibilityGate.CheckWithProfile(profile, AdConsentStatus.Granted);
            Assert.That(result.IsEligible, Is.False);
        }

        [Test]
        public void CheckWithProfile_ApprovedGame_ConsentGranted_IsEligible()
        {
            var profile = GameBusinessProfileCatalog.Require("Coral_Rescue");
            Assert.That(profile.AdsEnabled, Is.True, "Precondition: Coral_Rescue must have AdsEnabled=true");
            Assert.That(profile.MonetizationEnabled, Is.True, "Precondition: Coral_Rescue must have MonetizationEnabled=true");
            var result = AdEligibilityGate.CheckWithProfile(profile, AdConsentStatus.Granted);
            Assert.That(result.IsEligible, Is.True);
        }

        [Test]
        public void Ineligible_NullReason_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => AdEligibilityResult.Ineligible(null));
        }
    }
}
