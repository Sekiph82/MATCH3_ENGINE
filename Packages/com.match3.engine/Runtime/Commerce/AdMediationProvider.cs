using System;

namespace Match3.Engine.Runtime.Commerce
{
    /// <summary>
    /// Provider-agnostic contract for ad mediation adapters.
    /// Actual SDK adapters (LevelPlay/IronSource, etc.) implement this in a
    /// separate asmdef excluded from the core package.
    /// See ENG-M14-002-levelplay-mediation-policy.md for installation gating rules.
    /// ENGINE-INTERNAL USE: game code must not call these members directly. Register the provider with
    /// <see cref="AdMediationProviderRegistry.Register"/>; Initialize is reached only through
    /// <see cref="AdMediationInitializer.TryInitializeRegistered"/>, and RequestRewardedAd/ShowRewardedAd only
    /// through <see cref="AdShowCoordinator.CreateFromRegistry"/> (TryShowRewardedAd / TryShowLoadedRewardedAd), each of which
    /// runs <see cref="AdEligibilityGate.Check(string, AdConsentStatus, bool)"/> immediately before the call.
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
        /// <summary>Game-facing entry point (ENG-M14-002): initializes the registered provider through the gate.</summary>
        public static AdEligibilityResult TryInitializeRegistered(string gameId, AdConsentStatus consent, bool isKidProfile) =>
            TryInitialize(gameId, consent, isKidProfile, AdMediationProviderRegistry.Current);

        // Engine-internal: a public provider-taking overload would require game code to hold the provider.
        internal static AdEligibilityResult TryInitialize(
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
    /// Registration is public; <see cref="Current"/> is engine-internal so game code cannot fetch the provider
    /// and call ShowRewardedAd/Initialize around <see cref="AdEligibilityGate"/>.
    /// </summary>
    public static class AdMediationProviderRegistry
    {
        private static IAdMediationProvider _current = NullAdMediationProvider.Instance;

        /// <summary>Returns the currently registered provider (never null). Engine-internal (ENG-M14-002).</summary>
        internal static IAdMediationProvider Current => _current;

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
