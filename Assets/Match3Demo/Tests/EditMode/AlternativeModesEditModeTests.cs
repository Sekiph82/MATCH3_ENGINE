using System;
using System.Collections.Generic;
using System.Linq;
using Match3.Engine.Runtime;
using Match3.Engine.Runtime.Economy;
using Match3.Engine.Runtime.LiveOps;
using NUnit.Framework;

namespace Match3.Engine.Demo.Tests
{
    // ENG-M20-015. Every run here uses the pinned LibraStack SDK solver (NeutralSdkSnapshotValidator) as the
    // match authority and the engine planners for spawn/gravity/refill/score; no test double decides matches.
    // The "player" is deterministic: always the first SDK-legal move in canonical order.
    public sealed class AlternativeModesEditModeTests
    {
        internal const string Game = "Coral_Rescue";
        internal const long Start = 1000;

        internal static string Json(string id, string kind, int rows, int columns, int startColors, int ramp,
            int timeLimitMs, int moveLimit, int lives, int maxTurns, int target, string extra = "") =>
            "{\"schema\":\"alt-mode/1\",\"templateId\":\"" + id + "\",\"version\":1,\"kind\":\"" + kind + "\"," +
            "\"rows\":" + rows + ",\"columns\":" + columns + ",\"palette\":[\"A\",\"B\",\"C\",\"D\"]," +
            "\"startColors\":" + startColors + ",\"rampEveryTurns\":" + ramp + ",\"timeLimitMs\":" + timeLimitMs +
            ",\"moveLimit\":" + moveLimit + ",\"shuffleLives\":" + lives + ",\"backgroundGraceMs\":3000," +
            "\"pointsPerCell\":10,\"maxTurns\":" + maxTurns + ",\"targetScore\":" + target + "," +
            "\"rewardId\":\"Coins\",\"rewardTiers\":[{\"score\":100,\"amount\":5},{\"score\":300,\"amount\":10}," +
            "{\"score\":600,\"amount\":20}],\"perRunRewardCap\":25,\"perDayRewardCap\":40" + extra + "}";

        internal static ModeTemplate TimeAttack() =>
            ModeTemplate.FromJson(Json("ta.sprint60", "timeAttack", 6, 6, 4, 0, 60000, 0, 3, 500, 300));

        internal static ModeTemplate Endless() =>
            ModeTemplate.FromJson(Json("endless.ramp", "endless", 6, 6, 3, 5, 0, 0, 1, 400, 0));

        private static ModeTemplate Classic() =>
            ModeTemplate.FromJson(Json("classic.score20", "classic", 6, 6, 4, 0, 0, 20, 1, 500, 0));

        /// <summary>The pinned LibraStack SDK commit (Packages/manifest.json) is the matcher ID.</summary>
        internal const string SdkPin = "libra-match3-sdk@771e62908056c2c5fa69937a52ca52ab589f19ea";

        internal static ModeMatcher Sdk(ModeTemplate t, string pin = SdkPin) =>
            new ModeMatcher(pin, new NeutralSdkSnapshotValidator(new BoardTopology(t.Rows, t.Columns)).GetSdkMatchRuns);

        internal static FeatureFlagRecord Flag(GameModeKind kind, bool enabled = true, string game = Game) =>
            FeatureFlagRecord.Create(game, kind == GameModeKind.Endless ? LiveOpsFeature.EndlessMode : LiveOpsFeature.TimedMode,
                1, enabled, false);

        internal static ModeSession New(ModeTemplate t, uint seed, string game = Game) =>
            ModeSession.Start(game, t, seed, Start, Sdk(t), Flag(t.Kind, true, game), FeatureFlagPolicy.Default);

        internal static SwapCandidate FirstLegal(ModeSession s) =>
            new NeutralSdkSnapshotValidator(new BoardTopology(s.Template.Rows, s.Template.Columns))
                .FindRealLegalMoves(s.Board).Accepted[0];

        /// <summary>Tick + first legal swap every stepMs until the run ends or maxSwaps is reached.</summary>
        internal static long Play(ModeSession s, long now, long stepMs, int maxSwaps)
        {
            for (var i = 0; i < maxSwaps && s.Phase == ModePhase.Running; i++)
            {
                now += stepMs;
                s.Apply(ModeInput.Of(ModeInputKind.Tick, now));
                if (s.Phase != ModePhase.Running) break;
                var move = FirstLegal(s);
                Assert.AreEqual(ModeInputStatus.Applied, s.Apply(ModeInput.Swap(now, move.Source, move.Target)));
            }
            return now;
        }

        private static ModeSession Replay(ModeSession s) =>
            ModeSession.Replay(s.GameId, s.Template, s.Seed, s.StartMs, s.Inputs, Sdk(s.Template));

        // ---------- templates and gate ----------

        [Test]
        public void Templates_AreDeclarativeValidatedAndContentHashed()
        {
            var ta = TimeAttack();
            Assert.AreEqual(GameModeKind.TimeAttack, ta.Kind);
            Assert.AreEqual(60000L, ta.TimeLimitMs);
            Assert.AreEqual(64, ta.ContentHash.Length);
            Assert.AreEqual(GameModeKind.Endless, Endless().Kind);
            Assert.AreEqual(GameModeKind.Classic, Classic().Kind);
            Assert.AreNotEqual(ta.ContentHash, Endless().ContentHash);
            // Hash is canonical (key order independent).
            var reordered = ModeTemplate.FromJson("{\"version\":1," +
                Json("ta.sprint60", "timeAttack", 6, 6, 4, 0, 60000, 0, 3, 500, 300).Substring(1).Replace("\"version\":1,", ""));
            Assert.AreEqual(ta.ContentHash, reordered.ContentHash);

            // Unknown keys (e.g. a paid entry) and mode-rule violations are rejected.
            Assert.Throws<FormatException>(() => ModeTemplate.FromJson(
                Json("ta.sprint60", "timeAttack", 6, 6, 4, 0, 60000, 0, 3, 500, 300, ",\"entryFee\":100")));
            Assert.Throws<FormatException>(() => ModeTemplate.FromJson(
                Json("ta.bad", "timeAttack", 6, 6, 4, 0, 0, 0, 3, 500, 300)));
            Assert.Throws<FormatException>(() => ModeTemplate.FromJson(
                Json("endless.bad", "endless", 6, 6, 4, 0, 60000, 0, 1, 400, 0)));
            Assert.Throws<FormatException>(() => ModeTemplate.FromJson(
                Json("endless.bad", "endless", 6, 6, 3, 0, 0, 0, 1, 400, 0)));
            Assert.Throws<FormatException>(() => ModeTemplate.FromJson(
                Json("classic.bad", "classic", 6, 6, 4, 0, 0, 0, 1, 500, 0)));
            Assert.Throws<FormatException>(() => ModeTemplate.FromJson(
                Json("ta.sprint60", "timeAttack", 6, 6, 4, 0, 60000, 0, 3, 500, 300).Replace("alt-mode/1", "alt-mode/2")));
            Assert.Throws<FormatException>(() => ModeTemplate.FromJson(
                Json("ta.sprint60", "timeAttack", 6, 6, 4, 0, 60000, 0, 3, 500, 300).Replace("{\"score\":300", "{\"score\":50")));
        }

        [Test]
        public void ModeGate_UsesFeatureFlags_KillSwitch_AndAllowsWonderPopTimedAndEndless()
        {
            Assert.IsTrue(AlternativeModeGate.CanStart(Game, GameModeKind.Classic, default(FeatureFlagRecord),
                FeatureFlagPolicy.Default, out _));
            Assert.IsTrue(AlternativeModeGate.CanStart(Game, GameModeKind.TimeAttack, Flag(GameModeKind.TimeAttack),
                FeatureFlagPolicy.Default, out _));
            Assert.IsFalse(AlternativeModeGate.CanStart(Game, GameModeKind.TimeAttack, Flag(GameModeKind.TimeAttack, false),
                FeatureFlagPolicy.Default, out var reason));
            Assert.AreEqual("FeatureFlagDisabled", reason);
            Assert.IsFalse(AlternativeModeGate.CanStart(Game, GameModeKind.Endless, Flag(GameModeKind.TimeAttack),
                FeatureFlagPolicy.Default, out reason));
            Assert.AreEqual("WrongFeatureFlag", reason);
            Assert.IsFalse(AlternativeModeGate.CanStart("Coral_Rescue", GameModeKind.Endless,
                Flag(GameModeKind.Endless, true, "Dino_Islands"), FeatureFlagPolicy.Default, out reason));
            Assert.AreEqual("GameIdMismatch", reason);
            // Owner decision: Wonder_Pop is general audience; timed/endless are flag-controlled, not audience-blocked.
            Assert.IsTrue(AlternativeModeGate.CanStart("Wonder_Pop", GameModeKind.Endless,
                Flag(GameModeKind.Endless, true, "Wonder_Pop"), FeatureFlagPolicy.Default, out _));
            Assert.AreEqual(ModePhase.Running, New(TimeAttack(), 7, "Wonder_Pop").Phase);
            var ex = Assert.Throws<InvalidOperationException>(() => ModeSession.Start(Game, Endless(), 7, Start,
                Sdk(Endless()), Flag(GameModeKind.Endless, false), FeatureFlagPolicy.Default));
            Assert.IsTrue(ex.Message.Contains("FeatureFlagDisabled"));
        }

        // ---------- determinism (golden values) ----------

        [Test]
        public void TimeAttack_IsDeterministic_ReplaysExactly_AndMatchesGolden()
        {
            var a = New(TimeAttack(), 12345);
            var end = Play(a, Start, 1500, 1000);
            var b = New(TimeAttack(), 12345);
            Play(b, Start, 1500, 1000);

            Assert.AreEqual(ModePhase.Ended, a.Phase);
            Assert.AreEqual(ModeEndReason.TimeExpired, a.EndReason);
            Assert.AreEqual(60000L, a.ElapsedMs);
            Assert.AreEqual(a.StateHash(), b.StateHash());
            Assert.AreEqual(a.RunId, b.RunId);
            var replay = Replay(a);
            Assert.AreEqual(a.StateHash(), replay.StateHash());
            Assert.AreEqual(a.RunId, replay.RunId);
            Assert.AreNotEqual(a.StateHash(), Play2(New(TimeAttack(), 12346)).StateHash());

            Assert.AreEqual(GoldenTimeAttackScore, a.Score, "golden score");
            Assert.AreEqual(GoldenTimeAttackTurns, a.Turns, "golden turns");
            Assert.AreEqual(GoldenTimeAttackHash, a.StateHash(), "golden state hash");
            Assert.AreEqual(Start + 60000, end);
        }

        private static ModeSession Play2(ModeSession s)
        {
            Play(s, Start, 1500, 1000);
            return s;
        }

        [Test]
        public void Endless_IsDeterministic_RampsDifficulty_AndMatchesGolden()
        {
            var a = New(Endless(), 777);
            Assert.AreEqual(3, a.ActiveColors);
            Assert.IsTrue(a.Board.Values.All(v => v == "A" || v == "B" || v == "C"));
            Play(a, Start, 800, 60);
            var replay = Replay(a);

            Assert.AreEqual(a.StateHash(), replay.StateHash());
            Assert.AreEqual(-1L, a.RemainingMs);
            Assert.AreEqual(-1, a.MovesRemaining);
            Assert.AreEqual(4, a.ActiveColors, "ramp adds the 4th colour after 5 turns");
            Assert.AreEqual(GoldenEndlessScore, a.Score, "golden score");
            Assert.AreEqual(GoldenEndlessTurns, a.Turns, "golden turns");
            Assert.AreEqual(GoldenEndlessHash, a.StateHash(), "golden state hash");
        }

        internal const int GoldenTimeAttackScore = 2840;
        internal const int GoldenTimeAttackTurns = 39;
        internal const string GoldenTimeAttackHash =
            "a97279024c39bed91ab3a296e7066e5785a9ea5bb1254b06249c71295887930d";
        internal const int GoldenEndlessScore = 3277;
        internal const int GoldenEndlessTurns = 60;
        internal const string GoldenEndlessHash =
            "5dfbb8c869b0ebc706b10f11108ffa6f7617b8038d6cfa2cb94ea3764c0fa52a";

        // ---------- timer ----------

        [Test]
        public void TimeAttack_ExpiresExactlyAtTheLimit()
        {
            var s = New(TimeAttack(), 12345);
            var move = FirstLegal(s);
            Assert.AreEqual(ModeInputStatus.Applied, s.Apply(ModeInput.Swap(Start + 59850, move.Source, move.Target)));
            Assert.AreEqual(1, s.Turns);
            Assert.AreEqual(ModeInputStatus.Applied, s.Apply(ModeInput.Of(ModeInputKind.Tick, Start + 59999)));
            Assert.AreEqual(1L, s.RemainingMs);

            move = FirstLegal(s);
            Assert.AreEqual(ModeInputStatus.Ignored, s.Apply(ModeInput.Swap(Start + 60000, move.Source, move.Target)));
            Assert.AreEqual(ModePhase.Ended, s.Phase);
            Assert.AreEqual(ModeEndReason.TimeExpired, s.EndReason);
            Assert.AreEqual(60000L, s.ElapsedMs);
            Assert.AreEqual(0L, s.RemainingMs);
            Assert.AreEqual(1, s.Turns);
            s.Apply(ModeInput.Of(ModeInputKind.Tick, Start + 999999));
            Assert.AreEqual(60000L, s.ElapsedMs);

            // A coarse tick that overshoots the limit is clamped to the limit exactly.
            var coarse = New(TimeAttack(), 12345);
            coarse.Apply(ModeInput.Of(ModeInputKind.Tick, Start + 59000));
            Assert.AreEqual(ModePhase.Running, coarse.Phase);
            coarse.Apply(ModeInput.Of(ModeInputKind.Tick, Start + 61000));
            Assert.AreEqual(ModeEndReason.TimeExpired, coarse.EndReason);
            Assert.AreEqual(60000L, coarse.ElapsedMs);
        }

        [Test]
        public void Pause_FreezesTimer_AndBlocksMoves()
        {
            var s = New(TimeAttack(), 12345);
            s.Apply(ModeInput.Of(ModeInputKind.Pause, Start + 10000));
            Assert.AreEqual(ModePhase.Paused, s.Phase);
            s.Apply(ModeInput.Of(ModeInputKind.Tick, Start + 50000));
            Assert.AreEqual(10000L, s.ElapsedMs);
            var move = FirstLegal(s);
            Assert.AreEqual(ModeInputStatus.Ignored, s.Apply(ModeInput.Swap(Start + 50000, move.Source, move.Target)));
            Assert.AreEqual(0, s.Turns);
            Assert.AreEqual(ModeInputStatus.Applied, s.Apply(ModeInput.Of(ModeInputKind.Resume, Start + 50000)));
            s.Apply(ModeInput.Of(ModeInputKind.Tick, Start + 51000));
            Assert.AreEqual(11000L, s.ElapsedMs);
            Assert.AreEqual(s.StateHash(), Replay(s).StateHash());
        }

        [Test]
        public void Background_WithinGraceKeepsCounting_BeyondGraceAutoPausesAtGrace_ResumeIsExplicit()
        {
            var s = New(TimeAttack(), 12345);
            s.Apply(ModeInput.Of(ModeInputKind.Background, Start + 1000));
            s.Apply(ModeInput.Of(ModeInputKind.Foreground, Start + 3000)); // 2s < 3s grace
            Assert.AreEqual(ModePhase.Running, s.Phase);
            Assert.AreEqual(3000L, s.ElapsedMs);

            s.Apply(ModeInput.Of(ModeInputKind.Background, Start + 4000));
            s.Apply(ModeInput.Of(ModeInputKind.Tick, Start + 7000)); // exactly at grace end: still running
            Assert.AreEqual(ModePhase.Running, s.Phase);
            s.Apply(ModeInput.Of(ModeInputKind.Foreground, Start + 40000));
            Assert.AreEqual(ModePhase.Paused, s.Phase);
            Assert.IsTrue(s.IsAutoPaused);
            Assert.AreEqual(7000L, s.ElapsedMs, "only the 3s grace is charged");
            var move = FirstLegal(s);
            Assert.AreEqual(ModeInputStatus.Ignored, s.Apply(ModeInput.Swap(Start + 40000, move.Source, move.Target)));
            s.Apply(ModeInput.Of(ModeInputKind.Tick, Start + 45000));
            Assert.AreEqual(7000L, s.ElapsedMs);
            Assert.AreEqual(ModeInputStatus.Applied, s.Apply(ModeInput.Of(ModeInputKind.Resume, Start + 45000)));
            s.Apply(ModeInput.Of(ModeInputKind.Tick, Start + 46000));
            Assert.AreEqual(8000L, s.ElapsedMs);

            // Resume while still backgrounded is refused.
            s.Apply(ModeInput.Of(ModeInputKind.Background, Start + 47000));
            s.Apply(ModeInput.Of(ModeInputKind.Pause, Start + 47000));
            Assert.AreEqual(ModeInputStatus.Ignored, s.Apply(ModeInput.Of(ModeInputKind.Resume, Start + 48000)));
            Assert.AreEqual(s.StateHash(), Replay(s).StateHash());
        }

        [Test]
        public void Reconnect_RestoresFromLog_AndNeverGrantsExtraTime()
        {
            var live = New(TimeAttack(), 12345);
            var now = Play(live, Start, 1500, 10); // 10 swaps, 15s
            Assert.AreEqual(15000L, live.ElapsedMs);
            var remainingBefore = live.RemainingMs;

            // Process dies; a new process restores from the saved log and gets a fresh monotonic base (5).
            var restored = Replay(live);
            Assert.AreEqual(live.StateHash(), restored.StateHash());
            Assert.AreEqual(ModeInputStatus.Applied, restored.Apply(ModeInput.Of(ModeInputKind.Reconnect, 5)));
            Assert.AreEqual(ModePhase.Paused, restored.Phase);
            Assert.IsTrue(restored.IsAutoPaused);
            Assert.AreEqual(remainingBefore - 3000, restored.RemainingMs, "reconnect costs the unused grace");
            restored.Apply(ModeInput.Of(ModeInputKind.Tick, 100000));
            Assert.AreEqual(18000L, restored.ElapsedMs, "no time passes or returns while auto-paused");
            restored.Apply(ModeInput.Of(ModeInputKind.Resume, 100000));
            restored.Apply(ModeInput.Of(ModeInputKind.Tick, 101000));
            Assert.AreEqual(19000L, restored.ElapsedMs);

            // Reconnecting mid-background only spends what was left of that grace (no double charge).
            var bg = New(TimeAttack(), 12345);
            bg.Apply(ModeInput.Of(ModeInputKind.Background, Start + 1000));
            bg.Apply(ModeInput.Of(ModeInputKind.Tick, Start + 2000));
            bg.Apply(ModeInput.Of(ModeInputKind.Reconnect, 0));
            Assert.AreEqual(4000L, bg.ElapsedMs);
            Assert.AreEqual(restored.StateHash(), Replay(restored).StateHash());
            Assert.IsTrue(now > Start);
        }

        [Test]
        public void ProtocolViolations_AreRejected_NotLogged_AndFailReplay()
        {
            var s = New(TimeAttack(), 12345);
            s.Apply(ModeInput.Of(ModeInputKind.Tick, Start + 5000));
            var logged = s.Inputs.Count;
            Assert.AreEqual(ModeInputStatus.Rejected, s.Apply(ModeInput.Of(ModeInputKind.Tick, Start + 4000)));
            Assert.AreEqual(ModeInputStatus.Rejected, s.Apply(ModeInput.Swap(Start + 6000,
                new BoardPosition(0, 0), new BoardPosition(2, 2))));
            Assert.AreEqual(ModeInputStatus.Rejected, s.Apply(ModeInput.Swap(Start + 6000,
                new BoardPosition(0, 5), new BoardPosition(0, 6))));
            Assert.AreEqual(logged, s.Inputs.Count);
            Assert.AreEqual(5000L, s.ElapsedMs);

            var bad = new List<ModeInput>(s.Inputs) { ModeInput.Of(ModeInputKind.Tick, Start + 1) };
            Assert.Throws<FormatException>(() =>
                ModeSession.Replay(Game, s.Template, s.Seed, s.StartMs, bad, Sdk(s.Template)));
            Assert.Throws<ArgumentOutOfRangeException>(() => ModeInput.Of(ModeInputKind.Tick, -1));
            Assert.Throws<ArgumentOutOfRangeException>(() => ModeSession.Start(Game, TimeAttack(), 0, Start,
                Sdk(TimeAttack()), Flag(GameModeKind.TimeAttack), FeatureFlagPolicy.Default));
        }

        [Test]
        public void NoMatchSwap_SpendsNothing_AndLeavesBoardUnchanged()
        {
            var s = New(TimeAttack(), 12345);
            var legal = new NeutralSdkSnapshotValidator(new BoardTopology(6, 6)).FindRealLegalMoves(s.Board).Accepted;
            SwapCandidate? dud = null;
            for (var r = 0; r < 6 && dud == null; r++)
                for (var c = 0; c + 1 < 6 && dud == null; c++)
                {
                    var cand = new SwapCandidate(new BoardPosition(r, c), new BoardPosition(r, c + 1));
                    if (!legal.Contains(cand)) dud = cand;
                }
            Assert.IsTrue(dud.HasValue);
            var before = s.BoardSnapshot();
            Assert.AreEqual(ModeInputStatus.NoMatch, s.Apply(ModeInput.Swap(Start + 10, dud.Value.Source, dud.Value.Target)));
            Assert.AreEqual(before, s.BoardSnapshot());
            Assert.AreEqual(0, s.Turns);
            Assert.AreEqual(0, s.Score);
        }

        // ---------- endless / classic termination ----------

        [Test]
        public void Endless_EndsOnNoValidMovesAfterLives_OrTurnCap()
        {
            var tiny = ModeTemplate.FromJson(Json("endless.tiny", "endless", 4, 4, 4, 0, 0, 0, 1, 300, 0));
            var s = New(tiny, GoldenTinySeed);
            Play(s, Start, 200, 300);
            Assert.AreEqual(ModePhase.Ended, s.Phase);
            Assert.AreEqual(ModeEndReason.NoValidMoves, s.EndReason);
            Assert.AreEqual(0, s.ShuffleLivesRemaining, "the one life was spent on a reshuffle first");
            Assert.AreEqual(GoldenTinyTurns, s.Turns, "golden turns");
            var move = new SwapCandidate(new BoardPosition(0, 0), new BoardPosition(0, 1));
            Assert.AreEqual(ModeInputStatus.Ignored, s.Apply(ModeInput.Swap(Start + 999999, move.Source, move.Target)));

            var capped = ModeTemplate.FromJson(Json("endless.cap", "endless", 6, 6, 4, 0, 0, 0, 1, 7, 0));
            var c = New(capped, 777);
            Play(c, Start, 200, 50);
            Assert.AreEqual(ModeEndReason.TurnCap, c.EndReason);
            Assert.AreEqual(7, c.Turns);
        }

        internal const uint GoldenTinySeed = 1;
        internal const int GoldenTinyTurns = 30;

        [Test]
        public void Classic_EndsWhenMovesAreExhausted()
        {
            var s = ModeSession.Start(Game, Classic(), 99, Start, Sdk(Classic()), default(FeatureFlagRecord),
                FeatureFlagPolicy.Default);
            Play(s, Start, 200, 100);
            Assert.AreEqual(ModeEndReason.MovesExhausted, s.EndReason);
            Assert.AreEqual(20, s.Turns);
            Assert.AreEqual(0, s.MovesRemaining);
        }

        // ---------- records and rewards ----------

        private static ModeRunResult Finished(ModeTemplate t, uint seed, long stepMs = 1500, int maxSwaps = 1000)
        {
            var s = New(t, seed);
            Play(s, Start, stepMs, maxSwaps);
            return s.Result();
        }

        [Test]
        public void Records_AreSeparatePerMode_AndIgnoreDuplicates()
        {
            var ta = Finished(TimeAttack(), 12345);
            var capped = ModeTemplate.FromJson(Json("endless.cap", "endless", 6, 6, 4, 0, 0, 0, 1, 7, 0));
            var en = Finished(capped, 777, 800, 50);
            var book = new ModeRecordBook(Game);
            Assert.IsTrue(book.Submit(ta));
            Assert.IsFalse(book.Submit(ta), "same run ID counts once");
            Assert.IsTrue(book.Submit(en));

            var taRecord = book.Get(GameModeKind.TimeAttack, "ta.sprint60");
            var enRecord = book.Get(GameModeKind.Endless, "endless.cap");
            Assert.AreEqual(ta.Score, taRecord.BestScore);
            Assert.AreEqual(1, taRecord.Runs);
            Assert.AreEqual(ta.TimeToTargetMs, taRecord.BestTimeMs);
            Assert.IsTrue(ta.TimeToTargetMs > 0 && ta.TimeToTargetMs <= 60000);
            Assert.AreEqual(en.Score, enRecord.BestScore);
            Assert.AreEqual(en.ElapsedAtLastSwapMs, enRecord.BestTimeMs, "Endless best time = active ms at the last swap");
            Assert.IsNull(book.Get(GameModeKind.Endless, "ta.sprint60"));
            Assert.IsNull(book.Get(GameModeKind.Classic, "endless.cap"));
            Assert.Throws<InvalidOperationException>(() => New(TimeAttack(), 5).Result());
            Assert.Throws<ArgumentException>(() => new ModeRecordBook("Dino_Islands").Submit(ta));
        }

        [Test]
        public void Rewards_GoThroughTheLedger_CappedPerRunAndPerDay_RestartSafe_AndNotReEarnable()
        {
            var t = TimeAttack();
            var run1 = Finished(t, 12345);
            Assert.IsTrue(run1.Score >= 600, "fixture run reaches all three tiers");
            var ledger = new RewardLedger(Game, "player-1");
            var issuer = new ModeRewardIssuer(ledger);
            var day = new DateTime(2026, 11, 11, 10, 0, 0, DateTimeKind.Utc);

            var first = issuer.Issue(run1, t, TrustedUtcAnchor.Server(day));
            Assert.AreEqual(ModeRewardStatus.Granted, first.Status);
            Assert.AreEqual(3, first.Grants.Count);
            Assert.AreEqual(25L, ledger.Balance(CurrencyType.Coins), "5+10+20 capped to 25 per run");
            Assert.AreEqual(RewardLedgerKey.Derive(Game, "player-1", RewardSources.AltMode, "TimeAttack|2026-11-11|t1"),
                first.Grants[0].Key);
            var endOfDay = new DateTime(2026, 11, 12, 0, 0, 0, DateTimeKind.Utc).AddTicks(-1);
            Assert.IsTrue(first.Grants.All(g => g.Source == RewardSources.AltMode && g.ExpiresUtc == endOfDay));

            // The device clock never decides the reward day.
            var device = issuer.Issue(Finished(t, 70000), t, TrustedUtcAnchor.DeviceClock(day));
            Assert.AreEqual(ModeRewardStatus.Rejected, device.Status);
            Assert.AreEqual(RewardLedgerReasons.TrustedTimeAnchorRequired, device.Reason);

            // Restart: all local state is gone; the restored ledger alone blocks replays and holds the day cap.
            var restored = RewardLedger.FromSnapshot(ledger.ToSnapshot()).Ledger;
            var issuer2 = new ModeRewardIssuer(restored);
            var replayed = Replay(New(t, 12345).Also(s => Play(s, Start, 1500, 1000))).Result();
            Assert.AreEqual(run1.RunId, replayed.RunId);
            Assert.AreEqual(ModeRewardStatus.DuplicateRun, issuer2.Issue(replayed, t, TrustedUtcAnchor.Server(day)).Status);
            Assert.AreEqual(ModeRewardStatus.SeedReplayed,
                issuer2.Issue(Finished(t, 12345, 1400), t, TrustedUtcAnchor.Server(day)).Status, "same seed, other log");
            var second = issuer2.Issue(Finished(t, 22222), t, TrustedUtcAnchor.Server(day.AddHours(1)));
            Assert.AreEqual(15L, second.Grants.Sum(g => g.Amounts.Values.Sum()), "per-day cap 40 per mode");
            Assert.AreEqual(ModeRewardStatus.DailyCapReached,
                issuer2.Issue(Finished(t, 33333), t, TrustedUtcAnchor.Server(day.AddHours(2))).Status);
            Assert.AreEqual(40L, issuer2.GrantedOn(GameModeKind.TimeAttack, day));
            Assert.AreEqual(0L, issuer2.GrantedOn(GameModeKind.Endless, day), "caps are per mode");

            var issuer3 = new ModeRewardIssuer(RewardLedger.FromSnapshot(restored.ToSnapshot()).Ledger);
            Assert.AreEqual(ModeRewardStatus.DailyCapReached,
                issuer3.Issue(Finished(t, 33334), t, TrustedUtcAnchor.Server(day.AddHours(3))).Status, "second restart");
            Assert.AreEqual(ModeRewardStatus.Granted,
                issuer3.Issue(Finished(t, 44444), t, TrustedUtcAnchor.Server(day.AddDays(1))).Status, "next UTC day");
            Assert.AreEqual(ModeRewardStatus.Rejected,
                issuer3.Issue(Finished(t, 55555), Endless(), TrustedUtcAnchor.Server(day.AddDays(1))).Status, "template mismatch");
        }

        [Test]
        public void Rewards_AreNeverPremium_AndWonderPopUsesTheSameCappedLedgerPath()
        {
            Assert.Throws<FormatException>(() => ModeTemplate.FromJson(
                Json("ta.gems", "timeAttack", 6, 6, 4, 0, 60000, 0, 3, 500, 300).Replace("\"Coins\"", "\"PremiumGems\"")));
            Assert.Throws<FormatException>(() => ModeTemplate.FromJson(
                Json("ta.gems", "timeAttack", 6, 6, 4, 0, 60000, 0, 3, 500, 300).Replace("\"Coins\"", "\"cash\"")));

            var t = TimeAttack();
            var s = New(t, 12345, "Wonder_Pop");
            Play(s, Start, 1500, 1000);
            var ledger = new RewardLedger("Wonder_Pop", "family-player");
            var outcome = new ModeRewardIssuer(ledger).Issue(s.Result(), t,
                TrustedUtcAnchor.Server(new DateTime(2026, 11, 11, 10, 0, 0, DateTimeKind.Utc)));
            Assert.AreEqual(ModeRewardStatus.Granted, outcome.Status);
            Assert.AreEqual(25L, ledger.Balance(CurrencyType.Coins));
            Assert.AreEqual(0L, ledger.Balance(CurrencyType.PremiumGems));
        }

        // ---------- run identity, swap pacing, challenge, defaults ----------

        [Test]
        public void RunId_IsCanonical_AppliedSwapsOnly_AndBoundToTheMatcher()
        {
            var t = TimeAttack();
            var a = New(t, 12345);
            Play(a, Start, 1500, 10);
            // Same swaps, every timestamp shifted by 1 ms, plus a pause, an ignored swap and an extra tick.
            var log = new List<ModeInput>
            {
                ModeInput.Of(ModeInputKind.Tick, Start + 1),
                ModeInput.Of(ModeInputKind.Pause, Start + 1),
                ModeInput.Swap(Start + 1, new BoardPosition(0, 0), new BoardPosition(0, 1)),
                ModeInput.Of(ModeInputKind.Resume, Start + 1)
            };
            log.AddRange(a.Inputs.Select(i => i.Kind == ModeInputKind.Swap
                ? ModeInput.Swap(i.AtMs + 1, i.From, i.To)
                : ModeInput.Of(i.Kind, i.AtMs + 1)));
            var b = ModeSession.Replay(Game, t, 12345, Start, log, Sdk(t));
            Assert.AreEqual(a.Turns, b.Turns);
            Assert.AreEqual(a.Score, b.Score);
            Assert.AreEqual(a.RunId, b.RunId, "timestamps, ticks, pauses and ignored inputs are not identity");

            var otherPin = ModeSession.Replay(Game, t, 12345, Start, a.Inputs, Sdk(t, "libra-match3-sdk@other"));
            Assert.AreEqual(a.Score, otherPin.Score);
            Assert.AreNotEqual(a.RunId, otherPin.RunId);
            Assert.AreNotEqual(a.StateHash(), otherPin.StateHash());
            Assert.AreNotEqual(Sdk(t).TemplateHash(t), Sdk(t, "libra-match3-sdk@other").TemplateHash(t));
            Assert.AreNotEqual(a.RunId, Play2(New(t, 12346)).RunId);
            Assert.Throws<ArgumentException>(() => new ModeMatcher("", Sdk(t).MatchRuns));
        }

        [Test]
        public void Swaps_CloserThanTheMinimumInterval_AreRejected()
        {
            var s = New(TimeAttack(), 12345);
            Assert.AreEqual(ModeTemplate.DefaultMinSwapIntervalMs, s.Template.MinSwapIntervalMs);
            var m = FirstLegal(s);
            Assert.AreEqual(ModeInputStatus.Applied, s.Apply(ModeInput.Swap(Start + 1000, m.Source, m.Target)));
            m = FirstLegal(s);
            Assert.AreEqual(ModeInputStatus.Rejected, s.Apply(ModeInput.Swap(Start + 1149, m.Source, m.Target)));
            Assert.AreEqual(1, s.Turns);
            Assert.AreEqual(ModeInputStatus.Applied, s.Apply(ModeInput.Swap(Start + 1150, m.Source, m.Target)));

            var noPacing = ModeTemplate.FromJson(Json("ta.free", "timeAttack", 6, 6, 4, 0, 60000, 0, 3, 500, 300,
                ",\"minSwapIntervalMs\":0"));
            var f = New(noPacing, 12345);
            m = FirstLegal(f);
            f.Apply(ModeInput.Swap(Start, m.Source, m.Target));
            m = FirstLegal(f);
            Assert.AreEqual(ModeInputStatus.Applied, f.Apply(ModeInput.Swap(Start, m.Source, m.Target)));
            Assert.Throws<FormatException>(() => ModeTemplate.FromJson(Json("ta.bad", "timeAttack", 6, 6, 4, 0, 60000,
                0, 3, 500, 300, ",\"minSwapIntervalMs\":6000")));
        }

        [Test]
        public void Challenge_WinsOnTarget_LosesOnMoves_AndUsesTheTimedFlag()
        {
            var ch = ModeTemplate.FromJson(Json("challenge.t300", "challenge", 6, 6, 4, 0, 0, 10, 1, 500, 300));
            Assert.AreEqual(GameModeKind.Challenge, ch.Kind);
            var win = New(ch, 12345);
            Play(win, Start, 200, 100);
            Assert.AreEqual(ModeEndReason.TargetReached, win.EndReason);
            Assert.IsTrue(win.Score >= 300 && win.Turns <= 10);
            Assert.AreEqual(win.StateHash(), Replay(win).StateHash());

            var hard = ModeTemplate.FromJson(Json("challenge.hard", "challenge", 6, 6, 4, 0, 0, 3, 1, 500, 100000));
            var lose = New(hard, 12345);
            Play(lose, Start, 200, 100);
            Assert.AreEqual(ModeEndReason.MovesExhausted, lose.EndReason);
            Assert.AreEqual(3, lose.Turns);

            Assert.Throws<FormatException>(() => ModeTemplate.FromJson(Json("challenge.bad", "challenge", 6, 6, 4, 0, 0, 10, 1, 500, 0)));
            Assert.Throws<FormatException>(() => ModeTemplate.FromJson(Json("challenge.bad", "challenge", 6, 6, 4, 0, 60000, 10, 1, 500, 300)));
            Assert.IsFalse(AlternativeModeGate.CanStart(Game, GameModeKind.Challenge, Flag(GameModeKind.Endless),
                FeatureFlagPolicy.Default, out var reason));
            Assert.AreEqual("WrongFeatureFlag", reason);
            Assert.IsFalse(AlternativeModeGate.CanStart(Game, GameModeKind.Challenge, Flag(GameModeKind.TimeAttack, false),
                FeatureFlagPolicy.Default, out reason));
            Assert.AreEqual("FeatureFlagDisabled", reason);
        }

        [Test]
        public void TimedAndEndless_AreOffByDefault_InEveryTitleProfile()
        {
            var profiles = GameProfileCatalog.LoadPlaceholders();
            Assert.AreEqual(5, profiles.Count);
            foreach (var p in profiles)
            {
                Assert.IsFalse(p.IsEnabled("timed", EngineCompatibility.MinimumCapability), p.Id);
                Assert.IsFalse(p.IsEnabled("endless", EngineCompatibility.MinimumCapability), p.Id);
            }
            Assert.IsTrue(FeatureFlagRecord.Create(Game, LiveOpsFeature.TimedMode, 1, false, false).IsDefaultOff);
        }
    }

    internal static class ModeTestExtensions
    {
        public static T Also<T>(this T value, Action<T> action)
        {
            action(value);
            return value;
        }
    }
}
