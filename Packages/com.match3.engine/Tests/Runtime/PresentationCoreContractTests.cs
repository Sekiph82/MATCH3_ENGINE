using System;
using System.Collections.Generic;
using System.Linq;
using Match3.Engine.Runtime;
using NUnit.Framework;

namespace Match3.Engine.Tests
{
    public sealed class PresentationCoreContractTests
    {
        [Test]
        public void M09_001_BoardVisualsAreStablePooledNeutralAndFallbackSafe()
        {
            var topology = new BoardTopology(
                2, 2,
                occupants: new[]
                {
                    new BoardOccupant(new BoardPosition(1, 1), "blue"),
                    new BoardOccupant(new BoardPosition(0, 1), "missing"),
                    new BoardOccupant(new BoardPosition(0, 0), "red")
                });
            var catalog = new PresentationSpriteCatalog(
                new[]
                {
                    new PresentationSpriteBinding("red", "tile.red"),
                    new PresentationSpriteBinding("blue", "tile.blue")
                },
                "tile.placeholder");

            var plan = BoardVisualPlanner.Build(topology, catalog, 4);
            Assert.That(plan.RequiredSlots, Is.EqualTo(3));
            Assert.That(plan.PoolCapacity, Is.EqualTo(4));
            Assert.That(plan.Sprites.Select(x => x.Position.ToString()),
                Is.EqualTo(new[] { "0:0:0", "0:0:1", "0:1:1" }));
            Assert.That(plan.Sprites.Select(x => x.SortOrder),
                Is.EqualTo(new[] { 0, 1, 2 }));
            Assert.That(plan.Sprites[0].SpriteKey, Is.EqualTo("tile.red"));
            Assert.That(plan.Sprites[1].SpriteKey, Is.EqualTo("tile.placeholder"));
            Assert.That(plan.Sprites[1].UsesFallback, Is.True);
            Assert.That(plan.Warnings,
                Is.EqualTo(new[] { "missing-sprite:missing" }));

            Assert.Throws<InvalidOperationException>(() =>
                BoardVisualPlanner.Build(topology, catalog, 2));
            Assert.Throws<ArgumentException>(() =>
                new PresentationSpriteCatalog(
                    new[]
                    {
                        new PresentationSpriteBinding("red", "a"),
                        new PresentationSpriteBinding("red", "b")
                    },
                    "fallback"));
        }

        [Test]
        public void M09_002_PresentationVfxProjectionIsOrderedReplaySafeAndMissingKeyFallsBack()
        {
            var catalog = new PresentationEffectCatalog(
                new[]
                {
                    new PresentationEffectBinding(
                        "clear.normal", "fx.clear")
                },
                "fx.fallback");
            var ledger = new PresentationEventLedger();
            var firstEvent = new PresentationEvent(
                "event-1", 1,
                PresentationEventKind.Clear,
                "clear.normal",
                new[]
                {
                    new BoardPosition(1, 1),
                    new BoardPosition(0, 1)
                },
                PresentationIntensity.Small);

            var first = PresentationEffectPlanner.Project(
                ledger, catalog, firstEvent);
            Assert.That(first.Applied, Is.True);
            Assert.That(first.Cue.EffectKey, Is.EqualTo("fx.clear"));
            Assert.That(first.Cue.UsesFallback, Is.False);
            Assert.That(first.Cue.Positions.Select(x => x.ToString()),
                Is.EqualTo(new[] { "0:0:1", "0:1:1" }));

            var replay = PresentationEffectPlanner.Project(
                first.Result, catalog, firstEvent);
            Assert.That(replay.Applied, Is.False);
            Assert.That(replay.Cue, Is.Null);

            Assert.Throws<InvalidOperationException>(() =>
                PresentationEffectPlanner.Project(
                    first.Result, catalog,
                    new PresentationEvent(
                        "event-1", 1,
                        PresentationEventKind.Combo,
                        "clear.normal",
                        new[] { new BoardPosition(0, 0) })));

            Assert.Throws<InvalidOperationException>(() =>
                PresentationEffectPlanner.Project(
                    first.Result, catalog,
                    new PresentationEvent(
                        "event-gap", 3,
                        PresentationEventKind.Refill,
                        "refill",
                        new[] { new BoardPosition(0, 0) })));

            var fallback = PresentationEffectPlanner.Project(
                first.Result, catalog,
                new PresentationEvent(
                    "event-2", 2,
                    PresentationEventKind.Line,
                    "line.unbound",
                    new[] { new BoardPosition(0, 0) },
                    PresentationIntensity.Medium));
            Assert.That(fallback.Cue.EffectKey, Is.EqualTo("fx.fallback"));
            Assert.That(fallback.Cue.UsesFallback, Is.True);

            Assert.Throws<ArgumentException>(() =>
                new PresentationEvent(
                    "duplicate-cell", 3,
                    PresentationEventKind.Area,
                    "area",
                    new[]
                    {
                        new BoardPosition(0, 0),
                        new BoardPosition(0, 0)
                    }));
        }

        [Test]
        public void M09_003_AnimationArbitrationIsBoundedPriorityStableSkipSafeAndReducedMotionSafe()
        {
            var policy = new AnimationPolicy(
                maximumActive: 2,
                reducedMotionMaximumMilliseconds: 120);
            var snapshot = new AnimationSnapshot();

            var r1 = new AnimationRequest(
                "anim-1", "event-1", 10, 600, true);
            var r2 = new AnimationRequest(
                "anim-2", "event-2", 20, 600, false);
            var r3 = new AnimationRequest(
                "anim-3", "event-3", 30, 600, true);
            var r4 = new AnimationRequest(
                "anim-4", "event-4", 5, 600, true);

            var p1 = AnimationArbitrator.Schedule(snapshot, policy, r1);
            var p2 = AnimationArbitrator.Schedule(p1.Result, policy, r2);
            Assert.That(p2.Result.Active.Keys.OrderBy(x => x),
                Is.EqualTo(new[] { "anim-1", "anim-2" }));

            var p3 = AnimationArbitrator.Schedule(p2.Result, policy, r3);
            Assert.That(p3.CancelledRequestIds,
                Is.EqualTo(new[] { "anim-1" }));
            Assert.That(p3.Result.Active.Keys.OrderBy(x => x),
                Is.EqualTo(new[] { "anim-2", "anim-3" }));

            var p4 = AnimationArbitrator.Schedule(p3.Result, policy, r4);
            Assert.That(p4.DroppedToTerminalState, Is.True);
            Assert.That(p4.Result.Active.Keys.OrderBy(x => x),
                Is.EqualTo(new[] { "anim-2", "anim-3" }));

            var replay = AnimationArbitrator.Schedule(
                p4.Result, policy, r4);
            Assert.That(replay.Applied, Is.False);

            Assert.Throws<InvalidOperationException>(() =>
                AnimationArbitrator.Schedule(
                    p4.Result, policy,
                    new AnimationRequest(
                        "anim-4", "different-event", 5, 600, true)));

            var skipped = AnimationArbitrator.SkipSkippable(p4.Result);
            Assert.That(skipped.CancelledRequestIds,
                Is.EqualTo(new[] { "anim-3" }));
            Assert.That(skipped.Result.Active.Keys,
                Is.EqualTo(new[] { "anim-2" }));

            var completed = AnimationArbitrator.Complete(
                skipped.Result, "anim-2");
            Assert.That(completed.Result.Active, Is.Empty);

            var reduced = AnimationArbitrator.Schedule(
                new AnimationSnapshot(reducedMotion: true),
                policy,
                new AnimationRequest(
                    "reduced", "event-r", 100, 1000, true));
            Assert.That(reduced.CompletedImmediately, Is.True);
            Assert.That(reduced.Result.Active, Is.Empty);

            Assert.Throws<ArgumentException>(() =>
                new AnimationSnapshot(
                    active: new[]
                    {
                        new AnimationRequest(
                            "orphan", "event-x", 1, 500, true)
                    }));
        }

        [Test]
        public void M09_004_UiRoutingIsTypedAccessibleAndActivationReplaySafe()
        {
            var home = new UiSurfaceState(
                "home-state-1",
                UiSurfaceKind.Home,
                new[]
                {
                    new UiActionDefinition(
                        "play", UiSurfaceKind.Home,
                        UiActionKind.Navigate,
                        visible: true, enabled: true,
                        destination: UiSurfaceKind.WorldMap),
                    new UiActionDefinition(
                        "settings", UiSurfaceKind.Home,
                        UiActionKind.Command,
                        visible: true, enabled: true,
                        commandKey: "open.settings"),
                    new UiActionDefinition(
                        "locked-shop", UiSurfaceKind.Home,
                        UiActionKind.Command,
                        visible: true, enabled: false,
                        commandKey: "open.shop",
                        disabledReason: "shop.locked"),
                    new UiActionDefinition(
                        "hidden-debug", UiSurfaceKind.Home,
                        UiActionKind.Command,
                        visible: false, enabled: false,
                        commandKey: "debug")
                });

            var route = new UiRouteSnapshot(UiSurfaceKind.Home);
            var activation = new UiActivation(
                "tap-1", "home-state-1",
                UiSurfaceKind.Home, "play");
            var first = UiRouter.Activate(route, home, activation);
            Assert.That(first.Applied, Is.True);
            Assert.That(first.Result.CurrentSurface,
                Is.EqualTo(UiSurfaceKind.WorldMap));
            Assert.That(first.Destination,
                Is.EqualTo(UiSurfaceKind.WorldMap));

            var replay = UiRouter.Activate(
                first.Result, home, activation);
            Assert.That(replay.Applied, Is.False);
            Assert.That(replay.Result.CurrentSurface,
                Is.EqualTo(UiSurfaceKind.WorldMap));

            Assert.Throws<InvalidOperationException>(() =>
                UiRouter.Activate(
                    new UiRouteSnapshot(UiSurfaceKind.Home),
                    home,
                    new UiActivation(
                        "disabled-tap", "home-state-1",
                        UiSurfaceKind.Home, "locked-shop")));

            Assert.Throws<InvalidOperationException>(() =>
                UiRouter.Activate(
                    new UiRouteSnapshot(UiSurfaceKind.Home),
                    home,
                    new UiActivation(
                        "hidden-tap", "home-state-1",
                        UiSurfaceKind.Home, "hidden-debug")));

            Assert.Throws<InvalidOperationException>(() =>
                UiRouter.Activate(
                    first.Result, home,
                    new UiActivation(
                        "tap-1", "home-state-1",
                        UiSurfaceKind.Home, "settings")));

            Assert.Throws<ArgumentException>(() =>
                new UiActionDefinition(
                    "bad-disabled", UiSurfaceKind.Home,
                    UiActionKind.Command,
                    visible: true, enabled: false,
                    commandKey: "x"));

            Assert.Throws<ArgumentException>(() =>
                new UiSurfaceState(
                    "bad-surface", UiSurfaceKind.Home,
                    new[]
                    {
                        new UiActionDefinition(
                            "wrong", UiSurfaceKind.Results,
                            UiActionKind.Close,
                            visible: true, enabled: true)
                    }));
        }
    }
}
