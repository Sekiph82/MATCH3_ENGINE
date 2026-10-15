using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Match3.Engine.Runtime
{
    public sealed class AudioVoiceRule
    {
        public string CueKey { get; }
        public AudioCueKind Kind { get; }
        public int MaximumVoices { get; }
        public long CooldownMilliseconds { get; }
        public int Priority { get; }
        public bool DucksMusic { get; }

        public AudioVoiceRule(string cueKey, AudioCueKind kind,
            int maximumVoices, long cooldownMilliseconds,
            int priority, bool ducksMusic = false)
        {
            if (string.IsNullOrWhiteSpace(cueKey))
                throw new ArgumentException("Audio voice cue key required.", nameof(cueKey));
            if (!Enum.IsDefined(typeof(AudioCueKind), kind))
                throw new ArgumentOutOfRangeException(nameof(kind));
            if (maximumVoices < 1 || maximumVoices > 128)
                throw new ArgumentOutOfRangeException(nameof(maximumVoices));
            if (cooldownMilliseconds < 0 || cooldownMilliseconds > 60000)
                throw new ArgumentOutOfRangeException(nameof(cooldownMilliseconds));
            if (priority < 0 || priority > 1000)
                throw new ArgumentOutOfRangeException(nameof(priority));
            if (ducksMusic && kind == AudioCueKind.Music)
                throw new ArgumentException(
                    "Music voices cannot request music ducking.",
                    nameof(ducksMusic));

            CueKey = cueKey;
            Kind = kind;
            MaximumVoices = maximumVoices;
            CooldownMilliseconds = cooldownMilliseconds;
            Priority = priority;
            DucksMusic = ducksMusic;
        }
    }

    public sealed class AudioConcurrencyPolicy
    {
        private readonly ReadOnlyDictionary<string, AudioVoiceRule> _rules;

        public int MaximumGlobalVoices { get; }
        public int MusicDuckPercent { get; }
        public int MusicCrossfadeMilliseconds { get; }
        public IReadOnlyDictionary<string, AudioVoiceRule> Rules => _rules;

        public AudioConcurrencyPolicy(int maximumGlobalVoices,
            int musicDuckPercent, int musicCrossfadeMilliseconds,
            IEnumerable<AudioVoiceRule> rules)
        {
            if (maximumGlobalVoices < 1 || maximumGlobalVoices > 256)
                throw new ArgumentOutOfRangeException(nameof(maximumGlobalVoices));
            if (musicDuckPercent < 0 || musicDuckPercent > 100)
                throw new ArgumentOutOfRangeException(nameof(musicDuckPercent));
            if (musicCrossfadeMilliseconds < 0 ||
                musicCrossfadeMilliseconds > 10000)
                throw new ArgumentOutOfRangeException(
                    nameof(musicCrossfadeMilliseconds));
            if (rules == null) throw new ArgumentNullException(nameof(rules));

            var array = rules.Take(257).ToArray();
            if (array.Length < 1 || array.Length > 256 ||
                array.Any(x => x == null) ||
                array.GroupBy(x => x.CueKey, StringComparer.Ordinal)
                    .Any(x => x.Count() > 1))
                throw new ArgumentException(
                    "Audio concurrency policy needs 1..256 unique voice rules.",
                    nameof(rules));

            MaximumGlobalVoices = maximumGlobalVoices;
            MusicDuckPercent = musicDuckPercent;
            MusicCrossfadeMilliseconds = musicCrossfadeMilliseconds;
            _rules = new ReadOnlyDictionary<string, AudioVoiceRule>(
                array.ToDictionary(x => x.CueKey, x => x, StringComparer.Ordinal));
        }
    }

    public sealed class AudioVoiceRequest
    {
        public string RequestId { get; }
        public string CueKey { get; }
        public AudioCueKind Kind { get; }
        public long TimestampMilliseconds { get; }

        public AudioVoiceRequest(string requestId, string cueKey,
            AudioCueKind kind, long timestampMilliseconds)
        {
            if (string.IsNullOrWhiteSpace(requestId))
                throw new ArgumentException("Audio request ID required.", nameof(requestId));
            if (string.IsNullOrWhiteSpace(cueKey))
                throw new ArgumentException("Audio cue key required.", nameof(cueKey));
            if (!Enum.IsDefined(typeof(AudioCueKind), kind))
                throw new ArgumentOutOfRangeException(nameof(kind));
            if (timestampMilliseconds < 0)
                throw new ArgumentOutOfRangeException(nameof(timestampMilliseconds));

            RequestId = requestId;
            CueKey = cueKey;
            Kind = kind;
            TimestampMilliseconds = timestampMilliseconds;
        }

        internal string Signature =>
            CueKey + "|" + ((int)Kind) + "|" + TimestampMilliseconds;
    }

    public sealed class AudioVoiceLease
    {
        public string RequestId { get; }
        public string CueKey { get; }
        public AudioCueKind Kind { get; }
        public int Priority { get; }
        public long StartedAtMilliseconds { get; }

        internal AudioVoiceLease(AudioVoiceRequest request, int priority)
        {
            RequestId = request.RequestId;
            CueKey = request.CueKey;
            Kind = request.Kind;
            Priority = priority;
            StartedAtMilliseconds = request.TimestampMilliseconds;
        }
    }

    public sealed class AudioConcurrencySnapshot
    {
        private readonly ReadOnlyDictionary<string, AudioVoiceLease> _active;
        private readonly ReadOnlyDictionary<string, long> _lastStartByCue;
        private readonly ReadOnlyDictionary<string, string> _processedSignatures;

        public IReadOnlyDictionary<string, AudioVoiceLease> Active => _active;
        public IReadOnlyDictionary<string, long> LastStartByCue => _lastStartByCue;
        public IReadOnlyDictionary<string, string> ProcessedSignatures =>
            _processedSignatures;

        public AudioConcurrencySnapshot(
            IEnumerable<AudioVoiceLease> active = null,
            IEnumerable<KeyValuePair<string, long>> lastStartByCue = null,
            IEnumerable<KeyValuePair<string, string>> processedSignatures = null)
        {
            var activeMap = new Dictionary<string, AudioVoiceLease>(
                StringComparer.Ordinal);
            foreach (var lease in active ?? Enumerable.Empty<AudioVoiceLease>())
            {
                if (lease == null || activeMap.ContainsKey(lease.RequestId))
                    throw new ArgumentException("Invalid active audio voice set.");
                if (activeMap.Count >= 256)
                    throw new ArgumentException("Active audio voice set exceeds bound.");
                activeMap.Add(lease.RequestId, lease);
            }

            var last = new Dictionary<string, long>(StringComparer.Ordinal);
            foreach (var pair in lastStartByCue ??
                Enumerable.Empty<KeyValuePair<string, long>>())
            {
                if (string.IsNullOrWhiteSpace(pair.Key) ||
                    pair.Value < 0 || last.ContainsKey(pair.Key))
                    throw new ArgumentException("Invalid audio cooldown state.");
                if (last.Count >= 256)
                    throw new ArgumentException("Audio cooldown state exceeds bound.");
                last.Add(pair.Key, pair.Value);
            }

            var processed = new Dictionary<string, string>(
                StringComparer.Ordinal);
            foreach (var pair in processedSignatures ??
                Enumerable.Empty<KeyValuePair<string, string>>())
            {
                if (string.IsNullOrWhiteSpace(pair.Key) ||
                    string.IsNullOrWhiteSpace(pair.Value) ||
                    processed.ContainsKey(pair.Key))
                    throw new ArgumentException("Invalid audio request ledger.");
                if (processed.Count >= 100000)
                    throw new ArgumentException("Audio request ledger exceeds bound.");
                processed.Add(pair.Key, pair.Value);
            }

            foreach (var lease in activeMap.Values)
            {
                string signature;
                if (!processed.TryGetValue(lease.RequestId, out signature))
                    throw new ArgumentException(
                        "Active audio lease lacks processed request identity.");
            }

            _active = new ReadOnlyDictionary<string, AudioVoiceLease>(activeMap);
            _lastStartByCue = new ReadOnlyDictionary<string, long>(last);
            _processedSignatures =
                new ReadOnlyDictionary<string, string>(processed);
        }
    }

    public sealed class AudioConcurrencyPlan
    {
        private readonly ReadOnlyCollection<string> _cancelledRequestIds;

        public AudioConcurrencySnapshot Result { get; }
        public bool Applied { get; }
        public bool Started { get; }
        public string SuppressionReason { get; }
        public IReadOnlyList<string> CancelledRequestIds =>
            _cancelledRequestIds;
        public int MusicDuckPercent { get; }
        public int MusicCrossfadeMilliseconds { get; }

        internal AudioConcurrencyPlan(AudioConcurrencySnapshot result,
            bool applied, bool started, string suppressionReason,
            IEnumerable<string> cancelledRequestIds,
            int musicDuckPercent, int musicCrossfadeMilliseconds)
        {
            Result = result;
            Applied = applied;
            Started = started;
            SuppressionReason = suppressionReason;
            _cancelledRequestIds = new ReadOnlyCollection<string>(
                cancelledRequestIds.OrderBy(x => x, StringComparer.Ordinal).ToList());
            MusicDuckPercent = musicDuckPercent;
            MusicCrossfadeMilliseconds = musicCrossfadeMilliseconds;
        }
    }

    public static class AudioConcurrencyPlanner
    {
        public static AudioConcurrencyPlan Request(
            AudioConcurrencySnapshot snapshot,
            AudioConcurrencyPolicy policy,
            AudioVoiceRequest request)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            if (policy == null) throw new ArgumentNullException(nameof(policy));
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (snapshot.Active.Count > policy.MaximumGlobalVoices)
                throw new InvalidOperationException(
                    "Audio active state exceeds global voice policy.");

            AudioVoiceRule rule;
            if (!policy.Rules.TryGetValue(request.CueKey, out rule))
                throw new ArgumentException(
                    "Audio request references an unconfigured cue.",
                    nameof(request));
            if (request.Kind != rule.Kind)
                throw new InvalidOperationException(
                    "Audio request kind does not match the configured cue kind.");

            string existing;
            if (snapshot.ProcessedSignatures.TryGetValue(
                request.RequestId, out existing))
            {
                if (!StringComparer.Ordinal.Equals(existing, request.Signature))
                    throw new InvalidOperationException(
                        "Audio request ID is bound to another payload.");
                return new AudioConcurrencyPlan(
                    snapshot, false, false, null,
                    new string[0], 0, 0);
            }

            var processed = snapshot.ProcessedSignatures.ToDictionary(
                x => x.Key, x => x.Value, StringComparer.Ordinal);
            if (processed.Count >= 100000)
                throw new InvalidOperationException(
                    "Audio request ledger limit reached.");
            processed.Add(request.RequestId, request.Signature);

            var active = snapshot.Active.ToDictionary(
                x => x.Key, x => x.Value, StringComparer.Ordinal);
            var last = snapshot.LastStartByCue.ToDictionary(
                x => x.Key, x => x.Value, StringComparer.Ordinal);

            long lastStarted;
            if (last.TryGetValue(request.CueKey, out lastStarted))
            {
                if (request.TimestampMilliseconds < lastStarted)
                    throw new InvalidOperationException(
                        "Audio request clock rollback detected.");
                if (request.TimestampMilliseconds - lastStarted <
                    rule.CooldownMilliseconds)
                {
                    return new AudioConcurrencyPlan(
                        new AudioConcurrencySnapshot(
                            active.Values, last, processed),
                        true, false, "cooldown",
                        new string[0], 0, 0);
                }
            }

            var cancelled = new HashSet<string>(StringComparer.Ordinal);
            var crossfade = 0;

            if (request.Kind == AudioCueKind.Music)
            {
                foreach (var lease in active.Values
                    .Where(x => x.Kind == AudioCueKind.Music)
                    .ToArray())
                {
                    active.Remove(lease.RequestId);
                    cancelled.Add(lease.RequestId);
                }
                if (cancelled.Count > 0)
                    crossfade = policy.MusicCrossfadeMilliseconds;
            }

            var sameCue = active.Values
                .Where(x => StringComparer.Ordinal.Equals(
                    x.CueKey, request.CueKey))
                .OrderBy(x => x.Priority)
                .ThenBy(x => x.StartedAtMilliseconds)
                .ThenBy(x => x.RequestId, StringComparer.Ordinal)
                .ToList();

            while (sameCue.Count >= rule.MaximumVoices)
            {
                var victim = sameCue[0];
                if (request.Kind != AudioCueKind.Music &&
                    request.TimestampMilliseconds >= victim.StartedAtMilliseconds &&
                    rule.Priority <= victim.Priority)
                {
                    return new AudioConcurrencyPlan(
                        new AudioConcurrencySnapshot(
                            active.Values, last, processed),
                        true, false, "per-cue-cap",
                        cancelled, 0, crossfade);
                }
                active.Remove(victim.RequestId);
                cancelled.Add(victim.RequestId);
                sameCue.RemoveAt(0);
            }

            while (active.Count >= policy.MaximumGlobalVoices)
            {
                var victim = active.Values
                    .Where(x => x.Kind != AudioCueKind.Music)
                    .OrderBy(x => x.Priority)
                    .ThenBy(x => x.StartedAtMilliseconds)
                    .ThenBy(x => x.RequestId, StringComparer.Ordinal)
                    .FirstOrDefault();

                if (victim == null || rule.Priority <= victim.Priority)
                {
                    return new AudioConcurrencyPlan(
                        new AudioConcurrencySnapshot(
                            active.Values, last, processed),
                        true, false, "global-cap",
                        cancelled, 0, crossfade);
                }

                active.Remove(victim.RequestId);
                cancelled.Add(victim.RequestId);
            }

            active.Add(
                request.RequestId,
                new AudioVoiceLease(request, rule.Priority));
            last[request.CueKey] = request.TimestampMilliseconds;

            return new AudioConcurrencyPlan(
                new AudioConcurrencySnapshot(
                    active.Values, last, processed),
                true, true, null,
                cancelled,
                rule.DucksMusic ? policy.MusicDuckPercent : 0,
                crossfade);
        }

        public static AudioConcurrencyPlan Release(
            AudioConcurrencySnapshot snapshot, string requestId)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            if (string.IsNullOrWhiteSpace(requestId))
                throw new ArgumentException(
                    "Audio release request ID required.", nameof(requestId));
            if (!snapshot.Active.ContainsKey(requestId))
                return new AudioConcurrencyPlan(
                    snapshot, false, false, null,
                    new string[0], 0, 0);

            var active = snapshot.Active.ToDictionary(
                x => x.Key, x => x.Value, StringComparer.Ordinal);
            active.Remove(requestId);
            return new AudioConcurrencyPlan(
                new AudioConcurrencySnapshot(
                    active.Values,
                    snapshot.LastStartByCue,
                    snapshot.ProcessedSignatures),
                true, false, null,
                new string[0], 0, 0);
        }
    }

    public sealed class HapticPattern
    {
        public string PatternKey { get; }
        public int Intensity { get; }

        public HapticPattern(string patternKey, int intensity)
        {
            if (string.IsNullOrWhiteSpace(patternKey))
                throw new ArgumentException("Haptic pattern key required.", nameof(patternKey));
            if (intensity < 1 || intensity > 100)
                throw new ArgumentOutOfRangeException(nameof(intensity));
            PatternKey = patternKey;
            Intensity = intensity;
        }
    }

    public sealed class HapticPolicy
    {
        public bool Supported { get; }
        public bool Enabled { get; }
        public bool ReducedEffects { get; }
        public int MaximumPerSecond { get; }
        public long MinimumIntervalMilliseconds { get; }

        public HapticPolicy(bool supported, bool enabled,
            bool reducedEffects, int maximumPerSecond,
            long minimumIntervalMilliseconds)
        {
            if (maximumPerSecond < 0 || maximumPerSecond > 100)
                throw new ArgumentOutOfRangeException(nameof(maximumPerSecond));
            if (minimumIntervalMilliseconds < 0 ||
                minimumIntervalMilliseconds > 60000)
                throw new ArgumentOutOfRangeException(
                    nameof(minimumIntervalMilliseconds));
            Supported = supported;
            Enabled = enabled;
            ReducedEffects = reducedEffects;
            MaximumPerSecond = maximumPerSecond;
            MinimumIntervalMilliseconds = minimumIntervalMilliseconds;
        }
    }

    public sealed class HapticSnapshot
    {
        private readonly ReadOnlyDictionary<string, string> _processedSignatures;

        public long WindowSecond { get; }
        public int WindowCount { get; }
        public long? LastTriggeredMilliseconds { get; }
        public IReadOnlyDictionary<string, string> ProcessedSignatures =>
            _processedSignatures;

        public HapticSnapshot(long windowSecond = 0, int windowCount = 0,
            long? lastTriggeredMilliseconds = null,
            IEnumerable<KeyValuePair<string, string>> processedSignatures = null)
        {
            if (windowSecond < 0 || windowCount < 0 || windowCount > 100)
                throw new ArgumentOutOfRangeException(nameof(windowCount));
            if (lastTriggeredMilliseconds.HasValue &&
                lastTriggeredMilliseconds.Value < 0)
                throw new ArgumentOutOfRangeException(
                    nameof(lastTriggeredMilliseconds));

            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var pair in processedSignatures ??
                Enumerable.Empty<KeyValuePair<string, string>>())
            {
                if (string.IsNullOrWhiteSpace(pair.Key) ||
                    string.IsNullOrWhiteSpace(pair.Value) ||
                    map.ContainsKey(pair.Key))
                    throw new ArgumentException("Invalid haptic event ledger.");
                if (map.Count >= 100000)
                    throw new ArgumentException("Haptic event ledger exceeds bound.");
                map.Add(pair.Key, pair.Value);
            }

            WindowSecond = windowSecond;
            WindowCount = windowCount;
            LastTriggeredMilliseconds = lastTriggeredMilliseconds;
            _processedSignatures =
                new ReadOnlyDictionary<string, string>(map);
        }
    }

    public sealed class HapticPlan
    {
        public HapticSnapshot Result { get; }
        public bool Applied { get; }
        public bool Triggered { get; }
        public string SuppressionReason { get; }
        public string PatternKey { get; }
        public int Intensity { get; }

        internal HapticPlan(HapticSnapshot result, bool applied,
            bool triggered, string suppressionReason,
            string patternKey, int intensity)
        {
            Result = result;
            Applied = applied;
            Triggered = triggered;
            SuppressionReason = suppressionReason;
            PatternKey = patternKey;
            Intensity = intensity;
        }
    }

    public static class HapticFeedbackPlanner
    {
        public static HapticPlan Request(HapticSnapshot snapshot,
            HapticPolicy policy, HapticPattern pattern,
            string eventId, long timestampMilliseconds)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            if (policy == null) throw new ArgumentNullException(nameof(policy));
            if (pattern == null) throw new ArgumentNullException(nameof(pattern));
            if (string.IsNullOrWhiteSpace(eventId))
                throw new ArgumentException("Haptic event ID required.", nameof(eventId));
            if (timestampMilliseconds < 0)
                throw new ArgumentOutOfRangeException(nameof(timestampMilliseconds));

            var signature = pattern.PatternKey + "|" +
                pattern.Intensity + "|" + timestampMilliseconds;
            string prior;
            if (snapshot.ProcessedSignatures.TryGetValue(eventId, out prior))
            {
                if (!StringComparer.Ordinal.Equals(prior, signature))
                    throw new InvalidOperationException(
                        "Haptic event ID is bound to another payload.");
                return new HapticPlan(
                    snapshot, false, false, null,
                    pattern.PatternKey, 0);
            }

            if (snapshot.LastTriggeredMilliseconds.HasValue &&
                timestampMilliseconds <
                    snapshot.LastTriggeredMilliseconds.Value)
                throw new InvalidOperationException(
                    "Haptic request clock rollback detected.");

            var processed = snapshot.ProcessedSignatures.ToDictionary(
                x => x.Key, x => x.Value, StringComparer.Ordinal);
            if (processed.Count >= 100000)
                throw new InvalidOperationException(
                    "Haptic event ledger limit reached.");
            processed.Add(eventId, signature);

            var second = timestampMilliseconds / 1000;
            var count = second == snapshot.WindowSecond
                ? snapshot.WindowCount
                : 0;

            string reason = null;
            if (!policy.Supported)
                reason = "unsupported";
            else if (!policy.Enabled)
                reason = "disabled";
            else if (policy.ReducedEffects)
                reason = "reduced-effects";
            else if (policy.MaximumPerSecond == 0)
                reason = "rate-limit";
            else if (count >= policy.MaximumPerSecond)
                reason = "rate-limit";
            else if (snapshot.LastTriggeredMilliseconds.HasValue &&
                timestampMilliseconds -
                    snapshot.LastTriggeredMilliseconds.Value <
                    policy.MinimumIntervalMilliseconds)
                reason = "minimum-interval";

            if (reason != null)
            {
                return new HapticPlan(
                    new HapticSnapshot(
                        second, count,
                        snapshot.LastTriggeredMilliseconds,
                        processed),
                    true, false, reason,
                    pattern.PatternKey, 0);
            }

            return new HapticPlan(
                new HapticSnapshot(
                    second, count + 1,
                    timestampMilliseconds,
                    processed),
                true, true, null,
                pattern.PatternKey, pattern.Intensity);
        }
    }
}
