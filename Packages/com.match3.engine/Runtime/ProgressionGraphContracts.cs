using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Match3.Engine.Runtime
{
    public sealed class WorldDefinition
    {
        public string WorldId { get; }
        public string GameId { get; }

        public WorldDefinition(string worldId, string gameId)
        {
            if (string.IsNullOrWhiteSpace(worldId))
                throw new ArgumentException("World ID required.", nameof(worldId));
            if (!GameProfileCatalog.IsKnownId(gameId))
                throw new ArgumentException("Unknown game ID.", nameof(gameId));
            WorldId = worldId;
            GameId = gameId;
        }
    }

    public sealed class ChapterDefinition
    {
        public string ChapterId { get; }
        public string WorldId { get; }

        public ChapterDefinition(string chapterId, string worldId)
        {
            if (string.IsNullOrWhiteSpace(chapterId))
                throw new ArgumentException("Chapter ID required.", nameof(chapterId));
            if (string.IsNullOrWhiteSpace(worldId))
                throw new ArgumentException("World ID required.", nameof(worldId));
            ChapterId = chapterId;
            WorldId = worldId;
        }
    }

    public sealed class LevelDefinition
    {
        private readonly ReadOnlyCollection<string> _prerequisiteLevelIds;

        public string LevelId { get; }
        public string ChapterId { get; }
        public IReadOnlyList<string> PrerequisiteLevelIds => _prerequisiteLevelIds;
        public int RequiredStars { get; }

        public LevelDefinition(string levelId, string chapterId,
            IEnumerable<string> prerequisiteLevelIds = null,
            int requiredStars = 0)
        {
            if (string.IsNullOrWhiteSpace(levelId))
                throw new ArgumentException("Level ID required.", nameof(levelId));
            if (string.IsNullOrWhiteSpace(chapterId))
                throw new ArgumentException("Chapter ID required.", nameof(chapterId));
            if (requiredStars < 0 || requiredStars > 1000000)
                throw new ArgumentOutOfRangeException(nameof(requiredStars));

            var prerequisites = (prerequisiteLevelIds ?? Enumerable.Empty<string>())
                .Take(257).ToList();
            if (prerequisites.Count > 256)
                throw new ArgumentException(
                    "Level prerequisite limit exceeded.",
                    nameof(prerequisiteLevelIds));
            if (prerequisites.Any(string.IsNullOrWhiteSpace) ||
                prerequisites.Distinct(StringComparer.Ordinal).Count() !=
                    prerequisites.Count)
                throw new ArgumentException(
                    "Prerequisite level IDs must be unique and nonempty.",
                    nameof(prerequisiteLevelIds));
            if (prerequisites.Contains(levelId, StringComparer.Ordinal))
                throw new ArgumentException(
                    "Level cannot require itself.", nameof(prerequisiteLevelIds));

            LevelId = levelId;
            ChapterId = chapterId;
            RequiredStars = requiredStars;
            _prerequisiteLevelIds =
                new ReadOnlyCollection<string>(prerequisites);
        }
    }

    public sealed class ProgressionGraph
    {
        private readonly ReadOnlyDictionary<string, WorldDefinition> _worlds;
        private readonly ReadOnlyDictionary<string, ChapterDefinition> _chapters;
        private readonly ReadOnlyDictionary<string, LevelDefinition> _levels;

        public IReadOnlyDictionary<string, WorldDefinition> Worlds => _worlds;
        public IReadOnlyDictionary<string, ChapterDefinition> Chapters => _chapters;
        public IReadOnlyDictionary<string, LevelDefinition> Levels => _levels;

        public ProgressionGraph(IEnumerable<WorldDefinition> worlds,
            IEnumerable<ChapterDefinition> chapters,
            IEnumerable<LevelDefinition> levels)
        {
            var worldArray = Freeze(worlds, 128, nameof(worlds));
            var chapterArray = Freeze(chapters, 1024, nameof(chapters));
            var levelArray = Freeze(levels, 10000, nameof(levels));

            if (worldArray.Length == 0 ||
                chapterArray.Length == 0 ||
                levelArray.Length == 0)
                throw new ArgumentException(
                    "Graph requires worlds, chapters and levels.");

            _worlds = Unique(worldArray, x => x.WorldId, "world");
            _chapters = Unique(chapterArray, x => x.ChapterId, "chapter");
            _levels = Unique(levelArray, x => x.LevelId, "level");

            foreach (var chapter in chapterArray)
            {
                if (!_worlds.ContainsKey(chapter.WorldId))
                    throw new ArgumentException(
                        "Chapter references missing world: " +
                        chapter.ChapterId);
            }

            foreach (var level in levelArray)
            {
                if (!_chapters.ContainsKey(level.ChapterId))
                    throw new ArgumentException(
                        "Level references missing chapter: " +
                        level.LevelId);
                foreach (var dependency in level.PrerequisiteLevelIds)
                {
                    if (!_levels.ContainsKey(dependency))
                        throw new ArgumentException(
                            "Level references missing prerequisite: " +
                            dependency);
                }
            }

            ValidateLevelDag(levelArray);
        }

        public string GameIdForLevel(string levelId)
        {
            LevelDefinition level;
            if (!_levels.TryGetValue(levelId, out level))
                throw new ArgumentException("Unknown level ID.", nameof(levelId));
            var chapter = _chapters[level.ChapterId];
            return _worlds[chapter.WorldId].GameId;
        }

        private static T[] Freeze<T>(IEnumerable<T> source,
            int maximum, string parameter) where T : class
        {
            if (source == null) throw new ArgumentNullException(parameter);
            var array = source.Take(maximum + 1).ToArray();
            if (array.Length > maximum)
                throw new ArgumentException(
                    "Progression graph collection limit exceeded.",
                    parameter);
            if (array.Any(x => x == null))
                throw new ArgumentException(
                    "Progression graph contains null entry.", parameter);
            return array;
        }

        private static ReadOnlyDictionary<string, T> Unique<T>(
            IEnumerable<T> values, Func<T, string> keySelector, string label)
        {
            var map = new Dictionary<string, T>(StringComparer.Ordinal);
            foreach (var value in values)
            {
                var key = keySelector(value);
                if (map.ContainsKey(key))
                    throw new ArgumentException(
                        "Duplicate " + label + " ID: " + key);
                map.Add(key, value);
            }
            return new ReadOnlyDictionary<string, T>(map);
        }

        private static void ValidateLevelDag(
            IEnumerable<LevelDefinition> levels)
        {
            var map = levels.ToDictionary(x => x.LevelId, x => x,
                StringComparer.Ordinal);
            var state = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var id in map.Keys)
                Visit(id, map, state);
        }

        private static void Visit(string id,
            IDictionary<string, LevelDefinition> map,
            IDictionary<string, int> state)
        {
            int value;
            if (state.TryGetValue(id, out value))
            {
                if (value == 1)
                    throw new ArgumentException(
                        "Level prerequisite cycle detected.");
                if (value == 2) return;
            }

            state[id] = 1;
            foreach (var parent in map[id].PrerequisiteLevelIds)
                Visit(parent, map, state);
            state[id] = 2;
        }
    }

    public enum NavigationRouteKind
    {
        Home = 0,
        World = 1,
        Chapter = 2,
        Level = 3,
        Result = 4,
        Map = 5
    }

    public sealed class NavigationRoute
    {
        public NavigationRouteKind Kind { get; }
        public string WorldId { get; }
        public string ChapterId { get; }
        public string LevelId { get; }
        public string RouteId { get; }

        public NavigationRoute(NavigationRouteKind kind,
            string routeId, string worldId = null,
            string chapterId = null, string levelId = null)
        {
            if (!Enum.IsDefined(typeof(NavigationRouteKind), kind))
                throw new ArgumentOutOfRangeException(nameof(kind));
            if (string.IsNullOrWhiteSpace(routeId))
                throw new ArgumentException("Route ID required.", nameof(routeId));

            RequireShape(kind, worldId, chapterId, levelId);
            Kind = kind;
            RouteId = routeId;
            WorldId = worldId;
            ChapterId = chapterId;
            LevelId = levelId;
        }

        private static void RequireShape(NavigationRouteKind kind,
            string worldId, string chapterId, string levelId)
        {
            switch (kind)
            {
                case NavigationRouteKind.Home:
                    if (worldId != null || chapterId != null || levelId != null)
                        throw new ArgumentException(
                            "Home route cannot contain graph IDs.");
                    return;
                case NavigationRouteKind.World:
                    if (string.IsNullOrWhiteSpace(worldId) ||
                        chapterId != null || levelId != null)
                        throw new ArgumentException(
                            "World route requires only world ID.");
                    return;
                case NavigationRouteKind.Chapter:
                case NavigationRouteKind.Map:
                    if (string.IsNullOrWhiteSpace(worldId) ||
                        string.IsNullOrWhiteSpace(chapterId) ||
                        levelId != null)
                        throw new ArgumentException(
                            "Chapter/map route requires world and chapter IDs.");
                    return;
                case NavigationRouteKind.Level:
                case NavigationRouteKind.Result:
                    if (string.IsNullOrWhiteSpace(worldId) ||
                        string.IsNullOrWhiteSpace(chapterId) ||
                        string.IsNullOrWhiteSpace(levelId))
                        throw new ArgumentException(
                            "Level/result route requires full graph identity.");
                    return;
                default:
                    throw new ArgumentOutOfRangeException(nameof(kind));
            }
        }
    }

    public sealed class NavigationState
    {
        private readonly ReadOnlyCollection<string> _processedRouteIds;

        public NavigationRoute Current { get; }
        public IReadOnlyList<string> ProcessedRouteIds => _processedRouteIds;

        public NavigationState(NavigationRoute current,
            IEnumerable<string> processedRouteIds = null)
        {
            Current = current ?? throw new ArgumentNullException(nameof(current));
            var ids = (processedRouteIds ?? Enumerable.Empty<string>())
                .Take(100001).ToList();
            if (ids.Count > 100000 ||
                ids.Any(string.IsNullOrWhiteSpace) ||
                ids.Distinct(StringComparer.Ordinal).Count() != ids.Count)
                throw new ArgumentException("Invalid route history.",
                    nameof(processedRouteIds));
            _processedRouteIds = new ReadOnlyCollection<string>(
                ids.OrderBy(x => x, StringComparer.Ordinal).ToList());
        }
    }

    public sealed class NavigationPlan
    {
        public NavigationState Result { get; }
        public bool Applied { get; }

        internal NavigationPlan(NavigationState result, bool applied)
        {
            Result = result;
            Applied = applied;
        }
    }

    public static class NavigationPlanner
    {
        public static NavigationPlan Transition(ProgressionGraph graph,
            NavigationState state, NavigationRoute next)
        {
            if (graph == null) throw new ArgumentNullException(nameof(graph));
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (next == null) throw new ArgumentNullException(nameof(next));

            if (state.ProcessedRouteIds.Contains(next.RouteId))
                return new NavigationPlan(state, false);

            ValidateRoute(graph, next);
            if (!IsAllowed(state.Current.Kind, next.Kind))
                throw new InvalidOperationException(
                    "Illegal progression navigation transition: " +
                    state.Current.Kind + " -> " + next.Kind);

            return new NavigationPlan(
                new NavigationState(next,
                    state.ProcessedRouteIds.Concat(
                        new[] { next.RouteId })),
                true);
        }

        public static void ValidateRoute(ProgressionGraph graph,
            NavigationRoute route)
        {
            if (graph == null) throw new ArgumentNullException(nameof(graph));
            if (route == null) throw new ArgumentNullException(nameof(route));

            if (route.WorldId != null &&
                !graph.Worlds.ContainsKey(route.WorldId))
                throw new ArgumentException("Unknown world route.");
            if (route.ChapterId != null)
            {
                ChapterDefinition chapter;
                if (!graph.Chapters.TryGetValue(
                    route.ChapterId, out chapter))
                    throw new ArgumentException("Unknown chapter route.");
                if (!StringComparer.Ordinal.Equals(
                    chapter.WorldId, route.WorldId))
                    throw new ArgumentException(
                        "Chapter route belongs to another world.");
            }
            if (route.LevelId != null)
            {
                LevelDefinition level;
                if (!graph.Levels.TryGetValue(route.LevelId, out level))
                    throw new ArgumentException("Unknown level route.");
                if (!StringComparer.Ordinal.Equals(
                    level.ChapterId, route.ChapterId))
                    throw new ArgumentException(
                        "Level route belongs to another chapter.");
            }
        }

        private static bool IsAllowed(NavigationRouteKind from,
            NavigationRouteKind to)
        {
            return
                (from == NavigationRouteKind.Home &&
                    to == NavigationRouteKind.World) ||
                (from == NavigationRouteKind.World &&
                    to == NavigationRouteKind.Chapter) ||
                (from == NavigationRouteKind.Chapter &&
                    to == NavigationRouteKind.Level) ||
                (from == NavigationRouteKind.Level &&
                    to == NavigationRouteKind.Result) ||
                (from == NavigationRouteKind.Result &&
                    to == NavigationRouteKind.Map) ||
                (from == NavigationRouteKind.Map &&
                    to == NavigationRouteKind.Level) ||
                (from == NavigationRouteKind.Map &&
                    to == NavigationRouteKind.World);
        }
    }

    public sealed class UnlockSnapshot
    {
        private readonly ReadOnlyDictionary<string, int> _highestStars;
        private readonly ReadOnlyCollection<string> _unlockedLevelIds;
        private readonly ReadOnlyCollection<string> _processedAttemptIds;

        public IReadOnlyDictionary<string, int> HighestStars => _highestStars;
        public IReadOnlyList<string> UnlockedLevelIds => _unlockedLevelIds;
        public IReadOnlyList<string> ProcessedAttemptIds => _processedAttemptIds;

        public UnlockSnapshot(
            IEnumerable<KeyValuePair<string, int>> highestStars = null,
            IEnumerable<string> unlockedLevelIds = null,
            IEnumerable<string> processedAttemptIds = null)
        {
            var stars = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var pair in highestStars ??
                Enumerable.Empty<KeyValuePair<string, int>>())
            {
                if (string.IsNullOrWhiteSpace(pair.Key) ||
                    pair.Value < 0 || pair.Value > 3)
                    throw new ArgumentException("Invalid level star state.");
                if (stars.ContainsKey(pair.Key))
                    throw new ArgumentException("Duplicate star state.");
                stars.Add(pair.Key, pair.Value);
            }

            var unlocked = FreezeIds(
                unlockedLevelIds, "unlocked level");
            var attempts = FreezeIds(
                processedAttemptIds, "attempt");

            _highestStars =
                new ReadOnlyDictionary<string, int>(stars);
            _unlockedLevelIds =
                new ReadOnlyCollection<string>(unlocked);
            _processedAttemptIds =
                new ReadOnlyCollection<string>(attempts);
        }

        private static List<string> FreezeIds(
            IEnumerable<string> ids, string label)
        {
            var list = (ids ?? Enumerable.Empty<string>())
                .Take(100001).ToList();
            if (list.Count > 100000 ||
                list.Any(string.IsNullOrWhiteSpace) ||
                list.Distinct(StringComparer.Ordinal).Count() != list.Count)
                throw new ArgumentException(
                    "Invalid " + label + " IDs.");
            return list.OrderBy(x => x, StringComparer.Ordinal).ToList();
        }
    }

    public sealed class UnlockPlan
    {
        public UnlockSnapshot Result { get; }
        public bool Applied { get; }
        public IReadOnlyList<string> NewlyUnlockedLevelIds { get; }

        internal UnlockPlan(UnlockSnapshot result, bool applied,
            IEnumerable<string> newlyUnlocked)
        {
            Result = result;
            Applied = applied;
            NewlyUnlockedLevelIds = new ReadOnlyCollection<string>(
                newlyUnlocked.OrderBy(x => x, StringComparer.Ordinal).ToList());
        }
    }

    public static class UnlockPlanner
    {
        public static UnlockPlan ApplyVerifiedWin(
            ProgressionGraph graph, UnlockSnapshot snapshot,
            LevelResultAttempt attempt)
        {
            if (graph == null) throw new ArgumentNullException(nameof(graph));
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            if (attempt == null) throw new ArgumentNullException(nameof(attempt));
            if (!attempt.Verified || !attempt.Won)
                throw new InvalidOperationException(
                    "Only verified wins can update unlock progression.");
            if (!graph.Levels.ContainsKey(attempt.LevelId))
                throw new ArgumentException(
                    "Attempt references unknown level.");

            if (snapshot.ProcessedAttemptIds.Contains(attempt.AttemptId))
                return new UnlockPlan(snapshot, false, new string[0]);

            var stars = snapshot.HighestStars.ToDictionary(
                x => x.Key, x => x.Value, StringComparer.Ordinal);
            int previous;
            stars.TryGetValue(attempt.LevelId, out previous);
            if (attempt.Stars > previous)
                stars[attempt.LevelId] = attempt.Stars;

            var unlocked = new HashSet<string>(
                snapshot.UnlockedLevelIds, StringComparer.Ordinal);
            unlocked.Add(attempt.LevelId);

            var newlyUnlocked = new List<string>();
            var changed = true;
            while (changed)
            {
                changed = false;
                foreach (var level in graph.Levels.Values
                    .OrderBy(x => x.LevelId, StringComparer.Ordinal))
                {
                    if (unlocked.Contains(level.LevelId))
                        continue;
                    if (!level.PrerequisiteLevelIds.All(
                        unlocked.Contains))
                        continue;
                    var requiredStarSum =
                        level.PrerequisiteLevelIds.Sum(id =>
                        {
                            int s;
                            return stars.TryGetValue(id, out s) ? s : 0;
                        });
                    if (requiredStarSum < level.RequiredStars)
                        continue;

                    unlocked.Add(level.LevelId);
                    newlyUnlocked.Add(level.LevelId);
                    changed = true;
                }
            }

            return new UnlockPlan(
                new UnlockSnapshot(
                    stars,
                    unlocked,
                    snapshot.ProcessedAttemptIds.Concat(
                        new[] { attempt.AttemptId })),
                true,
                newlyUnlocked);
        }
    }

    public enum HeartTransactionKind
    {
        Spend = 0,
        Grant = 1
    }

    public sealed class HeartLedgerEntry
    {
        public string SourceId { get; }
        public HeartTransactionKind Kind { get; }
        public int Amount { get; }

        public HeartLedgerEntry(string sourceId,
            HeartTransactionKind kind, int amount)
        {
            if (string.IsNullOrWhiteSpace(sourceId))
                throw new ArgumentException(
                    "Heart transaction source ID required.",
                    nameof(sourceId));
            if (!Enum.IsDefined(typeof(HeartTransactionKind), kind))
                throw new ArgumentOutOfRangeException(nameof(kind));
            if (amount < 1 || amount > 1000000)
                throw new ArgumentOutOfRangeException(nameof(amount));
            SourceId = sourceId;
            Kind = kind;
            Amount = amount;
        }
    }

    public sealed class HeartSnapshot
    {
        private readonly ReadOnlyDictionary<string, HeartLedgerEntry> _ledger;

        public int Balance { get; }
        public int MaximumBalance { get; }
        public long RegenIntervalSeconds { get; }
        public long HighWaterUnixSeconds { get; }
        public long LastRegenUnixSeconds { get; }
        public IReadOnlyDictionary<string, HeartLedgerEntry> Ledger => _ledger;

        public HeartSnapshot(int balance, int maximumBalance,
            long regenIntervalSeconds, long highWaterUnixSeconds,
            long lastRegenUnixSeconds,
            IEnumerable<HeartLedgerEntry> ledger = null)
        {
            if (maximumBalance < 1 || maximumBalance > 1000)
                throw new ArgumentOutOfRangeException(nameof(maximumBalance));
            if (balance < 0 || balance > maximumBalance)
                throw new ArgumentOutOfRangeException(nameof(balance));
            if (regenIntervalSeconds < 1 ||
                regenIntervalSeconds > 604800)
                throw new ArgumentOutOfRangeException(nameof(regenIntervalSeconds));
            if (highWaterUnixSeconds < 0 ||
                lastRegenUnixSeconds < 0 ||
                lastRegenUnixSeconds > highWaterUnixSeconds)
                throw new ArgumentOutOfRangeException(nameof(highWaterUnixSeconds));

            var map = new Dictionary<string, HeartLedgerEntry>(
                StringComparer.Ordinal);
            foreach (var item in (ledger ??
                Enumerable.Empty<HeartLedgerEntry>()).Take(100001))
            {
                if (map.Count >= 100000)
                    throw new ArgumentException(
                        "Heart ledger history limit exceeded.",
                        nameof(ledger));
                if (item == null)
                    throw new ArgumentException(
                        "Null heart ledger entry.", nameof(ledger));
                if (map.ContainsKey(item.SourceId))
                    throw new ArgumentException(
                        "Duplicate heart transaction source.",
                        nameof(ledger));
                map.Add(item.SourceId, item);
            }

            Balance = balance;
            MaximumBalance = maximumBalance;
            RegenIntervalSeconds = regenIntervalSeconds;
            HighWaterUnixSeconds = highWaterUnixSeconds;
            LastRegenUnixSeconds = lastRegenUnixSeconds;
            _ledger =
                new ReadOnlyDictionary<string, HeartLedgerEntry>(map);
        }
    }

    public sealed class HeartPlan
    {
        public HeartSnapshot Result { get; }
        public bool Applied { get; }
        public int BalanceDelta { get; }

        internal HeartPlan(HeartSnapshot result,
            bool applied, int balanceDelta)
        {
            Result = result;
            Applied = applied;
            BalanceDelta = balanceDelta;
        }
    }

    public static class HeartPlanner
    {
        public static HeartPlan ReconcileTime(
            HeartSnapshot snapshot, long observedUnixSeconds)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            if (observedUnixSeconds < 0)
                throw new ArgumentOutOfRangeException(nameof(observedUnixSeconds));

            var highWater = Math.Max(
                snapshot.HighWaterUnixSeconds, observedUnixSeconds);

            if (snapshot.Balance >= snapshot.MaximumBalance)
            {
                if (highWater == snapshot.HighWaterUnixSeconds)
                    return new HeartPlan(snapshot, false, 0);
                return new HeartPlan(
                    Rebuild(snapshot, snapshot.Balance,
                        highWater, highWater, snapshot.Ledger.Values),
                    true, 0);
            }

            var elapsed = highWater - snapshot.LastRegenUnixSeconds;
            if (elapsed < snapshot.RegenIntervalSeconds)
            {
                if (highWater == snapshot.HighWaterUnixSeconds)
                    return new HeartPlan(snapshot, false, 0);
                return new HeartPlan(
                    Rebuild(snapshot, snapshot.Balance,
                        highWater, snapshot.LastRegenUnixSeconds,
                        snapshot.Ledger.Values),
                    true, 0);
            }

            var gainedLong = elapsed / snapshot.RegenIntervalSeconds;
            var room = snapshot.MaximumBalance - snapshot.Balance;
            var gained = (int)Math.Min(room, gainedLong);
            var balance = snapshot.Balance + gained;
            var lastRegen = balance >= snapshot.MaximumBalance
                ? highWater
                : snapshot.LastRegenUnixSeconds +
                    gained * snapshot.RegenIntervalSeconds;

            return new HeartPlan(
                Rebuild(snapshot, balance, highWater,
                    lastRegen, snapshot.Ledger.Values),
                true, gained);
        }

        public static HeartPlan Spend(HeartSnapshot snapshot,
            string sourceId, int amount = 1)
        {
            return Mutate(snapshot, sourceId,
                HeartTransactionKind.Spend, amount);
        }

        public static HeartPlan Grant(HeartSnapshot snapshot,
            string sourceId, int amount = 1)
        {
            return Mutate(snapshot, sourceId,
                HeartTransactionKind.Grant, amount);
        }

        private static HeartPlan Mutate(HeartSnapshot snapshot,
            string sourceId, HeartTransactionKind kind, int amount)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            if (string.IsNullOrWhiteSpace(sourceId))
                throw new ArgumentException("Heart source ID required.",
                    nameof(sourceId));
            if (amount < 1 || amount > 1000000)
                throw new ArgumentOutOfRangeException(nameof(amount));

            HeartLedgerEntry existing;
            if (snapshot.Ledger.TryGetValue(sourceId, out existing))
            {
                if (existing.Kind != kind || existing.Amount != amount)
                    throw new InvalidOperationException(
                        "Heart source ID is already bound to another transaction.");
                return new HeartPlan(snapshot, false, 0);
            }

            int delta;
            int balance;
            if (kind == HeartTransactionKind.Spend)
            {
                if (snapshot.Balance < amount)
                    throw new InvalidOperationException(
                        "Insufficient heart balance.");
                delta = -amount;
                balance = snapshot.Balance - amount;
            }
            else
            {
                var room = snapshot.MaximumBalance - snapshot.Balance;
                var granted = Math.Min(room, amount);
                delta = granted;
                balance = snapshot.Balance + granted;
            }

            var entry = new HeartLedgerEntry(sourceId, kind, amount);
            var ledger = snapshot.Ledger.Values.Concat(
                new[] { entry });
            return new HeartPlan(
                Rebuild(snapshot, balance,
                    snapshot.HighWaterUnixSeconds,
                    snapshot.LastRegenUnixSeconds,
                    ledger),
                true, delta);
        }

        private static HeartSnapshot Rebuild(
            HeartSnapshot snapshot, int balance,
            long highWater, long lastRegen,
            IEnumerable<HeartLedgerEntry> ledger)
        {
            return new HeartSnapshot(
                balance,
                snapshot.MaximumBalance,
                snapshot.RegenIntervalSeconds,
                highWater,
                lastRegen,
                ledger);
        }
    }
}
