using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;

namespace Match3.Engine.Runtime.LiveOps
{
    /// <summary>
    /// Platform-independent bucketing: FNV-1a 64-bit over the strict UTF-8 bytes of
    /// "len:experimentId|len:salt|len:playerId" (len = UTF-16 char count, invariant culture),
    /// then the fmix64 finalizer, reduced modulo bucketCount. Never uses string.GetHashCode (randomized per process).
    /// </summary>
    public static class StableBucketing
    {
        public const int DefaultBucketCount = 10000;
        private const ulong FnvOffsetBasis = 14695981039346656037UL;
        private const ulong FnvPrime = 1099511628211UL;
        // Throws on lone surrogates instead of silently substituting U+FFFD.
        private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);

        public static int Bucket(string experimentId, string salt, string playerId, int bucketCount = DefaultBucketCount)
        {
            if (string.IsNullOrEmpty(experimentId)) throw new ArgumentException("experimentId cannot be null or empty", nameof(experimentId));
            if (string.IsNullOrEmpty(salt)) throw new ArgumentException("salt cannot be null or empty", nameof(salt));
            if (string.IsNullOrEmpty(playerId)) throw new ArgumentException("playerId cannot be null or empty", nameof(playerId));
            if (bucketCount < 1) throw new ArgumentOutOfRangeException(nameof(bucketCount), "bucketCount must be at least 1");

            string material = Prefixed(experimentId) + "|" + Prefixed(salt) + "|" + Prefixed(playerId);
            byte[] bytes;
            try { bytes = StrictUtf8.GetBytes(material); }
            catch (EncoderFallbackException e) { throw new ArgumentException("identifiers must be valid Unicode", e); }

            return (int)(Mix64(Fnv1a64(bytes)) % (ulong)bucketCount);
        }

        /// <summary>
        /// MurmurHash3 fmix64 finalizer. Required because raw FNV-1a mod 2^k depends only on the low k bits
        /// of each input byte (odd prime), e.g. mod 2 is plain byte parity, which correlates buckets across salts.
        /// </summary>
        public static ulong Mix64(ulong k)
        {
            unchecked
            {
                k ^= k >> 33;
                k *= 0xff51afd7ed558ccdUL;
                k ^= k >> 33;
                k *= 0xc4ceb9fe1a85ec53UL;
                k ^= k >> 33;
                return k;
            }
        }

        public static ulong Fnv1a64(byte[] bytes)
        {
            if (bytes == null) throw new ArgumentNullException(nameof(bytes));
            ulong hash = FnvOffsetBasis;
            for (int i = 0; i < bytes.Length; i++)
            {
                hash ^= bytes[i];
                hash = unchecked(hash * FnvPrime);
            }
            return hash;
        }

        // Length = UTF-16 code units (string.Length), not UTF-8 bytes or code points; backend bucketing must match exactly.
        private static string Prefixed(string s) => s.Length.ToString(CultureInfo.InvariantCulture) + ":" + s;
    }

    public enum ExperimentState { Draft, Running, RolledBack, Completed }

    public readonly struct ExperimentVariant
    {
        public string Name { get; }
        public int Weight { get; }

        public ExperimentVariant(string name, int weight)
        {
            Name = name;
            Weight = weight;
        }
    }

    public sealed class ExperimentDefinition
    {
        public const int MaxHoldoutPercent = 50;

        public string Id { get; }
        public int Version { get; }
        public string Salt { get; }
        public IReadOnlyList<ExperimentVariant> Variants { get; }
        public int HoldoutPercent { get; }
        public ExperimentState State { get; }
        public int TotalWeight { get; }
        public string ControlVariant => Variants[0].Name;

        private ExperimentDefinition(string id, int version, string salt, IReadOnlyList<ExperimentVariant> variants,
            int totalWeight, int holdoutPercent, ExperimentState state)
        {
            Id = id;
            Version = version;
            Salt = salt;
            Variants = variants;
            TotalWeight = totalWeight;
            HoldoutPercent = holdoutPercent;
            State = state;
        }

        /// <summary>First variant is the control. Weights are relative positive integers.</summary>
        public static ExperimentDefinition Create(string id, int version, string salt,
            IEnumerable<ExperimentVariant> variants, int holdoutPercent, ExperimentState state)
        {
            if (string.IsNullOrEmpty(id)) throw new ArgumentException("id cannot be null or empty", nameof(id));
            if (version < 1) throw new ArgumentOutOfRangeException(nameof(version), "version must be at least 1");
            if (string.IsNullOrEmpty(salt)) throw new ArgumentException("salt cannot be null or empty", nameof(salt));
            if (variants == null) throw new ArgumentException("variants cannot be null", nameof(variants));
            if (holdoutPercent < 0 || holdoutPercent > MaxHoldoutPercent)
                throw new ArgumentOutOfRangeException(nameof(holdoutPercent), "holdoutPercent must be 0..50");
            if (state != ExperimentState.Draft && state != ExperimentState.Running &&
                state != ExperimentState.RolledBack && state != ExperimentState.Completed)
                throw new ArgumentOutOfRangeException(nameof(state), "unknown experiment state");

            var list = new List<ExperimentVariant>(variants);
            if (list.Count < 2) throw new ArgumentException("at least two variants (control + treatment) are required", nameof(variants));
            var names = new HashSet<string>(StringComparer.Ordinal);
            long total = 0;
            foreach (var v in list)
            {
                if (string.IsNullOrEmpty(v.Name)) throw new ArgumentException("variant name cannot be null or empty", nameof(variants));
                if (!names.Add(v.Name)) throw new ArgumentException("duplicate variant name: " + v.Name, nameof(variants));
                if (v.Weight <= 0) throw new ArgumentOutOfRangeException(nameof(variants), "variant weights must be positive");
                total += v.Weight;
            }
            if (total > int.MaxValue) throw new ArgumentOutOfRangeException(nameof(variants), "total variant weight overflows");

            return new ExperimentDefinition(id, version, salt, new ReadOnlyCollection<ExperimentVariant>(list),
                (int)total, holdoutPercent, state);
        }
    }

    public sealed class PlayerExperimentContext
    {
        public string GameId { get; }
        public string PlayerId { get; }
        public bool IsChildProfile { get; }
        public bool AnalyticsConsentGranted { get; }

        public PlayerExperimentContext(string gameId, string playerId, bool isChildProfile, bool analyticsConsentGranted)
        {
            if (string.IsNullOrEmpty(gameId)) throw new ArgumentException("gameId cannot be null or empty", nameof(gameId));
            if (string.IsNullOrEmpty(playerId)) throw new ArgumentException("playerId cannot be null or empty", nameof(playerId));
            GameId = gameId;
            PlayerId = playerId;
            IsChildProfile = isChildProfile;
            AnalyticsConsentGranted = analyticsConsentGranted;
        }
    }

    public enum ExperimentAssignmentKind { NotEligible, Holdout, Variant }

    public readonly struct ExperimentAssignment
    {
        public ExperimentAssignmentKind Kind { get; }
        /// <summary>The variant actually served. Always the control for NotEligible and Holdout.</summary>
        public string VariantName { get; }
        public string Reason { get; }

        // Internal: only ExperimentAssigner can mint assignments, so NotEligible cannot be forged as eligible.
        internal ExperimentAssignment(ExperimentAssignmentKind kind, string variantName, string reason)
        {
            Kind = kind;
            VariantName = variantName;
            Reason = reason;
        }
    }

    public static class ExperimentAssigner
    {
        public const string WonderPopGameId = "Wonder_Pop";
        // Distinct hash domain so the variant split is independent of the holdout split.
        private const string VariantSaltSuffix = "/variant";

        public static ExperimentAssignment Assign(ExperimentDefinition def, PlayerExperimentContext ctx)
        {
            if (def == null) throw new ArgumentNullException(nameof(def));
            if (ctx == null) throw new ArgumentNullException(nameof(ctx));
            string control = def.ControlVariant;

            // Fail closed: only catalog game IDs (Ordinal) are eligible (ctx rejects null/empty).
            if (!GameProfileCatalog.IsKnownId(ctx.GameId))
                return new ExperimentAssignment(ExperimentAssignmentKind.NotEligible, control, "UnknownGame");

            if (def.State == ExperimentState.RolledBack)
                return new ExperimentAssignment(ExperimentAssignmentKind.NotEligible, control, "RolledBack");
            if (def.State != ExperimentState.Running)
                return new ExperimentAssignment(ExperimentAssignmentKind.NotEligible, control, "NotRunning");

            if (string.Equals(ctx.GameId, WonderPopGameId, StringComparison.Ordinal))
                return new ExperimentAssignment(ExperimentAssignmentKind.NotEligible, control, "WonderPopNoTargeting");
            if (ctx.IsChildProfile)
                return new ExperimentAssignment(ExperimentAssignmentKind.NotEligible, control, "ChildProfileNoTargeting");
            if (!ctx.AnalyticsConsentGranted)
                return new ExperimentAssignment(ExperimentAssignmentKind.NotEligible, control, "NoConsent");

            int holdoutBucket = StableBucketing.Bucket(def.Id, def.Salt, ctx.PlayerId);
            if (holdoutBucket < def.HoldoutPercent * (StableBucketing.DefaultBucketCount / 100))
                return new ExperimentAssignment(ExperimentAssignmentKind.Holdout, control, "Holdout");

            int point = StableBucketing.Bucket(def.Id, def.Salt + VariantSaltSuffix, ctx.PlayerId, def.TotalWeight);
            int cumulative = 0;
            foreach (var v in def.Variants)
            {
                cumulative += v.Weight;
                if (point < cumulative)
                    return new ExperimentAssignment(ExperimentAssignmentKind.Variant, v.Name, "Assigned");
            }
            // Unreachable: point < TotalWeight == final cumulative.
            throw new InvalidOperationException("variant selection out of range");
        }
    }

    /// <summary>What was actually served at first exposure. Holdout serves the control but is not counted as control.</summary>
    public readonly struct ExposureRecord
    {
        public ExperimentAssignmentKind Kind { get; }
        public string VariantName { get; }

        internal ExposureRecord(ExperimentAssignmentKind kind, string variantName)
        {
            Kind = kind;
            VariantName = variantName;
        }
    }

    /// <summary>
    /// In-memory, idempotent exposure record. One exposure per (experimentId, version, playerId).
    /// The only way in is RecordExposure(def, ctx), which assigns internally and refuses NotEligible players
    /// (Wonder_Pop, child, no consent, not running), so an ineligible exposure cannot be logged.
    /// </summary>
    public sealed class ExposureLog
    {
        private readonly Dictionary<string, ExposureRecord> _records = new Dictionary<string, ExposureRecord>(StringComparer.Ordinal);
        private readonly Dictionary<string, long> _tallies = new Dictionary<string, long>(StringComparer.Ordinal);

        public int Count => _records.Count;

        public bool RecordExposure(ExperimentDefinition def, PlayerExperimentContext ctx)
        {
            var a = ExperimentAssigner.Assign(def, ctx); // throws on null def/ctx
            if (a.Kind == ExperimentAssignmentKind.NotEligible) return false;

            string exp = ExperimentKey(def);
            if (_records.ContainsKey(exp + ctx.PlayerId)) return false;
            _records.Add(exp + ctx.PlayerId, new ExposureRecord(a.Kind, a.VariantName));
            string tally = a.Kind == ExperimentAssignmentKind.Holdout ? HoldoutKey(exp) : VariantKey(exp, a.VariantName);
            _tallies.TryGetValue(tally, out long n);
            _tallies[tally] = n + 1;
            return true;
        }

        public bool TryGetExposure(ExperimentDefinition def, string playerId, out ExposureRecord record)
        {
            if (def == null) throw new ArgumentNullException(nameof(def));
            if (string.IsNullOrEmpty(playerId)) throw new ArgumentException("playerId cannot be null or empty", nameof(playerId));
            return _records.TryGetValue(ExperimentKey(def) + playerId, out record);
        }

        /// <summary>
        /// Exposures served as a real variant, in def.Variants order (holdout excluded), for this experiment id + version.
        /// Pair with def.Variants weights for SampleRatioGuardrail.Check.
        /// </summary>
        public IReadOnlyList<long> VariantCounts(ExperimentDefinition def)
        {
            if (def == null) throw new ArgumentNullException(nameof(def));
            string exp = ExperimentKey(def);
            var counts = new long[def.Variants.Count];
            for (int i = 0; i < counts.Length; i++)
                _tallies.TryGetValue(VariantKey(exp, def.Variants[i].Name), out counts[i]);
            return Array.AsReadOnly(counts);
        }

        public long HoldoutCount(ExperimentDefinition def)
        {
            if (def == null) throw new ArgumentNullException(nameof(def));
            _tallies.TryGetValue(HoldoutKey(ExperimentKey(def)), out long n);
            return n;
        }

        // Length-prefixed id and a digits-only version make the prefix unambiguous; what follows the final '|' is free text.
        private static string ExperimentKey(ExperimentDefinition def) =>
            def.Id.Length.ToString(CultureInfo.InvariantCulture) + ":" + def.Id + "|" +
            def.Version.ToString(CultureInfo.InvariantCulture) + "|";

        private static string HoldoutKey(string exp) => exp + "H";
        private static string VariantKey(string exp, string variant) => exp + "V" + variant;
    }

    public readonly struct SampleRatioResult
    {
        public double ChiSquare { get; }
        public int DegreesOfFreedom { get; }
        public bool IsMismatch { get; }

        public SampleRatioResult(double chiSquare, int degreesOfFreedom, bool isMismatch)
        {
            ChiSquare = chiSquare;
            DegreesOfFreedom = degreesOfFreedom;
            IsMismatch = isMismatch;
        }
    }

    /// <summary>Sample-ratio-mismatch check: Pearson chi-square goodness of fit at p = 0.001.</summary>
    public static class SampleRatioGuardrail
    {
        // Chi-square critical values at p = 0.001 for df = 1..9.
        private static readonly double[] Critical = { 10.828, 13.816, 16.266, 18.467, 20.515, 22.458, 24.322, 26.124, 27.877 };

        public static SampleRatioResult Check(IReadOnlyList<long> observed, IReadOnlyList<int> weights)
        {
            if (observed == null) throw new ArgumentNullException(nameof(observed));
            if (weights == null) throw new ArgumentNullException(nameof(weights));
            if (observed.Count != weights.Count) throw new ArgumentException("observed and weights must have the same length");
            if (observed.Count < 2) throw new ArgumentException("at least two groups are required", nameof(observed));
            int df = observed.Count - 1;
            if (df > Critical.Length) throw new ArgumentOutOfRangeException(nameof(observed), "at most 10 groups (df <= 9) are supported");

            double total = 0, weightSum = 0;
            for (int i = 0; i < observed.Count; i++)
            {
                if (observed[i] < 0) throw new ArgumentOutOfRangeException(nameof(observed), "observed counts cannot be negative");
                if (weights[i] <= 0) throw new ArgumentOutOfRangeException(nameof(weights), "weights must be positive");
                total += observed[i];
                weightSum += weights[i];
            }
            if (total == 0) return new SampleRatioResult(0, df, false);

            double chi = 0;
            for (int i = 0; i < observed.Count; i++)
            {
                double expected = total * weights[i] / weightSum;
                double d = observed[i] - expected;
                chi += d * d / expected;
            }
            return new SampleRatioResult(chi, df, chi > Critical[df - 1]);
        }
    }
}
