using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Match3.Engine.Runtime
{
    // Neutral UI instructions only: no artwork, rewards, timers, or rule authority.
    public enum FtueCueKind
    {
        Welcome = 0, Swap = 1, Special = 2, Combo = 3, Goal = 4
    }

    public enum FtueReceiptKind { Presented = 0, Completed = 1 }

    public sealed class FtueStepDefinition
    {
        public string Id { get; }
        public int Level { get; }
        public FtueCueKind Cue { get; }
        public UiSurfaceKind Surface { get; }
        public string LocalizationKey { get; }
        public string HighlightKey { get; }

        public FtueStepDefinition(string id, int level, FtueCueKind cue,
            UiSurfaceKind surface, string localizationKey, string highlightKey)
        {
            if (string.IsNullOrWhiteSpace(id) || id.Length > 64)
                throw new ArgumentException("Bounded step ID required.", nameof(id));
            if (level < 0 || level > 3)
                throw new ArgumentOutOfRangeException(nameof(level),
                    "FTUE is limited to Home and first three levels.");
            if (!Enum.IsDefined(typeof(FtueCueKind), cue))
                throw new ArgumentOutOfRangeException(nameof(cue));
            if (!Enum.IsDefined(typeof(UiSurfaceKind), surface))
                throw new ArgumentOutOfRangeException(nameof(surface));
            if (level == 0 ? (cue != FtueCueKind.Welcome ||
                surface != UiSurfaceKind.Home) :
                (cue == FtueCueKind.Welcome || surface != UiSurfaceKind.Hud))
                throw new ArgumentException("Cue and surface must match the stage.");
            if (string.IsNullOrWhiteSpace(localizationKey) ||
                localizationKey.Length > 96 ||
                string.IsNullOrWhiteSpace(highlightKey) ||
                highlightKey.Length > 96)
                throw new ArgumentException("Neutral text and focus keys required.");
            Id = id;
            Level = level;
            Cue = cue;
            Surface = surface;
            LocalizationKey = localizationKey;
            HighlightKey = highlightKey;
        }
    }

    public sealed class FtueCatalog
    {
        private readonly ReadOnlyCollection<FtueStepDefinition> _steps;
        public string GameId { get; }
        public int Version { get; }
        public IReadOnlyList<FtueStepDefinition> Steps => _steps;

        public FtueCatalog(string gameId, int version,
            IEnumerable<FtueStepDefinition> steps)
        {
            if (!GameProfileCatalog.IsKnownId(gameId))
                throw new ArgumentException("Unknown game ID.", nameof(gameId));
            if (version < 1 || version > 10000)
                throw new ArgumentOutOfRangeException(nameof(version));
            if (steps == null) throw new ArgumentNullException(nameof(steps));
            var array = steps.Take(33).ToArray();
            if (array.Length == 0 || array.Length > 32 ||
                array.Any(s => s == null) ||
                array.Select(s => s.Id).Distinct(StringComparer.Ordinal).Count() != array.Length)
                throw new ArgumentException("Invalid step list.", nameof(steps));
            if (array[0].Level != 0 ||
                array.Count(s => s.Level == 0) != 1 ||
                array.Any(s => s.Level < 0 || s.Level > 3) ||
                array.Any(s => s.Cue == FtueCueKind.Welcome && s.Level != 0))
                throw new ArgumentException("Home introduction must be first.");
            for (var i = 1; i < array.Length; i++)
                if (array[i].Level < array[i - 1].Level)
                    throw new ArgumentException("Steps must follow level order.");
            GameId = gameId;
            Version = version;
            _steps = new ReadOnlyCollection<FtueStepDefinition>(array);
        }

        public FtueStepDefinition Get(string id)
        {
            var step = _steps.FirstOrDefault(s =>
                StringComparer.Ordinal.Equals(s.Id, id));
            if (step == null)
                throw new ArgumentException("Unknown tutorial step.", nameof(id));
            return step;
        }

        public static FtueCatalog ThreeLevel(string gameId)
        {
            return new FtueCatalog(gameId, 1, new[]
            {
                new FtueStepDefinition("intro", 0, FtueCueKind.Welcome,
                    UiSurfaceKind.Home, "ftue.welcome", "cta.play"),
                new FtueStepDefinition("swap", 1, FtueCueKind.Swap,
                    UiSurfaceKind.Hud, "ftue.swap", "board.legal_move"),
                new FtueStepDefinition("special", 2, FtueCueKind.Special,
                    UiSurfaceKind.Hud, "ftue.special", "board.special"),
                new FtueStepDefinition("combo", 3, FtueCueKind.Combo,
                    UiSurfaceKind.Hud, "ftue.combo", "board.combo"),
                new FtueStepDefinition("goals", 3, FtueCueKind.Goal,
                    UiSurfaceKind.Hud, "ftue.goals", "hud.goals")
            });
        }
    }

    public sealed class FtueReceipt
    {
        public string Id { get; }
        public string StepId { get; }
        public FtueReceiptKind Kind { get; }

        public FtueReceipt(string id, string stepId, FtueReceiptKind kind)
        {
            if (string.IsNullOrWhiteSpace(id) || id.Length > 96 ||
                string.IsNullOrWhiteSpace(stepId) || stepId.Length > 64)
                throw new ArgumentException("Bounded receipt and step IDs required.");
            if (!Enum.IsDefined(typeof(FtueReceiptKind), kind))
                throw new ArgumentOutOfRangeException(nameof(kind));
            Id = id;
            StepId = stepId;
            Kind = kind;
        }

        public string Signature => StepId + "|" + (int)Kind;
    }

    // The host persists the immutable snapshot after EVERY accepted receipt.
    // Game rewards and authoritative saves remain owned by existing ledgers.
    public sealed class FtueSnapshot
    {
        private readonly ReadOnlyCollection<FtueReceipt> _receipts;
        private readonly ReadOnlyCollection<string> _completed;
        private readonly ReadOnlyCollection<string> _presented;
        public string GameId { get; }
        public int Version { get; }
        public IReadOnlyList<FtueReceipt> Receipts => _receipts;
        public IReadOnlyList<string> Completed => _completed;
        public IReadOnlyList<string> Presented => _presented;

        public FtueSnapshot(string gameId, int version,
            IEnumerable<FtueReceipt> receipts = null)
        {
            if (!GameProfileCatalog.IsKnownId(gameId))
                throw new ArgumentException("Unknown game ID.", nameof(gameId));
            if (version < 1 || version > 10000)
                throw new ArgumentOutOfRangeException(nameof(version));
            var entries = (receipts ?? Enumerable.Empty<FtueReceipt>())
                .Take(513).ToArray();
            if (entries.Length > 512 || entries.Any(r => r == null) ||
                entries.Select(r => r.Id).Distinct(StringComparer.Ordinal).Count() != entries.Length)
                throw new ArgumentException("Invalid or oversized FTUE receipt ledger.");
            var presented = new HashSet<string>(StringComparer.Ordinal);
            var completed = new HashSet<string>(StringComparer.Ordinal);
            foreach (var r in entries)
            {
                if (r.Kind == FtueReceiptKind.Presented)
                    presented.Add(r.StepId);
                else if (!presented.Contains(r.StepId) || !completed.Add(r.StepId))
                    throw new ArgumentException("Completion requires presentation exactly once.");
            }
            GameId = gameId;
            Version = version;
            _receipts = new ReadOnlyCollection<FtueReceipt>(entries);
            _presented = new ReadOnlyCollection<string>(
                presented.OrderBy(x => x, StringComparer.Ordinal).ToList());
            _completed = new ReadOnlyCollection<string>(
                completed.OrderBy(x => x, StringComparer.Ordinal).ToList());
        }
    }

    public sealed class FtuePlan
    {
        public FtueSnapshot Result { get; }
        public bool Applied { get; }
        public FtueStepDefinition Step { get; }
        internal FtuePlan(FtueSnapshot result, bool applied, FtueStepDefinition step)
        {
            Result = result;
            Applied = applied;
            Step = step;
        }
    }

    public static class FtuePlanner
    {
        public static FtuePlan Apply(FtueCatalog catalog, FtueSnapshot snapshot,
            string stepId, string receiptId, FtueReceiptKind kind, int level,
            bool uiAvailable = true)
        {
            if (catalog == null) throw new ArgumentNullException(nameof(catalog));
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            if (catalog.GameId != snapshot.GameId ||
                catalog.Version != snapshot.Version)
                throw new InvalidOperationException("FTUE catalog and save identity mismatch.");
            if (!Enum.IsDefined(typeof(FtueReceiptKind), kind))
                throw new ArgumentOutOfRangeException(nameof(kind));
            if (level < 0) throw new ArgumentOutOfRangeException(nameof(level));
            var step = catalog.Get(stepId);
            var receipt = new FtueReceipt(receiptId, stepId, kind);
            var prior = snapshot.Receipts.FirstOrDefault(r =>
                StringComparer.Ordinal.Equals(r.Id, receipt.Id));
            if (prior != null)
            {
                if (prior.Signature != receipt.Signature)
                    throw new InvalidOperationException("FTUE receipt replay payload changed.");
                return new FtuePlan(snapshot, false, step);
            }
            if (level != step.Level || !uiAvailable || level > 3 ||
                snapshot.Completed.Contains(stepId) ||
                (kind == FtueReceiptKind.Presented &&
                 snapshot.Presented.Contains(stepId)) ||
                (kind == FtueReceiptKind.Completed &&
                 !snapshot.Presented.Contains(stepId)))
                return new FtuePlan(snapshot, false, step);
            var index = catalog.Steps.ToList().FindIndex(s =>
                StringComparer.Ordinal.Equals(s.Id, stepId));
            if (catalog.Steps.Take(index).Any(s =>
                !snapshot.Completed.Contains(s.Id)))
                return new FtuePlan(snapshot, false, step);
            if (snapshot.Receipts.Count >= 512)
                throw new InvalidOperationException("FTUE receipt cap reached.");
            return new FtuePlan(
                new FtueSnapshot(snapshot.GameId, snapshot.Version,
                    snapshot.Receipts.Concat(new[] { receipt })),
                true, step);
        }
    }
}
