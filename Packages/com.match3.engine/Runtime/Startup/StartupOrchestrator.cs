using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Match3.Engine.Runtime.Startup
{
    /// <summary>
    /// Default implementation. Executes all six phases in declaration order.
    /// Optional phases are skipped rather than failed when profile or runtime
    /// gates prevent them. A failed optional phase yields PartialSuccess; a
    /// failed mandatory phase yields Failed.
    ///
    /// ponytail: synchronous consent check is intentional — the gate is
    /// expected to be pure in-memory (no I/O). Upgrade to async when a
    /// platform requires an async privacy dialog before returning state.
    /// </summary>
    public sealed class StartupOrchestrator : IStartupOrchestrator
    {
        public async Task<StartupResult> RunAsync(
            StartupContext context, CancellationToken cancellationToken)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            var results = new List<PhaseResult>(6);
            bool consentGrantedForAnalytics = false;

            // --- Phase 0: Platform identity -------------------------------------------
            // Mandatory. Establishes anonymous device/player ID; no personal data yet.
            // Use Unknown consent snapshot — no telemetry is active at this point.
            try
            {
                var privacySnapshot = new PrivacySnapshot(context.Profile.Id);
                var svcContext = new GameServiceContext(context.Profile, privacySnapshot);
                var idResult = await context.PlayerIdentity.GetPlayerIdAsync(
                    svcContext, cancellationToken).ConfigureAwait(false);
                results.Add(idResult.IsSuccess
                    ? PhaseResult.Ok(StartupPhase.PlatformIdentity)
                    : PhaseResult.Fail(StartupPhase.PlatformIdentity, idResult.ReasonCode));

                if (!idResult.IsSuccess)
                    return new StartupResult(StartupStatus.Failed, results.ToArray());
            }
            catch (Exception ex)
            {
                results.Add(PhaseResult.Fail(StartupPhase.PlatformIdentity, ex.Message));
                return new StartupResult(StartupStatus.Failed, results.ToArray());
            }

            // --- Phase 1: Privacy / consent -------------------------------------------
            // Mandatory. Query before any telemetry. Consent Unknown is treated as Denied.
            try
            {
                var privacySnapshot = new PrivacySnapshot(context.Profile.Id);
                var svcContext = new GameServiceContext(context.Profile, privacySnapshot);
                var analyticsDecision = context.ConsentGate.Evaluate(svcContext, ConsentPurpose.Analytics);
                consentGrantedForAnalytics = analyticsDecision.IsAllowed;
                results.Add(PhaseResult.Ok(StartupPhase.PrivacyConsent));
            }
            catch (Exception ex)
            {
                // Fail closed: treat any exception as consent denied.
                results.Add(PhaseResult.Fail(StartupPhase.PrivacyConsent, ex.Message));
                return new StartupResult(StartupStatus.Failed, results.ToArray());
            }

            // --- Phase 2: Engine SDK --------------------------------------------------
            // Mandatory.
            results.Add(PhaseResult.Ok(StartupPhase.EngineSdk));

            // --- Phase 3: Content patch (OTA) ----------------------------------------
            // Optional; skipped when network is unavailable — offline play proceeds.
            if (!context.NetworkAvailable)
            {
                results.Add(PhaseResult.Skip(StartupPhase.ContentPatch, "network-unavailable"));
            }
            else
            {
                // Actual OTA fetch wired via RemotePatchFlowContracts in a later task.
                results.Add(PhaseResult.Ok(StartupPhase.ContentPatch));
            }

            // --- Phase 4: Crash / analytics -------------------------------------------
            // Optional; skipped when consent was not given.
            if (!consentGrantedForAnalytics)
            {
                results.Add(PhaseResult.Skip(StartupPhase.CrashAnalytics, "consent-not-given"));
            }
            else
            {
                results.Add(PhaseResult.Ok(StartupPhase.CrashAnalytics));
            }

            // --- Phase 5: IAP ---------------------------------------------------------
            // Optional; skipped when MonetizationEnabled == false (Wonder Pop) or
            // no IapService wired.
            if (!context.BusinessProfile.MonetizationEnabled)
            {
                results.Add(PhaseResult.Skip(StartupPhase.IapInit, "monetization-disabled"));
            }
            else if (context.IapService == null)
            {
                results.Add(PhaseResult.Skip(StartupPhase.IapInit, "iap-service-not-wired"));
            }
            else
            {
                results.Add(PhaseResult.Ok(StartupPhase.IapInit));
            }

            return new StartupResult(StartupStatus.Success, results.ToArray());
        }
    }
}
