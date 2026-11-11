using System;
using System.Collections.Generic;
using System.Linq;
using Match3.Engine.Runtime.Identity;
using Match3.Engine.Runtime.LiveOps;
using Match3.Engine.Runtime.Privacy;
using NUnit.Framework;
using M = Match3.Engine.Demo.Tests.AlternativeModesEditModeTests;

namespace Match3.Engine.Demo.Tests
{
    // ENG-M20-015 tournament: default OFF, never Wonder_Pop/child, consent/region/opt-in, engine-recomputed
    // score from the input log (pinned SDK replay), tie rules, UTC close, rate limits, exactly-once prizes.
    public sealed class AsyncTournamentEditModeTests
    {
        private const string Game = M.Game;
        private static readonly DateTime T0 = new DateTime(2026, 11, 16, 0, 0, 0, DateTimeKind.Utc);
        private static readonly DateTime End = T0.AddDays(7);

        private static TournamentTemplate Template(int maxSubmissions = 7, int minIntervalMinutes = 0) =>
            new TournamentTemplate("cup.week47", M.TimeAttack(), 4242, T0, End, new[] { "US", "DE", "TR" },
                maxSubmissions, TimeSpan.FromMinutes(minIntervalMinutes), 2, "trophy-coins",
                new[] { new TournamentPrizeTier(1, 100), new TournamentPrizeTier(3, 50), new TournamentPrizeTier(10, 10) });

        private static TournamentService Service(int maxSubmissions = 7, int minIntervalMinutes = 0)
        {
            var t = Template(maxSubmissions, minIntervalMinutes);
            return new TournamentService(Game, t, M.Sdk(t.Mode));
        }

        private static FeatureFlagRecord TFlag(bool enabled = true, bool consentCheck = true, string game = Game) =>
            FeatureFlagRecord.Create(game, LiveOpsFeature.TournamentMode, 1, enabled, consentCheck);

        private static readonly ChildAccountPolicy LeaderboardOptInPolicy =
            new ChildAccountPolicy(true, false, "Adult opt-in tournament leaderboard");

        private static string Admit(TournamentService svc, string player, ChildProfile? profile = null,
            ConsentRecord? consent = null, string region = "DE", bool optIn = true, FeatureFlagRecord? flag = null,
            FeatureFlagPolicy? policy = null, ChildAccountPolicy? social = null, DateTime? now = null)
        {
            var ok = svc.TryAdmit(player, profile ?? ChildProfile.Create(Game, AgeCategory.Adult, false),
                consent ?? ConsentRecord.Create(Game, ConsentState.Granted, false, false), region, optIn,
                flag ?? TFlag(), policy ?? FeatureFlagPolicy.Default, social ?? LeaderboardOptInPolicy,
                now ?? T0.AddHours(1), out var reason);
            return ok ? "OK" : reason;
        }

        private static ModeSession Run(TournamentService svc, long stepMs)
        {
            var s = ModeSession.Start(Game, svc.Template.Mode, svc.Template.Seed, M.Start, M.Sdk(svc.Template.Mode),
                M.Flag(GameModeKind.TimeAttack), FeatureFlagPolicy.Default);
            M.Play(s, M.Start, stepMs, 1000);
            Assert.AreEqual(ModePhase.Ended, s.Phase);
            return s;
        }

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
                consent: ConsentRecord.Create(Game, ConsentState.NotRequested, false, false)));
            Assert.AreEqual(0, svc.Leaderboard().Count);
            Assert.AreEqual(TournamentSubmitStatus.NotAdmitted, svc.Submit("p1", M.Start, new ModeInput[0], 0, T0.AddHours(2)));
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
                profile: ChildProfile.Create(Game, AgeCategory.MinorWithParent, false)));
            Assert.AreEqual("RestrictedProfileProhibited", Admit(svc, "kid",
                profile: ChildProfile.Create(Game, AgeCategory.MinorUnverified, false)));
            Assert.AreEqual("RestrictedProfileProhibited", Admit(svc, "kid",
                profile: ChildProfile.Create(Game, AgeCategory.Adult, true)));
            // Adult profile but consent says minor: still refused.
            Assert.AreEqual("MinorProhibited", Admit(svc, "kid",
                consent: ConsentRecord.Create(Game, ConsentState.Granted, true, false)));
            Assert.AreEqual("GameIdMismatch", Admit(svc, "kid",
                profile: ChildProfile.Create("Dino_Islands", AgeCategory.Adult, false)));
        }

        [Test]
        public void Admission_NeedsConsent_AllowedRegion_OptIn_SocialPolicy_AndOpenWindow()
        {
            var svc = Service();
            Assert.AreEqual("ConsentRequired", Admit(svc, "p1", consent: ConsentRecord.Create(Game, ConsentState.Denied, false, false)));
            Assert.AreEqual("ConsentRequired", Admit(svc, "p1", consent: ConsentRecord.Create(Game, ConsentState.Revoked, false, false)));
            Assert.AreEqual("RestrictedRegionBlocked", Admit(svc, "p1", consent: ConsentRecord.Create(Game, ConsentState.Granted, false, true)));
            Assert.AreEqual("ConsentGameMismatch", Admit(svc, "p1", consent: ConsentRecord.Create("Dino_Islands", ConsentState.Granted, false, false)));
            Assert.AreEqual("RegionNotAllowed", Admit(svc, "p1", region: "FR"));
            Assert.AreEqual("RegionNotAllowed", Admit(svc, "p1", region: "de"));
            Assert.AreEqual("OptInRequired", Admit(svc, "p1", optIn: false));
            Assert.AreEqual("SocialFeaturesDisabled", Admit(svc, "p1", social: ChildAccountPolicy.Default));
            Assert.AreEqual("NotOpen", Admit(svc, "p1", now: T0.AddTicks(-1)));
            Assert.AreEqual("Closed", Admit(svc, "p1", now: End));
            Assert.AreEqual("InvalidPlayerId", Admit(svc, "p|1"));
            Assert.AreEqual("OK", Admit(svc, "p1"));
        }

        [Test]
        public void Score_IsRecomputedFromTheLog_TamperingAndCopiesAreRejected()
        {
            var svc = Service();
            Admit(svc, "p1");
            Admit(svc, "p2");
            var run = Run(svc, 1500);
            var log = run.Inputs.ToList();

            Assert.AreEqual(TournamentSubmitStatus.TamperedScore, svc.Submit("p1", M.Start, log, run.Score + 1, T0.AddHours(2)));
            Assert.AreEqual(TournamentSubmitStatus.Incomplete,
                svc.Submit("p1", M.Start, log.Take(log.Count - 1).ToList(), run.Score, T0.AddHours(2)));
            var backwards = new List<ModeInput>(log);
            backwards.Insert(3, ModeInput.Of(ModeInputKind.Tick, M.Start));
            Assert.AreEqual(TournamentSubmitStatus.InvalidLog, svc.Submit("p1", M.Start, backwards, run.Score, T0.AddHours(2)));
            Assert.AreEqual(TournamentSubmitStatus.InvalidLog, svc.Submit("p1", M.Start, null, run.Score, T0.AddHours(2)));
            // Pausing freezes the timer, so a tournament log may hold only MaxInterruptionsPerRun (2) of them.
            var paused = new List<ModeInput>();
            for (var i = 0; i < 3; i++)
            {
                paused.Add(ModeInput.Of(ModeInputKind.Pause, M.Start + i));
                paused.Add(ModeInput.Of(ModeInputKind.Resume, M.Start + i));
            }
            paused.AddRange(log);
            Assert.AreEqual(TournamentSubmitStatus.TooManyInterruptions, svc.Submit("p1", M.Start, paused, run.Score, T0.AddHours(2)));
            Assert.AreEqual(TournamentSubmitStatus.Accepted, svc.Submit("p1", M.Start, log, run.Score, T0.AddHours(2)));
            // Copying someone else's verified run is a duplicate, whoever sends it.
            Assert.AreEqual(TournamentSubmitStatus.DuplicateRun, svc.Submit("p2", M.Start, log, run.Score, T0.AddHours(3)));
            Assert.AreEqual(TournamentSubmitStatus.NotAdmitted, svc.Submit("p3", M.Start, log, run.Score, T0.AddHours(3)));

            var board = svc.Leaderboard();
            Assert.AreEqual(1, board.Count);
            Assert.AreEqual("p1", board[0].PlayerId);
            Assert.AreEqual(run.Score, board[0].Score);
            Assert.AreEqual(run.RunId, board[0].RunId);
            Assert.AreEqual(T0.AddHours(2), board[0].VerifiedCompletionUtc);
        }

        [Test]
        public void TieRules_ScoreDesc_ThenEarliestVerifiedCompletion_ThenRunIdOrdinal()
        {
            var svc = Service();
            foreach (var p in new[] { "p1", "p2", "p3", "p4" }) Assert.AreEqual("OK", Admit(svc, p));
            var full = Run(svc, 1500);
            var shorter = Run(svc, 3000); // same deterministic swaps, fewer of them -> strictly lower score
            Assert.IsTrue(shorter.Score < full.Score);
            var log = full.Inputs.ToList();
            var log2 = new[] { ModeInput.Of(ModeInputKind.Tick, M.Start + 1) }.Concat(log).ToList();
            var log3 = new[] { ModeInput.Of(ModeInputKind.Tick, M.Start + 2) }.Concat(log).ToList();

            Assert.AreEqual(TournamentSubmitStatus.Accepted, svc.Submit("p4", M.Start, shorter.Inputs, shorter.Score, T0.AddMinutes(30)));
            Assert.AreEqual(TournamentSubmitStatus.Accepted, svc.Submit("p1", M.Start, log, full.Score, T0.AddHours(1)));
            Assert.AreEqual(TournamentSubmitStatus.Accepted, svc.Submit("p2", M.Start, log2, full.Score, T0.AddHours(2)));
            Assert.AreEqual(TournamentSubmitStatus.Accepted, svc.Submit("p3", M.Start, log3, full.Score, T0.AddHours(2)));

            var board = svc.Leaderboard();
            Assert.AreEqual("p1", board[0].PlayerId, "equal score, earliest verified completion wins");
            var tied = new[] { board[1], board[2] };
            Assert.AreEqual(full.Score, tied[0].Score);
            Assert.AreEqual(tied[0].VerifiedCompletionUtc, tied[1].VerifiedCompletionUtc);
            Assert.IsTrue(string.CompareOrdinal(tied[0].RunId, tied[1].RunId) < 0, "same instant: run ID ordinal");
            Assert.AreEqual("p4", board[3].PlayerId, "lower score ranks last despite the earliest submission");
            Assert.AreEqual(new[] { 1, 2, 3, 4 }, board.Select(s => s.Rank).ToArray());
        }

        [Test]
        public void Window_ClosesExactlyAtUtcEnd()
        {
            var svc = Service();
            Admit(svc, "p1");
            Admit(svc, "p2");
            var run = Run(svc, 1500);
            var late = Run(svc, 2000);
            Assert.AreEqual(TournamentSubmitStatus.Closed, svc.Submit("p1", M.Start, run.Inputs, run.Score, End));
            Assert.AreEqual(TournamentSubmitStatus.Accepted, svc.Submit("p1", M.Start, run.Inputs, run.Score, End.AddTicks(-1)));
            Assert.IsNull(svc.Close(End.AddSeconds(-1)));
            Assert.IsFalse(svc.IsClosed);
            Assert.IsTrue(svc.Close(End).FirstClose);
            Assert.AreEqual(TournamentSubmitStatus.Closed, svc.Submit("p2", M.Start, late.Inputs, late.Score, End.AddTicks(-1)));
            Assert.AreEqual("Closed", Admit(svc, "p3", now: End.AddTicks(-1)));
            Assert.Throws<ArgumentException>(() => svc.Close(DateTime.Now));
        }

        [Test]
        public void RateLimit_CountsEveryAttempt_AndEnforcesTheInterval()
        {
            var svc = Service(maxSubmissions: 2, minIntervalMinutes: 10);
            Admit(svc, "p1");
            var run = Run(svc, 1500);
            Assert.AreEqual(TournamentSubmitStatus.TamperedScore, svc.Submit("p1", M.Start, run.Inputs, 1, T0.AddHours(1)));
            Assert.AreEqual(TournamentSubmitStatus.RateLimited, svc.Submit("p1", M.Start, run.Inputs, run.Score, T0.AddHours(1).AddMinutes(9)));
            Assert.AreEqual(TournamentSubmitStatus.Accepted, svc.Submit("p1", M.Start, run.Inputs, run.Score, T0.AddHours(1).AddMinutes(10)));
            Assert.AreEqual(TournamentSubmitStatus.RateLimited, svc.Submit("p1", M.Start, run.Inputs, run.Score, T0.AddHours(5)));
        }

        [Test]
        public void Close_EmitsPrizeKeysExactlyOnce_ByRankTier()
        {
            var svc = Service();
            var steps = new[] { 1500L, 2000L, 3000L, 6000L };
            for (var i = 0; i < steps.Length; i++)
            {
                var player = "p" + (i + 1);
                Assert.AreEqual("OK", Admit(svc, player));
                var run = Run(svc, steps[i]);
                Assert.AreEqual(TournamentSubmitStatus.Accepted, svc.Submit(player, M.Start, run.Inputs, run.Score, T0.AddHours(1 + i)));
            }

            var first = svc.Close(End.AddMinutes(5));
            Assert.IsTrue(first.FirstClose);
            Assert.AreEqual(new[] { "p1", "p2", "p3", "p4" }, first.Standings.Select(s => s.PlayerId).ToArray());
            Assert.AreEqual(new[] { 100, 50, 50, 10 }, first.Grants.Select(g => g.Amount).ToArray());
            Assert.AreEqual("tournament|Coral_Rescue|cup.week47|p1|prize", first.Grants[0].IdempotencyKey);
            Assert.AreEqual(4, first.Grants.Select(g => g.IdempotencyKey).Distinct().Count());
            Assert.IsTrue(first.Grants.All(g => g.RewardId == "trophy-coins"));

            var again = svc.Close(End.AddDays(1));
            Assert.IsFalse(again.FirstClose);
            Assert.AreEqual(0, again.Grants.Count);
            Assert.AreEqual(first.Standings.Select(s => s.RunId).ToArray(), again.Standings.Select(s => s.RunId).ToArray());
        }

        [Test]
        public void Template_RejectsClassicNonUtcAndBadPrizeTiers()
        {
            Assert.Throws<ArgumentException>(() => new TournamentTemplate("cup.x", ModeTemplate.FromJson(
                    M.Json("classic.c", "classic", 6, 6, 4, 0, 0, 20, 1, 500, 0)), 1, T0, End, new[] { "US" }, 3,
                TimeSpan.Zero, 2, "coins", new[] { new TournamentPrizeTier(1, 10) }));
            Assert.Throws<ArgumentException>(() => new TournamentTemplate("cup.x", M.TimeAttack(), 1,
                new DateTime(2026, 11, 16), End, new[] { "US" }, 3, TimeSpan.Zero, 2, "coins",
                new[] { new TournamentPrizeTier(1, 10) }));
            Assert.Throws<ArgumentException>(() => new TournamentTemplate("cup.x", M.TimeAttack(), 1, T0, End,
                new[] { "US" }, 3, TimeSpan.Zero, 2, "coins",
                new[] { new TournamentPrizeTier(1, 10), new TournamentPrizeTier(3, 50) }));
            Assert.Throws<ArgumentException>(() => new TournamentTemplate("cup.x", M.TimeAttack(), 1, T0, End,
                new[] { "usa" }, 3, TimeSpan.Zero, 2, "coins", new[] { new TournamentPrizeTier(1, 10) }));
        }
    }
}
