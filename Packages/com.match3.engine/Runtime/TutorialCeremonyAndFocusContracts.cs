using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Match3.Engine.Runtime
{
    public enum NeutralCeremonyKind
    {
        FirstClaim = 0, BoosterIntroduction = 1,
        FeatureIntroduction = 2, CollectionReveal = 3,
        WorldReveal = 4
    }

    public sealed class NeutralCeremonyReceipt
    {
        public string EventId { get; }
        public string EventKey { get; }
        public NeutralCeremonyKind Kind { get; }
        public int Version { get; }

        public NeutralCeremonyReceipt(string eventId, string eventKey,
            NeutralCeremonyKind kind, int version)
        {
            if (string.IsNullOrWhiteSpace(eventId) || eventId.Length > 96 ||
                string.IsNullOrWhiteSpace(eventKey) || eventKey.Length > 96)
                throw new ArgumentException(
                    "Ceremony event requires bounded semantic keys.");
            if (!Enum.IsDefined(typeof(NeutralCeremonyKind), kind))
                throw new ArgumentOutOfRangeException(nameof(kind));
            if (version < 1 || version > 10000)
                throw new ArgumentOutOfRangeException(nameof(version));
            EventId = eventId;
            EventKey = eventKey;
            Kind = kind;
            Version = version;
        }

        public string Signature =>
            EventKey + "|" + (int)Kind + "|" + Version;
    }

    public sealed class NeutralCeremonySnapshot
    {
        private readonly ReadOnlyCollection<NeutralCeremonyReceipt> _receipts;
        public string GameId { get; }
        public IReadOnlyList<NeutralCeremonyReceipt> Receipts => _receipts;

        public NeutralCeremonySnapshot(string gameId,
            IEnumerable<NeutralCeremonyReceipt> receipts = null)
        {
            if (!GameProfileCatalog.IsKnownId(gameId))
                throw new ArgumentException("Unknown game ID.", nameof(gameId));
            var list = (receipts ?? Enumerable.Empty<NeutralCeremonyReceipt>())
                .Take(257).ToArray();
            if (list.Length > 256 || list.Any(x => x == null) ||
                list.Select(x => x.EventId)
                    .Distinct(StringComparer.Ordinal).Count() != list.Length ||
                list.Select(x => x.Signature)
                    .Distinct(StringComparer.Ordinal).Count() != list.Length)
                throw new ArgumentException(
                    "Duplicate or oversized once-only ceremony ledger.");
            GameId = gameId;
            _receipts = new ReadOnlyCollection<NeutralCeremonyReceipt>(list);
        }
    }

    public sealed class NeutralCeremonyPlan
    {
        public NeutralCeremonySnapshot Result { get; }
        public bool Applied { get; }
        internal NeutralCeremonyPlan(NeutralCeremonySnapshot result, bool applied)
        {
            Result = result;
            Applied = applied;
        }
    }

    public static class NeutralCeremonyPlanner
    {
        // Caller must already have committed the authoritative earned/unlocked
        // fact in the existing game ledger. Never awards anything here.
        public static NeutralCeremonyPlan Apply(NeutralCeremonySnapshot snapshot,
            NeutralCeremonyReceipt requested, string expectedGameId,
            bool authorityReceiptCommitted)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            if (requested == null) throw new ArgumentNullException(nameof(requested));
            if (snapshot.GameId != expectedGameId)
                throw new InvalidOperationException("Ceremony belongs to another game.");
            var byEvent = snapshot.Receipts.FirstOrDefault(x =>
                StringComparer.Ordinal.Equals(x.EventId, requested.EventId));
            if (byEvent != null)
            {
                if (!StringComparer.Ordinal.Equals(
                    byEvent.Signature, requested.Signature))
                    throw new InvalidOperationException(
                        "Ceremony event was replayed with different payload.");
                return new NeutralCeremonyPlan(snapshot, false);
            }
            if (!authorityReceiptCommitted || snapshot.Receipts.Any(x =>
                StringComparer.Ordinal.Equals(
                    x.Signature, requested.Signature)))
                return new NeutralCeremonyPlan(snapshot, false);
            if (snapshot.Receipts.Count >= 256)
                throw new InvalidOperationException(
                    "Ceremony ledger cap exceeded.");
            return new NeutralCeremonyPlan(
                new NeutralCeremonySnapshot(snapshot.GameId,
                    snapshot.Receipts.Concat(new[] { requested })),
                true);
        }
    }

    public sealed class NeutralFocusControl
    {
        public string ActionId { get; }
        public string SpokenLabelKey { get; }
        public int Order { get; }
        public bool Enabled { get; }
        public string DisabledReasonKey { get; }

        public NeutralFocusControl(string actionId, string spokenLabelKey,
            int order, bool enabled, string disabledReasonKey = null)
        {
            if (string.IsNullOrWhiteSpace(actionId) ||
                string.IsNullOrWhiteSpace(spokenLabelKey) ||
                order < 0 || order > 255)
                throw new ArgumentException(
                    "Neutral focus control needs action, spoken label, bounded order.");
            if (enabled && !string.IsNullOrWhiteSpace(disabledReasonKey) ||
                !enabled && string.IsNullOrWhiteSpace(disabledReasonKey))
                throw new ArgumentException(
                    "Disabled control must explain why; enabled control cannot.");
            ActionId = actionId;
            SpokenLabelKey = spokenLabelKey;
            Order = order;
            Enabled = enabled;
            DisabledReasonKey = disabledReasonKey;
        }
    }

    public sealed class NeutralFocusOrder
    {
        private readonly ReadOnlyCollection<NeutralFocusControl> _controls;
        public IReadOnlyList<NeutralFocusControl> Controls => _controls;

        public NeutralFocusOrder(IEnumerable<NeutralFocusControl> controls)
        {
            if (controls == null) throw new ArgumentNullException(nameof(controls));
            var list = controls.Take(129).ToArray();
            if (list.Length == 0 || list.Length > 128 || list.Any(c => c == null) ||
                list.Select(c => c.ActionId).Distinct(StringComparer.Ordinal).Count() != list.Length ||
                list.Select(c => c.Order).Distinct().Count() != list.Length)
                throw new ArgumentException("Invalid or ambiguous focus order.");
            _controls = new ReadOnlyCollection<NeutralFocusControl>(
                list.OrderBy(c => c.Order).ToList());
        }
    }

    public sealed class NeutralAssistanceDecision
    {
        public bool HintAvailable { get; }
        public bool NoLossAlternativeAvailable { get; }
        public bool RequiresRewardedAdvertisement => false;
        public bool ReducedEffects { get; }

        internal NeutralAssistanceDecision(
            bool hint, bool noLoss, bool reducedEffects)
        {
            HintAvailable = hint;
            NoLossAlternativeAvailable = noLoss;
            ReducedEffects = reducedEffects;
        }
    }

    public static class NeutralAssistancePolicy
    {
        public static NeutralAssistanceDecision Evaluate(GameProfile profile,
            bool sdkHasLegalMove, bool authoritativeNoLossEligible,
            bool reducedEffects, bool modalOpen, bool gameReady)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            return new NeutralAssistanceDecision(
                sdkHasLegalMove && !modalOpen && gameReady,
                profile.Id == "Wonder_Pop" && authoritativeNoLossEligible,
                reducedEffects);
        }
    }
}
