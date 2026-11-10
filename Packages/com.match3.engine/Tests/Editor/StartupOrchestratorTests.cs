// ENG-M13-007 | Startup consent-aware orchestration — EditMode tests
// Verifies phase ordering, consent gating, offline skipping, and per-profile behavior.
using System.Threading;
using System.Threading.Tasks;
using Match3.Engine.Runtime;
using Match3.Engine.Runtime.Startup;
using NUnit.Framework;

namespace Match3.Engine.Tests.Editor
{
    public sealed class StartupOrchestratorTests
    {
        // --------------- minimal in-line stubs (no framework needed) ----------------

        private sealed class AlwaysSucceedIdentity : IPlayerIdentity
        {
            public Task<ServiceResult<string>> GetPlayerIdAsync(
                GameServiceContext context, CancellationToken cancellationToken) =>
                Task.FromResult(ServiceResult<string>.Success("anon-player-id"));
        }

        private sealed class AlwaysFailIdentity : IPlayerIdentity
        {
            public Task<ServiceResult<string>> GetPlayerIdAsync(
                GameServiceContext context, CancellationToken cancellationToken) =>
                Task.FromResult(ServiceResult<string>.Rejected(
                    ServiceResultStatus.Unavailable, "platform-unreachable"));
        }

        private sealed class FixedConsentGate : IConsentGate
        {
            private readonly ConsentDecisionStatus _analytics;
            public FixedConsentGate(ConsentDecisionStatus analytics) =>
                _analytics = analytics;

            public ConsentDecision Evaluate(GameServiceContext context, ConsentPurpose purpose)
            {
                if (purpose == ConsentPurpose.Analytics)
                {
                    if (_analytics == ConsentDecisionStatus.Allowed)
                        return ConsentDecision.Allow();
                    return ConsentDecision.Deny("test-denied");
                }
                return ConsentDecision.Deny("test-other-denied");
            }
        }

        private static StartupContext MakeContext(
            string gameId,
            bool networkAvailable = true,
            ConsentDecisionStatus consent = ConsentDecisionStatus.Allowed,
            IPlayerIdentity identity = null,
            IIapService iap = null)
        {
            var profile = new GameProfile(
                gameId, "placeholder." + gameId.ToLowerInvariant(),
                ProfileAgeRule.General, ProfileContentChannel.Development);
            var bizProfile = GameBusinessProfileCatalog.Require(gameId);
            return new StartupContext(
                profile, bizProfile, networkAvailable,
                new FixedConsentGate(consent),
                identity ?? new AlwaysSucceedIdentity(),
                iap);
        }

        // ---- Test 1 ----------------------------------------------------------------
        [Test]
        public async Task ConsentDenied_CrashAnalyticsPhaseIsSkipped()
        {
            var ctx = MakeContext("Coral_Rescue", consent: ConsentDecisionStatus.Denied);
            var result = await new StartupOrchestrator().RunAsync(ctx, CancellationToken.None);

            Assert.That(result.Status, Is.EqualTo(StartupStatus.Success));
            var analyticsPhase = FindPhase(result, StartupPhase.CrashAnalytics);
            Assert.That(analyticsPhase.Skipped, Is.True,
                "CrashAnalytics must be skipped when consent is denied");
        }

        // ---- Test 2 ----------------------------------------------------------------
        [Test]
        public async Task ConsentGranted_CrashAnalyticsPhaseSucceeds()
        {
            var ctx = MakeContext("Coral_Rescue", consent: ConsentDecisionStatus.Allowed);
            var result = await new StartupOrchestrator().RunAsync(ctx, CancellationToken.None);

            Assert.That(result.Status, Is.EqualTo(StartupStatus.Success));
            var analyticsPhase = FindPhase(result, StartupPhase.CrashAnalytics);
            Assert.That(analyticsPhase.Skipped, Is.False);
            Assert.That(analyticsPhase.Succeeded, Is.True);
        }

        // ---- Test 3 ----------------------------------------------------------------
        [Test]
        public async Task OfflineMode_ContentPatchPhaseIsSkipped_StartupCompletes()
        {
            var ctx = MakeContext("Dino_Islands", networkAvailable: false);
            var result = await new StartupOrchestrator().RunAsync(ctx, CancellationToken.None);

            Assert.That(result.Status, Is.EqualTo(StartupStatus.Success),
                "offline startup must still succeed");
            var patchPhase = FindPhase(result, StartupPhase.ContentPatch);
            Assert.That(patchPhase.Skipped, Is.True,
                "OTA content patch must be skipped when network is unavailable");
        }

        // ---- Test 4 ----------------------------------------------------------------
        [Test]
        public async Task WonderPop_IapPhaseIsSkipped()
        {
            var ctx = MakeContext("Wonder_Pop");
            var result = await new StartupOrchestrator().RunAsync(ctx, CancellationToken.None);

            Assert.That(result.Status, Is.EqualTo(StartupStatus.Success));
            var iapPhase = FindPhase(result, StartupPhase.IapInit);
            Assert.That(iapPhase.Skipped, Is.True,
                "Wonder Pop MonetizationEnabled=false must skip IAP phase");
        }

        // ---- Test 5 ----------------------------------------------------------------
        [Test]
        public async Task IdentityFailure_StartupReturnsFailed()
        {
            var ctx = MakeContext("Garage_Legends", identity: new AlwaysFailIdentity());
            var result = await new StartupOrchestrator().RunAsync(ctx, CancellationToken.None);

            Assert.That(result.Status, Is.EqualTo(StartupStatus.Failed),
                "mandatory identity failure must yield Failed status");
        }

        // ---- Test 6 ----------------------------------------------------------------
        [Test]
        public async Task AllFiveProfiles_SuccessfulStartupWithDefaultOnline()
        {
            var ids = new[]
            {
                "Coral_Rescue", "Garage_Legends", "Dino_Islands",
                "The_Fractured_Realms", "Wonder_Pop"
            };
            var orchestrator = new StartupOrchestrator();

            foreach (var id in ids)
            {
                var ctx = MakeContext(id);
                var result = await orchestrator.RunAsync(ctx, CancellationToken.None);
                Assert.That(result.Status, Is.EqualTo(StartupStatus.Success),
                    id + " must complete startup successfully");
                Assert.That(result.PhaseResults.Length, Is.EqualTo(6),
                    id + " must run all 6 phases (some may be skipped)");
            }
        }

        // ---- Test 7 ----------------------------------------------------------------
        [Test]
        public async Task WonderPop_ConsentDenied_BothAnalyticsAndIapAreSkipped()
        {
            var ctx = MakeContext("Wonder_Pop", consent: ConsentDecisionStatus.Denied);
            var result = await new StartupOrchestrator().RunAsync(ctx, CancellationToken.None);

            Assert.That(result.Status, Is.EqualTo(StartupStatus.Success));
            Assert.That(FindPhase(result, StartupPhase.CrashAnalytics).Skipped, Is.True,
                "analytics must be skipped: consent denied");
            Assert.That(FindPhase(result, StartupPhase.IapInit).Skipped, Is.True,
                "IAP must be skipped: monetization disabled");
        }

        // ---- Test 8 ----------------------------------------------------------------
        [Test]
        public async Task PhasesArePresentInDeclarationOrder()
        {
            var ctx = MakeContext("The_Fractured_Realms");
            var result = await new StartupOrchestrator().RunAsync(ctx, CancellationToken.None);

            Assert.That(result.PhaseResults.Length, Is.EqualTo(6));
            for (int i = 0; i < result.PhaseResults.Length; i++)
                Assert.That((int)result.PhaseResults[i].Phase, Is.EqualTo(i),
                    "phase at index " + i + " must match declared order");
        }

        // ---- helper ----------------------------------------------------------------
        private static PhaseResult FindPhase(StartupResult result, StartupPhase phase)
        {
            foreach (var p in result.PhaseResults)
                if (p.Phase == phase) return p;
            throw new System.InvalidOperationException("Phase not found: " + phase);
        }
    }
}
