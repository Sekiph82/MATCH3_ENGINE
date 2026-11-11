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
    // Key: "rgk1|" + L(gameId) + L(playerId) + L(source) + L(sourceInstanceId), L(s) = len ":" s "|", Ordinal.
    // Length prefixes make the key injective even when ids contain '|' or ':'.
    //
    // Sources: WINDOWED sources (daily, weekly, weekly_personal_best, seasonal_collection, tournament, catch_up)
    // REQUIRE ExpiresUtc and are judged against a server-anchored TrustedUtcAnchor (SourceExpired: anchor > expiry).
    // TryGrant(request, anchor) is the strict path; the one-argument TryGrant(request) is kept for existing callers
    // (ENG-M20-012) and treats request.GrantedUtc as caller-asserted server time. PERMANENT sources (first_clear, level_win, streak, chest, earned_pack, iap, ad_ssv)
    // FORBID ExpiresUtc: each instance id names one earned/purchased fact that never expires.
    //
    // Assets: fixed registry = CurrencyType names (Ordinal). Paid asset = PremiumGems; paid grants (iap or any
    // PremiumGems) are rejected for non-monetized or family-safe profiles, also in opening and settled totals.
    //
    // Persistence: canonical JSON lines (LiveOps.CanonicalJson): header line, then one line per key (live record or
    // settled tombstone) sorted by key. Header "sha256" = SHA-256 over canonical [headerWithoutSha, lines]. Restore
    // rejects hash mismatch, non-canonical text and policy violations. SHA-256 is tamper-EVIDENCE only; authenticity
    // needs a server signature (not implemented).
    //
    // Settlement: Prune(trusted anchor) turns a windowed record whose ExpiresUtc + retention(source) has passed into
    // a TOMBSTONE (source, instance, payload digest, amounts); timestamps/reference are dropped but the key is kept
    // forever. A tombstoned key is never applied again (any re-presentation -> KeySettled). Permanent records are
    // never pruned. Key count is NOT bounded: keys are the exactly-once proof.
    //
    // Merge: exact union by key over live records and tombstones. One variant per key wins by the total order
    // (payload digest asc, tombstone before live, GrantedUtc asc): commutative, associative, idempotent. Different
    // digests under one key are reported in ConflictedKeys and counted once. Balance = opening + amounts of the
    // winning variant of every unique key; never zero, never twice.
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

        public static IReadOnlyList<string> Windowed { get; } = Array.AsReadOnly(new[]
        {
            Daily, Weekly, WeeklyPersonalBest, SeasonalCollection, Tournament, CatchUp
        });

        public static bool IsKnown(string source) => source != null && All.Contains(source, StringComparer.Ordinal);
        public static bool IsWindowed(string source) => source != null && Windowed.Contains(source, StringComparer.Ordinal);
    }

    public static class RewardLedgerReasons
    {
        public const string IdempotencyConflict = "IdempotencyConflict";
        public const string KeySettled = "KeySettled";
        public const string PaidGrantNotAllowed = "PaidGrantNotAllowed";
        public const string AdRewardNotAllowed = "AdRewardNotAllowed";
        public const string LedgerOwnerMismatch = "LedgerOwnerMismatch";
        public const string UnknownRewardSource = "UnknownRewardSource";
        public const string InvalidSourceInstance = "InvalidSourceInstance";
        public const string InvalidReference = "InvalidReference";
        public const string InvalidAmounts = "InvalidAmounts";
        public const string NonUtcTimestamp = "NonUtcTimestamp";
        public const string ExpiryRequired = "ExpiryRequired";
        public const string ExpiryNotAllowed = "ExpiryNotAllowed";
        public const string TrustedTimeAnchorRequired = "TrustedTimeAnchorRequired";
        public const string SourceExpired = "SourceExpired";
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
        /// <summary>Recorded metadata only; expiry is checked against the trusted anchor, not this value.</summary>
        public DateTime GrantedUtc { get; }
        /// <summary>End of a windowed source's claim window; must be null for permanent sources.</summary>
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
        /// <summary>SHA-256 hex of canonical amounts+expiry+reference; equal digest under one key = same grant.</summary>
        public string PayloadDigest { get; }

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
            PayloadDigest = CanonicalJson.Sha256Hex(new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["amounts"] = RewardLedger.ToJsonMap(Amounts),
                ["expiresTicks"] = ExpiresUtc.HasValue ? (object)ExpiresUtc.Value.Ticks : null,
                ["reference"] = Reference
            });
        }

        internal Dictionary<string, object> ToJson() => new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["state"] = "live",
            ["source"] = Source,
            ["instance"] = SourceInstanceId,
            ["reference"] = Reference,
            ["amounts"] = RewardLedger.ToJsonMap(Amounts),
            ["grantedTicks"] = GrantedUtc.Ticks,
            ["expiresTicks"] = ExpiresUtc.HasValue ? (object)ExpiresUtc.Value.Ticks : null
        };
    }

    /// <summary>Settled key: kept forever so the grant can never be applied again or lost in a merge.</summary>
    public sealed class RewardLedgerTombstone
    {
        public string Key { get; }
        public string Source { get; }
        public string SourceInstanceId { get; }
        public string PayloadDigest { get; }
        public IReadOnlyDictionary<string, long> Amounts { get; }

        internal RewardLedgerTombstone(string key, string source, string instance, string digest, IReadOnlyDictionary<string, long> amounts)
        {
            Key = key;
            Source = source;
            SourceInstanceId = instance;
            PayloadDigest = digest;
            Amounts = amounts;
        }

        internal Dictionary<string, object> ToJson() => new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["state"] = "settled",
            ["source"] = Source,
            ["instance"] = SourceInstanceId,
            ["digest"] = PayloadDigest,
            ["amounts"] = RewardLedger.ToJsonMap(Amounts)
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
        /// <summary>Merge only: keys seen with different payload digests (counted once; route to reconciliation).</summary>
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
                if (!RewardSources.IsWindowed(kv.Key)) throw new ArgumentException("Retention applies to windowed sources only: " + kv.Key, nameof(daysBySource));
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
            var by = ((Dictionary<string, object>)d["bySource"]).ToDictionary(kv => kv.Key, kv => checked((int)(long)kv.Value), StringComparer.Ordinal);
            return new RewardRetentionPolicy(checked((int)(long)d["defaultDays"]), by);
        }
    }

    public sealed class RewardLedger
    {
        public const string SnapshotFormat = "m3-reward-ledger";
        public const long SnapshotVersion = 2;
        public const int MaxIdLength = 128;
        public const long MaxAmountPerAsset = 1000000000L;
        public static readonly string PaidAssetId = nameof(CurrencyType.PremiumGems);
        /// <summary>Fixed asset registry (Ordinal): the CurrencyType names.</summary>
        public static IReadOnlyList<string> KnownAssets { get; } = Array.AsReadOnly(Enum.GetNames(typeof(CurrencyType)));

        private readonly SortedDictionary<string, long> _opening;
        private readonly SortedDictionary<string, long> _balances;
        private readonly Dictionary<string, RewardLedgerRecord> _records = new Dictionary<string, RewardLedgerRecord>(StringComparer.Ordinal);
        private readonly Dictionary<string, RewardLedgerTombstone> _settled = new Dictionary<string, RewardLedgerTombstone>(StringComparer.Ordinal);
        private readonly bool _paidAllowed;
        private readonly bool _adRewardsAllowed;

        public string GameId { get; }
        public string PlayerId { get; }
        public RewardRetentionPolicy Retention { get; }
        /// <summary>Live (unsettled) records.</summary>
        public int Count => _records.Count;
        public int SettledCount => _settled.Count;

        public RewardLedger(string gameId, string playerId, IReadOnlyDictionary<string, long> opening = null,
            RewardRetentionPolicy retention = null)
        {
            if (!GameProfileCatalog.IsKnownId(gameId)) throw new ArgumentException("Unknown game profile.", nameof(gameId));
            if (!IsId(playerId)) throw new ArgumentException("Invalid player id.", nameof(playerId));
            GameId = gameId;
            PlayerId = playerId;
            Retention = retention ?? new RewardRetentionPolicy();
            var business = GameBusinessProfileCatalog.Require(gameId);
            // Non-monetized and family-safe profiles never hold paid currency (non-relaxable).
            _paidAllowed = business.MonetizationEnabled && !EconomyBalanceSimulator.IsFamilySafeProfile(gameId);
            _adRewardsAllowed = business.AdsEnabled;
            _opening = SortedCopy(opening ?? new Dictionary<string, long>());
            if (_opening.Any(kv => !IsKnownAsset(kv.Key) || kv.Value < 0))
                throw new ArgumentException("Opening balances need registered assets and non-negative amounts.", nameof(opening));
            if (!_paidAllowed && _opening.TryGetValue(PaidAssetId, out var paid) && paid > 0)
                throw new ArgumentException(RewardLedgerReasons.PaidGrantNotAllowed, nameof(opening));
            _balances = SortedCopy(_opening);
        }

        public long Balance(string assetId) => _balances.TryGetValue(assetId ?? string.Empty, out var v) ? v : 0L;
        public long Balance(CurrencyType currency) => Balance(currency.ToString());
        /// <summary>Live records only; settled keys are visible through <see cref="TryGetTombstone"/>.</summary>
        public bool TryGetRecord(string key, out RewardLedgerRecord record) => _records.TryGetValue(key ?? string.Empty, out record);
        public bool TryGetTombstone(string key, out RewardLedgerTombstone tombstone) => _settled.TryGetValue(key ?? string.Empty, out tombstone);
        /// <summary>True when the key was ever granted (live or settled).</summary>
        public bool ContainsKey(string key) => key != null && (_records.ContainsKey(key) || _settled.ContainsKey(key));

        /// <summary>Instance ids of every granted key (live and settled) for one source, Ordinal-sorted.</summary>
        public IReadOnlyList<string> InstanceIds(string source) =>
            _records.Values.Where(r => r.Source == source).Select(r => r.SourceInstanceId)
                .Concat(_settled.Values.Where(t => t.Source == source).Select(t => t.SourceInstanceId))
                .OrderBy(s => s, StringComparer.Ordinal).ToList();

        /// <summary>
        /// Compatibility overload: for windowed sources the caller ASSERTS that request.GrantedUtc is trusted server
        /// time (it becomes the anchor). Prefer <see cref="TryGrant(RewardLedgerRequest, TrustedUtcAnchor)"/>.
        /// </summary>
        public RewardLedgerGrantResult TryGrant(RewardLedgerRequest request) =>
            TryGrant(request, TrustedUtcAnchor.Server((request ?? throw new ArgumentNullException(nameof(request))).GrantedUtc));

        /// <summary>Applies the grant and records its key together, or changes nothing.</summary>
        public RewardLedgerGrantResult TryGrant(RewardLedgerRequest request, TrustedUtcAnchor anchor) => Apply(request, anchor, false);

        /// <summary>Settles windowed records whose expiry + retention has passed by the server-anchored time.</summary>
        public int Prune(TrustedUtcAnchor anchor)
        {
            if (!anchor.IsServerAnchored || anchor.UtcNow.Kind != DateTimeKind.Utc)
                throw new ArgumentException(RewardLedgerReasons.TrustedTimeAnchorRequired, nameof(anchor));
            var due = _records.Values.Where(r => r.ExpiresUtc.HasValue &&
                r.ExpiresUtc.Value.Ticks <= anchor.UtcNow.Ticks - Retention.For(r.Source).Ticks).ToList();
            foreach (var r in due)
            {
                _records.Remove(r.Key);
                _settled.Add(r.Key, new RewardLedgerTombstone(r.Key, r.Source, r.SourceInstanceId, r.PayloadDigest, r.Amounts));
            }
            return due.Count;
        }

        public string ToSnapshot()
        {
            var header = HeaderJson();
            var lines = Lines();
            header["sha256"] = CanonicalJson.Sha256Hex(new List<object> { HeaderJson(), lines });
            var sb = new StringBuilder(CanonicalJson.Serialize(header));
            foreach (var l in lines) sb.Append('\n').Append(CanonicalJson.Serialize(l));
            return sb.ToString();
        }

        public static RewardLedgerResult FromSnapshot(string text)
        {
            try
            {
                if (string.IsNullOrEmpty(text)) return Fail(RewardLedgerReasons.MalformedSnapshot);
                var parts = text.Split('\n');
                var header = CanonicalJson.Parse(parts[0]) as Dictionary<string, object>;
                if (header == null || !Equals(header["format"], SnapshotFormat)) return Fail(RewardLedgerReasons.MalformedSnapshot);
                if (!Equals(header["version"], SnapshotVersion)) return Fail(RewardLedgerReasons.UnsupportedSnapshotVersion);
                var lines = parts.Skip(1).Select(CanonicalJson.Parse).ToList();
                var claimed = header["sha256"] as string;
                header.Remove("sha256");
                if (!string.Equals(claimed, CanonicalJson.Sha256Hex(new List<object> { header, lines }), StringComparison.Ordinal))
                    return Fail(RewardLedgerReasons.IntegrityMismatch);

                var ledger = new RewardLedger((string)header["gameId"], (string)header["playerId"],
                    FromJsonMap(header["opening"]), RewardRetentionPolicy.FromJson(header["retention"]));
                foreach (Dictionary<string, object> l in lines)
                {
                    var source = (string)l["source"];
                    var instance = (string)l["instance"];
                    var amounts = FromJsonMap(l["amounts"]);
                    string reason;
                    if ((string)l["state"] == "settled")
                        reason = ledger.AddTombstone(source, instance, (string)l["digest"], amounts);
                    else if ((string)l["state"] == "live")
                    {
                        var res = ledger.Apply(new RewardLedgerRequest(ledger.GameId, ledger.PlayerId, source, instance, amounts,
                            new DateTime((long)l["grantedTicks"], DateTimeKind.Utc),
                            l["expiresTicks"] == null ? (DateTime?)null : new DateTime((long)l["expiresTicks"], DateTimeKind.Utc),
                            (string)l["reference"]), default(TrustedUtcAnchor), true);
                        reason = res.Outcome == RewardLedgerOutcome.Applied ? null
                            : res.Outcome == RewardLedgerOutcome.Duplicate ? "DuplicateKey" : res.Reason;
                    }
                    else reason = "UnknownState";
                    if (reason != null) return Fail(RewardLedgerReasons.MalformedSnapshot + ":" + reason);
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

        /// <summary>Exact, order-independent union of two ledgers of the same owner (e.g. cloud + local).</summary>
        public static RewardLedgerResult Merge(RewardLedger a, RewardLedger b)
        {
            if (a == null) throw new ArgumentNullException(nameof(a));
            if (b == null) throw new ArgumentNullException(nameof(b));
            if (a.GameId != b.GameId || a.PlayerId != b.PlayerId) return Fail(RewardLedgerReasons.MergeOwnerMismatch);
            if (CanonicalJson.Serialize(ToJsonMap(a._opening)) != CanonicalJson.Serialize(ToJsonMap(b._opening)))
                return Fail(RewardLedgerReasons.OpeningBalanceMismatch);
            if (CanonicalJson.Serialize(a.Retention.ToJson()) != CanonicalJson.Serialize(b.Retention.ToJson()))
                return Fail(RewardLedgerReasons.RetentionPolicyMismatch);

            var merged = new RewardLedger(a.GameId, a.PlayerId, a._opening, a.Retention);
            var conflicts = new List<string>();
            var keys = a._records.Keys.Concat(a._settled.Keys).Concat(b._records.Keys).Concat(b._settled.Keys)
                .Distinct(StringComparer.Ordinal).OrderBy(k => k, StringComparer.Ordinal);
            foreach (var key in keys)
            {
                var variants = new List<Variant>();
                foreach (var l in new[] { a, b })
                {
                    if (l._records.TryGetValue(key, out var r)) variants.Add(new Variant(r.PayloadDigest, false, r.GrantedUtc.Ticks, r, null));
                    if (l._settled.TryGetValue(key, out var t)) variants.Add(new Variant(t.PayloadDigest, true, 0, null, t));
                }
                if (variants.Select(v => v.Digest).Distinct(StringComparer.Ordinal).Count() > 1) conflicts.Add(key);
                var w = variants.OrderBy(v => v.Digest, StringComparer.Ordinal).ThenBy(v => v.Settled ? 0 : 1).ThenBy(v => v.Ticks).First();
                if (!TryAdd(merged._balances, w.Record?.Amounts ?? w.Tombstone.Amounts)) return Fail(RewardLedgerReasons.BalanceOverflow);
                if (w.Settled) merged._settled.Add(key, w.Tombstone);
                else merged._records.Add(key, w.Record);
            }
            return new RewardLedgerResult(merged, null, conflicts);
        }

        // ---- internals -------------------------------------------------------------------------------

        private readonly struct Variant
        {
            public readonly string Digest;
            public readonly bool Settled;
            public readonly long Ticks;
            public readonly RewardLedgerRecord Record;
            public readonly RewardLedgerTombstone Tombstone;

            public Variant(string digest, bool settled, long ticks, RewardLedgerRecord record, RewardLedgerTombstone tombstone)
            {
                Digest = digest;
                Settled = settled;
                Ticks = ticks;
                Record = record;
                Tombstone = tombstone;
            }
        }

        private RewardLedgerGrantResult Apply(RewardLedgerRequest request, TrustedUtcAnchor anchor, bool restoring)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (!string.Equals(request.GameId, GameId, StringComparison.Ordinal) ||
                !string.Equals(request.PlayerId, PlayerId, StringComparison.Ordinal))
                return RewardLedgerGrantResult.Rejected(RewardLedgerReasons.LedgerOwnerMismatch);
            var invalid = Validate(request) ?? CheckAmounts(request.Source, request.Amounts);
            if (invalid != null) return RewardLedgerGrantResult.Rejected(invalid);

            var key = RewardLedgerKey.Derive(GameId, PlayerId, request.Source, request.SourceInstanceId);
            var candidate = new RewardLedgerRecord(key, request);
            if (_records.TryGetValue(key, out var original))
                return string.Equals(original.PayloadDigest, candidate.PayloadDigest, StringComparison.Ordinal)
                    ? RewardLedgerGrantResult.Duplicate(original)
                    : RewardLedgerGrantResult.Rejected(RewardLedgerReasons.IdempotencyConflict);
            if (_settled.ContainsKey(key)) return RewardLedgerGrantResult.Rejected(RewardLedgerReasons.KeySettled);
            if (!restoring && request.ExpiresUtc.HasValue)
            {
                if (!anchor.IsServerAnchored || anchor.UtcNow.Kind != DateTimeKind.Utc)
                    return RewardLedgerGrantResult.Rejected(RewardLedgerReasons.TrustedTimeAnchorRequired);
                if (anchor.UtcNow > request.ExpiresUtc.Value) return RewardLedgerGrantResult.Rejected(RewardLedgerReasons.SourceExpired);
            }
            if (!TryAdd(_balances, candidate.Amounts)) return RewardLedgerGrantResult.Rejected(RewardLedgerReasons.BalanceOverflow);
            _records.Add(key, candidate);
            return RewardLedgerGrantResult.Applied(candidate);
        }

        private string AddTombstone(string source, string instance, string digest, IReadOnlyDictionary<string, long> amounts)
        {
            if (!RewardSources.IsWindowed(source)) return RewardLedgerReasons.UnknownRewardSource;
            if (!IsId(instance)) return RewardLedgerReasons.InvalidSourceInstance;
            if (digest == null || digest.Length != 64 || !digest.All(c => c >= '0' && c <= '9' || c >= 'a' && c <= 'f'))
                return "InvalidDigest";
            var invalid = CheckAmounts(source, amounts);
            if (invalid != null) return invalid;
            var key = RewardLedgerKey.Derive(GameId, PlayerId, source, instance);
            if (ContainsKey(key)) return "DuplicateKey";
            if (!TryAdd(_balances, amounts)) return RewardLedgerReasons.BalanceOverflow;
            _settled.Add(key, new RewardLedgerTombstone(key, source, instance, digest, amounts));
            return null;
        }

        private static string Validate(RewardLedgerRequest r)
        {
            if (!RewardSources.IsKnown(r.Source)) return RewardLedgerReasons.UnknownRewardSource;
            if (!IsId(r.SourceInstanceId)) return RewardLedgerReasons.InvalidSourceInstance;
            if (r.Reference != null && !IsId(r.Reference)) return RewardLedgerReasons.InvalidReference;
            if (r.GrantedUtc.Kind != DateTimeKind.Utc || r.ExpiresUtc.HasValue && r.ExpiresUtc.Value.Kind != DateTimeKind.Utc)
                return RewardLedgerReasons.NonUtcTimestamp;
            var windowed = RewardSources.IsWindowed(r.Source);
            if (windowed && !r.ExpiresUtc.HasValue) return RewardLedgerReasons.ExpiryRequired;
            if (!windowed && r.ExpiresUtc.HasValue) return RewardLedgerReasons.ExpiryNotAllowed;
            return null;
        }

        /// <summary>Asset registry, amount bounds and the paid/ad profile rules (shared by grants and tombstones).</summary>
        private string CheckAmounts(string source, IReadOnlyDictionary<string, long> amounts)
        {
            if (amounts == null || amounts.Count == 0 ||
                amounts.Any(kv => !IsKnownAsset(kv.Key) || kv.Value <= 0 || kv.Value > MaxAmountPerAsset))
                return RewardLedgerReasons.InvalidAmounts;
            if ((source == RewardSources.Purchase || amounts.ContainsKey(PaidAssetId)) && !_paidAllowed)
                return RewardLedgerReasons.PaidGrantNotAllowed;
            if (source == RewardSources.AdServerSideVerified && !_adRewardsAllowed)
                return RewardLedgerReasons.AdRewardNotAllowed;
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

        private List<object> Lines() =>
            _records.Keys.Concat(_settled.Keys).OrderBy(k => k, StringComparer.Ordinal)
                .Select(k => (object)(_records.TryGetValue(k, out var r) ? r.ToJson() : _settled[k].ToJson())).ToList();

        private Dictionary<string, object> HeaderJson() => new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["format"] = SnapshotFormat,
            ["version"] = SnapshotVersion,
            ["gameId"] = GameId,
            ["playerId"] = PlayerId,
            ["opening"] = ToJsonMap(_opening),
            ["retention"] = Retention.ToJson()
        };

        private static RewardLedgerResult Fail(string reason) => new RewardLedgerResult(null, reason);

        private static bool IsId(string s) =>
            !string.IsNullOrEmpty(s) && s.Length <= MaxIdLength && s.All(c => !char.IsControl(c) && !char.IsSurrogate(c));

        public static bool IsKnownAsset(string assetId) => assetId != null && KnownAssets.Contains(assetId, StringComparer.Ordinal);

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
