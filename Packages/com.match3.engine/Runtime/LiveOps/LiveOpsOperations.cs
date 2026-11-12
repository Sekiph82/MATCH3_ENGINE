using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Match3.Engine.Runtime.LiveOps
{
    // ENG-M20-009 LiveOps operations and approvals (engine data model; no backend, no network, no real keys).
    //
    // Config bundle (canonical JSON, LiveOps.CanonicalJson; digest = SHA-256 of the canonical serialization):
    //   { "format": "m3-config-bundle/1", "gameId": <catalog id>, "version": <int >= 1, monotonic per game>,
    //     "schemaVersion": <int>, optional sections (objects): "remoteConfig", "featureFlags", "experiments",
    //     "rollouts", "calendars", "economy", "offers" }
    //   remoteConfig keys are RemoteConfigKey names with string values, each checked by RemoteConfigVersionGate as a
    //   signed record with last-known-good fallback (schema floor included). featureFlags keys are LiveOpsFeature names
    //   with bool values; an enabled flag must pass FeatureFlagGate (e.g. Wonder_Pop tournament is rejected). Other
    //   sections are opaque here and validated by their own services. Unknown top-level fields reject.
    //
    // Signed release record ("signature block", canonical JSON; the signed bytes are the UTF-8 canonical record
    // without "signature"):
    //   { "format": "m3-config-release/1", "alg": "ES256", "keyId", "kind": "release"|"rollback", "gameId",
    //     "environment": "dev"|"staging"|"prod", "sequence" (strictly increasing per game+environment),
    //     "bundleVersion", "bundleSha256", "rollbackFromVersion" (0 for a release), "author", "approvers": [...],
    //     "economyChange": bool, "issuedTicks" (server UTC ticks), "signature": base64(r||s) }
    //   ES256 = ECDSA P-256 + SHA-256 (EcdsaP256, WebCrypto-compatible), verified against a pinned key set
    //   (key id -> SEC1 public key) with rotation (several active ids) and revocation (revoked ids always reject).
    //
    // Promotion: Dev -> Staging -> Prod. A bundle reaches Staging/Prod only if the same digest is live in the previous
    // environment. Prod needs approvals: author != approvers, approvers distinct; one approver, or two when the change
    // set (leaf diff against the live Prod bundle, or every leaf for a first release) touches an economy path
    // (section "economy"/"offers", or a path word in EconomyWords: coin, reward, price, offer, cap, ...). Ids are
    // opaque [A-Za-z0-9._-]{1,64}: an e-mail address cannot pass, so no PII lands in the signed block or log.
    // Rollback: a signed "rollback" record restores a version previously released in that environment; it needs one
    // approver in Prod (it restores already-approved content) and is bound to the version it replaces
    // (rollbackFromVersion), so an old rollback record cannot be replayed later.
    //
    // Client (ConfigBundleVerifier.TryAccept): fail closed in this order -- trusted server anchor, record syntax,
    // algorithm, key (unknown/revoked), signature, game, environment, issue time (<= anchor + MaxClockSkew), bundle
    // syntax/policy, digest, version binding, sequence (replay), version monotonicity (a lower version only through a
    // rollback record from exactly the current version), approvals (Prod). Accepted state = (sequence, bundle version,
    // highest bundle version ever accepted, digest); persist it with ToCanonical (it is the rollback-attack memory).
    //
    // Release log: append-only, hash-chained (entryHash = SHA-256 over the canonical entry incl. prevHash; genesis
    // prevHash = 64 zeros), server-anchored non-decreasing UTC ticks. Import re-verifies the chain; truncation is only
    // detectable against an externally anchored HeadHash (expectedHeadHash).
    //
    // NOT here (owner/backend scope): production key custody and the real signing service, approver authentication,
    // a persistent backend promotion service, Cloudflare deployment, dashboards. Production public keys plug in as
    // ConfigTrustedKeys entries pinned per game build; private keys never enter this repository.
    // ponytail: in-memory pipeline/log model, single writer; the backend service persists and serializes it.

    public enum ConfigEnvironment { Dev = 0, Staging = 1, Prod = 2 }

    public static class ConfigReleaseReasons
    {
        public const string TrustedTimeAnchorRequired = "TrustedTimeAnchorRequired";
        public const string ClientStateRequired = "ClientStateRequired";
        public const string NoTrustedKeys = "NoTrustedKeys";
        public const string MalformedSignatureBlock = "MalformedSignatureBlock";
        public const string UnsupportedRecordFormat = "UnsupportedRecordFormat";
        public const string UnsupportedAlgorithm = "UnsupportedAlgorithm";
        public const string UnknownKey = "UnknownKey";
        public const string RevokedKey = "RevokedKey";
        public const string BadSignature = "BadSignature";
        public const string WrongGame = "WrongGame";
        public const string WrongEnvironment = "WrongEnvironment";
        public const string IssuedInFuture = "IssuedInFuture";
        public const string MalformedBundle = "MalformedBundle";
        public const string UnsupportedBundleFormat = "UnsupportedBundleFormat";
        public const string UnknownBundleField = "UnknownBundleField";
        public const string UnknownGame = "UnknownGame";
        public const string InvalidVersion = "InvalidVersion";
        public const string SchemaVersionTooOld = "SchemaVersionTooOld";
        public const string UnknownConfigKey = "UnknownConfigKey";
        public const string DigestMismatch = "DigestMismatch";
        public const string VersionMismatch = "VersionMismatch";
        public const string StaleSequence = "StaleSequence";
        public const string VersionNotMonotonic = "VersionNotMonotonic";
        public const string RollbackBaseMismatch = "RollbackBaseMismatch";
        public const string RollbackNotOlder = "RollbackNotOlder";
        public const string InvalidApproverId = "InvalidApproverId";
        public const string AuthorCannotApprove = "AuthorCannotApprove";
        public const string DuplicateApprover = "DuplicateApprover";
        public const string InsufficientApprovals = "InsufficientApprovals";
        public const string EconomyChangeUndeclared = "EconomyChangeUndeclared";
        public const string NotInPreviousEnvironment = "NotInPreviousEnvironment";
        public const string UnknownRollbackTarget = "UnknownRollbackTarget";
        public const string NothingToRollBack = "NothingToRollBack";
        public const string ClockRollbackDetected = "ClockRollbackDetected";
        public const string MalformedLog = "MalformedLog";
        public const string LogIntegrityMismatch = "LogIntegrityMismatch";
        public const string LogChainBroken = "LogChainBroken";
        public const string LogHeadMismatch = "LogHeadMismatch";
    }

    public static class ConfigIds
    {
        /// <summary>Opaque id: 1..64 of [A-Za-z0-9._-] (no '@', no spaces: an e-mail address is never valid).</summary>
        public static bool IsValid(string id) =>
            !string.IsNullOrEmpty(id) && id.Length <= 64 && id.All(c =>
                c >= 'a' && c <= 'z' || c >= 'A' && c <= 'Z' || c >= '0' && c <= '9' || c == '.' || c == '_' || c == '-');

        internal static string Name(ConfigEnvironment e) =>
            e == ConfigEnvironment.Dev ? "dev" : e == ConfigEnvironment.Staging ? "staging" : e == ConfigEnvironment.Prod ? "prod" : null;

        internal static bool TryParseEnvironment(string s, out ConfigEnvironment e)
        {
            foreach (ConfigEnvironment v in Enum.GetValues(typeof(ConfigEnvironment)))
                if (Name(v) == s) { e = v; return true; }
            e = default(ConfigEnvironment);
            return false;
        }

        /// <summary>Approver rule shared by pipeline and client. Null = ok.</summary>
        internal static string CheckApprovals(string author, IReadOnlyList<string> approvers, int required)
        {
            if (!IsValid(author) || approvers == null || approvers.Any(a => !IsValid(a))) return ConfigReleaseReasons.InvalidApproverId;
            if (approvers.Contains(author, StringComparer.Ordinal)) return ConfigReleaseReasons.AuthorCannotApprove;
            if (approvers.Distinct(StringComparer.Ordinal).Count() != approvers.Count) return ConfigReleaseReasons.DuplicateApprover;
            return approvers.Count < required ? ConfigReleaseReasons.InsufficientApprovals : null;
        }
    }

    public sealed class ConfigBundle
    {
        public const string Format = "m3-config-bundle/1";
        public static IReadOnlyList<string> Sections { get; } = Array.AsReadOnly(new[]
            { "calendars", "economy", "experiments", "featureFlags", "offers", "remoteConfig", "rollouts" });
        /// <summary>Path words that make a change economy-affecting (two Prod approvers). Fail closed: broad on purpose.</summary>
        public static IReadOnlyList<string> EconomyWords { get; } = Array.AsReadOnly(new[]
        {
            "amount", "booster", "bundle", "cap", "coin", "cost", "currency", "gem", "grant", "offer", "payout",
            "price", "pricing", "prize", "reward", "wallet"
        });
        private static readonly string[] Header = { "format", "gameId", "schemaVersion", "version" };

        public string GameId { get; }
        public long Version { get; }
        public long SchemaVersion { get; }
        public string Sha256 { get; }
        public string CanonicalText { get; }
        private readonly Dictionary<string, object> _doc;

        private ConfigBundle(Dictionary<string, object> doc)
        {
            _doc = doc;
            GameId = (string)doc["gameId"];
            Version = (long)doc["version"];
            SchemaVersion = (long)doc["schemaVersion"];
            CanonicalText = CanonicalJson.Serialize(doc);
            Sha256 = CanonicalJson.Sha256Hex(doc);
        }

        /// <summary>Value of remoteConfig[key] or null.</summary>
        public string RemoteConfig(RemoteConfigKey key) =>
            _doc.TryGetValue("remoteConfig", out var s) && ((Dictionary<string, object>)s).TryGetValue(key.ToString(), out var v)
                ? (string)v : null;

        public static bool TryParse(string text, out ConfigBundle bundle, out string reason)
        {
            bundle = null;
            Dictionary<string, object> doc;
            try { doc = CanonicalJson.Parse(text) as Dictionary<string, object>; }
            catch (FormatException) { doc = null; }
            reason = doc == null ? ConfigReleaseReasons.MalformedBundle : Validate(doc);
            if (reason != null) return false;
            bundle = new ConfigBundle(doc);
            return true;
        }

        private static string Validate(Dictionary<string, object> doc)
        {
            if (doc.Keys.Any(k => !Header.Contains(k) && !Sections.Contains(k))) return ConfigReleaseReasons.UnknownBundleField;
            if (!doc.TryGetValue("format", out var f) || !(f is string)) return ConfigReleaseReasons.MalformedBundle;
            if ((string)f != Format) return ConfigReleaseReasons.UnsupportedBundleFormat;
            if (!(doc.TryGetValue("gameId", out var g) && g is string gameId)) return ConfigReleaseReasons.MalformedBundle;
            if (!GameProfileCatalog.IsKnownId(gameId)) return ConfigReleaseReasons.UnknownGame;
            if (!(doc.TryGetValue("version", out var v) && v is long version) || version < 1) return ConfigReleaseReasons.InvalidVersion;
            if (!(doc.TryGetValue("schemaVersion", out var sv) && sv is long schema) || schema < 0 || schema > int.MaxValue)
                return ConfigReleaseReasons.MalformedBundle;
            if (schema < RemoteConfigPolicy.Default.MinSchemaVersion) return ConfigReleaseReasons.SchemaVersionTooOld;
            foreach (var s in Sections)
                if (doc.TryGetValue(s, out var section) && !(section is Dictionary<string, object>)) return ConfigReleaseReasons.MalformedBundle;

            if (doc.TryGetValue("remoteConfig", out var rc))
                foreach (var kv in (Dictionary<string, object>)rc)
                {
                    if (!Enum.TryParse(kv.Key, false, out RemoteConfigKey key) || key.ToString() != kv.Key)
                        return ConfigReleaseReasons.UnknownConfigKey;
                    if (!(kv.Value is string value)) return ConfigReleaseReasons.MalformedBundle;
                    // The bundle is signed and the client keeps last-known-good as the fallback.
                    var record = RemoteConfigRecord.Create(gameId, key, value, (int)schema, true, true);
                    if (!RemoteConfigVersionGate.CanApplyConfig(gameId, record, RemoteConfigPolicy.Default, out var why)) return why;
                }
            if (doc.TryGetValue("featureFlags", out var ff))
                foreach (var kv in (Dictionary<string, object>)ff)
                {
                    if (!Enum.TryParse(kv.Key, false, out LiveOpsFeature feature) || feature.ToString() != kv.Key)
                        return ConfigReleaseReasons.UnknownConfigKey;
                    if (!(kv.Value is bool on)) return ConfigReleaseReasons.MalformedBundle;
                    if (on && !FeatureFlagGate.CanEnableFlag(gameId, FeatureFlagRecord.Create(gameId, feature, 1, true, true),
                            FeatureFlagPolicy.Default, out var why)) return why;
                }
            return null;
        }

        /// <summary>Leaf paths ("section/key/...") whose value differs between the two bundles (from = null: all leaves).</summary>
        public static IReadOnlyList<string> ChangedPaths(ConfigBundle from, ConfigBundle to)
        {
            if (to == null) throw new ArgumentNullException(nameof(to));
            var a = Leaves(from);
            var b = Leaves(to);
            return a.Keys.Union(b.Keys).Where(k => !a.TryGetValue(k, out var x) || !b.TryGetValue(k, out var y) || x != y)
                .OrderBy(k => k, StringComparer.Ordinal).ToList();
        }

        public static bool IsEconomyChange(ConfigBundle from, ConfigBundle to) => ChangedPaths(from, to).Any(IsEconomyPath);

        public static bool IsEconomyPath(string path)
        {
            var parts = (path ?? string.Empty).Split('/');
            if (parts[0] == "economy" || parts[0] == "offers") return true;
            return parts.SelectMany(Words).Any(w => EconomyWords.Contains(w));
        }

        private static Dictionary<string, string> Leaves(ConfigBundle b)
        {
            var leaves = new Dictionary<string, string>(StringComparer.Ordinal);
            if (b == null) return leaves;
            foreach (var s in Sections)
                if (b._doc.TryGetValue(s, out var section)) Flatten(s, section, leaves);
            return leaves;
        }

        private static void Flatten(string path, object value, Dictionary<string, string> into)
        {
            if (value is Dictionary<string, object> obj && obj.Count > 0)
                foreach (var kv in obj) Flatten(path + "/" + kv.Key, kv.Value, into);
            else into[path] = CanonicalJson.Serialize(value);
        }

        /// <summary>Lower-case words of a key: split on '-', '_', '.', digits and camelCase; plural 's' dropped.</summary>
        private static IEnumerable<string> Words(string key)
        {
            var sb = new StringBuilder();
            for (var i = 0; i <= key.Length; i++)
            {
                var c = i < key.Length ? key[i] : '_';
                var boundary = !char.IsLetter(c) ||
                               char.IsUpper(c) && sb.Length > 0 && (char.IsLower(key[i - 1]) || i + 1 < key.Length && char.IsLower(key[i + 1]));
                if (boundary && sb.Length > 0)
                {
                    var w = sb.ToString().ToLowerInvariant();
                    yield return w.Length > 3 && w.EndsWith("s", StringComparison.Ordinal) ? w.Substring(0, w.Length - 1) : w;
                    sb.Clear();
                }
                if (char.IsLetter(c)) sb.Append(c);
            }
        }
    }

    public sealed class ConfigReleaseRecord
    {
        public const string Format = "m3-config-release/1";
        public const string Algorithm = "ES256";
        public const string KindRelease = "release";
        public const string KindRollback = "rollback";
        private static readonly string[] Fields =
        {
            "alg", "approvers", "author", "bundleSha256", "bundleVersion", "economyChange", "environment", "format",
            "gameId", "issuedTicks", "keyId", "kind", "rollbackFromVersion", "sequence", "signature"
        };

        public string KeyId { get; }
        public string Kind { get; }
        public string GameId { get; }
        public ConfigEnvironment Environment { get; }
        public long Sequence { get; }
        public long BundleVersion { get; }
        public string BundleSha256 { get; }
        public long RollbackFromVersion { get; }
        public string Author { get; }
        public IReadOnlyList<string> Approvers { get; }
        public bool EconomyChange { get; }
        public DateTime IssuedUtc { get; }
        public bool IsRollback => Kind == KindRollback;
        internal byte[] Signature { get; }
        internal string Alg { get; }

        internal ConfigReleaseRecord(string keyId, string kind, string gameId, ConfigEnvironment environment, long sequence,
            long bundleVersion, string bundleSha256, long rollbackFromVersion, string author, IReadOnlyList<string> approvers,
            bool economyChange, DateTime issuedUtc, byte[] signature, string alg = Algorithm)
        {
            KeyId = keyId;
            Kind = kind;
            GameId = gameId;
            Environment = environment;
            Sequence = sequence;
            BundleVersion = bundleVersion;
            BundleSha256 = bundleSha256;
            RollbackFromVersion = rollbackFromVersion;
            Author = author;
            Approvers = approvers.ToList().AsReadOnly();
            EconomyChange = economyChange;
            IssuedUtc = issuedUtc;
            Signature = signature;
            Alg = alg;
        }

        /// <summary>The exact bytes covered by the signature: UTF-8 canonical JSON of every field except "signature".</summary>
        public byte[] SigningPayload() => Encoding.UTF8.GetBytes(CanonicalJson.Serialize(Body()));

        public string Sha256 => CanonicalJson.Sha256Hex(Json());

        public string ToCanonical() => CanonicalJson.Serialize(Json());

        private Dictionary<string, object> Json()
        {
            var d = Body();
            d["signature"] = Signature == null ? string.Empty : Convert.ToBase64String(Signature);
            return d;
        }

        private Dictionary<string, object> Body() => new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["format"] = Format,
            ["alg"] = Alg,
            ["keyId"] = KeyId,
            ["kind"] = Kind,
            ["gameId"] = GameId,
            ["environment"] = ConfigIds.Name(Environment),
            ["sequence"] = Sequence,
            ["bundleVersion"] = BundleVersion,
            ["bundleSha256"] = BundleSha256,
            ["rollbackFromVersion"] = RollbackFromVersion,
            ["author"] = Author,
            ["approvers"] = Approvers.Cast<object>().ToList(),
            ["economyChange"] = EconomyChange,
            ["issuedTicks"] = IssuedUtc.Ticks
        };

        /// <summary>Strict parse: every field present with its type, nothing extra, re-serializes byte-identically.</summary>
        public static bool TryParse(string text, out ConfigReleaseRecord record, out string reason)
        {
            record = null;
            try
            {
                if (!(CanonicalJson.Parse(text) is Dictionary<string, object> d) || d.Count != Fields.Length ||
                    Fields.Any(f => !d.ContainsKey(f)))
                {
                    reason = ConfigReleaseReasons.MalformedSignatureBlock;
                    return false;
                }
                if (!Equals(d["format"], Format)) { reason = ConfigReleaseReasons.UnsupportedRecordFormat; return false; }
                if (!Equals(d["alg"], Algorithm)) { reason = ConfigReleaseReasons.UnsupportedAlgorithm; return false; }
                var kind = (string)d["kind"];
                var sequence = (long)d["sequence"];
                var bundleVersion = (long)d["bundleVersion"];
                var from = (long)d["rollbackFromVersion"];
                var ticks = (long)d["issuedTicks"];
                var digest = (string)d["bundleSha256"];
                if (!ConfigIds.TryParseEnvironment((string)d["environment"], out var env) ||
                    kind != KindRelease && kind != KindRollback || sequence < 1 || bundleVersion < 1 ||
                    (kind == KindRelease ? from != 0 : from <= bundleVersion) ||
                    ticks < 0 || ticks > DateTime.MaxValue.Ticks || !ConfigIds.IsValid((string)d["keyId"]) ||
                    digest == null || digest.Length != 64 || digest.Any(c => !(c >= '0' && c <= '9' || c >= 'a' && c <= 'f')))
                {
                    reason = ConfigReleaseReasons.MalformedSignatureBlock;
                    return false;
                }
                var parsed = new ConfigReleaseRecord((string)d["keyId"], kind, (string)d["gameId"], env, sequence, bundleVersion,
                    digest, from, (string)d["author"], ((List<object>)d["approvers"]).Cast<string>().ToList(),
                    (bool)d["economyChange"], new DateTime(ticks, DateTimeKind.Utc), Convert.FromBase64String((string)d["signature"]));
                if (parsed.ToCanonical() != CanonicalJson.Serialize(d))
                {
                    reason = ConfigReleaseReasons.MalformedSignatureBlock;
                    return false;
                }
                record = parsed;
                reason = null;
                return true;
            }
            catch (Exception e) when (e is FormatException || e is InvalidCastException || e is NullReferenceException ||
                                      e is ArgumentException)
            {
                reason = ConfigReleaseReasons.MalformedSignatureBlock;
                return false;
            }
        }
    }

    /// <summary>Pinned verification keys of one game build: key id -> SEC1 P-256 public key, plus revoked ids.</summary>
    public sealed class ConfigTrustedKeys
    {
        private readonly Dictionary<string, byte[]> _keys = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        private readonly HashSet<string> _revoked = new HashSet<string>(StringComparer.Ordinal);

        public ConfigTrustedKeys(IEnumerable<KeyValuePair<string, byte[]>> activeKeys, IEnumerable<string> revokedKeyIds = null)
        {
            foreach (var kv in activeKeys ?? throw new ArgumentNullException(nameof(activeKeys)))
            {
                if (!ConfigIds.IsValid(kv.Key) || !EcdsaP256.IsValidPublicKey(kv.Value) || _keys.ContainsKey(kv.Key))
                    throw new ArgumentException("Key ids must be valid and unique; keys must be P-256 points.", nameof(activeKeys));
                _keys.Add(kv.Key, (byte[])kv.Value.Clone());
            }
            foreach (var id in revokedKeyIds ?? Enumerable.Empty<string>())
            {
                if (!ConfigIds.IsValid(id)) throw new ArgumentException("Invalid revoked key id.", nameof(revokedKeyIds));
                _revoked.Add(id);
            }
        }

        public int ActiveCount => _keys.Keys.Count(k => !_revoked.Contains(k));

        internal string Lookup(string keyId, out byte[] publicKey)
        {
            publicKey = null;
            if (_revoked.Contains(keyId)) return ConfigReleaseReasons.RevokedKey;
            if (!_keys.TryGetValue(keyId, out publicKey)) return ConfigReleaseReasons.UnknownKey;
            return null;
        }
    }

    /// <summary>Client-side memory of the accepted config (persist with ToCanonical; restore with TryParse).</summary>
    public sealed class ConfigClientState
    {
        public string GameId { get; }
        public ConfigEnvironment Environment { get; }
        public long Sequence { get; }
        public long BundleVersion { get; }
        public long HighestBundleVersion { get; }
        /// <summary>Digest of the accepted (last-known-good) bundle; null before the first accept.</summary>
        public string BundleSha256 { get; }

        public ConfigClientState(string gameId, ConfigEnvironment environment, long sequence = 0, long bundleVersion = 0,
            long highestBundleVersion = 0, string bundleSha256 = null)
        {
            if (!GameProfileCatalog.IsKnownId(gameId)) throw new ArgumentException("Unknown game.", nameof(gameId));
            if (ConfigIds.Name(environment) == null) throw new ArgumentOutOfRangeException(nameof(environment));
            if (sequence < 0 || bundleVersion < 0 || highestBundleVersion < bundleVersion ||
                (sequence == 0) != (bundleSha256 == null) || (bundleSha256 == null) != (bundleVersion == 0))
                throw new ArgumentException("Inconsistent client config state.");
            GameId = gameId;
            Environment = environment;
            Sequence = sequence;
            BundleVersion = bundleVersion;
            HighestBundleVersion = highestBundleVersion;
            BundleSha256 = bundleSha256;
        }

        public string ToCanonical() => CanonicalJson.Serialize(new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["gameId"] = GameId,
            ["environment"] = ConfigIds.Name(Environment),
            ["sequence"] = Sequence,
            ["bundleVersion"] = BundleVersion,
            ["highestBundleVersion"] = HighestBundleVersion,
            ["bundleSha256"] = BundleSha256
        });

        public static bool TryParse(string text, out ConfigClientState state)
        {
            state = null;
            try
            {
                var d = (Dictionary<string, object>)CanonicalJson.Parse(text);
                if (d.Count != 6 || !ConfigIds.TryParseEnvironment((string)d["environment"], out var env)) return false;
                var s = new ConfigClientState((string)d["gameId"], env, (long)d["sequence"], (long)d["bundleVersion"],
                    (long)d["highestBundleVersion"], (string)d["bundleSha256"]);
                if (s.ToCanonical() != text) return false;
                state = s;
                return true;
            }
            catch (Exception e) when (e is FormatException || e is ArgumentException || e is InvalidCastException ||
                                      e is KeyNotFoundException || e is NullReferenceException)
            {
                return false;
            }
        }
    }

    public sealed class ConfigAcceptResult
    {
        public bool Accepted { get; }
        public string Reason { get; }
        public ConfigBundle Bundle { get; }
        public ConfigReleaseRecord Record { get; }
        /// <summary>New state when accepted; otherwise the unchanged current state (last-known-good stays active).</summary>
        public ConfigClientState State { get; }

        internal ConfigAcceptResult(bool accepted, string reason, ConfigClientState state, ConfigBundle bundle = null,
            ConfigReleaseRecord record = null)
        {
            Accepted = accepted;
            Reason = reason ?? string.Empty;
            State = state;
            Bundle = bundle;
            Record = record;
        }
    }

    public static class ConfigBundleVerifier
    {
        /// <summary>Tolerated lead of a record's server issue time over the client's trusted anchor.</summary>
        public static readonly TimeSpan MaxClockSkew = TimeSpan.FromMinutes(5);

        /// <param name="currentVersion">The persisted client state (game, environment, sequence, versions, digest).</param>
        /// <param name="currentBundleText">Optional last-known-good bundle; when it matches the state digest an
        /// undeclared economy change in a Prod release is rejected (defense in depth over the pipeline rule).</param>
        public static ConfigAcceptResult TryAccept(string bundleText, string signatureBlock, ConfigTrustedKeys trustedKeys,
            ConfigClientState currentVersion, TrustedUtcAnchor anchor, string currentBundleText = null)
        {
            var state = currentVersion;
            ConfigAcceptResult Reject(string why) => new ConfigAcceptResult(false, why, state);

            if (state == null) return Reject(ConfigReleaseReasons.ClientStateRequired);
            if (trustedKeys == null || trustedKeys.ActiveCount == 0) return Reject(ConfigReleaseReasons.NoTrustedKeys);
            if (!anchor.IsServerAnchored || anchor.UtcNow.Kind != DateTimeKind.Utc)
                return Reject(ConfigReleaseReasons.TrustedTimeAnchorRequired);
            if (!ConfigReleaseRecord.TryParse(signatureBlock, out var record, out var why)) return Reject(why);
            why = trustedKeys.Lookup(record.KeyId, out var publicKey);
            if (why != null) return Reject(why);
            if (!EcdsaP256.Verify(publicKey, record.SigningPayload(), record.Signature)) return Reject(ConfigReleaseReasons.BadSignature);

            if (record.GameId != state.GameId) return Reject(ConfigReleaseReasons.WrongGame);
            if (record.Environment != state.Environment) return Reject(ConfigReleaseReasons.WrongEnvironment);
            if (record.IssuedUtc > anchor.UtcNow + MaxClockSkew) return Reject(ConfigReleaseReasons.IssuedInFuture);

            if (!ConfigBundle.TryParse(bundleText, out var bundle, out why)) return Reject(why);
            if (bundle.GameId != state.GameId) return Reject(ConfigReleaseReasons.WrongGame);
            if (bundle.Sha256 != record.BundleSha256) return Reject(ConfigReleaseReasons.DigestMismatch);
            if (bundle.Version != record.BundleVersion) return Reject(ConfigReleaseReasons.VersionMismatch);

            if (record.Sequence <= state.Sequence) return Reject(ConfigReleaseReasons.StaleSequence);
            if (record.IsRollback)
            {
                // Parse guarantees RollbackFromVersion > BundleVersion, so the target is older than the current version.
                if (state.BundleSha256 == null || record.RollbackFromVersion != state.BundleVersion)
                    return Reject(ConfigReleaseReasons.RollbackBaseMismatch);
            }
            else if (record.BundleVersion <= state.HighestBundleVersion) return Reject(ConfigReleaseReasons.VersionNotMonotonic);

            if (record.Environment == ConfigEnvironment.Prod)
            {
                why = ConfigIds.CheckApprovals(record.Author, record.Approvers, record.EconomyChange && !record.IsRollback ? 2 : 1);
                if (why != null) return Reject(why);
                if (!record.IsRollback && !record.EconomyChange && currentBundleText != null &&
                    ConfigBundle.TryParse(currentBundleText, out var current, out _) && current.Sha256 == state.BundleSha256 &&
                    ConfigBundle.IsEconomyChange(current, bundle))
                    return Reject(ConfigReleaseReasons.EconomyChangeUndeclared);
            }

            var next = new ConfigClientState(state.GameId, state.Environment, record.Sequence, record.BundleVersion,
                Math.Max(state.HighestBundleVersion, record.BundleVersion), bundle.Sha256);
            return new ConfigAcceptResult(true, null, next, bundle, record);
        }
    }

    public sealed class ConfigReleaseLogEntry
    {
        public const string GenesisHash = "0000000000000000000000000000000000000000000000000000000000000000";
        private static readonly string[] Fields =
        {
            "approvers", "author", "bundleSha256", "bundleVersion", "entryHash", "environment", "index", "kind",
            "prevHash", "recordSha256", "sequence", "utcTicks"
        };

        public long Index { get; }
        public string PrevHash { get; }
        public string Kind { get; }
        public ConfigEnvironment Environment { get; }
        public long Sequence { get; }
        public long BundleVersion { get; }
        public string BundleSha256 { get; }
        public string RecordSha256 { get; }
        public string Author { get; }
        public IReadOnlyList<string> Approvers { get; }
        public DateTime Utc { get; }
        public string EntryHash { get; }

        internal ConfigReleaseLogEntry(long index, string prevHash, ConfigReleaseRecord record, DateTime utc)
            : this(index, prevHash, record.Kind, record.Environment, record.Sequence, record.BundleVersion, record.BundleSha256,
                record.Sha256, record.Author, record.Approvers, utc, null)
        {
        }

        private ConfigReleaseLogEntry(long index, string prevHash, string kind, ConfigEnvironment environment, long sequence,
            long bundleVersion, string bundleSha256, string recordSha256, string author, IReadOnlyList<string> approvers,
            DateTime utc, string entryHash)
        {
            Index = index;
            PrevHash = prevHash;
            Kind = kind;
            Environment = environment;
            Sequence = sequence;
            BundleVersion = bundleVersion;
            BundleSha256 = bundleSha256;
            RecordSha256 = recordSha256;
            Author = author;
            Approvers = approvers.ToList().AsReadOnly();
            Utc = utc;
            EntryHash = entryHash ?? CanonicalJson.Sha256Hex(Body());
        }

        private Dictionary<string, object> Body() => new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["index"] = Index,
            ["prevHash"] = PrevHash,
            ["kind"] = Kind,
            ["environment"] = ConfigIds.Name(Environment),
            ["sequence"] = Sequence,
            ["bundleVersion"] = BundleVersion,
            ["bundleSha256"] = BundleSha256,
            ["recordSha256"] = RecordSha256,
            ["author"] = Author,
            ["approvers"] = Approvers.Cast<object>().ToList(),
            ["utcTicks"] = Utc.Ticks
        };

        internal string ToCanonical()
        {
            var d = Body();
            d["entryHash"] = EntryHash;
            return CanonicalJson.Serialize(d);
        }

        /// <summary>Parses one line with its claimed entryHash (not yet verified).</summary>
        internal static ConfigReleaseLogEntry Parse(string line)
        {
            var d = (Dictionary<string, object>)CanonicalJson.Parse(line);
            if (d.Count != Fields.Length || Fields.Any(f => !d.ContainsKey(f)) ||
                !ConfigIds.TryParseEnvironment((string)d["environment"], out var env))
                throw new FormatException("log entry fields");
            var entry = new ConfigReleaseLogEntry((long)d["index"], (string)d["prevHash"], (string)d["kind"], env,
                (long)d["sequence"], (long)d["bundleVersion"], (string)d["bundleSha256"], (string)d["recordSha256"],
                (string)d["author"], ((List<object>)d["approvers"]).Cast<string>().ToList(),
                new DateTime((long)d["utcTicks"], DateTimeKind.Utc), (string)d["entryHash"]);
            if (entry.ToCanonical() != line) throw new FormatException("non-canonical log entry");
            return entry;
        }

        internal bool HashMatches => EntryHash == CanonicalJson.Sha256Hex(Body());
    }

    /// <summary>Append-only, hash-chained promotion/rollback log.</summary>
    public sealed class ConfigReleaseLog
    {
        private readonly List<ConfigReleaseLogEntry> _entries = new List<ConfigReleaseLogEntry>();

        public IReadOnlyList<ConfigReleaseLogEntry> Entries => _entries.AsReadOnly();
        public string HeadHash => _entries.Count == 0 ? ConfigReleaseLogEntry.GenesisHash : _entries[_entries.Count - 1].EntryHash;

        internal string Append(ConfigReleaseRecord record, TrustedUtcAnchor anchor)
        {
            if (!anchor.IsServerAnchored || anchor.UtcNow.Kind != DateTimeKind.Utc) return ConfigReleaseReasons.TrustedTimeAnchorRequired;
            if (_entries.Count > 0 && anchor.UtcNow < _entries[_entries.Count - 1].Utc) return ConfigReleaseReasons.ClockRollbackDetected;
            _entries.Add(new ConfigReleaseLogEntry(_entries.Count, HeadHash, record, anchor.UtcNow));
            return null;
        }

        public string Export() => string.Join("\n", _entries.Select(e => e.ToCanonical()));

        /// <summary>Rebuilds and verifies the chain. expectedHeadHash (externally anchored) also detects truncation.</summary>
        public static bool TryImport(string text, out ConfigReleaseLog log, out string reason, string expectedHeadHash = null)
        {
            log = null;
            var result = new ConfigReleaseLog();
            try
            {
                foreach (var line in string.IsNullOrEmpty(text) ? new string[0] : text.Split('\n'))
                {
                    var e = ConfigReleaseLogEntry.Parse(line);
                    if (!e.HashMatches) { reason = ConfigReleaseReasons.LogIntegrityMismatch; return false; }
                    if (e.Index != result._entries.Count || e.PrevHash != result.HeadHash ||
                        result._entries.Count > 0 && e.Utc < result._entries[result._entries.Count - 1].Utc)
                    {
                        reason = ConfigReleaseReasons.LogChainBroken;
                        return false;
                    }
                    result._entries.Add(e);
                }
            }
            catch (Exception e) when (e is FormatException || e is InvalidCastException || e is KeyNotFoundException ||
                                      e is NullReferenceException || e is ArgumentException)
            {
                reason = ConfigReleaseReasons.MalformedLog;
                return false;
            }
            if (expectedHeadHash != null && expectedHeadHash != result.HeadHash)
            {
                reason = ConfigReleaseReasons.LogHeadMismatch;
                return false;
            }
            log = result;
            reason = null;
            return true;
        }
    }

    public sealed class ConfigOperationResult
    {
        public bool Succeeded => Reason.Length == 0;
        public string Reason { get; }
        public ConfigReleaseRecord Record { get; }
        /// <summary>Canonical signed record to ship next to the bundle (the client's signatureBlock).</summary>
        public string SignatureBlock { get; }
        public string BundleText { get; }

        internal ConfigOperationResult(string reason, ConfigReleaseRecord record = null, string bundleText = null)
        {
            Reason = reason ?? string.Empty;
            Record = record;
            SignatureBlock = record?.ToCanonical();
            BundleText = bundleText;
        }
    }

    /// <summary>Server-side promotion model: Dev -> Staging -> Prod, approvals, signing, release log, rollback.
    /// The signer is injected (backend key custody); this class never sees a private key.</summary>
    public sealed class ConfigReleasePipeline
    {
        private readonly string _keyId;
        private readonly Func<byte[], byte[]> _sign;
        private readonly Dictionary<ConfigEnvironment, List<ConfigBundle>> _released = new Dictionary<ConfigEnvironment, List<ConfigBundle>>();
        private readonly Dictionary<ConfigEnvironment, ConfigBundle> _live = new Dictionary<ConfigEnvironment, ConfigBundle>();
        private readonly Dictionary<ConfigEnvironment, long> _sequence = new Dictionary<ConfigEnvironment, long>();

        public string GameId { get; }
        public ConfigReleaseLog Log { get; } = new ConfigReleaseLog();

        /// <param name="sign">Returns an ES256 r||s signature over the given bytes with the key named keyId.</param>
        public ConfigReleasePipeline(string gameId, string keyId, Func<byte[], byte[]> sign)
        {
            if (!GameProfileCatalog.IsKnownId(gameId)) throw new ArgumentException("Unknown game.", nameof(gameId));
            if (!ConfigIds.IsValid(keyId)) throw new ArgumentException("Invalid key id.", nameof(keyId));
            GameId = gameId;
            _keyId = keyId;
            _sign = sign ?? throw new ArgumentNullException(nameof(sign));
            foreach (ConfigEnvironment e in Enum.GetValues(typeof(ConfigEnvironment)))
            {
                _released[e] = new List<ConfigBundle>();
                _sequence[e] = 0;
            }
        }

        /// <summary>Canonical text of the bundle live in the environment, or null.</summary>
        public string Live(ConfigEnvironment environment) => _live.TryGetValue(environment, out var b) ? b.CanonicalText : null;

        public ConfigOperationResult Promote(string bundleText, ConfigEnvironment target, string author,
            IReadOnlyList<string> approvers, TrustedUtcAnchor anchor)
        {
            approvers = approvers ?? new string[0];
            if (ConfigIds.Name(target) == null) throw new ArgumentOutOfRangeException(nameof(target));
            if (!anchor.IsServerAnchored || anchor.UtcNow.Kind != DateTimeKind.Utc)
                return new ConfigOperationResult(ConfigReleaseReasons.TrustedTimeAnchorRequired);
            if (!ConfigBundle.TryParse(bundleText, out var bundle, out var why)) return new ConfigOperationResult(why);
            if (bundle.GameId != GameId) return new ConfigOperationResult(ConfigReleaseReasons.WrongGame);
            if (target != ConfigEnvironment.Dev &&
                (!_live.TryGetValue(target - 1, out var previous) || previous.Sha256 != bundle.Sha256))
                return new ConfigOperationResult(ConfigReleaseReasons.NotInPreviousEnvironment);
            if (_released[target].Count > 0 && bundle.Version <= _released[target].Max(b => b.Version))
                return new ConfigOperationResult(ConfigReleaseReasons.VersionNotMonotonic);
            _live.TryGetValue(target, out var current);
            var economy = ConfigBundle.IsEconomyChange(current, bundle);
            why = ConfigIds.CheckApprovals(author, approvers, target == ConfigEnvironment.Prod ? (economy ? 2 : 1) : 0);
            if (why != null) return new ConfigOperationResult(why);
            return Release(ConfigReleaseRecord.KindRelease, target, bundle, 0, author, approvers, economy, anchor);
        }

        public ConfigOperationResult Rollback(ConfigEnvironment target, long toBundleVersion, string author,
            IReadOnlyList<string> approvers, TrustedUtcAnchor anchor)
        {
            approvers = approvers ?? new string[0];
            if (ConfigIds.Name(target) == null) throw new ArgumentOutOfRangeException(nameof(target));
            if (!anchor.IsServerAnchored || anchor.UtcNow.Kind != DateTimeKind.Utc)
                return new ConfigOperationResult(ConfigReleaseReasons.TrustedTimeAnchorRequired);
            if (!_live.TryGetValue(target, out var current)) return new ConfigOperationResult(ConfigReleaseReasons.NothingToRollBack);
            var restore = _released[target].FirstOrDefault(b => b.Version == toBundleVersion);
            if (restore == null) return new ConfigOperationResult(ConfigReleaseReasons.UnknownRollbackTarget);
            if (toBundleVersion >= current.Version) return new ConfigOperationResult(ConfigReleaseReasons.RollbackNotOlder);
            var why = ConfigIds.CheckApprovals(author, approvers, target == ConfigEnvironment.Prod ? 1 : 0);
            if (why != null) return new ConfigOperationResult(why);
            return Release(ConfigReleaseRecord.KindRollback, target, restore, current.Version, author, approvers,
                ConfigBundle.IsEconomyChange(current, restore), anchor);
        }

        private ConfigOperationResult Release(string kind, ConfigEnvironment target, ConfigBundle bundle, long from,
            string author, IReadOnlyList<string> approvers, bool economy, TrustedUtcAnchor anchor)
        {
            var unsigned = new ConfigReleaseRecord(_keyId, kind, GameId, target, _sequence[target] + 1, bundle.Version,
                bundle.Sha256, from, author, approvers, economy, anchor.UtcNow, null);
            var signature = _sign(unsigned.SigningPayload());
            if (signature == null || signature.Length != EcdsaP256.SignatureLength)
                throw new InvalidOperationException("Signer must return a 64-byte ES256 signature.");
            var record = new ConfigReleaseRecord(_keyId, kind, GameId, target, unsigned.Sequence, bundle.Version, bundle.Sha256,
                from, author, approvers, economy, anchor.UtcNow, signature);
            var why = Log.Append(record, anchor);
            if (why != null) return new ConfigOperationResult(why);
            _sequence[target] = record.Sequence;
            if (kind == ConfigReleaseRecord.KindRelease) _released[target].Add(bundle);
            _live[target] = bundle;
            return new ConfigOperationResult(null, record, bundle.CanonicalText);
        }
    }
}
