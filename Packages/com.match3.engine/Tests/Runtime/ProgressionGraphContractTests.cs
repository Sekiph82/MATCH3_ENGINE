using System;
using System.Collections.Generic;
using System.Linq;
using Match3.Engine.Runtime;
using NUnit.Framework;

namespace Match3.Engine.Tests
{
    public sealed class ProgressionGraphContractTests
    {
        private static ProgressionGraph FiveGameGraph()
        {
            var worlds = GameProfileCatalog.Ids.Select((game, i) =>
                new WorldDefinition("world-" + i, game)).ToArray();
            var chapters = worlds.Select((world, i) =>
                new ChapterDefinition("chapter-" + i, world.WorldId)).ToArray();
            var levels = chapters.Select((chapter, i) =>
                new LevelDefinition("level-" + i, chapter.ChapterId)).ToArray();
            return new ProgressionGraph(worlds, chapters, levels);
        }

        [Test]
        public void M08_001_SharedGraphSchemaValidatesFiveGameIdentityReferencesAndCycles()
        {
            var graph = FiveGameGraph();
            Assert.That(graph.Worlds.Count, Is.EqualTo(5));
            Assert.That(graph.Chapters.Count, Is.EqualTo(5));
            Assert.That(graph.Levels.Count, Is.EqualTo(5));

            for (var i = 0; i < GameProfileCatalog.Ids.Count; i++)
                Assert.That(graph.GameIdForLevel("level-" + i),
                    Is.EqualTo(GameProfileCatalog.Ids[i]));

            Assert.Throws<ArgumentException>(() =>
                new ProgressionGraph(
                    new[]
                    {
                        new WorldDefinition("dup", GameProfileCatalog.Ids[0]),
                        new WorldDefinition("dup", GameProfileCatalog.Ids[1])
                    },
                    new[] { new ChapterDefinition("c", "dup") },
                    new[] { new LevelDefinition("l", "c") }));

            Assert.Throws<ArgumentException>(() =>
                new ProgressionGraph(
                    new[] { new WorldDefinition("w", GameProfileCatalog.Ids[0]) },
                    new[] { new ChapterDefinition("c", "missing-world") },
                    new[] { new LevelDefinition("l", "c") }));

            Assert.Throws<ArgumentException>(() =>
                new ProgressionGraph(
                    new[] { new WorldDefinition("w", GameProfileCatalog.Ids[0]) },
                    new[] { new ChapterDefinition("c", "w") },
                    new[]
                    {
                        new LevelDefinition("a", "c", new[] { "b" }),
                        new LevelDefinition("b", "c", new[] { "a" })
                    }));

            Assert.Throws<ArgumentException>(() =>
                new WorldDefinition("w", "Unknown_Game"));

            Assert.Throws<ArgumentException>(() =>
                new ProgressionGraph(
                    new[]
                    {
                        new WorldDefinition("wa", GameProfileCatalog.Ids[0]),
                        new WorldDefinition("wb", GameProfileCatalog.Ids[1])
                    },
                    new[]
                    {
                        new ChapterDefinition("ca", "wa"),
                        new ChapterDefinition("cb", "wb")
                    },
                    new[]
                    {
                        new LevelDefinition("la", "ca"),
                        new LevelDefinition("lb", "cb", new[] { "la" })
                    }));
        }

        [Test]
        public void M08_002_NavigationFollowsCommonRouteAndRejectsMismatchIllegalOrDuplicateTransitions()
        {
            var graph = new ProgressionGraph(
                new[] { new WorldDefinition("w", GameProfileCatalog.Ids[0]) },
                new[] { new ChapterDefinition("c", "w") },
                new[] { new LevelDefinition("l", "c") });

            var state = new NavigationState(
                new NavigationRoute(NavigationRouteKind.Home, "route-home"));

            state = NavigationPlanner.Transition(graph, state,
                new NavigationRoute(NavigationRouteKind.World,
                    "route-world", "w")).Result;
            state = NavigationPlanner.Transition(graph, state,
                new NavigationRoute(NavigationRouteKind.Chapter,
                    "route-chapter", "w", "c")).Result;
            state = NavigationPlanner.Transition(graph, state,
                new NavigationRoute(NavigationRouteKind.Level,
                    "route-level", "w", "c", "l")).Result;
            state = NavigationPlanner.Transition(graph, state,
                new NavigationRoute(NavigationRouteKind.Result,
                    "route-result", "w", "c", "l")).Result;
            state = NavigationPlanner.Transition(graph, state,
                new NavigationRoute(NavigationRouteKind.Map,
                    "route-map", "w", "c")).Result;

            Assert.That(state.Current.Kind, Is.EqualTo(NavigationRouteKind.Map));
            Assert.That(state.ProcessedRouteIds.Count, Is.EqualTo(5));

            var duplicate = NavigationPlanner.Transition(graph, state,
                new NavigationRoute(NavigationRouteKind.Level,
                    "route-level", "w", "c", "l"));
            Assert.That(duplicate.Applied, Is.False);
            Assert.That(duplicate.Result, Is.SameAs(state));

            Assert.Throws<InvalidOperationException>(() =>
                NavigationPlanner.Transition(graph,
                    new NavigationState(
                        new NavigationRoute(NavigationRouteKind.Home, "home-2")),
                    new NavigationRoute(NavigationRouteKind.Result,
                        "illegal", "w", "c", "l")));

            Assert.Throws<ArgumentException>(() =>
                NavigationPlanner.ValidateRoute(graph,
                    new NavigationRoute(NavigationRouteKind.Chapter,
                        "bad-chapter", "w", "missing")));

            Assert.Throws<ArgumentException>(() =>
                new NavigationRoute(NavigationRouteKind.Level,
                    "bad-shape", "w", "c"));

            Assert.Throws<ArgumentException>(() =>
                NavigationPlanner.Transition(
                    graph,
                    new NavigationState(
                        new NavigationRoute(
                            NavigationRouteKind.World,
                            "bad-current", "missing-world")),
                    new NavigationRoute(
                        NavigationRouteKind.Chapter,
                        "next", "w", "c")));
        }

        [Test]
        public void M08_003_UnlockRulesAreCanonicalStarGatedMonotonicAndReplaySafe()
        {
            var graph = new ProgressionGraph(
                new[] { new WorldDefinition("w", GameProfileCatalog.Ids[0]) },
                new[] { new ChapterDefinition("c", "w") },
                new[]
                {
                    new LevelDefinition("L1", "c"),
                    new LevelDefinition("L2", "c", new[] { "L1" }, 2),
                    new LevelDefinition("L3", "c", new[] { "L2" }, 3)
                });

            var thresholds = new StarThresholds(100, 200, 300);
            var start = new UnlockSnapshot(
                unlockedLevelIds: new[] { "L1" });

            var win1 = new LevelResultAttempt(
                "attempt-1", "L1", true, 250, 2, true);
            var first = UnlockPlanner.ApplyVerifiedWin(
                graph, start, win1, thresholds);
            Assert.That(first.Applied, Is.True);
            Assert.That(first.NewlyUnlockedLevelIds,
                Is.EqualTo(new[] { "L2" }));
            Assert.That(first.Result.HighestStars["L1"], Is.EqualTo(2));

            var replay = UnlockPlanner.ApplyVerifiedWin(
                graph, first.Result, win1, thresholds);
            Assert.That(replay.Applied, Is.False);
            Assert.That(replay.NewlyUnlockedLevelIds, Is.Empty);

            var improve = UnlockPlanner.ApplyVerifiedWin(
                graph, first.Result,
                new LevelResultAttempt(
                    "attempt-2", "L1", true, 350, 3, true),
                thresholds);
            Assert.That(improve.Result.HighestStars["L1"], Is.EqualTo(3));
            Assert.That(improve.NewlyUnlockedLevelIds, Is.Empty);

            var win2 = UnlockPlanner.ApplyVerifiedWin(
                graph, improve.Result,
                new LevelResultAttempt(
                    "attempt-3", "L2", true, 350, 3, true),
                thresholds);
            Assert.That(win2.NewlyUnlockedLevelIds,
                Is.EqualTo(new[] { "L3" }));

            Assert.Throws<InvalidOperationException>(() =>
                UnlockPlanner.ApplyVerifiedWin(
                    graph, win2.Result,
                    new LevelResultAttempt(
                        "forged-stars", "L2", true, 150, 3, true),
                    thresholds));

            Assert.Throws<InvalidOperationException>(() =>
                UnlockPlanner.ApplyVerifiedWin(
                    graph, win2.Result,
                    new LevelResultAttempt(
                        "loss", "L2", false, 0, 0, true),
                    thresholds));

            Assert.Throws<ArgumentException>(() =>
                UnlockPlanner.ApplyVerifiedWin(
                    graph,
                    new UnlockSnapshot(
                        unlockedLevelIds: new[] { "UNKNOWN" }),
                    new LevelResultAttempt(
                        "attempt-malformed", "L1", true, 150, 1, true),
                    thresholds));
        }

        [Test]
        public void M08_004_HeartsReconcileOfflineTimeClampRollbackCapAndExactlyOnceLedger()
        {
            var start = new HeartSnapshot(
                balance: 2,
                maximumBalance: 5,
                regenIntervalSeconds: 900,
                highWaterUnixSeconds: 1000,
                lastRegenUnixSeconds: 1000);

            var offline = HeartPlanner.ReconcileTime(start, 2800);
            Assert.That(offline.Result.Balance, Is.EqualTo(4));
            Assert.That(offline.BalanceDelta, Is.EqualTo(2));
            Assert.That(offline.Result.HighWaterUnixSeconds, Is.EqualTo(2800));
            Assert.That(offline.Result.LastRegenUnixSeconds, Is.EqualTo(2800));

            var rollback = HeartPlanner.ReconcileTime(
                offline.Result, 2000);
            Assert.That(rollback.Applied, Is.False);
            Assert.That(rollback.Result, Is.SameAs(offline.Result));
            Assert.That(rollback.Result.Balance, Is.EqualTo(4));

            var full = HeartPlanner.ReconcileTime(
                rollback.Result, 10000);
            Assert.That(full.Result.Balance, Is.EqualTo(5));
            Assert.That(full.BalanceDelta, Is.EqualTo(1));
            Assert.That(full.Result.HighWaterUnixSeconds, Is.EqualTo(10000));

            var spent = HeartPlanner.Spend(
                full.Result, "level-start-1", 1);
            Assert.That(spent.Result.Balance, Is.EqualTo(4));
            Assert.That(spent.BalanceDelta, Is.EqualTo(-1));

            var spendReplay = HeartPlanner.Spend(
                spent.Result, "level-start-1", 1);
            Assert.That(spendReplay.Applied, Is.False);
            Assert.That(spendReplay.Result.Balance, Is.EqualTo(4));

            Assert.Throws<InvalidOperationException>(() =>
                HeartPlanner.Grant(
                    spent.Result, "level-start-1", 1));

            var grant = HeartPlanner.Grant(
                spent.Result, "gift-1", 10);
            Assert.That(grant.Result.Balance, Is.EqualTo(5));
            Assert.That(grant.BalanceDelta, Is.EqualTo(1));

            var grantReplay = HeartPlanner.Grant(
                grant.Result, "gift-1", 10);
            Assert.That(grantReplay.Applied, Is.False);
            Assert.That(grantReplay.Result.Balance, Is.EqualTo(5));

            Assert.Throws<InvalidOperationException>(() =>
                HeartPlanner.Spend(
                    new HeartSnapshot(0, 5, 900, 1000, 1000),
                    "empty-spend", 1));
        }
    }
}
