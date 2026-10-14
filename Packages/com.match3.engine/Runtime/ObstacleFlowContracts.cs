using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Match3.Engine.Runtime
{
    public sealed class GeneratorRule
    {
        public string Id { get; }
        public BoardPosition Position { get; }
        public IReadOnlyList<WeightedContent> Outputs { get; }

        public GeneratorRule(string id, BoardPosition position,
            IEnumerable<WeightedContent> outputs)
        {
            if (string.IsNullOrWhiteSpace(id))
                throw new ArgumentException("Generator ID required.", nameof(id));
            if (outputs == null) throw new ArgumentNullException(nameof(outputs));
            var list = outputs.ToList();
            if (list.Count == 0)
                throw new ArgumentException("Generator outputs required.", nameof(outputs));
            long total = 0;
            foreach (var output in list) total += output.Weight;
            if (total < 1 || total > Int32.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(outputs));
            Id = id;
            Position = position;
            Outputs = new ReadOnlyCollection<WeightedContent>(list);
        }
    }

    public sealed class IngredientState
    {
        public string InstanceId { get; }
        public string IngredientId { get; }
        public BoardPosition Position { get; }

        public IngredientState(string instanceId, string ingredientId,
            BoardPosition position)
        {
            if (string.IsNullOrWhiteSpace(instanceId))
                throw new ArgumentException("Ingredient instance ID required.",
                    nameof(instanceId));
            if (string.IsNullOrWhiteSpace(ingredientId))
                throw new ArgumentException("Ingredient ID required.",
                    nameof(ingredientId));
            InstanceId = instanceId;
            IngredientId = ingredientId;
            Position = position;
        }
    }

    public sealed class GeneratorPlan
    {
        public IReadOnlyList<IngredientState> Spawned { get; }
        public bool Blocked { get; }

        internal GeneratorPlan(IEnumerable<IngredientState> spawned, bool blocked)
        {
            Spawned = new ReadOnlyCollection<IngredientState>(spawned.ToList());
            Blocked = blocked;
        }
    }

    public static class IngredientGeneratorPlanner
    {
        public static GeneratorPlan Plan(BoardTopology topology,
            GeneratorRule rule, IEnumerable<IngredientState> existing,
            SeededRandom rng, string spawnSequenceId)
        {
            if (topology == null) throw new ArgumentNullException(nameof(topology));
            if (rule == null) throw new ArgumentNullException(nameof(rule));
            if (existing == null) throw new ArgumentNullException(nameof(existing));
            if (rng == null) throw new ArgumentNullException(nameof(rng));
            if (string.IsNullOrWhiteSpace(spawnSequenceId))
                throw new ArgumentException("Spawn sequence ID required.",
                    nameof(spawnSequenceId));
            if (!topology.Contains(rule.Position) || topology.IsHole(rule.Position) ||
                topology.IsLocked(rule.Position))
                throw new ArgumentException("Generator position is inactive or locked.",
                    nameof(rule));

            var items = existing.ToList();
            if (items.Any(x => x == null))
                throw new ArgumentException("Null ingredient state.", nameof(existing));
            if (items.GroupBy(x => x.InstanceId, StringComparer.Ordinal)
                .Any(x => x.Count() > 1))
                throw new ArgumentException("Duplicate ingredient instance ID.",
                    nameof(existing));
            if (items.Any(x => x.Position.Equals(rule.Position)) ||
                topology.HasOccupant(rule.Position))
                return new GeneratorPlan(new IngredientState[0], true);

            var total = rule.Outputs.Sum(x => x.Weight);
            var roll = rng.NextIndex(total);
            var cumulative = 0;
            string chosen = null;
            foreach (var output in rule.Outputs)
            {
                cumulative += output.Weight;
                if (roll < cumulative)
                {
                    chosen = output.ContentId;
                    break;
                }
            }
            if (chosen == null)
                throw new InvalidOperationException("Generator output selection failed.");

            var generated = new IngredientState(
                "ingredient:" + rule.Id + ":" + spawnSequenceId,
                chosen, rule.Position);
            return new GeneratorPlan(new[] { generated }, false);
        }
    }

    public sealed class IngredientCollectorRule
    {
        public string Id { get; }
        public IReadOnlyList<BoardPosition> Sinks { get; }
        public IReadOnlyList<string> AcceptedIngredientIds { get; }

        public IngredientCollectorRule(string id,
            IEnumerable<BoardPosition> sinks,
            IEnumerable<string> acceptedIngredientIds)
        {
            if (string.IsNullOrWhiteSpace(id))
                throw new ArgumentException("Collector ID required.", nameof(id));
            if (sinks == null) throw new ArgumentNullException(nameof(sinks));
            if (acceptedIngredientIds == null)
                throw new ArgumentNullException(nameof(acceptedIngredientIds));
            var sinkList = sinks.Distinct().OrderBy(x => x.Layer)
                .ThenBy(x => x.Row).ThenBy(x => x.Column).ToList();
            var ids = acceptedIngredientIds.Distinct(StringComparer.Ordinal)
                .OrderBy(x => x, StringComparer.Ordinal).ToList();
            if (sinkList.Count == 0 || ids.Count == 0 ||
                ids.Any(string.IsNullOrWhiteSpace))
                throw new ArgumentException("Collector sinks and accepted IDs required.");
            Id = id;
            Sinks = new ReadOnlyCollection<BoardPosition>(sinkList);
            AcceptedIngredientIds = new ReadOnlyCollection<string>(ids);
        }
    }

    public readonly struct IngredientCollectionReceipt
    {
        public string ReceiptId { get; }
        public string CollectorId { get; }
        public string IngredientInstanceId { get; }
        public string IngredientId { get; }

        public IngredientCollectionReceipt(string receiptId, string collectorId,
            string ingredientInstanceId, string ingredientId)
        {
            ReceiptId = receiptId;
            CollectorId = collectorId;
            IngredientInstanceId = ingredientInstanceId;
            IngredientId = ingredientId;
        }
    }

    public sealed class IngredientCollectionPlan
    {
        public IReadOnlyList<IngredientState> Remaining { get; }
        public IReadOnlyList<IngredientCollectionReceipt> Receipts { get; }
        public IReadOnlyList<string> ProcessedReceiptIds { get; }

        internal IngredientCollectionPlan(IEnumerable<IngredientState> remaining,
            IEnumerable<IngredientCollectionReceipt> receipts,
            IEnumerable<string> processed)
        {
            Remaining = new ReadOnlyCollection<IngredientState>(remaining.ToList());
            Receipts = new ReadOnlyCollection<IngredientCollectionReceipt>(
                receipts.ToList());
            ProcessedReceiptIds = new ReadOnlyCollection<string>(
                processed.OrderBy(x => x, StringComparer.Ordinal).ToList());
        }
    }

    public static class IngredientCollectionPlanner
    {
        public static IngredientCollectionPlan Plan(
            IEnumerable<IngredientState> ingredients,
            IngredientCollectorRule collector,
            IEnumerable<string> processedReceiptIds)
        {
            if (ingredients == null) throw new ArgumentNullException(nameof(ingredients));
            if (collector == null) throw new ArgumentNullException(nameof(collector));
            var items = ingredients.ToList();
            if (items.Any(x => x == null))
                throw new ArgumentException("Null ingredient.", nameof(ingredients));
            if (items.GroupBy(x => x.InstanceId, StringComparer.Ordinal)
                .Any(x => x.Count() > 1))
                throw new ArgumentException("Duplicate ingredient instance ID.",
                    nameof(ingredients));
            var processed = new HashSet<string>(
                processedReceiptIds ?? Enumerable.Empty<string>(),
                StringComparer.Ordinal);
            var receipts = new List<IngredientCollectionReceipt>();
            var remaining = new List<IngredientState>();

            foreach (var item in items.OrderBy(x => x.Position.Layer)
                .ThenBy(x => x.Position.Row)
                .ThenBy(x => x.Position.Column)
                .ThenBy(x => x.InstanceId, StringComparer.Ordinal))
            {
                var atSink = collector.Sinks.Contains(item.Position);
                var accepted = collector.AcceptedIngredientIds.Contains(
                    item.IngredientId);
                if (!atSink || !accepted)
                {
                    remaining.Add(item);
                    continue;
                }

                var receiptId = collector.Id + "|" + item.InstanceId;
                if (!processed.Add(receiptId))
                {
                    // A replayed already-processed ingredient cannot award twice.
                    continue;
                }
                receipts.Add(new IngredientCollectionReceipt(
                    receiptId, collector.Id, item.InstanceId, item.IngredientId));
            }

            return new IngredientCollectionPlan(remaining, receipts, processed);
        }

        public static bool IsReachable(BoardTopology topology,
            IEnumerable<BoardPosition> sources,
            IEnumerable<BoardPosition> sinks)
        {
            if (topology == null) throw new ArgumentNullException(nameof(topology));
            if (sources == null || sinks == null)
                throw new ArgumentNullException();
            var sourceList = sources.Distinct().ToArray();
            var sinkSet = new HashSet<BoardPosition>(sinks.Distinct());
            if (sourceList.Length == 0 || sinkSet.Count == 0)
                return false;
            foreach (var p in sourceList.Concat(sinkSet))
                if (!topology.Contains(p) || topology.IsHole(p) ||
                    topology.IsLocked(p))
                    return false;

            var queue = new Queue<BoardPosition>();
            var seen = new HashSet<BoardPosition>();
            foreach (var source in sourceList)
            {
                queue.Enqueue(source);
                seen.Add(source);
            }
            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                if (sinkSet.Contains(current)) return true;
                foreach (var next in Orthogonal(topology, current))
                    if (seen.Add(next)) queue.Enqueue(next);
            }
            return false;
        }

        private static IEnumerable<BoardPosition> Orthogonal(
            BoardTopology topology, BoardPosition p)
        {
            var candidates = new[]
            {
                p.Row > 0 ? new BoardPosition(p.Row - 1, p.Column, p.Layer) : p,
                p.Row + 1 < topology.Rows
                    ? new BoardPosition(p.Row + 1, p.Column, p.Layer) : p,
                p.Column > 0 ? new BoardPosition(p.Row, p.Column - 1, p.Layer) : p,
                p.Column + 1 < topology.Columns
                    ? new BoardPosition(p.Row, p.Column + 1, p.Layer) : p
            };
            foreach (var candidate in candidates.Distinct())
                if (!candidate.Equals(p) && topology.Contains(candidate) &&
                    !topology.IsHole(candidate) && !topology.IsLocked(candidate))
                    yield return candidate;
        }
    }

    public sealed class ObstacleMetadataEntry
    {
        public string Id { get; }
        public int MaxDurability { get; }
        public IReadOnlyList<string> Capabilities { get; }

        public ObstacleMetadataEntry(string id, int maxDurability,
            IEnumerable<string> capabilities)
        {
            if (string.IsNullOrWhiteSpace(id) || id.IndexOfAny(
                new[] { ';', '|', ',', '+', '=' }) >= 0)
                throw new ArgumentException("Invalid metadata obstacle ID.", nameof(id));
            if (maxDurability < 1 || maxDurability > 1024)
                throw new ArgumentOutOfRangeException(nameof(maxDurability));
            if (capabilities == null) throw new ArgumentNullException(nameof(capabilities));
            var caps = capabilities.Distinct(StringComparer.Ordinal)
                .OrderBy(x => x, StringComparer.Ordinal).ToList();
            if (caps.Any(x => string.IsNullOrWhiteSpace(x) ||
                x.IndexOfAny(new[] { ';', '|', ',', '+', '=' }) >= 0))
                throw new ArgumentException("Invalid obstacle capability.",
                    nameof(capabilities));
            Id = id;
            MaxDurability = maxDurability;
            Capabilities = new ReadOnlyCollection<string>(caps);
        }
    }

    public static class ObstacleMetadataSchema
    {
        public const int Version = 1;

        public static string Encode(IEnumerable<ObstacleMetadataEntry> entries)
        {
            if (entries == null) throw new ArgumentNullException(nameof(entries));
            var list = entries.ToList();
            if (list.Any(x => x == null))
                throw new ArgumentException("Null obstacle metadata.");
            if (list.GroupBy(x => x.Id, StringComparer.Ordinal).Any(x => x.Count() > 1))
                throw new ArgumentException("Duplicate obstacle metadata ID.");
            return "v1|" + string.Join(";", list
                .OrderBy(x => x.Id, StringComparer.Ordinal)
                .Select(x => x.Id + "," + x.MaxDurability + "," +
                    string.Join("+", x.Capabilities)));
        }

        public static IReadOnlyList<ObstacleMetadataEntry> Decode(string value)
        {
            if (string.IsNullOrWhiteSpace(value) ||
                !value.StartsWith("v1|", StringComparison.Ordinal))
                throw new FormatException("Unsupported obstacle metadata schema.");
            var body = value.Substring(3);
            if (body.Length == 0)
                return Array.AsReadOnly(new ObstacleMetadataEntry[0]);
            var entries = new List<ObstacleMetadataEntry>();
            foreach (var token in body.Split(';'))
            {
                var parts = token.Split(',');
                int hp;
                if (parts.Length != 3 || !Int32.TryParse(parts[1], out hp))
                    throw new FormatException("Malformed obstacle metadata entry.");
                var caps = parts[2].Length == 0
                    ? new string[0]
                    : parts[2].Split('+');
                entries.Add(new ObstacleMetadataEntry(parts[0], hp, caps));
            }
            // Re-encode validation catches duplicates and canonical restrictions.
            Encode(entries);
            return new ReadOnlyCollection<ObstacleMetadataEntry>(entries
                .OrderBy(x => x.Id, StringComparer.Ordinal).ToList());
        }
    }

    public sealed class ObstacleSkinBinding
    {
        public string GameId { get; }
        public IReadOnlyDictionary<string, string> PresentationKeys { get; }

        public ObstacleSkinBinding(string gameId,
            IDictionary<string, string> presentationKeys)
        {
            if (!GameProfileCatalog.IsKnownId(gameId))
                throw new ArgumentException("Unknown game profile.", nameof(gameId));
            if (presentationKeys == null || presentationKeys.Count == 0)
                throw new ArgumentException("Skin presentation map required.",
                    nameof(presentationKeys));
            if (presentationKeys.Any(x => string.IsNullOrWhiteSpace(x.Key) ||
                string.IsNullOrWhiteSpace(x.Value)))
                throw new ArgumentException("Skin map IDs and presentation keys required.");
            GameId = gameId;
            PresentationKeys = new ReadOnlyDictionary<string, string>(
                new Dictionary<string, string>(presentationKeys,
                    StringComparer.Ordinal));
        }
    }

    public static class FiveTitleObstacleSkinRegistry
    {
        public static IReadOnlyDictionary<string, ObstacleSkinBinding> Validate(
            IEnumerable<ObstacleSkinBinding> bindings,
            IEnumerable<string> requiredNeutralObstacleIds)
        {
            if (bindings == null) throw new ArgumentNullException(nameof(bindings));
            if (requiredNeutralObstacleIds == null)
                throw new ArgumentNullException(nameof(requiredNeutralObstacleIds));
            var list = bindings.ToList();
            var required = requiredNeutralObstacleIds.Distinct(StringComparer.Ordinal)
                .OrderBy(x => x, StringComparer.Ordinal).ToArray();
            if (required.Length == 0 || required.Any(string.IsNullOrWhiteSpace))
                throw new ArgumentException("Required neutral obstacle IDs missing.");
            if (list.Count != GameProfileCatalog.Ids.Count ||
                list.Select(x => x.GameId).Distinct(StringComparer.Ordinal).Count() !=
                    GameProfileCatalog.Ids.Count ||
                GameProfileCatalog.Ids.Any(id => !list.Any(x => x.GameId == id)))
                throw new InvalidOperationException(
                    "Exactly one obstacle skin binding per game profile is required.");
            foreach (var binding in list)
            {
                var keys = binding.PresentationKeys.Keys
                    .OrderBy(x => x, StringComparer.Ordinal).ToArray();
                if (!keys.SequenceEqual(required))
                    throw new InvalidOperationException(
                        "Every title must bind the identical neutral obstacle ID set.");
            }
            return new ReadOnlyDictionary<string, ObstacleSkinBinding>(
                list.ToDictionary(x => x.GameId, x => x, StringComparer.Ordinal));
        }
    }

    [Flags]
    public enum ObstacleCrossFeature
    {
        None = 0,
        Spreading = 1 << 0,
        Generator = 1 << 1,
        Countdown = 1 << 2,
        Egg = 1 << 3,
        Conveyor = 1 << 4,
        Portal = 1 << 5,
        Layered = 1 << 6,
        Immobilizer = 1 << 7,
        Targeted2x2 = 1 << 8,
        SpecialDamage = 1 << 9,
        IngredientExit = 1 << 10
    }

    public readonly struct ObstacleCrossProductCase
    {
        public string CellKey { get; }
        public ObstacleCrossFeature Features { get; }

        public ObstacleCrossProductCase(string cellKey,
            ObstacleCrossFeature features)
        {
            if (string.IsNullOrWhiteSpace(cellKey))
                throw new ArgumentException("Cross-product cell key required.",
                    nameof(cellKey));
            if ((features & ~AllFeatures) != 0)
                throw new ArgumentOutOfRangeException(nameof(features));
            CellKey = cellKey;
            Features = features;
        }

        private const ObstacleCrossFeature AllFeatures =
            ObstacleCrossFeature.Spreading |
            ObstacleCrossFeature.Generator |
            ObstacleCrossFeature.Countdown |
            ObstacleCrossFeature.Egg |
            ObstacleCrossFeature.Conveyor |
            ObstacleCrossFeature.Portal |
            ObstacleCrossFeature.Layered |
            ObstacleCrossFeature.Immobilizer |
            ObstacleCrossFeature.Targeted2x2 |
            ObstacleCrossFeature.SpecialDamage |
            ObstacleCrossFeature.IngredientExit;
    }

    public readonly struct ObstacleValidationError
    {
        public string CellKey { get; }
        public string Code { get; }

        public ObstacleValidationError(string cellKey, string code)
        {
            CellKey = cellKey;
            Code = code;
        }
    }

    public static class ObstacleCrossProductValidator
    {
        public static IReadOnlyList<ObstacleValidationError> Validate(
            IEnumerable<ObstacleCrossProductCase> cases)
        {
            if (cases == null) throw new ArgumentNullException(nameof(cases));
            var list = cases.ToList();
            if (list.GroupBy(x => x.CellKey, StringComparer.Ordinal).Any(x => x.Count() > 1))
                throw new ArgumentException("Duplicate cross-product cell declaration.");
            var errors = new List<ObstacleValidationError>();
            foreach (var item in list.OrderBy(x => x.CellKey, StringComparer.Ordinal))
            {
                var f = item.Features;
                RejectPair(item, f, ObstacleCrossFeature.Egg,
                    ObstacleCrossFeature.Countdown, "egg_countdown_same_cell", errors);
                RejectPair(item, f, ObstacleCrossFeature.Generator,
                    ObstacleCrossFeature.IngredientExit,
                    "generator_and_sink_same_cell", errors);
                RejectPair(item, f, ObstacleCrossFeature.Spreading,
                    ObstacleCrossFeature.IngredientExit,
                    "spreader_blocks_ingredient_sink", errors);
                RejectPair(item, f, ObstacleCrossFeature.Portal,
                    ObstacleCrossFeature.IngredientExit,
                    "portal_and_sink_phase_ambiguous", errors);
                // Conveyor+Portal is explicitly supported because M04 settlement
                // orders conveyor before portal. Layered/special/2x2/immobilizer
                // are also supported unless one of the explicit conflicts above applies.
            }
            return new ReadOnlyCollection<ObstacleValidationError>(errors);
        }

        public static void RequireSupported(IEnumerable<ObstacleCrossProductCase> cases)
        {
            var errors = Validate(cases);
            if (errors.Count > 0)
                throw new InvalidOperationException("Unsupported obstacle combination: " +
                    string.Join(",", errors.Select(x => x.CellKey + ":" + x.Code)));
        }

        private static void RejectPair(ObstacleCrossProductCase item,
            ObstacleCrossFeature features, ObstacleCrossFeature a,
            ObstacleCrossFeature b, string code,
            IList<ObstacleValidationError> errors)
        {
            if ((features & a) != 0 && (features & b) != 0)
                errors.Add(new ObstacleValidationError(item.CellKey, code));
        }
    }
}
