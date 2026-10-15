using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Match3.Engine.Runtime
{
    public enum GameEconomyMutationKind
    {
        Credit = 0,
        Debit = 1
    }

    public sealed class GameEconomyTransaction
    {
        public string SourceId { get; }
        public string GameId { get; }
        public string ResourceId { get; }
        public int Amount { get; }
        public GameEconomyMutationKind Kind { get; }

        public GameEconomyTransaction(string sourceId, string gameId,
            string resourceId, int amount, GameEconomyMutationKind kind)
        {
            if (string.IsNullOrWhiteSpace(sourceId))
                throw new ArgumentException("Transaction source ID required.",
                    nameof(sourceId));
            if (!GameProfileCatalog.IsKnownId(gameId))
                throw new ArgumentException("Unknown game ID.", nameof(gameId));
            if (string.IsNullOrWhiteSpace(resourceId) ||
                resourceId.Contains("|"))
                throw new ArgumentException(
                    "Resource ID required and cannot contain '|'.",
                    nameof(resourceId));
            if (amount < 1 || amount > 1000000000)
                throw new ArgumentOutOfRangeException(nameof(amount));
            if (!Enum.IsDefined(typeof(GameEconomyMutationKind), kind))
                throw new ArgumentOutOfRangeException(nameof(kind));
            SourceId = sourceId;
            GameId = gameId;
            ResourceId = resourceId;
            Amount = amount;
            Kind = kind;
        }

        internal string ResourceKey => GameId + "|" + ResourceId;
    }

    public sealed class GameEconomySnapshot
    {
        private readonly ReadOnlyDictionary<string, int> _balances;
        private readonly ReadOnlyDictionary<string, GameEconomyTransaction> _ledger;

        public IReadOnlyDictionary<string, int> Balances => _balances;
        public IReadOnlyDictionary<string, GameEconomyTransaction> Ledger => _ledger;

        public GameEconomySnapshot(
            IEnumerable<KeyValuePair<string, int>> balances = null,
            IEnumerable<GameEconomyTransaction> ledger = null)
        {
            var balanceMap = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var pair in (balances ??
                Enumerable.Empty<KeyValuePair<string, int>>()).Take(100001))
            {
                if (balanceMap.Count >= 100000)
                    throw new ArgumentException("Economy balance key limit exceeded.");
                if (string.IsNullOrWhiteSpace(pair.Key) ||
                    pair.Value < 0 || pair.Value > 1000000000)
                    throw new ArgumentException("Invalid economy balance.");
                if (balanceMap.ContainsKey(pair.Key))
                    throw new ArgumentException("Duplicate economy balance key.");
                var separator = pair.Key.IndexOf('|');
                if (separator <= 0 ||
                    separator >= pair.Key.Length - 1 ||
                    pair.Key.IndexOf('|', separator + 1) >= 0 ||
                    !GameProfileCatalog.IsKnownId(
                        pair.Key.Substring(0, separator)))
                    throw new ArgumentException(
                        "Economy balance key must be canonical game|resource.");
                balanceMap.Add(pair.Key, pair.Value);
            }

            var txMap = new Dictionary<string, GameEconomyTransaction>(
                StringComparer.Ordinal);
            foreach (var tx in (ledger ??
                Enumerable.Empty<GameEconomyTransaction>()).Take(100001))
            {
                if (txMap.Count >= 100000)
                    throw new ArgumentException("Economy ledger limit exceeded.");
                if (tx == null)
                    throw new ArgumentException("Null economy transaction.");
                if (txMap.ContainsKey(tx.SourceId))
                    throw new ArgumentException(
                        "Duplicate economy transaction source.");
                txMap.Add(tx.SourceId, tx);
            }

            _balances = new ReadOnlyDictionary<string, int>(balanceMap);
            _ledger =
                new ReadOnlyDictionary<string, GameEconomyTransaction>(txMap);
        }

        public int Balance(string gameId, string resourceId)
        {
            if (!GameProfileCatalog.IsKnownId(gameId))
                throw new ArgumentException("Unknown game ID.", nameof(gameId));
            if (string.IsNullOrWhiteSpace(resourceId))
                throw new ArgumentException("Resource ID required.",
                    nameof(resourceId));
            int value;
            return _balances.TryGetValue(
                gameId + "|" + resourceId, out value)
                ? value
                : 0;
        }
    }

    public sealed class GameEconomyPlan
    {
        public GameEconomySnapshot Result { get; }
        public bool Applied { get; }
        public int BalanceDelta { get; }

        internal GameEconomyPlan(GameEconomySnapshot result,
            bool applied, int balanceDelta)
        {
            Result = result;
            Applied = applied;
            BalanceDelta = balanceDelta;
        }
    }

    public static class GameEconomyPlanner
    {
        public static GameEconomyPlan Apply(GameEconomySnapshot snapshot,
            GameEconomyTransaction transaction)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            if (transaction == null)
                throw new ArgumentNullException(nameof(transaction));

            GameEconomyTransaction existing;
            if (snapshot.Ledger.TryGetValue(
                transaction.SourceId, out existing))
            {
                if (!Same(existing, transaction))
                    throw new InvalidOperationException(
                        "Economy source ID is bound to another transaction.");
                return new GameEconomyPlan(snapshot, false, 0);
            }

            var before = snapshot.Balance(
                transaction.GameId, transaction.ResourceId);
            int after;
            int delta;
            if (transaction.Kind == GameEconomyMutationKind.Debit)
            {
                if (before < transaction.Amount)
                    throw new InvalidOperationException(
                        "Insufficient game resource balance.");
                after = before - transaction.Amount;
                delta = -transaction.Amount;
            }
            else
            {
                var next = (long)before + transaction.Amount;
                if (next > 1000000000)
                    throw new InvalidOperationException(
                        "Game resource balance limit exceeded.");
                after = (int)next;
                delta = transaction.Amount;
            }

            var balances = snapshot.Balances.ToDictionary(
                x => x.Key, x => x.Value, StringComparer.Ordinal);
            balances[transaction.ResourceKey] = after;
            var ledger = snapshot.Ledger.Values.Concat(
                new[] { transaction });
            return new GameEconomyPlan(
                new GameEconomySnapshot(balances, ledger),
                true, delta);
        }

        private static bool Same(GameEconomyTransaction a,
            GameEconomyTransaction b)
        {
            return StringComparer.Ordinal.Equals(a.SourceId, b.SourceId) &&
                StringComparer.Ordinal.Equals(a.GameId, b.GameId) &&
                StringComparer.Ordinal.Equals(a.ResourceId, b.ResourceId) &&
                a.Amount == b.Amount &&
                a.Kind == b.Kind;
        }
    }

    public enum MissedDayPolicy
    {
        ResetStreak = 0,
        PreserveStreak = 1
    }

    public sealed class DailyRewardPolicy
    {
        public int MaximumStreak { get; }
        public MissedDayPolicy MissedDayPolicy { get; }
        public int MaximumForwardJumpDays { get; }

        public DailyRewardPolicy(int maximumStreak,
            MissedDayPolicy missedDayPolicy,
            int maximumForwardJumpDays = 31)
        {
            if (maximumStreak < 1 || maximumStreak > 3650)
                throw new ArgumentOutOfRangeException(nameof(maximumStreak));
            if (!Enum.IsDefined(typeof(MissedDayPolicy), missedDayPolicy))
                throw new ArgumentOutOfRangeException(nameof(missedDayPolicy));
            if (maximumForwardJumpDays < 1 ||
                maximumForwardJumpDays > 3650)
                throw new ArgumentOutOfRangeException(
                    nameof(maximumForwardJumpDays));
            MaximumStreak = maximumStreak;
            MissedDayPolicy = missedDayPolicy;
            MaximumForwardJumpDays = maximumForwardJumpDays;
        }
    }

    public sealed class DailyRewardSnapshot
    {
        private readonly ReadOnlyCollection<string> _processedClaimIds;

        public long HighWaterDayId { get; }
        public long LastClaimDayId { get; }
        public int Streak { get; }
        public IReadOnlyList<string> ProcessedClaimIds => _processedClaimIds;

        public DailyRewardSnapshot(long highWaterDayId,
            long lastClaimDayId = -1, int streak = 0,
            IEnumerable<string> processedClaimIds = null)
        {
            if (highWaterDayId < 0)
                throw new ArgumentOutOfRangeException(nameof(highWaterDayId));
            if (lastClaimDayId < -1 ||
                lastClaimDayId > highWaterDayId)
                throw new ArgumentOutOfRangeException(nameof(lastClaimDayId));
            if (streak < 0 || streak > 3650)
                throw new ArgumentOutOfRangeException(nameof(streak));
            if (lastClaimDayId == -1 && streak != 0)
                throw new ArgumentException(
                    "Unclaimed daily state cannot have a streak.");

            var ids = (processedClaimIds ??
                Enumerable.Empty<string>()).Take(100001).ToList();
            if (ids.Count > 100000 ||
                ids.Any(string.IsNullOrWhiteSpace) ||
                ids.Distinct(StringComparer.Ordinal).Count() != ids.Count)
                throw new ArgumentException("Invalid daily claim IDs.");

            HighWaterDayId = highWaterDayId;
            LastClaimDayId = lastClaimDayId;
            Streak = streak;
            _processedClaimIds = new ReadOnlyCollection<string>(
                ids.OrderBy(x => x, StringComparer.Ordinal).ToList());
        }
    }

    public sealed class DailyRewardPlan
    {
        public DailyRewardSnapshot Result { get; }
        public bool Applied { get; }
        public string RewardSourceId { get; }

        internal DailyRewardPlan(DailyRewardSnapshot result,
            bool applied, string rewardSourceId)
        {
            Result = result;
            Applied = applied;
            RewardSourceId = rewardSourceId;
        }
    }

    public static class DailyRewardPlanner
    {
        public static DailyRewardPlan Claim(DailyRewardSnapshot snapshot,
            long observedDayId, string claimId,
            DailyRewardPolicy policy)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            if (policy == null) throw new ArgumentNullException(nameof(policy));
            if (observedDayId < 0)
                throw new ArgumentOutOfRangeException(nameof(observedDayId));
            if (string.IsNullOrWhiteSpace(claimId))
                throw new ArgumentException("Daily claim ID required.",
                    nameof(claimId));

            if (snapshot.ProcessedClaimIds.Contains(claimId))
                return new DailyRewardPlan(snapshot, false, null);
            if (observedDayId < snapshot.HighWaterDayId)
                throw new InvalidOperationException(
                    "Daily reward clock rollback detected.");
            if (observedDayId - snapshot.HighWaterDayId >
                policy.MaximumForwardJumpDays)
                throw new InvalidOperationException(
                    "Daily reward clock jumped too far forward.");
            if (observedDayId == snapshot.LastClaimDayId)
                return new DailyRewardPlan(snapshot, false, null);

            int streak;
            if (snapshot.LastClaimDayId < 0)
            {
                streak = 1;
            }
            else if (observedDayId == snapshot.LastClaimDayId + 1)
            {
                streak = Math.Min(
                    policy.MaximumStreak, snapshot.Streak + 1);
            }
            else if (policy.MissedDayPolicy ==
                MissedDayPolicy.ResetStreak)
            {
                streak = 1;
            }
            else
            {
                streak = Math.Min(
                    policy.MaximumStreak,
                    Math.Max(1, snapshot.Streak));
            }

            var result = new DailyRewardSnapshot(
                observedDayId,
                observedDayId,
                streak,
                snapshot.ProcessedClaimIds.Concat(
                    new[] { claimId }));
            return new DailyRewardPlan(
                result, true, "daily:" + observedDayId);
        }
    }

    public enum TaskPeriodKind
    {
        Daily = 0,
        Weekly = 1
    }

    public sealed class TaskDefinition
    {
        public string TaskId { get; }
        public TaskPeriodKind PeriodKind { get; }
        public string EventType { get; }
        public int RequiredCount { get; }

        public TaskDefinition(string taskId, TaskPeriodKind periodKind,
            string eventType, int requiredCount)
        {
            if (string.IsNullOrWhiteSpace(taskId))
                throw new ArgumentException("Task ID required.", nameof(taskId));
            if (!Enum.IsDefined(typeof(TaskPeriodKind), periodKind))
                throw new ArgumentOutOfRangeException(nameof(periodKind));
            if (string.IsNullOrWhiteSpace(eventType))
                throw new ArgumentException("Task event type required.",
                    nameof(eventType));
            if (requiredCount < 1 || requiredCount > 1000000)
                throw new ArgumentOutOfRangeException(nameof(requiredCount));
            TaskId = taskId;
            PeriodKind = periodKind;
            EventType = eventType;
            RequiredCount = requiredCount;
        }
    }

    public sealed class TaskProgressEvent
    {
        public string EventId { get; }
        public string PeriodId { get; }
        public string EventType { get; }
        public int Delta { get; }

        public TaskProgressEvent(string eventId, string periodId,
            string eventType, int delta)
        {
            if (string.IsNullOrWhiteSpace(eventId))
                throw new ArgumentException("Task event ID required.",
                    nameof(eventId));
            if (string.IsNullOrWhiteSpace(periodId))
                throw new ArgumentException("Task period ID required.",
                    nameof(periodId));
            if (string.IsNullOrWhiteSpace(eventType))
                throw new ArgumentException("Task event type required.",
                    nameof(eventType));
            if (delta < 1 || delta > 1000000)
                throw new ArgumentOutOfRangeException(nameof(delta));
            EventId = eventId;
            PeriodId = periodId;
            EventType = eventType;
            Delta = delta;
        }
    }

    public sealed class TaskPeriodSnapshot
    {
        private readonly ReadOnlyDictionary<string, int> _counts;
        private readonly ReadOnlyCollection<string> _processedEventIds;
        private readonly ReadOnlyCollection<string> _claimedTaskIds;

        public TaskPeriodKind Kind { get; }
        public string PeriodId { get; }
        public long PeriodOrdinal { get; }
        public IReadOnlyDictionary<string, int> Counts => _counts;
        public IReadOnlyList<string> ProcessedEventIds => _processedEventIds;
        public IReadOnlyList<string> ClaimedTaskIds => _claimedTaskIds;

        public TaskPeriodSnapshot(TaskPeriodKind kind, string periodId,
            long periodOrdinal = 0,
            IEnumerable<KeyValuePair<string, int>> counts = null,
            IEnumerable<string> processedEventIds = null,
            IEnumerable<string> claimedTaskIds = null)
        {
            if (!Enum.IsDefined(typeof(TaskPeriodKind), kind))
                throw new ArgumentOutOfRangeException(nameof(kind));
            if (string.IsNullOrWhiteSpace(periodId))
                throw new ArgumentException("Task period ID required.",
                    nameof(periodId));
            if (periodOrdinal < 0)
                throw new ArgumentOutOfRangeException(nameof(periodOrdinal));

            var map = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var pair in counts ??
                Enumerable.Empty<KeyValuePair<string, int>>())
            {
                if (string.IsNullOrWhiteSpace(pair.Key) ||
                    pair.Value < 0 || pair.Value > 1000000 ||
                    map.ContainsKey(pair.Key))
                    throw new ArgumentException("Invalid task count state.");
                map.Add(pair.Key, pair.Value);
            }

            _counts =
                new ReadOnlyDictionary<string, int>(map);
            _processedEventIds =
                new ReadOnlyCollection<string>(
                    FreezeIds(processedEventIds, "task event"));
            _claimedTaskIds =
                new ReadOnlyCollection<string>(
                    FreezeIds(claimedTaskIds, "claimed task"));
            Kind = kind;
            PeriodId = periodId;
            PeriodOrdinal = periodOrdinal;
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

    public sealed class TaskPeriodPlan
    {
        public TaskPeriodSnapshot Result { get; }
        public bool Applied { get; }
        public string RewardSourceId { get; }

        internal TaskPeriodPlan(TaskPeriodSnapshot result,
            bool applied, string rewardSourceId = null)
        {
            Result = result;
            Applied = applied;
            RewardSourceId = rewardSourceId;
        }
    }

    public static class TaskProgressPlanner
    {
        public static TaskPeriodPlan ApplyEvent(
            TaskPeriodSnapshot snapshot,
            IEnumerable<TaskDefinition> definitions,
            TaskProgressEvent progressEvent)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            if (definitions == null) throw new ArgumentNullException(nameof(definitions));
            if (progressEvent == null)
                throw new ArgumentNullException(nameof(progressEvent));
            if (!StringComparer.Ordinal.Equals(
                snapshot.PeriodId, progressEvent.PeriodId))
                throw new InvalidOperationException(
                    "Task event belongs to another period.");
            if (snapshot.ProcessedEventIds.Contains(progressEvent.EventId))
                return new TaskPeriodPlan(snapshot, false);

            var defs = ValidateDefinitions(definitions, snapshot.Kind);
            ValidateTaskSnapshot(snapshot, defs);
            var counts = snapshot.Counts.ToDictionary(
                x => x.Key, x => x.Value, StringComparer.Ordinal);
            foreach (var def in defs.Where(x =>
                StringComparer.Ordinal.Equals(
                    x.EventType, progressEvent.EventType)))
            {
                int current;
                counts.TryGetValue(def.TaskId, out current);
                counts[def.TaskId] = Math.Min(
                    def.RequiredCount,
                    checked(current + progressEvent.Delta));
            }

            return new TaskPeriodPlan(
                new TaskPeriodSnapshot(
                    snapshot.Kind,
                    snapshot.PeriodId,
                    snapshot.PeriodOrdinal,
                    counts,
                    snapshot.ProcessedEventIds.Concat(
                        new[] { progressEvent.EventId }),
                    snapshot.ClaimedTaskIds),
                true);
        }

        public static TaskPeriodPlan Claim(
            TaskPeriodSnapshot snapshot,
            IEnumerable<TaskDefinition> definitions,
            string taskId)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            if (string.IsNullOrWhiteSpace(taskId))
                throw new ArgumentException("Task ID required.", nameof(taskId));
            var defs = ValidateDefinitions(definitions, snapshot.Kind);
            ValidateTaskSnapshot(snapshot, defs);
            var def = defs.SingleOrDefault(x =>
                StringComparer.Ordinal.Equals(x.TaskId, taskId));
            if (def == null)
                throw new ArgumentException("Unknown task ID.", nameof(taskId));
            if (snapshot.ClaimedTaskIds.Contains(taskId))
                return new TaskPeriodPlan(snapshot, false);

            int count;
            snapshot.Counts.TryGetValue(taskId, out count);
            if (count < def.RequiredCount)
                throw new InvalidOperationException(
                    "Task is not complete.");

            return new TaskPeriodPlan(
                new TaskPeriodSnapshot(
                    snapshot.Kind,
                    snapshot.PeriodId,
                    snapshot.PeriodOrdinal,
                    snapshot.Counts,
                    snapshot.ProcessedEventIds,
                    snapshot.ClaimedTaskIds.Concat(
                        new[] { taskId })),
                true,
                "task:" + snapshot.PeriodId + ":" + taskId);
        }

        public static TaskPeriodPlan Reset(
            TaskPeriodSnapshot snapshot, string newPeriodId,
            long newPeriodOrdinal)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            if (string.IsNullOrWhiteSpace(newPeriodId))
                throw new ArgumentException("New task period ID required.",
                    nameof(newPeriodId));
            if (newPeriodOrdinal < snapshot.PeriodOrdinal)
                throw new InvalidOperationException(
                    "Task period rollback detected.");
            if (newPeriodOrdinal == snapshot.PeriodOrdinal)
            {
                if (!StringComparer.Ordinal.Equals(
                    snapshot.PeriodId, newPeriodId))
                    throw new InvalidOperationException(
                        "Task period ID changed without advancing ordinal.");
                return new TaskPeriodPlan(snapshot, false);
            }
            return new TaskPeriodPlan(
                new TaskPeriodSnapshot(
                    snapshot.Kind, newPeriodId, newPeriodOrdinal),
                true);
        }

        private static void ValidateTaskSnapshot(
            TaskPeriodSnapshot snapshot, TaskDefinition[] definitions)
        {
            var byId = definitions.ToDictionary(
                x => x.TaskId, x => x, StringComparer.Ordinal);
            foreach (var pair in snapshot.Counts)
            {
                TaskDefinition def;
                if (!byId.TryGetValue(pair.Key, out def) ||
                    pair.Value > def.RequiredCount)
                    throw new ArgumentException(
                        "Task snapshot contains unknown or over-complete progress.");
            }
            foreach (var claimed in snapshot.ClaimedTaskIds)
            {
                TaskDefinition def;
                int count;
                if (!byId.TryGetValue(claimed, out def) ||
                    !snapshot.Counts.TryGetValue(claimed, out count) ||
                    count < def.RequiredCount)
                    throw new ArgumentException(
                        "Task snapshot contains invalid claimed state.");
            }
        }

        private static TaskDefinition[] ValidateDefinitions(
            IEnumerable<TaskDefinition> definitions, TaskPeriodKind kind)
        {
            if (definitions == null)
                throw new ArgumentNullException(nameof(definitions));
            var all = definitions.Take(257).ToArray();
            if (all.Length > 256)
                throw new ArgumentException("Task definition limit exceeded.");
            if (all.Any(x => x == null))
                throw new ArgumentException(
                    "Task definitions cannot contain null entries.");
            if (all.GroupBy(x => x.TaskId, StringComparer.Ordinal)
                .Any(x => x.Count() > 1))
                throw new ArgumentException(
                    "Task IDs must be globally unique across periods.");
            return all.Where(x => x.PeriodKind == kind).ToArray();
        }
    }

    public sealed class RestorationMilestone
    {
        private readonly ReadOnlyDictionary<string, string> _presentationKeys;

        public string MilestoneId { get; }
        public int RequiredProgress { get; }
        public IReadOnlyDictionary<string, string> PresentationKeys =>
            _presentationKeys;

        public RestorationMilestone(string milestoneId,
            int requiredProgress,
            IEnumerable<KeyValuePair<string, string>> presentationKeys)
        {
            if (string.IsNullOrWhiteSpace(milestoneId))
                throw new ArgumentException("Milestone ID required.",
                    nameof(milestoneId));
            if (requiredProgress < 1 || requiredProgress > 1000000000)
                throw new ArgumentOutOfRangeException(nameof(requiredProgress));
            if (presentationKeys == null)
                throw new ArgumentNullException(nameof(presentationKeys));

            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var pair in presentationKeys)
            {
                if (!GameProfileCatalog.IsKnownId(pair.Key) ||
                    string.IsNullOrWhiteSpace(pair.Value) ||
                    map.ContainsKey(pair.Key))
                    throw new ArgumentException(
                        "Invalid restoration presentation binding.");
                map.Add(pair.Key, pair.Value);
            }
            if (map.Count != GameProfileCatalog.Ids.Count ||
                GameProfileCatalog.Ids.Any(x => !map.ContainsKey(x)))
                throw new ArgumentException(
                    "Restoration milestone requires all five presentation bindings.");

            MilestoneId = milestoneId;
            RequiredProgress = requiredProgress;
            _presentationKeys =
                new ReadOnlyDictionary<string, string>(map);
        }
    }

    public sealed class RestorationSnapshot
    {
        private readonly ReadOnlyCollection<string> _processedEventIds;
        private readonly ReadOnlyCollection<string> _completedMilestoneIds;

        public int Progress { get; }
        public IReadOnlyList<string> ProcessedEventIds => _processedEventIds;
        public IReadOnlyList<string> CompletedMilestoneIds =>
            _completedMilestoneIds;

        public RestorationSnapshot(int progress = 0,
            IEnumerable<string> processedEventIds = null,
            IEnumerable<string> completedMilestoneIds = null)
        {
            if (progress < 0 || progress > 1000000000)
                throw new ArgumentOutOfRangeException(nameof(progress));
            Progress = progress;
            _processedEventIds = new ReadOnlyCollection<string>(
                Freeze(processedEventIds));
            _completedMilestoneIds = new ReadOnlyCollection<string>(
                Freeze(completedMilestoneIds));
        }

        private static List<string> Freeze(IEnumerable<string> ids)
        {
            var list = (ids ?? Enumerable.Empty<string>())
                .Take(100001).ToList();
            if (list.Count > 100000 ||
                list.Any(string.IsNullOrWhiteSpace) ||
                list.Distinct(StringComparer.Ordinal).Count() != list.Count)
                throw new ArgumentException("Invalid restoration IDs.");
            return list.OrderBy(x => x, StringComparer.Ordinal).ToList();
        }
    }

    public sealed class RestorationPlan
    {
        public RestorationSnapshot Result { get; }
        public bool Applied { get; }
        public IReadOnlyList<string> NewlyCompletedMilestoneIds { get; }

        internal RestorationPlan(RestorationSnapshot result,
            bool applied, IEnumerable<string> newlyCompleted)
        {
            Result = result;
            Applied = applied;
            NewlyCompletedMilestoneIds =
                new ReadOnlyCollection<string>(
                    newlyCompleted.OrderBy(
                        x => x, StringComparer.Ordinal).ToList());
        }
    }

    public static class RestorationPlanner
    {
        public static RestorationPlan ApplyProgress(
            RestorationSnapshot snapshot,
            IEnumerable<RestorationMilestone> milestones,
            string eventId, int delta)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            if (milestones == null) throw new ArgumentNullException(nameof(milestones));
            if (string.IsNullOrWhiteSpace(eventId))
                throw new ArgumentException("Restoration event ID required.",
                    nameof(eventId));
            if (delta < 1 || delta > 1000000000)
                throw new ArgumentOutOfRangeException(nameof(delta));
            if (snapshot.ProcessedEventIds.Contains(eventId))
                return new RestorationPlan(snapshot, false, new string[0]);

            var defs = milestones.Take(257).ToArray();
            if (defs.Length > 256 || defs.Any(x => x == null) ||
                defs.GroupBy(x => x.MilestoneId, StringComparer.Ordinal)
                    .Any(x => x.Count() > 1))
                throw new ArgumentException("Invalid restoration milestone set.");

            var byId = defs.ToDictionary(
                x => x.MilestoneId, x => x, StringComparer.Ordinal);
            foreach (var completedId in snapshot.CompletedMilestoneIds)
            {
                RestorationMilestone completedDefinition;
                if (!byId.TryGetValue(completedId, out completedDefinition) ||
                    snapshot.Progress < completedDefinition.RequiredProgress)
                    throw new ArgumentException(
                        "Restoration snapshot contains invalid completed milestone.");
            }

            var nextLong = (long)snapshot.Progress + delta;
            if (nextLong > 1000000000)
                throw new OverflowException("Restoration progress overflow.");
            var next = (int)nextLong;
            var completed = new HashSet<string>(
                snapshot.CompletedMilestoneIds, StringComparer.Ordinal);
            var newly = new List<string>();
            foreach (var def in defs.OrderBy(
                x => x.RequiredProgress)
                .ThenBy(x => x.MilestoneId, StringComparer.Ordinal))
            {
                if (next >= def.RequiredProgress &&
                    completed.Add(def.MilestoneId))
                    newly.Add(def.MilestoneId);
            }

            return new RestorationPlan(
                new RestorationSnapshot(
                    next,
                    snapshot.ProcessedEventIds.Concat(
                        new[] { eventId }),
                    completed),
                true, newly);
        }
    }
}
