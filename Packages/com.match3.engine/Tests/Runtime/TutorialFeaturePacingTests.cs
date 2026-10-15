using System;
using System.Linq;
using Match3.Engine.Runtime;
using NUnit.Framework;

namespace Match3.Engine.Tests
{
    public sealed class TutorialFeaturePacingTests
    {
        private static readonly EngineVersion V1 = new EngineVersion(1, 0, 0);

        private static FeatureIntroductionCatalog Definitions() =>
            new FeatureIntroductionCatalog(new[] {
                new FeatureIntroduction("collection", 2, "ftue.unlock.collection"),
                new FeatureIntroduction("daily", 3, "ftue.unlock.daily"),
                new FeatureIntroduction("weekly", 8, "ftue.unlock.weekly")
            });

        private static GameProfile Enabled(string id) =>
            new GameProfile(id, "neutral." + id,
                ProfileAgeRule.General,
                ProfileContentChannel.Development,
                new[] { new FeatureFlag("collection", V1),
                    new FeatureFlag("daily", V1) });

        [Test]
        public void M10_009_IntroductionRequiresApprovedCapabilityAndEarnedLevel()
        {
            var defs = Definitions();
            foreach (var gameId in GameProfileCatalog.Ids)
            {
                var profile = Enabled(gameId);
                var empty = new FeatureIntroductionSnapshot(gameId);
                var premature = FeatureIntroductionPlanner.Apply(
                    defs, empty, profile, "collection", "too-early", 1, true);
                Assert.That(premature.Applied, Is.False);
                var blocked = FeatureIntroductionPlanner.Apply(
                    defs, empty, profile, "collection", "modal", 2, false);
                Assert.That(blocked.Applied, Is.False);
                var granted = FeatureIntroductionPlanner.Apply(
                    defs, empty, profile, "collection", "unlocked", 2, true);
                Assert.That(granted.Applied, Is.True);
                Assert.That(granted.Visible, Is.True);
                Assert.That(granted.Result.HasIntroduced("collection"), Is.True);
                var replay = FeatureIntroductionPlanner.Apply(
                    defs, granted.Result, profile, "collection", "unlocked", 2, true);
                Assert.That(replay.Applied, Is.False);
                var duplicate = FeatureIntroductionPlanner.Apply(
                    defs, granted.Result, profile, "collection", "another", 9, true);
                Assert.That(duplicate.Applied, Is.False);
                Assert.That(duplicate.Result.Receipts.Count, Is.EqualTo(1));
                var notEnabled = FeatureIntroductionPlanner.Apply(
                    defs, granted.Result, profile, "weekly", "weekly", 20, true);
                Assert.That(notEnabled.Applied, Is.False);
                Assert.That(notEnabled.Visible, Is.False);
            }
        }

        [Test]
        public void M10_010_IntroductionsPersistThroughRestartAndCannotCrossGame()
        {
            var p = Enabled("Garage_Legends");
            var previous = new FeatureIntroductionSnapshot(p.Id);
            var granted = FeatureIntroductionPlanner.Apply(
                Definitions(), previous, p, "daily", "once", 3, true);
            var reloaded = new FeatureIntroductionSnapshot(
                p.Id, granted.Result.Receipts);
            Assert.That(FeatureIntroductionPlanner.IsVisible(
                p, reloaded, "daily"), Is.True);
            Assert.Throws<InvalidOperationException>(() =>
                FeatureIntroductionPlanner.Apply(Definitions(), reloaded,
                    p, "collection", "once", 3, true));
            Assert.Throws<InvalidOperationException>(() =>
                FeatureIntroductionPlanner.Apply(Definitions(), reloaded,
                    Enabled("Wonder_Pop"), "daily", "cross", 3, true));
            Assert.Throws<ArgumentException>(() =>
                new FeatureIntroductionSnapshot(p.Id, new[] {
                    new FeatureIntroductionReceipt("one", "daily"),
                    new FeatureIntroductionReceipt("two", "daily")
                }));
        }

        [Test]
        public void M10_009_FeatureSwitchOffDoesNotEraseEarnedIntroduction()
        {
            var p = Enabled("Dino_Islands");
            var grant = FeatureIntroductionPlanner.Apply(
                Definitions(), new FeatureIntroductionSnapshot(p.Id),
                p, "collection", "intro.collection", 2, true);
            var disabled = new GameProfile(
                p.Id, p.ThemeBindingKey, ProfileAgeRule.General,
                ProfileContentChannel.Development);
            Assert.That(grant.Result.HasIntroduced("collection"), Is.True);
            Assert.That(FeatureIntroductionPlanner.IsVisible(
                disabled, grant.Result, "collection"), Is.False);
            Assert.That(FeatureIntroductionPlanner.IsVisible(
                p, grant.Result, "collection"), Is.True);
            Assert.Throws<ArgumentException>(() =>
                new FeatureIntroductionCatalog(new[] {
                    new FeatureIntroduction("daily", 3, "one"),
                    new FeatureIntroduction("daily", 4, "two")
                }));
        }
    }
}
