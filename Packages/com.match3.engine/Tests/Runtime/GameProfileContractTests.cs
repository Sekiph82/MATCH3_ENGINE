using System;
using System.Collections.Generic;
using System.Linq;
using Match3.Engine.Runtime;
using NUnit.Framework;

namespace Match3.Engine.Tests
{
    public sealed class GameProfileContractTests
    {
        private static readonly EngineVersion V1 = new EngineVersion(1, 0, 0);

        [Test]
        public void AllFiveBrandNeutralProfilesAreUniqueAndOptionalFeaturesDefaultOff()
        {
            var profiles = GameProfileCatalog.LoadPlaceholders();
            Assert.That(profiles.Count, Is.EqualTo(5));
            Assert.That(profiles.Select(x => x.Id).Distinct().Count(), Is.EqualTo(5));
            CollectionAssert.AreEquivalent(new[] {
                "Coral_Rescue", "Garage_Legends", "Dino_Islands",
                "The_Fractured_Realms", "Wonder_Pop"
            }, profiles.Select(x => x.Id).ToArray());
            foreach (var profile in profiles)
            {
                Assert.That(GameProfileCatalog.RequireProfile(profiles, profile.Id), Is.SameAs(profile));
                Assert.That(profile.ThemeBindingKey, Does.StartWith("placeholder."));
                Assert.That(profile.AgeRule, Is.EqualTo(ProfileAgeRule.General));
                Assert.That(profile.ContentChannel, Is.EqualTo(ProfileContentChannel.Development));
                Assert.That(profile.EnabledFeatures, Is.Empty);
                Assert.That(profile.SchemaVersion, Is.EqualTo(1));
            }
        }

        [Test]
        public void FeatureCatalogContainsAllThirteenTypedOptionalCapabilities()
        {
            Assert.That(FeatureCatalog.SupportedIds.Count, Is.EqualTo(13));
            Assert.That(FeatureCatalog.SupportedIds.Distinct().Count(), Is.EqualTo(13));
            foreach (var id in FeatureCatalog.SupportedIds)
                Assert.That(FeatureCatalog.IsKnown(id), Is.True);
            Assert.That(FeatureCatalog.IsKnown("free_money"), Is.False);
            Assert.Throws<ArgumentException>(() => new FeatureFlag("free_money", V1));
            Assert.Throws<NotSupportedException>(() =>
                new FeatureFlag("portal", new EngineVersion(2, 0, 0)));
            Assert.Throws<NotSupportedException>(() =>
                new FeatureFlag("portal", new EngineVersion(1, 1, 0)));
        }

        [Test]
        public void KnownProfileMayEnableOnlyItsExplicitCompatibleFeatures()
        {
            var enabled = new[] { new FeatureFlag("collection", new EngineVersion(1, 0, 1)) };
            var profile = new GameProfile("Coral_Rescue", "placeholder.coral", ProfileAgeRule.General,
                ProfileContentChannel.FamilyTest, enabled);
            enabled[0] = new FeatureFlag("daily", V1);
            Assert.That(profile.EnabledFeatures.Select(x => x.Id), Is.EqualTo(new[] { "collection" }));
            Assert.That(profile.IsEnabled("collection", V1), Is.True);
            Assert.That(profile.IsEnabled("daily", V1), Is.False);
            Assert.Throws<ArgumentException>(() => profile.IsEnabled("unknown", V1));
            Assert.Throws<NotSupportedException>(() =>
                profile.IsEnabled("collection", new EngineVersion(1, 1, 0)));
        }

        [Test]
        public void InvalidProfileIdentifiersEnumsAndSchemaFailClosed()
        {
            Assert.Throws<ArgumentException>(() =>
                new GameProfile("unknown_game", "placeholder.key", ProfileAgeRule.General,
                    ProfileContentChannel.Development));
            Assert.Throws<ArgumentException>(() =>
                new GameProfile("Wonder_Pop", " ", ProfileAgeRule.General,
                    ProfileContentChannel.Development));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new GameProfile("Wonder_Pop", "placeholder.test", (ProfileAgeRule)77,
                    ProfileContentChannel.Development));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new GameProfile("Wonder_Pop", "placeholder.test", ProfileAgeRule.General,
                    (ProfileContentChannel)77));
            Assert.Throws<NotSupportedException>(() =>
                new GameProfile("Wonder_Pop", "placeholder.test", ProfileAgeRule.General,
                    ProfileContentChannel.Release, schemaVersion: 2));
        }

        [Test]
        public void DuplicateAndConflictingFeatureFlagsAreRejected()
        {
            Assert.Throws<ArgumentException>(() => new GameProfile("Garage_Legends", "placeholder.garage",
                ProfileAgeRule.General, ProfileContentChannel.Development,
                new[] { new FeatureFlag("egg", V1), new FeatureFlag("egg", V1) }));
            Assert.Throws<ArgumentException>(() => new GameProfile("Garage_Legends", "placeholder.garage",
                ProfileAgeRule.General, ProfileContentChannel.Development,
                new[] { new FeatureFlag("timed", V1), new FeatureFlag("endless", V1) }));
            Assert.Throws<ArgumentException>(() => new GameProfile("Garage_Legends", "placeholder.garage",
                ProfileAgeRule.General, ProfileContentChannel.Development,
                new[] { default(FeatureFlag) }));
        }

        [Test]
        public void ProfileCatalogRejectsUnknownMissingAndDuplicateProfiles()
        {
            var original = GameProfileCatalog.LoadPlaceholders();
            Assert.Throws<ArgumentException>(() => GameProfileCatalog.RequireProfile(original, "unknown"));
            var duplicate = new List<GameProfile>(original) { original[0] };
            Assert.Throws<InvalidOperationException>(() =>
                GameProfileCatalog.RequireProfile(duplicate, "Coral_Rescue"));
            Assert.Throws<InvalidOperationException>(() =>
                GameProfileCatalog.RequireProfile(new List<GameProfile>(), "Coral_Rescue"));
            Assert.Throws<ArgumentNullException>(() =>
                GameProfileCatalog.RequireProfile(null, "Coral_Rescue"));
        }

        [Test]
        public void VersionRulesDoNotAcceptIncompatibleMajorOrNegativeSegments()
        {
            Assert.That(new EngineVersion(1, 0, 4).IsCompatibleWith(V1), Is.True);
            Assert.That(new EngineVersion(1, 0, 0).IsCompatibleWith(new EngineVersion(1, 0, 1)), Is.False);
            Assert.That(new EngineVersion(2, 0, 0).IsCompatibleWith(V1), Is.False);
            Assert.Throws<ArgumentOutOfRangeException>(() => new EngineVersion(-1, 0, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new EngineVersion(1, -1, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new EngineVersion(1, 0, -1));
            Assert.Throws<NotSupportedException>(() => EngineCompatibility.RequireSupportedSchema(0));
        }
    }
}
