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
    }
}
