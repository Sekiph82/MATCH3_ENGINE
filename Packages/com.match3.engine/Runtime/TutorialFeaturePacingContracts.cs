using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Match3.Engine.Runtime
{
    public sealed class FeatureIntroduction
    {
        public string FeatureId { get; }
        public int EarliestLevel { get; }
        public string NeutralCueKey { get; }

        public FeatureIntroduction(string featureId, int earliestLevel,
            string neutralCueKey)
        {
            if (!FeatureCatalog.IsKnown(featureId))
                throw new ArgumentException("Unknown feature.", nameof(featureId));
            if (earliestLevel < 0 || earliestLevel > 99999)
                throw new ArgumentOutOfRangeException(nameof(earliestLevel));
            if (string.IsNullOrWhiteSpace(neutralCueKey) ||
                neutralCueKey.Length > 96)
                throw new ArgumentException(
                    "Feature needs neutral cue key.", nameof(neutralCueKey));
            FeatureId = featureId;
            EarliestLevel = earliestLevel;
            NeutralCueKey = neutralCueKey;
        }
    }

    public sealed class FeatureIntroductionCatalog
    {
        private readonly ReadOnlyDictionary<string, FeatureIntroduction> _byId;
        public IReadOnlyDictionary<string, FeatureIntroduction> ById => _byId;

        public FeatureIntroductionCatalog(
            IEnumerable<FeatureIntroduction> introductions)
        {
            if (introductions == null)
                throw new ArgumentNullException(nameof(introductions));
            var list = introductions.Take(65).ToArray();
            if (list.Length > 64 || list.Any(x => x == null) ||
                list.Select(x => x.FeatureId)
                    .Distinct(StringComparer.Ordinal).Count() != list.Length)
                throw new ArgumentException("Invalid feature introduction catalog.");
            _byId = new ReadOnlyDictionary<string, FeatureIntroduction>(
                list.ToDictionary(x => x.FeatureId, StringComparer.Ordinal));
        }

        public FeatureIntroduction Require(string featureId)
        {
            FeatureIntroduction result;
            if (featureId == null || !_byId.TryGetValue(featureId, out result))
                throw new ArgumentException(
                    "Unknown introduced feature.", nameof(featureId));
            return result;
        }
    }

    public sealed class FeatureIntroductionReceipt
    {
        public string SourceId { get; }
        public string FeatureId { get; }
        public FeatureIntroductionReceipt(string sourceId, string featureId)
        {
            if (string.IsNullOrWhiteSpace(sourceId) ||
                sourceId.Length > 96 || !FeatureCatalog.IsKnown(featureId))
                throw new ArgumentException(
                    "Feature introduction receipt requires bounded valid IDs.");
            SourceId = sourceId;
            FeatureId = featureId;
        }
    }

    public sealed class FeatureIntroductionSnapshot
    {
        private readonly ReadOnlyCollection<FeatureIntroductionReceipt> _receipts;
        public string GameId { get; }
        public IReadOnlyList<FeatureIntroductionReceipt> Receipts => _receipts;

        public FeatureIntroductionSnapshot(string gameId,
            IEnumerable<FeatureIntroductionReceipt> receipts = null)
        {
            if (!GameProfileCatalog.IsKnownId(gameId))
                throw new ArgumentException("Unknown game ID.", nameof(gameId));
            var list = (receipts ??
                Enumerable.Empty<FeatureIntroductionReceipt>()).Take(129).ToArray();
            if (list.Length > 128 || list.Any(x => x == null) ||
                list.Select(x => x.SourceId).Distinct(StringComparer.Ordinal)
                    .Count() != list.Length ||
                list.Select(x => x.FeatureId).Distinct(StringComparer.Ordinal)
                    .Count() != list.Length)
                throw new ArgumentException(
                    "Invalid or duplicated introduction receipt.");
            GameId = gameId;
            _receipts = new ReadOnlyCollection<FeatureIntroductionReceipt>(list);
        }

        public bool HasIntroduced(string featureId) =>
            _receipts.Any(x => StringComparer.Ordinal.Equals(
                x.FeatureId, featureId));
    }

    public sealed class FeatureIntroductionPlan
    {
        public FeatureIntroductionSnapshot Result { get; }
        public bool Applied { get; }
        public bool Visible { get; }
        internal FeatureIntroductionPlan(
            FeatureIntroductionSnapshot result, bool applied, bool visible)
        {
            Result = result;
            Applied = applied;
            Visible = visible;
        }
    }

    public static class FeatureIntroductionPlanner
    {
        public static FeatureIntroductionPlan Apply(
            FeatureIntroductionCatalog catalog,
            FeatureIntroductionSnapshot snapshot,
            GameProfile profile, string featureId,
            string receiptId, int currentLevel, bool accessibleNow)
        {
            if (catalog == null) throw new ArgumentNullException(nameof(catalog));
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            if (profile.Id != snapshot.GameId)
                throw new InvalidOperationException(
                    "Feature introduction save belongs to another game.");
            var introduction = catalog.Require(featureId);
            var claim = new FeatureIntroductionReceipt(receiptId, featureId);
            var prior = snapshot.Receipts.FirstOrDefault(x =>
                StringComparer.Ordinal.Equals(x.SourceId, claim.SourceId));
            if (prior != null)
            {
                if (!StringComparer.Ordinal.Equals(prior.FeatureId, featureId))
                    throw new InvalidOperationException(
                        "Feature introduction receipt changed identity.");
                return new FeatureIntroductionPlan(snapshot, false,
                    IsVisible(profile, snapshot, featureId));
            }
            if (currentLevel < introduction.EarliestLevel ||
                !accessibleNow ||
                !profile.IsEnabled(featureId, EngineCompatibility.MinimumCapability) ||
                snapshot.HasIntroduced(featureId))
                return new FeatureIntroductionPlan(snapshot, false,
                    IsVisible(profile, snapshot, featureId));
            if (snapshot.Receipts.Count >= 128)
                throw new InvalidOperationException(
                    "Feature introduction ledger limit reached.");
            var result = new FeatureIntroductionSnapshot(profile.Id,
                snapshot.Receipts.Concat(new[] { claim }));
            return new FeatureIntroductionPlan(result, true,
                IsVisible(profile, result, featureId));
        }

        public static bool IsVisible(GameProfile profile,
            FeatureIntroductionSnapshot snapshot, string featureId)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            if (profile.Id != snapshot.GameId)
                throw new InvalidOperationException("Foreign feature ledger.");
            return snapshot.HasIntroduced(featureId) &&
                profile.IsEnabled(featureId, EngineCompatibility.MinimumCapability);
        }
    }
}
