using System;
using System.Collections.Generic;
using System.Linq;
using Match3.Engine.Runtime;
using NUnit.Framework;

namespace Match3.Engine.Tests
{
    public sealed class TutorialAccessContractTests
    {
        private static GameProfile Profile(string id) =>
            new GameProfile(id, "neutral." + id.ToLowerInvariant(),
                ProfileAgeRule.General, ProfileContentChannel.Development);

        [Test]
        public void M10_003_SixShapesAreIdentifiableWithoutColor()
        {
            var catalog = AccessibleSymbolCatalog.SixNeutralSymbols();
            Assert.That(catalog.Symbols.Count, Is.EqualTo(6));
            Assert.That(catalog.Symbols.Select(x => x.ShapeKey).Distinct().Count(),
                Is.EqualTo(6));
            Assert.That(catalog.Symbols.Select(x => x.ScreenReaderKey).Distinct().Count(),
                Is.EqualTo(6));
            var duplicateShape = Enumerable.Range(1, 6).Select(i =>
                new AccessibleSymbol("piece." + i, "shape.common",
                    "pattern." + i, "voice." + i));
            Assert.Throws<ArgumentException>(() =>
                new AccessibleSymbolCatalog(duplicateShape));
            Assert.Throws<ArgumentException>(() =>
                new AccessibleSymbol("p", "", "patt", "voice"));
            Assert.Throws<ArgumentException>(() =>
                new AccessibleSymbolCatalog(catalog.Symbols.Take(5)));
        }

        [Test]
        public void M10_004_WonderChildRestrictionsCannotBeOverriddenByCaller()
        {
            var wonder = Profile("Wonder_Pop");
            foreach (GuardedUiFeature feature in new[] {
                GuardedUiFeature.ExternalLink, GuardedUiFeature.RewardedAd,
                GuardedUiFeature.Purchase, GuardedUiFeature.Friends,
                GuardedUiFeature.Leaderboard, GuardedUiFeature.Tournament })
            {
                var denied = SafeInteractionPolicy.Evaluate(
                    wonder, feature, adultVerified: true,
                    consentGranted: true, featureEnabled: true);
                Assert.That(denied.Enabled, Is.False);
                Assert.That(denied.DisabledReasonKey, Is.EqualTo(
                    "ui.disabled.child_safety"));
            }
            Assert.That(SafeInteractionPolicy.Evaluate(
                wonder, GuardedUiFeature.ContinueWithoutAd).Enabled, Is.True);
            Assert.That(SafeInteractionPolicy.Evaluate(
                wonder, GuardedUiFeature.Hint).Enabled, Is.True);
        }

        [Test]
        public void M10_011_InclusionAndDefaultDeniedSocialAcrossProfiles()
        {
            foreach (var game in GameProfileCatalog.Ids)
            {
                var profile = Profile(game);
                Assert.That(SafeInteractionPolicy.Evaluate(
                    profile, GuardedUiFeature.Tutorial).Enabled, Is.True);
                Assert.That(SafeInteractionPolicy.Evaluate(
                    profile, GuardedUiFeature.ContinueWithoutAd).Enabled, Is.True);
                var denied = SafeInteractionPolicy.Evaluate(
                    profile, GuardedUiFeature.Friends);
                Assert.That(denied.Enabled, Is.False);
                Assert.That(denied.DisabledReasonKey,
                    Is.EqualTo(game == "Wonder_Pop" ?
                        "ui.disabled.child_safety" :
                        "ui.disabled.age_unverified"));
            }
            var adult = Profile("Garage_Legends");
            Assert.That(SafeInteractionPolicy.Evaluate(adult,
                GuardedUiFeature.Purchase, true, false, true).Enabled, Is.False);
            Assert.That(SafeInteractionPolicy.Evaluate(adult,
                GuardedUiFeature.Purchase, true, true, false).Enabled, Is.False);
            Assert.That(SafeInteractionPolicy.Evaluate(adult,
                GuardedUiFeature.Purchase, true, true, true).Enabled, Is.True);
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                SafeInteractionPolicy.Evaluate(adult, (GuardedUiFeature)99));
        }

        [Test]
        public void M10_005_LocalizationIsEnglishTurkishCompleteAndBounded()
        {
            var dictionary = NeutralLocalizedText.EnglishTurkishFtue();
            var catalog = FtueCatalog.ThreeLevel("Coral_Rescue");
            foreach (var step in catalog.Steps)
            {
                Assert.That(dictionary.Resolve(step.LocalizationKey, "en"),
                    Does.Not.Contain("[missing:"));
                Assert.That(dictionary.Resolve(step.LocalizationKey, "tr"),
                    Does.Not.Contain("[missing:"));
            }
            Assert.That(dictionary.Resolve("unbound.key", "en"),
                Is.EqualTo("[missing:unbound.key]"));
            Assert.Throws<ArgumentException>(() =>
                dictionary.Resolve("ftue.swap", "fr"));
            Assert.Throws<ArgumentException>(() =>
                new NeutralLocalizedText(
                    new [] { new KeyValuePair<string, string>("only.en", "English") },
                    new [] { new KeyValuePair<string, string>("only.tr", "Türkçe") }));
            Assert.Throws<ArgumentException>(() =>
                dictionary.Resolve("ftue.swap", "en", 1, 2, 3, 4, 5));
        }

        [Test]
        public void M10_005_TextFitFlagsLongContentWithoutDroppingIt()
        {
            var normal = NeutralTextFit.Check(
                "Goals", lineCharacters: 20, maximumLines: 1);
            Assert.That(normal.Fits, Is.True);
            var longText = new string('W', 61);
            var over = NeutralTextFit.Check(
                longText, lineCharacters: 20, maximumLines: 3);
            Assert.That(over.Fits, Is.False);
            Assert.That(over.CharacterCount, Is.EqualTo(61));
            Assert.That(over.WrapRecommended, Is.True);
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                NeutralTextFit.Check("A", 0, 1));
        }
    }
}
