using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Match3.Engine.Runtime
{
    public sealed class PresentationSpriteBinding
    {
        public string ContentId { get; }
        public string SpriteKey { get; }

        public PresentationSpriteBinding(string contentId, string spriteKey)
        {
            if (string.IsNullOrWhiteSpace(contentId))
                throw new ArgumentException("Presentation content ID required.", nameof(contentId));
            if (string.IsNullOrWhiteSpace(spriteKey))
                throw new ArgumentException("Presentation sprite key required.", nameof(spriteKey));
            ContentId = contentId;
            SpriteKey = spriteKey;
        }
    }

    public sealed class PresentationSpriteCatalog
    {
        private readonly ReadOnlyDictionary<string, string> _spriteKeys;

        public string PlaceholderSpriteKey { get; }
        public IReadOnlyDictionary<string, string> SpriteKeys => _spriteKeys;

        public PresentationSpriteCatalog(
            IEnumerable<PresentationSpriteBinding> bindings,
            string placeholderSpriteKey)
        {
            if (bindings == null) throw new ArgumentNullException(nameof(bindings));
            if (string.IsNullOrWhiteSpace(placeholderSpriteKey))
                throw new ArgumentException(
                    "Placeholder sprite key required.", nameof(placeholderSpriteKey));

            var array = bindings.Take(2049).ToArray();
            if (array.Length > 2048 || array.Any(x => x == null))
                throw new ArgumentException(
                    "Presentation sprite binding limit exceeded or contains null.",
                    nameof(bindings));

            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var binding in array)
            {
                if (map.ContainsKey(binding.ContentId))
                    throw new ArgumentException(
                        "Duplicate presentation sprite content ID.", nameof(bindings));
                map.Add(binding.ContentId, binding.SpriteKey);
            }

            PlaceholderSpriteKey = placeholderSpriteKey;
            _spriteKeys = new ReadOnlyDictionary<string, string>(map);
        }
    }

    public sealed class BoardSpriteDescriptor
    {
        public BoardPosition Position { get; }
        public string ContentId { get; }
        public string SpriteKey { get; }
        public int SortOrder { get; }
        public bool UsesFallback { get; }

        internal BoardSpriteDescriptor(BoardPosition position,
            string contentId, string spriteKey, int sortOrder,
            bool usesFallback)
        {
            Position = position;
            ContentId = contentId;
            SpriteKey = spriteKey;
            SortOrder = sortOrder;
            UsesFallback = usesFallback;
        }
    }

    public sealed class BoardVisualPlan
    {
        private readonly ReadOnlyCollection<BoardSpriteDescriptor> _sprites;
        private readonly ReadOnlyCollection<string> _warnings;

        public IReadOnlyList<BoardSpriteDescriptor> Sprites => _sprites;
        public IReadOnlyList<string> Warnings => _warnings;
        public int PoolCapacity { get; }
        public int RequiredSlots => _sprites.Count;

        internal BoardVisualPlan(IEnumerable<BoardSpriteDescriptor> sprites,
            IEnumerable<string> warnings, int poolCapacity)
        {
            _sprites = new ReadOnlyCollection<BoardSpriteDescriptor>(
                sprites.ToList());
            _warnings = new ReadOnlyCollection<string>(
                warnings.OrderBy(x => x, StringComparer.Ordinal).ToList());
            PoolCapacity = poolCapacity;
        }
    }

    public static class BoardVisualPlanner
    {
        public static BoardVisualPlan Build(BoardTopology topology,
            PresentationSpriteCatalog catalog, int poolCapacity)
        {
            if (topology == null) throw new ArgumentNullException(nameof(topology));
            if (catalog == null) throw new ArgumentNullException(nameof(catalog));
            if (poolCapacity < 1 || poolCapacity > 65536)
                throw new ArgumentOutOfRangeException(nameof(poolCapacity));
            if (topology.Occupants.Count > poolCapacity)
                throw new InvalidOperationException(
                    "Board visual pool capacity is below required occupant count.");

            var ordered = topology.Occupants
                .OrderBy(x => x.Key.Layer)
                .ThenBy(x => x.Key.Row)
                .ThenBy(x => x.Key.Column)
                .ToArray();
            var sprites = new List<BoardSpriteDescriptor>(ordered.Length);
            var warnings = new HashSet<string>(StringComparer.Ordinal);

            for (var i = 0; i < ordered.Length; i++)
            {
                string spriteKey;
                var fallback = !catalog.SpriteKeys.TryGetValue(
                    ordered[i].Value, out spriteKey);
                if (fallback)
                {
                    spriteKey = catalog.PlaceholderSpriteKey;
                    warnings.Add("missing-sprite:" + ordered[i].Value);
                }

                sprites.Add(new BoardSpriteDescriptor(
                    ordered[i].Key,
                    ordered[i].Value,
                    spriteKey,
                    i,
                    fallback));
            }

            return new BoardVisualPlan(sprites, warnings, poolCapacity);
        }
    }

    public enum PresentationEventKind
    {
        Swap = 0,
        Clear = 1,
        Line = 2,
        Area = 3,
        Color = 4,
        Combo = 5,
        Refill = 6
    }

    public enum PresentationIntensity
    {
        Small = 0,
        Medium = 1,
        MajorUnlock = 2
    }

    public sealed class PresentationEvent
    {
        private readonly ReadOnlyCollection<BoardPosition> _positions;

        public string EventId { get; }
        public long Sequence { get; }
        public PresentationEventKind Kind { get; }
        public string PresentationKey { get; }
        public PresentationIntensity Intensity { get; }
        public IReadOnlyList<BoardPosition> Positions => _positions;

        public PresentationEvent(string eventId, long sequence,
            PresentationEventKind kind, string presentationKey,
            IEnumerable<BoardPosition> positions,
            PresentationIntensity intensity = PresentationIntensity.Small)
        {
            if (string.IsNullOrWhiteSpace(eventId))
                throw new ArgumentException("Presentation event ID required.", nameof(eventId));
            if (sequence < 1)
                throw new ArgumentOutOfRangeException(nameof(sequence));
            if (!Enum.IsDefined(typeof(PresentationEventKind), kind))
                throw new ArgumentOutOfRangeException(nameof(kind));
            if (string.IsNullOrWhiteSpace(presentationKey))
                throw new ArgumentException(
                    "Presentation event key required.", nameof(presentationKey));
            if (!Enum.IsDefined(typeof(PresentationIntensity), intensity))
                throw new ArgumentOutOfRangeException(nameof(intensity));
            if (positions == null) throw new ArgumentNullException(nameof(positions));

            var array = positions.Take(1025).ToArray();
            if (array.Length < 1 || array.Length > 1024)
                throw new ArgumentException(
                    "Presentation event requires 1..1024 positions.", nameof(positions));
            if (array.Distinct().Count() != array.Length)
                throw new ArgumentException(
                    "Presentation event cannot contain duplicate positions.",
                    nameof(positions));

            EventId = eventId;
            Sequence = sequence;
            Kind = kind;
            PresentationKey = presentationKey;
            Intensity = intensity;
            _positions = new ReadOnlyCollection<BoardPosition>(
                array.OrderBy(x => x.Layer)
                    .ThenBy(x => x.Row)
                    .ThenBy(x => x.Column)
                    .ToList());
        }

        internal string Signature =>
            Sequence + "|" + ((int)Kind) + "|" + PresentationKey + "|" +
            ((int)Intensity) + "|" +
            string.Join(",", _positions.Select(x => x.ToString()).ToArray());
    }

    public sealed class PresentationEffectBinding
    {
        public string PresentationKey { get; }
        public string EffectKey { get; }

        public PresentationEffectBinding(string presentationKey, string effectKey)
        {
            if (string.IsNullOrWhiteSpace(presentationKey))
                throw new ArgumentException(
                    "Presentation binding key required.", nameof(presentationKey));
            if (string.IsNullOrWhiteSpace(effectKey))
                throw new ArgumentException("Effect key required.", nameof(effectKey));
            PresentationKey = presentationKey;
            EffectKey = effectKey;
        }
    }

    public sealed class PresentationEffectCatalog
    {
        private readonly ReadOnlyDictionary<string, string> _effectKeys;

        public string FallbackEffectKey { get; }
        public IReadOnlyDictionary<string, string> EffectKeys => _effectKeys;

        public PresentationEffectCatalog(
            IEnumerable<PresentationEffectBinding> bindings,
            string fallbackEffectKey)
        {
            if (bindings == null) throw new ArgumentNullException(nameof(bindings));
            if (string.IsNullOrWhiteSpace(fallbackEffectKey))
                throw new ArgumentException(
                    "Fallback effect key required.", nameof(fallbackEffectKey));

            var array = bindings.Take(1025).ToArray();
            if (array.Length > 1024 || array.Any(x => x == null))
                throw new ArgumentException(
                    "Presentation effect binding limit exceeded or contains null.",
                    nameof(bindings));

            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var binding in array)
            {
                if (map.ContainsKey(binding.PresentationKey))
                    throw new ArgumentException(
                        "Duplicate presentation effect binding.", nameof(bindings));
                map.Add(binding.PresentationKey, binding.EffectKey);
            }

            FallbackEffectKey = fallbackEffectKey;
            _effectKeys = new ReadOnlyDictionary<string, string>(map);
        }
    }

    public sealed class PresentationEventLedger
    {
        private readonly ReadOnlyDictionary<string, string> _signatures;

        public long LastSequence { get; }
        public IReadOnlyDictionary<string, string> Signatures => _signatures;

        public PresentationEventLedger(long lastSequence = 0,
            IEnumerable<KeyValuePair<string, string>> signatures = null)
        {
            if (lastSequence < 0)
                throw new ArgumentOutOfRangeException(nameof(lastSequence));

            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var pair in signatures ??
                Enumerable.Empty<KeyValuePair<string, string>>())
            {
                if (string.IsNullOrWhiteSpace(pair.Key) ||
                    string.IsNullOrWhiteSpace(pair.Value) ||
                    map.ContainsKey(pair.Key))
                    throw new ArgumentException(
                        "Invalid presentation event ledger.", nameof(signatures));
                if (map.Count >= 100000)
                    throw new ArgumentException(
                        "Presentation event ledger limit exceeded.", nameof(signatures));
                map.Add(pair.Key, pair.Value);
            }

            LastSequence = lastSequence;
            _signatures = new ReadOnlyDictionary<string, string>(map);
        }
    }

    public sealed class PresentationEffectCue
    {
        private readonly ReadOnlyCollection<BoardPosition> _positions;

        public string EventId { get; }
        public string EffectKey { get; }
        public PresentationEventKind Kind { get; }
        public PresentationIntensity Intensity { get; }
        public bool UsesFallback { get; }
        public IReadOnlyList<BoardPosition> Positions => _positions;

        internal PresentationEffectCue(PresentationEvent source,
            string effectKey, bool usesFallback)
        {
            EventId = source.EventId;
            EffectKey = effectKey;
            Kind = source.Kind;
            Intensity = source.Intensity;
            UsesFallback = usesFallback;
            _positions = new ReadOnlyCollection<BoardPosition>(
                source.Positions.ToList());
        }
    }

    public sealed class PresentationEffectPlan
    {
        public PresentationEventLedger Result { get; }
        public bool Applied { get; }
        public PresentationEffectCue Cue { get; }

        internal PresentationEffectPlan(PresentationEventLedger result,
            bool applied, PresentationEffectCue cue)
        {
            Result = result;
            Applied = applied;
            Cue = cue;
        }
    }

    public static class PresentationEffectPlanner
    {
        public static PresentationEffectPlan Project(
            PresentationEventLedger ledger,
            PresentationEffectCatalog catalog,
            PresentationEvent presentationEvent)
        {
            if (ledger == null) throw new ArgumentNullException(nameof(ledger));
            if (catalog == null) throw new ArgumentNullException(nameof(catalog));
            if (presentationEvent == null)
                throw new ArgumentNullException(nameof(presentationEvent));

            string existingSignature;
            if (ledger.Signatures.TryGetValue(
                presentationEvent.EventId, out existingSignature))
            {
                if (!StringComparer.Ordinal.Equals(
                    existingSignature, presentationEvent.Signature))
                    throw new InvalidOperationException(
                        "Presentation event ID is bound to another payload.");
                return new PresentationEffectPlan(ledger, false, null);
            }

            if (presentationEvent.Sequence != ledger.LastSequence + 1)
                throw new InvalidOperationException(
                    "Presentation event sequence must be contiguous.");

            string effectKey;
            var fallback = !catalog.EffectKeys.TryGetValue(
                presentationEvent.PresentationKey, out effectKey);
            if (fallback)
                effectKey = catalog.FallbackEffectKey;

            var signatures = ledger.Signatures.ToDictionary(
                x => x.Key, x => x.Value, StringComparer.Ordinal);
            if (signatures.Count >= 100000)
                throw new InvalidOperationException(
                    "Presentation event ledger limit reached.");
            signatures.Add(
                presentationEvent.EventId, presentationEvent.Signature);

            return new PresentationEffectPlan(
                new PresentationEventLedger(
                    presentationEvent.Sequence, signatures),
                true,
                new PresentationEffectCue(
                    presentationEvent, effectKey, fallback));
        }
    }

    public sealed class AnimationRequest
    {
        public string RequestId { get; }
        public string EventId { get; }
        public int Priority { get; }
        public int DurationMilliseconds { get; }
        public bool Skippable { get; }

        public AnimationRequest(string requestId, string eventId,
            int priority, int durationMilliseconds, bool skippable = true)
        {
            if (string.IsNullOrWhiteSpace(requestId))
                throw new ArgumentException("Animation request ID required.", nameof(requestId));
            if (string.IsNullOrWhiteSpace(eventId))
                throw new ArgumentException("Animation event ID required.", nameof(eventId));
            if (priority < 0 || priority > 1000)
                throw new ArgumentOutOfRangeException(nameof(priority));
            if (durationMilliseconds < 0 || durationMilliseconds > 60000)
                throw new ArgumentOutOfRangeException(nameof(durationMilliseconds));

            RequestId = requestId;
            EventId = eventId;
            Priority = priority;
            DurationMilliseconds = durationMilliseconds;
            Skippable = skippable;
        }

        internal string Signature =>
            EventId + "|" + Priority + "|" + DurationMilliseconds + "|" +
            (Skippable ? "1" : "0");
    }

    public sealed class AnimationPolicy
    {
        public int MaximumActive { get; }
        public int ReducedMotionMaximumMilliseconds { get; }

        public AnimationPolicy(int maximumActive,
            int reducedMotionMaximumMilliseconds = 120)
        {
            if (maximumActive < 1 || maximumActive > 256)
                throw new ArgumentOutOfRangeException(nameof(maximumActive));
            if (reducedMotionMaximumMilliseconds < 0 ||
                reducedMotionMaximumMilliseconds > 1000)
                throw new ArgumentOutOfRangeException(
                    nameof(reducedMotionMaximumMilliseconds));
            MaximumActive = maximumActive;
            ReducedMotionMaximumMilliseconds =
                reducedMotionMaximumMilliseconds;
        }
    }

    public sealed class AnimationSnapshot
    {
        private readonly ReadOnlyDictionary<string, AnimationRequest> _active;
        private readonly ReadOnlyDictionary<string, string> _processedSignatures;

        public bool ReducedMotion { get; }
        public IReadOnlyDictionary<string, AnimationRequest> Active => _active;
        public IReadOnlyDictionary<string, string> ProcessedSignatures =>
            _processedSignatures;

        public AnimationSnapshot(bool reducedMotion = false,
            IEnumerable<AnimationRequest> active = null,
            IEnumerable<KeyValuePair<string, string>> processedSignatures = null)
        {
            var activeMap = new Dictionary<string, AnimationRequest>(
                StringComparer.Ordinal);
            foreach (var request in active ?? Enumerable.Empty<AnimationRequest>())
            {
                if (request == null || activeMap.ContainsKey(request.RequestId))
                    throw new ArgumentException("Invalid active animation set.");
                if (activeMap.Count >= 256)
                    throw new ArgumentException("Active animation set exceeds bound.");
                activeMap.Add(request.RequestId, request);
            }

            var processed = new Dictionary<string, string>(
                StringComparer.Ordinal);
            foreach (var pair in processedSignatures ??
                Enumerable.Empty<KeyValuePair<string, string>>())
            {
                if (string.IsNullOrWhiteSpace(pair.Key) ||
                    string.IsNullOrWhiteSpace(pair.Value) ||
                    processed.ContainsKey(pair.Key))
                    throw new ArgumentException(
                        "Invalid animation processed ledger.");
                if (processed.Count >= 100000)
                    throw new ArgumentException(
                        "Animation processed ledger exceeds bound.");
                processed.Add(pair.Key, pair.Value);
            }

            foreach (var pair in activeMap)
            {
                string signature;
                if (!processed.TryGetValue(pair.Key, out signature) ||
                    !StringComparer.Ordinal.Equals(
                        signature, pair.Value.Signature))
                    throw new ArgumentException(
                        "Active animation must have a matching processed signature.");
            }

            ReducedMotion = reducedMotion;
            _active = new ReadOnlyDictionary<string, AnimationRequest>(activeMap);
            _processedSignatures =
                new ReadOnlyDictionary<string, string>(processed);
        }
    }

    public sealed class AnimationPlan
    {
        private readonly ReadOnlyCollection<string> _cancelledRequestIds;

        public AnimationSnapshot Result { get; }
        public bool Applied { get; }
        public bool CompletedImmediately { get; }
        public bool DroppedToTerminalState { get; }
        public IReadOnlyList<string> CancelledRequestIds =>
            _cancelledRequestIds;

        internal AnimationPlan(AnimationSnapshot result,
            bool applied, bool completedImmediately,
            bool droppedToTerminalState,
            IEnumerable<string> cancelledRequestIds)
        {
            Result = result;
            Applied = applied;
            CompletedImmediately = completedImmediately;
            DroppedToTerminalState = droppedToTerminalState;
            _cancelledRequestIds = new ReadOnlyCollection<string>(
                cancelledRequestIds.OrderBy(x => x, StringComparer.Ordinal).ToList());
        }
    }

    public static class AnimationArbitrator
    {
        public static AnimationPlan Schedule(AnimationSnapshot snapshot,
            AnimationPolicy policy, AnimationRequest request)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            if (policy == null) throw new ArgumentNullException(nameof(policy));
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (snapshot.Active.Count > policy.MaximumActive)
                throw new InvalidOperationException(
                    "Active animation state exceeds policy maximum.");

            string existingSignature;
            if (snapshot.ProcessedSignatures.TryGetValue(
                request.RequestId, out existingSignature))
            {
                if (!StringComparer.Ordinal.Equals(
                    existingSignature, request.Signature))
                    throw new InvalidOperationException(
                        "Animation request ID is bound to another payload.");
                return new AnimationPlan(
                    snapshot, false, false, false, new string[0]);
            }

            var processed = snapshot.ProcessedSignatures.ToDictionary(
                x => x.Key, x => x.Value, StringComparer.Ordinal);
            if (processed.Count >= 100000)
                throw new InvalidOperationException(
                    "Animation processed ledger limit reached.");
            processed.Add(request.RequestId, request.Signature);

            if (snapshot.ReducedMotion ||
                request.DurationMilliseconds <=
                    policy.ReducedMotionMaximumMilliseconds)
            {
                return new AnimationPlan(
                    new AnimationSnapshot(
                        snapshot.ReducedMotion,
                        snapshot.Active.Values,
                        processed),
                    true, true, false, new string[0]);
            }

            var active = snapshot.Active.ToDictionary(
                x => x.Key, x => x.Value, StringComparer.Ordinal);
            var cancelled = new List<string>();
            var dropped = false;

            if (active.Count >= policy.MaximumActive)
            {
                var victim = active.Values
                    .OrderBy(x => x.Priority)
                    .ThenBy(x => x.RequestId, StringComparer.Ordinal)
                    .First();
                if (request.Priority > victim.Priority)
                {
                    active.Remove(victim.RequestId);
                    cancelled.Add(victim.RequestId);
                }
                else
                {
                    dropped = true;
                }
            }

            if (!dropped)
                active.Add(request.RequestId, request);

            return new AnimationPlan(
                new AnimationSnapshot(
                    snapshot.ReducedMotion, active.Values, processed),
                true, false, dropped, cancelled);
        }

        public static AnimationPlan Complete(AnimationSnapshot snapshot,
            string requestId)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            if (string.IsNullOrWhiteSpace(requestId))
                throw new ArgumentException(
                    "Animation completion ID required.", nameof(requestId));

            if (!snapshot.Active.ContainsKey(requestId))
                return new AnimationPlan(
                    snapshot, false, false, false, new string[0]);

            var active = snapshot.Active.ToDictionary(
                x => x.Key, x => x.Value, StringComparer.Ordinal);
            active.Remove(requestId);
            return new AnimationPlan(
                new AnimationSnapshot(
                    snapshot.ReducedMotion,
                    active.Values,
                    snapshot.ProcessedSignatures),
                true, false, false, new string[0]);
        }

        public static AnimationPlan SkipSkippable(
            AnimationSnapshot snapshot)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));

            var active = snapshot.Active
                .Where(x => !x.Value.Skippable)
                .ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal);
            var skipped = snapshot.Active
                .Where(x => x.Value.Skippable)
                .Select(x => x.Key)
                .ToArray();

            if (skipped.Length == 0)
                return new AnimationPlan(
                    snapshot, false, false, false, new string[0]);

            return new AnimationPlan(
                new AnimationSnapshot(
                    snapshot.ReducedMotion,
                    active.Values,
                    snapshot.ProcessedSignatures),
                true, true, false, skipped);
        }
    }
}
