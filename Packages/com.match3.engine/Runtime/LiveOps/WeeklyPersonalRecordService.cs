using System;
using System.Collections.Generic;
using System.Globalization;

namespace Match3.Engine.Runtime.LiveOps
{
    // ENG-M20-012: weekly per-player personal-record challenge.
    //
    // - Window: ISO-8601 week ("2026-W46"), Monday 00:00 UTC to next Monday 00:00 UTC, computed only
    //   from trusted UTC instants (DateTimeKind.Utc). Local/Unspecified instants are rejected; DST cannot apply.
    // - Records reuse ENG-M08-015: input is VerifiedProgressionResult, storage is PersonalBestRecord with the
    //   same key (game|profile|mode|level|week) and durable previous score/result ID.
    // - First verified completion in a window = participation. A strictly higher verified score replaces the
    //   record. Improvements of at least MinImprovement earn one capped, idempotently keyed grant request
    //   while the window is still open. No ranking, no other players' data.
    // - Grants: ENG-M16-011 ledger is not on main yet, so this emits WeeklyRecordGrantRequest (idempotency key +
    //   amount) for that ledger to consume; each key is emitted at most once by this service.
    // - Gate: FeatureFlagGate (weekly_personal_best is a restricted, consent-gated feature).
    //
    // ponytail: state is in-memory per player (game, profile) and not thread-safe; persistence/snapshot
    // export comes with the ENG-M16-011 ledger integration. Idempotency keys stay deterministic across restarts.
    public static class IsoWeekWindow
    {
        public static string WeekId(DateTime utcInstant)
        {
            if (utcInstant.Kind != DateTimeKind.Utc)
                throw new ArgumentException("Week window requires a DateTimeKind.Utc instant.", nameof(utcInstant));
            var monday = MondayOf(utcInstant.Date);
            var thursday = monday.AddDays(3); // ISO week-year is the year holding the week's Thursday
            var week = (thursday.DayOfYear - 1) / 7 + 1;
            return thursday.Year.ToString("D4", CultureInfo.InvariantCulture) + "-W" +
                week.ToString("D2", CultureInfo.InvariantCulture);
        }

        public static bool TryGetStartUtc(string weekId, out DateTime startUtc)
        {
            startUtc = default(DateTime);
            if (weekId == null || weekId.Length != 8 || weekId[4] != '-' || weekId[5] != 'W' ||
                !int.TryParse(weekId.Substring(0, 4), NumberStyles.None, CultureInfo.InvariantCulture, out var year) ||
                !int.TryParse(weekId.Substring(6, 2), NumberStyles.None, CultureInfo.InvariantCulture, out var week) ||
                year < 1000 || year > 9998 || week < 1 || week > 53)
                return false;
            var start = MondayOf(new DateTime(year, 1, 4, 0, 0, 0, DateTimeKind.Utc)).AddDays(7 * (week - 1));
            if (WeekId(start) != weekId) return false; // e.g. W53 in a 52-week year
            startUtc = start;
            return true;
        }

        public static DateTime StartUtc(string weekId) =>
            TryGetStartUtc(weekId, out var start) ? start : throw new FormatException("Invalid ISO week ID.");

        public static DateTime EndUtc(string weekId) => StartUtc(weekId).AddDays(7);

        private static DateTime MondayOf(DateTime date) =>
            date.AddDays(-(((int)date.DayOfWeek + 6) % 7));
    }

    public sealed class WeeklyPersonalRecordPolicy
    {
        public int MaxImprovementRewardsPerWindow { get; }
        public int MinImprovement { get; }
        public string RewardId { get; }
        public int RewardAmount { get; }

        public WeeklyPersonalRecordPolicy(int maxImprovementRewardsPerWindow, int minImprovement,
            string rewardId, int rewardAmount)
        {
            if (maxImprovementRewardsPerWindow < 0 || maxImprovementRewardsPerWindow > 50)
                throw new ArgumentOutOfRangeException(nameof(maxImprovementRewardsPerWindow));
            if (minImprovement < 1)
                throw new ArgumentOutOfRangeException(nameof(minImprovement));
            if (string.IsNullOrWhiteSpace(rewardId) || rewardId.Contains("|"))
                throw new ArgumentException("Reward ID required and cannot contain '|'.", nameof(rewardId));
            if (rewardAmount < 1 || rewardAmount > 100000)
                throw new ArgumentOutOfRangeException(nameof(rewardAmount));
            MaxImprovementRewardsPerWindow = maxImprovementRewardsPerWindow;
            MinImprovement = minImprovement;
            RewardId = rewardId;
            RewardAmount = rewardAmount;
        }
    }

    public enum WeeklyRecordStatus
    {
        NotEligible,
        Rejected,
        Duplicate,
        ReplayRejected,
        Participated,
        NotImproved,
        Improved
    }

    public static class WeeklyRecordReasons
    {
        public const string WrongFeatureFlag = "WrongFeatureFlag";
        public const string NonUtcInstant = "NonUtcInstant";
        public const string Unverified = "Unverified";
        public const string GameOrProfileMismatch = "GameOrProfileMismatch";
        public const string NotCompleted = "NotCompleted";
        public const string CompletionInFuture = "CompletionInFuture";
        public const string WeekIdMismatch = "WeekIdMismatch";
        public const string DuplicateResult = "DuplicateResult";
        public const string RunSeedReplayed = "RunSeedReplayed";
        // Grant withheld (record still updated):
        public const string WindowClosed = "WindowClosed";
        public const string WindowCapReached = "WindowCapReached";
        public const string BelowMinimumImprovement = "BelowMinimumImprovement";
    }

    /// <summary>Previous vs new personal record for one (mode, level, week). No ranking data.</summary>
    public sealed class WeeklyRecordView
    {
        public string WeekId { get; }
        public string ModeId { get; }
        public string LevelId { get; }
        public int? PreviousBest { get; }
        public int CurrentBest { get; }
        public int ImprovementDelta => PreviousBest.HasValue ? CurrentBest - PreviousBest.Value : 0;

        internal WeeklyRecordView(string weekId, string modeId, string levelId, PersonalBestRecord record)
        {
            WeekId = weekId;
            ModeId = modeId;
            LevelId = levelId;
            PreviousBest = record.PreviousScore;
            CurrentBest = record.Score;
        }
    }

    /// <summary>Exactly-once grant request for the ENG-M16-011 reward ledger.</summary>
    public sealed class WeeklyRecordGrantRequest
    {
        public string IdempotencyKey { get; }
        public string GameId { get; }
        public string ProfileId { get; }
        public string WeekId { get; }
        public string ModeId { get; }
        public string LevelId { get; }
        public int Tier { get; }
        public string RewardId { get; }
        public int Amount { get; }

        internal WeeklyRecordGrantRequest(string gameId, string profileId, string weekId, string modeId,
            string levelId, int tier, string rewardId, int amount)
        {
            IdempotencyKey = WeeklyPersonalRecordService.GrantKey(gameId, profileId, weekId, modeId, levelId, tier);
            GameId = gameId;
            ProfileId = profileId;
            WeekId = weekId;
            ModeId = modeId;
            LevelId = levelId;
            Tier = tier;
            RewardId = rewardId;
            Amount = amount;
        }
    }

    public sealed class WeeklyRecordOutcome
    {
        public WeeklyRecordStatus Status { get; }
        public string Reason { get; }
        public WeeklyRecordView View { get; }
        public WeeklyRecordGrantRequest Grant { get; }

        internal WeeklyRecordOutcome(WeeklyRecordStatus status, string reason,
            WeeklyRecordView view = null, WeeklyRecordGrantRequest grant = null)
        {
            Status = status;
            Reason = reason ?? string.Empty;
            View = view;
            Grant = grant;
        }
    }

    public sealed class WeeklyPersonalRecordService
    {
        private readonly WeeklyPersonalRecordPolicy _policy;
        private readonly Dictionary<string, PersonalBestRecord> _records =
            new Dictionary<string, PersonalBestRecord>(StringComparer.Ordinal);
        private readonly HashSet<string> _processedResultIds = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> _consumedRunSeeds = new HashSet<string>(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _tiersByRecord = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _grantsByWeek = new Dictionary<string, int>(StringComparer.Ordinal);

        public string GameId { get; }
        public string ProfileId { get; }

        public WeeklyPersonalRecordService(string gameId, string profileId, WeeklyPersonalRecordPolicy policy)
        {
            if (!GameProfileCatalog.IsKnownId(gameId))
                throw new ArgumentException("Unknown game ID.", nameof(gameId));
            if (string.IsNullOrWhiteSpace(profileId) || profileId.Contains("|"))
                throw new ArgumentException("Profile ID required and cannot contain '|'.", nameof(profileId));
            GameId = gameId;
            ProfileId = profileId;
            _policy = policy ?? throw new ArgumentNullException(nameof(policy));
        }

        public static string GrantKey(string gameId, string profileId, string weekId, string modeId,
            string levelId, int tier) =>
            "weeklyPB|" + gameId + "|" + profileId + "|" + weekId + "|" + modeId + "|" + levelId + "|t" +
            tier.ToString(CultureInfo.InvariantCulture);

        /// <param name="runSeed">Per-attempt session seed (ReplayTrace.Seed) of the verified run.</param>
        /// <param name="verifiedCompletionUtc">Server-anchored completion instant (set at sync for offline runs).</param>
        /// <param name="serverNowUtc">Trusted server time; never the device clock.</param>
        public WeeklyRecordOutcome Submit(VerifiedProgressionResult result, uint runSeed,
            DateTime verifiedCompletionUtc, DateTime serverNowUtc,
            FeatureFlagRecord weeklyPersonalBestFlag, FeatureFlagPolicy flagPolicy)
        {
            if (result == null) throw new ArgumentNullException(nameof(result));

            if (weeklyPersonalBestFlag.Feature != LiveOpsFeature.WeeklyPersonalBest)
                return new WeeklyRecordOutcome(WeeklyRecordStatus.NotEligible, WeeklyRecordReasons.WrongFeatureFlag);
            if (!FeatureFlagGate.CanEnableFlag(GameId, weeklyPersonalBestFlag, flagPolicy, out var flagReason))
                return new WeeklyRecordOutcome(WeeklyRecordStatus.NotEligible, flagReason);

            if (verifiedCompletionUtc.Kind != DateTimeKind.Utc || serverNowUtc.Kind != DateTimeKind.Utc)
                return Reject(WeeklyRecordReasons.NonUtcInstant);
            if (!result.Verified)
                return Reject(WeeklyRecordReasons.Unverified);
            if (!StringComparer.Ordinal.Equals(result.GameId, GameId) ||
                !StringComparer.Ordinal.Equals(result.ProfileId, ProfileId))
                return Reject(WeeklyRecordReasons.GameOrProfileMismatch);
            if (!result.Won)
                return Reject(WeeklyRecordReasons.NotCompleted);
            if (verifiedCompletionUtc > serverNowUtc)
                return Reject(WeeklyRecordReasons.CompletionInFuture);

            var weekId = IsoWeekWindow.WeekId(verifiedCompletionUtc);
            if (!StringComparer.Ordinal.Equals(result.CanonicalWeekId, weekId))
                return Reject(WeeklyRecordReasons.WeekIdMismatch);

            // Same key layout as ENG-M08-015 JourneyPlanner personal bests.
            var key = GameId + "|" + ProfileId + "|" + result.ModeId + "|" + result.LevelId + "|" + weekId;
            _records.TryGetValue(key, out var prior);

            if (_processedResultIds.Contains(result.ResultId))
                return new WeeklyRecordOutcome(WeeklyRecordStatus.Duplicate, WeeklyRecordReasons.DuplicateResult,
                    prior == null ? null : new WeeklyRecordView(weekId, result.ModeId, result.LevelId, prior));

            // A run seed counts once per (mode, level) across all weeks: re-submitting an old run is replay farming.
            var seedKey = result.ModeId + "|" + result.LevelId + "|" + runSeed.ToString(CultureInfo.InvariantCulture);
            if (_consumedRunSeeds.Contains(seedKey))
                return new WeeklyRecordOutcome(WeeklyRecordStatus.ReplayRejected, WeeklyRecordReasons.RunSeedReplayed,
                    prior == null ? null : new WeeklyRecordView(weekId, result.ModeId, result.LevelId, prior));

            _processedResultIds.Add(result.ResultId);
            _consumedRunSeeds.Add(seedKey);

            if (prior == null)
            {
                var first = new PersonalBestRecord(key, result.Score, result.ResultId);
                _records[key] = first;
                return new WeeklyRecordOutcome(WeeklyRecordStatus.Participated, string.Empty,
                    new WeeklyRecordView(weekId, result.ModeId, result.LevelId, first));
            }

            if (result.Score <= prior.Score)
                return new WeeklyRecordOutcome(WeeklyRecordStatus.NotImproved, string.Empty,
                    new WeeklyRecordView(weekId, result.ModeId, result.LevelId, prior));

            var record = new PersonalBestRecord(key, result.Score, result.ResultId, prior.Score, prior.ResultId);
            _records[key] = record;
            var view = new WeeklyRecordView(weekId, result.ModeId, result.LevelId, record);

            // Late arrivals land in their own (closed) week and never grant retroactively.
            if (!StringComparer.Ordinal.Equals(weekId, IsoWeekWindow.WeekId(serverNowUtc)))
                return new WeeklyRecordOutcome(WeeklyRecordStatus.Improved, WeeklyRecordReasons.WindowClosed, view);
            if (result.Score - prior.Score < _policy.MinImprovement)
                return new WeeklyRecordOutcome(WeeklyRecordStatus.Improved,
                    WeeklyRecordReasons.BelowMinimumImprovement, view);
            _grantsByWeek.TryGetValue(weekId, out var grantedThisWeek);
            if (grantedThisWeek >= _policy.MaxImprovementRewardsPerWindow)
                return new WeeklyRecordOutcome(WeeklyRecordStatus.Improved, WeeklyRecordReasons.WindowCapReached, view);

            _tiersByRecord.TryGetValue(key, out var tier);
            tier++;
            var grant = new WeeklyRecordGrantRequest(GameId, ProfileId, weekId, result.ModeId, result.LevelId,
                tier, _policy.RewardId, _policy.RewardAmount);
            _tiersByRecord[key] = tier; // tier only advances on issue, so each key is issued at most once

            _grantsByWeek[weekId] = grantedThisWeek + 1;
            return new WeeklyRecordOutcome(WeeklyRecordStatus.Improved, string.Empty, view, grant);
        }

        public WeeklyRecordView GetView(string modeId, string levelId, string weekId)
        {
            var key = GameId + "|" + ProfileId + "|" + modeId + "|" + levelId + "|" + weekId;
            return _records.TryGetValue(key, out var record)
                ? new WeeklyRecordView(weekId, modeId, levelId, record)
                : null;
        }

        private static WeeklyRecordOutcome Reject(string reason) =>
            new WeeklyRecordOutcome(WeeklyRecordStatus.Rejected, reason);
    }
}
