using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Match3.Engine.Runtime;
using NUnit.Framework;

namespace Match3.Engine.Tests
{
    public sealed class ObstacleFlowContractTests
    {
        private static BoardPosition P(int row, int col, int layer = 0) =>
            new BoardPosition(row, col, layer);

        [Test]
        public void M05_005_GeneratorAndCollectorAreSeededBoundedBlockedAndExactlyOnce()
        {
            var topology = new BoardTopology(2, 2);
            var rule = new GeneratorRule("gen", P(0,0), new[]
            {
                new WeightedContent("ingredient.apple", 1),
                new WeightedContent("ingredient.berry", 3)
            });

            var first = IngredientGeneratorPlanner.Plan(topology, rule,
                new IngredientState[0], new SeededRandom(99u), "spawn-1");
            var second = IngredientGeneratorPlanner.Plan(topology, rule,
                new IngredientState[0], new SeededRandom(99u), "spawn-1");
            Assert.That(first.Blocked, Is.False);
            Assert.That(first.Spawned.Count, Is.EqualTo(1));
            Assert.That(first.Spawned[0].IngredientId,
                Is.EqualTo(second.Spawned[0].IngredientId));
            Assert.That(first.Spawned[0].InstanceId,
                Is.EqualTo("ingredient:gen:spawn-1"));

            var occupied = new BoardTopology(2, 2,
                occupants: new[] { new BoardOccupant(P(0,0), "piece") });
            Assert.That(IngredientGeneratorPlanner.Plan(occupied, rule,
                new IngredientState[0], new SeededRandom(99u), "spawn-2").Blocked,
                Is.True);

            var locked = new BoardTopology(2, 2, locked: new[] { P(0,0) });
            Assert.Throws<ArgumentException>(() =>
                IngredientGeneratorPlanner.Plan(locked, rule,
                    new IngredientState[0], new SeededRandom(99u), "spawn-3"));

            var collector = new IngredientCollectorRule("sink",
                new[] { P(1,1) },
                new[] { first.Spawned[0].IngredientId });
            var atSink = new IngredientState(first.Spawned[0].InstanceId,
                first.Spawned[0].IngredientId, P(1,1));
            var collected = IngredientCollectionPlanner.Plan(
                new[] { atSink }, collector, null);
            Assert.That(collected.Receipts.Count, Is.EqualTo(1));
            Assert.That(collected.Remaining, Is.Empty);

            var replay = IngredientCollectionPlanner.Plan(
                new[] { atSink }, collector, collected.ProcessedReceiptIds);
            Assert.That(replay.Receipts, Is.Empty);

            Assert.Throws<ArgumentException>(() =>
                new GeneratorRule("too-many", P(0,0),
                    Enumerable.Range(0, 257)
                        .Select(x => new WeightedContent("id-" + x, 1))));
        }

        [Test]
        public void M05_008_MetadataSchemaRoundTripsCanonicallyAndRejectsMalformedRegistry()
        {
            var entries = new[]
            {
                new ObstacleMetadataEntry("rock", 3,
                    new[] { "layered", "special", "layered" }),
                new ObstacleMetadataEntry("ice", 2,
                    new[] { "immobilizer" })
            };

            var encoded = ObstacleMetadataSchema.Encode(entries);
            var decoded = ObstacleMetadataSchema.Decode(encoded);

            Assert.That(encoded, Is.EqualTo(
                "v1|ice,2,immobilizer;rock,3,layered+special"));
            Assert.That(ObstacleMetadataSchema.Encode(decoded), Is.EqualTo(encoded));
            Assert.That(decoded.Single(x => x.Id == "rock").Capabilities,
                Is.EqualTo(new[] { "layered", "special" }));

            Assert.Throws<FormatException>(() =>
                ObstacleMetadataSchema.Decode("v2|rock,3,layered"));
            Assert.Throws<ArgumentException>(() =>
                ObstacleMetadataSchema.Encode(new[]
                {
                    new ObstacleMetadataEntry("x", 1, new[] { "a" }),
                    new ObstacleMetadataEntry("x", 1, new[] { "b" })
                }));
            Assert.Throws<ArgumentException>(() =>
                new ObstacleMetadataEntry("bad;id", 1, new[] { "a" }));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new ObstacleMetadataEntry("x", 0, new[] { "a" }));
        }

        [Test]
        public void M05_009_FiveSkinBindingsShareNeutralMechanicsAndContainNoGameplayFields()
        {
            var required = new[] { "ice", "rock" };
            var bindings = GameProfileCatalog.Ids.Select(gameId =>
                new ObstacleSkinBinding(gameId,
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        { "ice", gameId + ".ice" },
                        { "rock", gameId + ".rock" }
                    })).ToArray();

            var registry = FiveTitleObstacleSkinRegistry.Validate(bindings, required);
            Assert.That(registry.Count, Is.EqualTo(5));
            Assert.That(registry.Keys.OrderBy(x => x, StringComparer.Ordinal),
                Is.EqualTo(GameProfileCatalog.Ids
                    .OrderBy(x => x, StringComparer.Ordinal)));

            var incomplete = bindings.ToArray();
            incomplete[0] = new ObstacleSkinBinding(incomplete[0].GameId,
                new Dictionary<string, string>
                {
                    { "ice", "only.ice" }
                });
            Assert.Throws<InvalidOperationException>(() =>
                FiveTitleObstacleSkinRegistry.Validate(incomplete, required));
            Assert.Throws<ArgumentException>(() =>
                new ObstacleSkinBinding("Unknown_Title",
                    new Dictionary<string, string> { { "ice", "x" } }));

            var publicProps = typeof(ObstacleSkinBinding)
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Select(x => x.Name).OrderBy(x => x, StringComparer.Ordinal).ToArray();
            Assert.That(publicProps, Is.EqualTo(new[]
            {
                "GameId", "PresentationKeys"
            }));
        }

        [Test]
        public void M05_013_IngredientExitRequiresCorrectIdentityAndReachableNeutralTopology()
        {
            var collector = new IngredientCollectorRule("exit",
                new[] { P(2,2) }, new[] { "ingredient.apple" });

            var accepted = new IngredientState("apple-1", "ingredient.apple", P(2,2));
            var wrong = new IngredientState("berry-1", "ingredient.berry", P(2,2));
            var plan = IngredientCollectionPlanner.Plan(
                new[] { accepted, wrong }, collector, null);

            Assert.That(plan.Receipts.Count, Is.EqualTo(1));
            Assert.That(plan.Receipts[0].IngredientInstanceId, Is.EqualTo("apple-1"));
            Assert.That(plan.Remaining.Select(x => x.InstanceId).ToArray(),
                Is.EqualTo(new[] { "berry-1" }));

            var open = new BoardTopology(3, 3);
            Assert.That(IngredientCollectionPlanner.IsReachable(open,
                new[] { P(0,0) }, new[] { P(2,2) }), Is.True);

            var blocked = new BoardTopology(3, 3,
                holes: new[] { P(1,0), P(1,1), P(1,2) });
            Assert.That(IngredientCollectionPlanner.IsReachable(blocked,
                new[] { P(0,0) }, new[] { P(2,2) }), Is.False);

            var invalidSink = new BoardTopology(2, 2,
                locked: new[] { P(1,1) });
            Assert.That(IngredientCollectionPlanner.IsReachable(invalidSink,
                new[] { P(0,0) }, new[] { P(1,1) }), Is.False);
        }

        [Test]
        public void M05_014_CrossProductMatrixAllowsDeclaredSupportAndRejectsAmbiguousPairsBeforePlay()
        {
            var supported = new ObstacleCrossProductCase("cell-supported",
                ObstacleCrossFeature.Conveyor |
                ObstacleCrossFeature.Portal |
                ObstacleCrossFeature.Layered |
                ObstacleCrossFeature.Immobilizer |
                ObstacleCrossFeature.Targeted2x2 |
                ObstacleCrossFeature.SpecialDamage |
                ObstacleCrossFeature.Countdown);

            Assert.That(ObstacleCrossProductValidator.Validate(
                new[] { supported }), Is.Empty);
            Assert.DoesNotThrow(() =>
                ObstacleCrossProductValidator.RequireSupported(
                    new[] { supported }));

            var invalid = new[]
            {
                new ObstacleCrossProductCase("egg-countdown",
                    ObstacleCrossFeature.Egg | ObstacleCrossFeature.Countdown),
                new ObstacleCrossProductCase("generator-sink",
                    ObstacleCrossFeature.Generator | ObstacleCrossFeature.IngredientExit),
                new ObstacleCrossProductCase("spread-sink",
                    ObstacleCrossFeature.Spreading | ObstacleCrossFeature.IngredientExit),
                new ObstacleCrossProductCase("portal-sink",
                    ObstacleCrossFeature.Portal | ObstacleCrossFeature.IngredientExit)
            };
            var errors = ObstacleCrossProductValidator.Validate(invalid);
            Assert.That(errors.Select(x => x.Code).OrderBy(x => x, StringComparer.Ordinal),
                Is.EqualTo(new[]
                {
                    "egg_countdown_same_cell",
                    "generator_and_sink_same_cell",
                    "portal_and_sink_phase_ambiguous",
                    "spreader_blocks_ingredient_sink"
                }.OrderBy(x => x, StringComparer.Ordinal)));

            Assert.Throws<InvalidOperationException>(() =>
                ObstacleCrossProductValidator.RequireSupported(invalid));
            Assert.Throws<ArgumentException>(() =>
                ObstacleCrossProductValidator.Validate(new[]
                {
                    new ObstacleCrossProductCase("dup", ObstacleCrossFeature.Layered),
                    new ObstacleCrossProductCase("dup", ObstacleCrossFeature.Portal)
                }));
        }
    }
}
