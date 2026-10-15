using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Match3.Engine.Runtime
{
    public readonly struct PixelRect : IEquatable<PixelRect>
    {
        public double X { get; }
        public double Y { get; }
        public double Width { get; }
        public double Height { get; }
        public double Right => X + Width;
        public double Bottom => Y + Height;

        public PixelRect(double x, double y, double width, double height)
        {
            if (double.IsNaN(x) || double.IsNaN(y) ||
                double.IsNaN(width) || double.IsNaN(height) ||
                double.IsInfinity(x) || double.IsInfinity(y) ||
                double.IsInfinity(width) || double.IsInfinity(height) ||
                x < 0 || y < 0 || width <= 0 || height <= 0)
                throw new ArgumentOutOfRangeException(nameof(width),
                    "Pixel rectangle must be finite and positive.");
            X = x;
            Y = y;
            Width = width;
            Height = height;
        }

        public bool Contains(PixelRect other) =>
            other.X >= X && other.Y >= Y &&
            other.Right <= Right && other.Bottom <= Bottom;

        public bool Equals(PixelRect other) =>
            X.Equals(other.X) && Y.Equals(other.Y) &&
            Width.Equals(other.Width) && Height.Equals(other.Height);

        public override bool Equals(object value) =>
            value is PixelRect && Equals((PixelRect)value);

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = X.GetHashCode();
                hash = (hash * 397) ^ Y.GetHashCode();
                hash = (hash * 397) ^ Width.GetHashCode();
                hash = (hash * 397) ^ Height.GetHashCode();
                return hash;
            }
        }
    }

    public enum PresentationDeviceClass
    {
        PhonePortrait = 0,
        TabletPortrait = 1
    }

    public sealed class ResponsiveLayoutPolicy
    {
        public double ViewportWidth { get; }
        public double ViewportHeight { get; }
        public double MinimumTouchTarget { get; }
        public double TabletWidthThreshold { get; }
        public PixelRect SafeArea { get; }
        public PresentationDeviceClass DeviceClass { get; }

        public ResponsiveLayoutPolicy(double viewportWidth,
            double viewportHeight, double insetLeft, double insetTop,
            double insetRight, double insetBottom,
            double minimumTouchTarget = 44,
            double tabletWidthThreshold = 768)
        {
            var values = new[]
            {
                viewportWidth, viewportHeight, insetLeft, insetTop,
                insetRight, insetBottom, minimumTouchTarget,
                tabletWidthThreshold
            };
            if (values.Any(x => double.IsNaN(x) || double.IsInfinity(x)) ||
                viewportWidth <= 0 || viewportHeight <= 0 ||
                viewportHeight < viewportWidth ||
                insetLeft < 0 || insetTop < 0 ||
                insetRight < 0 || insetBottom < 0 ||
                insetLeft + insetRight >= viewportWidth ||
                insetTop + insetBottom >= viewportHeight ||
                minimumTouchTarget <= 0 ||
                tabletWidthThreshold <= 0)
                throw new ArgumentOutOfRangeException(
                    nameof(viewportWidth), "Invalid responsive layout policy.");

            ViewportWidth = viewportWidth;
            ViewportHeight = viewportHeight;
            MinimumTouchTarget = minimumTouchTarget;
            TabletWidthThreshold = tabletWidthThreshold;
            SafeArea = new PixelRect(
                insetLeft,
                insetTop,
                viewportWidth - insetLeft - insetRight,
                viewportHeight - insetTop - insetBottom);
            DeviceClass = viewportWidth >= tabletWidthThreshold
                ? PresentationDeviceClass.TabletPortrait
                : PresentationDeviceClass.PhonePortrait;
        }
    }

    public sealed class LayoutElementRequest
    {
        public string ElementId { get; }
        public PixelRect Bounds { get; }
        public bool Interactive { get; }
        public string Text { get; }
        public bool AllowTextOverflow { get; }

        public LayoutElementRequest(string elementId, PixelRect bounds,
            bool interactive, string text = null,
            bool allowTextOverflow = false)
        {
            if (string.IsNullOrWhiteSpace(elementId))
                throw new ArgumentException(
                    "Layout element ID required.", nameof(elementId));
            if (text != null && text.Length > 4096)
                throw new ArgumentException(
                    "Layout text exceeds bounded length.", nameof(text));
            ElementId = elementId;
            Bounds = bounds;
            Interactive = interactive;
            Text = text;
            AllowTextOverflow = allowTextOverflow;
        }
    }

    public sealed class ResponsiveLayoutPlan
    {
        private readonly ReadOnlyCollection<string> _warnings;

        public PresentationDeviceClass DeviceClass { get; }
        public IReadOnlyList<string> Warnings => _warnings;

        internal ResponsiveLayoutPlan(PresentationDeviceClass deviceClass,
            IEnumerable<string> warnings)
        {
            DeviceClass = deviceClass;
            _warnings = new ReadOnlyCollection<string>(
                warnings.OrderBy(x => x, StringComparer.Ordinal).ToList());
        }
    }

    public static class ResponsiveLayoutValidator
    {
        public static ResponsiveLayoutPlan Validate(
            ResponsiveLayoutPolicy policy,
            IEnumerable<LayoutElementRequest> elements)
        {
            if (policy == null) throw new ArgumentNullException(nameof(policy));
            if (elements == null) throw new ArgumentNullException(nameof(elements));

            var array = elements.Take(513).ToArray();
            if (array.Length > 512 || array.Any(x => x == null))
                throw new ArgumentException(
                    "Layout element limit exceeded or contains null.",
                    nameof(elements));
            if (array.GroupBy(x => x.ElementId, StringComparer.Ordinal)
                .Any(x => x.Count() > 1))
                throw new ArgumentException(
                    "Duplicate layout element ID.", nameof(elements));

            var warnings = new List<string>();
            foreach (var element in array)
            {
                if (!policy.SafeArea.Contains(element.Bounds))
                    throw new InvalidOperationException(
                        "Layout element escapes safe area: " + element.ElementId);
                if (element.Interactive &&
                    (element.Bounds.Width < policy.MinimumTouchTarget ||
                     element.Bounds.Height < policy.MinimumTouchTarget))
                    throw new InvalidOperationException(
                        "Interactive layout element is below minimum touch target: " +
                        element.ElementId);

                if (!string.IsNullOrEmpty(element.Text))
                {
                    var columns = Math.Max(
                        1, (int)Math.Floor(element.Bounds.Width / 8.0));
                    var rows = Math.Max(
                        1, (int)Math.Floor(element.Bounds.Height / 16.0));
                    var capacity = checked(columns * rows);
                    if (element.Text.Length > capacity)
                    {
                        if (!element.AllowTextOverflow)
                            throw new InvalidOperationException(
                                "Text exceeds deterministic layout capacity: " +
                                element.ElementId);
                        warnings.Add("text-overflow:" + element.ElementId);
                    }
                }
            }

            return new ResponsiveLayoutPlan(
                policy.DeviceClass, warnings);
        }
    }

    public enum AudioCueKind
    {
        Music = 0,
        Sfx = 1
    }

    public sealed class ThemeAudioBinding
    {
        public string CueKey { get; }
        public AudioCueKind Kind { get; }
        public string AssetKey { get; }

        public ThemeAudioBinding(string cueKey, AudioCueKind kind,
            string assetKey)
        {
            if (string.IsNullOrWhiteSpace(cueKey))
                throw new ArgumentException("Audio cue key required.", nameof(cueKey));
            if (!Enum.IsDefined(typeof(AudioCueKind), kind))
                throw new ArgumentOutOfRangeException(nameof(kind));
            if (string.IsNullOrWhiteSpace(assetKey))
                throw new ArgumentException("Audio asset key required.", nameof(assetKey));
            CueKey = cueKey;
            Kind = kind;
            AssetKey = assetKey;
        }
    }

    public sealed class ThemeAudioCatalog
    {
        private readonly ReadOnlyDictionary<string, ThemeAudioBinding> _bindings;

        public IReadOnlyDictionary<string, ThemeAudioBinding> Bindings => _bindings;

        public ThemeAudioCatalog(IEnumerable<ThemeAudioBinding> bindings)
        {
            if (bindings == null) throw new ArgumentNullException(nameof(bindings));
            var array = bindings.Take(1025).ToArray();
            if (array.Length > 1024 || array.Any(x => x == null))
                throw new ArgumentException(
                    "Audio binding limit exceeded or contains null.",
                    nameof(bindings));
            var map = new Dictionary<string, ThemeAudioBinding>(
                StringComparer.Ordinal);
            foreach (var binding in array)
            {
                if (map.ContainsKey(binding.CueKey))
                    throw new ArgumentException(
                        "Duplicate audio cue key.", nameof(bindings));
                map.Add(binding.CueKey, binding);
            }
            _bindings = new ReadOnlyDictionary<string, ThemeAudioBinding>(map);
        }
    }

    public sealed class AudioSettingsSnapshot
    {
        private readonly ReadOnlyDictionary<string, string> _processedSignatures;

        public int MasterVolume { get; }
        public int MusicVolume { get; }
        public int SfxVolume { get; }
        public bool Muted { get; }
        public IReadOnlyDictionary<string, string> ProcessedSignatures =>
            _processedSignatures;

        public AudioSettingsSnapshot(int masterVolume = 100,
            int musicVolume = 100, int sfxVolume = 100,
            bool muted = false,
            IEnumerable<KeyValuePair<string, string>> processedSignatures = null)
        {
            ValidateVolume(masterVolume, nameof(masterVolume));
            ValidateVolume(musicVolume, nameof(musicVolume));
            ValidateVolume(sfxVolume, nameof(sfxVolume));

            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var pair in processedSignatures ??
                Enumerable.Empty<KeyValuePair<string, string>>())
            {
                if (string.IsNullOrWhiteSpace(pair.Key) ||
                    string.IsNullOrWhiteSpace(pair.Value) ||
                    map.ContainsKey(pair.Key))
                    throw new ArgumentException(
                        "Invalid audio settings ledger.",
                        nameof(processedSignatures));
                if (map.Count >= 100000)
                    throw new ArgumentException(
                        "Audio settings ledger limit exceeded.",
                        nameof(processedSignatures));
                map.Add(pair.Key, pair.Value);
            }

            MasterVolume = masterVolume;
            MusicVolume = musicVolume;
            SfxVolume = sfxVolume;
            Muted = muted;
            _processedSignatures =
                new ReadOnlyDictionary<string, string>(map);
        }

        private static void ValidateVolume(int value, string parameter)
        {
            if (value < 0 || value > 100)
                throw new ArgumentOutOfRangeException(parameter);
        }
    }

    public sealed class AudioSettingsPlan
    {
        public AudioSettingsSnapshot Result { get; }
        public bool Applied { get; }

        internal AudioSettingsPlan(AudioSettingsSnapshot result, bool applied)
        {
            Result = result;
            Applied = applied;
        }
    }

    public static class AudioSettingsPlanner
    {
        public static AudioSettingsPlan Apply(
            AudioSettingsSnapshot snapshot, string sourceId,
            int masterVolume, int musicVolume, int sfxVolume,
            bool muted)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            if (string.IsNullOrWhiteSpace(sourceId))
                throw new ArgumentException(
                    "Audio settings source ID required.", nameof(sourceId));
            if (masterVolume < 0 || masterVolume > 100 ||
                musicVolume < 0 || musicVolume > 100 ||
                sfxVolume < 0 || sfxVolume > 100)
                throw new ArgumentOutOfRangeException(nameof(masterVolume));

            var signature = masterVolume + "|" + musicVolume + "|" +
                sfxVolume + "|" + (muted ? "1" : "0");
            string prior;
            if (snapshot.ProcessedSignatures.TryGetValue(sourceId, out prior))
            {
                if (!StringComparer.Ordinal.Equals(prior, signature))
                    throw new InvalidOperationException(
                        "Audio settings source ID is bound to another payload.");
                return new AudioSettingsPlan(snapshot, false);
            }

            var map = snapshot.ProcessedSignatures.ToDictionary(
                x => x.Key, x => x.Value, StringComparer.Ordinal);
            if (map.Count >= 100000)
                throw new InvalidOperationException(
                    "Audio settings ledger limit reached.");
            map.Add(sourceId, signature);
            return new AudioSettingsPlan(
                new AudioSettingsSnapshot(
                    masterVolume, musicVolume, sfxVolume, muted, map),
                true);
        }
    }

    public sealed class AudioCuePlan
    {
        public string CueKey { get; }
        public AudioCueKind? Kind { get; }
        public string AssetKey { get; }
        public int EffectiveVolume { get; }
        public bool Suppressed { get; }
        public string Warning { get; }

        internal AudioCuePlan(string cueKey, AudioCueKind? kind,
            string assetKey, int effectiveVolume,
            bool suppressed, string warning)
        {
            CueKey = cueKey;
            Kind = kind;
            AssetKey = assetKey;
            EffectiveVolume = effectiveVolume;
            Suppressed = suppressed;
            Warning = warning;
        }
    }

    public static class AudioProjectionPlanner
    {
        public static AudioCuePlan Resolve(ThemeAudioCatalog catalog,
            AudioSettingsSnapshot settings, string cueKey)
        {
            if (catalog == null) throw new ArgumentNullException(nameof(catalog));
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            if (string.IsNullOrWhiteSpace(cueKey))
                throw new ArgumentException("Audio cue key required.", nameof(cueKey));

            ThemeAudioBinding binding;
            if (!catalog.Bindings.TryGetValue(cueKey, out binding))
                return new AudioCuePlan(
                    cueKey, null, null, 0, true,
                    "missing-audio:" + cueKey);

            var channel = binding.Kind == AudioCueKind.Music
                ? settings.MusicVolume
                : settings.SfxVolume;
            var volume = settings.Muted
                ? 0
                : (settings.MasterVolume * channel) / 100;

            return new AudioCuePlan(
                cueKey, binding.Kind, binding.AssetKey,
                volume, settings.Muted || volume == 0, null);
        }
    }

    public enum PresentationAssetKind
    {
        Sprite = 0,
        Effect = 1,
        Audio = 2,
        Plugin = 3
    }

    public sealed class ResolvedPresentationKey
    {
        public PresentationAssetKind Kind { get; }
        public string RequestedKey { get; }
        public string ResolvedKey { get; }
        public bool UsesFallback { get; }
        public string Warning { get; }

        internal ResolvedPresentationKey(PresentationAssetKind kind,
            string requestedKey, string resolvedKey,
            bool usesFallback, string warning)
        {
            Kind = kind;
            RequestedKey = requestedKey;
            ResolvedKey = resolvedKey;
            UsesFallback = usesFallback;
            Warning = warning;
        }
    }

    public static class PresentationFallbackResolver
    {
        public static ResolvedPresentationKey Resolve(
            PresentationAssetKind kind, string requestedKey,
            IEnumerable<string> availableKeys, string fallbackKey)
        {
            if (!Enum.IsDefined(typeof(PresentationAssetKind), kind))
                throw new ArgumentOutOfRangeException(nameof(kind));
            if (string.IsNullOrWhiteSpace(requestedKey))
                throw new ArgumentException(
                    "Presentation key required.", nameof(requestedKey));
            if (availableKeys == null)
                throw new ArgumentNullException(nameof(availableKeys));
            if (string.IsNullOrWhiteSpace(fallbackKey))
                throw new ArgumentException(
                    "Fallback key required.", nameof(fallbackKey));

            var keys = availableKeys.Take(4097).ToArray();
            if (keys.Length > 4096 ||
                keys.Any(string.IsNullOrWhiteSpace) ||
                keys.Distinct(StringComparer.Ordinal).Count() != keys.Length)
                throw new ArgumentException(
                    "Invalid presentation availability catalog.",
                    nameof(availableKeys));

            var available = keys.Contains(
                requestedKey, StringComparer.Ordinal);
            return available
                ? new ResolvedPresentationKey(
                    kind, requestedKey, requestedKey, false, null)
                : new ResolvedPresentationKey(
                    kind, requestedKey, fallbackKey, true,
                    "missing-" + kind.ToString().ToLowerInvariant() +
                    ":" + requestedKey);
        }
    }

    public enum PresentationBudgetTier
    {
        LowEnd = 0,
        Standard = 1,
        HighEnd = 2
    }

    public sealed class PresentationBudgetPolicy
    {
        public PresentationBudgetTier Tier { get; }
        public int MaximumSprites { get; }
        public int MaximumActiveEffects { get; }
        public int MaximumAudioVoices { get; }
        public int MaximumHapticsPerSecond { get; }
        public int MaximumModalDepth { get; }

        public PresentationBudgetPolicy(PresentationBudgetTier tier,
            int maximumSprites, int maximumActiveEffects,
            int maximumAudioVoices, int maximumHapticsPerSecond,
            int maximumModalDepth)
        {
            if (!Enum.IsDefined(typeof(PresentationBudgetTier), tier))
                throw new ArgumentOutOfRangeException(nameof(tier));
            if (maximumSprites < 1 || maximumSprites > 100000 ||
                maximumActiveEffects < 0 || maximumActiveEffects > 10000 ||
                maximumAudioVoices < 0 || maximumAudioVoices > 1024 ||
                maximumHapticsPerSecond < 0 ||
                    maximumHapticsPerSecond > 1000 ||
                maximumModalDepth < 1 || maximumModalDepth > 128)
                throw new ArgumentOutOfRangeException(nameof(maximumSprites));

            Tier = tier;
            MaximumSprites = maximumSprites;
            MaximumActiveEffects = maximumActiveEffects;
            MaximumAudioVoices = maximumAudioVoices;
            MaximumHapticsPerSecond = maximumHapticsPerSecond;
            MaximumModalDepth = maximumModalDepth;
        }
    }

    public sealed class PresentationCounters
    {
        public int Sprites { get; }
        public int ActiveEffects { get; }
        public int AudioVoices { get; }
        public int HapticsPerSecond { get; }
        public int ModalDepth { get; }

        public PresentationCounters(int sprites, int activeEffects,
            int audioVoices, int hapticsPerSecond, int modalDepth)
        {
            if (sprites < 0 || activeEffects < 0 ||
                audioVoices < 0 || hapticsPerSecond < 0 ||
                modalDepth < 0)
                throw new ArgumentOutOfRangeException(nameof(sprites));
            Sprites = sprites;
            ActiveEffects = activeEffects;
            AudioVoices = audioVoices;
            HapticsPerSecond = hapticsPerSecond;
            ModalDepth = modalDepth;
        }
    }

    public sealed class PresentationBudgetReport
    {
        private readonly ReadOnlyCollection<string> _overBudgetMetrics;

        public PresentationBudgetTier Tier { get; }
        public bool WithinBudget => _overBudgetMetrics.Count == 0;
        public IReadOnlyList<string> OverBudgetMetrics => _overBudgetMetrics;

        internal PresentationBudgetReport(PresentationBudgetTier tier,
            IEnumerable<string> overBudgetMetrics)
        {
            Tier = tier;
            _overBudgetMetrics = new ReadOnlyCollection<string>(
                overBudgetMetrics.OrderBy(x => x, StringComparer.Ordinal).ToList());
        }
    }

    public static class PresentationBudgetEvaluator
    {
        public static PresentationBudgetReport Evaluate(
            PresentationBudgetPolicy policy, PresentationCounters counters)
        {
            if (policy == null) throw new ArgumentNullException(nameof(policy));
            if (counters == null) throw new ArgumentNullException(nameof(counters));

            var over = new List<string>();
            if (counters.Sprites > policy.MaximumSprites)
                over.Add("sprites");
            if (counters.ActiveEffects > policy.MaximumActiveEffects)
                over.Add("effects");
            if (counters.AudioVoices > policy.MaximumAudioVoices)
                over.Add("audio-voices");
            if (counters.HapticsPerSecond > policy.MaximumHapticsPerSecond)
                over.Add("haptics-per-second");
            if (counters.ModalDepth > policy.MaximumModalDepth)
                over.Add("modal-depth");

            return new PresentationBudgetReport(policy.Tier, over);
        }
    }
}
