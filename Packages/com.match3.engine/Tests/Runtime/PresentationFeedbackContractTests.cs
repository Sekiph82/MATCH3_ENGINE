using System;
using System.Linq;
using Match3.Engine.Runtime;
using NUnit.Framework;

namespace Match3.Engine.Tests
{
    public sealed class PresentationFeedbackContractTests
    {
        private static AudioConcurrencyPolicy AudioPolicy()
        {
            return new AudioConcurrencyPolicy(
                maximumGlobalVoices: 3,
                musicDuckPercent: 40,
                musicCrossfadeMilliseconds: 250,
                rules: new[]
                {
                    new AudioVoiceRule(
                        "music.home", 1, 0, 50),
                    new AudioVoiceRule(
                        "music.battle", 1, 0, 60),
                    new AudioVoiceRule(
                        "sfx.clear", 2, 100, 10),
                    new AudioVoiceRule(
                        "sfx.major", 1, 0, 100, ducksMusic: true)
                });
        }

        [Test]
        public void M09_009_AudioConcurrencyIsBoundedPriorityCooldownReplaySafeAndDucksCrossfadesMusic()
        {
            var policy = AudioPolicy();
            var state = new AudioConcurrencySnapshot();

            var music = AudioConcurrencyPlanner.Request(
                state, policy,
                new AudioVoiceRequest(
                    "music-1", "music.home",
                    AudioCueKind.Music, 0));
            Assert.That(music.Started, Is.True);
            Assert.That(music.Result.Active.Count, Is.EqualTo(1));

            var clear1 = AudioConcurrencyPlanner.Request(
                music.Result, policy,
                new AudioVoiceRequest(
                    "clear-1", "sfx.clear",
                    AudioCueKind.Sfx, 100));
            Assert.That(clear1.Started, Is.True);

            var cooldown = AudioConcurrencyPlanner.Request(
                clear1.Result, policy,
                new AudioVoiceRequest(
                    "clear-too-soon", "sfx.clear",
                    AudioCueKind.Sfx, 150));
            Assert.That(cooldown.Started, Is.False);
            Assert.That(cooldown.SuppressionReason, Is.EqualTo("cooldown"));
            Assert.That(cooldown.Result.Active.Count, Is.EqualTo(2));

            var clear2 = AudioConcurrencyPlanner.Request(
                cooldown.Result, policy,
                new AudioVoiceRequest(
                    "clear-2", "sfx.clear",
                    AudioCueKind.Sfx, 250));
            Assert.That(clear2.Started, Is.True);
            Assert.That(clear2.Result.Active.Count, Is.EqualTo(3));

            var perCue = AudioConcurrencyPlanner.Request(
                clear2.Result, policy,
                new AudioVoiceRequest(
                    "clear-3", "sfx.clear",
                    AudioCueKind.Sfx, 400));
            Assert.That(perCue.Started, Is.False);
            Assert.That(perCue.SuppressionReason, Is.EqualTo("per-cue-cap"));
            Assert.That(perCue.Result.Active.Count, Is.EqualTo(3));

            var major = AudioConcurrencyPlanner.Request(
                perCue.Result, policy,
                new AudioVoiceRequest(
                    "major-1", "sfx.major",
                    AudioCueKind.Sfx, 500));
            Assert.That(major.Started, Is.True);
            Assert.That(major.MusicDuckPercent, Is.EqualTo(40));
            Assert.That(major.Result.Active.Count, Is.EqualTo(3));
            Assert.That(major.CancelledRequestIds.Count, Is.EqualTo(1));
            Assert.That(major.Result.Active.ContainsKey("music-1"), Is.True);

            var newMusic = AudioConcurrencyPlanner.Request(
                major.Result, policy,
                new AudioVoiceRequest(
                    "music-2", "music.battle",
                    AudioCueKind.Music, 600));
            Assert.That(newMusic.Started, Is.True);
            Assert.That(newMusic.CancelledRequestIds, Does.Contain("music-1"));
            Assert.That(newMusic.MusicCrossfadeMilliseconds, Is.EqualTo(250));
            Assert.That(newMusic.Result.Active.Values.Count(
                x => x.Kind == AudioCueKind.Music), Is.EqualTo(1));

            var replay = AudioConcurrencyPlanner.Request(
                newMusic.Result, policy,
                new AudioVoiceRequest(
                    "music-2", "music.battle",
                    AudioCueKind.Music, 600));
            Assert.That(replay.Applied, Is.False);
            Assert.That(replay.Result, Is.SameAs(newMusic.Result));

            Assert.Throws<InvalidOperationException>(() =>
                AudioConcurrencyPlanner.Request(
                    newMusic.Result, policy,
                    new AudioVoiceRequest(
                        "music-2", "music.home",
                        AudioCueKind.Music, 600)));

            Assert.Throws<InvalidOperationException>(() =>
                AudioConcurrencyPlanner.Request(
                    newMusic.Result, policy,
                    new AudioVoiceRequest(
                        "clear-rollback", "sfx.clear",
                        AudioCueKind.Sfx, 200)));

            var released = AudioConcurrencyPlanner.Release(
                newMusic.Result, "major-1");
            Assert.That(released.Applied, Is.True);
            Assert.That(released.Result.Active.ContainsKey("major-1"), Is.False);

            var releaseReplay = AudioConcurrencyPlanner.Release(
                released.Result, "major-1");
            Assert.That(releaseReplay.Applied, Is.False);

            Assert.Throws<ArgumentException>(() =>
                new AudioConcurrencyPolicy(
                    3, 40, 250,
                    new[]
                    {
                        new AudioVoiceRule("dup", 1, 0, 1),
                        new AudioVoiceRule("dup", 1, 0, 2)
                    }));
        }

        [Test]
        public void M09_010_HapticsAreOptionalRateLimitedReplaySafeAndReducedEffectsSuppressesOutput()
        {
            var pattern = new HapticPattern("impact.medium", 60);
            var enabled = new HapticPolicy(
                supported: true,
                enabled: true,
                reducedEffects: false,
                maximumPerSecond: 2,
                minimumIntervalMilliseconds: 200);
            var state = new HapticSnapshot();

            var first = HapticFeedbackPlanner.Request(
                state, enabled, pattern, "h1", 1000);
            Assert.That(first.Triggered, Is.True);
            Assert.That(first.Intensity, Is.EqualTo(60));

            var tooSoon = HapticFeedbackPlanner.Request(
                first.Result, enabled, pattern, "h2", 1100);
            Assert.That(tooSoon.Triggered, Is.False);
            Assert.That(tooSoon.SuppressionReason,
                Is.EqualTo("minimum-interval"));

            var second = HapticFeedbackPlanner.Request(
                tooSoon.Result, enabled, pattern, "h3", 1300);
            Assert.That(second.Triggered, Is.True);

            var rateLimited = HapticFeedbackPlanner.Request(
                second.Result, enabled, pattern, "h4", 1600);
            Assert.That(rateLimited.Triggered, Is.False);
            Assert.That(rateLimited.SuppressionReason,
                Is.EqualTo("rate-limit"));

            var replay = HapticFeedbackPlanner.Request(
                rateLimited.Result, enabled, pattern, "h4", 1600);
            Assert.That(replay.Applied, Is.False);
            Assert.That(replay.Result, Is.SameAs(rateLimited.Result));

            Assert.Throws<InvalidOperationException>(() =>
                HapticFeedbackPlanner.Request(
                    rateLimited.Result, enabled,
                    new HapticPattern("impact.major", 90),
                    "h4", 1600));

            var nextSecond = HapticFeedbackPlanner.Request(
                rateLimited.Result, enabled, pattern, "h5", 2100);
            Assert.That(nextSecond.Triggered, Is.True);
            Assert.That(nextSecond.Result.WindowCount, Is.EqualTo(1));

            var unsupported = HapticFeedbackPlanner.Request(
                new HapticSnapshot(),
                new HapticPolicy(false, true, false, 2, 0),
                pattern, "unsupported", 1000);
            Assert.That(unsupported.Triggered, Is.False);
            Assert.That(unsupported.SuppressionReason, Is.EqualTo("unsupported"));

            var disabled = HapticFeedbackPlanner.Request(
                new HapticSnapshot(),
                new HapticPolicy(true, false, false, 2, 0),
                pattern, "disabled", 1000);
            Assert.That(disabled.Triggered, Is.False);
            Assert.That(disabled.SuppressionReason, Is.EqualTo("disabled"));

            var reduced = HapticFeedbackPlanner.Request(
                new HapticSnapshot(),
                new HapticPolicy(true, true, true, 2, 0),
                pattern, "reduced", 1000);
            Assert.That(reduced.Triggered, Is.False);
            Assert.That(reduced.SuppressionReason,
                Is.EqualTo("reduced-effects"));
            Assert.That(reduced.Intensity, Is.EqualTo(0));

            Assert.Throws<InvalidOperationException>(() =>
                HapticFeedbackPlanner.Request(
                    nextSecond.Result, enabled, pattern,
                    "rollback", 2000));

            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new HapticPattern("bad", 101));
        }
    }
}
