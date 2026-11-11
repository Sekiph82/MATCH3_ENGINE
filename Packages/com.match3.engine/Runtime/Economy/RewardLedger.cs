using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Match3.Engine.Runtime.LiveOps;

namespace Match3.Engine.Runtime.Economy
{
    // ENG-M16-011 Cross-reward exactly-once ledger.
    //
    // Key scheme: "rgk1|" + L(gameId) + L(playerId) + L(source) + L(sourceInstanceId), L(s) = len ":" s "|",
    // all Ordinal. Length prefixes make the key injective even when ids contain '|' or ':'.
    // Persistence: canonical JSON lines (reuses LiveOps.CanonicalJson); line 1 = header, then one record per
    // line sorted by key. Header "sha256" = SHA-256 over canonical [headerWithoutSha, records]. Restore rejects
    // hash mismatch and any text that is not byte-identical to the canonical re-serialization. SHA-256 is
    // tamper-EVIDENCE only (anyone can recompute it); authenticity needs a server signature (not implemented).
    // Bounded growth: only records with an ExpiresUtc (event/weekly/tournament/catch-up windows) are pruned,
    // once ExpiresUtc + retention(source) <= prune horizon; their amounts fold into the settled segment so
    // balances are kept. Records without ExpiresUtc (first-clear, IAP, packs...) are replayable and never pruned.
    // A grant whose source is already settled by the horizon is rejected (SourceSettled), so pruning never
    // reopens a key.
    // Merge: same owner, opening and retention required. Settled segment = max by (horizon, canonical).
    // Records = key union; variants settled by that horizon are dropped; per key the variant with the latest
    // expiry (none = latest), then ordinal-smallest payload, then earliest GrantedUtc wins. This total order
    // makes merge commutative and associative. Same key with different payload is reported in ConflictedKeys
    // and still counted once. Grants known only to a device whose settled segment lost are not re-credited
    // (no double grant; possible under-credit -> server reconciliation, not implemented).
    // ponytail: single-writer (no locks); callers serialize access, add a lock if grants become concurrent.

    public static class RewardSources
    {
        public const string FirstClear = "first_clear";
        public const string LevelWin = "level_win";
        public const string Streak = "streak";
        public const string Chest = "chest";
        public const string EarnedPack = "earned_pack";
        public const string Daily = "daily";
        public const string Weekly = "weekly";
        public const string WeeklyPersonalBest = "weekly_personal_best";
        public const string SeasonalCollection = "seasonal_collection";
        public const string Tournament = "tournament";
        public const string CatchUp = "catch_up";
        public const string Purchase = "iap";
        public const string AdServerSideVerified = "ad_ssv";

        public static IReadOnlyList<string> All { get; } = Array.AsReadOnly(new[]
        {
            FirstClear, LevelWin, Streak, Chest, EarnedPack, Daily, Weekly, WeeklyPersonalBest,
            SeasonalCollection, Tournament, CatchUp, Purchase, AdServerSideVerified
        });

        public static bool IsKnown(string source) => source != null && All.Contains(source, StringComparer.Ordinal);
    }

    public static class RewardLedgerReasons
    {
        public const string IdempotencyConflict = "IdempotencyConflict";
        public const string PaidGrantNotAllowed = "PaidGrantNotAllowed";
        public const string AdRewardNotAllowed = "AdRewardNotAllowed";
        public const string LedgerOwnerMismatch = "LedgerOwnerMismatch";
        public const string UnknownRewardSource = "UnknownRewardSource";
        public const string InvalidSourceInstance = "InvalidSourceInstance";
        public const string InvalidReference = "InvalidReference";
        public const string InvalidAmounts = "InvalidAmounts";
        public const string NonUtcTimestamp = "NonUtcTimestamp";
        public const string SourceExpired = "SourceExpired";
        public const string SourceSettled = "SourceSettled";
        public const string BalanceOverflow = "BalanceOverflow";
        public const string MalformedSnapshot = "MalformedSnapshot";
        public const string UnsupportedSnapshotVersion = "UnsupportedSnapshotVersion";
        public const string IntegrityMismatch = "IntegrityMismatch";
        public const string NonCanonicalSnapshot = "NonCanonicalSnapshot";
        public const string MergeOwnerMismatch = "MergeOwnerMismatch";
        public const string OpeningBalanceMismatch = "OpeningBalanceMismatch";
        public const string RetentionPolicyMismatch = "RetentionPolicyMismatch";
    }

    public static class RewardLedgerKey
    {
        public static string Derive(string gameId, string playerId, string source, string sourceInstanceId)
        {
            if (gameId == null || playerId == null || source == null || sourceInstanceId == null)
                throw new ArgumentNullException(nameof(gameId), "All key components are required.");
            var sb = new StringBuilder("rgk1|");
            foreach (var part in new[] { gameId, playerId, source, sourceInstanceId })
                sb.Append(part.Length.ToString(CultureInfo.InvariantCulture)).Append(':').Append(part).Append('|');
            return sb.ToString();
        }
    }

    public sealed class RewardLedgerRequest
    {
        public string GameId { get; }
        public string PlayerId { get; }
        public string Source { get; }
        public string SourceInstanceId { get; }
        /// <summary>Optional triggering event id (order id, ad transaction, win event); part of the payload.</summary>
        public string Reference { get; }
        public IReadOnlyDictionary<string, long> Amounts { get; }
        public DateTime GrantedUtc { get; }
        /// <summary>End of the source's claim window; null = replayable/permanent source (never pruned).</summary>
        public DateTime? ExpiresUtc { get; }

        public RewardLedgerRequest(string gameId, string playerId, string source, string sourceInstanceId,
            IReadOnlyDictionary<string, long> amounts, DateTime grantedUtc, DateTime? expiresUtc = null,
            string reference = null)
        {
            GameId = gameId;
            PlayerId = playerId;
            Source = source;
            SourceInstanceId = sourceInstanceId;
            Reference = reference;
            Amounts = amounts == null ? null : RewardLedger.SortedCopy(amounts);
            GrantedUtc = grantedUtc;
            ExpiresUtc = expiresUtc;
        }
    }

    public sealed class RewardLedgerRecord
    {
        public string Key { get; }
        public string GameId { get; }
        public string PlayerId { get; }
        public string Source { get; }
        public string SourceInstanceId { get; }
        public string Reference { get; }
        public IReadOnlyDictionary<string, long> Amounts { get; }
        public DateTime GrantedUtc { get; }
        public DateTime? ExpiresUtc { get; }
        /// <summary>Canonical amounts+expiry+reference; equal payload under one key = same grant.</summary>
        internal string Payload { get; }

        internal RewardLedgerRecord(string key, RewardLedgerRequest r)
        {
            Key = key;
            GameId = r.GameId;
            PlayerId = r.PlayerId;
            Source = r.Source;
            SourceInstanceId = r.SourceInstanceId;
            Reference = r.Reference;
            Amounts = r.Amounts;
            GrantedUtc = r.GrantedUtc;
            ExpiresUtc = r.ExpiresUtc;
            Payload = CanonicalJson.Serialize(new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["amounts"] = RewardLedger.ToJsonMap(Amounts),
                ["expiresTicks"] = ExpiresUtc.HasValue ? (object)ExpiresUtc.Value.Ticks : null,
                ["reference"] = Reference
            });
        }

        internal Dictionary<string, object> ToJson() => new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["source"] = Source,
            ["instance"] = SourceInstanceId,
            ["reference"] = Reference,
            ["amounts"] = RewardLedger.ToJsonMap(Amounts),
            ["grantedTicks"] = GrantedUtc.Ticks,
            ["expiresTicks"] = ExpiresUtc.HasValue ? (object)ExpiresUtc.Value.Ticks : null
        };
    }

    public enum RewardLedgerOutcome { Applied, Duplicate, Rejected }

    public readonly struct RewardLedgerGrantResult
    {
        public RewardLedgerOutcome Outcome { get; }
        /// <summary>Applied: the new record. Duplicate: the ORIGINAL record. Rejected: null.</summary>
        public RewardLedgerRecord Record { get; }
        public string Reason { get; }

        private RewardLedgerGrantResult(RewardLedgerOutcome outcome, RewardLedgerRecord record, string reason)
        {
            Outcome = outcome;
            Record = record;
            Reason = reason ?? string.Empty;
        }

        internal static RewardLedgerGrantResult Applied(RewardLedgerRecord r) => new RewardLedgerGrantResult(RewardLedgerOutcome.Applied, r, null);
        internal static RewardLedgerGrantResult Duplicate(RewardLedgerRecord r) => new RewardLedgerGrantResult(RewardLedgerOutcome.Duplicate, r, null);
        internal static RewardLedgerGrantResult Rejected(string reason) => new RewardLedgerGrantResult(RewardLedgerOutcome.Rejected, null, reason);
    }

    public sealed class RewardLedgerResult
    {
        public RewardLedger Ledger { get; }
        public string Reason { get; }
        public bool Succeeded => Ledger != null;
        /// <summary>Merge only: keys seen with different payloads (counted once; route to reconciliation).</summary>
        public IReadOnlyList<string> ConflictedKeys { get; }

        internal RewardLedgerResult(RewardLedger ledger, string reason, IList<string> conflicts = null)
        {
            Ledger = ledger;
            Reason = reason ?? string.Empty;
            ConflictedKeys = Array.AsReadOnly((conflicts ?? new List<string>()).ToArray());
        }
    }

    public sealed class RewardRetentionPolicy
    {
        public const int StandardDays = 180;
        public const int MaxDays = 3650;
        public int DefaultDays { get; }
        public IReadOnlyDictionary<string, int> DaysBySource { get; }

        public RewardRetentionPolicy(int defaultDays = StandardDays, IReadOnlyDictionary<string, int> daysBySource = null)
        {
            if (defaultDays < 1 || defaultDays > MaxDays) throw new ArgumentOutOfRangeException(nameof(defaultDays));
            var map = new SortedDictionary<string, int>(StringComparer.Ordinal);
            foreach (var kv in daysBySource ?? new Dictionary<string, int>())
            {
                if (!RewardSources.IsKnown(kv.Key)) throw new ArgumentException("Unknown reward source " + kv.Key, nameof(daysBySource));
                if (kv.Value < 1 || kv.Value > MaxDays) throw new ArgumentOutOfRangeException(nameof(daysBySource));
                map[kv.Key] = kv.Value;
            }
            DefaultDays = defaultDays;
            DaysBySource = map;
        }

        public TimeSpan For(string source) =>
            TimeSpan.FromDays(DaysBySource.TryGetValue(source, out var d) ? d : DefaultDays);

        internal Dictionary<string, object> ToJson() => new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["defaultDays"] = (long)DefaultDays,
            ["bySource"] = DaysBySource.ToDictionary(kv => kv.Key, kv => (object)(long)kv.Value, StringComparer.Ordinal)
        };

        internal static RewardRetentionPolicy FromJson(object o)
        {
            var d = (Dictionary<string, object>)o;
            if (d.Count != 2) throw new FormatException("retention");
            var by = ((Dictionary<string, object>)d["bySource"]).ToDictionary(kv => kv.Key, kv => checked((int)(long)kv.Value), StringComparer.Ordinal);
            return new RewardRetentionPolicy(checked((int)(long)d["defaultDays"]), by);
        }
    }

    public sealed class RewardLedger
    {
        public const string SnapshotFormat = "m3-reward-ledger";
        public const long SnapshotVersion = 1;
        public const int MaxIdLength = 128;
        public const int MaxAssetsPerGrant = 16;
        public const long MaxAmountPerAsset = 1000000000L;
        public static readonly string PaidAssetId = nameof(CurrencyType.PremiumGems);

        private readonly SortedDictionary<string, long> _opening;
        private readonly SortedDictionary<string, long> _settled = new SortedDictionary<string, long>(StringComparer.Ordinal);
        private readonly SortedDictionary<string, long> _balances;
        private readonly Dictionary<string, RewardLedgerRecord> _records = new Dictionary<string, RewardLedgerRecord>(StringComparer.Ordinal);
        private readonly bool _paidAllowed;
        private readonly bool _adRewardsAllowed;
        private long? _settledThroughTicks;

        public string GameId { get; }
        public string PlayerId { get; }
        public RewardRetentionPolicy Retention { get; }
        public int Count => _records.Count;
        public int SettledCount { get; private set; }
        public DateTime? SettledThroughUtc =>
            _settledThroughTicks.HasValue ? new DateTime(_settledThroughTicks.Value, DateTimeKind.Utc) : (DateTime?)null;

        public RewardLedger(string gameId, string playerId, IReadOnlyDictionary<string, long> opening = null,
            RewardRetentionPolicy retention = null)
        {
            if (!GameProfileCatalog.IsKnownId(gameId)) throw new ArgumentException("Unknown game profile.", nameof(gameId));
            if (!IsId(playerId)) throw new ArgumentException("Invalid player id.", nameof(playerId));
            _opening = SortedCopy(opening ?? new Dictionary<string, long>());
            if (_opening.Any(kv => !IsAssetId(kv.Key) || kv.Value < 0))
                throw new ArgumentException("Opening balances need valid asset ids and non-negative amounts.", nameof(opening));
            GameId = gameId;
            PlayerId = playerId;
            Retention = retention ?? new RewardRetentionPolicy();
            _balances = SortedCopy(_opening);
            var business = GameBusinessProfileCatalog.Require(gameId);
            // Child-safe profiles never receive paid currency or purchase credits (non-relaxable).
            _paidAllowed = business.MonetizationEnabled && !EconomyBalanceSimulator.IsFamilySafeProfile(gameId);
            _adRewardsAllowed = business.AdsEnabled;
        }

        public long Balance(string assetId) => _balances.TryGetValue(assetId ?? string.Empty, out var v) ? v : 0L;
        public long Balance(CurrencyType currency) => Balance(currency.ToString());
        public bool TryGetRecord(string key, out RewardLedgerRecord record) => _records.TryGetValue(key ?? string.Empty, out record);

        /// <summary>Applies the grant and records its key together, or changes nothing.</summary>
        public RewardLedgerGrantResult TryGrant(RewardLedgerRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (!string.Equals(request.GameId, GameId, StringComparison.Ordinal) ||
                !string.Equals(request.PlayerId, PlayerId, StringComparison.Ordinal))
                return RewardLedgerGrantResult.Rejected(RewardLedgerReasons.LedgerOwnerMismatch);
            var invalid = Validate(request);
            if (invalid != null) return RewardLedgerGrantResult.Rejected(invalid);
            var isPaid = request.Source == RewardSources.Purchase || request.Amounts.ContainsKey(PaidAssetId);
            if (isPaid && !_paidAllowed) return RewardLedgerGrantResult.Rejected(RewardLedgerReasons.PaidGrantNotAllowed);
            if (request.Source == RewardSources.AdServerSideVerified && !_adRewardsAllowed)
                return RewardLedgerGrantResult.Rejected(RewardLedgerReasons.AdRewardNotAllowed);

            var key = RewardLedgerKey.Derive(GameId, PlayerId, request.Source, request.SourceInstanceId);
            var candidate = new RewardLedgerRecord(key, request);
            if (_records.TryGetValue(key, out var original))
                return string.Equals(original.Payload, candidate.Payload, StringComparison.Ordinal)
                    ? RewardLedgerGrantResult.Duplicate(original)
                    : RewardLedgerGrantResult.Rejected(RewardLedgerReasons.IdempotencyConflict);
            if (_settledThroughTicks.HasValue && IsSettledBy(candidate, _settledThroughTicks.Value))
                return RewardLedgerGrantResult.Rejected(RewardLedgerReasons.SourceSettled);
            if (!TryAdd(_balances, candidate.Amounts)) return RewardLedgerGrantResult.Rejected(RewardLedgerReasons.BalanceOverflow);
            _records.Add(key, candidate);
            return RewardLedgerGrantResult.Applied(candidate);
        }

        /// <summary>Folds expired records past retention into the settled segment. Horizon is monotonic.</summary>
        public int Prune(DateTime nowUtc)
        {
            if (nowUtc.Kind != DateTimeKind.Utc) throw new ArgumentException("UTC required.", nameof(nowUtc));
            if (_settledThroughTicks.HasValue && nowUtc.Ticks <= _settledThroughTicks.Value) return 0;
            var prunable = _records.Values.Where(r => IsSettledBy(r, nowUtc.Ticks)).ToList();
            foreach (var r in prunable)
            {
                TryAdd(_settled, r.Amounts); // cannot overflow: already part of _balances
                _records.Remove(r.Key);
            }
            SettledCount += prunable.Count;
            _settledThroughTicks = nowUtc.Ticks;
            return prunable.Count;
        }

        public string ToSnapshot()
        {
            var header = HeaderJson();
            var records = _records.Keys.OrderBy(k => k, StringComparer.Ordinal)
                .Select(k => (object)_records[k].ToJson()).ToList();
            header["sha256"] = CanonicalJson.Sha256Hex(new List<object> { HeaderJson(), records });
            var sb = new StringBuilder(CanonicalJson.Serialize(header));
            foreach (var r in records) sb.Append('\n').Append(CanonicalJson.Serialize(r));
            return sb.ToString();
        }

        public static RewardLedgerResult FromSnapshot(string text)
        {
            try
            {
                if (string.IsNullOrEmpty(text)) return Fail(RewardLedgerReasons.MalformedSnapshot);
                var lines = text.Split('\n');
                var header = CanonicalJson.Parse(lines[0]) as Dictionary<string, object>;
                if (header == null || !Equals(header["format"], SnapshotFormat)) return Fail(RewardLedgerReasons.MalformedSnapshot);
                if (!Equals(header["version"], SnapshotVersion)) return Fail(RewardLedgerReasons.UnsupportedSnapshotVersion);
                var records = lines.Skip(1).Select(CanonicalJson.Parse).ToList();
                var claimed = header["sha256"] as string;
                header.Remove("sha256");
                if (!string.Equals(claimed, CanonicalJson.Sha256Hex(new List<object> { header, records }), StringComparison.Ordinal))
                    return Fail(RewardLedgerReasons.IntegrityMismatch);

                var ledger = new RewardLedger((string)header["gameId"], (string)header["playerId"],
                    FromJsonMap(header["opening"]), RewardRetentionPolicy.FromJson(header["retention"]));
                var settled = (Dictionary<string, object>)header["settled"];
                var totals = FromJsonMap(settled["totals"]);
                if (totals.Any(kv => !IsAssetId(kv.Key) || kv.Value < 0) || !TryAdd(ledger._balances, totals))
                    return Fail(RewardLedgerReasons.MalformedSnapshot);
                foreach (var kv in totals) ledger._settled[kv.Key] = kv.Value;
                ledger.SettledCount = checked((int)(long)settled["count"]);
                ledger._settledThroughTicks = settled["throughTicks"] == null ? (long?)null : (long)settled["throughTicks"];

                foreach (Dictionary<string, object> r in records)
                {
                    var res = ledger.TryGrant(new RewardLedgerRequest(ledger.GameId, ledger.PlayerId,
                        (string)r["source"], (string)r["instance"], FromJsonMap(r["amounts"]),
                        new DateTime((long)r["grantedTicks"], DateTimeKind.Utc),
                        r["expiresTicks"] == null ? (DateTime?)null : new DateTime((long)r["expiresTicks"], DateTimeKind.Utc),
                        (string)r["reference"]));
                    if (res.Outcome != RewardLedgerOutcome.Applied)
                        return Fail(RewardLedgerReasons.MalformedSnapshot + ":" + (res.Outcome == RewardLedgerOutcome.Duplicate ? "DuplicateKey" : res.Reason));
                }
                return string.Equals(ledger.ToSnapshot(), text, StringComparison.Ordinal)
                    ? new RewardLedgerResult(ledger, null)
                    : Fail(RewardLedgerReasons.NonCanonicalSnapshot);
            }
            catch (Exception e) when (e is FormatException || e is ArgumentException || e is InvalidCastException ||
                                      e is KeyNotFoundException || e is NullReferenceException || e is OverflowException)
            {
                return Fail(RewardLedgerReasons.MalformedSnapshot);
            }
        }

        /// <summary>Order-independent union of two ledgers of the same owner (e.g. cloud + local).</summary>
        public static RewardLedgerResult Merge(RewardLedger a, RewardLedger b)
        {
            if (a == null) throw new ArgumentNullException(nameof(a));
            if (b == null) throw new ArgumentNullException(nameof(b));
            if (a.GameId != b.GameId || a.PlayerId != b.PlayerId) return Fail(RewardLedgerReasons.MergeOwnerMismatch);
            if (CanonicalJson.Serialize(ToJsonMap(a._opening)) != CanonicalJson.Serialize(ToJsonMap(b._opening)))
                return Fail(RewardLedgerReasons.OpeningBalanceMismatch);
            if (CanonicalJson.Serialize(a.Retention.ToJson()) != CanonicalJson.Serialize(b.Retention.ToJson()))
                return Fail(RewardLedgerReasons.RetentionPolicyMismatch);

            var src = CompareSettled(a, b) >= 0 ? a : b;
            var merged = new RewardLedger(a.GameId, a.PlayerId, a._opening, a.Retention);
            foreach (var kv in src._settled) merged._settled[kv.Key] = kv.Value;
            merged.SettledCount = src.SettledCount;
            merged._settledThroughTicks = src._settledThroughTicks;
            if (!TryAdd(merged._balances, merged._settled)) return Fail(RewardLedgerReasons.BalanceOverflow);

            var conflicts = new List<string>();
            foreach (var key in a._records.Keys.Union(b._records.Keys).OrderBy(k => k, StringComparer.Ordinal))
            {
                var variants = new[] { a, b }.Where(l => l._records.ContainsKey(key)).Select(l => l._records[key]).ToList();
                if (variants.Select(v => v.Payload).Distinct(StringComparer.Ordinal).Count() > 1) conflicts.Add(key);
                var live = variants.Where(v => !merged._settledThroughTicks.HasValue || !merged.IsSettledBy(v, merged._settledThroughTicks.Value));
                var winner = live.OrderBy(v => v, WinnerOrder.Instance).FirstOrDefault();
                if (winner == null) continue;
                if (!TryAdd(merged._balances, winner.Amounts)) return Fail(RewardLedgerReasons.BalanceOverflow);
                merged._records.Add(key, winner);
            }
            return new RewardLedgerResult(merged, null, conflicts);
        }

        // ---- internals -------------------------------------------------------------------------------

        private sealed class WinnerOrder : IComparer<RewardLedgerRecord>
        {
            public static readonly WinnerOrder Instance = new WinnerOrder();
            public int Compare(RewardLedgerRecord x, RewardLedgerRecord y)
            {
                // Latest expiry first (none = latest) so a winner is never settled before a loser.
                var ex = x.ExpiresUtc?.Ticks ?? long.MaxValue;
                var ey = y.ExpiresUtc?.Ticks ?? long.MaxValue;
                if (ex != ey) return ey.CompareTo(ex);
                var c = string.CompareOrdinal(x.Payload, y.Payload);
                return c != 0 ? c : x.GrantedUtc.Ticks.CompareTo(y.GrantedUtc.Ticks);
            }
        }

        private static int CompareSettled(RewardLedger a, RewardLedger b)
        {
            var c = (a._settledThroughTicks ?? -1L).CompareTo(b._settledThroughTicks ?? -1L);
            return c != 0 ? c : string.CompareOrdinal(CanonicalJson.Serialize(a.SettledJson()), CanonicalJson.Serialize(b.SettledJson()));
        }

        private bool IsSettledBy(RewardLedgerRecord r, long horizonTicks) =>
            r.ExpiresUtc.HasValue && r.ExpiresUtc.Value.Ticks <= horizonTicks - Retention.For(r.Source).Ticks;

        private static string Validate(RewardLedgerRequest r)
        {
            if (!RewardSources.IsKnown(r.Source)) return RewardLedgerReasons.UnknownRewardSource;
            if (!IsId(r.SourceInstanceId)) return RewardLedgerReasons.InvalidSourceInstance;
            if (r.Reference != null && !IsId(r.Reference)) return RewardLedgerReasons.InvalidReference;
            if (r.Amounts == null || r.Amounts.Count == 0 || r.Amounts.Count > MaxAssetsPerGrant ||
                r.Amounts.Any(kv => !IsAssetId(kv.Key) || kv.Value <= 0 || kv.Value > MaxAmountPerAsset))
                return RewardLedgerReasons.InvalidAmounts;
            if (r.GrantedUtc.Kind != DateTimeKind.Utc || r.ExpiresUtc.HasValue && r.ExpiresUtc.Value.Kind != DateTimeKind.Utc)
                return RewardLedgerReasons.NonUtcTimestamp;
            if (r.ExpiresUtc.HasValue && r.GrantedUtc > r.ExpiresUtc.Value) return RewardLedgerReasons.SourceExpired;
            return null;
        }

        /// <summary>Checks every asset first, then applies: no partial balance change on overflow.</summary>
        private static bool TryAdd(SortedDictionary<string, long> into, IReadOnlyDictionary<string, long> amounts)
        {
            foreach (var kv in amounts)
                if (into.TryGetValue(kv.Key, out var cur) && kv.Value > long.MaxValue - cur) return false;
            foreach (var kv in amounts) into[kv.Key] = (into.TryGetValue(kv.Key, out var cur) ? cur : 0L) + kv.Value;
            return true;
        }

        private Dictionary<string, object> SettledJson() => new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["count"] = (long)SettledCount,
            ["throughTicks"] = _settledThroughTicks.HasValue ? (object)_settledThroughTicks.Value : null,
            ["totals"] = ToJsonMap(_settled)
        };

        private Dictionary<string, object> HeaderJson() => new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["format"] = SnapshotFormat,
            ["version"] = SnapshotVersion,
            ["gameId"] = GameId,
            ["playerId"] = PlayerId,
            ["opening"] = ToJsonMap(_opening),
            ["retention"] = Retention.ToJson(),
            ["settled"] = SettledJson()
        };

        private static RewardLedgerResult Fail(string reason) => new RewardLedgerResult(null, reason);

        private static bool IsId(string s) =>
            !string.IsNullOrEmpty(s) && s.Length <= MaxIdLength && s.All(c => !char.IsControl(c) && !char.IsSurrogate(c));

        internal static bool IsAssetId(string s) =>
            !string.IsNullOrEmpty(s) && s.Length <= 64 &&
            s.All(c => c >= 'A' && c <= 'Z' || c >= 'a' && c <= 'z' || c >= '0' && c <= '9' || c == '_' || c == '-');

        internal static SortedDictionary<string, long> SortedCopy(IEnumerable<KeyValuePair<string, long>> source)
        {
            var map = new SortedDictionary<string, long>(StringComparer.Ordinal);
            foreach (var kv in source) map[kv.Key] = kv.Value;
            return map;
        }

        internal static Dictionary<string, object> ToJsonMap(IEnumerable<KeyValuePair<string, long>> map) =>
            map.ToDictionary(kv => kv.Key, kv => (object)kv.Value, StringComparer.Ordinal);

        private static SortedDictionary<string, long> FromJsonMap(object o) =>
            SortedCopy(((Dictionary<string, object>)o).Select(kv => new KeyValuePair<string, long>(kv.Key, (long)kv.Value)));
    }
}
