using System;
using System.Collections.Generic;
using System.Linq;
using Match3.Engine.Runtime;
using Match3.Engine.Runtime.Economy;
using Match3.Engine.Runtime.Identity;
using Match3.Engine.Runtime.LiveOps;
using Match3.Engine.Runtime.Privacy;
using NUnit.Framework;
using M = Match3.Engine.Demo.Tests.AlternativeModesEditModeTests;

namespace Match3.Engine.Demo.Tests
{
    // ENG-M20-015 tournament: default OFF, never Wonder_Pop/child, consent/region/opt-in admission re-checked at
    // every step, single-use server attempt tickets with per-attempt seeds, engine-recomputed score (pinned SDK
    // replay), server wall-time bound, canonical-run copy detection, tie rules, UTC close, rate limits, and prizes
    // granted exactly once through the RewardLedger.
    public sealed class AsyncTournamentEditModeTests
    {
        private const string Game = M.Game;
        private static readonly DateTime T0 = new DateTime(2026, 11, 16, 0, 0, 0, DateTimeKind.Utc);
        private static readonly DateTime End = T0.AddDays(7);
        private const uint SharedSeed = 4242;

        private static TournamentTemplate Template(int maxAttempts = 10, int minIntervalMinutes = 0, uint sharedSeed = 0) =>
            new TournamentTemplate("cup.week47", M.TimeAttack(), T0, End, new[] { "US", "DE", "TR" },
                maxAttempts, TimeSpan.FromMinutes(minIntervalMinutes), 2, "Coins",
                new[] { new TournamentPrizeTier(1, 100), new TournamentPrizeTier(3, 50), new TournamentPrizeTier(10, 10) },
                sharedSeed);

        private static TournamentService Service(int maxAttempts = 10, int minIntervalMinutes = 0, uint sharedSeed = 0)
        {
            var t = Template(maxAttempts, minIntervalMinutes, sharedSeed);
            return new TournamentService(Game, t, M.Sdk(t.Mode));
        }

        private static FeatureFlagRecord TFlag(bool enabled = true, bool consentCheck = true, string game = Game) =>
            FeatureFlagRecord.Create(game, LiveOpsFeature.TournamentMode, 1, enabled, consentCheck);

        private static readonly ChildAccountPolicy OptInPolicy =
            new ChildAccountPolicy(true, true, "Adult opt-in tournament leaderboard with public handle");

        private static TournamentEligibility Elig(ChildProfile? profile = null, ConsentRecord? consent = null,
            string region = "DE", ChildAccountPolicy? social = null) =>
            new TournamentEligibility(profile ?? ChildProfile.Create(Game, AgeCategory.Adult, false),
                consent ?? ConsentRecord.Create(Game, ConsentState.Granted, false, false), region, social ?? OptInPolicy);

        private static string Admit(TournamentService svc, string player, TournamentEligibility e = null,
            bool optIn = true, FeatureFlagRecord? flag = null, FeatureFlagPolicy? policy = null, DateTime? now = null)
        {
            var ok = svc.TryAdmit(player, e ?? Elig(), optIn, player, flag ?? TFlag(),
                policy ?? FeatureFlagPolicy.Default, now ?? T0.AddHours(1), out var reason);
            return ok ? "OK" : reason;
        }

        private static AttemptTicket Ticket(TournamentService svc, string player, DateTime issued)
        {
            var t = svc.IssueAttempt(player, Elig(), TFlag(), FeatureFlagPolicy.Default, issued, out var reason);
            Assert.IsNotNull(t, reason);
            return t;
        }

        private static ModeSession Run(TournamentService svc, AttemptTicket ticket, long stepMs)
        {
            var s = ModeSession.Start(Game, svc.Template.Mode, ticket.Seed, M.Start, M.Sdk(svc.Template.Mode),
                M.Flag(GameModeKind.TimeAttack), FeatureFlagPolicy.Default);
            M.Play(s, M.Start, stepMs, 1000);
            Assert.AreEqual(ModePhase.Ended, s.Phase);
            return s;
        }

        private static TournamentSubmitStatus Submit(TournamentService svc, AttemptTicket ticket,
            IReadOnlyList<ModeInput> inputs, int claimed, DateTime receipt, TournamentEligibility e = null,
            FeatureFlagRecord? flag = null) =>
            svc.Submit(ticket, M.Start, inputs, claimed, receipt, e ?? Elig(), flag ?? TFlag(), FeatureFlagPolicy.Default);

        /// <summary>Ticket at T0+hours, honest run, receipt two minutes later (60 s run).</summary>
        private static TournamentSubmitStatus PlayAndSubmit(TournamentService svc, string player, double hours,
            long stepMs, out ModeSession run)
        {
            var ticket = Ticket(svc, player, T0.AddHours(hours));
            run = Run(svc, ticket, stepMs);
            return Submit(svc, ticket, run.Inputs, run.Score, ticket.ServerIssuedUtc.AddMinutes(2));
        }

        // ---------- gates ----------

        [Test]
        public void Tournament_IsDefaultOff_AndTheRestrictedFlagNeedsAConsentCheck()
        {
            var svc = Service();
            Assert.AreEqual("FeatureFlagDisabled", Admit(svc, "p1", flag: TFlag(enabled: false)));
            Assert.AreEqual("RestrictedFeatureRequiresConsent", Admit(svc, "p1", flag: TFlag(consentCheck: false)));
            Assert.AreEqual("WrongFeatureFlag", Admit(svc, "p1",
                flag: FeatureFlagRecord.Create(Game, LiveOpsFeature.TimedMode, 1, true, true)));
            Assert.AreEqual("GameIdMismatch", Admit(svc, "p1", flag: TFlag(game: "Dino_Islands")));
            // A permissive remote policy still cannot skip the player's own consent.
            Assert.AreEqual("ConsentRequired", Admit(svc, "p1", flag: TFlag(consentCheck: false),
                policy: FeatureFlagPolicy.Permissive("remote"),
                e: Elig(consent: ConsentRecord.Create(Game, ConsentState.NotRequested, false, false))));
            Assert.AreEqual(0, svc.Leaderboard().Count);
            Assert.IsNull(svc.IssueAttempt("p1", Elig(), TFlag(), FeatureFlagPolicy.Default, T0.AddHours(2), out var reason));
            Assert.AreEqual("NotAdmitted", reason);
            Assert.IsTrue(Template().PerAttemptSeed, "per-attempt server seeds are the default");
        }

        [Test]
        public void WonderPop_AndChildProfiles_AreNeverAdmitted()
        {
            var t = Template();
            Assert.Throws<InvalidOperationException>(() => new TournamentService("Wonder_Pop", t, M.Sdk(t.Mode)));
            Assert.IsFalse(FeatureFlagGate.CanEnableFlag("Wonder_Pop", TFlag(game: "Wonder_Pop"),
                FeatureFlagPolicy.Permissive("remote"), out var reason));
            Assert.AreEqual("WonderPopTournamentProhibited", reason);

            var svc = Service();
            Assert.AreEqual("RestrictedProfileProhibited", Admit(svc, "kid",
                Elig(profile: ChildProfile.Create(Game, AgeCategory.MinorWithParent, false))));
            Assert.AreEqual("RestrictedProfileProhibited", Admit(svc, "kid",
                Elig(profile: ChildProfile.Create(Game, AgeCategory.MinorUnverified, false))));
            Assert.AreEqual("RestrictedProfileProhibited", Admit(svc, "kid",
                Elig(profile: ChildProfile.Create(Game, AgeCategory.Adult, true))));
            Assert.AreEqual("MinorProhibited", Admit(svc, "kid",
                Elig(consent: ConsentRecord.Create(Game, ConsentState.Granted, true, false))));
            Assert.AreEqual("GameIdMismatch", Admit(svc, "kid",
                Elig(profile: ChildProfile.Create("Dino_Islands", AgeCategory.Adult, false))));
        }

        [Test]
        public void Admission_NeedsConsent_AllowedRegion_OptIn_SocialPolicy_AndOpenWindow()
        {
            var svc = Service();
            Assert.AreEqual("ConsentRequired", Admit(svc, "p1", Elig(consent: ConsentRecord.Create(Game, ConsentState.Denied, false, false))));
            Assert.AreEqual("ConsentRequired", Admit(svc, "p1", Elig(consent: ConsentRecord.Create(Game, ConsentState.Revoked, false, false))));
            Assert.AreEqual("RestrictedRegionBlocked", Admit(svc, "p1", Elig(consent: ConsentRecord.Create(Game, ConsentState.Granted, false, true))));
            Assert.AreEqual("ConsentGameMismatch", Admit(svc, "p1", Elig(consent: ConsentRecord.Create("Dino_Islands", ConsentState.Granted, false, false))));
            Assert.AreEqual("RegionNotAllowed", Admit(svc, "p1", Elig(region: "FR")));
            Assert.AreEqual("RegionNotAllowed", Admit(svc, "p1", Elig(region: "de")));
            Assert.AreEqual("OptInRequired", Admit(svc, "p1", optIn: false));
            Assert.AreEqual("SocialFeaturesDisabled", Admit(svc, "p1", Elig(social: ChildAccountPolicy.Default)));
            Assert.AreEqual("NotOpen", Admit(svc, "p1", now: T0.AddTicks(-1)));
            Assert.AreEqual("Closed", Admit(svc, "p1", now: End));
            Assert.AreEqual("InvalidPlayerId", Admit(svc, "p|1"));
            Assert.AreEqual("OK", Admit(svc, "p1"));
        }

        // ---------- verification ----------

        [Test]
        public void Score_IsRecomputedFromTheLog_TicketsAreSingleUseAndUnforgeable()
        {
            var svc = Service();
            Admit(svc, "p1");
            var t1 = Ticket(svc, "p1", T0.AddHours(1));
            var run = Run(svc, t1, 1500);
            var log = run.Inputs.ToList();
            var receipt = t1.ServerIssuedUtc.AddMinutes(2);

            Assert.AreEqual(TournamentSubmitStatus.TamperedScore, Submit(svc, t1, log, run.Score + 1, receipt));
            Assert.AreEqual(TournamentSubmitStatus.TicketUsed, Submit(svc, t1, log, run.Score, receipt), "a ticket is consumed once");

            // The log is only valid for the ticket's own server seed.
            var t2 = Ticket(svc, "p1", T0.AddHours(2));
            Assert.AreNotEqual(t1.Seed, t2.Seed);
            Assert.AreNotEqual(TournamentSubmitStatus.Accepted, Submit(svc, t2, log, run.Score, t2.ServerIssuedUtc.AddMinutes(2)));

            var t3 = Ticket(svc, "p1", T0.AddHours(3));
            var forged = new AttemptTicket(t3.TournamentId, t3.PlayerId, t3.AttemptNo, t3.ServerIssuedUtc, t3.Nonce, t1.Seed);
            Assert.AreEqual(TournamentSubmitStatus.UnknownTicket, Submit(svc, forged, log, run.Score, t3.ServerIssuedUtc.AddMinutes(2)));
            var invented = new AttemptTicket("cup.week47", "p1", 9, T0.AddHours(3), "00112233445566778899aabbccddeeff", t1.Seed);
            Assert.AreEqual(TournamentSubmitStatus.UnknownTicket, Submit(svc, invented, log, run.Score, T0.AddHours(4)));
            Assert.AreEqual(TournamentSubmitStatus.UnknownTicket, Submit(svc, null, log, run.Score, T0.AddHours(4)));

            var r3 = Run(svc, t3, 1500);
            var backwards = new List<ModeInput>(r3.Inputs);
            backwards.Insert(3, ModeInput.Of(ModeInputKind.Tick, M.Start));
            Assert.AreEqual(TournamentSubmitStatus.InvalidLog, Submit(svc, t3, backwards, r3.Score, t3.ServerIssuedUtc.AddMinutes(2)));
            var t4 = Ticket(svc, "p1", T0.AddHours(4));
            var r4 = Run(svc, t4, 1500);
            Assert.AreEqual(TournamentSubmitStatus.Incomplete,
                Submit(svc, t4, r4.Inputs.Take(r4.Inputs.Count - 1).ToList(), r4.Score, t4.ServerIssuedUtc.AddMinutes(2)));
            var t5 = Ticket(svc, "p1", T0.AddHours(5));
            Assert.AreEqual(TournamentSubmitStatus.InvalidLog, Submit(svc, t5, null, 0, t5.ServerIssuedUtc.AddMinutes(2)));
            var t6 = Ticket(svc, "p1", T0.AddHours(6));
            var r6 = Run(svc, t6, 1500);
            var paused = new List<ModeInput>();
            for (var i = 0; i < 3; i++)
            {
                paused.Add(ModeInput.Of(ModeInputKind.Pause, M.Start + i));
                paused.Add(ModeInput.Of(ModeInputKind.Resume, M.Start + i));
            }
            paused.AddRange(r6.Inputs);
            Assert.AreEqual(TournamentSubmitStatus.TooManyInterruptions, Submit(svc, t6, paused, r6.Score, t6.ServerIssuedUtc.AddMinutes(2)));

            var t7 = Ticket(svc, "p1", T0.AddHours(7));
            var r7 = Run(svc, t7, 1500);
            Assert.AreEqual(TournamentSubmitStatus.Accepted, Submit(svc, t7, r7.Inputs, r7.Score, t7.ServerIssuedUtc.AddMinutes(2)));
            var board = svc.Leaderboard();
            Assert.AreEqual(1, board.Count);
            Assert.AreEqual("p1", board[0].DisplayHandle);
            Assert.AreEqual(r7.Score, board[0].Score);
            Assert.AreEqual(r7.RunId, board[0].RunId);
            Assert.AreEqual(t7.ServerIssuedUtc.AddMinutes(2), board[0].VerifiedCompletionUtc);
        }

        [Test]
        public void CopiedRuns_AreRejected_EvenWhenShiftedByATickOrOneMillisecond()
        {
            var svc = Service(sharedSeed: SharedSeed);
            foreach (var p in new[] { "p1", "p2" }) Assert.AreEqual("OK", Admit(svc, p));
            Assert.AreEqual(TournamentSubmitStatus.Accepted, PlayAndSubmit(svc, "p1", 1, 1500, out var run));
            var log = run.Inputs.ToList();
            var tickShifted = new[] { ModeInput.Of(ModeInputKind.Tick, M.Start + 1) }.Concat(log).ToList();
            var msShifted = log.Select(i => i.Kind == ModeInputKind.Swap
                ? ModeInput.Swap(i.AtMs + 1, i.From, i.To) : ModeInput.Of(i.Kind, i.AtMs + 1)).ToList();

            foreach (var copy in new[] { log, tickShifted, msShifted })
            {
                var t = Ticket(svc, "p2", T0.AddHours(2));
                Assert.AreEqual(SharedSeed, t.Seed);
                Assert.AreEqual(TournamentSubmitStatus.Copied, Submit(svc, t, copy, run.Score, t.ServerIssuedUtc.AddMinutes(2)));
            }
            var own = Ticket(svc, "p1", T0.AddHours(3));
            Assert.AreEqual(TournamentSubmitStatus.DuplicateRun, Submit(svc, own, msShifted, run.Score, own.ServerIssuedUtc.AddMinutes(2)));
            Assert.AreEqual(1, svc.Leaderboard().Count);
        }

        [Test]
        public void ServerWallTime_BoundsTheRun_AndCompressedSwapsAreRejected()
        {
            var svc = Service();
            Admit(svc, "p1");
            var t1 = Ticket(svc, "p1", T0.AddHours(1));
            var run = Run(svc, t1, 1500); // 60 s of active time
            Assert.AreEqual(TournamentSubmitStatus.TooFast, Submit(svc, t1, run.Inputs, run.Score, t1.ServerIssuedUtc.AddSeconds(30)));

            var t2 = Ticket(svc, "p1", T0.AddHours(2));
            var r2 = Run(svc, t2, 1500);
            Assert.AreEqual(TournamentSubmitStatus.Accepted,
                Submit(svc, t2, r2.Inputs, r2.Score, t2.ServerIssuedUtc.AddMilliseconds(58500)), "within 2 s tolerance");

            // Same swaps squeezed 10 ms apart: the session refuses them (MinSwapIntervalMs 150).
            var t3 = Ticket(svc, "p1", T0.AddHours(3));
            var r3 = Run(svc, t3, 1500);
            var swaps = r3.Inputs.Where(i => i.Kind == ModeInputKind.Swap).ToList();
            var squeezed = swaps.Select((i, n) => ModeInput.Swap(M.Start + 10 * (n + 1), i.From, i.To)).ToList();
            squeezed.Add(ModeInput.Of(ModeInputKind.Tick, M.Start + 60000));
            Assert.AreEqual(TournamentSubmitStatus.InvalidLog, Submit(svc, t3, squeezed, r3.Score, T0.AddHours(4)));
        }

        // ---------- ranking ----------

        /// <summary>Distinct runs on the shared seed that differ only in the last swap and tie on score.</summary>
        private static List<List<ModeInput>> EqualScoreRuns(TournamentService svc, ModeSession baseRun, int needed)
        {
            var inputs = baseRun.Inputs.ToList();
            var last = inputs.FindLastIndex(i => i.Kind == ModeInputKind.Swap);
            var prefix = inputs.Take(last).ToList();
            var tail = inputs.Skip(last + 1).ToList();
            var at = inputs[last].AtMs;
            var before = ModeSession.Replay(Game, svc.Template.Mode, SharedSeed, M.Start, prefix, M.Sdk(svc.Template.Mode));
            var legal = new NeutralSdkSnapshotValidator(new BoardTopology(6, 6)).FindRealLegalMoves(before.Board).Accepted;
            var byScore = new Dictionary<int, List<List<ModeInput>>>();
            foreach (var move in legal)
            {
                var log = prefix.Concat(new[] { ModeInput.Swap(at, move.Source, move.Target) }).Concat(tail).ToList();
                var s = ModeSession.Replay(Game, svc.Template.Mode, SharedSeed, M.Start, log, M.Sdk(svc.Template.Mode));
                if (s.Phase != ModePhase.Ended) continue;
                if (!byScore.TryGetValue(s.Score, out var group)) byScore[s.Score] = group = new List<List<ModeInput>>();
                group.Add(log);
            }
            var best = byScore.Values.OrderByDescending(g => g.Count).First();
            Assert.IsTrue(best.Count >= needed, "fixture needs " + needed + " equal-score alternatives");
            return best.Take(needed).ToList();
        }

        [Test]
        public void TieRules_ScoreDesc_ThenEarliestVerifiedCompletion_ThenRunIdOrdinal()
        {
            var svc = Service(sharedSeed: SharedSeed);
            foreach (var p in new[] { "p1", "p2", "p3", "p4" }) Assert.AreEqual("OK", Admit(svc, p));
            var probe = Run(svc, new AttemptTicket("cup.week47", "probe", 1, T0, "probe", SharedSeed), 1500);
            var tied = EqualScoreRuns(svc, probe, 3);
            var tiedScore = ModeSession.Replay(Game, svc.Template.Mode, SharedSeed, M.Start, tied[0], M.Sdk(svc.Template.Mode)).Score;

            // p4: same opening, fewer swaps -> strictly lower score, but submitted first.
            var t4 = Ticket(svc, "p4", T0.AddHours(1));
            var shorter = Run(svc, t4, 3000);
            Assert.IsTrue(shorter.Score < tiedScore);
            Assert.AreEqual(TournamentSubmitStatus.Accepted, Submit(svc, t4, shorter.Inputs, shorter.Score, T0.AddHours(1).AddMinutes(2)));

            var t1 = Ticket(svc, "p1", T0.AddHours(2));
            var t2 = Ticket(svc, "p2", T0.AddHours(2));
            var t3 = Ticket(svc, "p3", T0.AddHours(2));
            Assert.AreEqual(TournamentSubmitStatus.Accepted, Submit(svc, t1, tied[0], tiedScore, T0.AddHours(2).AddMinutes(2)));
            Assert.AreEqual(TournamentSubmitStatus.Accepted, Submit(svc, t2, tied[1], tiedScore, T0.AddHours(2).AddMinutes(3)));
            Assert.AreEqual(TournamentSubmitStatus.Accepted, Submit(svc, t3, tied[2], tiedScore, T0.AddHours(2).AddMinutes(3)));

            var board = svc.Leaderboard();
            Assert.AreEqual("p1", board[0].DisplayHandle, "equal score, earliest verified completion wins");
            Assert.AreEqual(tiedScore, board[1].Score);
            Assert.AreEqual(tiedScore, board[2].Score);
            Assert.AreEqual(board[1].VerifiedCompletionUtc, board[2].VerifiedCompletionUtc);
            Assert.IsTrue(string.CompareOrdinal(board[1].RunId, board[2].RunId) < 0, "same instant: run ID ordinal");
            Assert.AreEqual("p4", board[3].DisplayHandle, "lower score ranks last despite the earliest submission");
            Assert.AreEqual(new[] { 1, 2, 3, 4 }, board.Select(s => s.Rank).ToArray());
        }

        // ---------- window, rate limit, kill switch ----------

        [Test]
        public void Window_ClosesExactlyAtUtcEnd()
        {
            var svc = Service();
            Admit(svc, "p1");
            var t1 = Ticket(svc, "p1", End.AddMinutes(-10));
            var run = Run(svc, t1, 1500);
            Assert.AreEqual(TournamentSubmitStatus.Closed, Submit(svc, t1, run.Inputs, run.Score, End));
            Assert.AreEqual(TournamentSubmitStatus.Accepted, Submit(svc, t1, run.Inputs, run.Score, End.AddTicks(-1)));
            Assert.IsNull(svc.IssueAttempt("p1", Elig(), TFlag(), FeatureFlagPolicy.Default, End, out var reason));
            Assert.AreEqual("Closed", reason);
            Assert.AreEqual("Closed", Admit(svc, "p3", now: End));
            Assert.IsNull(svc.Close(TrustedUtcAnchor.Server(End.AddSeconds(-1)), TFlag(), FeatureFlagPolicy.Default,
                _ => Elig(), _ => null));
            Assert.IsFalse(svc.IsClosed);
            Assert.Throws<ArgumentException>(() => svc.Close(TrustedUtcAnchor.DeviceClock(End), TFlag(),
                FeatureFlagPolicy.Default, _ => Elig(), _ => null));
            Assert.Throws<ArgumentException>(() => svc.Submit(t1, M.Start, run.Inputs, run.Score, DateTime.Now, Elig(),
                TFlag(), FeatureFlagPolicy.Default));
        }

        [Test]
        public void RateLimit_AppliesToAttemptTickets()
        {
            var svc = Service(maxAttempts: 2, minIntervalMinutes: 10);
            Admit(svc, "p1");
            Assert.IsNotNull(svc.IssueAttempt("p1", Elig(), TFlag(), FeatureFlagPolicy.Default, T0.AddHours(1), out _));
            Assert.IsNull(svc.IssueAttempt("p1", Elig(), TFlag(), FeatureFlagPolicy.Default, T0.AddHours(1).AddMinutes(9), out var reason));
            Assert.AreEqual("RateLimited", reason);
            Assert.IsNotNull(svc.IssueAttempt("p1", Elig(), TFlag(), FeatureFlagPolicy.Default, T0.AddHours(1).AddMinutes(10), out _));
            Assert.IsNull(svc.IssueAttempt("p1", Elig(), TFlag(), FeatureFlagPolicy.Default, T0.AddHours(5), out reason));
            Assert.AreEqual("RateLimited", reason);
        }

        [Test]
        public void KillSwitchAndRevokedConsent_StopSubmissionsMidTournament()
        {
            var svc = Service();
            Admit(svc, "p1");
            var t1 = Ticket(svc, "p1", T0.AddHours(1));
            var run = Run(svc, t1, 1500);
            var at = t1.ServerIssuedUtc.AddMinutes(2);
            Assert.AreEqual(TournamentSubmitStatus.NotEligible, Submit(svc, t1, run.Inputs, run.Score, at, flag: TFlag(enabled: false)));
            Assert.AreEqual(TournamentSubmitStatus.NotEligible, Submit(svc, t1, run.Inputs, run.Score, at,
                Elig(consent: ConsentRecord.Create(Game, ConsentState.Revoked, false, false))));
            Assert.AreEqual(TournamentSubmitStatus.NotEligible, Submit(svc, t1, run.Inputs, run.Score, at,
                Elig(profile: ChildProfile.Create(Game, AgeCategory.MinorUnverified, false))));
            Assert.IsNull(svc.IssueAttempt("p1", Elig(), TFlag(enabled: false), FeatureFlagPolicy.Default, T0.AddHours(2), out var reason));
            Assert.AreEqual("FeatureFlagDisabled", reason);
            // The ticket was not consumed by the ineligible calls; once eligible again it still works.
            Assert.AreEqual(TournamentSubmitStatus.Accepted, Submit(svc, t1, run.Inputs, run.Score, at));
        }

        // ---------- prizes ----------

        [Test]
        public void Close_GrantsPrizesThroughTheLedgerExactlyOnce_WithholdsIneligible_AndSurvivesRestore()
        {
            var svc = Service();
            var players = new[] { "p1", "p2", "p3", "p4" };
            foreach (var p in players) Assert.AreEqual("OK", Admit(svc, p));
            for (var i = 0; i < players.Length; i++)
                Assert.AreEqual(TournamentSubmitStatus.Accepted, PlayAndSubmit(svc, players[i], 1 + i, 1500, out _));
            var ledgers = players.ToDictionary(p => p, p => new RewardLedger(Game, p));
            var standings = svc.Leaderboard();
            var fourth = standings[3].DisplayHandle;
            var revoked = Elig(consent: ConsentRecord.Create(Game, ConsentState.Revoked, false, false));
            var anchor = TrustedUtcAnchor.Server(End.AddMinutes(5));

            var killed = svc.Close(anchor, TFlag(enabled: false), FeatureFlagPolicy.Default, _ => Elig(), p => ledgers[p]);
            Assert.IsTrue(killed.FirstClose);
            Assert.AreEqual(0, killed.Granted.Count);
            Assert.AreEqual(4, killed.Withheld.Count(w => w.Reason == "FeatureFlagDisabled"));

            var first = svc.Close(anchor, TFlag(), FeatureFlagPolicy.Default, p => p == fourth ? revoked : Elig(), p => ledgers[p]);
            Assert.IsFalse(first.FirstClose);
            Assert.AreEqual(3, first.Granted.Count);
            Assert.AreEqual(4, first.Withheld.Single().Rank);
            Assert.AreEqual("ConsentRequired", first.Withheld.Single().Reason);
            Assert.AreEqual(new long[] { 100, 50, 50, 0 },
                first.Standings.Select(s => ledgers[s.DisplayHandle].Balance(CurrencyType.Coins)).ToArray());
            Assert.AreEqual(RewardLedgerKey.Derive(Game, standings[0].DisplayHandle, RewardSources.Tournament, "cup.week47|prize"),
                first.Granted[0].Key);
            Assert.IsTrue(first.Granted.All(g => g.ExpiresUtc == End.AddDays(30)));

            var again = svc.Close(anchor, TFlag(), FeatureFlagPolicy.Default, _ => Elig(), p => ledgers[p]);
            Assert.AreEqual(1, again.Granted.Count, "the withheld prize is granted once eligibility returns");
            Assert.AreEqual(3, again.AlreadyGranted);
            Assert.AreEqual(10L, ledgers[fourth].Balance(CurrencyType.Coins));

            // Restore every ledger from its snapshot: still exactly once.
            var restored = ledgers.ToDictionary(kv => kv.Key, kv => RewardLedger.FromSnapshot(kv.Value.ToSnapshot()).Ledger);
            var afterRestore = svc.Close(TrustedUtcAnchor.Server(End.AddDays(1)), TFlag(), FeatureFlagPolicy.Default,
                _ => Elig(), p => restored[p]);
            Assert.AreEqual(0, afterRestore.Granted.Count);
            Assert.AreEqual(4, afterRestore.AlreadyGranted);
            Assert.AreEqual(new long[] { 100, 50, 50, 10 },
                afterRestore.Standings.Select(s => restored[s.DisplayHandle].Balance(CurrencyType.Coins)).ToArray());
            Assert.AreEqual(standings.Select(s => s.RunId).ToArray(), afterRestore.Standings.Select(s => s.RunId).ToArray());
            Assert.AreEqual(TournamentSubmitStatus.Closed, svc.Submit(new AttemptTicket("cup.week47", "p1", 1, T0, "x", 1),
                M.Start, new ModeInput[0], 0, End.AddTicks(-1), Elig(), TFlag(), FeatureFlagPolicy.Default));
        }

        [Test]
        public void Leaderboard_ShowsOnlyAllowedOptInHandles_NeverPlayerIds()
        {
            var svc = Service();
            Assert.IsTrue(svc.TryAdmit("player-guid-1", Elig(), true, "Ace", TFlag(), FeatureFlagPolicy.Default, T0.AddHours(1), out _));
            Assert.IsTrue(svc.TryAdmit("player-guid-2", Elig(social: new ChildAccountPolicy(true, false, "no public handles")),
                true, "Bee", TFlag(), FeatureFlagPolicy.Default, T0.AddHours(1), out _));
            Assert.IsTrue(svc.TryAdmit("player-guid-3", Elig(), true, null, TFlag(), FeatureFlagPolicy.Default, T0.AddHours(1), out _));
            foreach (var p in new[] { "player-guid-1", "player-guid-2", "player-guid-3" })
            {
                var t = Ticket(svc, p, T0.AddHours(2));
                var run = Run(svc, t, 1500);
                Assert.AreEqual(TournamentSubmitStatus.Accepted, Submit(svc, t, run.Inputs, run.Score, T0.AddHours(2).AddMinutes(2)));
            }
            var handles = svc.Leaderboard().Select(s => s.DisplayHandle).ToList();
            Assert.AreEqual(1, handles.Count(h => h == "Ace"));
            Assert.AreEqual(2, handles.Count(h => h == null), "handle refused by policy or not given -> anonymous");
            Assert.IsNull(typeof(TournamentStandingView).GetProperty("PlayerId"));
        }

        [Test]
        public void Template_RejectsClassicNonUtcBadPrizesAndRegions()
        {
            var tiers = new[] { new TournamentPrizeTier(1, 10) };
            Assert.Throws<ArgumentException>(() => new TournamentTemplate("cup.x", ModeTemplate.FromJson(
                    M.Json("classic.c", "classic", 6, 6, 4, 0, 0, 20, 1, 500, 0)), T0, End, new[] { "US" }, 3,
                TimeSpan.Zero, 2, "Coins", tiers));
            Assert.Throws<ArgumentException>(() => new TournamentTemplate("cup.x", M.TimeAttack(),
                new DateTime(2026, 11, 16), End, new[] { "US" }, 3, TimeSpan.Zero, 2, "Coins", tiers));
            Assert.Throws<ArgumentException>(() => new TournamentTemplate("cup.x", M.TimeAttack(), T0, End,
                new[] { "US" }, 3, TimeSpan.Zero, 2, "Coins",
                new[] { new TournamentPrizeTier(1, 10), new TournamentPrizeTier(3, 50) }));
            Assert.Throws<ArgumentException>(() => new TournamentTemplate("cup.x", M.TimeAttack(), T0, End,
                new[] { "usa" }, 3, TimeSpan.Zero, 2, "Coins", tiers));
            Assert.Throws<ArgumentException>(() => new TournamentTemplate("cup.x", M.TimeAttack(), T0, End,
                new[] { "US" }, 3, TimeSpan.Zero, 2, "PremiumGems", tiers));
            Assert.Throws<ArgumentException>(() => new TournamentTemplate("cup.x", M.TimeAttack(), T0, End,
                new[] { "US" }, 3, TimeSpan.Zero, 2, "coins", tiers));
        }
    }
}
