using System;
using System.Collections.Generic;
using Match3.Engine.Runtime;
using Match3.Engine.Runtime.Economy;
using Match3.Engine.Runtime.LiveOps;
using NUnit.Framework;
using C = Match3.Engine.Runtime.LiveOps.CatchUpReasons;

namespace Match3.Engine.Tests.Editor
{
    // ENG-M20-013.
    public sealed class ComebackCatchUpServiceTests
    {
        private const string Player = "player-1";
        private static readonly DateTime LastActive = new DateTime(2026, 10, 1, 9, 30, 0, DateTimeKind.Utc);
        private static readonly DateTime Return = LastActive.AddDays(20); // threshold 14, window 7 (Standard)
        private static readonly TrustedUtcAnchor Last = TrustedUtcAnchor.Server(LastActive);

        private static GameProfile Profile(string id = "Coral_Rescue", bool catchUp = true) =>
            new GameProfile(id, "placeholder." + id.ToLowerInvariant(), ProfileAgeRule.General, ProfileContentChannel.Development,
                catchUp ? new[] { new FeatureFlag("catch_up", EngineCompatibility.MinimumCapability) } : null);

        private static ComebackCatchUpService Service(out RewardLedger ledger, string id = "Coral_Rescue", bool catchUp = true, bool child = false)
        {
            ledger = new RewardLedger(id, Player);
            return new ComebackCatchUpService(Profile(id, catchUp), ledger, CatchUpPolicy.Standard, child);
        }

        private static TrustedUtcAnchor At(DateTime utc) => TrustedUtcAnchor.Server(utc);

        private static VerifiedProgressionResult Won(string id, string game = "Coral_Rescue", bool won = true,
            bool verified = true, string profile = Player) =>
            new VerifiedProgressionResult(id, game, profile, "classic", "lvl.1", "lvl.1", won, false, true,
                100, won ? 1 : 0, 1, 0, "2026-W43", verified);

        // A verified win completed at the server-anchored instant `at`.
        private static RewardLedgerGrantResult Win(ComebackCatchUpService s, DateTime at, string id, string game = "Coral_Rescue") =>
            s.RecordWin(At(at), Won(id, game), at);

        [Test]
        public void RecordWin_RequiresVerifiedWonOwnResultCompletedInsideWindow()
        {
            var s = Service(out var ledger);
            s.Evaluate(At(Return), Last);
            var now = At(Return.AddHours(2));
            var at = Return.AddHours(1);
#pragma warning disable CS0618
            Assert.That(s.RecordWin(now, "win-1").Reason, Is.EqualTo(C.VerifiedWinRequired), "string overload fails closed");
#pragma warning restore CS0618
            Assert.That(s.RecordWin(now, null, at).Reason, Is.EqualTo(C.UnverifiedWin));
            Assert.That(s.RecordWin(now, Won("x", verified: false), at).Reason, Is.EqualTo(C.UnverifiedWin));
            Assert.That(s.RecordWin(now, Won("x", won: false), at).Reason, Is.EqualTo(C.NotAWin));
            Assert.That(s.RecordWin(now, Won("x", profile: "player-2"), at).Reason, Is.EqualTo(C.WinOwnerMismatch));
            Assert.That(s.RecordWin(now, Won("x", game: "Dino_Islands"), at).Reason, Is.EqualTo(C.WinOwnerMismatch));
            Assert.That(s.RecordWin(now, Won("x"), Return.AddHours(-1)).Reason, Is.EqualTo(C.CompletionOutsideWindow), "before window");
            Assert.That(s.RecordWin(now, Won("x"), Return.AddHours(3)).Reason, Is.EqualTo(C.CompletionOutsideWindow), "after server now");
            Assert.That(s.RecordWin(now, Won("x"), new DateTime(2026, 10, 21, 11, 0, 0, DateTimeKind.Local)).Reason, Is.EqualTo(C.NonUtcTime));
            Assert.That(ledger.Count, Is.EqualTo(0));
            var ok = s.RecordWin(now, Won("x"), at);
            Assert.That(ok.Outcome, Is.EqualTo(RewardLedgerOutcome.Applied));
            Assert.That(ok.Record.Reference, Is.EqualTo("x"), "result id is the ledger reference");
            Assert.That(s.RecordWin(At(Return.AddHours(3)), Won("x"), at).Outcome, Is.EqualTo(RewardLedgerOutcome.Duplicate));
        }

        [Test]
        public void PersistedState_Validated_EditedFreshWindowCannotOpenMoreGrants()
        {
            var s = Service(out var ledger);
            s.Evaluate(At(Return), Last);
            for (var i = 1; i <= 3; i++) Win(s, Return.AddHours(i), "win-" + i);
            ComebackCatchUpService Restore(CatchUpWindowState st) =>
                new ComebackCatchUpService(Profile(), ledger, CatchUpPolicy.Standard, false, st);

            // Edited state: a fresh windowId (absence one day after the recorded one) with no ledger evidence.
            var fresh = LastActive.AddDays(1);
            var edited = new CatchUpWindowState("w" + fresh.Ticks, Return.AddDays(1), Return.AddDays(8), false);
            Assert.Throws<ArgumentException>(() => Restore(edited));
            Assert.Throws<ArgumentException>(() => Restore(new CatchUpWindowState("bogus", Return, Return.AddDays(7), false)));
            Assert.Throws<ArgumentException>(() => Restore(new CatchUpWindowState("w0" + LastActive.Ticks, Return, Return.AddDays(7), false)));
            Assert.Throws<ArgumentException>(() => Restore(new CatchUpWindowState("w" + LastActive.Ticks, LastActive.AddDays(5),
                LastActive.AddDays(12), false)), "opened before the absence was genuine");
            Assert.Throws<ArgumentException>(() => Restore(new CatchUpWindowState("w" + LastActive.Ticks, Return, Return.AddDays(8), false)),
                "longer than the policy window");
            Assert.That(ledger.Count, Is.EqualTo(3));
            Assert.That(ledger.Balance(CurrencyType.Coins), Is.EqualTo(300));

            // The genuine persisted state restores fine and stays capped.
            var restored = Restore(s.State);
            Assert.That(Win(restored, Return.AddHours(5), "win-9").Reason, Is.EqualTo(C.WinCapReached));
        }

        [Test]
        public void PersistedState_SignalDerivedFromLedgerHistory_OnRestore()
        {
            var s = Service(out var ledger);
            s.Evaluate(At(Return), Last);
            Win(s, Return.AddHours(1), "win-1");
            var unsent = new CatchUpWindowState(s.State.WindowId, s.State.OpenedUtc, s.State.EndsUtc, false);
            var restored = new ComebackCatchUpService(Profile(), ledger, CatchUpPolicy.Standard, false, unsent);
            Assert.That(restored.State.SignalSent, Is.True);
            Assert.That(restored.Evaluate(At(Return.AddHours(2)), Last).EmitEligibleSignal, Is.False);
            // Without ledger evidence the persisted flag is authoritative.
            var notYet = new ComebackCatchUpService(Profile(), new RewardLedger("Coral_Rescue", Player), CatchUpPolicy.Standard, false,
                new CatchUpWindowState("w" + LastActive.Ticks, Return, Return.AddDays(7), false));
            Assert.That(notYet.Evaluate(At(Return.AddHours(1)), Last).EmitEligibleSignal, Is.True);
        }

        [Test]
        public void GenuineAbsence_OpensWindow_TransparentExplanation_SignalOnce()
        {
            var s = Service(out _);
            var e = s.Evaluate(At(Return), Last);
            Assert.That(e.Status, Is.EqualTo(CatchUpStatus.Active), e.Reason);
            Assert.That(e.EmitEligibleSignal, Is.True);
            Assert.That(e.Explanation.RemainingWins, Is.EqualTo(3));
            Assert.That(e.Explanation.MaxWins, Is.EqualTo(3));
            Assert.That(e.Explanation.WindowEndsUtc, Is.EqualTo(Return.AddDays(7)));
            Assert.That(e.Explanation.RewardPerWin["Coins"], Is.EqualTo(100));
            for (var i = 1; i <= 5; i++)
            {
                var again = s.Evaluate(At(Return.AddHours(i)), Last);
                Assert.That(again.Status, Is.EqualTo(CatchUpStatus.Active));
                Assert.That(again.EmitEligibleSignal, Is.False, "at most one eligible signal per window");
            }
        }

        [Test]
        public void ShortAbsence_Ineligible()
        {
            var s = Service(out _);
            var e = s.Evaluate(At(LastActive.AddDays(14).AddTicks(-1)), Last);
            Assert.That(e.Reason, Is.EqualTo(C.InsufficientAbsence));
            Assert.That(e.EmitEligibleSignal, Is.False);
            Assert.That(s.Evaluate(At(LastActive.AddDays(14)), Last).Status, Is.EqualTo(CatchUpStatus.Active));
        }

        [Test]
        public void MissingOrUntrustedAnchors_Rejected_NoDeviceClock()
        {
            var s = Service(out var ledger);
            Assert.That(s.Evaluate(default(TrustedUtcAnchor), Last).Reason, Is.EqualTo(C.TrustedTimeAnchorRequired));
            Assert.That(s.Evaluate(TrustedUtcAnchor.DeviceClock(Return), Last).Reason, Is.EqualTo(C.TrustedTimeAnchorRequired));
            // Spoofed (client-provided) last-active value.
            Assert.That(s.Evaluate(At(Return), TrustedUtcAnchor.DeviceClock(LastActive)).Reason, Is.EqualTo(C.TrustedLastActiveRequired));
            Assert.That(s.Evaluate(At(Return), default(TrustedUtcAnchor)).Reason, Is.EqualTo(C.TrustedLastActiveRequired));
            Assert.That(s.State, Is.Null);
            s.Evaluate(At(Return), Last);
            Assert.That(s.RecordWin(TrustedUtcAnchor.DeviceClock(Return.AddHours(1)), Won("win-1"), Return.AddHours(1)).Reason, Is.EqualTo(C.TrustedTimeAnchorRequired));
            Assert.That(ledger.Count, Is.EqualTo(0));
        }

        [Test]
        public void NonUtcInputs_Rejected()
        {
            var s = Service(out _);
            Assert.That(s.Evaluate(At(new DateTime(2026, 10, 30, 0, 0, 0, DateTimeKind.Local)), Last).Reason, Is.EqualTo(C.NonUtcTime));
            Assert.That(s.Evaluate(At(Return), At(new DateTime(2026, 10, 1))).Reason, Is.EqualTo(C.NonUtcTime));
        }

        [Test]
        public void ClockRollback_Rejected()
        {
            var s = Service(out var ledger);
            Assert.That(s.Evaluate(At(LastActive.AddDays(-1)), Last).Reason, Is.EqualTo(C.ClockRollbackDetected));
            s.Evaluate(At(Return), Last);
            Assert.That(s.Evaluate(At(Return.AddDays(-1)), Last).Reason, Is.EqualTo(C.ClockRollbackDetected));
            Assert.That(Win(s, Return.AddDays(-1), "win-1").Reason, Is.EqualTo(C.ClockRollbackDetected));
            Assert.That(ledger.Count, Is.EqualTo(0));
        }

        [Test]
        public void ThreeWinsRewardedExactlyOnce_FourthNotRewarded_ReplayNotDoubleGranted()
        {
            var s = Service(out var ledger);
            s.Evaluate(At(Return), Last);
            for (var i = 1; i <= 3; i++)
                Assert.That(Win(s, Return.AddHours(i), "win-" + i).Outcome, Is.EqualTo(RewardLedgerOutcome.Applied));
            var replay = Win(s, Return.AddHours(5), "win-2");
            Assert.That(replay.Outcome, Is.EqualTo(RewardLedgerOutcome.Duplicate));
            Assert.That(replay.Record.GrantedUtc, Is.EqualTo(Return.AddHours(2)));
            Assert.That(Win(s, Return.AddHours(6), "win-4").Reason, Is.EqualTo(C.WinCapReached));
            Assert.That(ledger.Count, Is.EqualTo(3));
            Assert.That(ledger.Balance(CurrencyType.Coins), Is.EqualTo(300));
            Assert.That(ledger.Balance(CurrencyType.Boosters), Is.EqualTo(3));
            var done = s.Evaluate(At(Return.AddHours(7)), Last);
            Assert.That(done.Status, Is.EqualTo(CatchUpStatus.Completed));
            Assert.That(done.Explanation.RemainingWins, Is.EqualTo(0));
            Assert.That(done.EmitEligibleSignal, Is.False);
            Assert.That(ledger.TryGetRecord(RewardLedgerKey.Derive("Coral_Rescue", Player, RewardSources.CatchUp, "w" + LastActive.Ticks + "#1"), out _), Is.True);
        }

        [Test]
        public void WindowExpiry_ThenSameAbsenceCannotReopen_NewGenuineAbsenceOpensNewWindow()
        {
            var s = Service(out var ledger);
            s.Evaluate(At(Return), Last);
            Win(s, Return.AddHours(1), "win-1");
            Assert.That(Win(s, Return.AddDays(7), "win-2").Reason, Is.EqualTo(C.WindowExpired));
            Assert.That(s.Evaluate(At(Return.AddDays(30)), Last).Reason, Is.EqualTo(C.NewAbsenceRequired));
            // Player was active during the window, then absent again for 13 days: not yet genuine.
            var active2 = Return.AddDays(2);
            Assert.That(s.Evaluate(At(active2.AddDays(13)), At(active2)).Reason, Is.EqualTo(C.InsufficientAbsence));
            var e = s.Evaluate(At(active2.AddDays(15)), At(active2));
            Assert.That(e.Status, Is.EqualTo(CatchUpStatus.Active));
            Assert.That(e.EmitEligibleSignal, Is.True);
            Assert.That(e.Explanation.RemainingWins, Is.EqualTo(3));
            Assert.That(Win(s, active2.AddDays(15).AddHours(1), "win-1").Outcome, Is.EqualTo(RewardLedgerOutcome.Applied),
                "win ids are scoped per window");
            Assert.That(ledger.Count, Is.EqualTo(2));
        }

        [Test]
        public void Reinstall_LostLocalState_DerivesWindowFromLedger_NotFromCurrentPolicy()
        {
            var s = Service(out var ledger);
            s.Evaluate(At(Return), Last);
            Win(s, Return.AddHours(1), "win-1");
            Win(s, Return.AddHours(2), "win-2");
            var cloud = RewardLedger.FromSnapshot(ledger.ToSnapshot()).Ledger;

            // Policy window length changed since: the pinned window must not move.
            var longer = new CatchUpPolicy(14, 30, CatchUpPolicy.Standard.RewardPerWin);
            var reinstalled = new ComebackCatchUpService(Profile(), cloud, longer, false);
            var e = reinstalled.Evaluate(At(Return.AddDays(3)), Last);
            Assert.That(e.Status, Is.EqualTo(CatchUpStatus.Active));
            Assert.That(e.EmitEligibleSignal, Is.False, "no second signal after reinstall");
            Assert.That(e.Explanation.WinsEarned, Is.EqualTo(2));
            Assert.That(e.Explanation.WindowEndsUtc, Is.EqualTo(Return.AddDays(7)), "window not extended by reinstall");
            Assert.That(reinstalled.State.OpenedUtc, Is.EqualTo(Return.AddHours(1)), "start = earliest recorded win");
            Assert.That(Win(reinstalled, Return.AddDays(3), "win-1").Outcome, Is.EqualTo(RewardLedgerOutcome.Duplicate));
            Assert.That(Win(reinstalled, Return.AddDays(3), "win-3").Outcome, Is.EqualTo(RewardLedgerOutcome.Applied));
            Assert.That(Win(reinstalled, Return.AddDays(3), "win-4").Reason, Is.EqualTo(C.WinCapReached));
            Assert.That(cloud.Balance(CurrencyType.Coins), Is.EqualTo(300));

            // Reinstall after the window ended: the window is closed, not re-opened.
            var late = new ComebackCatchUpService(Profile(), cloud, CatchUpPolicy.Standard, false);
            Assert.That(late.Evaluate(At(Return.AddDays(9)), Last).Reason, Is.EqualTo(C.WindowExpired));
            Assert.That(late.Evaluate(At(Return.AddDays(10)), Last).Reason, Is.EqualTo(C.NewAbsenceRequired));
        }

        [Test]
        public void StateLossPlusFabricatedLastActive_CannotOpenSecondTrack()
        {
            var s = Service(out var ledger);
            s.Evaluate(At(Return), Last);
            Win(s, Return.AddHours(1), "win-1");
            // Local state lost; a forged "server" last-active value is presented to get a fresh windowId.
            foreach (var forged in new[] { LastActive.AddDays(-30), LastActive.AddDays(-1), LastActive.AddDays(1), LastActive.AddDays(7) })
            {
                var fresh = new ComebackCatchUpService(Profile(), ledger, CatchUpPolicy.Standard, false);
                var e = fresh.Evaluate(At(Return.AddDays(1)), At(forged));
                Assert.That(e.Reason, Is.EqualTo(C.RecentCatchUpWindow), forged.ToString("o"));
                Assert.That(e.EmitEligibleSignal, Is.False);
                Assert.That(Win(fresh, Return.AddDays(1), "forged-win").Reason, Is.EqualTo(C.NoActiveWindow));
            }
            Assert.That(ledger.Count, Is.EqualTo(1));
            // A genuine later absence (>= latest recorded absence + threshold, and a full threshold ago) is allowed.
            var genuine = LastActive.AddDays(22);
            var ok = new ComebackCatchUpService(Profile(), ledger, CatchUpPolicy.Standard, false).Evaluate(At(genuine.AddDays(14)), At(genuine));
            Assert.That(ok.Status, Is.EqualTo(CatchUpStatus.Active), ok.Reason);
        }

        [Test]
        public void SettledCatchUpKeys_StillBlockFabricatedTrack()
        {
            var s = Service(out var ledger);
            s.Evaluate(At(Return), Last);
            Win(s, Return.AddHours(1), "win-1");
            Assert.That(ledger.Prune(At(Return.AddDays(400))), Is.EqualTo(1));
            var fresh = new ComebackCatchUpService(Profile(), ledger, CatchUpPolicy.Standard, false);
            Assert.That(fresh.Evaluate(At(Return.AddDays(400)), At(LastActive.AddDays(5))).Reason, Is.EqualTo(C.RecentCatchUpWindow));
            Assert.That(fresh.Evaluate(At(Return.AddDays(400)), Last).Reason, Is.EqualTo(C.WindowExpired), "same absence, settled window");
        }

        [Test]
        public void PersistedState_RoundTrip_PreventsSecondSignal()
        {
            var s = Service(out var ledger);
            Assert.That(s.Evaluate(At(Return), Last).EmitEligibleSignal, Is.True);
            var text = s.State.ToCanonical();
            Assert.That(CatchUpWindowState.TryParse(text, out var restored), Is.True);
            Assert.That(restored.WindowLength, Is.EqualTo(TimeSpan.FromDays(7)));
            var resumed = new ComebackCatchUpService(Profile(), ledger, CatchUpPolicy.Standard, false, restored);
            Assert.That(resumed.Evaluate(At(Return.AddHours(1)), Last).EmitEligibleSignal, Is.False);
            Assert.That(CatchUpWindowState.TryParse(text.Replace(":true", ": true"), out _), Is.False);
            Assert.That(CatchUpWindowState.TryParse("{}", out _), Is.False);
        }

        [Test]
        public void ChildPlayer_OffEntirely_OnAnyTitle()
        {
            foreach (var id in new[] { "Coral_Rescue", "Wonder_Pop" })
            {
                var s = Service(out var ledger, id, child: true);
                var e = s.Evaluate(At(Return), Last);
                Assert.That(e.Status, Is.EqualTo(CatchUpStatus.Off), id);
                Assert.That(e.Reason, Is.EqualTo(C.ChildProfileOff));
                Assert.That(e.EmitEligibleSignal, Is.False);
                Assert.That(e.Explanation, Is.Null);
                Assert.That(s.State, Is.Null);
                Assert.That(Win(s, Return, "win-1").Reason, Is.EqualTo(C.ChildProfileOff));
                Assert.That(ledger.Count, Is.EqualTo(0));
            }
        }

        [Test]
        public void FamilySafeTitle_WonderPop_NonTargetedTrack_NeverSignals()
        {
            var s = Service(out var ledger, "Wonder_Pop");
            var e = s.Evaluate(At(Return), Last);
            Assert.That(e.Status, Is.EqualTo(CatchUpStatus.Active), e.Reason);
            Assert.That(e.EmitEligibleSignal, Is.False);
            Assert.That(s.Evaluate(At(Return.AddHours(1)), Last).EmitEligibleSignal, Is.False);
            Assert.That(Win(s, Return.AddHours(2), "win-1", "Wonder_Pop").Outcome, Is.EqualTo(RewardLedgerOutcome.Applied));
            Assert.That(ledger.Balance(CurrencyType.Coins), Is.EqualTo(100));
            Assert.That(ledger.Balance(CurrencyType.PremiumGems), Is.EqualTo(0));
        }

        [Test]
        public void FeatureFlagOff_ServiceOff_DefaultForEveryTitle()
        {
            foreach (var id in GameProfileCatalog.Ids)
            {
                var s = Service(out var ledger, id, catchUp: false);
                var e = s.Evaluate(At(Return), Last);
                Assert.That(e.Status, Is.EqualTo(CatchUpStatus.Off), id);
                Assert.That(e.Reason, Is.EqualTo(C.FeatureDisabled));
                Assert.That(Win(s, Return, "win-1").Reason, Is.EqualTo(C.FeatureDisabled));
                Assert.That(ledger.Count, Is.EqualTo(0));
            }
        }

        [Test]
        public void TimezoneAndDst_Independent_AllUtc()
        {
            // Absence spanning the US DST end (2026-11-01) expressed in different offsets yields identical results.
            var lastLocal = new DateTimeOffset(2026, 10, 25, 1, 30, 0, TimeSpan.FromHours(-4));
            var nowLocal = new DateTimeOffset(2026, 11, 8, 0, 30, 0, TimeSpan.FromHours(-5)); // exactly 14 days later in UTC
            var a = Service(out _).Evaluate(At(nowLocal.UtcDateTime), At(lastLocal.UtcDateTime));
            var b = Service(out _).Evaluate(At(nowLocal.ToOffset(TimeSpan.FromHours(9)).UtcDateTime),
                At(lastLocal.ToOffset(TimeSpan.FromHours(14)).UtcDateTime));
            Assert.That(a.Status, Is.EqualTo(CatchUpStatus.Active), a.Reason);
            Assert.That(b.Explanation.WindowEndsUtc, Is.EqualTo(a.Explanation.WindowEndsUtc));
            Assert.That(b.Explanation.WindowId, Is.EqualTo(a.Explanation.WindowId));
            var justShort = Service(out _).Evaluate(At(nowLocal.UtcDateTime.AddTicks(-1)), At(lastLocal.UtcDateTime));
            Assert.That(justShort.Reason, Is.EqualTo(C.InsufficientAbsence), "local wall-clock hours (DST) never count");
        }

        [Test]
        public void Policy_RejectsPaidUnregisteredOrUncappedRewards_AndLedgerMismatch()
        {
            Assert.Throws<ArgumentException>(() => new CatchUpPolicy(14, 7, new Dictionary<string, long> { ["PremiumGems"] = 1 }));
            Assert.Throws<ArgumentException>(() => new CatchUpPolicy(14, 7, new Dictionary<string, long> { ["coins"] = 1 }));
            Assert.Throws<ArgumentException>(() => new CatchUpPolicy(14, 7,
                new Dictionary<string, long> { ["Coins"] = CatchUpPolicy.MaxRewardPerAssetPerWin + 1 }));
            Assert.Throws<ArgumentException>(() => new CatchUpPolicy(14, 7, new Dictionary<string, long>()));
            Assert.Throws<ArgumentOutOfRangeException>(() => new CatchUpPolicy(0, 7, new Dictionary<string, long> { ["Coins"] = 1 }));
            Assert.Throws<ArgumentException>(() =>
                new ComebackCatchUpService(Profile("Dino_Islands"), new RewardLedger("Coral_Rescue", Player), CatchUpPolicy.Standard, false));
        }
    }
}
