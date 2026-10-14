using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Match3.Engine.Runtime
{
    public sealed class VerifiedProgressionResult
    {
        public string ResultId { get; }
        public string GameId { get; }
        public string ProfileId { get; }
        public string ModeId { get; }
        public string LevelId { get; }
        public string FrontierLevelId { get; }
        public bool Won { get; }
        public bool FirstClear { get; }
        public bool Replay { get; }
        public int Score { get; }
        public int Stars { get; }
        public int AttemptOrdinal { get; }
        public long CanonicalDayId { get; }
        public string CanonicalWeekId { get; }
        public bool Verified { get; }

        public VerifiedProgressionResult(
            string resultId, string gameId, string profileId,
            string modeId, string levelId, string frontierLevelId,
            bool won, bool firstClear, bool replay,
            int score, int stars, int attemptOrdinal,
            long canonicalDayId, string canonicalWeekId,
            bool verified = true)
        {
            if (string.IsNullOrWhiteSpace(resultId))
                throw new ArgumentException("Result ID required.", nameof(resultId));
            if (!GameProfileCatalog.IsKnownId(gameId))
                throw new ArgumentException("Unknown game ID.", nameof(gameId));
            if (string.IsNullOrWhiteSpace(profileId))
                throw new ArgumentException("Profile ID required.", nameof(profileId));
            if (string.IsNullOrWhiteSpace(modeId))
                throw new ArgumentException("Mode ID required.", nameof(modeId));
            if (string.IsNullOrWhiteSpace(levelId) ||
                string.IsNullOrWhiteSpace(frontierLevelId))
                throw new ArgumentException("Level IDs required.");
            if (score < 0)
                throw new ArgumentOutOfRangeException(nameof(score));
            if (stars < 0 || stars > 3)
                throw new ArgumentOutOfRangeException(nameof(stars));
            if (!won && stars != 0)
                throw new ArgumentException("Lost result cannot award stars.");
            if (firstClear && (!won || replay))
                throw new ArgumentException(
                    "First clear must be a genuine non-replay win.");
            if (attemptOrdinal < 1 || attemptOrdinal > 1000000)
                throw new ArgumentOutOfRangeException(nameof(attemptOrdinal));
            if (canonicalDayId < 0)
                throw new ArgumentOutOfRangeException(nameof(canonicalDayId));
            if (string.IsNullOrWhiteSpace(canonicalWeekId))
                throw new ArgumentException("Canonical week ID required.",
                    nameof(canonicalWeekId));

            ResultId = resultId;
            GameId = gameId;
            ProfileId = profileId;
            ModeId = modeId;
            LevelId = levelId;
            FrontierLevelId = frontierLevelId;
            Won = won;
            FirstClear = firstClear;
            Replay = replay;
            Score = score;
            Stars = stars;
            AttemptOrdinal = attemptOrdinal;
            CanonicalDayId = canonicalDayId;
            CanonicalWeekId = canonicalWeekId;
            Verified = verified;
        }

        public bool IsFrontier =>
            StringComparer.Ordinal.Equals(LevelId, FrontierLevelId);
    }

    public sealed class WinStreakPolicy
    {
        public int MaximumStreak { get; }
        public int MaximumBonusTier { get; }
        public int MaximumShields { get; }

        public WinStreakPolicy(int maximumStreak,
            int maximumBonusTier, int maximumShields = 3)
        {
            if (maximumStreak < 1 || maximumStreak > 100000)
                throw new ArgumentOutOfRangeException(nameof(maximumStreak));
            if (maximumBonusTier < 1 ||
                maximumBonusTier > maximumStreak)
                throw new ArgumentOutOfRangeException(nameof(maximumBonusTier));
            if (maximumShields < 0 || maximumShields > 100)
                throw new ArgumentOutOfRangeException(nameof(maximumShields));
            MaximumStreak = maximumStreak;
            MaximumBonusTier = maximumBonusTier;
            MaximumShields = maximumShields;
        }
    }

    public sealed class WinStreakSnapshot
    {
        private readonly ReadOnlyCollection<string> _processedEventIds;

        public string GameId { get; }
        public string ProfileId { get; }
        public string ModeId { get; }
        public int Streak { get; }
        public int ShieldCharges { get; }
        public long HighWaterDayId { get; }
        public IReadOnlyList<string> ProcessedEventIds => _processedEventIds;

        public WinStreakSnapshot(string gameId, string profileId,
            string modeId, int streak = 0,
            int shieldCharges = 0, long highWaterDayId = 0,
            IEnumerable<string> processedEventIds = null)
        {
            if (!GameProfileCatalog.IsKnownId(gameId))
                throw new ArgumentException("Unknown game ID.", nameof(gameId));
            if (string.IsNullOrWhiteSpace(profileId))
                throw new ArgumentException("Profile ID required.", nameof(profileId));
            if (string.IsNullOrWhiteSpace(modeId))
                throw new ArgumentException("Mode ID required.", nameof(modeId));
            if (streak < 0 || streak > 100000)
                throw new ArgumentOutOfRangeException(nameof(streak));
            if (shieldCharges < 0 || shieldCharges > 100)
                throw new ArgumentOutOfRangeException(nameof(shieldCharges));
            if (highWaterDayId < 0)
                throw new ArgumentOutOfRangeException(nameof(highWaterDayId));
            GameId = gameId;
            ProfileId = profileId;
            ModeId = modeId;
            Streak = streak;
            ShieldCharges = shieldCharges;
            HighWaterDayId = highWaterDayId;
            _processedEventIds = new ReadOnlyCollection<string>(
                FreezeIds(processedEventIds));
        }

        private static List<string> FreezeIds(IEnumerable<string> ids)
        {
            var list = (ids ?? Enumerable.Empty<string>())
                .Take(100001).ToList();
            if (list.Count > 100000 ||
                list.Any(string.IsNullOrWhiteSpace) ||
                list.Distinct(StringComparer.Ordinal).Count() != list.Count)
                throw new ArgumentException("Invalid streak event IDs.");
            return list.OrderBy(x => x, StringComparer.Ordinal).ToList();
        }
    }

    public sealed class WinStreakPlan
    {
        public WinStreakSnapshot Result { get; }
        public bool Applied { get; }
        public int BonusTier { get; }
        public string RewardSourceId { get; }

        internal WinStreakPlan(WinStreakSnapshot result,
            bool applied, int bonusTier, string rewardSourceId)
        {
            Result = result;
            Applied = applied;
            BonusTier = bonusTier;
            RewardSourceId = rewardSourceId;
        }
    }

    public static class WinStreakPlanner
    {
        public static WinStreakPlan ApplyResult(
            WinStreakSnapshot snapshot,
            VerifiedProgressionResult result,
            WinStreakPolicy policy,
            bool useShieldOnLoss = false)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            if (result == null) throw new ArgumentNullException(nameof(result));
            if (policy == null) throw new ArgumentNullException(nameof(policy));
            if (!result.Verified)
                throw new InvalidOperationException(
                    "Unverified result cannot mutate win streak.");
            if (snapshot.Streak > policy.MaximumStreak ||
                snapshot.ShieldCharges > policy.MaximumShields)
                throw new InvalidOperationException(
                    "Win streak snapshot exceeds active policy bounds.");
            if (!StringComparer.Ordinal.Equals(snapshot.GameId, result.GameId) ||
                !StringComparer.Ordinal.Equals(snapshot.ProfileId, result.ProfileId) ||
                !StringComparer.Ordinal.Equals(snapshot.ModeId, result.ModeId))
                throw new InvalidOperationException(
                    "Win streak result belongs to another game/profile/mode.");
            if (result.CanonicalDayId < snapshot.HighWaterDayId)
                throw new InvalidOperationException(
                    "Win streak day rollback detected.");
            if (snapshot.ProcessedEventIds.Contains(result.ResultId))
                return new WinStreakPlan(
                    snapshot, false,
                    Math.Min(snapshot.Streak, policy.MaximumBonusTier),
                    null);

            var streak = snapshot.Streak;
            var shields = snapshot.ShieldCharges;
            string rewardSource = null;

            if (result.Replay || !result.IsFrontier)
            {
                // Receipt is consumed for replay-safety, but streak is unchanged.
            }
            else if (result.Won && result.FirstClear)
            {
                streak = Math.Min(policy.MaximumStreak, streak + 1);
                var tier = Math.Min(streak, policy.MaximumBonusTier);
                rewardSource = "streak:" + result.ResultId + ":tier:" + tier;
            }
            else if (!result.Won)
            {
                if (useShieldOnLoss && shields > 0)
                    shields--;
                else
                    streak = 0;
            }

            var next = new WinStreakSnapshot(
                snapshot.GameId,
                snapshot.ProfileId,
                snapshot.ModeId,
                streak,
                shields,
                Math.Max(snapshot.HighWaterDayId, result.CanonicalDayId),
                snapshot.ProcessedEventIds.Concat(new[] { result.ResultId }));
            return new WinStreakPlan(
                next, true,
                Math.Min(streak, policy.MaximumBonusTier),
                rewardSource);
        }

        public static WinStreakPlan RecordRetry(
            WinStreakSnapshot snapshot, string retryId,
            long canonicalDayId, WinStreakPolicy policy)
        {
            return Lifecycle(snapshot, retryId, canonicalDayId,
                policy, reset: false);
        }

        public static WinStreakPlan Reset(
            WinStreakSnapshot snapshot, string resetId,
            long canonicalDayId, WinStreakPolicy policy)
        {
            return Lifecycle(snapshot, resetId, canonicalDayId,
                policy, reset: true);
        }

        public static WinStreakPlan GrantShield(
            WinStreakSnapshot snapshot, string sourceId,
            int amount, WinStreakPolicy policy)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            if (policy == null) throw new ArgumentNullException(nameof(policy));
            if (string.IsNullOrWhiteSpace(sourceId))
                throw new ArgumentException("Shield source ID required.",
                    nameof(sourceId));
            if (snapshot.Streak > policy.MaximumStreak ||
                snapshot.ShieldCharges > policy.MaximumShields)
                throw new InvalidOperationException(
                    "Win streak snapshot exceeds active policy bounds.");
            if (amount < 1 || amount > 100)
                throw new ArgumentOutOfRangeException(nameof(amount));
            if (snapshot.ProcessedEventIds.Contains(sourceId))
                return new WinStreakPlan(
                    snapshot, false,
                    Math.Min(snapshot.Streak, policy.MaximumBonusTier), null);

            var shields = Math.Min(
                policy.MaximumShields, snapshot.ShieldCharges + amount);
            return new WinStreakPlan(
                new WinStreakSnapshot(
                    snapshot.GameId,
                    snapshot.ProfileId,
                    snapshot.ModeId,
                    snapshot.Streak, shields,
                    snapshot.HighWaterDayId,
                    snapshot.ProcessedEventIds.Concat(new[] { sourceId })),
                true,
                Math.Min(snapshot.Streak, policy.MaximumBonusTier),
                null);
        }

        private static WinStreakPlan Lifecycle(
            WinStreakSnapshot snapshot, string eventId,
            long canonicalDayId, WinStreakPolicy policy,
            bool reset)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            if (policy == null) throw new ArgumentNullException(nameof(policy));
            if (string.IsNullOrWhiteSpace(eventId))
                throw new ArgumentException("Streak lifecycle ID required.",
                    nameof(eventId));
            if (snapshot.Streak > policy.MaximumStreak ||
                snapshot.ShieldCharges > policy.MaximumShields)
                throw new InvalidOperationException(
                    "Win streak snapshot exceeds active policy bounds.");
            if (canonicalDayId < snapshot.HighWaterDayId)
                throw new InvalidOperationException(
                    "Win streak day rollback detected.");
            if (snapshot.ProcessedEventIds.Contains(eventId))
                return new WinStreakPlan(
                    snapshot, false,
                    Math.Min(snapshot.Streak, policy.MaximumBonusTier), null);

            var streak = reset ? 0 : snapshot.Streak;
            return new WinStreakPlan(
                new WinStreakSnapshot(
                    snapshot.GameId,
                    snapshot.ProfileId,
                    snapshot.ModeId,
                    streak,
                    snapshot.ShieldCharges,
                    Math.Max(snapshot.HighWaterDayId, canonicalDayId),
                    snapshot.ProcessedEventIds.Concat(new[] { eventId })),
                true,
                Math.Min(streak, policy.MaximumBonusTier),
                null);
        }
    }

    public enum ProgressMeterKind
    {
        StarChest = 0,
        GiftMeter = 1
    }

    public enum ProgressMeterState
    {
        Locked = 0,
        Claimable = 1,
        Claimed = 2
    }

    public enum ProgressMeterExpiryPolicy
    {
        Never = 0,
        ResetOnPeriodChange = 1
    }

    public sealed class ProgressMeterDefinition
    {
        public string MeterId { get; }
        public ProgressMeterKind Kind { get; }
        public int Target { get; }
        public int Cap { get; }
        public ProgressMeterExpiryPolicy ExpiryPolicy { get; }

        public ProgressMeterDefinition(string meterId,
            ProgressMeterKind kind, int target, int cap,
            ProgressMeterExpiryPolicy expiryPolicy)
        {
            if (string.IsNullOrWhiteSpace(meterId))
                throw new ArgumentException("Meter ID required.", nameof(meterId));
            if (!Enum.IsDefined(typeof(ProgressMeterKind), kind))
                throw new ArgumentOutOfRangeException(nameof(kind));
            if (target < 1 || cap < target || cap > 1000000000)
                throw new ArgumentOutOfRangeException(nameof(target));
            if (!Enum.IsDefined(typeof(ProgressMeterExpiryPolicy), expiryPolicy))
                throw new ArgumentOutOfRangeException(nameof(expiryPolicy));
            MeterId = meterId;
            Kind = kind;
            Target = target;
            Cap = cap;
            ExpiryPolicy = expiryPolicy;
        }
    }

    public sealed class ProgressMeterSnapshot
    {
        private readonly ReadOnlyCollection<string> _processedEventIds;
        private readonly ReadOnlyCollection<string> _claimIds;

        public string MeterId { get; }
        public string PeriodId { get; }
        public long PeriodOrdinal { get; }
        public int Progress { get; }
        public ProgressMeterState State { get; }
        public IReadOnlyList<string> ProcessedEventIds => _processedEventIds;
        public IReadOnlyList<string> ClaimIds => _claimIds;

        public ProgressMeterSnapshot(string meterId, string periodId,
            long periodOrdinal, int progress = 0,
            ProgressMeterState state = ProgressMeterState.Locked,
            IEnumerable<string> processedEventIds = null,
            IEnumerable<string> claimIds = null)
        {
            if (string.IsNullOrWhiteSpace(meterId) ||
                string.IsNullOrWhiteSpace(periodId))
                throw new ArgumentException("Meter and period IDs required.");
            if (periodOrdinal < 0)
                throw new ArgumentOutOfRangeException(nameof(periodOrdinal));
            if (progress < 0 || progress > 1000000000)
                throw new ArgumentOutOfRangeException(nameof(progress));
            if (!Enum.IsDefined(typeof(ProgressMeterState), state))
                throw new ArgumentOutOfRangeException(nameof(state));
            MeterId = meterId;
            PeriodId = periodId;
            PeriodOrdinal = periodOrdinal;
            Progress = progress;
            State = state;
            _processedEventIds =
                new ReadOnlyCollection<string>(
                    Freeze(processedEventIds, "meter event"));
            _claimIds =
                new ReadOnlyCollection<string>(
                    Freeze(claimIds, "meter claim"));
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

    public sealed class ProgressMeterPlan
    {
        public ProgressMeterSnapshot Result { get; }
        public bool Applied { get; }
        public string RewardSourceId { get; }

        internal ProgressMeterPlan(ProgressMeterSnapshot result,
            bool applied, string rewardSourceId = null)
        {
            Result = result;
            Applied = applied;
            RewardSourceId = rewardSourceId;
        }
    }

    public static class ProgressMeterPlanner
    {
        public static ProgressMeterPlan Add(
            ProgressMeterDefinition definition,
            ProgressMeterSnapshot snapshot,
            string eventId, string periodId,
            long periodOrdinal, int amount)
        {
            RequireMatching(definition, snapshot);
            if (string.IsNullOrWhiteSpace(eventId))
                throw new ArgumentException("Meter event ID required.",
                    nameof(eventId));
            if (amount < 1 || amount > 1000000000)
                throw new ArgumentOutOfRangeException(nameof(amount));

            var current = ReconcilePeriod(
                definition, snapshot, periodId, periodOrdinal);
            if (current.ProcessedEventIds.Contains(eventId))
                return new ProgressMeterPlan(current, false);
            if (current.State == ProgressMeterState.Claimed)
                return new ProgressMeterPlan(current, false);

            var progress = (int)Math.Min(
                definition.Cap, (long)current.Progress + amount);
            var state = progress >= definition.Target
                ? ProgressMeterState.Claimable
                : ProgressMeterState.Locked;

            return new ProgressMeterPlan(
                new ProgressMeterSnapshot(
                    current.MeterId,
                    current.PeriodId,
                    current.PeriodOrdinal,
                    progress,
                    state,
                    current.ProcessedEventIds.Concat(new[] { eventId }),
                    current.ClaimIds),
                true);
        }

        public static ProgressMeterPlan Claim(
            ProgressMeterDefinition definition,
            ProgressMeterSnapshot snapshot,
            string claimId, string periodId,
            long periodOrdinal)
        {
            RequireMatching(definition, snapshot);
            if (string.IsNullOrWhiteSpace(claimId))
                throw new ArgumentException("Meter claim ID required.",
                    nameof(claimId));

            var current = ReconcilePeriod(
                definition, snapshot, periodId, periodOrdinal);
            if (current.ClaimIds.Contains(claimId))
                return new ProgressMeterPlan(current, false);
            if (current.State == ProgressMeterState.Claimed)
                return new ProgressMeterPlan(current, false);
            if (current.State != ProgressMeterState.Claimable)
                throw new InvalidOperationException(
                    "Progress meter is not claimable.");

            return new ProgressMeterPlan(
                new ProgressMeterSnapshot(
                    current.MeterId,
                    current.PeriodId,
                    current.PeriodOrdinal,
                    current.Progress,
                    ProgressMeterState.Claimed,
                    current.ProcessedEventIds,
                    current.ClaimIds.Concat(new[] { claimId })),
                true,
                "meter:" + current.MeterId + ":" +
                    current.PeriodId + ":" + claimId);
        }

        private static ProgressMeterSnapshot ReconcilePeriod(
            ProgressMeterDefinition definition,
            ProgressMeterSnapshot snapshot,
            string periodId, long periodOrdinal)
        {
            if (string.IsNullOrWhiteSpace(periodId))
                throw new ArgumentException("Meter period ID required.",
                    nameof(periodId));
            if (periodOrdinal < snapshot.PeriodOrdinal)
                throw new InvalidOperationException(
                    "Progress meter period rollback detected.");
            if (periodOrdinal == snapshot.PeriodOrdinal)
            {
                if (!StringComparer.Ordinal.Equals(
                    snapshot.PeriodId, periodId))
                    throw new InvalidOperationException(
                        "Meter period ID changed without ordinal advance.");
                return snapshot;
            }

            if (definition.ExpiryPolicy ==
                ProgressMeterExpiryPolicy.Never)
                return new ProgressMeterSnapshot(
                    snapshot.MeterId,
                    periodId,
                    periodOrdinal,
                    snapshot.Progress,
                    snapshot.State,
                    snapshot.ProcessedEventIds,
                    snapshot.ClaimIds);

            return new ProgressMeterSnapshot(
                snapshot.MeterId,
                periodId,
                periodOrdinal);
        }

        private static void RequireMatching(
            ProgressMeterDefinition definition,
            ProgressMeterSnapshot snapshot)
        {
            if (definition == null)
                throw new ArgumentNullException(nameof(definition));
            if (snapshot == null)
                throw new ArgumentNullException(nameof(snapshot));
            if (!StringComparer.Ordinal.Equals(
                definition.MeterId, snapshot.MeterId))
                throw new InvalidOperationException(
                    "Progress meter definition/snapshot mismatch.");
            if (snapshot.Progress > definition.Cap)
                throw new InvalidOperationException(
                    "Progress meter snapshot exceeds cap.");
            if (snapshot.State == ProgressMeterState.Locked &&
                snapshot.Progress >= definition.Target)
                throw new InvalidOperationException(
                    "Locked meter snapshot already reached its target.");
            if (snapshot.State == ProgressMeterState.Claimable &&
                snapshot.Progress < definition.Target)
                throw new InvalidOperationException(
                    "Claimable meter snapshot is below target.");
            if (snapshot.State == ProgressMeterState.Claimed &&
                snapshot.Progress < definition.Target)
                throw new InvalidOperationException(
                    "Claimed meter snapshot is below target.");
        }
    }

    public enum RewardSourceKind
    {
        FirstClear = 0,
        Gift = 1,
        Streak = 2,
        Chest = 3,
        Collection = 4,
        Event = 5,
        Replay = 6,
        Purchase = 7,
        RewardedVerified = 8,
        Recovery = 9,
        Daily = 10,
        Task = 11,
        Achievement = 12,
        Pack = 13
    }

    public sealed class RewardResourceDelta
    {
        public string ResourceId { get; }
        public int Delta { get; }

        public RewardResourceDelta(string resourceId, int delta)
        {
            if (string.IsNullOrWhiteSpace(resourceId))
                throw new ArgumentException("Reward resource ID required.",
                    nameof(resourceId));
            if (delta == 0 || delta < -1000000000 ||
                delta > 1000000000)
                throw new ArgumentOutOfRangeException(nameof(delta));
            ResourceId = resourceId;
            Delta = delta;
        }
    }

    public sealed class RewardGrantRequest
    {
        private readonly ReadOnlyCollection<RewardResourceDelta> _deltas;

        public string SourceId { get; }
        public string GameId { get; }
        public RewardSourceKind SourceKind { get; }
        public IReadOnlyList<RewardResourceDelta> Deltas => _deltas;

        public RewardGrantRequest(string sourceId, string gameId,
            RewardSourceKind sourceKind,
            IEnumerable<RewardResourceDelta> deltas)
        {
            if (string.IsNullOrWhiteSpace(sourceId))
                throw new ArgumentException("Reward source ID required.",
                    nameof(sourceId));
            if (!GameProfileCatalog.IsKnownId(gameId))
                throw new ArgumentException("Unknown game ID.", nameof(gameId));
            if (!Enum.IsDefined(typeof(RewardSourceKind), sourceKind))
                throw new ArgumentOutOfRangeException(nameof(sourceKind));
            if (deltas == null)
                throw new ArgumentNullException(nameof(deltas));

            var array = deltas.Take(65).ToArray();
            if (array.Length < 1 || array.Length > 64 ||
                array.Any(x => x == null) ||
                array.GroupBy(x => x.ResourceId, StringComparer.Ordinal)
                    .Any(x => x.Count() > 1))
                throw new ArgumentException(
                    "Reward grant requires 1..64 unique resource deltas.",
                    nameof(deltas));
            SourceId = sourceId;
            GameId = gameId;
            SourceKind = sourceKind;
            _deltas = new ReadOnlyCollection<RewardResourceDelta>(
                array.OrderBy(x => x.ResourceId, StringComparer.Ordinal)
                    .ToList());
        }
    }

    public sealed class RewardGrantRecord
    {
        public RewardGrantRequest Request { get; }

        public RewardGrantRecord(RewardGrantRequest request)
        {
            Request = request ??
                throw new ArgumentNullException(nameof(request));
        }
    }

    public sealed class UnifiedRewardSnapshot
    {
        private readonly ReadOnlyDictionary<string, RewardGrantRecord> _records;

        public GameEconomySnapshot Economy { get; }
        public IReadOnlyDictionary<string, RewardGrantRecord> Records =>
            _records;

        public UnifiedRewardSnapshot(GameEconomySnapshot economy = null,
            IEnumerable<RewardGrantRecord> records = null)
        {
            Economy = economy ?? new GameEconomySnapshot();
            var map = new Dictionary<string, RewardGrantRecord>(
                StringComparer.Ordinal);
            foreach (var record in (records ??
                Enumerable.Empty<RewardGrantRecord>()).Take(100001))
            {
                if (map.Count >= 100000)
                    throw new ArgumentException(
                        "Reward record history limit exceeded.");
                if (record == null ||
                    map.ContainsKey(record.Request.SourceId))
                    throw new ArgumentException(
                        "Invalid reward grant record.");
                map.Add(record.Request.SourceId, record);
            }
            _records =
                new ReadOnlyDictionary<string, RewardGrantRecord>(map);
        }
    }

    public sealed class UnifiedRewardPlan
    {
        public UnifiedRewardSnapshot Result { get; }
        public bool Applied { get; }

        internal UnifiedRewardPlan(
            UnifiedRewardSnapshot result, bool applied)
        {
            Result = result;
            Applied = applied;
        }
    }

    public static class UnifiedRewardPlanner
    {
        public static UnifiedRewardPlan Apply(
            UnifiedRewardSnapshot snapshot,
            RewardGrantRequest request)
        {
            if (snapshot == null)
                throw new ArgumentNullException(nameof(snapshot));
            if (request == null)
                throw new ArgumentNullException(nameof(request));

            RewardGrantRecord existing;
            if (snapshot.Records.TryGetValue(
                request.SourceId, out existing))
            {
                if (!Same(existing.Request, request))
                    throw new InvalidOperationException(
                        "Reward source ID is bound to another grant.");
                return new UnifiedRewardPlan(snapshot, false);
            }

            var economy = snapshot.Economy;
            foreach (var delta in request.Deltas)
            {
                var componentSource =
                    "reward:" + request.SourceId + ":" + delta.ResourceId;
                var tx = new GameEconomyTransaction(
                    componentSource,
                    request.GameId,
                    delta.ResourceId,
                    Math.Abs(delta.Delta),
                    delta.Delta > 0
                        ? GameEconomyMutationKind.Credit
                        : GameEconomyMutationKind.Debit);
                economy = GameEconomyPlanner.Apply(
                    economy, tx).Result;
            }

            var records = snapshot.Records.Values.Concat(
                new[] { new RewardGrantRecord(request) });
            return new UnifiedRewardPlan(
                new UnifiedRewardSnapshot(economy, records),
                true);
        }

        private static bool Same(
            RewardGrantRequest a, RewardGrantRequest b)
        {
            if (!StringComparer.Ordinal.Equals(a.SourceId, b.SourceId) ||
                !StringComparer.Ordinal.Equals(a.GameId, b.GameId) ||
                a.SourceKind != b.SourceKind ||
                a.Deltas.Count != b.Deltas.Count)
                return false;

            for (var i = 0; i < a.Deltas.Count; i++)
            {
                if (!StringComparer.Ordinal.Equals(
                        a.Deltas[i].ResourceId,
                        b.Deltas[i].ResourceId) ||
                    a.Deltas[i].Delta != b.Deltas[i].Delta)
                    return false;
            }
            return true;
        }
    }
}
