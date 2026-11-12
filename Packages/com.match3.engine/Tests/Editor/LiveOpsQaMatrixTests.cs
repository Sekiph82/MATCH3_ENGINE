using System;
using System.Collections.Generic;
using System.Linq;
using Match3.Engine.Runtime;
using Match3.Engine.Runtime.Economy;
using Match3.Engine.Runtime.Identity;
using Match3.Engine.Runtime.LiveOps;
using Match3.Engine.Runtime.Privacy;
using NUnit.Framework;
using LR = Match3.Engine.Runtime.Economy.RewardLedgerReasons;
using CR = Match3.Engine.Runtime.LiveOps.ConfigReleaseReasons;
using RemoteConfigRecord = Match3.Engine.Runtime.LiveOps.RemoteConfigRecord;
using OfferType = Match3.Engine.Runtime.LiveOps.OfferType;

namespace Match3.Engine.Tests.Editor
{
    // ENG-M20-011 LiveOps full QA matrix. Five scenarios (clock skew, offline -> reconnect, expired event, bad config,
    // already claimed) x the five catalog games, exercising the EXISTING services together on one RewardLedger per
    // player: RewardLedger, WeeklyPersonalRecordService, ComebackCatchUpService, ModeRewardIssuer (alt modes),
    // TournamentService, ConfigBundleVerifier/ConfigReleasePipeline, RemoteConfigVersionGate, FeatureFlagGate,
    // LimitedTimeChallengeGate, WeeklyChallengeCalendarGate/WeeklyClaimLedger and OffersAndPricingGate.
    // Oracles, checked at the end of every case:
    //   - balance oracle: each asset balance == the sum of grants services reported as Applied (no duplicate grant);
    //   - no orphan data: the set of ledger keys == the set of keys services reported as Applied (every record traces
    //     to a service outcome; every rejected attempt left nothing);
    //   - every rejection asserts its stable reason (fail closed).
    // Wonder_Pop (family-safe): no paid asset, no tournament, catch-up never signals.
    // Deterministic: fixed server instants, fixed seeds (tournament uses a shared seed), a fixed row/column test
    // matcher; the only randomness is the test signing key and ECDSA nonces, which never affect outcomes.
    public sealed class LiveOpsQaMatrixTests
    {
        private const string Player = "p1";
        private static readonly DateTime Now = new DateTime(2026, 11, 11, 12, 0, 0, DateTimeKind.Utc); // Wed, ISO 2026-W46
        private static readonly DateTime LastActive = Now.AddDays(-20);
        private static readonly TimeSpan Skew = TimeSpan.FromDays(3);
        private static readonly string[] TwoApprovers = { "rev-1", "rev-2" };
        private static readonly string Cfg = new string('c', 64);
        private static readonly string Brd = new string('b', 64);
        private static readonly WeeklyPersonalRecordPolicy WeeklyPolicy = new WeeklyPersonalRecordPolicy(2, 10, "Coins", 25, 1);
        private static readonly ModeMatcher Matcher = new ModeMatcher("qa-row-col-matcher", Runs);

        // Test-only signing key, generated per run; only its public half is pinned (in memory).
        private static readonly byte[] SignKey;
        private static readonly byte[] PinnedKey;
        private static readonly byte[] RogueKey;

        static LiveOpsQaMatrixTests()
        {
            EcdsaP256.GenerateKey(out SignKey, out PinnedKey);
            EcdsaP256.GenerateKey(out RogueKey, out _);
        }

        private static IEnumerable<string> Games() => GameProfileCatalog.Ids;

        private static bool FamilySafe(string game) => EconomyBalanceSimulator.IsFamilySafeProfile(game);

        private static TrustedUtcAnchor Server(DateTime t) => TrustedUtcAnchor.Server(t);

        // ---------- oracle ----------

        private sealed class World
        {
            public readonly string Game;
            public RewardLedger Ledger;
            private readonly Dictionary<string, long> _expected = new Dictionary<string, long>(StringComparer.Ordinal);
            private readonly HashSet<string> _traced = new HashSet<string>(StringComparer.Ordinal);

            public World(string game)
            {
                Game = game;
                Ledger = new RewardLedger(game, Player);
            }

            /// <summary>Records a grant a service reported as applied; the same key twice is a duplicate grant.</summary>
            public void Applied(RewardLedgerRecord r)
            {
                Assert.IsNotNull(r);
                Assert.IsTrue(_traced.Add(r.Key), Game + ": key applied twice " + r.Key);
                foreach (var kv in r.Amounts) _expected[kv.Key] = (_expected.TryGetValue(kv.Key, out var v) ? v : 0) + kv.Value;
            }

            public void Applied(RewardLedgerGrantResult g)
            {
                Assert.AreEqual(RewardLedgerOutcome.Applied, g.Outcome, Game + " " + g.Reason);
                Applied(g.Record);
            }

            public void Oracle()
            {
                foreach (var asset in RewardLedger.KnownAssets)
                    Assert.AreEqual(_expected.TryGetValue(asset, out var e) ? e : 0L, Ledger.Balance(asset), Game + " balance " + asset);
                var keys = RewardSources.All.SelectMany(s => Ledger.InstanceIds(s).Select(i => RewardLedgerKey.Derive(Game, Player, s, i))).ToList();
                CollectionAssert.AreEquivalent(_traced, keys, Game + ": every ledger record traces to a service-reported grant");
                if (FamilySafe(Game)) Assert.AreEqual(0L, Ledger.Balance(RewardLedger.PaidAssetId), "family-safe: no paid asset");
            }
        }

        // ---------- service helpers ----------

        private static Dictionary<string, long> Coins(long n) => new Dictionary<string, long> { ["Coins"] = n };

        private static RewardLedgerRequest Daily(string game, string day, long coins, DateTime expires) =>
            new RewardLedgerRequest(game, Player, RewardSources.Daily, day, Coins(coins), Now, expires);

        private static FeatureFlagRecord Flag(string game, LiveOpsFeature feature) => FeatureFlagRecord.Create(game, feature, 1, true, true);

        private static VerifiedProgressionResult Result(string game, string id, int score, string week = "2026-W46") =>
            new VerifiedProgressionResult(id, game, Player, "classic", "lvl.1", "lvl.1", true, false, true, score, 1, 1, 0, week, true);

        private static ReplayTrace Trace(string game, uint seed) =>
            new ReplayTrace(game, Cfg, seed, new[] { new ReplayStep(1, new BoardPosition(0, 0), new BoardPosition(0, 1), Brd) });

        private static WeeklyRecordOutcome Submit(WeeklyPersonalRecordService svc, string id, int score, uint seed,
            DateTime completion, TrustedUtcAnchor now) =>
            svc.Submit(Result(svc.GameId, id, score, IsoWeekWindow.WeekId(completion)), Trace(svc.GameId, seed), completion, now,
                Flag(svc.GameId, LiveOpsFeature.WeeklyPersonalBest), FeatureFlagPolicy.Default);

        private static ComebackCatchUpService CatchUp(World w, CatchUpWindowState state = null) =>
            new ComebackCatchUpService(new GameProfile(w.Game, "placeholder." + w.Game.ToLowerInvariant(), ProfileAgeRule.General,
                    ProfileContentChannel.Development, new[] { new FeatureFlag("catch_up", EngineCompatibility.MinimumCapability) }),
                w.Ledger, CatchUpPolicy.Standard, false, Server(LastActive), state);

        // Time Attack sprint: one 3-match (30 points) reaches every tier; per run cap 25, per day cap 40.
        private static ModeTemplate Sprint() => ModeTemplate.FromJson(
            "{\"schema\":\"alt-mode/1\",\"templateId\":\"qa.sprint\",\"version\":1,\"kind\":\"timeAttack\",\"rows\":6,\"columns\":6," +
            "\"palette\":[\"A\",\"B\",\"C\",\"D\"],\"startColors\":4,\"rampEveryTurns\":0,\"timeLimitMs\":5000,\"moveLimit\":0," +
            "\"shuffleLives\":3,\"backgroundGraceMs\":3000,\"pointsPerCell\":10,\"maxTurns\":500,\"targetScore\":0,\"rewardId\":\"Coins\"," +
            "\"rewardTiers\":[{\"score\":10,\"amount\":5},{\"score\":20,\"amount\":10},{\"score\":30,\"amount\":20}]," +
            "\"perRunRewardCap\":25,\"perDayRewardCap\":40}");

        /// <summary>Test match authority: maximal same-content row/column runs of length >= 3.</summary>
        private static IReadOnlyList<MatchRunProjection> Runs(IReadOnlyDictionary<BoardPosition, string> board)
        {
            var runs = new List<MatchRunProjection>();
            if (board.Count == 0) return runs;
            var rows = board.Keys.Max(p => p.Row) + 1;
            var cols = board.Keys.Max(p => p.Column) + 1;
            foreach (var horizontal in new[] { true, false })
                for (var a = 0; a < (horizontal ? rows : cols); a++)
                {
                    var run = new List<BoardPosition>();
                    string color = null;
                    for (var b = 0; b <= (horizontal ? cols : rows); b++)
                    {
                        string c = null;
                        var at = horizontal ? new BoardPosition(a, Math.Min(b, cols)) : new BoardPosition(Math.Min(b, rows), a);
                        if (b < (horizontal ? cols : rows)) board.TryGetValue(at, out c);
                        if (c != null && c == color) { run.Add(at); continue; }
                        if (run.Count >= 3) runs.Add(new MatchRunProjection(horizontal ? MatchRunAxis.Horizontal : MatchRunAxis.Vertical, color, run));
                        run = c == null ? new List<BoardPosition>() : new List<BoardPosition> { at };
                        color = c;
                    }
                }
            return runs;
        }

        private static ModeSession Play(string game, ModeTemplate t, uint seed)
        {
            var s = ModeSession.Start(game, t, seed, 1000, Matcher,
                FeatureFlagRecord.Create(game, LiveOpsFeature.TimedMode, 1, true, false), FeatureFlagPolicy.Default);
            var board = s.Board;
            foreach (var p in board.Keys.OrderBy(p => p.Row).ThenBy(p => p.Column))
                foreach (var q in new[] { new BoardPosition(p.Row, p.Column + 1), new BoardPosition(p.Row + 1, p.Column) })
                {
                    if (!board.ContainsKey(q)) continue;
                    var swapped = board.ToDictionary(kv => kv.Key, kv => kv.Value);
                    swapped[p] = board[q];
                    swapped[q] = board[p];
                    if (Runs(swapped).Count == 0) continue;
                    Assert.AreEqual(ModeInputStatus.Applied, s.Apply(ModeInput.Swap(1200, p, q)));
                    s.Apply(ModeInput.Of(ModeInputKind.Tick, 1000 + t.TimeLimitMs + 1));
                    Assert.AreEqual(ModePhase.Ended, s.Phase);
                    Assert.GreaterOrEqual(s.Score, 30, "one 3-match reaches every reward tier");
                    return s;
                }
            Assert.Fail("no legal opening move");
            return null;
        }

        private static readonly ChildAccountPolicy AdultOptIn = new ChildAccountPolicy(true, true, "QA adult opt-in tournament");

        private static TournamentEligibility Elig(string game) =>
            new TournamentEligibility(ChildProfile.Create(game, AgeCategory.Adult, false),
                ConsentRecord.Create(game, ConsentState.Granted, false, false), "DE", AdultOptIn);

        /// <summary>Single-player tournament [start, end) the player wins with a verified run.</summary>
        private static TournamentService WonTournament(string game, string id, DateTime start, DateTime end)
        {
            var template = new TournamentTemplate(id, Sprint(), start, end, new[] { "DE" }, 5, TimeSpan.Zero, 2, "Coins",
                new[] { new TournamentPrizeTier(1, 100) }, 777, 2000, TimeSpan.FromDays(7));
            var svc = new TournamentService(game, template, Matcher);
            var flag = Flag(game, LiveOpsFeature.TournamentMode);
            Assert.IsTrue(svc.TryAdmit(Player, Elig(game), true, "qa", flag, FeatureFlagPolicy.Default, start.AddHours(1), out var why), why);
            var ticket = svc.IssueAttempt(Player, Elig(game), flag, FeatureFlagPolicy.Default, start.AddHours(1), out why);
            Assert.IsNotNull(ticket, why);
            var run = Play(game, template.Mode, ticket.Seed);
            Assert.AreEqual(TournamentSubmitStatus.Accepted, svc.Submit(ticket, 1000, run.Inputs, run.Score,
                ticket.ServerIssuedUtc.AddMinutes(2), Elig(game), flag, FeatureFlagPolicy.Default));
            return svc;
        }

        private static TournamentCloseResult Close(TournamentService svc, World w, DateTime at) =>
            svc.Close(Server(at), Flag(w.Game, LiveOpsFeature.TournamentMode), FeatureFlagPolicy.Default, _ => Elig(w.Game), _ => w.Ledger);

        private static ConfigReleasePipeline Pipeline(string game, byte[] key = null, string keyId = "qa-key")
        {
            var k = key ?? SignKey;
            return new ConfigReleasePipeline(game, keyId, payload => EcdsaP256.Sign(k, payload));
        }

        private static ConfigTrustedKeys Pinned() =>
            new ConfigTrustedKeys(new[] { new KeyValuePair<string, byte[]>("qa-key", PinnedKey) });

        private static ConfigOperationResult ToProd(ConfigReleasePipeline p, string bundle, DateTime at)
        {
            foreach (var env in new[] { ConfigEnvironment.Dev, ConfigEnvironment.Staging })
                Assert.IsTrue(p.Promote(bundle, env, "author-1", null, Server(at)).Succeeded);
            var op = p.Promote(bundle, ConfigEnvironment.Prod, "author-1", TwoApprovers, Server(at));
            Assert.IsTrue(op.Succeeded, op.Reason);
            return op;
        }

        private static ConfigAcceptResult Accept(string bundle, string block, ConfigClientState s, TrustedUtcAnchor at,
            ConfigTrustedKeys keys = null) =>
            ConfigBundleVerifier.TryAccept(bundle, block, keys ?? Pinned(), s, at);

        private static long CoinsOf(string bundleText)
        {
            Assert.IsTrue(ConfigBundle.TryParse(bundleText, out var b, out var why), why);
            return long.Parse(b.RemoteConfig(RemoteConfigKey.CoinRewardAmount));
        }

        // ---------- matrix ----------

        [TestCaseSource(nameof(Games))]
        public void ClockSkew_DeviceClockAheadOrBehindNeverDecides(string game)
        {
            var w = new World(game);
            foreach (var device in new[] { TrustedUtcAnchor.DeviceClock(Now + Skew), TrustedUtcAnchor.DeviceClock(Now - Skew) })
            {
                Assert.AreEqual(LR.TrustedTimeAnchorRequired, w.Ledger.TryGrant(Daily(game, "d-2026-11-11", 10, Now.Date.AddDays(1)), device).Reason);
                Assert.AreEqual(CatchUpReasons.TrustedTimeAnchorRequired, CatchUp(w).Evaluate(device, Server(LastActive)).Reason);
                Assert.AreEqual(WeeklyRecordReasons.TrustedTimeAnchorRequired,
                    Submit(new WeeklyPersonalRecordService(game, Player, WeeklyPolicy, w.Ledger), "r0", 100, 1, Now, device).Reason);
                Assert.AreEqual(LR.TrustedTimeAnchorRequired, new ModeRewardIssuer(w.Ledger).Issue(Play(game, Sprint(), 11).Result(), Sprint(), device).Reason);
            }
            // Server time decides expiry: a device clock that is behind cannot reopen yesterday's daily.
            Assert.AreEqual(LR.SourceExpired, w.Ledger.TryGrant(Daily(game, "d-2026-11-10", 10, Now.Date), Server(Now)).Reason);
            w.Applied(w.Ledger.TryGrant(Daily(game, "d-2026-11-11", 10, Now.Date.AddDays(1)), Server(Now)));

            // Catch-up: last-active must be server-anchored; a "future" last-active or a backwards server clock is a rollback.
            var cu = CatchUp(w);
            Assert.AreEqual(CatchUpReasons.TrustedLastActiveRequired, cu.Evaluate(Server(Now), TrustedUtcAnchor.DeviceClock(LastActive)).Reason);
            Assert.AreEqual(CatchUpReasons.ClockRollbackDetected, cu.Evaluate(Server(Now), Server(Now + Skew)).Reason);
            Assert.AreEqual(CatchUpReasons.InsufficientAbsence, cu.Evaluate(Server(Now), Server(Now - Skew)).Reason);
            var open = cu.Evaluate(Server(Now), Server(LastActive));
            Assert.AreEqual(CatchUpStatus.Active, open.Status);
            Assert.AreEqual(!FamilySafe(game), open.EmitEligibleSignal, "family-safe titles never get a targeted signal");
            Assert.AreEqual(CatchUpReasons.ClockRollbackDetected, cu.Evaluate(Server(Now.AddHours(-1)), Server(LastActive)).Reason);
            Assert.AreEqual(CatchUpReasons.TrustedTimeAnchorRequired,
                cu.RecordWin(TrustedUtcAnchor.DeviceClock(Now.AddHours(1)), Result(game, "w1", 100), Now.AddMinutes(30)).Reason);
            Assert.AreEqual(CatchUpReasons.CompletionOutsideWindow,
                cu.RecordWin(Server(Now.AddHours(1)), Result(game, "w1", 100), Now + Skew).Reason, "device-stamped future completion");
            w.Applied(cu.RecordWin(Server(Now.AddHours(1)), Result(game, "w1", 100), Now.AddMinutes(30)));

            // Weekly record: a device-stamped completion in the future is rejected by server time.
            var wk = new WeeklyPersonalRecordService(game, Player, WeeklyPolicy, w.Ledger);
            Assert.AreEqual(WeeklyRecordReasons.CompletionInFuture, Submit(wk, "r1", 100, 1, Now.AddHours(2), Server(Now)).Reason);
            Assert.AreEqual(WeeklyRecordStatus.Participated, Submit(wk, "r1", 100, 1, Now, Server(Now)).Status);

            // Config: device clock rejected; a record issued after the trusted anchor + skew is rejected.
            var op = ToProd(Pipeline(game), LiveOpsOperationsTests.Bundle(1, game: game), Now);
            var s = new ConfigClientState(game, ConfigEnvironment.Prod);
            Assert.AreEqual(CR.TrustedTimeAnchorRequired, Accept(op.BundleText, op.SignatureBlock, s, TrustedUtcAnchor.DeviceClock(Now)).Reason);
            Assert.AreEqual(CR.IssuedInFuture, Accept(op.BundleText, op.SignatureBlock, s, Server(Now.AddHours(-1))).Reason);
            Assert.IsTrue(Accept(op.BundleText, op.SignatureBlock, s, Server(Now)).Accepted);

            Assert.IsFalse(WeeklyChallengeCalendarGate.CanClaimReward(game, CalendarEventRecord.Create(game,
                CalendarEventType.OfflineRecovery, "2026-W46", false, true, false), CalendarEventPolicy.Default, out var why));
            Assert.AreEqual("UtcAnchorRequired", why);
            w.Oracle();
        }

        [TestCaseSource(nameof(Games))]
        public void OfflineThenReconnect_SnapshotsRestore_MergeNeverDoubleCounts(string game)
        {
            var w = new World(game);
            // Online session.
            var p = Pipeline(game);
            var op1 = ToProd(p, LiveOpsOperationsTests.Bundle(1, game: game), Now);
            var cfg = Accept(op1.BundleText, op1.SignatureBlock, new ConfigClientState(game, ConfigEnvironment.Prod), Server(Now));
            Assert.IsTrue(cfg.Accepted, cfg.Reason);
            var daily = Daily(game, "d-2026-11-11", CoinsOf(op1.BundleText), Now.Date.AddDays(1));
            w.Applied(w.Ledger.TryGrant(daily, Server(Now)));

            var wk = new WeeklyPersonalRecordService(game, Player, WeeklyPolicy, w.Ledger);
            var wkBefore = wk.ExportSnapshot();
            Assert.AreEqual(WeeklyRecordStatus.Participated, Submit(wk, "r1", 100, 1, Now, Server(Now)).Status);
            var improved = Submit(wk, "r2", 150, 2, Now, Server(Now));
            Assert.AreEqual(WeeklyRecordStatus.Improved, improved.Status);
            w.Applied(improved.Grant);

            var cu = CatchUp(w);
            Assert.AreEqual(CatchUpStatus.Active, cu.Evaluate(Server(Now), Server(LastActive)).Status);
            w.Applied(cu.RecordWin(Server(Now.AddHours(1)), Result(game, "w1", 100), Now.AddMinutes(30)));
            w.Applied(cu.RecordWin(Server(Now.AddHours(4)), Result(game, "w2", 100), Now.AddHours(4)));

            var run = Play(game, Sprint(), 21).Result();
            var alt = new ModeRewardIssuer(w.Ledger).Issue(run, Sprint(), Server(Now));
            Assert.AreEqual(ModeRewardStatus.Granted, alt.Status, alt.Reason);
            foreach (var g in alt.Grants) w.Applied(g);

            // Persist every service through its own snapshot API, then go offline.
            var ledgerSnap = w.Ledger.ToSnapshot();
            var wkSnap = wk.ExportSnapshot();
            var wkSeq = wk.Sequence;
            var cuSnap = cu.State.ToCanonical();
            var cfgSnap = cfg.State.ToCanonical();

            var local = RewardLedger.FromSnapshot(ledgerSnap).Ledger;
            var firstClear = new RewardLedgerRequest(game, Player, RewardSources.FirstClear, "lvl.7", Coins(30), Now.AddHours(2));
            var offlineFirst = local.TryGrant(firstClear, TrustedUtcAnchor.DeviceClock(Now.AddHours(2)));
            Assert.AreEqual(RewardLedgerOutcome.Applied, offlineFirst.Outcome, "permanent earned facts need no server time");
            Assert.AreEqual(LR.TrustedTimeAnchorRequired, local.TryGrant(Daily(game, "d-2026-11-12", 10, Now.Date.AddDays(2)),
                TrustedUtcAnchor.DeviceClock(Now.AddDays(1))).Reason, "windowed claims wait for reconnect");
            // Another device applied the same first clear plus a level win against the cloud copy.
            var cloud = RewardLedger.FromSnapshot(ledgerSnap).Ledger;
            Assert.AreEqual(RewardLedgerOutcome.Applied, cloud.TryGrant(firstClear, Server(Now.AddHours(2))).Outcome);
            var levelWin = cloud.TryGrant(new RewardLedgerRequest(game, Player, RewardSources.LevelWin, "win-9", Coins(5), Now.AddHours(3)),
                Server(Now.AddHours(3)));
            Assert.AreEqual(RewardLedgerOutcome.Applied, levelWin.Outcome);

            // Reconnect: exact union, no conflicts, each key counted once.
            var merged = RewardLedger.Merge(local, cloud);
            Assert.IsTrue(merged.Succeeded, merged.Reason);
            CollectionAssert.IsEmpty(merged.ConflictedKeys);
            w.Ledger = merged.Ledger;
            w.Applied(offlineFirst.Record);
            w.Applied(levelWin.Record);
            Assert.AreEqual(RewardLedgerOutcome.Duplicate, w.Ledger.TryGrant(daily, Server(Now.AddHours(4))).Outcome, "queued replay of a granted daily");

            // Restore services from their snapshots on the merged ledger; replays never grant again.
            var wk2 = WeeklyPersonalRecordService.FromSnapshot(wkSnap, game, Player, WeeklyPolicy, w.Ledger, out var why, wkSeq);
            Assert.IsNotNull(wk2, why);
            Assert.IsNull(WeeklyPersonalRecordService.FromSnapshot(wkBefore, game, Player, WeeklyPolicy, w.Ledger, out why, wkSeq));
            Assert.AreEqual(WeeklyRecordReasons.SnapshotRollback, why);
            Assert.AreEqual(WeeklyRecordStatus.Duplicate, Submit(wk2, "r2", 150, 2, Now, Server(Now.AddHours(4))).Status);
            var lost = new WeeklyPersonalRecordService(game, Player, WeeklyPolicy, w.Ledger); // local weekly state lost entirely
            Submit(lost, "r1", 100, 1, Now, Server(Now.AddHours(4)));
            var regrant = Submit(lost, "r2", 150, 2, Now, Server(Now.AddHours(4)));
            Assert.AreEqual(WeeklyRecordReasons.AlreadyGranted, regrant.Reason, "the ledger slot, not local state, holds the grant");

            Assert.IsTrue(CatchUpWindowState.TryParse(cuSnap, out var cuState));
            var again = CatchUp(w, cuState).RecordWin(Server(Now.AddHours(5)), Result(game, "w1", 100), Now.AddMinutes(30));
            Assert.AreEqual(RewardLedgerOutcome.Duplicate, again.Outcome, again.Reason);
            var rederived = CatchUp(w);
            Assert.AreEqual(CatchUpStatus.Active, rederived.Evaluate(Server(Now.AddHours(5)), Server(LastActive)).Status);
            again = rederived.RecordWin(Server(Now.AddHours(5)), Result(game, "w2", 100), Now.AddHours(4));
            Assert.AreEqual(RewardLedgerOutcome.Duplicate, again.Outcome, "re-derived window: " + again.Reason);
            // Finding (ComebackCatchUpService, not changed here): a lost-state window is re-derived from its earliest grant
            // time, so a win completed between the original open and its grant now fails closed instead of Duplicate.
            again = rederived.RecordWin(Server(Now.AddHours(5)), Result(game, "w1", 100), Now.AddMinutes(30));
            Assert.AreNotEqual(RewardLedgerOutcome.Applied, again.Outcome);
            Assert.That(again.Outcome == RewardLedgerOutcome.Duplicate || again.Reason == CatchUpReasons.CompletionOutsideWindow, again.Reason);

            Assert.AreEqual(ModeRewardStatus.DuplicateRun, new ModeRewardIssuer(w.Ledger).Issue(run, Sprint(), Server(Now.AddHours(5))).Status);

            Assert.IsTrue(ConfigClientState.TryParse(cfgSnap, out var restored));
            Assert.AreEqual(CR.StaleSequence, Accept(op1.BundleText, op1.SignatureBlock, restored, Server(Now.AddHours(5))).Reason);
            var op2 = ToProd(p, LiveOpsOperationsTests.Bundle(2, coins: "120", game: game), Now.AddHours(1));
            Assert.IsTrue(Accept(op2.BundleText, op2.SignatureBlock, restored, Server(Now.AddHours(5))).Accepted, "release made while offline");
            w.Oracle();
        }

        [TestCaseSource(nameof(Games))]
        public void ExpiredEvent_NeverActivatesNorGrants(string game)
        {
            var w = new World(game);
            Assert.IsFalse(LimitedTimeChallengeGate.CanActivateEvent(game, ChallengeEventRecord.Create(game,
                ChallengeEventType.SeasonalContent, false, true, false), ChallengeEventPolicy.Default, out var why));
            Assert.AreEqual(LimitedTimeChallengeGate.UseLastKnownGoodReason, why);
            Assert.IsFalse(LimitedTimeChallengeGate.CanActivateEvent(game, ChallengeEventRecord.Create(game,
                ChallengeEventType.SeasonalContent, false, false, false), ChallengeEventPolicy.Default, out why));
            Assert.AreEqual(LimitedTimeChallengeGate.ExpiredEventRequiresFallbackReason, why);

            var w46End = IsoWeekWindow.EndUtc("2026-W46");
            Assert.AreEqual(LR.SourceExpired, w.Ledger.TryGrant(new RewardLedgerRequest(game, Player, RewardSources.Weekly, "2026-W46",
                Coins(50), Now, w46End), Server(w46End.AddMinutes(1))).Reason);

            // A W46 improvement synced after rollover updates its record but never grants retroactively.
            var wk = new WeeklyPersonalRecordService(game, Player, WeeklyPolicy, w.Ledger);
            Assert.AreEqual(WeeklyRecordStatus.Participated, Submit(wk, "r1", 100, 1, Now, Server(Now)).Status);
            var late = Submit(wk, "r2", 200, 2, Now.AddDays(2), Server(w46End.AddHours(1)));
            Assert.AreEqual(WeeklyRecordStatus.Improved, late.Status);
            Assert.AreEqual(WeeklyRecordReasons.WindowClosed, late.Reason);
            Assert.IsNull(late.Grant);

            var cu = CatchUp(w);
            Assert.AreEqual(CatchUpStatus.Active, cu.Evaluate(Server(Now), Server(LastActive)).Status);
            var afterWindow = Now.AddDays(CatchUpPolicy.Standard.WindowLengthDays);
            Assert.AreEqual(CatchUpReasons.WindowExpired,
                cu.RecordWin(Server(afterWindow), Result(game, "w1", 100), afterWindow.AddMinutes(-1)).Reason);
            Assert.AreEqual(CatchUpStatus.Ineligible, cu.Evaluate(Server(afterWindow), Server(LastActive)).Status);

            if (game == "Wonder_Pop")
                Assert.Throws<InvalidOperationException>(() => WonTournament(game, "cup.expired", Now.AddDays(-1), Now));
            else
            {
                var cup = WonTournament(game, "cup.expired", Now.AddDays(-1), Now);
                Assert.AreEqual(TournamentSubmitStatus.Closed, cup.Submit(null, 1000, new ModeInput[0], 0, Now, Elig(game),
                    Flag(game, LiveOpsFeature.TournamentMode), FeatureFlagPolicy.Default));
                var close = Close(cup, w, Now + cup.Template.PrizeClaimWindow + TimeSpan.FromHours(1));
                Assert.AreEqual(0, close.Granted.Count);
                Assert.AreEqual(LR.SourceExpired, close.Withheld.Single().Reason, "prize claim window expired");
            }
            w.Oracle();
        }

        [TestCaseSource(nameof(Games))]
        public void BadConfig_FailsClosed_AndLastKnownGoodStaysInForce(string game)
        {
            var w = new World(game);
            var p = Pipeline(game);
            var op1 = ToProd(p, LiveOpsOperationsTests.Bundle(1, game: game), Now);
            var lkg = Accept(op1.BundleText, op1.SignatureBlock, new ConfigClientState(game, ConfigEnvironment.Prod), Server(Now)).State;
            var lkgText = op1.BundleText;
            var op2 = ToProd(p, LiveOpsOperationsTests.Bundle(2, coins: "120", game: game), Now);
            var at = Server(Now.AddMinutes(1));
            var other = GameProfileCatalog.Ids.First(g => g != game);
            var foreign = ToProd(Pipeline(other), LiveOpsOperationsTests.Bundle(2, game: other), Now);
            var rogue = ToProd(Pipeline(game, RogueKey), LiveOpsOperationsTests.Bundle(2, coins: "120", game: game), Now);
            var staging = Pipeline(game);
            staging.Promote(op2.BundleText, ConfigEnvironment.Dev, "author-1", null, at);
            var stagingOp = staging.Promote(op2.BundleText, ConfigEnvironment.Staging, "author-1", null, at);

            var bad = new[]
            {
                (op2.BundleText, (string)null, CR.MalformedSignatureBlock),                               // unsigned
                (op2.BundleText, System.Text.RegularExpressions.Regex.Replace(op2.SignatureBlock, "\"signature\":\"[^\"]*\"", "\"signature\":\"\""), CR.BadSignature),
                (LiveOpsOperationsTests.Bundle(2, coins: "100000", game: game), op2.SignatureBlock, CR.DigestMismatch), // tampered
                (LiveOpsOperationsTests.Bundle(2, coins: "120", game: game, schema: 0), op2.SignatureBlock, CR.SchemaVersionTooOld),
                (foreign.BundleText, foreign.SignatureBlock, CR.WrongGame),
                (rogue.BundleText, rogue.SignatureBlock, CR.BadSignature),                                // unpinned signer, same key id
                (op1.BundleText, op1.SignatureBlock, CR.StaleSequence),                                    // rollback attack / replay
                (stagingOp.BundleText, stagingOp.SignatureBlock, CR.WrongEnvironment)
            };
            foreach (var (bundle, block, reason) in bad)
            {
                var r = Accept(bundle, block, lkg, at);
                Assert.AreEqual(reason, r.Reason, game + " " + reason);
                Assert.AreSame(lkg, r.State, "rejection keeps last-known-good");
            }
            // The economy keeps using last-known-good values while bad config is rejected.
            w.Applied(w.Ledger.TryGrant(Daily(game, "d-2026-11-11", CoinsOf(lkgText), Now.Date.AddDays(1)), at));

            // Per-record gate: unsigned non-gameplay keys, unsigned Wonder_Pop config and old schemas never apply.
            var permissive = new RemoteConfigPolicy(false, 2, "QA permissive signature policy, schema floor 2");
            Assert.IsFalse(RemoteConfigVersionGate.CanApplyConfig(game, RemoteConfigRecord.Create(game, RemoteConfigKey.OfferPresentation,
                "x", 2, false, true), permissive, out var why));
            Assert.AreEqual(FamilySafe(game) ? "WonderPopUnsignedConfigProhibited" : "SignatureRequiredForKey", why);
            Assert.IsFalse(RemoteConfigVersionGate.CanApplyConfig(game, RemoteConfigRecord.Create(game, RemoteConfigKey.CoinRewardAmount,
                "5", 2, false, false), permissive, out why));
            Assert.AreEqual(FamilySafe(game) ? "WonderPopUnsignedConfigProhibited" : "FallbackRequired", why);
            Assert.IsFalse(RemoteConfigVersionGate.CanApplyConfig(game, RemoteConfigRecord.Create(game, RemoteConfigKey.CoinRewardAmount,
                "5", 1, true, true), permissive, out why));
            Assert.AreEqual("SchemaVersionTooOld", why);

            // The good release is then accepted and drives the next grant.
            var good = Accept(op2.BundleText, op2.SignatureBlock, lkg, at);
            Assert.IsTrue(good.Accepted, good.Reason);
            w.Applied(w.Ledger.TryGrant(Daily(game, "d-2026-11-12", CoinsOf(op2.BundleText), Now.Date.AddDays(2)), at));
            Assert.AreEqual(220L, w.Ledger.Balance("Coins"));
            w.Oracle();
        }

        [TestCaseSource(nameof(Games))]
        public void AlreadyClaimed_OfferRewardEventSlotWinRunAndPrize_NeverGrantTwice(string game)
        {
            var w = new World(game);
            var wp = game == "Wonder_Pop";
            var daily = Daily(game, "d-2026-11-11", 10, Now.Date.AddDays(1));
            w.Applied(w.Ledger.TryGrant(daily, Server(Now)));
            Assert.AreEqual(RewardLedgerOutcome.Duplicate, w.Ledger.TryGrant(daily, Server(Now.AddMinutes(1))).Outcome);
            Assert.AreEqual(LR.IdempotencyConflict, w.Ledger.TryGrant(Daily(game, "d-2026-11-11", 99, Now.Date.AddDays(1)), Server(Now)).Reason);

            var claims = new WeeklyClaimLedger();
            Assert.IsTrue(claims.TryClaim(game, Player, "2026-W46"));
            Assert.IsFalse(claims.TryClaim(game, Player, "2026-W46"));
            Assert.IsFalse(WeeklyChallengeCalendarGate.CanClaimReward(game, CalendarEventRecord.Create(game,
                CalendarEventType.WeeklyChallenge, "2026-W46", true, false, true), CalendarEventPolicy.Default, out var why));
            Assert.AreEqual(wp ? "WonderPopDuplicateClaimProhibited" : "DuplicateClaimDetected", why);

            // Offers: presentation gate, then the claimed purchase reward through the ledger (order id = instance).
            Assert.IsFalse(OffersAndPricingGate.CanPresentOffer(game, OfferRecord.Create(game, OfferType.StarterBundle, true, true, false),
                OfferPolicy.Default, out why));
            Assert.AreEqual(wp ? "WonderPopRealMoneyRequiresStoreAuth" : "RealMoneyOfferRequiresStoreAuth", why);
            Assert.IsFalse(OffersAndPricingGate.CanPresentOffer(game, OfferRecord.Create(game, OfferType.BonusBoosterEvent, false, false, false),
                OfferPolicy.Default, out why));
            Assert.AreEqual("OfferRequiresOwnerApproval", why);
            var order = new RewardLedgerRequest(game, Player, RewardSources.Purchase, "order-1",
                new Dictionary<string, long> { [RewardLedger.PaidAssetId] = 50 }, Now);
            if (wp)
            {
                Assert.AreEqual(LR.PaidGrantNotAllowed, w.Ledger.TryGrant(order, Server(Now)).Reason);
                Assert.AreEqual(LR.PaidGrantNotAllowed, w.Ledger.TryGrant(new RewardLedgerRequest(game, Player, RewardSources.Chest, "c1",
                    new Dictionary<string, long> { [RewardLedger.PaidAssetId] = 1 }, Now), Server(Now)).Reason, "no paid asset from any source");
            }
            else
            {
                w.Applied(w.Ledger.TryGrant(order, Server(Now)));
                Assert.AreEqual(RewardLedgerOutcome.Duplicate, w.Ledger.TryGrant(order, Server(Now.AddHours(1))).Outcome, "already-claimed offer");
            }

            // Weekly record slots: cap 2, +10 minimum step; duplicate result and replayed trace never grant.
            var wk = new WeeklyPersonalRecordService(game, Player, WeeklyPolicy, w.Ledger);
            Assert.AreEqual(WeeklyRecordStatus.Participated, Submit(wk, "r1", 100, 1, Now, Server(Now)).Status);
            w.Applied(Submit(wk, "r2", 120, 2, Now, Server(Now)).Grant);
            w.Applied(Submit(wk, "r3", 140, 3, Now, Server(Now)).Grant);
            Assert.AreEqual(WeeklyRecordReasons.WindowCapReached, Submit(wk, "r4", 160, 4, Now, Server(Now)).Reason);
            Assert.AreEqual(WeeklyRecordStatus.Duplicate, Submit(wk, "r2", 120, 2, Now, Server(Now)).Status);
            Assert.AreEqual(WeeklyRecordStatus.ReplayRejected, Submit(wk, "r5", 180, 4, Now, Server(Now)).Status);

            // Catch-up: three wins, then the cap; the same win again is a duplicate.
            var cu = CatchUp(w);
            Assert.AreEqual(CatchUpStatus.Active, cu.Evaluate(Server(Now), Server(LastActive)).Status);
            for (var i = 1; i <= 3; i++)
                w.Applied(cu.RecordWin(Server(Now.AddHours(i)), Result(game, "w" + i, 100), Now.AddHours(i - 0.5)));
            Assert.AreEqual(CatchUpReasons.WinCapReached, cu.RecordWin(Server(Now.AddHours(4)), Result(game, "w4", 100), Now.AddHours(3.5)).Reason);
            Assert.AreEqual(RewardLedgerOutcome.Duplicate, cu.RecordWin(Server(Now.AddHours(4)), Result(game, "w1", 100), Now.AddHours(0.5)).Outcome);

            // Alt modes: per-run cap 25, per-day cap 40; a run is rewarded once.
            var issuer = new ModeRewardIssuer(w.Ledger);
            var first = Play(game, Sprint(), 31).Result();
            var a = issuer.Issue(first, Sprint(), Server(Now));
            Assert.AreEqual(ModeRewardStatus.Granted, a.Status, a.Reason);
            Assert.AreEqual(25L, a.Grants.Sum(g => g.Amounts["Coins"]));
            foreach (var g in a.Grants) w.Applied(g);
            Assert.AreEqual(ModeRewardStatus.DuplicateRun, issuer.Issue(first, Sprint(), Server(Now)).Status);
            var b = issuer.Issue(Play(game, Sprint(), 32).Result(), Sprint(), Server(Now));
            Assert.AreEqual(ModeRewardStatus.Granted, b.Status, b.Reason);
            foreach (var g in b.Grants) w.Applied(g);
            Assert.AreEqual(40L, issuer.GrantedOn(GameModeKind.TimeAttack, Now.Date));
            Assert.AreEqual(ModeRewardStatus.DailyCapReached, issuer.Issue(Play(game, Sprint(), 33).Result(), Sprint(), Server(Now)).Status);

            // Tournament prize: exactly once across repeated closes; never on Wonder_Pop.
            if (wp)
            {
                Assert.Throws<InvalidOperationException>(() => WonTournament(game, "cup.w46", Now.AddDays(-1), Now));
                Assert.IsFalse(FeatureFlagGate.CanEnableFlag(game, Flag(game, LiveOpsFeature.TournamentMode), FeatureFlagPolicy.Default, out why));
                Assert.AreEqual("WonderPopTournamentProhibited", why);
            }
            else
            {
                var cup = WonTournament(game, "cup.w46", Now.AddDays(-1), Now);
                var c1 = Close(cup, w, Now.AddHours(1));
                Assert.AreEqual(1, c1.Granted.Count);
                w.Applied(c1.Granted[0]);
                var c2 = Close(cup, w, Now.AddHours(2));
                Assert.AreEqual(0, c2.Granted.Count);
                Assert.AreEqual(1, c2.AlreadyGranted);
            }
            w.Oracle();
        }
    }
}
