// ENG-M13-009: SDK upgrade policy enforcement.
// See Documentation~/ENG-M13-009-sdk-upgrade-policy.md for full change-control procedure.
using System;

namespace Match3.Engine
{
    /// <summary>
    /// Declares pinned SDK/platform versions for MATCH3_ENGINE.
    /// Call Validate() at startup (Dev/Editor builds) to catch accidental editor upgrades early.
    /// </summary>
    public static class SdkVersionPolicy
    {
        // Pinned Unity version. Any change requires a TASKS.md entry + owner approval (Category C).
        public const string UnityVersion = "6000.6.5f1";

        // Android platform targets.
        public const int AndroidMinSdk    = 24;   // Android 7.0+; Play Store 64-bit policy minimum.
        public const int AndroidTargetSdk = 35;   // Android 15; current Play Store submission requirement.

        // iOS deployment target.
        public const string IosDeploymentTarget = "15.0";

        // EDM4U auto-update must be disabled; version managed manually.
        public const string Edm4uAutoUpdatePolicy = "MANUAL_PIN_NO_AUTO_UPDATE";

        // IL2CPP required for all release builds (Android + iOS).
        public const string ReleaseScriptingBackend = "IL2CPP";

        /// <summary>
        /// In Dev/Editor builds, confirms the caller-supplied running version matches the pinned string.
        /// Callers pass UnityEngine.Application.unityVersion (this assembly has noEngineReferences).
        /// In production builds (IL2CPP, non-Development) this is a no-op returning true.
        /// Throws InvalidOperationException on mismatch so mismatches surface at startup, not silently.
        /// </summary>
        /// <returns>true when the pinned version matches or when running in a production build.</returns>
        /// <exception cref="InvalidOperationException">
        /// Thrown in Dev/Editor builds when the running Unity version does not match UnityVersion.
        /// </exception>
        public static bool Validate(string runningUnityVersion)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            string running = runningUnityVersion;
            if (!string.Equals(running, UnityVersion, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"[SdkVersionPolicy] Unity version mismatch. " +
                    $"Pinned: '{UnityVersion}', running: '{running}'. " +
                    $"Revert to the pinned version or follow ENG-M13-009 change-control " +
                    $"(Documentation~/ENG-M13-009-sdk-upgrade-policy.md, Section 8).");
            }
#endif
            return true;
        }
    }
}
