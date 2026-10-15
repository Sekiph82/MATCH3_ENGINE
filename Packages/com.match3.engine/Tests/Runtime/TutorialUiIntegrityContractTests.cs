using System;
using System.Collections.Generic;
using System.Linq;
using Match3.Engine.Runtime;
using NUnit.Framework;

namespace Match3.Engine.Tests
{
    public sealed class TutorialUiIntegrityContractTests
    {
        private static UiSurfaceState[] EverySurface(
            bool addUnregisteredCommand = false, bool omitLastRoute = false)
        {
            var all = Enum.GetValues(typeof(UiSurfaceKind))
                .Cast<UiSurfaceKind>().ToArray();
            var actions = all.Where(s => s != UiSurfaceKind.Home)
                .Where(s => !omitLastRoute || s != UiSurfaceKind.Consent)
                .Select(s => new UiActionDefinition(
                    "open." + s, UiSurfaceKind.Home, UiActionKind.Navigate,
                    true, true, destination: s)).ToList();
            if (addUnregisteredCommand)
                actions.Add(new UiActionDefinition(
                    "claim", UiSurfaceKind.Home, UiActionKind.Command,
                    true, true, commandKey: "purchase.unhandled"));
            var result = new List<UiSurfaceState> {
                new UiSurfaceState("home.ready", UiSurfaceKind.Home, actions)
            };
            result.AddRange(all.Where(s => s != UiSurfaceKind.Home).Select(s =>
                new UiSurfaceState("neutral." + s, s,
                    new [] {
                        new UiActionDefinition("back", s,
                            UiActionKind.Back, true, true),
                        new UiActionDefinition("future", s,
                            UiActionKind.Command, true, false,
                            commandKey: "feature.future",
                            disabledReason: "ui.disabled.feature_unavailable")
                    })));
            return result.ToArray();
        }

        [Test]
        public void M10_007_TouchTargetsRespectFiniteBoundsAndBlocking()
        {
            var hit = new NeutralTouchRegion("next", 10, 20, 44, 50, "cta.next");
            Assert.That(NeutralTouchRouter.CanActivate(hit, 10, 20, false, false),
                Is.True);
            Assert.That(NeutralTouchRouter.CanActivate(hit, 53.99, 69.99,
                false, false), Is.True);
            Assert.That(NeutralTouchRouter.CanActivate(hit, 54, 25,
                false, false), Is.False);
            Assert.That(NeutralTouchRouter.CanActivate(hit, 20, 70,
                false, false), Is.False);
            Assert.That(NeutralTouchRouter.CanActivate(hit, double.NaN, 25,
                false, false), Is.False);
            Assert.That(NeutralTouchRouter.CanActivate(hit, 15, 25,
                true, false), Is.False);
            Assert.That(NeutralTouchRouter.CanActivate(hit, 15, 25,
                false, true), Is.False);
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new NeutralTouchRegion("tiny", 0, 0, 30, 44, "label"));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new NeutralTouchRegion("nan", 0, 0, double.NaN, 44, "label"));
        }

        [Test]
        public void M10_008_NeutralHudIsReadableWithoutAnimationOrAudio()
        {
            var full = new NeutralHudReadability(
                "Goals: 8", 8, 17, 16, 2, false, false);
            var reduced = new NeutralHudReadability(
                "Goals: 8", 8, 17, 16, 2, true, true);
            Assert.That(full.GoalRemaining, Is.EqualTo(reduced.GoalRemaining));
            Assert.That(full.MovesRemaining, Is.EqualTo(reduced.MovesRemaining));
            Assert.That(reduced.ReducedMotion, Is.True);
            Assert.That(reduced.AudioMuted, Is.True);
            Assert.That(reduced.LabelFit.Fits, Is.True);
            Assert.Throws<InvalidOperationException>(() =>
                new NeutralHudReadability(
                    new string('Z', 100), 1, 1, 10, 1, false, false));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new NeutralHudReadability("goals", -1, 2, 10, 1, false, false));
        }

        [Test]
        public void M10_012_SeventeenSurfaceGraphRejectsUnreachableDeadActions()
        {
            foreach (var game in GameProfileCatalog.Ids)
            {
                Assert.That(game, Is.Not.Empty);
                var report = UiGraphAuditor.Validate(
                    EverySurface(), new string[0], true, true);
                Assert.That(report.Reachable.Count, Is.EqualTo(17));
                Assert.That(report.EnabledActionCount, Is.GreaterThan(17));
                Assert.That(report.DisabledActionCount, Is.EqualTo(16));
            }
            Assert.Throws<InvalidOperationException>(() =>
                UiGraphAuditor.Validate(
                    EverySurface(addUnregisteredCommand: true),
                    new string[0], true, true));
            Assert.Throws<InvalidOperationException>(() =>
                UiGraphAuditor.Validate(
                    EverySurface(omitLastRoute: true),
                    new string[0], true, true));
            Assert.Throws<InvalidOperationException>(() =>
                UiGraphAuditor.Validate(
                    EverySurface(), new string[0], false, true));
            Assert.Throws<ArgumentException>(() =>
                UiGraphAuditor.Validate(
                    EverySurface().Take(16), new string[0], true, true));
        }

        [Test]
        public void M10_013_NoFakeClaimsOrUrgencyFromUnverifiedRewardStates()
        {
            var locked = HonestRewardProjection.Project(false, false, 100);
            Assert.That(locked.State, Is.EqualTo(RewardPanelState.Locked));
            Assert.That(locked.CanClaim, Is.False);
            Assert.That(locked.ShowCountdown, Is.False);
            var earned = HonestRewardProjection.Project(true, false, 100);
            Assert.That(earned.CanClaim, Is.True);
            Assert.That(earned.ShowCountdown, Is.False,
                "No verified expiry means no countdown and no urgency.");
            var timed = HonestRewardProjection.Project(true, false, 100, 140);
            Assert.That(timed.RemainingSeconds, Is.EqualTo(40));
            Assert.That(timed.ShowCountdown, Is.True);
            var expired = HonestRewardProjection.Project(true, false, 140, 140);
            Assert.That(expired.State, Is.EqualTo(RewardPanelState.Expired));
            Assert.That(expired.CanClaim, Is.False);
            var claimed = HonestRewardProjection.Project(true, true, 140, 150);
            Assert.That(claimed.State, Is.EqualTo(RewardPanelState.Claimed));
            Assert.That(claimed.ShowCountdown, Is.False);
            Assert.Throws<InvalidOperationException>(() =>
                HonestRewardProjection.Project(false, true, 100));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                HonestRewardProjection.Project(true, false, -1));
        }

        [Test]
        public void M10_013_AllEngagementViewsRequireVerifiedLedgerAndNeverInventOffers()
        {
            foreach (var gameId in GameProfileCatalog.Ids)
            {
                var profile = new GameProfile(gameId, "neutral." + gameId,
                    ProfileAgeRule.General, ProfileContentChannel.Development,
                    new[] { new FeatureFlag("catch_up", new EngineVersion(1, 0, 0)) });
                foreach (EngagementKind kind in Enum.GetValues(typeof(EngagementKind)))
                {
                    var fact = new VerifiedEngagementFact(gameId,
                        "real.source." + kind, kind, true, false, 3, 7);
                    var view = HonestEngagementProjector.Project(profile,
                        fact, 100, sourceIsAuthoritativelyVerified: true);
                    Assert.That(view.Kind, Is.EqualTo(kind));
                    Assert.That(view.Progress, Is.EqualTo(3));
                    Assert.That(view.Target, Is.EqualTo(7));
                    Assert.That(view.State, Is.EqualTo(RewardPanelState.Available));
                    Assert.That(view.ShowCountdown, Is.False);
                    Assert.That(view.CanClaim, Is.EqualTo(
                        kind == EngagementKind.Gift ||
                        kind == EngagementKind.Collection ||
                        kind == EngagementKind.CatchUp));
                    Assert.Throws<InvalidOperationException>(() =>
                        HonestEngagementProjector.Project(profile,
                            fact, 100, sourceIsAuthoritativelyVerified: false));
                }
                Assert.Throws<InvalidOperationException>(() =>
                    HonestEngagementProjector.Project(
                        profile,
                        new VerifiedEngagementFact(
                            gameId == "Wonder_Pop" ? "Coral_Rescue" : "Wonder_Pop",
                            "foreign.receipt", EngagementKind.Journey,
                            true, false, 1, 3),
                        100, true));
            }
            var general = new GameProfile("Coral_Rescue", "neutral",
                ProfileAgeRule.General, ProfileContentChannel.Development);
            var catchup = new VerifiedEngagementFact("Coral_Rescue",
                "catchup.source", EngagementKind.CatchUp, true, false,
                1, 1, verifiedExpiryUnixSeconds: 200);
            Assert.Throws<InvalidOperationException>(() =>
                HonestEngagementProjector.Project(general, catchup, 100, true));
            var earned = new VerifiedEngagementFact("Coral_Rescue",
                "weekly.source", EngagementKind.WeeklyRecord, true, false,
                4, 5, verifiedExpiryUnixSeconds: 125);
            var projection = HonestEngagementProjector.Project(
                general, earned, 100, true);
            Assert.That(projection.ShowCountdown, Is.True);
            Assert.That(projection.RemainingSeconds, Is.EqualTo(25));
            Assert.That(projection.CanClaim, Is.False);
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new VerifiedEngagementFact("Coral_Rescue", "x",
                    EngagementKind.Gift, true, false, 7, 6));
            Assert.Throws<InvalidOperationException>(() =>
                new VerifiedEngagementFact("Coral_Rescue", "x",
                    EngagementKind.Gift, false, true, 0, 1));
        }

        [Test]
        public void M10_012_DuplicateUiActivationCannotReopenOrRepurchaseAcrossProfiles()
        {
            foreach (var id in GameProfileCatalog.Ids)
            {
                var home = new UiSurfaceState("home.ready." + id,
                    UiSurfaceKind.Home, new[] {
                        new UiActionDefinition("goto.shop",
                            UiSurfaceKind.Home, UiActionKind.Navigate,
                            true, true, destination: UiSurfaceKind.Shop)
                    });
                var shop = new UiSurfaceState("shop.ready." + id,
                    UiSurfaceKind.Shop, new[] {
                        new UiActionDefinition("buy.once",
                            UiSurfaceKind.Shop, UiActionKind.Command,
                            true, true, commandKey: "purchase.intent"),
                        new UiActionDefinition("back",
                            UiSurfaceKind.Shop, UiActionKind.Back, true, true)
                    });
                var first = UiRouter.Activate(
                    new UiRouteSnapshot(UiSurfaceKind.Home), home,
                    new UiActivation("enter." + id, home.StateId,
                        UiSurfaceKind.Home, "goto.shop"));
                Assert.That(first.Applied, Is.True);
                Assert.That(first.Result.CurrentSurface, Is.EqualTo(UiSurfaceKind.Shop));
                var replayEnter = UiRouter.Activate(first.Result, home,
                    new UiActivation("enter." + id, home.StateId,
                        UiSurfaceKind.Home, "goto.shop"));
                Assert.That(replayEnter.Applied, Is.False);
                var purchase = UiRouter.Activate(first.Result, shop,
                    new UiActivation("purchase." + id, shop.StateId,
                        UiSurfaceKind.Shop, "buy.once"));
                Assert.That(purchase.Applied, Is.True);
                Assert.That(purchase.CommandKey, Is.EqualTo("purchase.intent"));
                var replay = UiRouter.Activate(purchase.Result, shop,
                    new UiActivation("purchase." + id, shop.StateId,
                        UiSurfaceKind.Shop, "buy.once"));
                Assert.That(replay.Applied, Is.False);
                Assert.Throws<InvalidOperationException>(() =>
                    UiRouter.Activate(purchase.Result, shop,
                        new UiActivation("purchase." + id, shop.StateId,
                            UiSurfaceKind.Shop, "back")));
                Assert.Throws<InvalidOperationException>(() =>
                    UiRouter.Activate(purchase.Result, home,
                        new UiActivation("stale." + id, home.StateId,
                            UiSurfaceKind.Home, "goto.shop")));
            }
        }
    }
}
