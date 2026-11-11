using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using Match3.Engine.Runtime.Economy;

namespace Match3.Engine.Runtime.LiveOps
{
    // ENG-M20-012: weekly per-player personal-record challenge.
    //
    // - Window: ISO-8601 week ("2026-W46"), Monday 00:00 UTC to next Monday 00:00 UTC, computed only
    //   from trusted UTC instants (DateTimeKind.Utc). Local/Unspecified instants are rejected; DST cannot apply.
    // - Records reuse ENG-M08-015: input is VerifiedProgressionResult, storage is PersonalBestRecord with the
    //   same key (game|profile|mode|level|week) and durable previous score/result ID.
    // - First verified completion in a window = participation. A strictly higher verified score replaces the
    //   record. Improvements of at least MinImprovement earn one grant through the ENG-M16-011 RewardLedger
    //   while the window is still open. No ranking.
    // - Cap: the ledger itself holds the cap. Grants use week-wide slots (source weekly_personal_best, instance
    //   "week|t<k>", k = 1..MaxImprovementRewardsPerWindow, Reference = result ID, ExpiresUtc = end of the ISO
    //   week UTC). Slots are probed upward with TryGrant: Applied = granted; Duplicate = already granted;
    //   IdempotencyConflict = slot taken, and if that record's Reference is this result (e.g. reward amount
    //   changed mid-week) it is AlreadyGranted, otherwise the next slot is tried; no free slot = WindowCapReached.
    //   KeySettled (slot tombstoned by ledger Prune) is AlreadyGranted only when the tombstone's payload digest
    //   equals this grant's (same result, amount and week end); otherwise it fails closed with KeySettled.
    // - Time: Submit takes a server-anchored TrustedUtcAnchor, which is also the ledger grant anchor; the legacy
    //   DateTime overload is [Obsolete] and always fails closed (TrustedTimeAnchorRequired).
    //   So local state loss, an older snapshot or several devices on one merged ledger cannot exceed the cap.
    // - Replay: one entry per result ID and per verified ReplayTrace digest (mode|level|sha256(ReplayCodec.Encode)).
    //   A fixed-seed level replayed with different moves is a different run and may still improve the record.
    // - Durability: ExportSnapshot()/FromSnapshot() persist all local state (records, result IDs, replay keys)
    //   plus a monotonic Sequence as canonical JSON (LiveOps.CanonicalJson, same codec as RewardLedger) with a
    //   SHA-256 field: tamper-EVIDENCE, not authentication. Snapshot version 2; version 1 (which carried local
    //   grant counts) is rejected with UnsupportedSnapshotVersion, not migrated: the ledger is the cap authority,
    //   so starting from an empty local state is safe. Callers must keep a high-water Sequence and reject lower
    //   ones; FromSnapshot enforces it when given minimumSequence (SnapshotRollback). A rolled-back snapshot can
    //   only lose local replay/duplicate memory, never re-open a grant (ledger slots).
    // - Retention: weeks older than RetainedPastWeeks before the server week are rejected (WeekBeyondRetention)
    //   and pruned, so pruning never reopens a result ID or a grant (grants are only for the current week).
    // - No late-sync grant grace by design: an offline result synced after rollover updates its own week's
    //   record but never grants (no retroactive cap accounting).
    // - Small-step farming is bounded: every grant needs a +MinImprovement step over the current record and the
    //   per-window cap bounds the total grants per player per week.
    // ponytail: single-writer, not thread-safe; snapshot import limited to CanonicalJson.MaxUtf8Bytes (256 KiB),
    // fine for a two-week retention; split per week if a title needs far more levels.
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
        /// <summary>Non-premium ledger asset ID (e.g. "Coins").</summary>
        public string RewardId { get; }
        public int RewardAmount { get; }
        /// <summary>Closed weeks still accepted (record only, no grant); older weeks are rejected and pruned.</summary>
        public int RetainedPastWeeks { get; }

        public WeeklyPersonalRecordPolicy(int maxImprovementRewardsPerWindow, int minImprovement,
            string rewardId, int rewardAmount, int retainedPastWeeks = 1)
        {
            if (maxImprovementRewardsPerWindow < 0 || maxImprovementRewardsPerWindow > 50)
                throw new ArgumentOutOfRangeException(nameof(maxImprovementRewardsPerWindow));
            if (minImprovement < 1)
                throw new ArgumentOutOfRangeException(nameof(minImprovement));
            // Registered ledger asset (Ordinal) only; premium currency is never a weekly reward.
            if (!RewardLedger.IsKnownAsset(rewardId) || rewardId == RewardLedger.PaidAssetId)
                throw new ArgumentException("Reward must be a registered non-premium ledger asset ID.", nameof(rewardId));
            if (rewardAmount < 1 || rewardAmount > 100000)
                throw new ArgumentOutOfRangeException(nameof(rewardAmount));
            if (retainedPastWeeks < 0 || retainedPastWeeks > 52)
                throw new ArgumentOutOfRangeException(nameof(retainedPastWeeks));
            MaxImprovementRewardsPerWindow = maxImprovementRewardsPerWindow;
            MinImprovement = minImprovement;
            RewardId = rewardId;
            RewardAmount = rewardAmount;
            RetainedPastWeeks = retainedPastWeeks;
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
        public const string TrustedTimeAnchorRequired = "TrustedTimeAnchorRequired";
        public const string Unverified = "Unverified";
        public const string GameOrProfileMismatch = "GameOrProfileMismatch";
        public const string TraceGameMismatch = "TraceGameMismatch";
        public const string NotCompleted = "NotCompleted";
        public const string CompletionInFuture = "CompletionInFuture";
        public const string WeekIdMismatch = "WeekIdMismatch";
        public const string WeekBeyondRetention = "WeekBeyondRetention";
        public const string DuplicateResult = "DuplicateResult";
        public const string TraceReplayed = "TraceReplayed";
        // Grant withheld or not newly applied (record still updated); ledger rejections pass their own reason:
        public const string WindowClosed = "WindowClosed";
        public const string WindowCapReached = "WindowCapReached";
        public const string BelowMinimumImprovement = "BelowMinimumImprovement";
        public const string AlreadyGranted = "AlreadyGranted";
        // Snapshot import:
        public const string MalformedSnapshot = "MalformedSnapshot";
        public const string UnsupportedSnapshotVersion = "UnsupportedSnapshotVersion";
        public const string IntegrityMismatch = "IntegrityMismatch";
        public const string SnapshotOwnerMismatch = "SnapshotOwnerMismatch";
        public const string NonCanonicalSnapshot = "NonCanonicalSnapshot";
        public const string SnapshotRollback = "SnapshotRollback";
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

    public sealed class WeeklyRecordOutcome
    {
        public WeeklyRecordStatus Status { get; }
        public string Reason { get; }
        public WeeklyRecordView View { get; }
        /// <summary>Ledger record applied now, or the original one when Reason is AlreadyGranted; else null.</summary>
        public RewardLedgerRecord Grant { get; }

        internal WeeklyRecordOutcome(WeeklyRecordStatus status, string reason,
            WeeklyRecordView view = null, RewardLedgerRecord grant = null)
        {
            Status = status;
            Reason = reason ?? string.Empty;
            View = view;
            Grant = grant;
        }
    }

    public sealed class WeeklyPersonalRecordService
    {
        public const string SnapshotFormat = "m3-weekly-pb";
        public const long SnapshotVersion = 2;

        private sealed class Entry
        {
            public string Mode, Level, Week;
            public PersonalBestRecord Record;
        }

        private readonly WeeklyPersonalRecordPolicy _policy;
        private readonly RewardLedger _ledger;
        private readonly Dictionary<string, Entry> _records = new Dictionary<string, Entry>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _resultWeeks = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _replayWeeks = new Dictionary<string, string>(StringComparer.Ordinal);

        public string GameId { get; }
        public string ProfileId { get; }
        /// <summary>Increases on every local state change; persisted in the snapshot (rollback detection).</summary>
        public long Sequence { get; private set; }

        /// <param name="ledger">ENG-M16-011 ledger of the same game and player (ledger PlayerId = profileId).</param>
        public WeeklyPersonalRecordService(string gameId, string profileId, WeeklyPersonalRecordPolicy policy,
            RewardLedger ledger)
        {
            if (!GameProfileCatalog.IsKnownId(gameId))
                throw new ArgumentException("Unknown game ID.", nameof(gameId));
            if (string.IsNullOrWhiteSpace(profileId) || profileId.Contains("|"))
                throw new ArgumentException("Profile ID required and cannot contain '|'.", nameof(profileId));
            _policy = policy ?? throw new ArgumentNullException(nameof(policy));
            _ledger = ledger ?? throw new ArgumentNullException(nameof(ledger));
            if (!StringComparer.Ordinal.Equals(ledger.GameId, gameId) ||
                !StringComparer.Ordinal.Equals(ledger.PlayerId, profileId))
                throw new ArgumentException("Ledger belongs to another game/player.", nameof(ledger));
            GameId = gameId;
            ProfileId = profileId;
        }

        public static string GrantInstanceId(string weekId, int slot) =>
            weekId + "|t" + slot.ToString(CultureInfo.InvariantCulture);

        /// <summary>
        /// Trust contract (inherited from ENG-M08-015, not redesigned here): <paramref name="result"/> and
        /// <paramref name="verifiedTrace"/> are produced only by the server-side verifier that re-simulated the
        /// trace; clients never construct VerifiedProgressionResult. This service enforces consistency (owner,
        /// week window, duplicate/replay, cap, exactly-once grant), not the authenticity of the score.
        /// </summary>
        /// <param name="verifiedTrace">The verified run's replay trace; its canonical digest is the replay key.</param>
        /// <param name="verifiedCompletionUtc">Server-anchored completion instant (set at sync for offline runs).</param>
        /// <param name="serverNow">Server-anchored time (never the device clock); it is also the ledger grant anchor.</param>
        public WeeklyRecordOutcome Submit(VerifiedProgressionResult result, ReplayTrace verifiedTrace,
            DateTime verifiedCompletionUtc, TrustedUtcAnchor serverNow,
            FeatureFlagRecord weeklyPersonalBestFlag, FeatureFlagPolicy flagPolicy)
        {
            if (result == null) throw new ArgumentNullException(nameof(result));
            if (verifiedTrace == null) throw new ArgumentNullException(nameof(verifiedTrace));

            if (weeklyPersonalBestFlag.Feature != LiveOpsFeature.WeeklyPersonalBest)
                return new WeeklyRecordOutcome(WeeklyRecordStatus.NotEligible, WeeklyRecordReasons.WrongFeatureFlag);
            if (!FeatureFlagGate.CanEnableFlag(GameId, weeklyPersonalBestFlag, flagPolicy, out var flagReason))
                return new WeeklyRecordOutcome(WeeklyRecordStatus.NotEligible, flagReason);

            if (!serverNow.IsServerAnchored) return Reject(WeeklyRecordReasons.TrustedTimeAnchorRequired);
            var serverNowUtc = serverNow.UtcNow;
            if (verifiedCompletionUtc.Kind != DateTimeKind.Utc || serverNowUtc.Kind != DateTimeKind.Utc)
                return Reject(WeeklyRecordReasons.NonUtcInstant);
            if (!result.Verified)
                return Reject(WeeklyRecordReasons.Unverified);
            if (!StringComparer.Ordinal.Equals(result.GameId, GameId) ||
                !StringComparer.Ordinal.Equals(result.ProfileId, ProfileId))
                return Reject(WeeklyRecordReasons.GameOrProfileMismatch);
            if (!StringComparer.Ordinal.Equals(verifiedTrace.ProfileId, GameId))
                return Reject(WeeklyRecordReasons.TraceGameMismatch);
            if (!result.Won)
                return Reject(WeeklyRecordReasons.NotCompleted);
            if (verifiedCompletionUtc > serverNowUtc)
                return Reject(WeeklyRecordReasons.CompletionInFuture);

            var weekId = IsoWeekWindow.WeekId(verifiedCompletionUtc);
            if (!StringComparer.Ordinal.Equals(result.CanonicalWeekId, weekId))
                return Reject(WeeklyRecordReasons.WeekIdMismatch);
            if (string.CompareOrdinal(weekId, RetentionHorizon(serverNowUtc)) < 0)
                return Reject(WeeklyRecordReasons.WeekBeyondRetention);

            // Same key layout as ENG-M08-015 JourneyPlanner personal bests.
            var key = GameId + "|" + ProfileId + "|" + result.ModeId + "|" + result.LevelId + "|" + weekId;
            _records.TryGetValue(key, out var entry);
            var prior = entry?.Record;

            if (_resultWeeks.ContainsKey(result.ResultId))
                return new WeeklyRecordOutcome(WeeklyRecordStatus.Duplicate, WeeklyRecordReasons.DuplicateResult,
                    View(weekId, result, prior));

            var replayKey = result.ModeId + "|" + result.LevelId + "|" + TraceDigest(verifiedTrace);
            if (_replayWeeks.ContainsKey(replayKey))
                return new WeeklyRecordOutcome(WeeklyRecordStatus.ReplayRejected, WeeklyRecordReasons.TraceReplayed,
                    View(weekId, result, prior));

            _resultWeeks[result.ResultId] = weekId;
            _replayWeeks[replayKey] = weekId;
            Sequence++;

            if (prior == null)
            {
                var first = new PersonalBestRecord(key, result.Score, result.ResultId);
                _records[key] = new Entry { Mode = result.ModeId, Level = result.LevelId, Week = weekId, Record = first };
                return new WeeklyRecordOutcome(WeeklyRecordStatus.Participated, string.Empty, View(weekId, result, first));
            }

            if (result.Score <= prior.Score)
                return new WeeklyRecordOutcome(WeeklyRecordStatus.NotImproved, string.Empty, View(weekId, result, prior));

            var record = new PersonalBestRecord(key, result.Score, result.ResultId, prior.Score, prior.ResultId);
            entry.Record = record;
            var view = View(weekId, result, record);

            // Late arrivals land in their own (closed) week and never grant retroactively.
            if (!StringComparer.Ordinal.Equals(weekId, IsoWeekWindow.WeekId(serverNowUtc)))
                return new WeeklyRecordOutcome(WeeklyRecordStatus.Improved, WeeklyRecordReasons.WindowClosed, view);
            if (result.Score - prior.Score < _policy.MinImprovement)
                return new WeeklyRecordOutcome(WeeklyRecordStatus.Improved,
                    WeeklyRecordReasons.BelowMinimumImprovement, view);
            // Week-wide ledger slots k = 1..cap (see header): the ledger, not local state, enforces the cap.
            for (var slot = 1; slot <= _policy.MaxImprovementRewardsPerWindow; slot++)
            {
                var instance = GrantInstanceId(weekId, slot);
                var slotKey = RewardLedgerKey.Derive(GameId, ProfileId, RewardSources.WeeklyPersonalBest, instance);
                var request = new RewardLedgerRequest(GameId, ProfileId,
                    RewardSources.WeeklyPersonalBest, instance,
                    new Dictionary<string, long> { [_policy.RewardId] = _policy.RewardAmount },
                    serverNowUtc, IsoWeekWindow.EndUtc(weekId), result.ResultId);
                var granted = _ledger.TryGrant(request, serverNow);
                if (granted.Outcome == RewardLedgerOutcome.Applied)
                    return new WeeklyRecordOutcome(WeeklyRecordStatus.Improved, string.Empty, view, granted.Record);
                if (granted.Outcome == RewardLedgerOutcome.Duplicate)
                    return new WeeklyRecordOutcome(WeeklyRecordStatus.Improved, WeeklyRecordReasons.AlreadyGranted,
                        view, granted.Record);
                // Settled slot: a tombstone keeps only the payload digest (amounts + expiry + reference). An equal
                // digest means this very result was granted there; anything else fails closed with KeySettled.
                if (granted.Reason == RewardLedgerReasons.KeySettled &&
                    _ledger.TryGetTombstone(slotKey, out var settled) &&
                    StringComparer.Ordinal.Equals(settled.PayloadDigest, new RewardLedgerRecord(slotKey, request).PayloadDigest))
                    return new WeeklyRecordOutcome(WeeklyRecordStatus.Improved, WeeklyRecordReasons.AlreadyGranted, view);
                if (granted.Reason != RewardLedgerReasons.IdempotencyConflict)
                    return new WeeklyRecordOutcome(WeeklyRecordStatus.Improved, granted.Reason, view);
                if (_ledger.TryGetRecord(slotKey, out var existing) &&
                    StringComparer.Ordinal.Equals(existing.Reference, result.ResultId))
                    return new WeeklyRecordOutcome(WeeklyRecordStatus.Improved, WeeklyRecordReasons.AlreadyGranted,
                        view, existing);
            }
            return new WeeklyRecordOutcome(WeeklyRecordStatus.Improved, WeeklyRecordReasons.WindowCapReached, view);
        }

        /// <summary>Superseded: a bare DateTime cannot prove server time. Always fails closed.</summary>
        [Obsolete("Pass a server-anchored TrustedUtcAnchor; this overload always returns TrustedTimeAnchorRequired.")]
        public WeeklyRecordOutcome Submit(VerifiedProgressionResult result, ReplayTrace verifiedTrace,
            DateTime verifiedCompletionUtc, DateTime serverNowUtc,
            FeatureFlagRecord weeklyPersonalBestFlag, FeatureFlagPolicy flagPolicy) =>
            Reject(WeeklyRecordReasons.TrustedTimeAnchorRequired);

        public WeeklyRecordView GetView(string modeId, string levelId, string weekId)
        {
            var key = GameId + "|" + ProfileId + "|" + modeId + "|" + levelId + "|" + weekId;
            return _records.TryGetValue(key, out var e) ? new WeeklyRecordView(weekId, modeId, levelId, e.Record) : null;
        }

        /// <summary>Drops all state for weeks before the retention horizon. Returns the removed entry count.</summary>
        public int Prune(DateTime serverNowUtc)
        {
            var horizon = RetentionHorizon(serverNowUtc);
            bool Old(string week) => string.CompareOrdinal(week, horizon) < 0;
            var removed = 0;
            foreach (var k in _records.Where(kv => Old(kv.Value.Week)).Select(kv => kv.Key).ToList())
            {
                _records.Remove(k);
                removed++;
            }
            foreach (var map in new[] { _resultWeeks, _replayWeeks })
                foreach (var k in map.Where(kv => Old(kv.Value)).Select(kv => kv.Key).ToList())
                {
                    map.Remove(k);
                    removed++;
                }
            if (removed > 0) Sequence++;
            return removed;
        }

        public string ExportSnapshot()
        {
            var doc = SnapshotBody();
            doc["sha256"] = CanonicalJson.Sha256Hex(SnapshotBody());
            return CanonicalJson.Serialize(doc);
        }

        /// <summary>
        /// Restores state; null plus a reason when the snapshot is malformed, tampered, non-canonical, foreign,
        /// of another version, or older than <paramref name="minimumSequence"/> (the caller's stored high-water mark).
        /// </summary>
        public static WeeklyPersonalRecordService FromSnapshot(string text, string gameId, string profileId,
            WeeklyPersonalRecordPolicy policy, RewardLedger ledger, out string reason, long minimumSequence = 0)
        {
            var service = new WeeklyPersonalRecordService(gameId, profileId, policy, ledger);
            try
            {
                if (!(CanonicalJson.Parse(text) is Dictionary<string, object> doc) || doc.Count != 9 ||
                    !Equals(doc["format"], SnapshotFormat))
                    return Fail(WeeklyRecordReasons.MalformedSnapshot, out reason);
                if (!Equals(doc["version"], SnapshotVersion))
                    return Fail(WeeklyRecordReasons.UnsupportedSnapshotVersion, out reason);
                var claimed = doc["sha256"] as string;
                doc.Remove("sha256");
                if (!StringComparer.Ordinal.Equals(claimed, CanonicalJson.Sha256Hex(doc)))
                    return Fail(WeeklyRecordReasons.IntegrityMismatch, out reason);
                if (!Equals(doc["gameId"], gameId) || !Equals(doc["profileId"], profileId))
                    return Fail(WeeklyRecordReasons.SnapshotOwnerMismatch, out reason);
                var sequence = (long)doc["sequence"];
                if (sequence < 0) throw new FormatException("sequence");
                if (sequence < minimumSequence)
                    return Fail(WeeklyRecordReasons.SnapshotRollback, out reason);
                service.Sequence = sequence;

                foreach (Dictionary<string, object> r in (List<object>)doc["records"])
                {
                    var week = Week(r["week"]);
                    var mode = Id(r["mode"], false);
                    var level = Id(r["level"], false);
                    var key = gameId + "|" + profileId + "|" + mode + "|" + level + "|" + week;
                    var prev = r["prevScore"] == null ? (int?)null : checked((int)(long)r["prevScore"]);
                    service._records.Add(key, new Entry
                    {
                        Mode = mode,
                        Level = level,
                        Week = week,
                        Record = new PersonalBestRecord(key, checked((int)(long)r["score"]), (string)r["resultId"],
                            prev, (string)r["prevResultId"])
                    });
                }
                foreach (Dictionary<string, object> r in (List<object>)doc["results"])
                    service._resultWeeks.Add(Id(r["id"], true), Week(r["week"]));
                foreach (Dictionary<string, object> r in (List<object>)doc["replays"])
                    service._replayWeeks.Add(Id(r["key"], true), Week(r["week"]));
                if (!StringComparer.Ordinal.Equals(service.ExportSnapshot(), text))
                    return Fail(WeeklyRecordReasons.NonCanonicalSnapshot, out reason);
            }
            catch (Exception e) when (e is FormatException || e is ArgumentException || e is InvalidCastException ||
                                      e is KeyNotFoundException || e is NullReferenceException || e is OverflowException)
            {
                return Fail(WeeklyRecordReasons.MalformedSnapshot, out reason);
            }
            reason = string.Empty;
            return service;
        }

        private Dictionary<string, object> SnapshotBody() => new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["format"] = SnapshotFormat,
            ["version"] = SnapshotVersion,
            ["gameId"] = GameId,
            ["profileId"] = ProfileId,
            ["sequence"] = Sequence,
            ["records"] = _records.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => (object)
                new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["mode"] = kv.Value.Mode,
                    ["level"] = kv.Value.Level,
                    ["week"] = kv.Value.Week,
                    ["score"] = (long)kv.Value.Record.Score,
                    ["resultId"] = kv.Value.Record.ResultId,
                    ["prevScore"] = kv.Value.Record.PreviousScore.HasValue
                        ? (object)(long)kv.Value.Record.PreviousScore.Value : null,
                    ["prevResultId"] = kv.Value.Record.PreviousResultId
                }).ToList(),
            ["results"] = Pairs(_resultWeeks, "id"),
            ["replays"] = Pairs(_replayWeeks, "key")
        };

        private static List<object> Pairs(Dictionary<string, string> map, string name) =>
            map.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => (object)
                new Dictionary<string, object>(StringComparer.Ordinal) { [name] = kv.Key, ["week"] = kv.Value }).ToList();

        private string RetentionHorizon(DateTime serverNowUtc) =>
            IsoWeekWindow.WeekId(serverNowUtc.AddDays(-7 * _policy.RetainedPastWeeks));

        private static string TraceDigest(ReplayTrace trace)
        {
            using (var sha = SHA256.Create())
                return string.Concat(sha.ComputeHash(ReplayCodec.Encode(trace))
                    .Select(b => b.ToString("x2", CultureInfo.InvariantCulture)));
        }

        private static string Week(object o) =>
            o is string s && IsoWeekWindow.TryGetStartUtc(s, out _) ? s : throw new FormatException("week");

        private static string Id(object o, bool pipeAllowed) =>
            o is string s && !string.IsNullOrWhiteSpace(s) && (pipeAllowed || !s.Contains("|"))
                ? s : throw new FormatException("id");

        private static WeeklyPersonalRecordService Fail(string code, out string reason)
        {
            reason = code;
            return null;
        }

        private static WeeklyRecordView View(string weekId, VerifiedProgressionResult r, PersonalBestRecord record) =>
            record == null ? null : new WeeklyRecordView(weekId, r.ModeId, r.LevelId, record);

        private static WeeklyRecordOutcome Reject(string reason) =>
            new WeeklyRecordOutcome(WeeklyRecordStatus.Rejected, reason);
    }
}
