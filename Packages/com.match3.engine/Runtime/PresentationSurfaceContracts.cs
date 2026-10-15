using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Match3.Engine.Runtime
{
    public sealed class UiCompletenessReport
    {
        private readonly ReadOnlyCollection<UiSurfaceKind> _surfaces;

        public IReadOnlyList<UiSurfaceKind> Surfaces => _surfaces;
        public int VisibleActionCount { get; }

        internal UiCompletenessReport(
            IEnumerable<UiSurfaceKind> surfaces, int visibleActionCount)
        {
            _surfaces = new ReadOnlyCollection<UiSurfaceKind>(
                surfaces.OrderBy(x => (int)x).ToList());
            VisibleActionCount = visibleActionCount;
        }
    }

    public static class UiCompletenessValidator
    {
        public static UiCompletenessReport Validate(
            IEnumerable<UiSurfaceState> states)
        {
            if (states == null) throw new ArgumentNullException(nameof(states));
            var array = states.Take(129).ToArray();
            if (array.Length > 128 || array.Any(x => x == null))
                throw new ArgumentException(
                    "UI completeness state set exceeds bound or contains null.",
                    nameof(states));
            if (array.GroupBy(x => x.Surface)
                .Any(x => x.Count() != 1))
                throw new ArgumentException(
                    "UI completeness requires one state per surface.",
                    nameof(states));

            var required = Enum.GetValues(typeof(UiSurfaceKind))
                .Cast<UiSurfaceKind>()
                .OrderBy(x => (int)x)
                .ToArray();
            var actual = array.Select(x => x.Surface)
                .OrderBy(x => (int)x)
                .ToArray();
            if (!required.SequenceEqual(actual))
                throw new InvalidOperationException(
                    "UI completeness is missing one or more required surfaces.");

            var visibleCount = 0;
            foreach (var state in array)
            {
                var visible = state.Actions.Values
                    .Where(x => x.Visible)
                    .ToArray();
                if (visible.Length == 0)
                    throw new InvalidOperationException(
                        "Player-facing UI surface has no visible action: " +
                        state.Surface + ".");
                visibleCount += visible.Length;

                if (RequiresEscape(state.Surface) &&
                    !visible.Any(x => x.Enabled &&
                        (x.Kind == UiActionKind.Back ||
                         x.Kind == UiActionKind.Close ||
                         x.Kind == UiActionKind.Navigate)))
                    throw new InvalidOperationException(
                        "UI surface lacks an enabled escape route: " +
                        state.Surface + ".");
            }

            return new UiCompletenessReport(actual, visibleCount);
        }

        private static bool RequiresEscape(UiSurfaceKind surface)
        {
            return surface != UiSurfaceKind.Home &&
                surface != UiSurfaceKind.Hud;
        }
    }

    public sealed class PresentationTimingEnvelope
    {
        public PresentationIntensity Intensity { get; }
        public int MaximumDurationMilliseconds { get; }
        public int MaximumEffects { get; }
        public int MaximumAudioCues { get; }
        public bool HapticAllowed { get; }

        public PresentationTimingEnvelope(PresentationIntensity intensity,
            int maximumDurationMilliseconds, int maximumEffects,
            int maximumAudioCues, bool hapticAllowed)
        {
            if (!Enum.IsDefined(typeof(PresentationIntensity), intensity))
                throw new ArgumentOutOfRangeException(nameof(intensity));
            if (maximumDurationMilliseconds < 0 ||
                maximumDurationMilliseconds > 10000)
                throw new ArgumentOutOfRangeException(
                    nameof(maximumDurationMilliseconds));
            if (maximumEffects < 0 || maximumEffects > 256)
                throw new ArgumentOutOfRangeException(nameof(maximumEffects));
            if (maximumAudioCues < 0 || maximumAudioCues > 64)
                throw new ArgumentOutOfRangeException(nameof(maximumAudioCues));
            Intensity = intensity;
            MaximumDurationMilliseconds = maximumDurationMilliseconds;
            MaximumEffects = maximumEffects;
            MaximumAudioCues = maximumAudioCues;
            HapticAllowed = hapticAllowed;
        }
    }

    public sealed class PresentationTimingPolicy
    {
        private readonly ReadOnlyDictionary<PresentationIntensity,
            PresentationTimingEnvelope> _envelopes;

        public int ReducedMotionMaximumMilliseconds { get; }
        public IReadOnlyDictionary<PresentationIntensity,
            PresentationTimingEnvelope> Envelopes => _envelopes;

        public PresentationTimingPolicy(
            IEnumerable<PresentationTimingEnvelope> envelopes,
            int reducedMotionMaximumMilliseconds = 120)
        {
            if (envelopes == null)
                throw new ArgumentNullException(nameof(envelopes));
            if (reducedMotionMaximumMilliseconds < 0 ||
                reducedMotionMaximumMilliseconds > 1000)
                throw new ArgumentOutOfRangeException(
                    nameof(reducedMotionMaximumMilliseconds));

            var array = envelopes.ToArray();
            var required = Enum.GetValues(typeof(PresentationIntensity))
                .Cast<PresentationIntensity>()
                .OrderBy(x => (int)x)
                .ToArray();
            if (array.Length != required.Length ||
                array.Any(x => x == null) ||
                array.GroupBy(x => x.Intensity).Any(x => x.Count() != 1) ||
                !required.SequenceEqual(
                    array.Select(x => x.Intensity)
                        .OrderBy(x => (int)x)))
                throw new ArgumentException(
                    "Timing policy requires one envelope per presentation intensity.",
                    nameof(envelopes));

            var ordered = required.Select(x =>
                array.Single(y => y.Intensity == x)).ToArray();
            for (var i = 1; i < ordered.Length; i++)
            {
                if (ordered[i].MaximumDurationMilliseconds <
                        ordered[i - 1].MaximumDurationMilliseconds ||
                    ordered[i].MaximumEffects <
                        ordered[i - 1].MaximumEffects ||
                    ordered[i].MaximumAudioCues <
                        ordered[i - 1].MaximumAudioCues)
                    throw new ArgumentException(
                        "Presentation intensity budgets must be monotonic.",
                        nameof(envelopes));
            }

            ReducedMotionMaximumMilliseconds =
                reducedMotionMaximumMilliseconds;
            _envelopes = new ReadOnlyDictionary<PresentationIntensity,
                PresentationTimingEnvelope>(
                array.ToDictionary(x => x.Intensity, x => x));
        }
    }

    public sealed class CeremonyStep
    {
        public string StepId { get; }
        public int Order { get; }
        public PresentationIntensity Intensity { get; }
        public string PresentationKey { get; }

        public CeremonyStep(string stepId, int order,
            PresentationIntensity intensity, string presentationKey)
        {
            if (string.IsNullOrWhiteSpace(stepId))
                throw new ArgumentException(
                    "Ceremony step ID required.", nameof(stepId));
            if (order < 1 || order > 1024)
                throw new ArgumentOutOfRangeException(nameof(order));
            if (!Enum.IsDefined(typeof(PresentationIntensity), intensity))
                throw new ArgumentOutOfRangeException(nameof(intensity));
            if (string.IsNullOrWhiteSpace(presentationKey))
                throw new ArgumentException(
                    "Ceremony presentation key required.",
                    nameof(presentationKey));
            StepId = stepId;
            Order = order;
            Intensity = intensity;
            PresentationKey = presentationKey;
        }

        internal string Signature =>
            Order + "|" + ((int)Intensity) + "|" + PresentationKey;
    }

    public sealed class CeremonySnapshot
    {
        private readonly ReadOnlyDictionary<string, string> _signatures;

        public IReadOnlyDictionary<string, string> Signatures => _signatures;

        public CeremonySnapshot(
            IEnumerable<KeyValuePair<string, string>> signatures = null)
        {
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var pair in signatures ??
                Enumerable.Empty<KeyValuePair<string, string>>())
            {
                if (string.IsNullOrWhiteSpace(pair.Key) ||
                    string.IsNullOrWhiteSpace(pair.Value) ||
                    map.ContainsKey(pair.Key))
                    throw new ArgumentException(
                        "Invalid ceremony ledger.", nameof(signatures));
                if (map.Count >= 100000)
                    throw new ArgumentException(
                        "Ceremony ledger limit exceeded.", nameof(signatures));
                map.Add(pair.Key, pair.Value);
            }
            _signatures =
                new ReadOnlyDictionary<string, string>(map);
        }
    }

    public sealed class CeremonyPlan
    {
        private readonly ReadOnlyCollection<string> _completedStepIds;

        public CeremonySnapshot Result { get; }
        public bool Applied { get; }
        public IReadOnlyList<string> CompletedStepIds => _completedStepIds;
        public int DurationMilliseconds { get; }
        public int EffectBudget { get; }
        public int AudioCueBudget { get; }
        public bool HapticsEnabled { get; }
        public bool Skipped { get; }
        public bool ReducedMotion { get; }

        internal CeremonyPlan(CeremonySnapshot result, bool applied,
            IEnumerable<string> completedStepIds,
            int durationMilliseconds, int effectBudget,
            int audioCueBudget, bool hapticsEnabled,
            bool skipped, bool reducedMotion)
        {
            Result = result;
            Applied = applied;
            _completedStepIds = new ReadOnlyCollection<string>(
                completedStepIds.ToList());
            DurationMilliseconds = durationMilliseconds;
            EffectBudget = effectBudget;
            AudioCueBudget = audioCueBudget;
            HapticsEnabled = hapticsEnabled;
            Skipped = skipped;
            ReducedMotion = reducedMotion;
        }
    }

    public static class CeremonyPlanner
    {
        public static CeremonyPlan Project(
            PresentationTimingPolicy policy,
            CeremonySnapshot snapshot,
            string ceremonyId,
            IEnumerable<CeremonyStep> steps,
            bool skipRequested,
            bool reducedMotion)
        {
            if (policy == null) throw new ArgumentNullException(nameof(policy));
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            if (string.IsNullOrWhiteSpace(ceremonyId))
                throw new ArgumentException(
                    "Ceremony ID required.", nameof(ceremonyId));
            if (steps == null) throw new ArgumentNullException(nameof(steps));

            var array = steps.Take(257)
                .OrderBy(x => x == null ? int.MaxValue : x.Order)
                .ToArray();
            if (array.Length < 1 || array.Length > 256 ||
                array.Any(x => x == null) ||
                array.GroupBy(x => x.StepId, StringComparer.Ordinal)
                    .Any(x => x.Count() > 1))
                throw new ArgumentException(
                    "Ceremony requires 1..256 unique steps.", nameof(steps));
            for (var i = 0; i < array.Length; i++)
            {
                if (array[i].Order != i + 1)
                    throw new ArgumentException(
                        "Ceremony step order must be contiguous from one.",
                        nameof(steps));
            }

            var signature = string.Join(";", array.Select(
                x => x.StepId + "=" + x.Signature));
            string existing;
            if (snapshot.Signatures.TryGetValue(ceremonyId, out existing))
            {
                if (!StringComparer.Ordinal.Equals(existing, signature))
                    throw new InvalidOperationException(
                        "Ceremony ID is bound to another payload.");
                return new CeremonyPlan(
                    snapshot, false,
                    array.Select(x => x.StepId),
                    0, 0, 0, false,
                    skipRequested, reducedMotion);
            }

            var ledger = snapshot.Signatures.ToDictionary(
                x => x.Key, x => x.Value, StringComparer.Ordinal);
            if (ledger.Count >= 100000)
                throw new InvalidOperationException(
                    "Ceremony ledger limit reached.");
            ledger.Add(ceremonyId, signature);

            var duration = 0;
            var effects = 0;
            var audio = 0;
            var haptics = false;
            if (!skipRequested)
            {
                foreach (var step in array)
                {
                    var envelope = policy.Envelopes[step.Intensity];
                    duration += reducedMotion
                        ? Math.Min(
                            envelope.MaximumDurationMilliseconds,
                            policy.ReducedMotionMaximumMilliseconds)
                        : envelope.MaximumDurationMilliseconds;
                    effects += envelope.MaximumEffects;
                    audio += envelope.MaximumAudioCues;
                    haptics |= envelope.HapticAllowed && !reducedMotion;
                }
            }

            return new CeremonyPlan(
                new CeremonySnapshot(ledger),
                true,
                array.Select(x => x.StepId),
                duration, effects, audio, haptics,
                skipRequested, reducedMotion);
        }
    }

    public enum DynamicOverlayKind
    {
        Modal = 0,
        AdReturn = 1,
        StoreReturn = 2,
        Keyboard = 3,
        Consent = 4,
        PurchaseBanner = 5
    }

    public sealed class DynamicOverlayRequest
    {
        public string OverlayId { get; }
        public DynamicOverlayKind Kind { get; }
        public PixelRect Bounds { get; }
        public bool Interactive { get; }
        public bool Dismissible { get; }
        public string Text { get; }
        public bool AllowTextOverflow { get; }

        public DynamicOverlayRequest(string overlayId,
            DynamicOverlayKind kind, PixelRect bounds,
            bool interactive, bool dismissible,
            string text = null, bool allowTextOverflow = false)
        {
            if (string.IsNullOrWhiteSpace(overlayId))
                throw new ArgumentException(
                    "Overlay ID required.", nameof(overlayId));
            if (!Enum.IsDefined(typeof(DynamicOverlayKind), kind))
                throw new ArgumentOutOfRangeException(nameof(kind));
            if (bounds == null)
                throw new ArgumentNullException(nameof(bounds));
            if (text != null && text.Length > 4096)
                throw new ArgumentException(
                    "Overlay text exceeds bounded length.", nameof(text));
            OverlayId = overlayId;
            Kind = kind;
            Bounds = bounds;
            Interactive = interactive;
            Dismissible = dismissible;
            Text = text;
            AllowTextOverflow = allowTextOverflow;
        }

        internal string Signature =>
            ((int)Kind) + "|" + Bounds.X + "|" + Bounds.Y + "|" +
            Bounds.Width + "|" + Bounds.Height + "|" +
            (Interactive ? "1" : "0") + "|" +
            (Dismissible ? "1" : "0") + "|" +
            (Text ?? "") + "|" + (AllowTextOverflow ? "1" : "0");
    }

    public sealed class DynamicOverlayPolicy
    {
        public int MaximumDepth { get; }

        public DynamicOverlayPolicy(int maximumDepth)
        {
            if (maximumDepth < 1 || maximumDepth > 32)
                throw new ArgumentOutOfRangeException(nameof(maximumDepth));
            MaximumDepth = maximumDepth;
        }
    }

    public sealed class DynamicOverlaySnapshot
    {
        private readonly ReadOnlyCollection<DynamicOverlayRequest> _stack;
        private readonly ReadOnlyDictionary<string, string> _signatures;

        public IReadOnlyList<DynamicOverlayRequest> Stack => _stack;
        public IReadOnlyDictionary<string, string> Signatures => _signatures;

        public DynamicOverlaySnapshot(
            IEnumerable<DynamicOverlayRequest> stack = null,
            IEnumerable<KeyValuePair<string, string>> signatures = null)
        {
            var items = (stack ?? Enumerable.Empty<DynamicOverlayRequest>())
                .Take(33).ToList();
            if (items.Count > 32 || items.Any(x => x == null) ||
                items.GroupBy(x => x.OverlayId, StringComparer.Ordinal)
                    .Any(x => x.Count() > 1))
                throw new ArgumentException(
                    "Invalid overlay stack.", nameof(stack));

            var ledger = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var pair in signatures ??
                Enumerable.Empty<KeyValuePair<string, string>>())
            {
                if (string.IsNullOrWhiteSpace(pair.Key) ||
                    string.IsNullOrWhiteSpace(pair.Value) ||
                    ledger.ContainsKey(pair.Key))
                    throw new ArgumentException(
                        "Invalid overlay transition ledger.",
                        nameof(signatures));
                if (ledger.Count >= 100000)
                    throw new ArgumentException(
                        "Overlay transition ledger limit exceeded.");
                ledger.Add(pair.Key, pair.Value);
            }

            _stack = new ReadOnlyCollection<DynamicOverlayRequest>(items);
            _signatures = new ReadOnlyDictionary<string, string>(ledger);
        }
    }

    public sealed class DynamicOverlayPlan
    {
        public DynamicOverlaySnapshot Result { get; }
        public bool Applied { get; }
        public string Warning { get; }

        internal DynamicOverlayPlan(
            DynamicOverlaySnapshot result, bool applied, string warning)
        {
            Result = result;
            Applied = applied;
            Warning = warning;
        }
    }

    public static class DynamicOverlayPlanner
    {
        public static DynamicOverlayPlan Push(
            ResponsiveLayoutPolicy layout,
            DynamicOverlayPolicy policy,
            DynamicOverlaySnapshot snapshot,
            DynamicOverlayRequest request,
            string transitionId)
        {
            if (layout == null) throw new ArgumentNullException(nameof(layout));
            if (policy == null) throw new ArgumentNullException(nameof(policy));
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (string.IsNullOrWhiteSpace(transitionId))
                throw new ArgumentException(
                    "Overlay transition ID required.", nameof(transitionId));

            var signature = "push|" + request.OverlayId + "|" +
                request.Signature;
            string prior;
            if (snapshot.Signatures.TryGetValue(transitionId, out prior))
            {
                if (!StringComparer.Ordinal.Equals(prior, signature))
                    throw new InvalidOperationException(
                        "Overlay transition ID is bound to another payload.");
                return new DynamicOverlayPlan(snapshot, false, null);
            }

            if (snapshot.Stack.Count >= policy.MaximumDepth)
                throw new InvalidOperationException(
                    "Overlay stack depth limit reached.");

            var layoutPlan = ResponsiveLayoutValidator.Validate(
                layout,
                new[]
                {
                    new LayoutElementRequest(
                        request.OverlayId,
                        request.Bounds,
                        request.Interactive,
                        request.Text,
                        request.AllowTextOverflow)
                });
            if (snapshot.Stack.Any(x =>
                StringComparer.Ordinal.Equals(
                    x.OverlayId, request.OverlayId)))
                throw new InvalidOperationException(
                    "Overlay ID is already active.");

            var ledger = CopyLedger(snapshot);
            ledger.Add(transitionId, signature);
            return new DynamicOverlayPlan(
                new DynamicOverlaySnapshot(
                    snapshot.Stack.Concat(new[] { request }),
                    ledger),
                true,
                layoutPlan.Warnings.FirstOrDefault());
        }

        public static DynamicOverlayPlan Back(
            DynamicOverlaySnapshot snapshot, string transitionId)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            if (string.IsNullOrWhiteSpace(transitionId))
                throw new ArgumentException(
                    "Overlay transition ID required.", nameof(transitionId));

            string prior;
            if (snapshot.Signatures.TryGetValue(transitionId, out prior))
            {
                if (!prior.StartsWith("back|", StringComparison.Ordinal))
                    throw new InvalidOperationException(
                        "Overlay transition ID is bound to another payload.");
                return new DynamicOverlayPlan(snapshot, false, null);
            }

            if (snapshot.Stack.Count == 0)
                return new DynamicOverlayPlan(snapshot, false, null);

            var top = snapshot.Stack[snapshot.Stack.Count - 1];
            if (!top.Dismissible)
                return new DynamicOverlayPlan(
                    snapshot, false, "overlay-not-dismissible");

            var signature = "back|" + top.OverlayId;

            var ledger = CopyLedger(snapshot);
            ledger.Add(transitionId, signature);
            return new DynamicOverlayPlan(
                new DynamicOverlaySnapshot(
                    snapshot.Stack.Take(snapshot.Stack.Count - 1),
                    ledger),
                true, null);
        }

        private static Dictionary<string, string> CopyLedger(
            DynamicOverlaySnapshot snapshot)
        {
            return snapshot.Signatures.ToDictionary(
                x => x.Key, x => x.Value, StringComparer.Ordinal);
        }
    }

    public enum PlayerSurfaceStateKind
    {
        Ready = 0,
        Loading = 1,
        Offline = 2,
        Error = 3,
        Owned = 4,
        Claimed = 5,
        Disabled = 6,
        Pending = 7
    }

    public sealed class PlayerSurfaceBinding
    {
        private readonly ReadOnlyCollection<string> _textKeys;
        private readonly ReadOnlyCollection<string> _iconKeys;

        public string GameId { get; }
        public UiSurfaceKind Surface { get; }
        public PlayerSurfaceStateKind State { get; }
        public IReadOnlyList<string> TextKeys => _textKeys;
        public IReadOnlyList<string> IconKeys => _iconKeys;

        public PlayerSurfaceBinding(string gameId,
            UiSurfaceKind surface, PlayerSurfaceStateKind state,
            IEnumerable<string> textKeys,
            IEnumerable<string> iconKeys)
        {
            if (!GameProfileCatalog.IsKnownId(gameId))
                throw new ArgumentException("Unknown game ID.", nameof(gameId));
            if (!Enum.IsDefined(typeof(UiSurfaceKind), surface))
                throw new ArgumentOutOfRangeException(nameof(surface));
            if (!Enum.IsDefined(typeof(PlayerSurfaceStateKind), state))
                throw new ArgumentOutOfRangeException(nameof(state));

            GameId = gameId;
            Surface = surface;
            State = state;
            _textKeys = new ReadOnlyCollection<string>(
                FreezeKeys(textKeys, "text"));
            _iconKeys = new ReadOnlyCollection<string>(
                FreezeKeys(iconKeys, "icon"));
            if (_textKeys.Count == 0 && _iconKeys.Count == 0)
                throw new ArgumentException(
                    "Player-facing surface binding needs text or icon keys.");
        }

        internal string Key =>
            GameId + "|" + ((int)Surface) + "|" + ((int)State);

        private static List<string> FreezeKeys(
            IEnumerable<string> keys, string label)
        {
            var list = (keys ?? Enumerable.Empty<string>())
                .Take(129).ToList();
            if (list.Count > 128 ||
                list.Any(string.IsNullOrWhiteSpace) ||
                list.Distinct(StringComparer.Ordinal).Count() != list.Count)
                throw new ArgumentException(
                    "Invalid surface " + label + " keys.");
            return list.OrderBy(x => x, StringComparer.Ordinal).ToList();
        }
    }

    public sealed class PlayerSurfaceMatrix
    {
        private readonly ReadOnlyDictionary<string, PlayerSurfaceBinding> _bindings;

        public IReadOnlyDictionary<string, PlayerSurfaceBinding> Bindings =>
            _bindings;

        public PlayerSurfaceMatrix(
            IEnumerable<PlayerSurfaceBinding> bindings)
        {
            if (bindings == null) throw new ArgumentNullException(nameof(bindings));
            var array = bindings.Take(2049).ToArray();
            if (array.Length > 2048 || array.Any(x => x == null))
                throw new ArgumentException(
                    "Player-facing surface matrix exceeds bound or contains null.",
                    nameof(bindings));

            var map = new Dictionary<string, PlayerSurfaceBinding>(
                StringComparer.Ordinal);
            foreach (var binding in array)
            {
                if (map.ContainsKey(binding.Key))
                    throw new ArgumentException(
                        "Duplicate player-facing surface binding.",
                        nameof(bindings));
                map.Add(binding.Key, binding);
            }

            foreach (var gameId in GameProfileCatalog.Ids)
            foreach (UiSurfaceKind surface in Enum.GetValues(
                typeof(UiSurfaceKind)))
            foreach (PlayerSurfaceStateKind state in Enum.GetValues(
                typeof(PlayerSurfaceStateKind)))
            {
                var key = gameId + "|" + ((int)surface) + "|" + ((int)state);
                if (!map.ContainsKey(key))
                    throw new InvalidOperationException(
                        "Player-facing visual matrix is incomplete: " + key);
            }

            _bindings =
                new ReadOnlyDictionary<string, PlayerSurfaceBinding>(map);
        }
    }

    public sealed class PlayerSurfaceApproval
    {
        private readonly ReadOnlyCollection<string> _deviceSignoffs;

        public bool OwnerVisualApproved { get; }
        public IReadOnlyList<string> DeviceSignoffs => _deviceSignoffs;

        public PlayerSurfaceApproval(bool ownerVisualApproved,
            IEnumerable<string> deviceSignoffs = null)
        {
            var signs = (deviceSignoffs ?? Enumerable.Empty<string>())
                .Select(x => x == null ? null : x.Trim().ToLowerInvariant())
                .ToList();
            if (signs.Any(string.IsNullOrWhiteSpace) ||
                signs.Distinct(StringComparer.Ordinal).Count() != signs.Count)
                throw new ArgumentException(
                    "Invalid visual device signoff IDs.",
                    nameof(deviceSignoffs));
            OwnerVisualApproved = ownerVisualApproved;
            _deviceSignoffs = new ReadOnlyCollection<string>(
                signs.OrderBy(x => x, StringComparer.Ordinal).ToList());
        }
    }

    public sealed class PlayerSurfaceGateReport
    {
        public bool CodeReady { get; }
        public bool OwnerVisualApproved { get; }
        public bool IosDeviceApproved { get; }
        public bool AndroidDeviceApproved { get; }
        public bool FinalApproved =>
            CodeReady && OwnerVisualApproved &&
            IosDeviceApproved && AndroidDeviceApproved;

        internal PlayerSurfaceGateReport(bool codeReady,
            bool ownerVisualApproved,
            bool iosDeviceApproved,
            bool androidDeviceApproved)
        {
            CodeReady = codeReady;
            OwnerVisualApproved = ownerVisualApproved;
            IosDeviceApproved = iosDeviceApproved;
            AndroidDeviceApproved = androidDeviceApproved;
        }
    }

    public static class PlayerSurfaceGate
    {
        public static PlayerSurfaceGateReport Evaluate(
            PlayerSurfaceMatrix matrix, PlayerSurfaceApproval approval)
        {
            if (matrix == null) throw new ArgumentNullException(nameof(matrix));
            if (approval == null) throw new ArgumentNullException(nameof(approval));

            return new PlayerSurfaceGateReport(
                matrix.Bindings.Count > 0,
                approval.OwnerVisualApproved,
                approval.DeviceSignoffs.Contains("ios"),
                approval.DeviceSignoffs.Contains("android"));
        }
    }
}
