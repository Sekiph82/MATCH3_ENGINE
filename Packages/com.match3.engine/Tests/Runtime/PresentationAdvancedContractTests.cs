using System;
using System.Collections.Generic;
using System.Linq;
using Match3.Engine.Runtime;
using NUnit.Framework;

namespace Match3.Engine.Tests
{
    public sealed class PresentationAdvancedContractTests
    {
        private static UiSurfaceState[] CompleteSurfaceStates()
        {
            return Enum.GetValues(typeof(UiSurfaceKind))
                .Cast<UiSurfaceKind>()
                .Select(surface =>
                {
                    UiActionDefinition action;
                    if (surface == UiSurfaceKind.Home)
                    {
                        action = new UiActionDefinition(
                            "action-" + surface,
                            surface,
                            UiActionKind.Navigate,
                            visible: true,
                            enabled: true,
                            destination: UiSurfaceKind.WorldMap);
                    }
                    else if (surface == UiSurfaceKind.Hud)
                    {
                        action = new UiActionDefinition(
                            "action-" + surface,
                            surface,
                            UiActionKind.Command,
                            visible: true,
                            enabled: true,
                            commandKey: "hud.pause");
                    }
                    else
                    {
                        action = new UiActionDefinition(
                            "action-" + surface,
                            surface,
                            UiActionKind.Close,
                            visible: true,
                            enabled: true);
                    }

                    return new UiSurfaceState(
                        "state-" + surface,
                        surface,
                        new[] { action });
                })
                .ToArray();
        }

        private static PresentationTimingPolicy TimingPolicy()
        {
            return new PresentationTimingPolicy(
                new[]
                {
                    new PresentationTimingEnvelope(
                        PresentationIntensity.Small,
                        100, 2, 1, false),
                    new PresentationTimingEnvelope(
                        PresentationIntensity.Medium,
                        250, 4, 2, true),
                    new PresentationTimingEnvelope(
                        PresentationIntensity.MajorUnlock,
                        800, 8, 4, true)
                },
                reducedMotionMaximumMilliseconds: 120);
        }

        private static ResponsiveLayoutPolicy PhoneLayout()
        {
            return new ResponsiveLayoutPolicy(
                viewportWidth: 390,
                viewportHeight: 844,
                insetLeft: 10,
                insetTop: 20,
                insetRight: 10,
                insetBottom: 30,
                minimumTouchTarget: 44,
                tabletWidthThreshold: 768);
        }

        [Test]
        public void M09_011_AllPlayerSurfacesAreCompleteEscapableAndDoubleSubmitSafe()
        {
            var states = CompleteSurfaceStates();
            var report = UiCompletenessValidator.Validate(states);

            Assert.That(report.Surfaces.Count,
                Is.EqualTo(Enum.GetValues(typeof(UiSurfaceKind)).Length));
            Assert.That(report.VisibleActionCount, Is.EqualTo(states.Length));

            var homeLoading = new UiSurfaceState(
                "state-Home-loading",
                UiSurfaceKind.Home,
                new[]
                {
                    new UiActionDefinition(
                        "loading-help",
                        UiSurfaceKind.Home,
                        UiActionKind.Command,
                        visible: true,
                        enabled: true,
                        commandKey: "home.loading.help")
                });
            var multiStateReport = UiCompletenessValidator.Validate(
                states.Concat(new[] { homeLoading }));
            Assert.That(multiStateReport.Surfaces.Count,
                Is.EqualTo(report.Surfaces.Count));

            var home = states.Single(x => x.Surface == UiSurfaceKind.Home);
            var route = new UiRouteSnapshot(UiSurfaceKind.Home);
            var activation = new UiActivation(
                "submit-1",
                home.StateId,
                UiSurfaceKind.Home,
                "action-Home");

            var first = UiRouter.Activate(route, home, activation);
            Assert.That(first.Applied, Is.True);
            Assert.That(first.Result.CurrentSurface,
                Is.EqualTo(UiSurfaceKind.WorldMap));

            var replay = UiRouter.Activate(
                first.Result, home, activation);
            Assert.That(replay.Applied, Is.False);
            Assert.That(replay.Result, Is.SameAs(first.Result));

            Assert.Throws<InvalidOperationException>(() =>
                UiCompletenessValidator.Validate(
                    states.Where(x => x.Surface != UiSurfaceKind.Store)));

            Assert.Throws<ArgumentException>(() =>
                UiCompletenessValidator.Validate(
                    states.Concat(
                        new[]
                        {
                            new UiSurfaceState(
                                states[0].StateId,
                                UiSurfaceKind.Home,
                                new[]
                                {
                                    new UiActionDefinition(
                                        "duplicate-state-id",
                                        UiSurfaceKind.Home,
                                        UiActionKind.Command,
                                        true, true,
                                        commandKey: "x")
                                })
                        })));

            var deadShop = new UiSurfaceState(
                "dead-shop",
                UiSurfaceKind.Shop,
                new[]
                {
                    new UiActionDefinition(
                        "buy",
                        UiSurfaceKind.Shop,
                        UiActionKind.Command,
                        visible: true,
                        enabled: true,
                        commandKey: "shop.buy")
                });
            Assert.Throws<InvalidOperationException>(() =>
                UiCompletenessValidator.Validate(
                    states.Where(x => x.Surface != UiSurfaceKind.Shop)
                        .Concat(new[] { deadShop })));
        }

        [Test]
        public void M09_012_TimingQualityKeepsCeremonyOrderAndTerminalParityAcrossSkipAndReducedMotion()
        {
            var policy = TimingPolicy();
            var steps = new[]
            {
                new CeremonyStep(
                    "score", 1,
                    PresentationIntensity.Small,
                    "ceremony.score"),
                new CeremonyStep(
                    "chest", 2,
                    PresentationIntensity.Medium,
                    "ceremony.chest"),
                new CeremonyStep(
                    "unlock", 3,
                    PresentationIntensity.MajorUnlock,
                    "ceremony.unlock")
            };

            var normal = CeremonyPlanner.Project(
                policy,
                new CeremonySnapshot(),
                "ceremony-normal",
                steps,
                skipRequested: false,
                reducedMotion: false);
            Assert.That(normal.Applied, Is.True);
            Assert.That(normal.CompletedStepIds,
                Is.EqualTo(new[] { "score", "chest", "unlock" }));
            Assert.That(normal.DurationMilliseconds, Is.EqualTo(1150));
            Assert.That(normal.EffectBudget, Is.EqualTo(14));
            Assert.That(normal.AudioCueBudget, Is.EqualTo(7));
            Assert.That(normal.HapticsEnabled, Is.True);

            var reduced = CeremonyPlanner.Project(
                policy,
                new CeremonySnapshot(),
                "ceremony-reduced",
                steps,
                skipRequested: false,
                reducedMotion: true);
            Assert.That(reduced.CompletedStepIds,
                Is.EqualTo(normal.CompletedStepIds));
            Assert.That(reduced.DurationMilliseconds, Is.EqualTo(340));
            Assert.That(reduced.DurationMilliseconds,
                Is.LessThan(normal.DurationMilliseconds));
            Assert.That(reduced.EffectBudget, Is.EqualTo(3));
            Assert.That(reduced.EffectBudget,
                Is.LessThan(normal.EffectBudget));
            Assert.That(reduced.HapticsEnabled, Is.False);

            var skipped = CeremonyPlanner.Project(
                policy,
                new CeremonySnapshot(),
                "ceremony-skipped",
                steps,
                skipRequested: true,
                reducedMotion: false);
            Assert.That(skipped.CompletedStepIds,
                Is.EqualTo(normal.CompletedStepIds));
            Assert.That(skipped.DurationMilliseconds, Is.EqualTo(0));
            Assert.That(skipped.EffectBudget, Is.EqualTo(0));
            Assert.That(skipped.AudioCueBudget, Is.EqualTo(0));
            Assert.That(skipped.HapticsEnabled, Is.False);

            var replay = CeremonyPlanner.Project(
                policy,
                normal.Result,
                "ceremony-normal",
                steps,
                skipRequested: false,
                reducedMotion: false);
            Assert.That(replay.Applied, Is.False);

            Assert.Throws<InvalidOperationException>(() =>
                CeremonyPlanner.Project(
                    policy,
                    normal.Result,
                    "ceremony-normal",
                    new[]
                    {
                        new CeremonyStep(
                            "different", 1,
                            PresentationIntensity.Small,
                            "other")
                    },
                    false, false));

            Assert.Throws<ArgumentException>(() =>
                CeremonyPlanner.Project(
                    policy,
                    new CeremonySnapshot(),
                    "bad-order",
                    new[]
                    {
                        new CeremonyStep(
                            "one", 1,
                            PresentationIntensity.Small,
                            "a"),
                        new CeremonyStep(
                            "three", 3,
                            PresentationIntensity.Medium,
                            "b")
                    },
                    false, false));

            Assert.Throws<ArgumentException>(() =>
                new PresentationTimingPolicy(
                    new[]
                    {
                        new PresentationTimingEnvelope(
                            PresentationIntensity.Small, 200, 4, 2, false),
                        new PresentationTimingEnvelope(
                            PresentationIntensity.Medium, 100, 2, 1, false),
                        new PresentationTimingEnvelope(
                            PresentationIntensity.MajorUnlock, 300, 6, 3, true)
                    }));
        }

        [Test]
        public void M09_013_DynamicOverlaysRespectSafeAreaDepthBackAndReplaySafety()
        {
            var layout = PhoneLayout();
            var policy = new DynamicOverlayPolicy(maximumDepth: 2);
            var start = new DynamicOverlaySnapshot();

            var keyboard = new DynamicOverlayRequest(
                "keyboard",
                DynamicOverlayKind.Keyboard,
                new PixelRect(10, 500, 370, 300),
                interactive: false,
                dismissible: true);
            var p1 = DynamicOverlayPlanner.Push(
                layout, policy, start, keyboard, "push-keyboard");
            Assert.That(p1.Applied, Is.True);
            Assert.That(p1.Result.Stack.Count, Is.EqualTo(1));

            var pushReplay = DynamicOverlayPlanner.Push(
                layout, policy, p1.Result,
                keyboard, "push-keyboard");
            Assert.That(pushReplay.Applied, Is.False);

            var originalCulture = System.Globalization.CultureInfo.CurrentCulture;
            try
            {
                System.Globalization.CultureInfo.CurrentCulture =
                    new System.Globalization.CultureInfo("tr-TR");
                var cultureReplay = DynamicOverlayPlanner.Push(
                    layout, policy, p1.Result,
                    new DynamicOverlayRequest(
                        "keyboard",
                        DynamicOverlayKind.Keyboard,
                        new PixelRect(10, 500, 370, 300),
                        interactive: false,
                        dismissible: true),
                    "push-keyboard");
                Assert.That(cultureReplay.Applied, Is.False);
            }
            finally
            {
                System.Globalization.CultureInfo.CurrentCulture =
                    originalCulture;
            }

            var consent = new DynamicOverlayRequest(
                "consent",
                DynamicOverlayKind.Consent,
                new PixelRect(20, 100, 350, 360),
                interactive: true,
                dismissible: true,
                text: "Consent copy");
            var p2 = DynamicOverlayPlanner.Push(
                layout, policy, p1.Result, consent, "push-consent");
            Assert.That(p2.Result.Stack.Count, Is.EqualTo(2));

            var maxDepthReplay = DynamicOverlayPlanner.Push(
                layout, policy, p2.Result, consent, "push-consent");
            Assert.That(maxDepthReplay.Applied, Is.False);
            Assert.That(maxDepthReplay.Result, Is.SameAs(p2.Result));

            Assert.Throws<InvalidOperationException>(() =>
                DynamicOverlayPlanner.Push(
                    layout, policy, p2.Result,
                    new DynamicOverlayRequest(
                        "banner",
                        DynamicOverlayKind.PurchaseBanner,
                        new PixelRect(20, 40, 350, 60),
                        interactive: true,
                        dismissible: false),
                    "push-banner"));

            var back1 = DynamicOverlayPlanner.Back(
                p2.Result, "back-1");
            Assert.That(back1.Applied, Is.True);
            Assert.That(back1.Result.Stack.Single().OverlayId,
                Is.EqualTo("keyboard"));

            var backReplay = DynamicOverlayPlanner.Back(
                back1.Result, "back-1");
            Assert.That(backReplay.Applied, Is.False);

            var back2 = DynamicOverlayPlanner.Back(
                back1.Result, "back-2");
            Assert.That(back2.Applied, Is.True);
            Assert.That(back2.Result.Stack, Is.Empty);

            var emptyReplay = DynamicOverlayPlanner.Back(
                back2.Result, "back-2");
            Assert.That(emptyReplay.Applied, Is.False);

            Assert.Throws<InvalidOperationException>(() =>
                DynamicOverlayPlanner.Push(
                    layout, policy, p1.Result,
                    new DynamicOverlayRequest(
                        "other",
                        DynamicOverlayKind.StoreReturn,
                        new PixelRect(20, 100, 350, 300),
                        interactive: true,
                        dismissible: true),
                    "push-keyboard"));

            Assert.Throws<InvalidOperationException>(() =>
                DynamicOverlayPlanner.Push(
                    layout, policy, start,
                    new DynamicOverlayRequest(
                        "unsafe",
                        DynamicOverlayKind.Modal,
                        new PixelRect(0, 0, 50, 50),
                        interactive: true,
                        dismissible: true),
                    "unsafe-push"));

            var nondismissible = DynamicOverlayPlanner.Push(
                layout, policy, start,
                new DynamicOverlayRequest(
                    "pending-purchase",
                    DynamicOverlayKind.PurchaseBanner,
                    new PixelRect(20, 60, 350, 60),
                    interactive: true,
                    dismissible: false),
                "purchase-push");
            var blockedBack = DynamicOverlayPlanner.Back(
                nondismissible.Result, "purchase-back");
            Assert.That(blockedBack.Applied, Is.False);
            Assert.That(blockedBack.Warning,
                Is.EqualTo("overlay-not-dismissible"));
        }

        [Test]
        public void M09_014_PlayerSurfaceMatrixIsCodeCompleteButFinalGateRequiresOwnerAndBothDevices()
        {
            var bindings = (
                from gameId in GameProfileCatalog.Ids
                from UiSurfaceKind surface in Enum.GetValues(
                    typeof(UiSurfaceKind))
                from PlayerSurfaceStateKind state in Enum.GetValues(
                    typeof(PlayerSurfaceStateKind))
                select new PlayerSurfaceBinding(
                    gameId,
                    surface,
                    state,
                    "master." + gameId + "." + surface + "." + state,
                    new[]
                    {
                        "text." + gameId + "." + surface + "." + state
                    },
                    new[]
                    {
                        "icon." + gameId + "." + surface + "." + state
                    }))
                .ToArray();

            Assert.That(bindings.Length,
                Is.EqualTo(
                    GameProfileCatalog.Ids.Count *
                    Enum.GetValues(typeof(UiSurfaceKind)).Length *
                    Enum.GetValues(typeof(PlayerSurfaceStateKind)).Length));

            var matrix = new PlayerSurfaceMatrix(bindings);
            var codeOnly = PlayerSurfaceGate.Evaluate(
                matrix,
                new PlayerSurfaceApproval(false));
            Assert.That(codeOnly.CodeReady, Is.True);
            Assert.That(codeOnly.FinalApproved, Is.False);

            var ownerIosOnly = PlayerSurfaceGate.Evaluate(
                matrix,
                new PlayerSurfaceApproval(
                    true, new[] { "ios" }));
            Assert.That(ownerIosOnly.OwnerVisualApproved, Is.True);
            Assert.That(ownerIosOnly.IosDeviceApproved, Is.True);
            Assert.That(ownerIosOnly.AndroidDeviceApproved, Is.False);
            Assert.That(ownerIosOnly.FinalApproved, Is.False);

            var explicitFullApproval = PlayerSurfaceGate.Evaluate(
                matrix,
                new PlayerSurfaceApproval(
                    true, new[] { "ios", "android" }));
            Assert.That(explicitFullApproval.FinalApproved, Is.True,
                "Code permits closure only when explicit owner visual and both device signoffs are supplied; the audit must not fabricate them.");

            Assert.Throws<InvalidOperationException>(() =>
                new PlayerSurfaceMatrix(bindings.Take(bindings.Length - 1)));

            Assert.Throws<ArgumentException>(() =>
                new PlayerSurfaceBinding(
                    "Coral_Rescue",
                    UiSurfaceKind.Home,
                    PlayerSurfaceStateKind.Ready,
                    "master.Coral_Rescue.Home.Ready",
                    new string[0],
                    new string[0]));

            Assert.Throws<ArgumentException>(() =>
                new PlayerSurfaceBinding(
                    "Coral_Rescue",
                    UiSurfaceKind.Home,
                    PlayerSurfaceStateKind.Ready,
                    "",
                    new[] { "text.home" },
                    new[] { "icon.home" }));
        }

        [Test]
        public void M09_015_OptionalPluginsAreVersionLicenseLockedFallbackSafeAndShippingBaggageFree()
        {
            var optional = new PresentationPluginManifest(
                "dotween-like",
                "1.2.3",
                "LICENSED-MIT",
                optional: true);

            var missing = PresentationPluginResolver.Resolve(
                optional,
                new KeyValuePair<string, string>[0]);
            Assert.That(missing.Available, Is.False);
            Assert.That(missing.UsesFallback, Is.True);
            Assert.That(missing.Warning,
                Is.EqualTo("plugin-missing:dotween-like"));

            var mismatch = PresentationPluginResolver.Resolve(
                optional,
                new[]
                {
                    new KeyValuePair<string, string>(
                        "dotween-like", "1.2.2")
                });
            Assert.That(mismatch.Available, Is.False);
            Assert.That(mismatch.UsesFallback, Is.True);
            Assert.That(mismatch.Warning,
                Is.EqualTo("plugin-version-mismatch:dotween-like"));

            var exact = PresentationPluginResolver.Resolve(
                optional,
                new[]
                {
                    new KeyValuePair<string, string>(
                        "dotween-like", "1.2.3")
                });
            Assert.That(exact.Available, Is.True);
            Assert.That(exact.UsesFallback, Is.False);

            Assert.Throws<InvalidOperationException>(() =>
                PresentationPluginResolver.Resolve(
                    new PresentationPluginManifest(
                        "required", "2.0.0", "COMMERCIAL",
                        optional: false),
                    new KeyValuePair<string, string>[0]));

            Assert.Throws<ArgumentException>(() =>
                new PresentationPluginManifest(
                    "bad-authority", "1.0.0", "MIT",
                    optional: true,
                    gameplayAuthority: true));

            var clean = PresentationPluginShippingScanner.Scan(
                new[]
                {
                    "Runtime/TweenRuntime.dll",
                    "Runtime/Particles/Pool.cs"
                });
            Assert.That(clean.Pass, Is.True);

            var dirty = PresentationPluginShippingScanner.Scan(
                new[]
                {
                    "Runtime/TweenRuntime.dll",
                    "Examples/Demo.cs",
                    "Examples~/Legacy.cs",
                    "Samples~/Sample.cs",
                    "Editor/Inspector.cs",
                    "Runtime/Debug/Overlay.cs"
                });
            Assert.That(dirty.Pass, Is.False);
            Assert.That(dirty.Violations.Count, Is.EqualTo(5));

            Assert.Throws<ArgumentException>(() =>
                new PresentationPluginManifest(
                    "no-license", "1.0.0", "",
                    optional: true));
        }

        [Test]
        public void M09_016_ScalableAndRasterArtShareNeutralAccessibleContractWithoutProhibitedQmlImport()
        {
            var policy = new SourceArtPolicy(
                maximumRuntimeBytes: 8 * 1024 * 1024,
                maximumRasterUpscale: 2.0,
                allowedLicenses: new[]
                {
                    SourceArtLicenseClass.Original,
                    SourceArtLicenseClass.MIT,
                    SourceArtLicenseClass.CommercialLicensed
                });

            var raster = new SourceArtDescriptor(
                "tile-raster",
                SourceArtKind.RasterAtlas,
                SourceArtLicenseClass.Original,
                512, 512,
                "shape.shell",
                "access.shell",
                1024 * 1024);
            var vector = new SourceArtDescriptor(
                "tile-vector",
                SourceArtKind.ScalableVector,
                SourceArtLicenseClass.MIT,
                512, 512,
                "shape.shell",
                "access.shell",
                128 * 1024);

            var rasterPlan = SourceArtRenderAdapter.Project(
                policy,
                new SourceArtRenderRequest(
                    raster, 1024, 1024));
            var vectorPlan = SourceArtRenderAdapter.Project(
                policy,
                new SourceArtRenderRequest(
                    vector, 2048, 2048));

            Assert.That(rasterPlan.Scalable, Is.False);
            Assert.That(rasterPlan.ScaleX, Is.EqualTo(2.0));
            Assert.That(vectorPlan.Scalable, Is.True);
            Assert.That(vectorPlan.ScaleX, Is.EqualTo(4.0));
            Assert.That(vectorPlan.ShapeKey,
                Is.EqualTo(rasterPlan.ShapeKey));
            Assert.That(vectorPlan.AccessibilityKey,
                Is.EqualTo(rasterPlan.AccessibilityKey));
            Assert.That(rasterPlan.WithinRuntimeBudget, Is.True);
            Assert.That(vectorPlan.WithinRuntimeBudget, Is.True);

            Assert.Throws<InvalidOperationException>(() =>
                SourceArtRenderAdapter.Project(
                    policy,
                    new SourceArtRenderRequest(
                        raster, 2048, 2048)));

            Assert.Throws<InvalidOperationException>(() =>
                SourceArtRenderAdapter.Project(
                    policy,
                    new SourceArtRenderRequest(
                        vector, 2048, 1024)),
                "Scalable rendering must preserve authored shape aspect ratio.");

            Assert.Throws<InvalidOperationException>(() =>
                SourceArtRenderAdapter.Project(
                    policy,
                    new SourceArtRenderRequest(
                        new SourceArtDescriptor(
                            "qml-reference",
                            SourceArtKind.ScalableVector,
                            SourceArtLicenseClass.LgplQmlReferenceOnly,
                            256, 256,
                            "shape.x",
                            "access.x",
                            1024),
                        512, 512)));

            Assert.Throws<InvalidOperationException>(() =>
                SourceArtRenderAdapter.Project(
                    policy,
                    new SourceArtRenderRequest(
                        new SourceArtDescriptor(
                            "embedded-code",
                            SourceArtKind.ScalableVector,
                            SourceArtLicenseClass.Original,
                            256, 256,
                            "shape.x",
                            "access.x",
                            1024,
                            containsExecutableOrQmlCode: true),
                        512, 512)));

            Assert.Throws<InvalidOperationException>(() =>
                SourceArtRenderAdapter.Project(
                    policy,
                    new SourceArtRenderRequest(
                        new SourceArtDescriptor(
                            "too-heavy",
                            SourceArtKind.RasterAtlas,
                            SourceArtLicenseClass.CommercialLicensed,
                            512, 512,
                            "shape.x",
                            "access.x",
                            16 * 1024 * 1024),
                        512, 512)));

            Assert.Throws<ArgumentException>(() =>
                new SourceArtPolicy(
                    1024 * 1024,
                    2.0,
                    new[]
                    {
                        SourceArtLicenseClass.LgplQmlReferenceOnly
                    }));
        }
    }
}
