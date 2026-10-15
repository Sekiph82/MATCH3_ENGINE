using System;
using System.Collections.Generic;
using System.Linq;
using Match3.Engine.Runtime;
using NUnit.Framework;

namespace Match3.Engine.Tests
{
    public sealed class MetaProgressionContractTests
    {
        private static CollectionCatalog Catalog()
        {
            return new CollectionCatalog(
                "Coral_Rescue",
                new[]
                {
                    new CollectionItemDefinition(
                        "card-a", "set-core", CollectionItemKind.Card),
                    new CollectionItemDefinition(
                        "card-b", "set-core", CollectionItemKind.Card),
                    new CollectionItemDefinition(
                        "season-a", "set-season", CollectionItemKind.Species,
                        "S1")
                },
                new[]
                {
                    new CollectionSetDefinition(
                        "set-core", new[] { "card-a", "card-b" },
                        "collection:set-core"),
                    new CollectionSetDefinition(
                        "set-season", new[] { "season-a" },
                        "collection:set-season", "S1")
                });
        }

        private static VerifiedProgressionResult R(
            string id, bool won, bool firstClear, bool replay,
            string level = "L1", string frontier = "L1",
            int score = 1000, int stars = 3, int attempt = 1,
            string week = "2026-W42", bool verified = true,
            string game = "Coral_Rescue", string profile = "P1",
            string mode = "campaign")
        {
            return new VerifiedProgressionResult(
                id, game, profile, mode, level, frontier,
                won, firstClear, replay, score, won ? stars : 0,
                attempt, 100, week, verified);
        }

        [Test]
        public void M08_013_CollectionsTrackNewDuplicateSetMasterSeasonVisibilityAndRetainEarnedItems()
        {
            var catalog = Catalog();
            var snapshot = new CollectionSnapshot("Coral_Rescue");

            var a = CollectionPlanner.Earn(
                catalog, snapshot, "earn-a", "card-a");
            Assert.That(a.IsNewItem, Is.True);
            Assert.That(a.IsDuplicate, Is.False);
            Assert.That(a.NewlyCompletedSetIds, Is.Empty);

            var duplicate = CollectionPlanner.Earn(
                catalog, a.Result, "earn-a-dup", "card-a");
            Assert.That(duplicate.IsNewItem, Is.False);
            Assert.That(duplicate.IsDuplicate, Is.True);
            Assert.That(duplicate.Result.Counts["card-a"], Is.EqualTo(2));

            var b = CollectionPlanner.Earn(
                catalog, duplicate.Result, "earn-b", "card-b");
            Assert.That(b.NewlyCompletedSetIds,
                Is.EqualTo(new[] { "set-core" }));
            Assert.That(b.MasterComplete, Is.False);

            var season = CollectionPlanner.Earn(
                catalog, b.Result, "earn-season", "season-a");
            Assert.That(season.NewlyCompletedSetIds,
                Is.EqualTo(new[] { "set-season" }));
            Assert.That(season.MasterComplete, Is.True);

            Assert.That(catalog.VisibleSetIds(null),
                Is.EqualTo(new[] { "set-core" }));
            Assert.That(catalog.VisibleSetIds("S1"),
                Is.EqualTo(new[] { "set-core", "set-season" }));
            Assert.That(catalog.VisibleSetIds("S2"),
                Is.EqualTo(new[] { "set-core" }));

            Assert.That(season.Result.Counts["season-a"], Is.EqualTo(1),
                "Season visibility changes must not delete permanent earned collection history.");

            var replay = CollectionPlanner.Earn(
                catalog, season.Result, "earn-season", "season-a");
            Assert.That(replay.Applied, Is.False);

            Assert.Throws<ArgumentException>(() =>
                CollectionPlanner.Earn(
                    catalog,
                    new CollectionSnapshot(
                        "Coral_Rescue",
                        completedSetIds: new[] { "set-core" }),
                    "bad-state", "card-a"));

            Assert.Throws<ArgumentException>(() =>
                new CollectionCatalog(
                    "Coral_Rescue",
                    new[]
                    {
                        new CollectionItemDefinition(
                            "x", "sx", CollectionItemKind.Card),
                        new CollectionItemDefinition(
                            "y", "sy", CollectionItemKind.Card)
                    },
                    new[]
                    {
                        new CollectionSetDefinition(
                            "sx", new[] { "x" }, "reward:same"),
                        new CollectionSetDefinition(
                            "sy", new[] { "y" }, "reward:same")
                    }));
        }

        [Test]
        public void M08_014_EarnedPacksAreDeterministicReplaySafeAndBoundMissingGuarantees()
        {
            var catalog = Catalog();
            var pack = new EarnedPackDefinition(
                "earned-core",
                new[] { "card-a", "card-b" },
                drawCount: 2,
                pityThreshold: 2);

            var collection = CollectionPlanner.Earn(
                catalog, new CollectionSnapshot("Coral_Rescue"),
                "existing-a", "card-a").Result;

            var state = new EarnedPackSnapshot(
                "Coral_Rescue", "earned-core", pityCounter: 2);
            var one = EarnedPackPlanner.Open(
                catalog, collection, pack, state,
                new EarnedPackReceipt(
                    "earned-receipt-1", "Coral_Rescue", "earned-core",
                    RewardSourceKind.Event), 12345u);
            var two = EarnedPackPlanner.Open(
                catalog, collection, pack,
                new EarnedPackSnapshot(
                "Coral_Rescue", "earned-core", pityCounter: 2),
                new EarnedPackReceipt(
                    "earned-receipt-2", "Coral_Rescue", "earned-core",
                    RewardSourceKind.Event), 12345u);

            Assert.That(one.Applied, Is.True);
            Assert.That(one.ItemIds, Is.EqualTo(two.ItemIds));
            Assert.That(one.ItemIds.Count, Is.EqualTo(2));
            Assert.That(one.ItemIds.All(x => pack.ItemIds.Contains(x)), Is.True);
            Assert.That(one.MissingItemGuaranteeUsed, Is.True);
            Assert.That(one.ItemIds.Contains("card-b"), Is.True,
                "Guarantee must choose a genuinely missing item from the defined pool.");
            Assert.That(one.Result.PityCounter, Is.EqualTo(0));

            var replay = EarnedPackPlanner.Open(
                catalog, collection, pack, one.Result,
                new EarnedPackReceipt(
                    "earned-receipt-1", "Coral_Rescue", "earned-core",
                    RewardSourceKind.Event), 12345u);
            Assert.That(replay.Applied, Is.False);
            Assert.That(replay.ItemIds, Is.Empty);

            Assert.Throws<ArgumentException>(() =>
                EarnedPackPlanner.Open(
                    catalog, collection,
                    new EarnedPackDefinition(
                        "bad-pack", new[] { "not-in-catalog" }, 1, 1),
                    new EarnedPackSnapshot(
                    "Coral_Rescue", "bad-pack"),
                    new EarnedPackReceipt(
                    "earned-only-receipt", "Coral_Rescue", "bad-pack",
                    RewardSourceKind.Event), 1u));

            Assert.Throws<ArgumentException>(() =>
                new EarnedPackReceipt(
                    "paid-random", "Coral_Rescue", "earned-core",
                    RewardSourceKind.Purchase));

            Assert.Throws<InvalidOperationException>(() =>
                EarnedPackPlanner.Open(
                    catalog,
                    new CollectionSnapshot("Garage_Legends"),
                    pack,
                    new EarnedPackSnapshot(
                        "Garage_Legends", "earned-core"),
                    new EarnedPackReceipt(
                        "foreign-game", "Garage_Legends", "earned-core",
                        RewardSourceKind.Event),
                    7u));
            Assert.Throws<InvalidOperationException>(() =>
                EarnedPackPlanner.Open(
                    catalog, collection, pack,
                    new EarnedPackSnapshot(
                    "Coral_Rescue", "earned-core"),
                    new EarnedPackReceipt(
                        "unverified", "Coral_Rescue", "earned-core",
                        RewardSourceKind.Event, false), 99u));

            Assert.Throws<ArgumentOutOfRangeException>(() =>
                EarnedPackPlanner.Open(
                    catalog, collection, pack,
                    new EarnedPackSnapshot(
                    "Coral_Rescue", "earned-core"),
                    new EarnedPackReceipt(
                        "earned-receipt-3", "Coral_Rescue", "earned-core",
                        RewardSourceKind.Event), 0u));
        }

        [Test]
        public void M08_015_JourneyTracksTenLevelsFirstTryAndWeeklyPersonalBestWithoutRegression()
        {
            var levels = Enumerable.Range(1, 10)
                .Select(i => "L" + i).ToArray();
            var definition = new JourneyDefinition(
                "Coral_Rescue", "P1", "campaign", levels);
            var start = new JourneySnapshot();

            Assert.That(JourneyPlanner.UpcomingLevel(definition, start),
                Is.EqualTo("L1"));

            var first = JourneyPlanner.ApplyResult(
                definition, start,
                R("jr1", true, true, false,
                    level: "L1", frontier: "L1",
                    score: 1200, stars: 3, attempt: 1));
            Assert.That(first.FirstTryWinAdded, Is.True);
            Assert.That(first.PersonalBestImproved, Is.True);
            Assert.That(first.Result.CompletedLevelIds,
                Does.Contain("L1"));
            Assert.That(first.Result.FirstTryWinLevelIds,
                Does.Contain("L1"));
            Assert.That(JourneyPlanner.UpcomingLevel(
                definition, first.Result), Is.EqualTo("L2"));

            var lower = JourneyPlanner.ApplyResult(
                definition, first.Result,
                R("jr2", true, false, true,
                    level: "L1", frontier: "L2",
                    score: 900, stars: 2, attempt: 2));
            Assert.That(lower.PersonalBestImproved, Is.False);
            var key = "Coral_Rescue|P1|campaign|L1|2026-W42";
            Assert.That(lower.Result.PersonalBests[key].Score, Is.EqualTo(1200));

            var higher = JourneyPlanner.ApplyResult(
                definition, lower.Result,
                R("jr3", true, false, true,
                    level: "L1", frontier: "L2",
                    score: 1500, stars: 3, attempt: 3));
            Assert.That(higher.PersonalBestImproved, Is.True);
            Assert.That(higher.Result.PersonalBests[key].Score, Is.EqualTo(1500));

            var nextWeek = JourneyPlanner.ApplyResult(
                definition, higher.Result,
                R("jr4", true, false, true,
                    level: "L1", frontier: "L2",
                    score: 1000, stars: 3, attempt: 4,
                    week: "2026-W43"));
            Assert.That(nextWeek.Result.PersonalBests.Count, Is.EqualTo(2));

            Assert.Throws<InvalidOperationException>(() =>
                JourneyPlanner.ApplyResult(
                    definition, nextWeek.Result,
                    R("forged", true, false, true,
                        level: "L1", frontier: "L2",
                        verified: false)));

            Assert.Throws<InvalidOperationException>(() =>
                JourneyPlanner.ApplyResult(
                    definition, nextWeek.Result,
                    R("cross-game", true, false, true,
                        level: "L1", frontier: "L2",
                        game: "Garage_Legends")));
        }

        [Test]
        public void M08_016_FailRecoveryCountsOnlyGenuineFrontierFailuresAndNeverSpendsOrAutoUses()
        {
            var policy = new FailRecoveryPolicy(3);
            var candidates = new[]
            {
                new BoosterRecommendationCandidate(
                    "slot", 1, 1, true, true),
                new BoosterRecommendationCandidate(
                    "selector", 2, 0, true, true),
                new BoosterRecommendationCandidate(
                    "tornado", 3, 0, true, true)
            };
            var state = new FailRecoverySnapshot(
                "Coral_Rescue", "P1", "campaign", "L5");

            for (var i = 1; i <= 2; i++)
            {
                var p = FailRecoveryPlanner.ApplyResult(
                    state,
                    R("fail-" + i, false, false, false,
                        level: "L5", frontier: "L5",
                        score: 0, stars: 0, attempt: i),
                    policy, candidates, true);
                state = p.Result;
                Assert.That(p.Recommendations, Is.Empty);
            }

            var third = FailRecoveryPlanner.ApplyResult(
                state,
                R("fail-3", false, false, false,
                    level: "L5", frontier: "L5",
                    score: 0, stars: 0, attempt: 3),
                policy, candidates, true);
            Assert.That(third.Result.ConsecutiveFailures, Is.EqualTo(3));
            Assert.That(third.Recommendations.Count, Is.EqualTo(2));
            Assert.That(third.Recommendations[0].BoosterId, Is.EqualTo("slot"));
            Assert.That(third.Recommendations[0].OwnedReady, Is.True);
            Assert.That(third.Recommendations[0].RewardedOfferAvailable, Is.False);
            Assert.That(third.Recommendations[0].PurchaseOfferAvailable, Is.False);
            Assert.That(third.Recommendations[1].BoosterId, Is.EqualTo("selector"));
            Assert.That(third.Recommendations[1].RewardedOfferAvailable, Is.True);
            Assert.That(third.Recommendations[1].PurchaseOfferAvailable, Is.True);

            var replayLoss = FailRecoveryPlanner.ApplyResult(
                third.Result,
                R("replay-loss", false, false, true,
                    level: "L5", frontier: "L5",
                    score: 0, stars: 0, attempt: 4),
                policy, candidates, true);
            Assert.That(replayLoss.Result.ConsecutiveFailures, Is.EqualTo(3));
            Assert.That(replayLoss.Recommendations, Is.Empty);

            var nonFrontier = FailRecoveryPlanner.ApplyResult(
                replayLoss.Result,
                R("old-level-loss", false, false, false,
                    level: "L4", frontier: "L5",
                    score: 0, stars: 0, attempt: 5),
                policy, candidates, true);
            Assert.That(nonFrontier.Result.ConsecutiveFailures, Is.EqualTo(3));
            Assert.That(nonFrontier.Recommendations, Is.Empty);

            var disabledAds = FailRecoveryPlanner.ApplyResult(
                nonFrontier.Result,
                R("fail-4", false, false, false,
                    level: "L5", frontier: "L5",
                    score: 0, stars: 0, attempt: 6),
                policy, candidates, false);
            Assert.That(disabledAds.Recommendations.Count, Is.EqualTo(2));
            Assert.That(disabledAds.Recommendations[1].RewardedOfferAvailable,
                Is.False);
            Assert.That(disabledAds.Recommendations[1].PurchaseOfferAvailable,
                Is.True);

            var win = FailRecoveryPlanner.ApplyResult(
                disabledAds.Result,
                R("win", true, true, false,
                    level: "L5", frontier: "L5",
                    attempt: 7),
                policy, candidates, true);
            Assert.That(win.Result.ConsecutiveFailures, Is.EqualTo(0));

            var nextFrontier = FailRecoveryPlanner.ApplyResult(
                win.Result,
                R("next-frontier-loss", false, false, false,
                    level: "L6", frontier: "L6",
                    score: 0, stars: 0, attempt: 8),
                policy, candidates, true);
            Assert.That(nextFrontier.Result.FrontierLevelId, Is.EqualTo("L6"));
            Assert.That(nextFrontier.Result.ConsecutiveFailures, Is.EqualTo(1));
            Assert.That(nextFrontier.Recommendations, Is.Empty);

            Assert.Throws<InvalidOperationException>(() =>
                FailRecoveryPlanner.ApplyResult(
                    win.Result,
                    R("forged", false, false, false,
                        level: "L5", frontier: "L5",
                        score: 0, stars: 0, attempt: 9,
                        verified: false),
                    policy, candidates, true));
        }

        [Test]
        public void M08_017_AchievementsAreCanonicalReplaySafeAndPlatformMappingsCannotCollide()
        {
            var definitions = new[]
            {
                new AchievementDefinition(
                    "win-10", 10,
                    "gc.win10", "gpgs.win10"),
                new AchievementDefinition(
                    "collect-5", 5,
                    "gc.collect5", "gpgs.collect5")
            };
            var snapshot = new AchievementSnapshot();

            var one = AchievementPlanner.Apply(
                snapshot, definitions,
                new AchievementProgressEvent(
                    "ach-event-1", "win-10", 9));
            Assert.That(one.Result.Counts["win-10"], Is.EqualTo(9));
            Assert.That(one.NewlyUnlocked, Is.False);

            var two = AchievementPlanner.Apply(
                one.Result, definitions,
                new AchievementProgressEvent(
                    "ach-event-2", "win-10", 1));
            Assert.That(two.NewlyUnlocked, Is.True);
            Assert.That(two.Result.UnlockedIds, Does.Contain("win-10"));

            var replay = AchievementPlanner.Apply(
                two.Result, definitions,
                new AchievementProgressEvent(
                    "ach-event-2", "win-10", 1));
            Assert.That(replay.Applied, Is.False);

            Assert.Throws<InvalidOperationException>(() =>
                AchievementPlanner.Apply(
                    two.Result, definitions,
                    new AchievementProgressEvent(
                        "forged", "collect-5", 5, false)));

            Assert.Throws<ArgumentException>(() =>
                AchievementPlanner.Apply(
                    new AchievementSnapshot(),
                    new[]
                    {
                        new AchievementDefinition(
                            "a", 1, "gc.same", "gpgs.a"),
                        new AchievementDefinition(
                            "b", 1, "gc.same", "gpgs.b")
                    },
                    new AchievementProgressEvent(
                        "dup-native", "a", 1)));

            Assert.That(definitions[0].AppleGameCenterId,
                Is.EqualTo("gc.win10"));
            Assert.That(definitions[0].GooglePlayGamesId,
                Is.EqualTo("gpgs.win10"));

            var denied = AchievementPlatformProjector.ProjectUnlocked(
                two.Result, definitions,
                AchievementPlatform.AppleGameCenter,
                nativeSignedIn: false);
            Assert.That(denied, Is.Empty);
            Assert.That(two.Result.UnlockedIds, Does.Contain("win-10"),
                "Native sign-in denial must not remove local achievement state.");

            var apple = AchievementPlatformProjector.ProjectUnlocked(
                two.Result, definitions,
                AchievementPlatform.AppleGameCenter,
                nativeSignedIn: true);
            var google = AchievementPlatformProjector.ProjectUnlocked(
                two.Result, definitions,
                AchievementPlatform.GooglePlayGames,
                nativeSignedIn: true);
            Assert.That(apple, Is.EqualTo(new[] { "gc.win10" }));
            Assert.That(google, Is.EqualTo(new[] { "gpgs.win10" }));

            Assert.Throws<ArgumentException>(() =>
                AchievementPlanner.Apply(
                    new AchievementSnapshot(
                        new[]
                        {
                            new KeyValuePair<string, int>("win-10", 10)
                        }),
                    definitions,
                    new AchievementProgressEvent(
                        "bad-save", "collect-5", 1)));

            Assert.Throws<ArgumentException>(() =>
                AchievementPlatformProjector.ProjectUnlocked(
                    two.Result,
                    new[]
                    {
                        new AchievementDefinition(
                            "win-10", 10, "gc.same", "gpgs.win10"),
                        new AchievementDefinition(
                            "collect-5", 5, "gc.same", "gpgs.collect5")
                    },
                    AchievementPlatform.AppleGameCenter,
                    nativeSignedIn: true));
        }

        [Test]
        public void M08_019_ReentryRoutesPreserveAuthoritativeFrontierRewardsAndSaveRevision()
        {
            var graph = new ProgressionGraph(
                new[] { new WorldDefinition("w", "Coral_Rescue") },
                new[] { new ChapterDefinition("c", "w") },
                new[] { new LevelDefinition("L5", "c") });
            var current = new NavigationRoute(
                NavigationRouteKind.Level,
                "route-current", "w", "c", "L5");
            var map = new NavigationRoute(
                NavigationRouteKind.Map,
                "route-map", "w", "c");
            var authority = new ReentryAuthoritySnapshot(
                "Coral_Rescue", "P1", "L5",
                saveRevision: 42, heartBalance: 0,
                currentRoute: current,
                frontierMapRoute: map,
                committedRewardSourceIds:
                    new[] { "firstclear:L4", "daily:100" });

            var resumed = ReentryPlanner.Resolve(
                graph, authority, ReentryReason.Resumed, "stale-modal-A");
            Assert.That(resumed.Authority, Is.SameAs(authority));
            Assert.That(resumed.TargetRoute, Is.SameAs(current));

            foreach (var reason in new[]
            {
                ReentryReason.Returning,
                ReentryReason.Failed,
                ReentryReason.NoHeart
            })
            {
                var plan = ReentryPlanner.Resolve(
                    authority, reason, "unfinished-animation");
                Assert.That(plan.TargetRoute, Is.SameAs(map));
                Assert.That(plan.Authority.SaveRevision, Is.EqualTo(42));
                Assert.That(plan.Authority.FrontierLevelId, Is.EqualTo("L5"));
                Assert.That(plan.Authority.CommittedRewardSourceIds,
                    Is.EqualTo(new[] { "daily:100", "firstclear:L4" }));
            }

            var first = ReentryPlanner.Resolve(
                graph, authority, ReentryReason.FirstTime, "ignored");
            Assert.That(first.TargetRoute.Kind,
                Is.EqualTo(NavigationRouteKind.Home));
            Assert.That(first.Authority.HeartBalance, Is.EqualTo(0));

            var tokenA = ReentryPlanner.Resolve(
                graph, authority, ReentryReason.Returning, "A");
            var tokenB = ReentryPlanner.Resolve(
                graph, authority, ReentryReason.Returning, "B");
            Assert.That(tokenA.Authority, Is.SameAs(tokenB.Authority));
            Assert.That(tokenA.TargetRoute, Is.SameAs(tokenB.TargetRoute),
                "Modal/animation tokens must never become save authority.");

            var foreignGraph = new ProgressionGraph(
                new[] { new WorldDefinition("w2", "Garage_Legends") },
                new[] { new ChapterDefinition("c2", "w2") },
                new[] { new LevelDefinition("L5", "c2") });
            Assert.Throws<InvalidOperationException>(() =>
                ReentryPlanner.Resolve(
                    foreignGraph, authority,
                    ReentryReason.Returning, "ignored"));
        }
    }
}
