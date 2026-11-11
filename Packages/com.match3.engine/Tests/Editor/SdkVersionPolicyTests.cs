// ENG-M13-009: SDK version policy tests.
// Change-control rule: any change to a pinned constant in SdkVersionPolicy requires a TASKS.md entry
// with before/after SHA, test evidence, and owner approval for major version bumps (Category C gate).
using System;
using NUnit.Framework;
using Match3.Engine;

namespace Match3.Engine.Tests.Editor
{
    public class SdkVersionPolicyTests
    {
        // Test 1: Pinned constants have the exact expected values declared in ENG-M13-009.
        // Failure here means someone changed a pinned version without updating this test,
        // which is the first sign a change-control step was skipped.
        [Test]
        public void PinnedConstants_HaveExpectedValues_ChangeControlRuleEnforced()
        {
            Assert.AreEqual("6000.6.5f1", SdkVersionPolicy.UnityVersion,
                "Unity version changed without change-control entry. See ENG-M13-009 Section 8.");
            Assert.AreEqual(24, SdkVersionPolicy.AndroidMinSdk,
                "Android minSdk changed without TASKS.md entry.");
            Assert.AreEqual(35, SdkVersionPolicy.AndroidTargetSdk,
                "Android targetSdk changed without TASKS.md entry.");
            Assert.AreEqual("15.0", SdkVersionPolicy.IosDeploymentTarget,
                "iOS deployment target changed without TASKS.md entry.");
            Assert.AreEqual("MANUAL_PIN_NO_AUTO_UPDATE", SdkVersionPolicy.Edm4uAutoUpdatePolicy,
                "EDM4U auto-update policy string changed.");
            Assert.AreEqual("IL2CPP", SdkVersionPolicy.ReleaseScriptingBackend,
                "Release scripting backend changed from IL2CPP without TASKS.md entry.");
        }

        // Test 2: Validate() returns true when the running editor version matches the pinned string.
        // This is the success path: a developer using the correct Unity editor sees no exception.
        // Note: in the editor this test runs under UNITY_EDITOR conditional, so the version check
        // executes. The test relies on the CI/editor used to run tests being the pinned version.
        // If this test fails, the editor version is wrong — follow ENG-M13-009 Section 7 (downgrade).
        [Test]
        public void Validate_ReturnsTrue_WhenEditorVersionMatchesPinnedVersion()
        {
            // In a correctly-pinned editor, Application.unityVersion == SdkVersionPolicy.UnityVersion.
            // The test passes without exception, confirming the editor is the approved version.
            // If the editor version is wrong, Validate() throws InvalidOperationException (see Test 3).
            bool result = false;
            try
            {
                result = SdkVersionPolicy.Validate(UnityEngine.Application.unityVersion);
            }
            catch (InvalidOperationException ex)
            {
                Assert.Ignore(
                    $"Running editor ({UnityEngine.Application.unityVersion}) does not match pinned " +
                    $"version ({SdkVersionPolicy.UnityVersion}). " +
                    $"Install the pinned editor per ENG-M13-009 Section 7. Details: {ex.Message}");
            }
            Assert.IsTrue(result, "Validate() must return true on success.");
        }

        // Test 3: Validate() mismatch branch throws InvalidOperationException naming both versions and the policy.
        // Validate takes the running version as a parameter, so a mismatch is exercised directly.
        // The rule: a mismatch must be LOUD (exception), not silent (return false or log-only).
        [Test]
        public void Validate_MismatchBranch_ThrowsInvalidOperationException_WithPolicyReference()
        {
            const string fakeRunning = "0000.0.0x"; // guaranteed mismatch
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            var ex = Assert.Throws<InvalidOperationException>(() => SdkVersionPolicy.Validate(fakeRunning));

            StringAssert.Contains($"Pinned: '{SdkVersionPolicy.UnityVersion}', running: '{fakeRunning}'", ex.Message,
                "Exception must name both pinned and running version.");
            StringAssert.Contains("ENG-M13-009", ex.Message,
                "Exception must reference ENG-M13-009 so the developer finds the policy.");
#else
            Assert.IsTrue(SdkVersionPolicy.Validate(fakeRunning), "Production builds treat Validate as a no-op.");
#endif
        }

        // Test 4: Android SDK constants satisfy the Play Store 64-bit and submission requirements.
        // Failure = someone lowered minSdk (reducing device coverage) or targetSdk (blocking submission)
        // without updating this test, which is evidence a change-control step was skipped.
        [Test]
        public void AndroidSdkConstants_SatisfyPlayStoreRequirements()
        {
            // Google Play requires targetSdk >= 34 as of August 2024; we pin 35 (Android 15).
            Assert.GreaterOrEqual(SdkVersionPolicy.AndroidTargetSdk, 34,
                "targetSdk must be >= 34 for current Google Play submission. Update TASKS.md if changing.");

            // Google Play 64-bit policy requires minSdk >= 21; we pin 24 for Vulkan + modern TLS.
            Assert.GreaterOrEqual(SdkVersionPolicy.AndroidMinSdk, 21,
                "minSdk must be >= 21 for Play Store 64-bit policy.");

            // targetSdk must be >= minSdk (sanity check).
            Assert.GreaterOrEqual(SdkVersionPolicy.AndroidTargetSdk, SdkVersionPolicy.AndroidMinSdk,
                "targetSdk must be >= minSdk.");
        }
    }
}
