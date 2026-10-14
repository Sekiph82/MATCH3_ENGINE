using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Match3.Engine.Runtime
{
    public sealed class GoalProgressEvent
    {
        public string EventId { get; }
        public int Sequence { get; }
        public string GoalId { get; }
        public int Delta { get; }
        public int ScoreDelta { get; }

        public GoalProgressEvent(string eventId, int sequence,
            string goalId, int delta, int scoreDelta = 0)
        {
            if (string.IsNullOrWhiteSpace(eventId))
                throw new ArgumentException("Progress event ID required.", nameof(eventId));
            if (sequence < 1) throw new ArgumentOutOfRangeException(nameof(sequence));
            if (string.IsNullOrWhiteSpace(goalId))
                throw new ArgumentException("Goal ID required.", nameof(goalId));
            if (delta < 0 || delta > 1000000)
                throw new ArgumentOutOfRangeException(nameof(delta));
            if (scoreDelta < 0)
                throw new ArgumentOutOfRangeException(nameof(scoreDelta));
            EventId = eventId;
            Sequence = sequence;
            GoalId = goalId;
            Delta = delta;
            ScoreDelta = scoreDelta;
        }
    }

    public sealed class GoalProgressSnapshot
    {
        private readonly ReadOnlyDictionary<string, int> _counts;
        private readonly ReadOnlyCollection<string> _processedEventIds;

        public IReadOnlyDictionary<string, int> Counts => _counts;
        public IReadOnlyList<string> ProcessedEventIds => _processedEventIds;
        public int LastSequence { get; }
        public int Score { get; }

        internal GoalProgressSnapshot(
            IDictionary<string, int> counts,
            IEnumerable<string> processedEventIds,
            int lastSequence, int score)
        {
            if (counts == null) throw new ArgumentNullException(nameof(counts));
            if (counts.Any(x => string.IsNullOrWhiteSpace(x.Key) ||
                x.Value < 0 || x.Value > 1000000))
                throw new ArgumentException("Invalid goal progress counts.", nameof(counts));
            var ids = (processedEventIds ?? Enumerable.Empty<string>())
                .Take(100001).ToList();
            if (ids.Count > 100000 || ids.Any(string.IsNullOrWhiteSpace) ||
                ids.Distinct(StringComparer.Ordinal).Count() != ids.Count)
                throw new ArgumentException("Invalid progress event IDs.",
                    nameof(processedEventIds));
            if (lastSequence < 0) throw new ArgumentOutOfRangeException(nameof(lastSequence));
            if (score < 0) throw new ArgumentOutOfRangeException(nameof(score));

            _counts = new ReadOnlyDictionary<string, int>(
                new Dictionary<string, int>(counts, StringComparer.Ordinal));
            _processedEventIds = new ReadOnlyCollection<string>(
                ids.OrderBy(x => x, StringComparer.Ordinal).ToList());
            LastSequence = lastSequence;
            Score = score;
        }
    }

    public readonly struct GoalProgressView
    {
        public string GoalId { get; }
        public int Current { get; }
        public int Required { get; }
        public bool Complete { get; }

        public GoalProgressView(string goalId, int current,
            int required, bool complete)
        {
            GoalId = goalId;
            Current = current;
            Required = required;
            Complete = complete;
        }
    }

    public sealed class GoalProgressPlan
    {
        public GoalProgressSnapshot Result { get; }
        public bool Applied { get; }

        internal GoalProgressPlan(GoalProgressSnapshot result, bool applied)
        {
            Result = result ?? throw new ArgumentNullException(nameof(result));
            Applied = applied;
        }
    }

    public static class GoalProgressPlanner
    {
        public static GoalProgressSnapshot Create(GoalRegistry registry)
        {
            if (registry == null) throw new ArgumentNullException(nameof(registry));
            return new GoalProgressSnapshot(
                registry.ById.Keys.ToDictionary(x => x, x => 0,
                    StringComparer.Ordinal),
                new string[0], 0, 0);
        }

        public static GoalProgressPlan Apply(GoalRegistry registry,
            GoalProgressSnapshot snapshot, GoalProgressEvent progressEvent)
        {
            if (registry == null) throw new ArgumentNullException(nameof(registry));
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            if (progressEvent == null) throw new ArgumentNullException(nameof(progressEvent));

            if (snapshot.ProcessedEventIds.Contains(progressEvent.EventId))
                return new GoalProgressPlan(snapshot, false);
            if (progressEvent.Sequence != snapshot.LastSequence + 1)
                throw new InvalidOperationException(
                    "Progress event sequence must be contiguous.");
            GoalDefinition goal;
            if (!registry.ById.TryGetValue(progressEvent.GoalId, out goal))
                throw new ArgumentException("Unknown goal ID.", nameof(progressEvent));
            if (goal.Kind == GoalKind.Composite)
                throw new InvalidOperationException(
                    "Composite goal progress is derived from child goals.");

            var counts = snapshot.Counts.ToDictionary(x => x.Key, x => x.Value,
                StringComparer.Ordinal);
            int current;
            if (!counts.TryGetValue(goal.GoalId, out current))
                throw new InvalidOperationException(
                    "Progress snapshot is missing a registered goal.");
            counts[goal.GoalId] = Math.Min(
                goal.RequiredCount,
                checked(current + progressEvent.Delta));
            var score = DeterministicScorePlanner.AddScore(
                snapshot.Score, progressEvent.ScoreDelta);
            return new GoalProgressPlan(
                new GoalProgressSnapshot(
                    counts,
                    snapshot.ProcessedEventIds.Concat(
                        new[] { progressEvent.EventId }),
                    progressEvent.Sequence,
                    score),
                true);
        }

        public static bool IsComplete(GoalRegistry registry,
            GoalProgressSnapshot snapshot, string goalId)
        {
            if (registry == null) throw new ArgumentNullException(nameof(registry));
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            GoalDefinition goal;
            if (!registry.ById.TryGetValue(goalId, out goal))
                throw new ArgumentException("Unknown goal ID.", nameof(goalId));

            if (goal.Kind == GoalKind.Composite)
                return goal.ChildGoalIds.All(x =>
                    IsComplete(registry, snapshot, x));

            int count;
            return snapshot.Counts.TryGetValue(goalId, out count) &&
                count >= goal.RequiredCount;
        }

        public static IReadOnlyList<GoalProgressView> ProjectViews(
            GoalRegistry registry, GoalProgressSnapshot snapshot)
        {
            if (registry == null) throw new ArgumentNullException(nameof(registry));
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            var views = new List<GoalProgressView>();
            foreach (var goal in registry.ById.Values
                .OrderBy(x => x.GoalId, StringComparer.Ordinal))
            {
                if (goal.Kind == GoalKind.Composite)
                {
                    var completed = goal.ChildGoalIds.Count(x =>
                        IsComplete(registry, snapshot, x));
                    views.Add(new GoalProgressView(
                        goal.GoalId, completed, goal.ChildGoalIds.Count,
                        completed == goal.ChildGoalIds.Count));
                }
                else
                {
                    int current;
                    snapshot.Counts.TryGetValue(goal.GoalId, out current);
                    views.Add(new GoalProgressView(
                        goal.GoalId, current, goal.RequiredCount,
                        current >= goal.RequiredCount));
                }
            }
            return new ReadOnlyCollection<GoalProgressView>(views);
        }
    }

    public sealed class GoalCapacity
    {
        public GoalKind Kind { get; }
        public string TargetId { get; }
        public int MaximumAchievable { get; }

        public GoalCapacity(GoalKind kind, string targetId, int maximumAchievable)
        {
            if (kind == GoalKind.Composite ||
                !Enum.IsDefined(typeof(GoalKind), kind))
                throw new ArgumentOutOfRangeException(nameof(kind));
            if (string.IsNullOrWhiteSpace(targetId))
                throw new ArgumentException("Capacity target ID required.", nameof(targetId));
            if (maximumAchievable < 0 || maximumAchievable > 1000000)
                throw new ArgumentOutOfRangeException(nameof(maximumAchievable));
            Kind = kind;
            TargetId = targetId;
            MaximumAchievable = maximumAchievable;
        }

        internal string Key =>
            ((int)Kind).ToString() + "|" + TargetId;
    }

    public static class LevelGoalValidator
    {
        public static void RequirePlayable(GoalRegistry registry,
            IEnumerable<GoalCapacity> capacities)
        {
            if (registry == null) throw new ArgumentNullException(nameof(registry));
            if (capacities == null) throw new ArgumentNullException(nameof(capacities));
            var list = capacities.Take(1025).ToList();
            if (list.Count > 1024)
                throw new ArgumentException("Goal capacity registry limit exceeded.",
                    nameof(capacities));
            if (list.Any(x => x == null))
                throw new ArgumentException("Null goal capacity.", nameof(capacities));
            if (list.GroupBy(x => x.Key, StringComparer.Ordinal)
                .Any(x => x.Count() > 1))
                throw new ArgumentException("Duplicate goal capacity.", nameof(capacities));
            var map = list.ToDictionary(x => x.Key, x => x,
                StringComparer.Ordinal);

            foreach (var goal in registry.ById.Values)
            {
                if (goal.Kind == GoalKind.Composite) continue;
                var key = ((int)goal.Kind).ToString() + "|" + goal.TargetId;
                GoalCapacity capacity;
                if (!map.TryGetValue(key, out capacity))
                    throw new InvalidOperationException(
                        "Goal target is unavailable before play: " + goal.GoalId);
                if (capacity.MaximumAchievable < goal.RequiredCount)
                    throw new InvalidOperationException(
                        "Goal target count is impossible before play: " + goal.GoalId);
            }
        }
    }

    public readonly struct GoalQaFixture
    {
        public int Index { get; }
        public int Moves { get; }
        public int BasePoints { get; }
        public int ComboDepth { get; }
        public bool GoalsComplete { get; }

        public GoalQaFixture(int index, int moves,
            int basePoints, int comboDepth, bool goalsComplete)
        {
            Index = index;
            Moves = moves;
            BasePoints = basePoints;
            ComboDepth = comboDepth;
            GoalsComplete = goalsComplete;
        }
    }

    public static class GoalQaFixtureGenerator
    {
        public static IReadOnlyList<GoalQaFixture> Generate(
            uint seed, int count)
        {
            if (seed == 0) throw new ArgumentOutOfRangeException(nameof(seed));
            if (count < 1 || count > 10000)
                throw new ArgumentOutOfRangeException(nameof(count));
            var rng = new SeededRandom(seed);
            var fixtures = new List<GoalQaFixture>(count);
            for (var i = 0; i < count; i++)
            {
                fixtures.Add(new GoalQaFixture(
                    i,
                    1 + rng.NextIndex(50),
                    10 + rng.NextIndex(10000),
                    rng.NextIndex(16),
                    rng.NextIndex(2) == 1));
            }
            return new ReadOnlyCollection<GoalQaFixture>(fixtures);
        }

        public static string Hash(IEnumerable<GoalQaFixture> fixtures)
        {
            if (fixtures == null) throw new ArgumentNullException(nameof(fixtures));
            var list = fixtures.Take(10001).ToList();
            if (list.Count > 10000)
                throw new ArgumentException("QA fixture hash limit exceeded.",
                    nameof(fixtures));
            var canonical = string.Join(";", list.Select(x =>
                x.Index + "|" + x.Moves + "|" + x.BasePoints + "|" +
                x.ComboDepth + "|" + (x.GoalsComplete ? 1 : 0)));
            using (var sha = SHA256.Create())
            {
                var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(canonical));
                var builder = new StringBuilder(bytes.Length * 2);
                foreach (var b in bytes) builder.Append(b.ToString("x2"));
                return builder.ToString();
            }
        }
    }

    public sealed class LevelResultAttempt
    {
        public string AttemptId { get; }
        public string LevelId { get; }
        public bool Won { get; }
        public int Score { get; }
        public int Stars { get; }
        public bool Verified { get; }

        public LevelResultAttempt(string attemptId, string levelId,
            bool won, int score, int stars, bool verified = true)
        {
            if (string.IsNullOrWhiteSpace(attemptId))
                throw new ArgumentException("Attempt ID required.", nameof(attemptId));
            if (string.IsNullOrWhiteSpace(levelId))
                throw new ArgumentException("Level ID required.", nameof(levelId));
            if (score < 0) throw new ArgumentOutOfRangeException(nameof(score));
            if (stars < 0 || stars > 3)
                throw new ArgumentOutOfRangeException(nameof(stars));
            if (!won && stars != 0)
                throw new ArgumentException("Lost attempt cannot award stars.");
            AttemptId = attemptId;
            LevelId = levelId;
            Won = won;
            Score = score;
            Stars = stars;
            Verified = verified;
        }
    }

    public sealed class LevelProgressionSnapshot
    {
        private readonly ReadOnlyCollection<string> _processedAttemptIds;

        public string LevelId { get; }
        public bool FirstClearGranted { get; }
        public int HighestStars { get; }
        public int BestScore { get; }
        public IReadOnlyList<string> ProcessedAttemptIds => _processedAttemptIds;

        public LevelProgressionSnapshot(string levelId,
            bool firstClearGranted = false,
            int highestStars = 0, int bestScore = 0,
            IEnumerable<string> processedAttemptIds = null)
        {
            if (string.IsNullOrWhiteSpace(levelId))
                throw new ArgumentException("Level ID required.", nameof(levelId));
            if (highestStars < 0 || highestStars > 3)
                throw new ArgumentOutOfRangeException(nameof(highestStars));
            if (bestScore < 0)
                throw new ArgumentOutOfRangeException(nameof(bestScore));
            var ids = (processedAttemptIds ?? Enumerable.Empty<string>())
                .Take(100001).ToList();
            if (ids.Count > 100000 ||
                ids.Any(string.IsNullOrWhiteSpace) ||
                ids.Distinct(StringComparer.Ordinal).Count() != ids.Count)
                throw new ArgumentException("Invalid attempt IDs.",
                    nameof(processedAttemptIds));
            LevelId = levelId;
            FirstClearGranted = firstClearGranted;
            HighestStars = highestStars;
            BestScore = bestScore;
            _processedAttemptIds = new ReadOnlyCollection<string>(
                ids.OrderBy(x => x, StringComparer.Ordinal).ToList());
        }
    }

    public sealed class FirstClearProgressionPlan
    {
        private readonly ReadOnlyCollection<string> _rewardSourceIds;

        public LevelProgressionSnapshot Result { get; }
        public bool Applied { get; }
        public bool FirstClearApplied { get; }
        public bool PersonalBestImproved { get; }
        public bool HighestStarsImproved { get; }
        public IReadOnlyList<string> RewardSourceIds => _rewardSourceIds;

        internal FirstClearProgressionPlan(LevelProgressionSnapshot result,
            bool applied, bool firstClearApplied,
            bool personalBestImproved, bool highestStarsImproved,
            IEnumerable<string> rewardSourceIds)
        {
            Result = result ?? throw new ArgumentNullException(nameof(result));
            Applied = applied;
            FirstClearApplied = firstClearApplied;
            PersonalBestImproved = personalBestImproved;
            HighestStarsImproved = highestStarsImproved;
            _rewardSourceIds = new ReadOnlyCollection<string>(
                rewardSourceIds.ToList());
        }
    }

    public static class FirstClearProgressionPlanner
    {
        public static FirstClearProgressionPlan Apply(
            LevelProgressionSnapshot snapshot,
            LevelResultAttempt attempt)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            if (attempt == null) throw new ArgumentNullException(nameof(attempt));
            if (!StringComparer.Ordinal.Equals(snapshot.LevelId, attempt.LevelId))
                throw new ArgumentException("Attempt level does not match progression.");
            if (!attempt.Verified)
                throw new InvalidOperationException(
                    "Unverified attempt cannot mutate progression.");
            if (snapshot.ProcessedAttemptIds.Contains(attempt.AttemptId))
                return new FirstClearProgressionPlan(
                    snapshot, false, false, false, false, new string[0]);

            var first = attempt.Won && !snapshot.FirstClearGranted;
            var best = attempt.Won && attempt.Score > snapshot.BestScore;
            var stars = attempt.Won && attempt.Stars > snapshot.HighestStars;
            var result = new LevelProgressionSnapshot(
                snapshot.LevelId,
                snapshot.FirstClearGranted || first,
                stars ? attempt.Stars : snapshot.HighestStars,
                best ? attempt.Score : snapshot.BestScore,
                snapshot.ProcessedAttemptIds.Concat(new[] { attempt.AttemptId }));

            var rewards = first
                ? new[]
                {
                    "firstclear:" + snapshot.LevelId + ":coins",
                    "firstclear:" + snapshot.LevelId + ":tasks",
                    "firstclear:" + snapshot.LevelId + ":album",
                    "firstclear:" + snapshot.LevelId + ":progression"
                }
                : new string[0];

            return new FirstClearProgressionPlan(
                result, true, first, best, stars, rewards);
        }
    }

    public sealed class PostWinPolicy
    {
        public bool ConvertLeftoverMoves { get; }
        public int MaximumConvertibleMoves { get; }
        public int ScorePerMove { get; }

        public PostWinPolicy(bool convertLeftoverMoves,
            int maximumConvertibleMoves = 20, int scorePerMove = 100)
        {
            if (maximumConvertibleMoves < 0 ||
                maximumConvertibleMoves > 1000)
                throw new ArgumentOutOfRangeException(
                    nameof(maximumConvertibleMoves));
            if (scorePerMove < 0 || scorePerMove > 1000000)
                throw new ArgumentOutOfRangeException(nameof(scorePerMove));
            ConvertLeftoverMoves = convertLeftoverMoves;
            MaximumConvertibleMoves = maximumConvertibleMoves;
            ScorePerMove = scorePerMove;
        }
    }

    public sealed class PostWinResolution
    {
        private readonly ReadOnlyCollection<FinalePresentationEvent> _presentationEvents;

        public WinFinalePlan AuthorityPlan { get; }
        public IReadOnlyList<FinalePresentationEvent> PresentationEvents =>
            _presentationEvents;

        internal PostWinResolution(WinFinalePlan authorityPlan,
            IEnumerable<FinalePresentationEvent> presentationEvents)
        {
            AuthorityPlan = authorityPlan ??
                throw new ArgumentNullException(nameof(authorityPlan));
            _presentationEvents =
                new ReadOnlyCollection<FinalePresentationEvent>(
                    presentationEvents.ToList());
        }
    }

    public static class PostWinPlanner
    {
        public static PostWinResolution Resolve(
            CommittedWinSnapshot committed,
            int leftoverMoves,
            PostWinPolicy policy,
            bool skipPresentation = false,
            bool reducedMotion = false)
        {
            if (committed == null) throw new ArgumentNullException(nameof(committed));
            if (policy == null) throw new ArgumentNullException(nameof(policy));
            if (leftoverMoves < 0)
                throw new ArgumentOutOfRangeException(nameof(leftoverMoves));

            var converted = policy.ConvertLeftoverMoves
                ? Math.Min(leftoverMoves, policy.MaximumConvertibleMoves)
                : 0;
            var authority = WinFinalePlanner.Build(
                committed,
                converted,
                policy.ConvertLeftoverMoves,
                policy.ScorePerMove);

            var events = skipPresentation || reducedMotion
                ? new FinalePresentationEvent[0]
                : authority.Events.ToArray();
            return new PostWinResolution(authority, events);
        }
    }
}
