using System;

namespace Match3.Engine.Runtime.Commerce
{
    /// <summary>
    /// Per-game, per-platform ad network configuration.
    /// Wonder_Pop is permanently ads-disabled (AdsEnabled=false, PlatformAppId="").
    /// </summary>
    public readonly struct AdNetworkConfig
    {
        public string GameId { get; }
        public string PlatformAppId { get; }
        public bool AdsEnabled { get; }

        public AdNetworkConfig(string gameId, string platformAppId, bool adsEnabled)
        {
            if (string.IsNullOrEmpty(gameId))
                throw new ArgumentException("GameId must not be null or empty.", nameof(gameId));
            if (!adsEnabled && platformAppId != string.Empty)
                throw new ArgumentException("PlatformAppId must be empty string when AdsEnabled is false.", nameof(platformAppId));

            GameId = gameId;
            PlatformAppId = platformAppId ?? string.Empty;
            AdsEnabled = adsEnabled;
        }
    }

    /// <summary>
    /// Contract for a one-time, idempotent ad network SDK initializer.
    /// </summary>
    public interface IAdNetworkInitializer
    {
        string NetworkName { get; }
        bool IsInitialized { get; }

        /// <summary>
        /// Initialize the network SDK. Idempotent: a second call after success is a no-op.
        /// Throws <see cref="InvalidOperationException"/> if config.AdsEnabled is false.
        /// </summary>
        void Initialize(AdNetworkConfig config);
    }

    /// <summary>
    /// Stand-in used when no real initializer is registered.
    /// Initialize always throws; IsInitialized is always false.
    /// </summary>
    public sealed class NullAdNetworkInitializer : IAdNetworkInitializer
    {
        public static readonly NullAdNetworkInitializer Instance = new NullAdNetworkInitializer();

        public string NetworkName => "Null";
        public bool IsInitialized => false;

        public void Initialize(AdNetworkConfig config) =>
            throw new InvalidOperationException("NullAdNetworkInitializer cannot initialize an ad network. Register a real IAdNetworkInitializer first.");
    }

    /// <summary>
    /// Process-wide registry for the active <see cref="IAdNetworkInitializer"/>.
    /// Defaults to <see cref="NullAdNetworkInitializer"/>.
    /// </summary>
    public static class AdNetworkInitializerRegistry
    {
        private static IAdNetworkInitializer _current = NullAdNetworkInitializer.Instance;

        public static IAdNetworkInitializer Current => _current;

        public static void Register(IAdNetworkInitializer initializer)
        {
            if (initializer == null) throw new ArgumentNullException(nameof(initializer));
            _current = initializer;
        }

        public static void Reset() => _current = NullAdNetworkInitializer.Instance;

        /// <summary>
        /// The only sanctioned ad-network initialization path. Runs
        /// <see cref="AdEligibilityGate.Check(string, AdConsentStatus, bool)"/> for config.GameId FIRST
        /// (missing/unknown/case-variant game, Wonder_Pop, kid profile and Unknown/Denied consent all fail
        /// closed), then requires config.AdsEnabled and a not-yet-initialized Current, and only then calls
        /// <see cref="IAdNetworkInitializer.Initialize"/>. IsEligible == true means Initialize was called.
        /// </summary>
        public static AdEligibilityResult TryInitialize(AdNetworkConfig config, AdConsentStatus consent, bool isKidProfile)
        {
            var eligibility = AdEligibilityGate.Check(config.GameId, consent, isKidProfile);
            if (!eligibility.IsEligible)
                return eligibility;
            if (!config.AdsEnabled)
                return AdEligibilityResult.Ineligible("AdsDisabledInConfig");
            if (_current.IsInitialized)
                return AdEligibilityResult.Ineligible("AlreadyInitialized");

            _current.Initialize(config);
            return eligibility;
        }

        /// <summary>Consent and kid status unknown: never initializes (fail-closed), always returns false.</summary>
        [Obsolete("Consent and kid status unknown: never initializes, always returns false. Use TryInitialize(config, consent, isKidProfile).")]
        public static bool TryInitialize(AdNetworkConfig config) => false;
    }
}
