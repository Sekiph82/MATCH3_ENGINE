using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;

namespace Match3.Engine.Runtime
{
    // Semantic markers: art/tile geometry lives in the later Visual UI Factory.
    public sealed class AccessibleSymbol
    {
        public string PieceKey { get; }
        public string ShapeKey { get; }
        public string PatternKey { get; }
        public string ScreenReaderKey { get; }

        public AccessibleSymbol(string pieceKey, string shapeKey,
            string patternKey, string screenReaderKey)
        {
            if (!GoodKey(pieceKey) || !GoodKey(shapeKey) ||
                !GoodKey(patternKey) || !GoodKey(screenReaderKey))
                throw new ArgumentException(
                    "Every piece requires neutral shape, pattern and spoken-label keys.");
            PieceKey = pieceKey;
            ShapeKey = shapeKey;
            PatternKey = patternKey;
            ScreenReaderKey = screenReaderKey;
        }

        private static bool GoodKey(string value) =>
            !string.IsNullOrWhiteSpace(value) && value.Length <= 96 &&
            value.All(c => char.IsLetterOrDigit(c) || c == '.' || c == '_' || c == '-');
    }

    public sealed class AccessibleSymbolCatalog
    {
        private readonly ReadOnlyCollection<AccessibleSymbol> _symbols;
        public IReadOnlyList<AccessibleSymbol> Symbols => _symbols;

        public AccessibleSymbolCatalog(IEnumerable<AccessibleSymbol> symbols)
        {
            if (symbols == null) throw new ArgumentNullException(nameof(symbols));
            var arr = symbols.Take(65).ToArray();
            if (arr.Length < 6 || arr.Length > 64 || arr.Any(x => x == null) ||
                arr.Select(x => x.PieceKey).Distinct(StringComparer.Ordinal).Count() != arr.Length ||
                arr.Select(x => x.ShapeKey).Distinct(StringComparer.Ordinal).Count() != arr.Length ||
                arr.Select(x => x.ScreenReaderKey).Distinct(StringComparer.Ordinal).Count() != arr.Length)
                throw new ArgumentException(
                    "Six or more independently shaped and labeled symbols required.",
                    nameof(symbols));
            _symbols = new ReadOnlyCollection<AccessibleSymbol>(arr);
        }

        public static AccessibleSymbolCatalog SixNeutralSymbols()
        {
            return new AccessibleSymbolCatalog(
                Enumerable.Range(1, 6).Select(n =>
                    new AccessibleSymbol("piece." + n, "shape." + n,
                        "pattern." + n, "label.piece." + n)));
        }
    }

    public enum GuardedUiFeature
    {
        Tutorial = 0, Hint = 1, ContinueWithoutAd = 2,
        ExternalLink = 3, RewardedAd = 4, Purchase = 5,
        Friends = 6, Leaderboard = 7, Tournament = 8
    }

    public sealed class GuardedUiDecision
    {
        public bool Enabled { get; }
        public string DisabledReasonKey { get; }

        internal GuardedUiDecision(bool enabled, string reason)
        {
            Enabled = enabled;
            DisabledReasonKey = reason;
        }
    }

    public static class SafeInteractionPolicy
    {
        // Age is NOT established by ProfileAgeRule.General. The caller must
        // explicitly supply independent consent/age verification and feature opt-in.
        // Wonder Pop is child-restricted even when a caller provides adult=true.
        public static GuardedUiDecision Evaluate(GameProfile profile,
            GuardedUiFeature feature, bool adultVerified = false,
            bool consentGranted = false, bool featureEnabled = false)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            if (!Enum.IsDefined(typeof(GuardedUiFeature), feature))
                throw new ArgumentOutOfRangeException(nameof(feature));
            if (feature == GuardedUiFeature.Tutorial ||
                feature == GuardedUiFeature.Hint ||
                feature == GuardedUiFeature.ContinueWithoutAd)
                return new GuardedUiDecision(true, null);
            if (profile.Id == "Wonder_Pop")
                return new GuardedUiDecision(false, "ui.disabled.child_safety");
            if (!adultVerified)
                return new GuardedUiDecision(false, "ui.disabled.age_unverified");
            if (!consentGranted)
                return new GuardedUiDecision(false, "ui.disabled.consent_required");
            if (!featureEnabled)
                return new GuardedUiDecision(false, "ui.disabled.feature_unavailable");
            return new GuardedUiDecision(true, null);
        }
    }

    public sealed class NeutralLocalizedText
    {
        private readonly ReadOnlyDictionary<string, string> _en;
        private readonly ReadOnlyDictionary<string, string> _tr;

        public NeutralLocalizedText(
            IEnumerable<KeyValuePair<string, string>> english,
            IEnumerable<KeyValuePair<string, string>> turkish)
        {
            _en = Freeze(english, "english");
            _tr = Freeze(turkish, "turkish");
            if (_en.Count == 0 ||
                !_en.Keys.OrderBy(x => x, StringComparer.Ordinal)
                    .SequenceEqual(_tr.Keys.OrderBy(x => x, StringComparer.Ordinal)))
                throw new ArgumentException(
                    "EN/TR must provide identical neutral text keys.");
        }

        private static ReadOnlyDictionary<string, string> Freeze(
            IEnumerable<KeyValuePair<string, string>> items, string label)
        {
            if (items == null) throw new ArgumentNullException(label);
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var pair in items.Take(257))
            {
                if (map.Count >= 256 || string.IsNullOrWhiteSpace(pair.Key) ||
                    pair.Key.Length > 96 ||
                    string.IsNullOrWhiteSpace(pair.Value) ||
                    pair.Value.Length > 384 ||
                    map.ContainsKey(pair.Key))
                    throw new ArgumentException(
                        "Invalid neutral localization entry.", label);
                map.Add(pair.Key, pair.Value);
            }
            return new ReadOnlyDictionary<string, string>(map);
        }

        public string Resolve(string key, string language,
            params object[] formatArguments)
        {
            if (string.IsNullOrWhiteSpace(key) || key.Length > 96 ||
                key.Any(ch => !(char.IsLetterOrDigit(ch) ||
                    ch == '.' || ch == '_' || ch == '-')))
                throw new ArgumentException(
                    "Bounded neutral text key required.", nameof(key));
            if (language != "en" && language != "tr")
                throw new ArgumentException("Explicit supported locale required.", nameof(language));
            string text;
            var table = language == "tr" ? _tr : _en;
            if (!table.TryGetValue(key, out text))
                return "[missing:" + key + "]";
            var args = formatArguments ?? new object[0];
            if (args.Length > 4)
                throw new ArgumentException("At most four text arguments allowed.");
            var result = string.Format(CultureInfo.InvariantCulture, text, args);
            if (result.Length > 512)
                throw new InvalidOperationException(
                    "Rendered localization exceeds 512-character safety budget.");
            return result;
        }

        public static NeutralLocalizedText EnglishTurkishFtue()
        {
            var en = new[] {
                Pair("ftue.welcome", "Ready to play?"),
                Pair("ftue.swap", "Swap two neighboring pieces."),
                Pair("ftue.special", "Create a special piece with four matches."),
                Pair("ftue.combo", "Combine special pieces."),
                Pair("ftue.goals", "Follow your level goals."),
                Pair("ui.disabled.child_safety", "Unavailable for this age group."),
                Pair("ui.disabled.age_unverified", "Age confirmation required."),
                Pair("ui.disabled.consent_required", "Consent is required."),
                Pair("ui.disabled.feature_unavailable", "This feature is currently unavailable.")
            };
            var tr = new[] {
                Pair("ftue.welcome", "Oynamaya hazır mısın?"),
                Pair("ftue.swap", "Yan yana iki taşı değiştir."),
                Pair("ftue.special", "Dört taşı eşleştirerek özel taş oluştur."),
                Pair("ftue.combo", "Özel taşları birleştir."),
                Pair("ftue.goals", "Bölüm hedeflerini takip et."),
                Pair("ui.disabled.child_safety", "Bu yaş grubu için kullanılamaz."),
                Pair("ui.disabled.age_unverified", "Yaş doğrulaması gerekiyor."),
                Pair("ui.disabled.consent_required", "Onay gerekiyor."),
                Pair("ui.disabled.feature_unavailable", "Bu özellik şu anda kullanılamıyor.")
            };
            return new NeutralLocalizedText(en, tr);
        }

        private static KeyValuePair<string, string> Pair(string k, string v) =>
            new KeyValuePair<string, string>(k, v);
    }

    public sealed class TextFitReport
    {
        public bool Fits { get; }
        public bool WrapRecommended { get; }
        public int CharacterCount { get; }

        internal TextFitReport(bool fits, bool wrap, int count)
        {
            Fits = fits;
            WrapRecommended = wrap;
            CharacterCount = count;
        }
    }

    public static class NeutralTextFit
    {
        // Early, deterministic string-budget check. Actual rendered bounds
        // remain a downstream Unity UI/real-device acceptance obligation.
        public static TextFitReport Check(string text, int lineCharacters,
            int maximumLines)
        {
            if (text == null) throw new ArgumentNullException(nameof(text));
            if (lineCharacters < 8 || lineCharacters > 200)
                throw new ArgumentOutOfRangeException(nameof(lineCharacters));
            if (maximumLines < 1 || maximumLines > 8)
                throw new ArgumentOutOfRangeException(nameof(maximumLines));
            var lines = text.Replace("\r\n", "\n").Split('\n');
            var needed = lines.Sum(x => Math.Max(1,
                (x.Length + lineCharacters - 1) / lineCharacters));
            return new TextFitReport(
                needed <= maximumLines, needed > lines.Length, text.Length);
        }
    }
}
