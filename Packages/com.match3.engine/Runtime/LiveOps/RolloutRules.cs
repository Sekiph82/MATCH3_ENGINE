using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;

namespace Match3.Engine.Runtime.LiveOps
{
    /// <summary>
    /// Semantic version MAJOR.MINOR.PATCH[-prerelease] with SemVer 2.0.0 precedence.
    /// Build metadata ("+...") and leading zeros are rejected (fail closed).
    /// </summary>
    public readonly struct SemVer : IComparable<SemVer>
    {
        public int Major { get; }
        public int Minor { get; }
        public int Patch { get; }
        /// <summary>Null for a release version.</summary>
        public string Prerelease { get; }

        private SemVer(int major, int minor, int patch, string prerelease)
        {
            Major = major;
            Minor = minor;
            Patch = patch;
            Prerelease = prerelease;
        }

        public static SemVer Parse(string text)
        {
            if (!TryParse(text, out var v)) throw new FormatException("invalid semantic version: " + text);
            return v;
        }

        public static bool TryParse(string text, out SemVer version)
        {
            version = default;
            if (string.IsNullOrEmpty(text)) return false;

            string core = text, pre = null;
            int dash = text.IndexOf('-');
            if (dash >= 0)
            {
                core = text.Substring(0, dash);
                pre = text.Substring(dash + 1);
                if (!ValidPrerelease(pre)) return false;
            }

            string[] parts = core.Split('.');
            if (parts.Length != 3) return false;
            if (!ParseNumeric(parts[0], out int major) || !ParseNumeric(parts[1], out int minor) || !ParseNumeric(parts[2], out int patch))
                return false;

            version = new SemVer(major, minor, patch, pre);
            return true;
        }

        public int CompareTo(SemVer other)
        {
            int c = Major.CompareTo(other.Major);
            if (c != 0) return c;
            c = Minor.CompareTo(other.Minor);
            if (c != 0) return c;
            c = Patch.CompareTo(other.Patch);
            if (c != 0) return c;
            if (Prerelease == null) return other.Prerelease == null ? 0 : 1;
            if (other.Prerelease == null) return -1;

            string[] a = Prerelease.Split('.'), b = other.Prerelease.Split('.');
            for (int i = 0; i < a.Length && i < b.Length; i++)
            {
                bool an = AllDigits(a[i]), bn = AllDigits(b[i]);
                if (an && bn)
                {
                    // No leading zeros, so longer digit string is the larger number (no overflow).
                    c = a[i].Length != b[i].Length ? a[i].Length.CompareTo(b[i].Length) : string.CompareOrdinal(a[i], b[i]);
                }
                else if (an) c = -1;
                else if (bn) c = 1;
                else c = string.CompareOrdinal(a[i], b[i]);
                if (c != 0) return Math.Sign(c);
            }
            return a.Length.CompareTo(b.Length);
        }

        public override string ToString() =>
            Major.ToString(CultureInfo.InvariantCulture) + "." + Minor.ToString(CultureInfo.InvariantCulture) + "." +
            Patch.ToString(CultureInfo.InvariantCulture) + (Prerelease == null ? "" : "-" + Prerelease);

        private static bool ParseNumeric(string s, out int value)
        {
            value = 0;
            if (!AllDigits(s) || (s.Length > 1 && s[0] == '0')) return false;
            return int.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out value);
        }

        private static bool ValidPrerelease(string pre)
        {
            if (pre.Length == 0) return false;
            foreach (string id in pre.Split('.'))
            {
                if (id.Length == 0) return false;
                foreach (char ch in id)
                    if (!((ch >= '0' && ch <= '9') || (ch >= 'A' && ch <= 'Z') || (ch >= 'a' && ch <= 'z') || ch == '-'))
                        return false;
                if (AllDigits(id) && id.Length > 1 && id[0] == '0') return false;
            }
            return true;
        }

        private static bool AllDigits(string s)
        {
            if (s.Length == 0) return false;
            foreach (char ch in s)
                if (ch < '0' || ch > '9') return false;
            return true;
        }
    }

    public enum Platform { iOS, Android, Editor, Other }

    public sealed class RolloutRule
    {
        public string Id { get; }
        public int Version { get; }
        /// <summary>Titles this rule applies to (ordinal, exact ids). Required and non-empty: no implicit all-titles rule.</summary>
        public IReadOnlyCollection<string> GameIds { get; }
        public SemVer? MinAppVersion { get; }
        public SemVer? MaxAppVersion { get; }
        public IReadOnlyCollection<Platform> Platforms { get; }
        /// <summary>Uppercase ISO-3166 alpha-2 codes. Empty means all regions.</summary>
        public IReadOnlyCollection<string> Regions { get; }
        public int RolloutPercent { get; }
        public string Salt { get; }
        /// <summary>Not configurable: child profiles are never part of a rollout cohort.</summary>
        public bool ExcludeChildProfiles => true;

        private readonly HashSet<string> _gameIds;
        private readonly HashSet<Platform> _platforms;
        private readonly HashSet<string> _regions;

        private RolloutRule(string id, int version, HashSet<string> gameIds, SemVer? min, SemVer? max, HashSet<Platform> platforms,
            HashSet<string> regions, int rolloutPercent, string salt)
        {
            Id = id;
            Version = version;
            _gameIds = gameIds;
            GameIds = new ReadOnlyCollection<string>(new List<string>(gameIds));
            MinAppVersion = min;
            MaxAppVersion = max;
            _platforms = platforms;
            _regions = regions;
            Platforms = new ReadOnlyCollection<Platform>(new List<Platform>(platforms));
            Regions = new ReadOnlyCollection<string>(new List<string>(regions));
            RolloutPercent = rolloutPercent;
            Salt = salt;
        }

        public static RolloutRule Create(string id, int version, IEnumerable<string> gameIds, string minAppVersion, string maxAppVersion,
            IEnumerable<Platform> platforms, IEnumerable<string> regions, int rolloutPercent, string salt)
        {
            if (string.IsNullOrEmpty(id)) throw new ArgumentException("id cannot be null or empty", nameof(id));
            if (version < 1) throw new ArgumentOutOfRangeException(nameof(version), "version must be at least 1");
            if (string.IsNullOrEmpty(salt)) throw new ArgumentException("salt cannot be null or empty", nameof(salt));
            if (rolloutPercent < 0 || rolloutPercent > 100)
                throw new ArgumentOutOfRangeException(nameof(rolloutPercent), "rolloutPercent must be 0..100");

            if (gameIds == null) throw new ArgumentException("gameIds cannot be null", nameof(gameIds));
            var gameSet = new HashSet<string>(StringComparer.Ordinal);
            foreach (var g in gameIds)
            {
                if (string.IsNullOrEmpty(g)) throw new ArgumentException("game id cannot be null or empty", nameof(gameIds));
                gameSet.Add(g);
            }
            if (gameSet.Count == 0) throw new ArgumentException("at least one game id is required", nameof(gameIds));

            SemVer? min = null, max = null;
            if (minAppVersion != null)
            {
                if (!SemVer.TryParse(minAppVersion, out var v)) throw new ArgumentException("invalid minAppVersion", nameof(minAppVersion));
                min = v;
            }
            if (maxAppVersion != null)
            {
                if (!SemVer.TryParse(maxAppVersion, out var v)) throw new ArgumentException("invalid maxAppVersion", nameof(maxAppVersion));
                max = v;
            }
            if (min.HasValue && max.HasValue && min.Value.CompareTo(max.Value) > 0)
                throw new ArgumentException("minAppVersion is greater than maxAppVersion");

            if (platforms == null) throw new ArgumentException("platforms cannot be null", nameof(platforms));
            var platformSet = new HashSet<Platform>();
            foreach (var p in platforms)
            {
                if (p != Platform.iOS && p != Platform.Android && p != Platform.Editor && p != Platform.Other)
                    throw new ArgumentOutOfRangeException(nameof(platforms), "unknown platform");
                platformSet.Add(p);
            }
            if (platformSet.Count == 0) throw new ArgumentException("at least one platform is required", nameof(platforms));

            if (regions == null) throw new ArgumentException("regions cannot be null", nameof(regions));
            var regionSet = new HashSet<string>(StringComparer.Ordinal);
            foreach (var r in regions)
            {
                if (!IsRegionCode(r)) throw new ArgumentException("region must be an uppercase ISO-3166 alpha-2 code: " + r, nameof(regions));
                regionSet.Add(r);
            }

            return new RolloutRule(id, version, gameSet, min, max, platformSet, regionSet, rolloutPercent, salt);
        }

        internal bool HasGame(string gameId) => _gameIds.Contains(gameId);
        internal bool HasPlatform(Platform p) => _platforms.Contains(p);
        internal bool AllowsRegion(string code) => _regions.Count == 0 || (code != null && _regions.Contains(code));

        // ponytail: shape check only (two uppercase ASCII letters), not membership in the ISO-3166 list.
        internal static bool IsRegionCode(string r) =>
            r != null && r.Length == 2 && r[0] >= 'A' && r[0] <= 'Z' && r[1] >= 'A' && r[1] <= 'Z';
    }

    /// <summary>The only attributes a rollout can see; nothing behavioral or sensitive is representable.</summary>
    public sealed class RolloutContext
    {
        public string GameId { get; }
        public string PlayerId { get; }
        public string AppVersion { get; }
        public Platform Platform { get; }
        public string RegionCode { get; }
        public bool IsChildProfile { get; }

        public RolloutContext(string gameId, string playerId, string appVersion, Platform platform, string regionCode, bool isChildProfile)
        {
            if (string.IsNullOrEmpty(gameId)) throw new ArgumentException("gameId cannot be null or empty", nameof(gameId));
            if (string.IsNullOrEmpty(playerId)) throw new ArgumentException("playerId cannot be null or empty", nameof(playerId));
            GameId = gameId;
            PlayerId = playerId;
            AppVersion = appVersion;
            Platform = platform;
            RegionCode = regionCode;
            IsChildProfile = isChildProfile;
        }
    }

    public readonly struct RolloutDecision
    {
        public bool Included { get; }
        public string Reason { get; }

        public RolloutDecision(bool included, string reason)
        {
            Included = included;
            Reason = reason;
        }
    }

    public static class RolloutEvaluator
    {
        public static RolloutDecision Evaluate(RolloutRule rule, RolloutContext ctx)
        {
            if (rule == null) throw new ArgumentNullException(nameof(rule));
            if (ctx == null) throw new ArgumentNullException(nameof(ctx));

            // Fail closed: only catalog game IDs (Ordinal) are eligible (ctx rejects null/empty).
            if (!GameProfileCatalog.IsKnownId(ctx.GameId)) return new RolloutDecision(false, "UnknownGame");
            if (ctx.IsChildProfile) return new RolloutDecision(false, "ChildProfileExcluded");
            if (!rule.HasGame(ctx.GameId)) return new RolloutDecision(false, "GameNotInRule");
            if (!SemVer.TryParse(ctx.AppVersion, out var app)) return new RolloutDecision(false, "InvalidAppVersion");
            if ((rule.MinAppVersion.HasValue && app.CompareTo(rule.MinAppVersion.Value) < 0) ||
                (rule.MaxAppVersion.HasValue && app.CompareTo(rule.MaxAppVersion.Value) > 0))
                return new RolloutDecision(false, "AppVersionOutOfRange");
            if (!rule.HasPlatform(ctx.Platform)) return new RolloutDecision(false, "PlatformExcluded");
            if (!rule.AllowsRegion(ctx.RegionCode)) return new RolloutDecision(false, "RegionExcluded");

            // Bucket depends only on rule Id + Salt + player, so raising RolloutPercent only adds players.
            int bucket = StableBucketing.Bucket(rule.Id, rule.Salt, ctx.PlayerId);
            if (bucket >= rule.RolloutPercent * (StableBucketing.DefaultBucketCount / 100))
                return new RolloutDecision(false, "OutsideRolloutPercent");
            return new RolloutDecision(true, "Included");
        }
    }

    public readonly struct RolloutAuditEntry
    {
        public string RuleId { get; }
        public int Version { get; }
        public string ChangedBy { get; }
        public DateTime UtcTimestamp { get; }
        public string Summary { get; }

        internal RolloutAuditEntry(string ruleId, int version, string changedBy, DateTime utcTimestamp, string summary)
        {
            RuleId = ruleId;
            Version = version;
            ChangedBy = changedBy;
            UtcTimestamp = utcTimestamp;
            Summary = summary;
        }
    }

    /// <summary>Append-only audit trail. Versions per RuleId must strictly increase; no update or delete.</summary>
    public sealed class RolloutAuditLog
    {
        private readonly List<RolloutAuditEntry> _entries = new List<RolloutAuditEntry>();
        private readonly Dictionary<string, int> _lastVersion = new Dictionary<string, int>(StringComparer.Ordinal);

        public IReadOnlyList<RolloutAuditEntry> Entries { get; }

        public RolloutAuditLog()
        {
            Entries = new ReadOnlyCollection<RolloutAuditEntry>(_entries);
        }

        public void Append(string ruleId, int version, string changedBy, DateTime utcTimestamp, string summary)
        {
            if (string.IsNullOrEmpty(ruleId)) throw new ArgumentException("ruleId cannot be null or empty", nameof(ruleId));
            if (version < 1) throw new ArgumentOutOfRangeException(nameof(version), "version must be at least 1");
            if (string.IsNullOrEmpty(changedBy)) throw new ArgumentException("changedBy cannot be null or empty", nameof(changedBy));
            if (utcTimestamp.Kind != DateTimeKind.Utc) throw new ArgumentException("timestamp must be UTC", nameof(utcTimestamp));
            if (string.IsNullOrEmpty(summary)) throw new ArgumentException("summary cannot be null or empty", nameof(summary));
            if (_lastVersion.TryGetValue(ruleId, out int last) && version <= last)
                throw new ArgumentException("version must be greater than last recorded version " + last + " for rule " + ruleId, nameof(version));

            _entries.Add(new RolloutAuditEntry(ruleId, version, changedBy, utcTimestamp, summary));
            _lastVersion[ruleId] = version;
        }
    }
}
