using System;
using System.Threading;
using System.Threading.Tasks;

namespace Match3.Engine.Runtime.Startup
{
    /// <summary>
    /// Ordered phases for consent-aware startup. Must execute in declared order;
    /// phases may be skipped (not reordered) when profile gates prevent them.
    /// </summary>
    public enum StartupPhase
    {
        /// <summary>Establish anonymous device/player identity — no tracked data yet.</summary>
        PlatformIdentity = 0,

        /// <summary>Query GDPR/COPPA consent state — gate for all telemetry phases.</summary>
        PrivacyConsent = 1,

        /// <summary>Initialize board engine and gem catalog.</summary>
        EngineSdk = 2,

        /// <summary>Optional OTA level-pack patch via Cloudflare R2; skipped offline.</summary>
        ContentPatch = 3,

        /// <summary>Crash reporter and analytics — only after consent gate passes.</summary>
        CrashAnalytics = 4,

        /// <summary>Optional IAP — only when MonetizationEnabled; Wonder Pop skips.</summary>
        IapInit = 5
    }

    public enum StartupStatus
    {
        Success = 0,
        /// <summary>A required phase failed; offline basic play is still available.</summary>
        PartialSuccess = 1,
        /// <summary>A mandatory phase failed; startup cannot continue.</summary>
        Failed = 2
    }

    public sealed class PhaseResult
    {
        public StartupPhase Phase { get; }
        public bool Skipped { get; }
        public bool Succeeded { get; }
        public string FailureReason { get; }

        private PhaseResult(StartupPhase phase, bool skipped, bool succeeded, string failureReason)
        {
            Phase = phase;
            Skipped = skipped;
            Succeeded = succeeded;
            FailureReason = failureReason;
        }

        public static PhaseResult Ok(StartupPhase phase) =>
            new PhaseResult(phase, false, true, null);

        public static PhaseResult Skip(StartupPhase phase, string reason) =>
            new PhaseResult(phase, true, true, reason);

        public static PhaseResult Fail(StartupPhase phase, string reason) =>
            new PhaseResult(phase, false, false,
                reason ?? throw new ArgumentNullException(nameof(reason)));
    }

    public sealed class StartupResult
    {
        public StartupStatus Status { get; }
        public PhaseResult[] PhaseResults { get; }

        public StartupResult(StartupStatus status, PhaseResult[] phaseResults)
        {
            if (!Enum.IsDefined(typeof(StartupStatus), status))
                throw new ArgumentOutOfRangeException(nameof(status));
            Status = status;
            PhaseResults = phaseResults ??
                throw new ArgumentNullException(nameof(phaseResults));
        }
    }

    /// <summary>
    /// Context supplied to the orchestrator at startup; provides profile and
    /// network/consent state without coupling to Unity or platform SDKs.
    /// </summary>
    public sealed class StartupContext
    {
        public GameProfile Profile { get; }
        public GameBusinessProfile BusinessProfile { get; }
        /// <summary>True when network is reachable at startup time.</summary>
        public bool NetworkAvailable { get; }
        /// <summary>Consent gate implementation; must be non-null.</summary>
        public IConsentGate ConsentGate { get; }
        public IPlayerIdentity PlayerIdentity { get; }
        /// <summary>Null means IAP is not wired; phase is always skipped.</summary>
        public IIapService IapService { get; }

        public StartupContext(
            GameProfile profile,
            GameBusinessProfile businessProfile,
            bool networkAvailable,
            IConsentGate consentGate,
            IPlayerIdentity playerIdentity,
            IIapService iapService = null)
        {
            Profile = profile ?? throw new ArgumentNullException(nameof(profile));
            BusinessProfile = businessProfile ?? throw new ArgumentNullException(nameof(businessProfile));
            ConsentGate = consentGate ?? throw new ArgumentNullException(nameof(consentGate));
            PlayerIdentity = playerIdentity ?? throw new ArgumentNullException(nameof(playerIdentity));
            NetworkAvailable = networkAvailable;
            IapService = iapService;
        }
    }

    /// <summary>
    /// Runs startup phases in order, skipping optional phases according to profile
    /// gates and runtime conditions. Offline-first: network-dependent phases are
    /// skipped gracefully. Consent-first: telemetry phases are skipped when consent
    /// is not given.
    /// </summary>
    public interface IStartupOrchestrator
    {
        Task<StartupResult> RunAsync(StartupContext context, CancellationToken cancellationToken);
    }
}
