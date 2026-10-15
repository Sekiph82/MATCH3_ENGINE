using System;
using System.Linq;
using Match3.Engine.Runtime;
using NUnit.Framework;

namespace Match3.Engine.Tests
{
    public sealed class TutorialCeremonyAndFocusTests
    {
        [Test]
        public void M10_010_FirstClaimAndUnlockCeremoniesFireExactlyOnce()
        {
            foreach (var gameId in GameProfileCatalog.Ids)
            {
                var empty = new NeutralCeremonySnapshot(gameId);
                var first = new NeutralCeremonyReceipt(
                    "grant.receipt.01", "first.claim", NeutralCeremonyKind.FirstClaim, 1);
                var premature = NeutralCeremonyPlanner.Apply(
                    empty, first, gameId, false);
                Assert.That(premature.Applied, Is.False);
                var applied = NeutralCeremonyPlanner.Apply(
                    empty, first, gameId, true);
                Assert.That(applied.Applied, Is.True);
                var reloaded = new NeutralCeremonySnapshot(
                    gameId, applied.Result.Receipts);
                Assert.That(NeutralCeremonyPlanner.Apply(
                    reloaded, first, gameId, true).Applied, Is.False);
                Assert.That(NeutralCeremonyPlanner.Apply(
                    reloaded, new NeutralCeremonyReceipt(
                        "grant.receipt.02", "first.claim",
                        NeutralCeremonyKind.FirstClaim, 1),
                    gameId, true).Applied, Is.False,
                    "Same earned source cannot play twice using a different event ID.");
                Assert.Throws<InvalidOperationException>(() =>
                    NeutralCeremonyPlanner.Apply(reloaded,
                        new NeutralCeremonyReceipt(
                            "grant.receipt.01", "a.different.claim",
                            NeutralCeremonyKind.FirstClaim, 1),
                        gameId, true));
                Assert.Throws<InvalidOperationException>(() =>
                    NeutralCeremonyPlanner.Apply(reloaded, first,
                        gameId == "Wonder_Pop" ? "Coral_Rescue" : "Wonder_Pop", true));
            }
        }

        [Test]
        public void M10_010_VersionedRevealsAndUnknownReceiptsFailSafely()
        {
            var empty = new NeutralCeremonySnapshot("Coral_Rescue");
            var rev1 = NeutralCeremonyPlanner.Apply(empty,
                new NeutralCeremonyReceipt(
                    "v1", "world.reveal", NeutralCeremonyKind.WorldReveal, 1),
                "Coral_Rescue", true);
            var rev2 = NeutralCeremonyPlanner.Apply(rev1.Result,
                new NeutralCeremonyReceipt(
                    "v2", "world.reveal", NeutralCeremonyKind.WorldReveal, 2),
                "Coral_Rescue", true);
            Assert.That(rev2.Applied, Is.True);
            Assert.That(rev2.Result.Receipts.Count, Is.EqualTo(2));
            Assert.Throws<ArgumentException>(() =>
                new NeutralCeremonySnapshot("Coral_Rescue", new[] {
                    new NeutralCeremonyReceipt("first", "same", NeutralCeremonyKind.FeatureIntroduction, 1),
                    new NeutralCeremonyReceipt("second", "same", NeutralCeremonyKind.FeatureIntroduction, 1)
                }));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new NeutralCeremonyReceipt("x", "y", (NeutralCeremonyKind)100, 1));
        }

        [Test]
        public void M10_011_ScreenReaderFocusOrderHasLabelsAndReasonedDisabledStates()
        {
            var path = new NeutralFocusOrder(new[] {
                new NeutralFocusControl("cta.back", "label.back", 1, true),
                new NeutralFocusControl("cta.claim", "label.claim", 3, false,
                    "ui.disabled.feature_unavailable"),
                new NeutralFocusControl("cta.help", "label.help", 2, true)
            });
            Assert.That(path.Controls.Select(x => x.ActionId),
                Is.EqualTo(new[] { "cta.back", "cta.help", "cta.claim" }));
            Assert.Throws<ArgumentException>(() =>
                new NeutralFocusOrder(new[] {
                    new NeutralFocusControl("a", "one", 1, true),
                    new NeutralFocusControl("b", "two", 1, true)
                }));
            Assert.Throws<ArgumentException>(() =>
                new NeutralFocusControl("cta", "", 1, true));
            Assert.Throws<ArgumentException>(() =>
                new NeutralFocusControl("cta", "label", 1, false));
        }

        [Test]
        public void M10_011_SafeHintsAndChildNoLossDoNotRequireAds()
        {
            foreach (var id in GameProfileCatalog.Ids)
            {
                var p = new GameProfile(id, "neutral." + id,
                    ProfileAgeRule.General, ProfileContentChannel.Development);
                var available = NeutralAssistancePolicy.Evaluate(
                    p, sdkHasLegalMove: true,
                    authoritativeNoLossEligible: true,
                    reducedEffects: true,
                    modalOpen: false, gameReady: true);
                Assert.That(available.HintAvailable, Is.True);
                Assert.That(available.ReducedEffects, Is.True);
                Assert.That(available.RequiresRewardedAdvertisement, Is.False);
                Assert.That(available.NoLossAlternativeAvailable,
                    Is.EqualTo(id == "Wonder_Pop"));
                var suppressed = NeutralAssistancePolicy.Evaluate(
                    p, true, false, false, true, true);
                Assert.That(suppressed.HintAvailable, Is.False);
            }
        }
    }
}
