using System;
using System.Collections.Generic;
using Match3.Engine.Runtime;
using NUnit.Framework;

namespace Match3.Engine.Tests
{
    public sealed class PresentationPlatformContractTests
    {
        [Test]
        public void M09_005_ResponsiveLayoutEnforcesSafeAreaTouchTargetsAndTextOverflowPolicy()
        {
            var phone = new ResponsiveLayoutPolicy(
                viewportWidth: 390,
                viewportHeight: 844,
                insetLeft: 10,
                insetTop: 20,
                insetRight: 10,
                insetBottom: 30,
                minimumTouchTarget: 44,
                tabletWidthThreshold: 768);

            Assert.That(phone.DeviceClass,
                Is.EqualTo(PresentationDeviceClass.PhonePortrait));
            Assert.That(phone.SafeArea,
                Is.EqualTo(new PixelRect(10, 20, 370, 794)));

            var valid = ResponsiveLayoutValidator.Validate(
                phone,
                new[]
                {
                    new LayoutElementRequest(
                        "play", new PixelRect(100, 700, 120, 60), true),
                    new LayoutElementRequest(
                        "title", new PixelRect(40, 60, 240, 48), false,
                        "CORAL RESCUE")
                });
            Assert.That(valid.Warnings, Is.Empty);

            Assert.Throws<InvalidOperationException>(() =>
                ResponsiveLayoutValidator.Validate(
                    phone,
                    new[]
                    {
                        new LayoutElementRequest(
                            "unsafe", new PixelRect(0, 20, 50, 50), true)
                    }));

            Assert.Throws<InvalidOperationException>(() =>
                ResponsiveLayoutValidator.Validate(
                    phone,
                    new[]
                    {
                        new LayoutElementRequest(
                            "too-small", new PixelRect(30, 30, 43, 44), true)
                    }));

            Assert.Throws<InvalidOperationException>(() =>
                ResponsiveLayoutValidator.Validate(
                    phone,
                    new[]
                    {
                        new LayoutElementRequest(
                            "long-copy", new PixelRect(30, 30, 80, 32), false,
                            "123456789012345678901234567890")
                    }));

            var overflow = ResponsiveLayoutValidator.Validate(
                phone,
                new[]
                {
                    new LayoutElementRequest(
                        "long-copy", new PixelRect(30, 30, 80, 32), false,
                        "123456789012345678901234567890",
                        allowTextOverflow: true)
                });
            Assert.That(overflow.Warnings,
                Is.EqualTo(new[] { "text-overflow:long-copy" }));

            var tablet = new ResponsiveLayoutPolicy(
                834, 1194, 0, 24, 0, 20);
            Assert.That(tablet.DeviceClass,
                Is.EqualTo(PresentationDeviceClass.TabletPortrait));

            Assert.Throws<ArgumentException>(() =>
                ResponsiveLayoutValidator.Validate(
                    phone,
                    new[]
                    {
                        new LayoutElementRequest(
                            "dup", new PixelRect(20, 40, 50, 50), false),
                        new LayoutElementRequest(
                            "dup", new PixelRect(80, 40, 50, 50), false)
                    }));
        }

        [Test]
        public void M09_006_AudioAbstractionUsesTypedKeysSavedSettingsReplaySafetyAndMissingCueSuppression()
        {
            var catalog = new ThemeAudioCatalog(
                new[]
                {
                    new ThemeAudioBinding(
                        "music.home", AudioCueKind.Music, "audio.home"),
                    new ThemeAudioBinding(
                        "sfx.clear", AudioCueKind.Sfx, "audio.clear")
                });

            var initial = new AudioSettingsSnapshot();
            var changed = AudioSettingsPlanner.Apply(
                initial, "settings-1",
                masterVolume: 80,
                musicVolume: 50,
                sfxVolume: 25,
                muted: false);
            Assert.That(changed.Applied, Is.True);
            Assert.That(changed.Result.MasterVolume, Is.EqualTo(80));

            var music = AudioProjectionPlanner.Resolve(
                catalog, changed.Result, "music.home");
            Assert.That(music.Kind, Is.EqualTo(AudioCueKind.Music));
            Assert.That(music.AssetKey, Is.EqualTo("audio.home"));
            Assert.That(music.EffectiveVolume, Is.EqualTo(40));
            Assert.That(music.Suppressed, Is.False);

            var sfx = AudioProjectionPlanner.Resolve(
                catalog, changed.Result, "sfx.clear");
            Assert.That(sfx.EffectiveVolume, Is.EqualTo(20));

            var replay = AudioSettingsPlanner.Apply(
                changed.Result, "settings-1", 80, 50, 25, false);
            Assert.That(replay.Applied, Is.False);

            Assert.Throws<InvalidOperationException>(() =>
                AudioSettingsPlanner.Apply(
                    changed.Result, "settings-1", 100, 50, 25, false));

            var muted = AudioSettingsPlanner.Apply(
                changed.Result, "settings-2", 80, 50, 25, true);
            var mutedCue = AudioProjectionPlanner.Resolve(
                catalog, muted.Result, "sfx.clear");
            Assert.That(mutedCue.Suppressed, Is.True);
            Assert.That(mutedCue.EffectiveVolume, Is.EqualTo(0));

            var missing = AudioProjectionPlanner.Resolve(
                catalog, changed.Result, "sfx.missing");
            Assert.That(missing.Suppressed, Is.True);
            Assert.That(missing.AssetKey, Is.Null);
            Assert.That(missing.Warning, Is.EqualTo("missing-audio:sfx.missing"));

            Assert.Throws<ArgumentException>(() =>
                new ThemeAudioCatalog(
                    new[]
                    {
                        new ThemeAudioBinding(
                            "dup", AudioCueKind.Sfx, "a"),
                        new ThemeAudioBinding(
                            "dup", AudioCueKind.Music, "b")
                    }));

            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new AudioSettingsSnapshot(masterVolume: 101));
        }

        [Test]
        public void M09_007_FallbackResolverNeverNeedsGameplayAuthorityAndRejectsMalformedCatalogs()
        {
            var found = PresentationFallbackResolver.Resolve(
                PresentationAssetKind.Sprite,
                "tile.red",
                new[] { "tile.red", "tile.blue" },
                "tile.placeholder");
            Assert.That(found.UsesFallback, Is.False);
            Assert.That(found.ResolvedKey, Is.EqualTo("tile.red"));
            Assert.That(found.Warning, Is.Null);

            var missing = PresentationFallbackResolver.Resolve(
                PresentationAssetKind.Effect,
                "fx.rocket",
                new[] { "fx.clear" },
                "fx.none");
            Assert.That(missing.UsesFallback, Is.True);
            Assert.That(missing.ResolvedKey, Is.EqualTo("fx.none"));
            Assert.That(missing.Warning, Is.EqualTo("missing-effect:fx.rocket"));

            var pluginMissing = PresentationFallbackResolver.Resolve(
                PresentationAssetKind.Plugin,
                "plugin.optional",
                new string[0],
                "plugin.none");
            Assert.That(pluginMissing.UsesFallback, Is.True);
            Assert.That(pluginMissing.ResolvedKey, Is.EqualTo("plugin.none"));

            Assert.Throws<ArgumentException>(() =>
                PresentationFallbackResolver.Resolve(
                    PresentationAssetKind.Audio,
                    "audio.x",
                    new[] { "dup", "dup" },
                    "audio.none"));

            Assert.Throws<ArgumentException>(() =>
                PresentationFallbackResolver.Resolve(
                    PresentationAssetKind.Audio,
                    "audio.x",
                    new[] { "" },
                    "audio.none"));
        }

        [Test]
        public void M09_008_PresentationBudgetsReportDeterministicTierSpecificOveragesWithoutClaimingDeviceProfiling()
        {
            var low = new PresentationBudgetPolicy(
                PresentationBudgetTier.LowEnd,
                maximumSprites: 100,
                maximumActiveEffects: 8,
                maximumAudioVoices: 6,
                maximumHapticsPerSecond: 2,
                maximumModalDepth: 3);

            var within = PresentationBudgetEvaluator.Evaluate(
                low,
                new PresentationCounters(
                    sprites: 100,
                    activeEffects: 8,
                    audioVoices: 6,
                    hapticsPerSecond: 2,
                    modalDepth: 3));
            Assert.That(within.WithinBudget, Is.True);
            Assert.That(within.OverBudgetMetrics, Is.Empty);

            var over = PresentationBudgetEvaluator.Evaluate(
                low,
                new PresentationCounters(
                    sprites: 101,
                    activeEffects: 10,
                    audioVoices: 7,
                    hapticsPerSecond: 4,
                    modalDepth: 5));
            Assert.That(over.WithinBudget, Is.False);
            Assert.That(over.OverBudgetMetrics,
                Is.EqualTo(new[]
                {
                    "audio-voices",
                    "effects",
                    "haptics-per-second",
                    "modal-depth",
                    "sprites"
                }));

            var high = new PresentationBudgetPolicy(
                PresentationBudgetTier.HighEnd,
                1000, 100, 32, 8, 8);
            var sameCounters = PresentationBudgetEvaluator.Evaluate(
                high,
                new PresentationCounters(101, 10, 7, 4, 5));
            Assert.That(sameCounters.WithinBudget, Is.True);

            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new PresentationBudgetPolicy(
                    PresentationBudgetTier.LowEnd,
                    maximumSprites: 0,
                    maximumActiveEffects: 1,
                    maximumAudioVoices: 1,
                    maximumHapticsPerSecond: 1,
                    maximumModalDepth: 1));

            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new PresentationCounters(-1, 0, 0, 0, 0));
        }
    }
}
