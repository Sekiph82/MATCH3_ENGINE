using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Match3.Engine.Runtime
{
    public enum CollectionItemKind
    {
        Card = 0,
        Species = 1,
        Vehicle = 2,
        Dino = 3,
        ColorToken = 4
    }

    public sealed class CollectionItemDefinition
    {
        public string ItemId { get; }
        public string SetId { get; }
        public CollectionItemKind Kind { get; }
        public string SeasonId { get; }

        public CollectionItemDefinition(string itemId, string setId,
            CollectionItemKind kind, string seasonId = null)
        {
            if (string.IsNullOrWhiteSpace(itemId))
                throw new ArgumentException("Collection item ID required.", nameof(itemId));
            if (string.IsNullOrWhiteSpace(setId))
                throw new ArgumentException("Collection set ID required.", nameof(setId));
            if (!Enum.IsDefined(typeof(CollectionItemKind), kind))
                throw new ArgumentOutOfRangeException(nameof(kind));
            if (seasonId != null && string.IsNullOrWhiteSpace(seasonId))
                throw new ArgumentException("Season ID cannot be blank.", nameof(seasonId));
            ItemId = itemId;
            SetId = setId;
            Kind = kind;
            SeasonId = seasonId;
        }
    }

    public sealed class CollectionSetDefinition
    {
        private readonly ReadOnlyCollection<string> _itemIds;

        public string SetId { get; }
        public string SeasonId { get; }
        public string FixedRewardSourceId { get; }
        public IReadOnlyList<string> ItemIds => _itemIds;

        public CollectionSetDefinition(string setId,
            IEnumerable<string> itemIds, string fixedRewardSourceId,
            string seasonId = null)
        {
            if (string.IsNullOrWhiteSpace(setId))
                throw new ArgumentException("Collection set ID required.", nameof(setId));
            if (string.IsNullOrWhiteSpace(fixedRewardSourceId))
                throw new ArgumentException("Fixed reward source ID required.",
                    nameof(fixedRewardSourceId));
            if (seasonId != null && string.IsNullOrWhiteSpace(seasonId))
                throw new ArgumentException("Season ID cannot be blank.", nameof(seasonId));
            var ids = (itemIds ?? throw new ArgumentNullException(nameof(itemIds)))
                .Take(257).ToList();
            if (ids.Count < 1 || ids.Count > 256 ||
                ids.Any(string.IsNullOrWhiteSpace) ||
                ids.Distinct(StringComparer.Ordinal).Count() != ids.Count)
                throw new ArgumentException(
                    "Collection set needs 1..256 unique item IDs.", nameof(itemIds));
            SetId = setId;
            SeasonId = seasonId;
            FixedRewardSourceId = fixedRewardSourceId;
            _itemIds = new ReadOnlyCollection<string>(
                ids.OrderBy(x => x, StringComparer.Ordinal).ToList());
        }
    }

    public sealed class CollectionCatalog
    {
        private readonly ReadOnlyDictionary<string, CollectionItemDefinition> _items;
        private readonly ReadOnlyDictionary<string, CollectionSetDefinition> _sets;

        public string GameId { get; }
        public IReadOnlyDictionary<string, CollectionItemDefinition> Items => _items;
        public IReadOnlyDictionary<string, CollectionSetDefinition> Sets => _sets;

        public CollectionCatalog(string gameId,
            IEnumerable<CollectionItemDefinition> items,
            IEnumerable<CollectionSetDefinition> sets)
        {
            if (!GameProfileCatalog.IsKnownId(gameId))
                throw new ArgumentException("Unknown game ID.", nameof(gameId));
            var itemArray = (items ?? throw new ArgumentNullException(nameof(items)))
                .Take(4097).ToArray();
            var setArray = (sets ?? throw new ArgumentNullException(nameof(sets)))
                .Take(257).ToArray();
            if (itemArray.Length < 1 || itemArray.Length > 4096 ||
                itemArray.Any(x => x == null) ||
                itemArray.GroupBy(x => x.ItemId, StringComparer.Ordinal)
                    .Any(x => x.Count() > 1))
                throw new ArgumentException("Invalid collection item catalog.", nameof(items));
            if (setArray.Length < 1 || setArray.Length > 256 ||
                setArray.Any(x => x == null) ||
                setArray.GroupBy(x => x.SetId, StringComparer.Ordinal)
                    .Any(x => x.Count() > 1) ||
                setArray.GroupBy(
                    x => x.FixedRewardSourceId, StringComparer.Ordinal)
                    .Any(x => x.Count() > 1))
                throw new ArgumentException(
                    "Invalid collection set catalog or duplicate fixed reward source.",
                    nameof(sets));

            var itemMap = itemArray.ToDictionary(x => x.ItemId, x => x,
                StringComparer.Ordinal);
            var setMap = setArray.ToDictionary(x => x.SetId, x => x,
                StringComparer.Ordinal);

            foreach (var item in itemArray)
            {
                CollectionSetDefinition set;
                if (!setMap.TryGetValue(item.SetId, out set) ||
                    !set.ItemIds.Contains(item.ItemId))
                    throw new ArgumentException(
                        "Collection item/set references are inconsistent.");
                if (!StringComparer.Ordinal.Equals(item.SeasonId, set.SeasonId))
                    throw new ArgumentException(
                        "Collection item season must match its set season.");
            }

            foreach (var set in setArray)
            {
                foreach (var itemId in set.ItemIds)
                {
                    CollectionItemDefinition item;
                    if (!itemMap.TryGetValue(itemId, out item) ||
                        !StringComparer.Ordinal.Equals(item.SetId, set.SetId))
                        throw new ArgumentException(
                            "Collection set references missing or foreign item.");
                }
            }

            GameId = gameId;
            _items = new ReadOnlyDictionary<string, CollectionItemDefinition>(itemMap);
            _sets = new ReadOnlyDictionary<string, CollectionSetDefinition>(setMap);
        }

        public IReadOnlyList<string> VisibleSetIds(string activeSeasonId)
        {
            return _sets.Values
                .Where(x => x.SeasonId == null ||
                    StringComparer.Ordinal.Equals(x.SeasonId, activeSeasonId))
                .Select(x => x.SetId)
                .OrderBy(x => x, StringComparer.Ordinal)
                .ToArray();
        }
    }

    public sealed class CollectionSnapshot
    {
        private readonly ReadOnlyDictionary<string, int> _counts;
        private readonly ReadOnlyCollection<string> _processedEarnIds;
        private readonly ReadOnlyCollection<string> _completedSetIds;

        public string GameId { get; }
        public IReadOnlyDictionary<string, int> Counts => _counts;
        public IReadOnlyList<string> ProcessedEarnIds => _processedEarnIds;
        public IReadOnlyList<string> CompletedSetIds => _completedSetIds;

        public CollectionSnapshot(string gameId,
            IEnumerable<KeyValuePair<string, int>> counts = null,
            IEnumerable<string> processedEarnIds = null,
            IEnumerable<string> completedSetIds = null)
        {
            if (!GameProfileCatalog.IsKnownId(gameId))
                throw new ArgumentException("Unknown game ID.", nameof(gameId));
            GameId = gameId;
            var map = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var pair in counts ??
                Enumerable.Empty<KeyValuePair<string, int>>())
            {
                if (string.IsNullOrWhiteSpace(pair.Key) ||
                    pair.Value < 0 || pair.Value > 1000000 ||
                    map.ContainsKey(pair.Key))
                    throw new ArgumentException("Invalid collection count state.");
                map.Add(pair.Key, pair.Value);
            }
            _counts = new ReadOnlyDictionary<string, int>(map);
            _processedEarnIds = new ReadOnlyCollection<string>(
                Freeze(processedEarnIds, "collection earn"));
            _completedSetIds = new ReadOnlyCollection<string>(
                Freeze(completedSetIds, "completed set"));
        }

        private static List<string> Freeze(IEnumerable<string> ids, string label)
        {
            var list = (ids ?? Enumerable.Empty<string>())
                .Take(100001).ToList();
            if (list.Count > 100000 ||
                list.Any(string.IsNullOrWhiteSpace) ||
                list.Distinct(StringComparer.Ordinal).Count() != list.Count)
                throw new ArgumentException("Invalid " + label + " IDs.");
            return list.OrderBy(x => x, StringComparer.Ordinal).ToList();
        }
    }

    public sealed class CollectionEarnPlan
    {
        private readonly ReadOnlyCollection<string> _newlyCompletedSetIds;

        public CollectionSnapshot Result { get; }
        public bool Applied { get; }
        public bool IsNewItem { get; }
        public bool IsDuplicate { get; }
        public IReadOnlyList<string> NewlyCompletedSetIds => _newlyCompletedSetIds;
        public bool MasterComplete { get; }

        internal CollectionEarnPlan(CollectionSnapshot result, bool applied,
            bool isNewItem, bool isDuplicate,
            IEnumerable<string> newlyCompletedSetIds, bool masterComplete)
        {
            Result = result;
            Applied = applied;
            IsNewItem = isNewItem;
            IsDuplicate = isDuplicate;
            _newlyCompletedSetIds = new ReadOnlyCollection<string>(
                newlyCompletedSetIds.OrderBy(x => x, StringComparer.Ordinal).ToList());
            MasterComplete = masterComplete;
        }
    }

    public static class CollectionPlanner
    {
        public static CollectionEarnPlan Earn(CollectionCatalog catalog,
            CollectionSnapshot snapshot, string earnId, string itemId)
        {
            if (catalog == null) throw new ArgumentNullException(nameof(catalog));
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            if (string.IsNullOrWhiteSpace(earnId))
                throw new ArgumentException("Collection earn ID required.", nameof(earnId));
            if (!catalog.Items.ContainsKey(itemId))
                throw new ArgumentException("Unknown collection item.", nameof(itemId));

            if (!StringComparer.Ordinal.Equals(catalog.GameId, snapshot.GameId))
                throw new InvalidOperationException(
                    "Collection snapshot belongs to another game.");
            ValidateSnapshot(catalog, snapshot);
            if (snapshot.ProcessedEarnIds.Contains(earnId))
                return new CollectionEarnPlan(snapshot, false, false, false,
                    new string[0],
                    snapshot.CompletedSetIds.Count == catalog.Sets.Count);

            var counts = snapshot.Counts.ToDictionary(
                x => x.Key, x => x.Value, StringComparer.Ordinal);
            int before;
            counts.TryGetValue(itemId, out before);
            if (before >= 1000000)
                throw new InvalidOperationException("Collection duplicate count limit exceeded.");
            counts[itemId] = before + 1;

            var completed = new HashSet<string>(
                snapshot.CompletedSetIds, StringComparer.Ordinal);
            var newly = new List<string>();
            var set = catalog.Sets[catalog.Items[itemId].SetId];
            if (!completed.Contains(set.SetId) &&
                set.ItemIds.All(x => counts.ContainsKey(x) && counts[x] > 0))
            {
                completed.Add(set.SetId);
                newly.Add(set.SetId);
            }

            var result = new CollectionSnapshot(
                snapshot.GameId,
                counts,
                snapshot.ProcessedEarnIds.Concat(new[] { earnId }),
                completed);
            return new CollectionEarnPlan(
                result, true, before == 0, before > 0, newly,
                completed.Count == catalog.Sets.Count);
        }

        private static void ValidateSnapshot(
            CollectionCatalog catalog, CollectionSnapshot snapshot)
        {
            if (snapshot.Counts.Keys.Any(x => !catalog.Items.ContainsKey(x)) ||
                snapshot.CompletedSetIds.Any(x => !catalog.Sets.ContainsKey(x)))
                throw new ArgumentException(
                    "Collection snapshot references unknown catalog IDs.");

            foreach (var setId in snapshot.CompletedSetIds)
            {
                var set = catalog.Sets[setId];
                if (!set.ItemIds.All(x =>
                    snapshot.Counts.ContainsKey(x) && snapshot.Counts[x] > 0))
                    throw new ArgumentException(
                        "Collection snapshot marks incomplete set complete.");
            }
        }
    }

    public sealed class EarnedPackDefinition
    {
        private readonly ReadOnlyCollection<string> _itemIds;

        public string PackId { get; }
        public int DrawCount { get; }
        public int PityThreshold { get; }
        public IReadOnlyList<string> ItemIds => _itemIds;

        public EarnedPackDefinition(string packId, IEnumerable<string> itemIds,
            int drawCount, int pityThreshold)
        {
            if (string.IsNullOrWhiteSpace(packId))
                throw new ArgumentException("Pack ID required.", nameof(packId));
            var ids = (itemIds ?? throw new ArgumentNullException(nameof(itemIds)))
                .Take(1025).ToList();
            if (ids.Count < 1 || ids.Count > 1024 ||
                ids.Any(string.IsNullOrWhiteSpace) ||
                ids.Distinct(StringComparer.Ordinal).Count() != ids.Count)
                throw new ArgumentException("Pack pool requires unique item IDs.", nameof(itemIds));
            if (drawCount < 1 || drawCount > 64)
                throw new ArgumentOutOfRangeException(nameof(drawCount));
            if (pityThreshold < 0 || pityThreshold > 1000)
                throw new ArgumentOutOfRangeException(nameof(pityThreshold));
            PackId = packId;
            DrawCount = drawCount;
            PityThreshold = pityThreshold;
            _itemIds = new ReadOnlyCollection<string>(
                ids.OrderBy(x => x, StringComparer.Ordinal).ToList());
        }
    }

    public sealed class EarnedPackSnapshot
    {
        private readonly ReadOnlyCollection<string> _processedReceiptIds;

        public string GameId { get; }
        public string PackId { get; }
        public int PityCounter { get; }
        public IReadOnlyList<string> ProcessedReceiptIds => _processedReceiptIds;

        public EarnedPackSnapshot(string gameId, string packId,
            int pityCounter = 0,
            IEnumerable<string> processedReceiptIds = null)
        {
            if (!GameProfileCatalog.IsKnownId(gameId))
                throw new ArgumentException("Unknown pack game ID.", nameof(gameId));
            if (string.IsNullOrWhiteSpace(packId))
                throw new ArgumentException("Pack ID required.", nameof(packId));
            if (pityCounter < 0 || pityCounter > 1000)
                throw new ArgumentOutOfRangeException(nameof(pityCounter));
            GameId = gameId;
            PackId = packId;
            PityCounter = pityCounter;
            var ids = (processedReceiptIds ?? Enumerable.Empty<string>())
                .Take(100001).ToList();
            if (ids.Count > 100000 ||
                ids.Any(string.IsNullOrWhiteSpace) ||
                ids.Distinct(StringComparer.Ordinal).Count() != ids.Count)
                throw new ArgumentException("Invalid pack receipt IDs.");
            _processedReceiptIds = new ReadOnlyCollection<string>(
                ids.OrderBy(x => x, StringComparer.Ordinal).ToList());
        }
    }

    public sealed class EarnedPackReceipt
    {
        public string ReceiptId { get; }
        public string GameId { get; }
        public string PackId { get; }
        public RewardSourceKind SourceKind { get; }
        public bool Verified { get; }
        public bool IsChildProfile { get; }

        public EarnedPackReceipt(string receiptId, string gameId,
            string packId, RewardSourceKind sourceKind,
            bool verified = true, bool isChildProfile = false)
        {
            if (string.IsNullOrWhiteSpace(receiptId))
                throw new ArgumentException(
                    "Earned pack receipt ID required.", nameof(receiptId));
            if (!GameProfileCatalog.IsKnownId(gameId))
                throw new ArgumentException(
                    "Unknown earned-pack game ID.", nameof(gameId));
            if (string.IsNullOrWhiteSpace(packId))
                throw new ArgumentException(
                    "Earned pack ID required.", nameof(packId));
            if (!Enum.IsDefined(typeof(RewardSourceKind), sourceKind))
                throw new ArgumentOutOfRangeException(nameof(sourceKind));
            if (sourceKind == RewardSourceKind.Purchase ||
                sourceKind == RewardSourceKind.Replay ||
                sourceKind == RewardSourceKind.RewardedVerified ||
                sourceKind == RewardSourceKind.Pack)
                throw new ArgumentException(
                    "Random pack opening cannot originate from paid, replay, ad-randomized or recursive pack sources.",
                    nameof(sourceKind));
            ReceiptId = receiptId;
            GameId = gameId;
            PackId = packId;
            SourceKind = sourceKind;
            Verified = verified;
            IsChildProfile = isChildProfile;
        }
    }

    public sealed class EarnedPackPlan
    {
        private readonly ReadOnlyCollection<string> _itemIds;

        public EarnedPackSnapshot Result { get; }
        public bool Applied { get; }
        public IReadOnlyList<string> ItemIds => _itemIds;
        public bool MissingItemGuaranteeUsed { get; }

        internal EarnedPackPlan(EarnedPackSnapshot result, bool applied,
            IEnumerable<string> itemIds, bool guaranteeUsed)
        {
            Result = result;
            Applied = applied;
            _itemIds = new ReadOnlyCollection<string>(itemIds.ToList());
            MissingItemGuaranteeUsed = guaranteeUsed;
        }
    }

    public static class EarnedPackPlanner
    {
        public static EarnedPackPlan Open(
            CollectionCatalog catalog, CollectionSnapshot collection,
            EarnedPackDefinition definition, EarnedPackSnapshot snapshot,
            EarnedPackReceipt receipt, uint seed)
        {
            if (catalog == null) throw new ArgumentNullException(nameof(catalog));
            if (collection == null) throw new ArgumentNullException(nameof(collection));
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            if (receipt == null)
                throw new ArgumentNullException(nameof(receipt));
            if (!receipt.Verified)
                throw new InvalidOperationException(
                    "Unverified earned receipt cannot open a pack.");
            if (!StringComparer.Ordinal.Equals(catalog.GameId, collection.GameId) ||
                !StringComparer.Ordinal.Equals(catalog.GameId, snapshot.GameId) ||
                !StringComparer.Ordinal.Equals(catalog.GameId, receipt.GameId))
                throw new InvalidOperationException(
                    "Pack catalog, collection, pity state and receipt must belong to the same game.");
            if (!StringComparer.Ordinal.Equals(snapshot.PackId, definition.PackId) ||
                !StringComparer.Ordinal.Equals(
                    receipt.PackId, definition.PackId))
                throw new InvalidOperationException(
                    "Earned receipt belongs to another pack.");
            if (seed == 0)
                throw new ArgumentOutOfRangeException(nameof(seed));
            if (definition.ItemIds.Any(x => !catalog.Items.ContainsKey(x)))
                throw new ArgumentException("Pack pool references unknown collection item.");

            if (snapshot.ProcessedReceiptIds.Contains(receipt.ReceiptId))
                return new EarnedPackPlan(snapshot, false, new string[0], false);

            var draws = new List<string>();
            var missing = definition.ItemIds
                .Where(x => !collection.Counts.ContainsKey(x) ||
                    collection.Counts[x] == 0)
                .OrderBy(x => x, StringComparer.Ordinal)
                .ToList();
            var guarantee = false;

            if (receipt.IsChildProfile)
            {
                for (var i = 0; i < definition.DrawCount; i++)
                    draws.Add(definition.ItemIds[
                        i % definition.ItemIds.Count]);
            }
            else
            {
                var rng = new SeededRandom(seed);
                guarantee = definition.PityThreshold > 0 &&
                    snapshot.PityCounter >= definition.PityThreshold &&
                    missing.Count > 0;

                if (guarantee)
                    draws.Add(missing[rng.NextIndex(missing.Count)]);

                while (draws.Count < definition.DrawCount)
                    draws.Add(definition.ItemIds[
                        rng.NextIndex(definition.ItemIds.Count)]);
            }

            var hasNew = draws.Any(x =>
                !collection.Counts.ContainsKey(x) || collection.Counts[x] == 0);
            var pity = hasNew
                ? 0
                : Math.Min(1000, snapshot.PityCounter + 1);

            return new EarnedPackPlan(
                new EarnedPackSnapshot(
                    snapshot.GameId,
                    snapshot.PackId,
                    pity,
                    snapshot.ProcessedReceiptIds.Concat(
                        new[] { receipt.ReceiptId })),
                true, draws, guarantee);
        }
    }

    public sealed class JourneyDefinition
    {
        private readonly ReadOnlyCollection<string> _levelIds;

        public string GameId { get; }
        public string ProfileId { get; }
        public string ModeId { get; }
        public IReadOnlyList<string> LevelIds => _levelIds;

        public JourneyDefinition(string gameId, string profileId,
            string modeId, IEnumerable<string> levelIds)
        {
            if (!GameProfileCatalog.IsKnownId(gameId))
                throw new ArgumentException("Unknown game ID.", nameof(gameId));
            if (string.IsNullOrWhiteSpace(profileId) ||
                string.IsNullOrWhiteSpace(modeId) ||
                profileId.Contains("|") || modeId.Contains("|"))
                throw new ArgumentException(
                    "Journey profile/mode IDs required and cannot contain '|'.");
            var ids = (levelIds ?? throw new ArgumentNullException(nameof(levelIds)))
                .ToList();
            if (ids.Count != 10 ||
                ids.Any(x => string.IsNullOrWhiteSpace(x) || x.Contains("|")) ||
                ids.Distinct(StringComparer.Ordinal).Count() != 10)
                throw new ArgumentException(
                    "Journey strip requires exactly ten unique level IDs.",
                    nameof(levelIds));
            GameId = gameId;
            ProfileId = profileId;
            ModeId = modeId;
            _levelIds = new ReadOnlyCollection<string>(ids);
        }
    }

    public sealed class PersonalBestRecord
    {
        public string Key { get; }
        public int Score { get; }
        public string ResultId { get; }

        public PersonalBestRecord(string key, int score, string resultId)
        {
            if (string.IsNullOrWhiteSpace(key) ||
                string.IsNullOrWhiteSpace(resultId))
                throw new ArgumentException("Personal-best IDs required.");
            if (score < 0)
                throw new ArgumentOutOfRangeException(nameof(score));
            Key = key;
            Score = score;
            ResultId = resultId;
        }
    }

    public sealed class JourneySnapshot
    {
        private readonly ReadOnlyCollection<string> _completedLevelIds;
        private readonly ReadOnlyCollection<string> _firstTryWinLevelIds;
        private readonly ReadOnlyCollection<string> _processedResultIds;
        private readonly ReadOnlyDictionary<string, PersonalBestRecord> _personalBests;

        public IReadOnlyList<string> CompletedLevelIds => _completedLevelIds;
        public IReadOnlyList<string> FirstTryWinLevelIds => _firstTryWinLevelIds;
        public IReadOnlyList<string> ProcessedResultIds => _processedResultIds;
        public IReadOnlyDictionary<string, PersonalBestRecord> PersonalBests => _personalBests;

        public JourneySnapshot(
            IEnumerable<string> completedLevelIds = null,
            IEnumerable<string> firstTryWinLevelIds = null,
            IEnumerable<string> processedResultIds = null,
            IEnumerable<PersonalBestRecord> personalBests = null)
        {
            _completedLevelIds = new ReadOnlyCollection<string>(
                Freeze(completedLevelIds, "completed journey"));
            _firstTryWinLevelIds = new ReadOnlyCollection<string>(
                Freeze(firstTryWinLevelIds, "first-try journey"));
            _processedResultIds = new ReadOnlyCollection<string>(
                Freeze(processedResultIds, "journey result"));

            var map = new Dictionary<string, PersonalBestRecord>(
                StringComparer.Ordinal);
            foreach (var record in personalBests ??
                Enumerable.Empty<PersonalBestRecord>())
            {
                if (record == null || map.ContainsKey(record.Key))
                    throw new ArgumentException("Invalid personal-best records.");
                map.Add(record.Key, record);
            }
            _personalBests =
                new ReadOnlyDictionary<string, PersonalBestRecord>(map);
        }

        private static List<string> Freeze(IEnumerable<string> ids, string label)
        {
            var list = (ids ?? Enumerable.Empty<string>())
                .Take(100001).ToList();
            if (list.Count > 100000 ||
                list.Any(string.IsNullOrWhiteSpace) ||
                list.Distinct(StringComparer.Ordinal).Count() != list.Count)
                throw new ArgumentException("Invalid " + label + " IDs.");
            return list.OrderBy(x => x, StringComparer.Ordinal).ToList();
        }
    }

    public sealed class JourneyPlan
    {
        public JourneySnapshot Result { get; }
        public bool Applied { get; }
        public bool FirstTryWinAdded { get; }
        public bool PersonalBestImproved { get; }

        internal JourneyPlan(JourneySnapshot result, bool applied,
            bool firstTryWinAdded, bool personalBestImproved)
        {
            Result = result;
            Applied = applied;
            FirstTryWinAdded = firstTryWinAdded;
            PersonalBestImproved = personalBestImproved;
        }
    }

    public static class JourneyPlanner
    {
        public static JourneyPlan ApplyResult(JourneyDefinition definition,
            JourneySnapshot snapshot, VerifiedProgressionResult result)
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            if (result == null) throw new ArgumentNullException(nameof(result));
            if (!result.Verified)
                throw new InvalidOperationException(
                    "Unverified result cannot mutate journey.");
            if (!StringComparer.Ordinal.Equals(definition.GameId, result.GameId) ||
                !StringComparer.Ordinal.Equals(definition.ProfileId, result.ProfileId) ||
                !StringComparer.Ordinal.Equals(definition.ModeId, result.ModeId))
                throw new InvalidOperationException(
                    "Journey result belongs to another game/profile/mode.");
            if (!definition.LevelIds.Contains(result.LevelId))
                throw new ArgumentException(
                    "Journey result references level outside strip.");
            if (snapshot.CompletedLevelIds.Any(x => !definition.LevelIds.Contains(x)) ||
                snapshot.FirstTryWinLevelIds.Any(x => !definition.LevelIds.Contains(x)))
                throw new ArgumentException(
                    "Journey snapshot references level outside strip.");
            if (snapshot.ProcessedResultIds.Contains(result.ResultId))
                return new JourneyPlan(snapshot, false, false, false);

            var completed = new HashSet<string>(
                snapshot.CompletedLevelIds, StringComparer.Ordinal);
            var firstTry = new HashSet<string>(
                snapshot.FirstTryWinLevelIds, StringComparer.Ordinal);
            var bests = snapshot.PersonalBests.ToDictionary(
                x => x.Key, x => x.Value, StringComparer.Ordinal);

            if (result.Won)
                completed.Add(result.LevelId);

            var firstTryAdded =
                result.Won && result.FirstClear && !result.Replay &&
                result.AttemptOrdinal == 1 &&
                firstTry.Add(result.LevelId);

            var key = result.GameId + "|" + result.ProfileId + "|" +
                result.ModeId + "|" + result.LevelId + "|" +
                result.CanonicalWeekId;
            PersonalBestRecord prior;
            var improved = !bests.TryGetValue(key, out prior) ||
                result.Score > prior.Score;
            if (improved)
                bests[key] = new PersonalBestRecord(
                    key, result.Score, result.ResultId);

            return new JourneyPlan(
                new JourneySnapshot(
                    completed,
                    firstTry,
                    snapshot.ProcessedResultIds.Concat(
                        new[] { result.ResultId }),
                    bests.Values),
                true, firstTryAdded, improved);
        }

        public static string UpcomingLevel(
            JourneyDefinition definition, JourneySnapshot snapshot)
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            if (snapshot.CompletedLevelIds.Any(x =>
                    !definition.LevelIds.Contains(x)) ||
                snapshot.FirstTryWinLevelIds.Any(x =>
                    !definition.LevelIds.Contains(x)))
                throw new ArgumentException(
                    "Journey snapshot references level outside strip.");
            return definition.LevelIds.FirstOrDefault(
                x => !snapshot.CompletedLevelIds.Contains(x));
        }
    }

    public sealed class FailRecoveryPolicy
    {
        public int FailureThreshold { get; }

        public FailRecoveryPolicy(int failureThreshold = 3)
        {
            if (failureThreshold < 1 || failureThreshold > 100)
                throw new ArgumentOutOfRangeException(nameof(failureThreshold));
            FailureThreshold = failureThreshold;
        }
    }

    public sealed class BoosterRecommendationCandidate
    {
        public string BoosterId { get; }
        public int Rank { get; }
        public int OwnedCharges { get; }
        public bool RewardedEligible { get; }
        public bool PurchaseEligible { get; }

        public BoosterRecommendationCandidate(string boosterId,
            int rank, int ownedCharges, bool rewardedEligible,
            bool purchaseEligible)
        {
            if (string.IsNullOrWhiteSpace(boosterId))
                throw new ArgumentException("Booster ID required.", nameof(boosterId));
            if (rank < 0 || rank > 1000000)
                throw new ArgumentOutOfRangeException(nameof(rank));
            if (ownedCharges < 0 || ownedCharges > 1000000)
                throw new ArgumentOutOfRangeException(nameof(ownedCharges));
            BoosterId = boosterId;
            Rank = rank;
            OwnedCharges = ownedCharges;
            RewardedEligible = rewardedEligible;
            PurchaseEligible = purchaseEligible;
        }
    }

    public sealed class FailRecoveryRecommendation
    {
        public string BoosterId { get; }
        public bool OwnedReady { get; }
        public bool RewardedOfferAvailable { get; }
        public bool PurchaseOfferAvailable { get; }

        internal FailRecoveryRecommendation(string boosterId,
            bool ownedReady, bool rewardedOfferAvailable,
            bool purchaseOfferAvailable)
        {
            BoosterId = boosterId;
            OwnedReady = ownedReady;
            RewardedOfferAvailable = rewardedOfferAvailable;
            PurchaseOfferAvailable = purchaseOfferAvailable;
        }
    }

    public sealed class FailRecoverySnapshot
    {
        private readonly ReadOnlyCollection<string> _processedResultIds;

        public string GameId { get; }
        public string ProfileId { get; }
        public string ModeId { get; }
        public string FrontierLevelId { get; }
        public int ConsecutiveFailures { get; }
        public IReadOnlyList<string> ProcessedResultIds => _processedResultIds;

        public FailRecoverySnapshot(string gameId, string profileId,
            string modeId, string frontierLevelId,
            int consecutiveFailures = 0,
            IEnumerable<string> processedResultIds = null)
        {
            if (!GameProfileCatalog.IsKnownId(gameId))
                throw new ArgumentException("Unknown game ID.", nameof(gameId));
            if (string.IsNullOrWhiteSpace(profileId) ||
                string.IsNullOrWhiteSpace(modeId) ||
                string.IsNullOrWhiteSpace(frontierLevelId))
                throw new ArgumentException("Recovery identity required.");
            if (consecutiveFailures < 0 || consecutiveFailures > 100000)
                throw new ArgumentOutOfRangeException(nameof(consecutiveFailures));
            GameId = gameId;
            ProfileId = profileId;
            ModeId = modeId;
            FrontierLevelId = frontierLevelId;
            ConsecutiveFailures = consecutiveFailures;
            _processedResultIds = new ReadOnlyCollection<string>(
                Freeze(processedResultIds));
        }

        private static List<string> Freeze(IEnumerable<string> ids)
        {
            var list = (ids ?? Enumerable.Empty<string>())
                .Take(100001).ToList();
            if (list.Count > 100000 ||
                list.Any(string.IsNullOrWhiteSpace) ||
                list.Distinct(StringComparer.Ordinal).Count() != list.Count)
                throw new ArgumentException("Invalid recovery result IDs.");
            return list.OrderBy(x => x, StringComparer.Ordinal).ToList();
        }
    }

    public sealed class FailRecoveryPlan
    {
        private readonly ReadOnlyCollection<FailRecoveryRecommendation> _recommendations;

        public FailRecoverySnapshot Result { get; }
        public bool Applied { get; }
        public IReadOnlyList<FailRecoveryRecommendation> Recommendations =>
            _recommendations;

        internal FailRecoveryPlan(FailRecoverySnapshot result, bool applied,
            IEnumerable<FailRecoveryRecommendation> recommendations)
        {
            Result = result;
            Applied = applied;
            _recommendations = new ReadOnlyCollection<FailRecoveryRecommendation>(
                recommendations.ToList());
        }
    }

    public static class FailRecoveryPlanner
    {
        public static FailRecoveryPlan ApplyResult(
            FailRecoverySnapshot snapshot,
            VerifiedProgressionResult result,
            FailRecoveryPolicy policy,
            IEnumerable<BoosterRecommendationCandidate> candidates,
            bool rewardedProviderAvailable)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            if (result == null) throw new ArgumentNullException(nameof(result));
            if (policy == null) throw new ArgumentNullException(nameof(policy));
            if (candidates == null) throw new ArgumentNullException(nameof(candidates));
            if (!result.Verified)
                throw new InvalidOperationException(
                    "Unverified result cannot mutate recovery state.");
            if (!StringComparer.Ordinal.Equals(snapshot.GameId, result.GameId) ||
                !StringComparer.Ordinal.Equals(snapshot.ProfileId, result.ProfileId) ||
                !StringComparer.Ordinal.Equals(snapshot.ModeId, result.ModeId))
                throw new InvalidOperationException(
                    "Recovery result belongs to another game/profile/mode.");
            if (snapshot.ProcessedResultIds.Contains(result.ResultId))
                return new FailRecoveryPlan(snapshot, false, new FailRecoveryRecommendation[0]);

            var frontierLevelId = snapshot.FrontierLevelId;
            var failures = snapshot.ConsecutiveFailures;
            if (!StringComparer.Ordinal.Equals(
                frontierLevelId, result.FrontierLevelId))
            {
                frontierLevelId = result.FrontierLevelId;
                failures = 0;
            }

            if (!result.Replay && result.IsFrontier &&
                StringComparer.Ordinal.Equals(
                    frontierLevelId, result.LevelId))
            {
                failures = result.Won ? 0 : Math.Min(100000, failures + 1);
            }

            var next = new FailRecoverySnapshot(
                snapshot.GameId,
                snapshot.ProfileId,
                snapshot.ModeId,
                frontierLevelId,
                failures,
                snapshot.ProcessedResultIds.Concat(
                    new[] { result.ResultId }));

            var recommendations = new List<FailRecoveryRecommendation>();
            var genuineFrontierFailure =
                !result.Replay && !result.Won && result.IsFrontier &&
                StringComparer.Ordinal.Equals(
                    frontierLevelId, result.LevelId);
            if (failures >= policy.FailureThreshold &&
                genuineFrontierFailure)
            {
                var list = candidates.Take(257).ToArray();
                if (list.Length > 256 || list.Any(x => x == null) ||
                    list.GroupBy(x => x.BoosterId, StringComparer.Ordinal)
                        .Any(x => x.Count() > 1))
                    throw new ArgumentException(
                        "Invalid recovery booster candidates.");
                foreach (var candidate in list
                    .Where(x => x.OwnedCharges > 0 ||
                        x.PurchaseEligible ||
                        (x.RewardedEligible && rewardedProviderAvailable))
                    .OrderBy(x => x.Rank)
                    .ThenBy(x => x.BoosterId, StringComparer.Ordinal)
                    .Take(2))
                {
                    recommendations.Add(
                        new FailRecoveryRecommendation(
                            candidate.BoosterId,
                            candidate.OwnedCharges > 0,
                            candidate.OwnedCharges == 0 &&
                                candidate.RewardedEligible &&
                                rewardedProviderAvailable,
                            candidate.OwnedCharges == 0 &&
                                candidate.PurchaseEligible));
                }
            }

            return new FailRecoveryPlan(next, true, recommendations);
        }
    }

    public sealed class AchievementDefinition
    {
        public string AchievementId { get; }
        public int RequiredCount { get; }
        public string AppleGameCenterId { get; }
        public string GooglePlayGamesId { get; }

        public AchievementDefinition(string achievementId, int requiredCount,
            string appleGameCenterId = null, string googlePlayGamesId = null)
        {
            if (string.IsNullOrWhiteSpace(achievementId))
                throw new ArgumentException("Achievement ID required.",
                    nameof(achievementId));
            if (requiredCount < 1 || requiredCount > 1000000)
                throw new ArgumentOutOfRangeException(nameof(requiredCount));
            if (appleGameCenterId != null &&
                string.IsNullOrWhiteSpace(appleGameCenterId))
                throw new ArgumentException("Apple achievement ID cannot be blank.");
            if (googlePlayGamesId != null &&
                string.IsNullOrWhiteSpace(googlePlayGamesId))
                throw new ArgumentException("Google achievement ID cannot be blank.");
            AchievementId = achievementId;
            RequiredCount = requiredCount;
            AppleGameCenterId = appleGameCenterId;
            GooglePlayGamesId = googlePlayGamesId;
        }
    }

    public sealed class AchievementProgressEvent
    {
        public string EventId { get; }
        public string AchievementId { get; }
        public int Delta { get; }
        public bool Verified { get; }

        public AchievementProgressEvent(string eventId,
            string achievementId, int delta, bool verified = true)
        {
            if (string.IsNullOrWhiteSpace(eventId) ||
                string.IsNullOrWhiteSpace(achievementId))
                throw new ArgumentException("Achievement event IDs required.");
            if (delta < 1 || delta > 1000000)
                throw new ArgumentOutOfRangeException(nameof(delta));
            EventId = eventId;
            AchievementId = achievementId;
            Delta = delta;
            Verified = verified;
        }
    }

    public sealed class AchievementSnapshot
    {
        private readonly ReadOnlyDictionary<string, int> _counts;
        private readonly ReadOnlyCollection<string> _unlockedIds;
        private readonly ReadOnlyCollection<string> _processedEventIds;

        public IReadOnlyDictionary<string, int> Counts => _counts;
        public IReadOnlyList<string> UnlockedIds => _unlockedIds;
        public IReadOnlyList<string> ProcessedEventIds => _processedEventIds;

        public AchievementSnapshot(
            IEnumerable<KeyValuePair<string, int>> counts = null,
            IEnumerable<string> unlockedIds = null,
            IEnumerable<string> processedEventIds = null)
        {
            var map = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var pair in counts ??
                Enumerable.Empty<KeyValuePair<string, int>>())
            {
                if (string.IsNullOrWhiteSpace(pair.Key) ||
                    pair.Value < 0 || pair.Value > 1000000 ||
                    map.ContainsKey(pair.Key))
                    throw new ArgumentException("Invalid achievement count state.");
                map.Add(pair.Key, pair.Value);
            }
            _counts = new ReadOnlyDictionary<string, int>(map);
            _unlockedIds = new ReadOnlyCollection<string>(
                Freeze(unlockedIds, "unlocked achievement"));
            _processedEventIds = new ReadOnlyCollection<string>(
                Freeze(processedEventIds, "achievement event"));
        }

        private static List<string> Freeze(IEnumerable<string> ids, string label)
        {
            var list = (ids ?? Enumerable.Empty<string>())
                .Take(100001).ToList();
            if (list.Count > 100000 ||
                list.Any(string.IsNullOrWhiteSpace) ||
                list.Distinct(StringComparer.Ordinal).Count() != list.Count)
                throw new ArgumentException("Invalid " + label + " IDs.");
            return list.OrderBy(x => x, StringComparer.Ordinal).ToList();
        }
    }

    public sealed class AchievementPlan
    {
        public AchievementSnapshot Result { get; }
        public bool Applied { get; }
        public bool NewlyUnlocked { get; }

        internal AchievementPlan(AchievementSnapshot result,
            bool applied, bool newlyUnlocked)
        {
            Result = result;
            Applied = applied;
            NewlyUnlocked = newlyUnlocked;
        }
    }

    public static class AchievementPlanner
    {
        public static AchievementPlan Apply(
            AchievementSnapshot snapshot,
            IEnumerable<AchievementDefinition> definitions,
            AchievementProgressEvent progressEvent)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            if (definitions == null) throw new ArgumentNullException(nameof(definitions));
            if (progressEvent == null)
                throw new ArgumentNullException(nameof(progressEvent));
            if (!progressEvent.Verified)
                throw new InvalidOperationException(
                    "Unverified event cannot advance achievement.");

            var defs = definitions.Take(257).ToArray();
            if (defs.Length > 256 || defs.Any(x => x == null) ||
                defs.GroupBy(x => x.AchievementId, StringComparer.Ordinal)
                    .Any(x => x.Count() > 1) ||
                defs.Where(x => x.AppleGameCenterId != null)
                    .GroupBy(x => x.AppleGameCenterId, StringComparer.Ordinal)
                    .Any(x => x.Count() > 1) ||
                defs.Where(x => x.GooglePlayGamesId != null)
                    .GroupBy(x => x.GooglePlayGamesId, StringComparer.Ordinal)
                    .Any(x => x.Count() > 1))
                throw new ArgumentException(
                    "Invalid or duplicate achievement/platform definitions.");
            var def = defs.SingleOrDefault(x =>
                StringComparer.Ordinal.Equals(
                    x.AchievementId, progressEvent.AchievementId));
            if (def == null)
                throw new ArgumentException("Unknown achievement ID.");
            if (snapshot.Counts.Keys.Any(x =>
                    !defs.Any(d => d.AchievementId == x)) ||
                snapshot.UnlockedIds.Any(x =>
                    !defs.Any(d => d.AchievementId == x)))
                throw new ArgumentException(
                    "Achievement snapshot references unknown definition.");

            var definitionMap = defs.ToDictionary(
                x => x.AchievementId, x => x, StringComparer.Ordinal);
            foreach (var pair in snapshot.Counts)
            {
                if (pair.Value > definitionMap[pair.Key].RequiredCount)
                    throw new ArgumentException(
                        "Achievement snapshot exceeds required count.");
                if (pair.Value >= definitionMap[pair.Key].RequiredCount &&
                    !snapshot.UnlockedIds.Contains(pair.Key))
                    throw new ArgumentException(
                        "Completed achievement snapshot is not unlocked.");
            }
            foreach (var unlockedId in snapshot.UnlockedIds)
            {
                int count;
                if (!snapshot.Counts.TryGetValue(unlockedId, out count) ||
                    count < definitionMap[unlockedId].RequiredCount)
                    throw new ArgumentException(
                        "Unlocked achievement snapshot lacks required progress.");
            }

            if (snapshot.ProcessedEventIds.Contains(progressEvent.EventId))
                return new AchievementPlan(snapshot, false, false);

            var counts = snapshot.Counts.ToDictionary(
                x => x.Key, x => x.Value, StringComparer.Ordinal);
            int current;
            counts.TryGetValue(def.AchievementId, out current);
            var next = Math.Min(
                def.RequiredCount, checked(current + progressEvent.Delta));
            counts[def.AchievementId] = next;

            var unlocked = new HashSet<string>(
                snapshot.UnlockedIds, StringComparer.Ordinal);
            var newly = next >= def.RequiredCount &&
                unlocked.Add(def.AchievementId);

            return new AchievementPlan(
                new AchievementSnapshot(
                    counts,
                    unlocked,
                    snapshot.ProcessedEventIds.Concat(
                        new[] { progressEvent.EventId })),
                true, newly);
        }
    }

    public enum AchievementPlatform
    {
        AppleGameCenter = 0,
        GooglePlayGames = 1
    }

    public static class AchievementPlatformProjector
    {
        public static IReadOnlyList<string> ProjectUnlocked(
            AchievementSnapshot snapshot,
            IEnumerable<AchievementDefinition> definitions,
            AchievementPlatform platform,
            bool nativeSignedIn)
        {
            if (snapshot == null)
                throw new ArgumentNullException(nameof(snapshot));
            if (definitions == null)
                throw new ArgumentNullException(nameof(definitions));
            if (!Enum.IsDefined(typeof(AchievementPlatform), platform))
                throw new ArgumentOutOfRangeException(nameof(platform));

            var defs = definitions.Take(257).ToArray();
            if (defs.Length > 256 || defs.Any(x => x == null) ||
                defs.GroupBy(x => x.AchievementId, StringComparer.Ordinal)
                    .Any(x => x.Count() > 1) ||
                defs.Where(x => x.AppleGameCenterId != null)
                    .GroupBy(x => x.AppleGameCenterId, StringComparer.Ordinal)
                    .Any(x => x.Count() > 1) ||
                defs.Where(x => x.GooglePlayGamesId != null)
                    .GroupBy(x => x.GooglePlayGamesId, StringComparer.Ordinal)
                    .Any(x => x.Count() > 1))
                throw new ArgumentException(
                    "Invalid or duplicate achievement/platform definitions.");
            if (snapshot.UnlockedIds.Any(id =>
                !defs.Any(x => StringComparer.Ordinal.Equals(
                    x.AchievementId, id))))
                throw new ArgumentException(
                    "Achievement snapshot references unknown definition.");

            if (!nativeSignedIn)
                return new string[0];

            var ids = new List<string>();
            foreach (var achievementId in snapshot.UnlockedIds)
            {
                var def = defs.Single(x =>
                    StringComparer.Ordinal.Equals(
                        x.AchievementId, achievementId));
                var nativeId = platform == AchievementPlatform.AppleGameCenter
                    ? def.AppleGameCenterId
                    : def.GooglePlayGamesId;
                if (!string.IsNullOrWhiteSpace(nativeId))
                    ids.Add(nativeId);
            }
            return ids.OrderBy(x => x, StringComparer.Ordinal).ToArray();
        }
    }

    public enum ReentryReason
    {
        FirstTime = 0,
        Returning = 1,
        Failed = 2,
        NoHeart = 3,
        Resumed = 4
    }

    public sealed class ReentryAuthoritySnapshot
    {
        private readonly ReadOnlyCollection<string> _committedRewardSourceIds;

        public string GameId { get; }
        public string ProfileId { get; }
        public string FrontierLevelId { get; }
        public long SaveRevision { get; }
        public int HeartBalance { get; }
        public NavigationRoute CurrentRoute { get; }
        public NavigationRoute FrontierMapRoute { get; }
        public IReadOnlyList<string> CommittedRewardSourceIds =>
            _committedRewardSourceIds;

        public ReentryAuthoritySnapshot(string gameId, string profileId,
            string frontierLevelId, long saveRevision, int heartBalance,
            NavigationRoute currentRoute, NavigationRoute frontierMapRoute,
            IEnumerable<string> committedRewardSourceIds = null)
        {
            if (!GameProfileCatalog.IsKnownId(gameId))
                throw new ArgumentException("Unknown game ID.", nameof(gameId));
            if (string.IsNullOrWhiteSpace(profileId) ||
                string.IsNullOrWhiteSpace(frontierLevelId))
                throw new ArgumentException("Re-entry identity required.");
            if (saveRevision < 0)
                throw new ArgumentOutOfRangeException(nameof(saveRevision));
            if (heartBalance < 0 || heartBalance > 1000000)
                throw new ArgumentOutOfRangeException(nameof(heartBalance));
            CurrentRoute = currentRoute ??
                throw new ArgumentNullException(nameof(currentRoute));
            FrontierMapRoute = frontierMapRoute ??
                throw new ArgumentNullException(nameof(frontierMapRoute));
            if (frontierMapRoute.Kind != NavigationRouteKind.Map)
                throw new ArgumentException(
                    "Frontier route must be a map route.",
                    nameof(frontierMapRoute));
            GameId = gameId;
            ProfileId = profileId;
            FrontierLevelId = frontierLevelId;
            SaveRevision = saveRevision;
            HeartBalance = heartBalance;
            _committedRewardSourceIds = new ReadOnlyCollection<string>(
                Freeze(committedRewardSourceIds));
        }

        private static List<string> Freeze(IEnumerable<string> ids)
        {
            var list = (ids ?? Enumerable.Empty<string>())
                .Take(100001).ToList();
            if (list.Count > 100000 ||
                list.Any(string.IsNullOrWhiteSpace) ||
                list.Distinct(StringComparer.Ordinal).Count() != list.Count)
                throw new ArgumentException("Invalid committed reward IDs.");
            return list.OrderBy(x => x, StringComparer.Ordinal).ToList();
        }
    }

    public sealed class ReentryPlan
    {
        public ReentryAuthoritySnapshot Authority { get; }
        public NavigationRoute TargetRoute { get; }
        public ReentryReason Reason { get; }

        internal ReentryPlan(ReentryAuthoritySnapshot authority,
            NavigationRoute targetRoute, ReentryReason reason)
        {
            Authority = authority;
            TargetRoute = targetRoute;
            Reason = reason;
        }
    }

    public static class ReentryPlanner
    {
        public static ReentryPlan Resolve(
            ProgressionGraph graph,
            ReentryAuthoritySnapshot authority,
            ReentryReason reason, string presentationToken = null)
        {
            if (graph == null)
                throw new ArgumentNullException(nameof(graph));
            if (authority == null)
                throw new ArgumentNullException(nameof(authority));
            if (!Enum.IsDefined(typeof(ReentryReason), reason))
                throw new ArgumentOutOfRangeException(nameof(reason));
            if (!graph.Levels.ContainsKey(authority.FrontierLevelId) ||
                !StringComparer.Ordinal.Equals(
                    graph.GameIdForLevel(authority.FrontierLevelId),
                    authority.GameId))
                throw new InvalidOperationException(
                    "Re-entry frontier does not belong to authority game.");

            NavigationPlanner.ValidateRoute(graph, authority.CurrentRoute);
            NavigationPlanner.ValidateRoute(graph, authority.FrontierMapRoute);

            var frontier = graph.Levels[authority.FrontierLevelId];
            var chapter = graph.Chapters[frontier.ChapterId];
            if (!StringComparer.Ordinal.Equals(
                    authority.FrontierMapRoute.ChapterId,
                    frontier.ChapterId) ||
                !StringComparer.Ordinal.Equals(
                    authority.FrontierMapRoute.WorldId,
                    chapter.WorldId))
                throw new InvalidOperationException(
                    "Re-entry map route does not contain the frontier level.");

            if (authority.CurrentRoute.LevelId != null &&
                !StringComparer.Ordinal.Equals(
                    graph.GameIdForLevel(authority.CurrentRoute.LevelId),
                    authority.GameId))
                throw new InvalidOperationException(
                    "Current re-entry route belongs to another game.");

            NavigationRoute target;
            switch (reason)
            {
                case ReentryReason.FirstTime:
                    target = new NavigationRoute(
                        NavigationRouteKind.Home,
                        "reentry:" + authority.SaveRevision + ":first");
                    break;
                case ReentryReason.Resumed:
                    target = authority.CurrentRoute;
                    break;
                case ReentryReason.Returning:
                case ReentryReason.Failed:
                case ReentryReason.NoHeart:
                    target = authority.FrontierMapRoute;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(reason));
            }

            // Presentation token is intentionally ignored. It cannot mutate
            // frontier, rewards, save revision, heart balance or route authority.
            return new ReentryPlan(authority, target, reason);
        }
    }
}
