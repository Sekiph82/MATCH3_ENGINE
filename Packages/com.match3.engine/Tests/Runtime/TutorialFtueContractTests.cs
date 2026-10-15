using System;
using System.Linq;
using Match3.Engine.Runtime;
using NUnit.Framework;

namespace Match3.Engine.Tests
{
    public sealed class TutorialFtueContractTests
    {
        private const string Profile = "Coral_Rescue";
        private static FtueCatalog Catalog() => FtueCatalog.ThreeLevel(Profile);
        private static FtueSnapshot Fresh() => new FtueSnapshot(Profile, 1);

        private static FtueSnapshot Complete(
            FtueCatalog catalog, FtueSnapshot snapshot,
            string stepId, int level)
        {
            var shown = FtuePlanner.Apply(
                catalog, snapshot, stepId, "show." + stepId,
                FtueReceiptKind.Presented, level);
            Assert.That(shown.Applied, Is.True);
            var completed = FtuePlanner.Apply(
                catalog, shown.Result, stepId, "done." + stepId,
                FtueReceiptKind.Completed, level);
            Assert.That(completed.Applied, Is.True);
            return completed.Result;
        }

        [Test]
        public void M10_001_FtueCatalogRejectsInvalidStatesAndDuplicateStepKeys()
        {
            var one = new FtueStepDefinition(
                "intro", 0, FtueCueKind.Welcome,
                UiSurfaceKind.Home, "ftue.welcome", "cta.play");
            Assert.Throws<ArgumentException>(() =>
                new FtueCatalog(Profile, 1, new[] { one, one }));
            Assert.Throws<ArgumentException>(() =>
                new FtueCatalog(Profile, 1,
                    new[] { new FtueStepDefinition(
                        "swap", 1, FtueCueKind.Swap,
                        UiSurfaceKind.Hud, "ftue.swap", "board.move") }));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new FtueStepDefinition("too-late", 4, FtueCueKind.Goal,
                    UiSurfaceKind.Hud, "f", "g"));
            Assert.Throws<ArgumentException>(() =>
                new FtueStepDefinition("mismatch", 1, FtueCueKind.Swap,
                    UiSurfaceKind.Shop, "f", "g"));
            Assert.Throws<ArgumentException>(() =>
                new FtueStepDefinition("empty", 1, FtueCueKind.Swap,
                    UiSurfaceKind.Hud, "", "g"));
            Assert.Throws<ArgumentException>(() =>
                Catalog().Get("not-a-real-step"));
        }

        [Test]
        public void M10_002_ThreeLevelLessonWindowAndAllProfilesHaveSameNeutralCues()
        {
            foreach (var game in GameProfileCatalog.Ids)
            {
                var catalog = FtueCatalog.ThreeLevel(game);
                Assert.That(catalog.Version, Is.EqualTo(1));
                Assert.That(catalog.GameId, Is.EqualTo(game));
                Assert.That(catalog.Steps.Select(s => s.Level),
                    Is.EqualTo(new[] { 0, 1, 2, 3, 3 }));
                Assert.That(catalog.Steps.Select(s => s.Cue), Is.EqualTo(
                    new[] { FtueCueKind.Welcome, FtueCueKind.Swap,
                        FtueCueKind.Special, FtueCueKind.Combo,
                        FtueCueKind.Goal }));
                Assert.That(catalog.Steps.All(s =>
                    s.LocalizationKey.StartsWith("ftue.") &&
                    !string.IsNullOrEmpty(s.HighlightKey)), Is.True);
            }
            var result = FtuePlanner.Apply(Catalog(), Fresh(), "intro", "late",
                FtueReceiptKind.Presented, 10);
            Assert.That(result.Applied, Is.False,
                "There must be no forced 10-level tutorial.");
        }

        [Test]
        public void M10_009_ProgressIsOrderedAndCannotBeForcedWhenUiUnavailable()
        {
            var catalog = Catalog();
            var first = Fresh();
            var wrongLevel = FtuePlanner.Apply(
                catalog, first, "intro", "wrong-level",
                FtueReceiptKind.Presented, 1);
            Assert.That(wrongLevel.Applied, Is.False);
            var earlySpecial = FtuePlanner.Apply(
                catalog, first, "special", "early",
                FtueReceiptKind.Presented, 2);
            Assert.That(earlySpecial.Applied, Is.False);
            var modal = FtuePlanner.Apply(
                catalog, first, "intro", "blocked",
                FtueReceiptKind.Presented, 0, uiAvailable: false);
            Assert.That(modal.Applied, Is.False);

            var snapshot = Complete(catalog, first, "intro", 0);
            snapshot = Complete(catalog, snapshot, "swap", 1);
            snapshot = Complete(catalog, snapshot, "special", 2);
            snapshot = Complete(catalog, snapshot, "combo", 3);
            snapshot = Complete(catalog, snapshot, "goals", 3);
            Assert.That(snapshot.Completed.Count, Is.EqualTo(5));
            Assert.That(snapshot.Receipts.Count, Is.EqualTo(10));
            Assert.That(FtuePlanner.Apply(
                catalog, snapshot, "swap", "second-show",
                FtueReceiptKind.Presented, 1).Applied, Is.False);
        }

        [Test]
        public void M10_010_ReceiptReplayAndSaveCopyCannotDoubleFire()
        {
            var catalog = Catalog();
            var p = FtuePlanner.Apply(catalog, Fresh(), "intro",
                "event.a", FtueReceiptKind.Presented, 0);
            Assert.That(p.Applied, Is.True);
            var copied = new FtueSnapshot(Profile, 1, p.Result.Receipts);
            var replay = FtuePlanner.Apply(catalog, copied, "intro",
                "event.a", FtueReceiptKind.Presented, 0);
            Assert.That(replay.Applied, Is.False);
            Assert.That(replay.Result.Receipts.Count, Is.EqualTo(1));
            Assert.Throws<InvalidOperationException>(() =>
                FtuePlanner.Apply(catalog, copied, "intro", "event.a",
                    FtueReceiptKind.Completed, 0));
            Assert.That(FtuePlanner.Apply(
                catalog, copied, "intro", "event.b",
                FtueReceiptKind.Presented, 0).Applied, Is.False);
            var completed = FtuePlanner.Apply(
                catalog, copied, "intro", "event.c",
                FtueReceiptKind.Completed, 0);
            Assert.That(completed.Applied, Is.True);
            Assert.That(FtuePlanner.Apply(
                catalog, completed.Result, "intro", "event.c",
                FtueReceiptKind.Completed, 0).Applied, Is.False);
        }

        [Test]
        public void M10_010_RejectCompletionWithoutPresentationAndCrossGameSave()
        {
            var catalog = Catalog();
            var snapshot = Fresh();
            Assert.That(FtuePlanner.Apply(
                catalog, snapshot, "intro", "done-without-show",
                FtueReceiptKind.Completed, 0).Applied, Is.False);
            Assert.Throws<InvalidOperationException>(() =>
                FtuePlanner.Apply(
                    catalog, FtueSnapshotFor("Wonder_Pop"), "intro",
                    "cross-game", FtueReceiptKind.Presented, 0));
            Assert.Throws<InvalidOperationException>(() =>
                FtuePlanner.Apply(
                    catalog, new FtueSnapshot(Profile, 2), "intro",
                    "wrong-version", FtueReceiptKind.Presented, 0));
            Assert.Throws<ArgumentException>(() =>
                new FtueSnapshot(Profile, 1, new[] {
                    new FtueReceipt("a", "intro", FtueReceiptKind.Completed)
                }));
            Assert.Throws<ArgumentException>(() =>
                new FtueSnapshot(Profile, 1, new[] {
                    new FtueReceipt("a", "intro", FtueReceiptKind.Presented),
                    new FtueReceipt("a", "intro", FtueReceiptKind.Completed)
                }));
        }

        private static FtueSnapshot FtueSnapshotFor(string id) =>
            new FtueSnapshot(id, 1);
    }
}
