using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Match3.Engine.Runtime.LiveOps
{
    // ENG-M20-010: Engine-owned, producer-neutral receiver for world/level/event
    // metadata. Any producer (Level Factory, a test pack tool, a hand-written
    // fixture) emits the same versioned JSON envelope; the Engine validates it.
    //
    // Wire contract (schema 1.x):
    // {
    //   "schemaVersion": "1.0",            // MAJOR.MINOR; only major 1 accepted
    //   "producerId": "<opaque ascii>",    // recorded, never trusted for authorization
    //   "gameId": "<receiver game>",
    //   "items": [ {
    //     "contentKind": "World" | "Level" | "Event",
    //     "contentId": "<id>", "contentVersion": <int >= 1>,
    //     "payload": { ...kind fields..., "x-<ext>": <ignored> },
    //     "payloadSha256": "<64 lowercase hex>"   // optional integrity digest (unkeyed; not authentication)
    //   } ]
    // }
    // World payload: { "order": int }
    // Level payload: { "worldId": id, "order": int, "moves": int,
    //                  "objectives": [ { "kind": GoalKind, "targetId": id, "count": int } ],
    //                  "obstacleIds": [ id ] }
    // Event payload: { "startUtc": "yyyy-MM-ddTHH:mm:ssZ", "endUtc": "...", "levelIds": [ id ] }
    //
    // payloadSha256 = SHA-256 over CanonicalJson.Serialize(payload) as UTF-8
    // (sorted keys, no whitespace, integers only, ASCII-escaped strings; byte-identical
    // to Python json.dumps(payload, sort_keys=True, separators=(",", ":"))).
    //
    // Fail-closed policy: unknown envelope fields reject; unknown payload fields
    // reject unless prefixed "x-"; any key naming prices, SKUs, IAP, receipts,
    // credentials or tokens rejects anywhere (monetization stays Engine/store
    // authoritative). Any rejected item rejects the whole batch (nothing applied).

    public enum ContentKind { World = 0, Level = 1, Event = 2 }

    /// <summary>
    /// Integrity only: payloadSha256 is an unkeyed SHA-256, so DigestVerified proves the payload
    /// was not altered relative to the digest in the same envelope -- it does NOT authenticate the producer.
    /// </summary>
    public enum ContentIntegrity { NoDigest = 0, DigestVerified = 1 }

    public static class ContentMetadataReasons
    {
        public const string Accepted = "Accepted";
        public const string Unchanged = "Unchanged";
        public const string ItemRejected = "ItemRejected";

        public const string MalformedDocument = "MalformedDocument";
        public const string MissingField = "MissingField";
        public const string FieldTypeInvalid = "FieldTypeInvalid";
        public const string UnknownField = "UnknownField";
        public const string UnknownPayloadField = "UnknownPayloadField";
        public const string ForbiddenMonetizationField = "ForbiddenMonetizationField";
        public const string SchemaVersionMalformed = "SchemaVersionMalformed";
        public const string SchemaMajorUnsupported = "SchemaMajorUnsupported";
        public const string ProducerIdInvalid = "ProducerIdInvalid";
        public const string GameMismatch = "GameMismatch";
        public const string EmptyBatch = "EmptyBatch";
        public const string BatchTooLarge = "BatchTooLarge";

        public const string UnknownContentKind = "UnknownContentKind";
        public const string ContentKindNotAllowed = "ContentKindNotAllowed";
        public const string IdInvalid = "IdInvalid";
        public const string DuplicateContentId = "DuplicateContentId";
        public const string ValueOutOfRange = "ValueOutOfRange";
        public const string DigestMalformed = "DigestMalformed";
        public const string DigestMismatch = "DigestMismatch";
        public const string DanglingReference = "DanglingReference";
        public const string DuplicateOrder = "DuplicateOrder";
        public const string EventWindowMalformed = "EventWindowMalformed";
        public const string EventWindowInverted = "EventWindowInverted";
        public const string ContentVersionRegression = "ContentVersionRegression";
        public const string ContentVersionConflict = "ContentVersionConflict";
    }

    public sealed class LevelObjective
    {
        public GoalKind Kind { get; }
        public string TargetId { get; }
        public int Count { get; }

        internal LevelObjective(GoalKind kind, string targetId, int count)
        {
            Kind = kind;
            TargetId = targetId;
            Count = count;
        }
    }

    /// <summary>One accepted metadata item. Kind-specific fields are default/empty for other kinds.</summary>
    public sealed class ContentMetadataItem
    {
        private static readonly IReadOnlyList<string> NoIds = Array.AsReadOnly(Array.Empty<string>());

        public ContentKind Kind { get; internal set; }
        public string ContentId { get; internal set; }
        public int ContentVersion { get; internal set; }
        public string ProducerId { get; internal set; }
        public ContentIntegrity Integrity { get; internal set; }
        /// <summary>SHA-256 of the canonical payload, always computed by the Engine.</summary>
        public string PayloadSha256 { get; internal set; }
        /// <summary>World and Level.</summary>
        public int Order { get; internal set; }
        /// <summary>Level.</summary>
        public string WorldId { get; internal set; }
        public int Moves { get; internal set; }
        public IReadOnlyList<LevelObjective> Objectives { get; internal set; } = Array.AsReadOnly(Array.Empty<LevelObjective>());
        public IReadOnlyList<string> ObstacleIds { get; internal set; } = NoIds;
        /// <summary>Event (UTC, start &lt; end).</summary>
        public DateTime StartUtc { get; internal set; }
        public DateTime EndUtc { get; internal set; }
        public IReadOnlyList<string> LevelIds { get; internal set; } = NoIds;
    }

    public sealed class ContentItemResult
    {
        public int Index { get; }
        public string ContentKind { get; }
        public string ContentId { get; }
        public string Reason { get; }
        public string Detail { get; }
        public ContentIntegrity Integrity { get; }
        public bool Accepted =>
            Reason == ContentMetadataReasons.Accepted || Reason == ContentMetadataReasons.Unchanged;

        internal ContentItemResult(int index, string kind, string id, string reason, string detail, ContentIntegrity integrity)
        {
            Index = index;
            ContentKind = kind;
            ContentId = id;
            Reason = reason;
            Detail = detail ?? "";
            Integrity = integrity;
        }
    }

    public sealed class ContentBatchResult
    {
        public bool Accepted { get; }
        /// <summary>Accepted, a batch-level reason code, or ItemRejected.</summary>
        public string Reason { get; }
        public string Detail { get; }
        public IReadOnlyList<ContentItemResult> Items { get; }
        /// <summary>Deterministic one-line-per-item summary.</summary>
        public string Summary { get; }

        internal ContentBatchResult(bool accepted, string reason, string detail, IList<ContentItemResult> items, string game, string producer)
        {
            Accepted = accepted;
            Reason = reason;
            Detail = detail ?? "";
            Items = new ReadOnlyCollection<ContentItemResult>(items ?? new List<ContentItemResult>());
            var sb = new StringBuilder();
            sb.Append(accepted ? "ACCEPTED" : "REJECTED")
              .Append(" reason=").Append(reason)
              .Append(" game=").Append(game ?? "-")
              .Append(" producer=").Append(producer ?? "-")
              .Append(" items=").Append(Items.Count)
              .Append(" rejected=").Append(Items.Count(i => !i.Accepted))
              .Append(" noDigest=").Append(Items.Count(i => i.Integrity == ContentIntegrity.NoDigest));
            if (Detail.Length > 0) sb.Append(" detail=").Append(Detail);
            foreach (var i in Items)
                sb.Append('\n').Append('[').Append(i.Index).Append("] ")
                  .Append(i.ContentKind ?? "-").Append('/').Append(i.ContentId ?? "-").Append(' ')
                  .Append(i.Reason).Append(' ').Append(i.Integrity)
                  .Append(i.Detail.Length > 0 ? " " + i.Detail : "");
            Summary = sb.ToString();
        }
    }

    public sealed class ContentMetadataReceiver
    {
        public const int SupportedSchemaMajor = 1;
        public const int MaxItems = 512;

        private static readonly string[] EnvelopeFields = { "gameId", "items", "producerId", "schemaVersion" };
        private static readonly string[] ItemFields = { "contentId", "contentKind", "contentVersion", "payload", "payloadSha256" };
        private static readonly string[] WorldFields = { "order" };
        private static readonly string[] LevelFields = { "moves", "objectives", "obstacleIds", "order", "worldId" };
        private static readonly string[] EventFields = { "endUtc", "levelIds", "startUtc" };
        private static readonly string[] ObjectiveFields = { "count", "kind", "targetId" };

        // Exact words (after compaction, also with one trailing 's' removed). Soft-currency
        // words (cost, coins, gems) stay allowed: they are Engine-owned virtual economy terms.
        private static readonly HashSet<string> ForbiddenWords = new HashSet<string>(StringComparer.Ordinal)
        {
            "price", "pricing", "sku", "iap", "inapp", "receipt", "purchase", "currency", "credential", "secret",
            "token", "password", "apikey", "payment", "billing", "transaction", "paid", "bearer", "auth",
            "authorization", "authorisation", "productid", "storeproduct", "storesku", "privatekey"
        };
        // Substrings of the whole compact key: long, unambiguous monetization/credential terms only,
        // so all-lowercase concatenations ("storeproductid", "purchasetoken") are caught too.
        private static readonly string[] ForbiddenSubstrings =
        {
            "price", "receipt", "purchase", "credential", "password", "apikey", "productid", "storeproduct",
            "privatekey", "payment", "billing", "transaction", "bearer"
        };

        private readonly HashSet<ContentKind> _allowedKinds;
        private readonly HashSet<string> _knownWorldIds;
        private readonly HashSet<string> _knownLevelIds;
        private readonly Dictionary<string, ContentMetadataItem> _applied = new Dictionary<string, ContentMetadataItem>(StringComparer.Ordinal);

        public string GameId { get; }
        public IReadOnlyDictionary<string, ContentMetadataItem> Applied => new ReadOnlyDictionary<string, ContentMetadataItem>(_applied);

        /// <param name="gameId">The only game this receiver accepts content for.</param>
        /// <param name="allowedKinds">Per-game-profile allow-list of content kinds.</param>
        /// <param name="knownWorldIds">World IDs already shipped with the build (reference targets).</param>
        /// <param name="knownLevelIds">Level IDs already shipped with the build (reference targets).</param>
        public ContentMetadataReceiver(string gameId, IEnumerable<ContentKind> allowedKinds,
            IEnumerable<string> knownWorldIds = null, IEnumerable<string> knownLevelIds = null)
        {
            if (!GameProfileCatalog.IsKnownId(gameId))
                throw new ArgumentException("Unknown game ID.", nameof(gameId));
            if (allowedKinds == null) throw new ArgumentNullException(nameof(allowedKinds));
            GameId = gameId;
            _allowedKinds = new HashSet<ContentKind>(allowedKinds);
            if (_allowedKinds.Any(k => !Enum.IsDefined(typeof(ContentKind), k)))
                throw new ArgumentException("Undefined content kind.", nameof(allowedKinds));
            _knownWorldIds = KnownSet(knownWorldIds, nameof(knownWorldIds));
            _knownLevelIds = KnownSet(knownLevelIds, nameof(knownLevelIds));
        }

        public ContentBatchResult Receive(string json)
        {
            object root;
            try { root = CanonicalJson.Parse(json); }
            catch (FormatException ex) { return BatchReject(ContentMetadataReasons.MalformedDocument, ex.Message); }

            if (!(root is Dictionary<string, object> doc))
                return BatchReject(ContentMetadataReasons.MalformedDocument, "root is not an object");

            foreach (var key in doc.Keys.OrderBy(k => k, StringComparer.Ordinal))
            {
                if (IsForbiddenKey(key)) return BatchReject(ContentMetadataReasons.ForbiddenMonetizationField, key);
                if (Array.IndexOf(EnvelopeFields, key) < 0) return BatchReject(ContentMetadataReasons.UnknownField, key);
            }
            foreach (var key in EnvelopeFields)
                if (!doc.ContainsKey(key)) return BatchReject(ContentMetadataReasons.MissingField, key);

            if (!(doc["schemaVersion"] is string schema) || !TryParseSchema(schema, out int major))
                return BatchReject(ContentMetadataReasons.SchemaVersionMalformed, "schemaVersion");
            if (major != SupportedSchemaMajor)
                return BatchReject(ContentMetadataReasons.SchemaMajorUnsupported, schema);
            if (!(doc["producerId"] is string producer) || !IsOpaqueProducerId(producer))
                return BatchReject(ContentMetadataReasons.ProducerIdInvalid, "producerId");
            if (!(doc["gameId"] is string game))
                return BatchReject(ContentMetadataReasons.FieldTypeInvalid, "gameId", null, producer);
            if (!StringComparer.Ordinal.Equals(game, GameId))
                return BatchReject(ContentMetadataReasons.GameMismatch, "expected " + GameId, game, producer);
            if (!(doc["items"] is List<object> rawItems))
                return BatchReject(ContentMetadataReasons.FieldTypeInvalid, "items", game, producer);
            if (rawItems.Count == 0) return BatchReject(ContentMetadataReasons.EmptyBatch, "", game, producer);
            if (rawItems.Count > MaxItems) return BatchReject(ContentMetadataReasons.BatchTooLarge, rawItems.Count.ToString(CultureInfo.InvariantCulture), game, producer);

            // Pass 1: per-item structure, values, digest.
            var n = rawItems.Count;
            var items = new ContentMetadataItem[n];
            var kindText = new string[n];
            var idText = new string[n];
            var reasons = new string[n];
            var details = new string[n];
            var seenIds = new HashSet<string>(StringComparer.Ordinal);
            for (var i = 0; i < n; i++)
            {
                try
                {
                    items[i] = ParseItem(rawItems[i], producer, out kindText[i], out idText[i]);
                    if (!seenIds.Add(items[i].ContentId))
                        throw new Reject(ContentMetadataReasons.DuplicateContentId, items[i].ContentId);
                }
                catch (Reject r)
                {
                    items[i] = null;
                    reasons[i] = r.Code;
                    details[i] = r.Message;
                }
            }

            // Pass 2: references, ordering and version against applied state.
            var batchWorlds = new HashSet<string>(items.Where(x => x != null && x.Kind == ContentKind.World).Select(x => x.ContentId), StringComparer.Ordinal);
            var batchLevels = new HashSet<string>(items.Where(x => x != null && x.Kind == ContentKind.Level).Select(x => x.ContentId), StringComparer.Ordinal);
            var orderSlots = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var a in _applied.Values)
                if (!seenIds.Contains(a.ContentId) && OrderSlot(a) != null) orderSlots[OrderSlot(a)] = a.ContentId;

            for (var i = 0; i < n; i++)
            {
                var item = items[i];
                if (item == null) continue;
                try
                {
                    if (item.Kind == ContentKind.Level && !ResolvesWorld(item.WorldId, batchWorlds))
                        throw new Reject(ContentMetadataReasons.DanglingReference, "worldId " + item.WorldId);
                    if (item.Kind == ContentKind.Event)
                        foreach (var level in item.LevelIds)
                            if (!ResolvesLevel(level, batchLevels))
                                throw new Reject(ContentMetadataReasons.DanglingReference, "levelId " + level);
                    var slot = OrderSlot(item);
                    if (slot != null)
                    {
                        if (orderSlots.TryGetValue(slot, out var holder))
                            throw new Reject(ContentMetadataReasons.DuplicateOrder, slot + " held by " + holder);
                        orderSlots[slot] = item.ContentId;
                    }
                    reasons[i] = ContentMetadataReasons.Accepted;
                    if (_applied.TryGetValue(item.ContentId, out var prior))
                    {
                        if (prior.Kind != item.Kind ||
                            (prior.ContentVersion == item.ContentVersion && prior.PayloadSha256 != item.PayloadSha256))
                            throw new Reject(ContentMetadataReasons.ContentVersionConflict, "v" + item.ContentVersion);
                        if (item.ContentVersion < prior.ContentVersion)
                            throw new Reject(ContentMetadataReasons.ContentVersionRegression, "v" + item.ContentVersion + " < v" + prior.ContentVersion);
                        if (item.ContentVersion == prior.ContentVersion)
                            reasons[i] = ContentMetadataReasons.Unchanged;
                    }
                }
                catch (Reject r)
                {
                    reasons[i] = r.Code;
                    details[i] = r.Message;
                }
            }

            var results = new List<ContentItemResult>(n);
            for (var i = 0; i < n; i++)
                results.Add(new ContentItemResult(i, kindText[i], idText[i], reasons[i], details[i],
                    items[i]?.Integrity ?? ContentIntegrity.NoDigest));

            if (results.Any(r => !r.Accepted))
                return new ContentBatchResult(false, ContentMetadataReasons.ItemRejected, "", results, game, producer);

            // Atomic apply: only reached when every item is accepted. Unchanged re-deliveries keep
            // the stored record (original producer and integrity), they never overwrite it.
            for (var i = 0; i < n; i++)
                if (reasons[i] == ContentMetadataReasons.Accepted) _applied[items[i].ContentId] = items[i];
            return new ContentBatchResult(true, ContentMetadataReasons.Accepted, "", results, game, producer);
        }

        private ContentMetadataItem ParseItem(object raw, string producer, out string kindText, out string idText)
        {
            kindText = null;
            idText = null;
            if (!(raw is Dictionary<string, object> obj))
                throw new Reject(ContentMetadataReasons.FieldTypeInvalid, "item is not an object");
            kindText = obj.TryGetValue("contentKind", out var k) ? k as string : null;
            idText = obj.TryGetValue("contentId", out var id) ? id as string : null;

            RejectForbiddenKeys(obj);
            foreach (var key in obj.Keys.OrderBy(x => x, StringComparer.Ordinal))
                if (Array.IndexOf(ItemFields, key) < 0)
                    throw new Reject(ContentMetadataReasons.UnknownField, key);

            if (kindText == null) throw Missing(obj, "contentKind");
            ContentKind kind;
            switch (kindText)
            {
                case "World": kind = ContentKind.World; break;
                case "Level": kind = ContentKind.Level; break;
                case "Event": kind = ContentKind.Event; break;
                default: throw new Reject(ContentMetadataReasons.UnknownContentKind, kindText);
            }
            if (!_allowedKinds.Contains(kind))
                throw new Reject(ContentMetadataReasons.ContentKindNotAllowed, kindText + " for " + GameId);

            var item = new ContentMetadataItem
            {
                Kind = kind,
                ContentId = RequireId(obj, "contentId"),
                ContentVersion = RequireInt(obj, "contentVersion", 1, int.MaxValue),
                ProducerId = producer
            };

            if (!obj.TryGetValue("payload", out var p)) throw new Reject(ContentMetadataReasons.MissingField, "payload");
            if (!(p is Dictionary<string, object> payload)) throw new Reject(ContentMetadataReasons.FieldTypeInvalid, "payload");

            switch (kind)
            {
                case ContentKind.World:
                    RequireKnownFields(payload, WorldFields);
                    item.Order = RequireInt(payload, "order", 1, 100000);
                    break;
                case ContentKind.Level:
                    RequireKnownFields(payload, LevelFields);
                    item.WorldId = RequireId(payload, "worldId");
                    item.Order = RequireInt(payload, "order", 1, 100000);
                    item.Moves = RequireInt(payload, "moves", 1, 999);
                    item.Objectives = Array.AsReadOnly(RequireList(payload, "objectives", 1, 8).Select(ParseObjective).ToArray());
                    item.ObstacleIds = RequireIdList(payload, "obstacleIds", 0, 32);
                    break;
                case ContentKind.Event:
                    RequireKnownFields(payload, EventFields);
                    item.StartUtc = RequireUtc(payload, "startUtc");
                    item.EndUtc = RequireUtc(payload, "endUtc");
                    if (item.StartUtc >= item.EndUtc)
                        throw new Reject(ContentMetadataReasons.EventWindowInverted, "startUtc >= endUtc");
                    item.LevelIds = RequireIdList(payload, "levelIds", 1, 256);
                    break;
            }

            item.PayloadSha256 = CanonicalJson.Sha256Hex(payload);
            item.Integrity = ContentIntegrity.NoDigest;
            if (obj.TryGetValue("payloadSha256", out var d))
            {
                if (!(d is string digest) || !IsLowerHex64(digest))
                    throw new Reject(ContentMetadataReasons.DigestMalformed, "payloadSha256");
                if (!StringComparer.Ordinal.Equals(digest, item.PayloadSha256))
                    throw new Reject(ContentMetadataReasons.DigestMismatch, "computed " + item.PayloadSha256);
                item.Integrity = ContentIntegrity.DigestVerified;
            }
            return item;
        }

        private static LevelObjective ParseObjective(object raw)
        {
            if (!(raw is Dictionary<string, object> obj))
                throw new Reject(ContentMetadataReasons.FieldTypeInvalid, "objective");
            RequireKnownFields(obj, ObjectiveFields);
            if (!obj.TryGetValue("kind", out var k)) throw new Reject(ContentMetadataReasons.MissingField, "objective.kind");
            GoalKind kind;
            switch (k as string)
            {
                case "CollectPiece": kind = GoalKind.CollectPiece; break;
                case "ClearObstacle": kind = GoalKind.ClearObstacle; break;
                case "RescueDrop": kind = GoalKind.RescueDrop; break;
                default: throw new Reject(ContentMetadataReasons.ValueOutOfRange, "objective.kind");
            }
            return new LevelObjective(kind, RequireId(obj, "targetId"), RequireInt(obj, "count", 1, 9999));
        }

        private bool ResolvesWorld(string id, HashSet<string> batch) =>
            batch.Contains(id) || _knownWorldIds.Contains(id) ||
            _applied.TryGetValue(id, out var a) && a.Kind == ContentKind.World;

        private bool ResolvesLevel(string id, HashSet<string> batch) =>
            batch.Contains(id) || _knownLevelIds.Contains(id) ||
            _applied.TryGetValue(id, out var a) && a.Kind == ContentKind.Level;

        private static string OrderSlot(ContentMetadataItem item)
        {
            // Distinct prefixes: a world whose ID is literally "world" cannot collide with world slots.
            if (item.Kind == ContentKind.World) return "W|" + item.Order.ToString(CultureInfo.InvariantCulture);
            if (item.Kind == ContentKind.Level) return "L|" + item.WorldId + "|" + item.Order.ToString(CultureInfo.InvariantCulture);
            return null;
        }

        private ContentBatchResult BatchReject(string code, string detail, string game = null, string producer = null) =>
            new ContentBatchResult(false, code, detail, null, game, producer);

        // ---- field helpers -------------------------------------------------------

        private sealed class Reject : Exception
        {
            public string Code { get; }
            public Reject(string code, string detail) : base(detail) { Code = code; }
        }

        private static Reject Missing(Dictionary<string, object> obj, string key) =>
            obj.ContainsKey(key)
                ? new Reject(ContentMetadataReasons.FieldTypeInvalid, key)
                : new Reject(ContentMetadataReasons.MissingField, key);

        private static void RequireKnownFields(Dictionary<string, object> payload, string[] known)
        {
            foreach (var key in payload.Keys.OrderBy(x => x, StringComparer.Ordinal))
                if (Array.IndexOf(known, key) < 0 && !key.StartsWith("x-", StringComparison.Ordinal))
                    throw new Reject(ContentMetadataReasons.UnknownPayloadField, key);
            foreach (var key in known)
                if (!payload.ContainsKey(key)) throw new Reject(ContentMetadataReasons.MissingField, key);
        }

        private static int RequireInt(Dictionary<string, object> obj, string key, int min, int max)
        {
            if (!obj.TryGetValue(key, out var v)) throw new Reject(ContentMetadataReasons.MissingField, key);
            if (!(v is long l)) throw new Reject(ContentMetadataReasons.FieldTypeInvalid, key);
            if (l < min || l > max) throw new Reject(ContentMetadataReasons.ValueOutOfRange, key);
            return (int)l;
        }

        private static string RequireId(Dictionary<string, object> obj, string key)
        {
            if (!obj.TryGetValue(key, out var v)) throw new Reject(ContentMetadataReasons.MissingField, key);
            if (!(v is string s)) throw new Reject(ContentMetadataReasons.FieldTypeInvalid, key);
            if (!IsValidId(s)) throw new Reject(ContentMetadataReasons.IdInvalid, key);
            return s;
        }

        private static List<object> RequireList(Dictionary<string, object> obj, string key, int min, int max)
        {
            if (!obj.TryGetValue(key, out var v)) throw new Reject(ContentMetadataReasons.MissingField, key);
            if (!(v is List<object> list)) throw new Reject(ContentMetadataReasons.FieldTypeInvalid, key);
            if (list.Count < min || list.Count > max) throw new Reject(ContentMetadataReasons.ValueOutOfRange, key);
            return list;
        }

        private static IReadOnlyList<string> RequireIdList(Dictionary<string, object> obj, string key, int min, int max)
        {
            var list = RequireList(obj, key, min, max);
            var ids = new List<string>(list.Count);
            foreach (var v in list)
            {
                if (!(v is string s)) throw new Reject(ContentMetadataReasons.FieldTypeInvalid, key);
                if (!IsValidId(s)) throw new Reject(ContentMetadataReasons.IdInvalid, key);
                ids.Add(s);
            }
            if (ids.Distinct(StringComparer.Ordinal).Count() != ids.Count)
                throw new Reject(ContentMetadataReasons.DuplicateContentId, key);
            return ids.AsReadOnly();
        }

        private static DateTime RequireUtc(Dictionary<string, object> obj, string key)
        {
            if (!obj.TryGetValue(key, out var v)) throw new Reject(ContentMetadataReasons.MissingField, key);
            if (!(v is string s) || !DateTime.TryParseExact(s, "yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture,
                    DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var dt))
                throw new Reject(ContentMetadataReasons.EventWindowMalformed, key);
            return dt;
        }

        private static void RejectForbiddenKeys(object node)
        {
            if (node is Dictionary<string, object> obj)
            {
                foreach (var kv in obj.OrderBy(x => x.Key, StringComparer.Ordinal))
                {
                    if (IsForbiddenKey(kv.Key)) throw new Reject(ContentMetadataReasons.ForbiddenMonetizationField, kv.Key);
                    RejectForbiddenKeys(kv.Value);
                }
            }
            else if (node is List<object> list)
            {
                foreach (var v in list) RejectForbiddenKeys(v);
            }
        }

        /// <summary>
        /// True when the key names a monetization or credential concept. The key is lowercased and
        /// split on '-', '_' and camelCase two ways (acronym kept whole: "SKUs" -> skus; acronym end
        /// split: "IAPId" -> iap, id). Each word and each adjacent word pair is matched exactly against
        /// <see cref="ForbiddenWords"/>, also with one trailing 's' removed ("SKUs", "productIds");
        /// the compact whole key is also searched for long unambiguous substrings ("storeproductid").
        /// Word-exact matching keeps "x-mediaPath" allowed. Known conservative rejections: any key
        /// containing the word "token" or "secret" (e.g. "tokenCount") is rejected (fail closed).
        /// </summary>
        public static bool IsForbiddenKey(string key)
        {
            if (string.IsNullOrEmpty(key)) return false;
            var compact = key.Replace("-", "").Replace("_", "").ToLowerInvariant();
            foreach (var sub in ForbiddenSubstrings)
                if (compact.IndexOf(sub, StringComparison.Ordinal) >= 0) return true;
            return HasForbiddenWord(SplitWords(key, false)) || HasForbiddenWord(SplitWords(key, true));
        }

        private static bool HasForbiddenWord(List<string> words)
        {
            for (var i = 0; i < words.Count; i++)
            {
                if (IsForbiddenWord(words[i])) return true;
                if (i + 1 < words.Count && IsForbiddenWord(words[i] + words[i + 1])) return true;
            }
            return false;
        }

        private static bool IsForbiddenWord(string w) =>
            ForbiddenWords.Contains(w) ||
            w.Length > 1 && w[w.Length - 1] == 's' && ForbiddenWords.Contains(w.Substring(0, w.Length - 1));

        private static List<string> SplitWords(string key, bool splitAcronymEnd)
        {
            var words = new List<string>();
            var current = new StringBuilder();
            for (var i = 0; i < key.Length; i++)
            {
                var c = key[i];
                var boundary = c == '-' || c == '_' ||
                    char.IsUpper(c) && i > 0 && (char.IsLower(key[i - 1]) ||
                        splitAcronymEnd && char.IsUpper(key[i - 1]) && i + 1 < key.Length && char.IsLower(key[i + 1]));
                if (boundary && current.Length > 0) { words.Add(current.ToString()); current.Clear(); }
                if (c != '-' && c != '_') current.Append(char.ToLowerInvariant(c));
            }
            if (current.Length > 0) words.Add(current.ToString());
            return words;
        }

        /// <summary>ASCII letter first, then letters/digits/'.'/'_'/'-', 3..80 chars.</summary>
        public static bool IsValidId(string s)
        {
            if (s == null || s.Length < 3 || s.Length > 80) return false;
            if (!(s[0] >= 'A' && s[0] <= 'Z' || s[0] >= 'a' && s[0] <= 'z')) return false;
            foreach (var c in s)
                if (!(c >= 'A' && c <= 'Z' || c >= 'a' && c <= 'z' || c >= '0' && c <= '9' || c == '.' || c == '_' || c == '-'))
                    return false;
            return true;
        }

        private static bool IsOpaqueProducerId(string s) =>
            s.Length >= 1 && s.Length <= 128 && s.All(c => c >= '!' && c <= '~');

        private static bool IsLowerHex64(string s) =>
            s.Length == 64 && s.All(c => c >= '0' && c <= '9' || c >= 'a' && c <= 'f');

        private static bool TryParseSchema(string s, out int major)
        {
            major = 0;
            var parts = s.Split('.');
            return parts.Length == 2 && IsCanonicalUInt(parts[1]) && IsCanonicalUInt(parts[0]) &&
                int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out major);
        }

        private static bool IsCanonicalUInt(string s) =>
            s.Length >= 1 && s.Length <= 4 && s.All(c => c >= '0' && c <= '9') && (s.Length == 1 || s[0] != '0');

        private static HashSet<string> KnownSet(IEnumerable<string> ids, string name)
        {
            var set = new HashSet<string>(StringComparer.Ordinal);
            foreach (var id in ids ?? Enumerable.Empty<string>())
                if (!IsValidId(id)) throw new ArgumentException("Invalid known content ID.", name);
                else set.Add(id);
            return set;
        }
    }

    /// <summary>
    /// Minimal strict JSON reader plus canonical writer for content metadata.
    /// Reader: objects, arrays, strings, integers (long), true/false/null; rejects
    /// fractions/exponents, duplicate keys, trailing data, unpaired surrogates, control
    /// characters, depth &gt; 32 and input &gt; 256 KiB of UTF-8. Object keys must be 1..64 chars of
    /// [A-Za-z0-9_-] so ordinal key order equals code-point order.
    /// Writer: sorted keys, no whitespace, ASCII-only (\uXXXX lowercase) escaping --
    /// byte-identical to Python json.dumps(v, sort_keys=True, separators=(",", ":")).
    /// </summary>
    public static class CanonicalJson
    {
        public const int MaxUtf8Bytes = 256 * 1024;
        public const int MaxDepth = 32;

        public static object Parse(string text)
        {
            if (text == null) throw new FormatException("null input");
            // Every char encodes to >= 1 UTF-8 byte, so the cheap length check is a valid early exit.
            if (text.Length > MaxUtf8Bytes || Encoding.UTF8.GetByteCount(text) > MaxUtf8Bytes)
                throw new FormatException("input exceeds " + MaxUtf8Bytes + " UTF-8 bytes");
            var pos = 0;
            var value = ReadValue(text, ref pos, 0);
            SkipWs(text, ref pos);
            if (pos != text.Length) throw new FormatException("trailing data at " + pos);
            return value;
        }

        public static string Serialize(object value)
        {
            var sb = new StringBuilder();
            Write(sb, value);
            return sb.ToString();
        }

        public static string Sha256Hex(object value)
        {
            using (var sha = SHA256.Create())
            {
                var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(Serialize(value)));
                var sb = new StringBuilder(64);
                foreach (var b in hash) sb.Append(b.ToString("x2", CultureInfo.InvariantCulture));
                return sb.ToString();
            }
        }

        private static void Write(StringBuilder sb, object value)
        {
            switch (value)
            {
                case null: sb.Append("null"); break;
                case bool b: sb.Append(b ? "true" : "false"); break;
                case long l: sb.Append(l.ToString(CultureInfo.InvariantCulture)); break;
                case int i: sb.Append(i.ToString(CultureInfo.InvariantCulture)); break;
                case string s: WriteString(sb, s); break;
                case IDictionary<string, object> obj:
                    sb.Append('{');
                    var first = true;
                    foreach (var key in obj.Keys.OrderBy(k => k, StringComparer.Ordinal))
                    {
                        if (!first) sb.Append(',');
                        first = false;
                        WriteString(sb, key);
                        sb.Append(':');
                        Write(sb, obj[key]);
                    }
                    sb.Append('}');
                    break;
                case IList<object> list:
                    sb.Append('[');
                    for (var i = 0; i < list.Count; i++)
                    {
                        if (i > 0) sb.Append(',');
                        Write(sb, list[i]);
                    }
                    sb.Append(']');
                    break;
                default: throw new FormatException("unsupported value type " + value.GetType().Name);
            }
        }

        private static void WriteString(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (var c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    default:
                        if (c >= ' ' && c <= '~') sb.Append(c);
                        else sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        break;
                }
            }
            sb.Append('"');
        }

        private static void SkipWs(string t, ref int p)
        {
            while (p < t.Length && (t[p] == ' ' || t[p] == '\t' || t[p] == '\n' || t[p] == '\r')) p++;
        }

        private static object ReadValue(string t, ref int p, int depth)
        {
            if (depth > MaxDepth) throw new FormatException("nesting too deep");
            SkipWs(t, ref p);
            if (p >= t.Length) throw new FormatException("unexpected end");
            var c = t[p];
            if (c == '{') return ReadObject(t, ref p, depth);
            if (c == '[') return ReadArray(t, ref p, depth);
            if (c == '"') return ReadString(t, ref p);
            if (c == '-' || c >= '0' && c <= '9') return ReadInteger(t, ref p);
            if (Literal(t, ref p, "true")) return true;
            if (Literal(t, ref p, "false")) return false;
            if (Literal(t, ref p, "null")) return null;
            throw new FormatException("unexpected character at " + p);
        }

        private static bool Literal(string t, ref int p, string word)
        {
            if (string.CompareOrdinal(t, p, word, 0, word.Length) != 0) return false;
            p += word.Length;
            return true;
        }

        private static Dictionary<string, object> ReadObject(string t, ref int p, int depth)
        {
            var obj = new Dictionary<string, object>(StringComparer.Ordinal);
            p++;
            SkipWs(t, ref p);
            if (p < t.Length && t[p] == '}') { p++; return obj; }
            while (true)
            {
                SkipWs(t, ref p);
                if (p >= t.Length || t[p] != '"') throw new FormatException("expected key at " + p);
                var key = ReadString(t, ref p);
                if (key.Length < 1 || key.Length > 64 || !key.All(ch =>
                        ch >= 'A' && ch <= 'Z' || ch >= 'a' && ch <= 'z' || ch >= '0' && ch <= '9' || ch == '_' || ch == '-'))
                    throw new FormatException("invalid key " + key);
                if (obj.ContainsKey(key)) throw new FormatException("duplicate key " + key);
                SkipWs(t, ref p);
                if (p >= t.Length || t[p] != ':') throw new FormatException("expected ':' at " + p);
                p++;
                obj[key] = ReadValue(t, ref p, depth + 1);
                SkipWs(t, ref p);
                if (p < t.Length && t[p] == ',') { p++; continue; }
                if (p < t.Length && t[p] == '}') { p++; return obj; }
                throw new FormatException("expected ',' or '}' at " + p);
            }
        }

        private static List<object> ReadArray(string t, ref int p, int depth)
        {
            var list = new List<object>();
            p++;
            SkipWs(t, ref p);
            if (p < t.Length && t[p] == ']') { p++; return list; }
            while (true)
            {
                list.Add(ReadValue(t, ref p, depth + 1));
                SkipWs(t, ref p);
                if (p < t.Length && t[p] == ',') { p++; continue; }
                if (p < t.Length && t[p] == ']') { p++; return list; }
                throw new FormatException("expected ',' or ']' at " + p);
            }
        }

        private static long ReadInteger(string t, ref int p)
        {
            var start = p;
            if (t[p] == '-') p++;
            if (p >= t.Length || t[p] < '0' || t[p] > '9') throw new FormatException("invalid number at " + start);
            if (t[p] == '0' && p + 1 < t.Length && t[p + 1] >= '0' && t[p + 1] <= '9')
                throw new FormatException("leading zero at " + start);
            while (p < t.Length && t[p] >= '0' && t[p] <= '9') p++;
            if (p < t.Length && (t[p] == '.' || t[p] == 'e' || t[p] == 'E'))
                throw new FormatException("non-integer number at " + start);
            if (!long.TryParse(t.Substring(start, p - start), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var v))
                throw new FormatException("integer out of range at " + start);
            return v;
        }

        private static string ReadString(string t, ref int p)
        {
            var sb = new StringBuilder();
            p++;
            while (true)
            {
                if (p >= t.Length) throw new FormatException("unterminated string");
                var c = t[p++];
                if (c == '"') break;
                if (c < ' ') throw new FormatException("control character in string");
                if (c != '\\') { sb.Append(c); continue; }
                if (p >= t.Length) throw new FormatException("unterminated escape");
                var e = t[p++];
                switch (e)
                {
                    case '"': sb.Append('"'); break;
                    case '\\': sb.Append('\\'); break;
                    case '/': sb.Append('/'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'u':
                        if (p + 4 > t.Length || !int.TryParse(t.Substring(p, 4), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var code))
                            throw new FormatException("invalid \\u escape");
                        sb.Append((char)code);
                        p += 4;
                        break;
                    default: throw new FormatException("invalid escape \\" + e);
                }
            }
            var s = sb.ToString();
            for (var i = 0; i < s.Length; i++)
            {
                if (char.IsHighSurrogate(s[i]) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1])) { i++; continue; }
                if (char.IsSurrogate(s[i])) throw new FormatException("unpaired surrogate");
            }
            return s;
        }
    }
}
