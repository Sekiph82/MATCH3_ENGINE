using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Match3.Engine.Runtime
{
    public enum GoalKind
    {
        CollectPiece = 0,
        ClearObstacle = 1,
        RescueDrop = 2,
        Composite = 3
    }

    public sealed class GoalDefinition
    {
        private readonly ReadOnlyCollection<string> _childGoalIds;

        public string GoalId { get; }
        public GoalKind Kind { get; }
        public string TargetId { get; }
        public int RequiredCount { get; }
        public IReadOnlyList<string> ChildGoalIds => _childGoalIds;

        public GoalDefinition(string goalId, GoalKind kind,
            string targetId, int requiredCount,
            IEnumerable<string> childGoalIds = null)
        {
            if (string.IsNullOrWhiteSpace(goalId))
                throw new ArgumentException("Goal ID required.", nameof(goalId));
            if (!Enum.IsDefined(typeof(GoalKind), kind))
                throw new ArgumentOutOfRangeException(nameof(kind));
            if (requiredCount < 1 || requiredCount > 1000000)
                throw new ArgumentOutOfRangeException(nameof(requiredCount));

            var children = (childGoalIds ?? Enumerable.Empty<string>()).ToList();
            if (children.Count > 128)
                throw new ArgumentException("Composite goal child limit exceeded.",
                    nameof(childGoalIds));
            if (children.Any(string.IsNullOrWhiteSpace) ||
                children.Distinct(StringComparer.Ordinal).Count() != children.Count)
                throw new ArgumentException("Composite child IDs must be unique and nonempty.",
                    nameof(childGoalIds));

            if (kind == GoalKind.Composite)
            {
                if (!string.IsNullOrEmpty(targetId))
                    throw new ArgumentException(
                        "Composite goal cannot define a direct target.",
                        nameof(targetId));
                if (children.Count == 0)
                    throw new ArgumentException(
                        "Composite goal requires children.", nameof(childGoalIds));
            }
            else
            {
                if (string.IsNullOrWhiteSpace(targetId))
                    throw new ArgumentException(
                        "Atomic goal requires a target ID.", nameof(targetId));
                if (children.Count != 0)
                    throw new ArgumentException(
                        "Atomic goal cannot define child goals.", nameof(childGoalIds));
            }

            GoalId = goalId;
            Kind = kind;
            TargetId = targetId;
            RequiredCount = requiredCount;
            _childGoalIds = new ReadOnlyCollection<string>(children);
        }
    }

    public sealed class GoalRegistry
    {
        private readonly ReadOnlyDictionary<string, GoalDefinition> _byId;
        private readonly ReadOnlyCollection<string> _rootGoalIds;

        public IReadOnlyDictionary<string, GoalDefinition> ById => _byId;
        public IReadOnlyList<string> RootGoalIds => _rootGoalIds;

        public GoalRegistry(IEnumerable<GoalDefinition> goals)
        {
            if (goals == null) throw new ArgumentNullException(nameof(goals));
            var array = goals.ToArray();
            if (array.Length == 0)
                throw new ArgumentException("At least one goal required.", nameof(goals));
            if (array.Length > 256)
                throw new ArgumentException("Goal registry limit exceeded.", nameof(goals));
            if (array.Any(x => x == null))
                throw new ArgumentException("Null goal definition.", nameof(goals));
            if (array.GroupBy(x => x.GoalId, StringComparer.Ordinal)
                .Any(x => x.Count() != 1))
                throw new ArgumentException("Duplicate goal ID.", nameof(goals));

            var map = array.ToDictionary(x => x.GoalId, x => x,
                StringComparer.Ordinal);
            foreach (var goal in array.Where(x => x.Kind == GoalKind.Composite))
            {
                foreach (var child in goal.ChildGoalIds)
                {
                    if (StringComparer.Ordinal.Equals(goal.GoalId, child))
                        throw new ArgumentException("Composite goal cannot reference itself.");
                    if (!map.ContainsKey(child))
                        throw new ArgumentException(
                            "Composite goal references unknown child: " + child);
                }
            }

            var state = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var goal in array)
                Visit(goal.GoalId, map, state);

            _byId = new ReadOnlyDictionary<string, GoalDefinition>(map);
            var referenced = new HashSet<string>(
                array.SelectMany(x => x.ChildGoalIds), StringComparer.Ordinal);
            _rootGoalIds = new ReadOnlyCollection<string>(
                array.Select(x => x.GoalId)
                    .Where(x => !referenced.Contains(x))
                    .OrderBy(x => x, StringComparer.Ordinal)
                    .ToList());
            if (_rootGoalIds.Count == 0)
                throw new ArgumentException("Goal registry has no root goals.");
        }

        private static void Visit(string id,
            IDictionary<string, GoalDefinition> map,
            IDictionary<string, int> state)
        {
            int status;
            if (state.TryGetValue(id, out status))
            {
                if (status == 1)
                    throw new ArgumentException("Composite goal cycle detected.");
                if (status == 2) return;
            }
            state[id] = 1;
            var goal = map[id];
            foreach (var child in goal.ChildGoalIds)
                Visit(child, map, state);
            state[id] = 2;
        }
    }

    public sealed class LevelBudgetSnapshot
    {
        private readonly ReadOnlyCollection<string> _processedMoveIds;
        private readonly ReadOnlyCollection<string> _processedTimeIds;

        public int MovesRemaining { get; }
        public bool TimeEnabled { get; }
        public long TimeLimitMilliseconds { get; }
        public long ElapsedMilliseconds { get; }
        public IReadOnlyList<string> ProcessedMoveIds => _processedMoveIds;
        public IReadOnlyList<string> ProcessedTimeIds => _processedTimeIds;

        public LevelBudgetSnapshot(int movesRemaining,
            bool timeEnabled = false,
            long timeLimitMilliseconds = 0,
            long elapsedMilliseconds = 0,
            IEnumerable<string> processedMoveIds = null,
            IEnumerable<string> processedTimeIds = null)
        {
            if (movesRemaining < 0 || movesRemaining > 1000000)
                throw new ArgumentOutOfRangeException(nameof(movesRemaining));
            if (timeEnabled)
            {
                if (timeLimitMilliseconds < 1 ||
                    timeLimitMilliseconds > 86400000L)
                    throw new ArgumentOutOfRangeException(
                        nameof(timeLimitMilliseconds));
                if (elapsedMilliseconds < 0 ||
                    elapsedMilliseconds > timeLimitMilliseconds)
                    throw new ArgumentOutOfRangeException(
                        nameof(elapsedMilliseconds));
            }
            else if (timeLimitMilliseconds != 0 || elapsedMilliseconds != 0)
            {
                throw new ArgumentException(
                    "Disabled time mode cannot carry time state.");
            }

            MovesRemaining = movesRemaining;
            TimeEnabled = timeEnabled;
            TimeLimitMilliseconds = timeLimitMilliseconds;
            ElapsedMilliseconds = elapsedMilliseconds;
            _processedMoveIds = FreezeIds(processedMoveIds, "move");
            _processedTimeIds = FreezeIds(processedTimeIds, "time");
        }

        public bool TimeExpired =>
            TimeEnabled && ElapsedMilliseconds >= TimeLimitMilliseconds;

        private static ReadOnlyCollection<string> FreezeIds(
            IEnumerable<string> ids, string label)
        {
            var list = (ids ?? Enumerable.Empty<string>())
                .Take(100001).ToList();
            if (list.Count > 100000 ||
                list.Any(string.IsNullOrWhiteSpace) ||
                list.Distinct(StringComparer.Ordinal).Count() != list.Count)
                throw new ArgumentException("Invalid " + label + " event IDs.");
            return new ReadOnlyCollection<string>(
                list.OrderBy(x => x, StringComparer.Ordinal).ToList());
        }
    }

    public sealed class BudgetPlan
    {
        public LevelBudgetSnapshot Result { get; }
        public bool Applied { get; }

        internal BudgetPlan(LevelBudgetSnapshot result, bool applied)
        {
            Result = result ?? throw new ArgumentNullException(nameof(result));
            Applied = applied;
        }
    }

    public static class LevelBudgetPlanner
    {
        public static BudgetPlan ApplyCommittedTurn(LevelBudgetSnapshot snapshot,
            TurnReceipt turn)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            if (turn == null) throw new ArgumentNullException(nameof(turn));

            if (!turn.Committed ||
                turn.Kind != TurnMutationKind.PlayerMove)
                return new BudgetPlan(snapshot, false);

            if (snapshot.ProcessedMoveIds.Contains(turn.TurnId))
                return new BudgetPlan(snapshot, false);

            if (snapshot.MovesRemaining <= 0)
                throw new InvalidOperationException("Move budget already exhausted.");

            return new BudgetPlan(new LevelBudgetSnapshot(
                snapshot.MovesRemaining - 1,
                snapshot.TimeEnabled,
                snapshot.TimeLimitMilliseconds,
                snapshot.ElapsedMilliseconds,
                snapshot.ProcessedMoveIds.Concat(new[] { turn.TurnId }),
                snapshot.ProcessedTimeIds), true);
        }

        public static BudgetPlan ApplyElapsed(LevelBudgetSnapshot snapshot,
            string tickId, long cumulativeElapsedMilliseconds)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            if (!snapshot.TimeEnabled)
                throw new InvalidOperationException("Time mode is disabled.");
            if (string.IsNullOrWhiteSpace(tickId))
                throw new ArgumentException("Time tick ID required.", nameof(tickId));
            if (snapshot.ProcessedTimeIds.Contains(tickId))
                return new BudgetPlan(snapshot, false);
            if (cumulativeElapsedMilliseconds < snapshot.ElapsedMilliseconds)
                throw new InvalidOperationException(
                    "Elapsed time cannot move backwards.");
            if (cumulativeElapsedMilliseconds < 0)
                throw new ArgumentOutOfRangeException(
                    nameof(cumulativeElapsedMilliseconds));

            var clamped = Math.Min(snapshot.TimeLimitMilliseconds,
                cumulativeElapsedMilliseconds);
            return new BudgetPlan(new LevelBudgetSnapshot(
                snapshot.MovesRemaining,
                true,
                snapshot.TimeLimitMilliseconds,
                clamped,
                snapshot.ProcessedMoveIds,
                snapshot.ProcessedTimeIds.Concat(new[] { tickId })), true);
        }
    }

    public sealed class StarThresholds
    {
        public int OneStar { get; }
        public int TwoStars { get; }
        public int ThreeStars { get; }

        public StarThresholds(int oneStar, int twoStars, int threeStars)
        {
            if (oneStar < 0 || oneStar >= twoStars || twoStars >= threeStars)
                throw new ArgumentException(
                    "Star thresholds must be nonnegative and strictly increasing.");
            OneStar = oneStar;
            TwoStars = twoStars;
            ThreeStars = threeStars;
        }

        public int StarsFor(int score)
        {
            if (score < 0) throw new ArgumentOutOfRangeException(nameof(score));
            if (score >= ThreeStars) return 3;
            if (score >= TwoStars) return 2;
            if (score >= OneStar) return 1;
            return 0;
        }
    }

    public static class DeterministicScorePlanner
    {
        public static int ScoreEvent(int basePoints, int comboDepth,
            int comboStepPercent = 25, int maximumMultiplierPercent = 400)
        {
            if (basePoints < 0 || basePoints > 100000000)
                throw new ArgumentOutOfRangeException(nameof(basePoints));
            if (comboDepth < 0 || comboDepth > 1000)
                throw new ArgumentOutOfRangeException(nameof(comboDepth));
            if (comboStepPercent < 0 || comboStepPercent > 1000)
                throw new ArgumentOutOfRangeException(nameof(comboStepPercent));
            if (maximumMultiplierPercent < 100 ||
                maximumMultiplierPercent > 10000)
                throw new ArgumentOutOfRangeException(
                    nameof(maximumMultiplierPercent));

            var multiplier = Math.Min(
                maximumMultiplierPercent,
                checked(100 + comboDepth * comboStepPercent));
            var score = (long)basePoints * multiplier / 100L;
            if (score > Int32.MaxValue)
                throw new OverflowException("Score event overflow.");
            return (int)score;
        }

        public static int AddScore(int currentScore, int delta)
        {
            if (currentScore < 0 || delta < 0)
                throw new ArgumentOutOfRangeException();
            var value = (long)currentScore + delta;
            if (value > Int32.MaxValue)
                throw new OverflowException("Total score overflow.");
            return (int)value;
        }
    }

    public enum LevelTerminalOutcome
    {
        None = 0,
        Win = 1,
        LoseMoves = 2,
        LoseTime = 3
    }

    public readonly struct LevelTerminalResolution
    {
        public LevelTerminalOutcome Outcome { get; }
        public bool FinalSettleComplete { get; }

        public LevelTerminalResolution(LevelTerminalOutcome outcome,
            bool finalSettleComplete)
        {
            Outcome = outcome;
            FinalSettleComplete = finalSettleComplete;
        }
    }

    public static class LevelTerminalArbitrator
    {
        public static LevelTerminalResolution Resolve(
            bool finalSettleComplete,
            GoalRegistry registry,
            GoalProgressSnapshot progress,
            LevelBudgetSnapshot budget)
        {
            if (registry == null) throw new ArgumentNullException(nameof(registry));
            if (progress == null) throw new ArgumentNullException(nameof(progress));
            var allRootsComplete = registry.RootGoalIds.All(x =>
                GoalProgressPlanner.IsComplete(registry, progress, x));
            return Resolve(finalSettleComplete, allRootsComplete, budget);
        }

        public static LevelTerminalResolution Resolve(
            bool finalSettleComplete,
            bool allGoalsComplete,
            LevelBudgetSnapshot budget)
        {
            if (budget == null) throw new ArgumentNullException(nameof(budget));

            if (!finalSettleComplete)
                return new LevelTerminalResolution(
                    LevelTerminalOutcome.None, false);

            if (allGoalsComplete)
                return new LevelTerminalResolution(
                    LevelTerminalOutcome.Win, true);

            if (budget.TimeExpired)
                return new LevelTerminalResolution(
                    LevelTerminalOutcome.LoseTime, true);

            if (budget.MovesRemaining == 0)
                return new LevelTerminalResolution(
                    LevelTerminalOutcome.LoseMoves, true);

            return new LevelTerminalResolution(
                LevelTerminalOutcome.None, true);
        }
    }
}
