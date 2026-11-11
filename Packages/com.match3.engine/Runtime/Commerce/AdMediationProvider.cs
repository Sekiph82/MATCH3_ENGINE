using System;

namespace Match3.Engine.Runtime.Commerce
{
    /// <summary>
    /// Provider-agnostic contract for ad mediation adapters.
    /// Actual SDK adapters (LevelPlay/IronSource, etc.) implement this in a
    /// separate asmdef excluded from the core package.
    /// See ENG-M14-002-levelplay-mediation-policy.md for installation gating rules.
    /// </summary>
    public interface IAdMediationProvider
    {
        /// <summary>Unique provider identifier, e.g. "LevelPlay" or "Null".</summary>
        string ProviderId { get; }

        /// <summary>True when the underlying SDK is loaded and initialized.</summary>
        bool IsAvailable { get; }

        /// <summary>
        /// One-time initialization. Must not throw if called more than once.
        /// </summary>
        void Initialize(string gameId);

        /// <summary>
        /// Begin loading a rewarded ad. On success calls <see cref="RewardedAdLifecycle.NotifyReady"/>;
        /// on failure calls <see cref="RewardedAdLifecycle.NotifyFailed"/>.
        /// No-op when <see cref="IsAvailable"/> is false.
        /// </summary>
        void RequestRewardedAd(string placementId, RewardedAdLifecycle lifecycle);

        /// <summary>
        /// Show a previously loaded rewarded ad. Drives the lifecycle to
        /// Completed, Skipped, or Failed.
        /// No-op when <see cref="IsAvailable"/> is false.
        /// </summary>
        void ShowRewardedAd(string placementId, RewardedAdLifecycle lifecycle);
    }

    /// <summary>
    /// Disabled stub used when no real mediation SDK is registered.
    /// IsAvailable is always false; requests are immediately failed.
    /// </summary>
    public sealed class NullAdMediationProvider : IAdMediationProvider
    {
        public static readonly NullAdMediationProvider Instance = new NullAdMediationProvider();

        public string ProviderId => "Null";
        public bool IsAvailable => false;

        public void Initialize(string gameId) { /* no-op */ }

        public void RequestRewardedAd(string placementId, RewardedAdLifecycle lifecycle)
        {
            // lifecycle must be in Loading state before NotifyFailed can be called
            lifecycle.RequestLoad();
            lifecycle.NotifyFailed("NullProvider");
        }

        public void ShowRewardedAd(string placementId, RewardedAdLifecycle lifecycle)
        {
            // no-op: lifecycle is already in Failed state from RequestRewardedAd
        }
    }

    /// <summary>
    /// The only sanctioned initialization path for an <see cref="IAdMediationProvider"/>:
    /// <see cref="IAdMediationProvider.Initialize"/> is called only when
    /// <see cref="AdEligibilityGate.Check(string, AdConsentStatus, bool)"/> passes
    /// (catalog game with ads approved, non-kid profile, consent granted or not required).
    /// </summary>
    public static class AdMediationInitializer
    {
        public static AdEligibilityResult TryInitialize(
            string gameId, AdConsentStatus consent, bool isKidProfile, IAdMediationProvider provider)
        {
            if (provider == null) throw new ArgumentNullException(nameof(provider));

            var eligibility = AdEligibilityGate.Check(gameId, consent, isKidProfile);
            if (eligibility.IsEligible)
                provider.Initialize(gameId);
            return eligibility;
        }
    }

    /// <summary>
    /// Process-wide registry for the active <see cref="IAdMediationProvider"/>.
    /// Defaults to <see cref="NullAdMediationProvider"/>; replace at startup by
    /// calling <see cref="Register"/> from the optional SDK adapter assembly.
    /// </summary>
    public static class AdMediationProviderRegistry
    {
        private static IAdMediationProvider _current = NullAdMediationProvider.Instance;

        /// <summary>Returns the currently registered provider (never null).</summary>
        public static IAdMediationProvider Current => _current;

        /// <summary>Replaces the current provider. Throws <see cref="ArgumentNullException"/> if null.</summary>
        public static void Register(IAdMediationProvider provider)
        {
            if (provider == null) throw new ArgumentNullException(nameof(provider));
            _current = provider;
        }

        /// <summary>Restores the registry to <see cref="NullAdMediationProvider"/> (for testing).</summary>
        public static void Reset() => _current = NullAdMediationProvider.Instance;
    }
}
