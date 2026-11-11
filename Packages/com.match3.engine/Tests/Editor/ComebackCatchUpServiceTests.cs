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

        private static GameProfile Profile(string id = "Coral_Rescue", bool catchUp = true) =>
            new GameProfile(id, "placeholder." + id.ToLowerInvariant(), ProfileAgeRule.General, ProfileContentChannel.Development,
                catchUp ? new[] { new FeatureFlag("catch_up", EngineCompatibility.MinimumCapability) } : null);

        private static ComebackCatchUpService Service(out RewardLedger ledger, string id = "Coral_Rescue", bool catchUp = true)
        {
            ledger = new RewardLedger(id, Player);
            return new ComebackCatchUpService(Profile(id, catchUp), ledger, CatchUpPolicy.Standard);
        }

        private static TrustedUtcAnchor At(DateTime utc) => TrustedUtcAnchor.Server(utc);

        [Test]
        public void GenuineAbsence_OpensWindow_TransparentExplanation_SignalOnce()
        {
            var s = Service(out _);
            var e = s.Evaluate(At(Return), LastActive);
            Assert.That(e.Status, Is.EqualTo(CatchUpStatus.Active), e.Reason);
            Assert.That(e.EmitEligibleSignal, Is.True);
            Assert.That(e.Explanation.RemainingWins, Is.EqualTo(3));
            Assert.That(e.Explanation.MaxWins, Is.EqualTo(3));
            Assert.That(e.Explanation.WindowEndsUtc, Is.EqualTo(Return.AddDays(7)));
            Assert.That(e.Explanation.RewardPerWin["Coins"], Is.EqualTo(100));
            for (var i = 1; i <= 5; i++)
            {
                var again = s.Evaluate(At(Return.AddHours(i)), LastActive);
                Assert.That(again.Status, Is.EqualTo(CatchUpStatus.Active));
                Assert.That(again.EmitEligibleSignal, Is.False, "at most one eligible signal per window");
            }
        }

        [Test]
        public void ShortAbsence_Ineligible()
        {
            var s = Service(out _);
            var e = s.Evaluate(At(LastActive.AddDays(14).AddTicks(-1)), LastActive);
            Assert.That(e.Reason, Is.EqualTo(C.InsufficientAbsence));
            Assert.That(e.EmitEligibleSignal, Is.False);
            Assert.That(s.Evaluate(At(LastActive.AddDays(14)), LastActive).Status, Is.EqualTo(CatchUpStatus.Active));
        }

        [Test]
        public void MissingOrUntrustedAnchor_Rejected_NoDeviceClock()
        {
            var s = Service(out var ledger);
            Assert.That(s.Evaluate(default(TrustedUtcAnchor), LastActive).Reason, Is.EqualTo(C.TrustedTimeAnchorRequired));
            Assert.That(s.Evaluate(TrustedUtcAnchor.DeviceClock(Return), LastActive).Reason, Is.EqualTo(C.TrustedTimeAnchorRequired));
            Assert.That(s.State, Is.Null);
            s.Evaluate(At(Return), LastActive);
            Assert.That(s.RecordWin(TrustedUtcAnchor.DeviceClock(Return.AddHours(1)), "win-1").Reason, Is.EqualTo(C.TrustedTimeAnchorRequired));
            Assert.That(ledger.Count, Is.EqualTo(0));
        }

        [Test]
        public void NonUtcInputs_Rejected()
        {
            var s = Service(out _);
            Assert.That(s.Evaluate(At(new DateTime(2026, 10, 30, 0, 0, 0, DateTimeKind.Local)), LastActive).Reason, Is.EqualTo(C.NonUtcTime));
            Assert.That(s.Evaluate(At(Return), new DateTime(2026, 10, 1)).Reason, Is.EqualTo(C.NonUtcTime));
        }

        [Test]
        public void ClockRollback_Rejected()
        {
            var s = Service(out var ledger);
            Assert.That(s.Evaluate(At(LastActive.AddDays(-1)), LastActive).Reason, Is.EqualTo(C.ClockRollbackDetected));
            s.Evaluate(At(Return), LastActive);
            Assert.That(s.Evaluate(At(Return.AddDays(-1)), LastActive).Reason, Is.EqualTo(C.ClockRollbackDetected));
            Assert.That(s.RecordWin(At(Return.AddDays(-1)), "win-1").Reason, Is.EqualTo(C.ClockRollbackDetected));
            Assert.That(ledger.Count, Is.EqualTo(0));
        }

        [Test]
        public void ThreeWinsRewardedExactlyOnce_FourthNotRewarded_ReplayNotDoubleGranted()
        {
            var s = Service(out var ledger);
            s.Evaluate(At(Return), LastActive);
            for (var i = 1; i <= 3; i++)
                Assert.That(s.RecordWin(At(Return.AddHours(i)), "win-" + i).Outcome, Is.EqualTo(RewardLedgerOutcome.Applied));
            var replay = s.RecordWin(At(Return.AddHours(5)), "win-2");
            Assert.That(replay.Outcome, Is.EqualTo(RewardLedgerOutcome.Duplicate));
            Assert.That(replay.Record.GrantedUtc, Is.EqualTo(Return.AddHours(2)));
            Assert.That(s.RecordWin(At(Return.AddHours(6)), "win-4").Reason, Is.EqualTo(C.WinCapReached));
            Assert.That(ledger.Count, Is.EqualTo(3));
            Assert.That(ledger.Balance(CurrencyType.Coins), Is.EqualTo(300));
            Assert.That(ledger.Balance(CurrencyType.Boosters), Is.EqualTo(3));
            var done = s.Evaluate(At(Return.AddHours(7)), LastActive);
            Assert.That(done.Status, Is.EqualTo(CatchUpStatus.Completed));
            Assert.That(done.Explanation.RemainingWins, Is.EqualTo(0));
            Assert.That(done.EmitEligibleSignal, Is.False);
            Assert.That(ledger.TryGetRecord(RewardLedgerKey.Derive("Coral_Rescue", Player, RewardSources.CatchUp, "w" + LastActive.Ticks + "#1"), out _), Is.True);
        }

        [Test]
        public void WindowExpiry_ThenSameAbsenceCannotReopen_NewGenuineAbsenceOpensNewWindow()
        {
            var s = Service(out var ledger);
            s.Evaluate(At(Return), LastActive);
            s.RecordWin(At(Return.AddHours(1)), "win-1");
            Assert.That(s.RecordWin(At(Return.AddDays(7)), "win-2").Reason, Is.EqualTo(C.WindowExpired));
            Assert.That(s.Evaluate(At(Return.AddDays(30)), LastActive).Reason, Is.EqualTo(C.NewAbsenceRequired));
            // Player was active during the window, then absent again for 13 days: not yet genuine.
            var active2 = Return.AddDays(2);
            Assert.That(s.Evaluate(At(active2.AddDays(13)), active2).Reason, Is.EqualTo(C.InsufficientAbsence));
            var e = s.Evaluate(At(active2.AddDays(15)), active2);
            Assert.That(e.Status, Is.EqualTo(CatchUpStatus.Active));
            Assert.That(e.EmitEligibleSignal, Is.True);
            Assert.That(e.Explanation.RemainingWins, Is.EqualTo(3));
            Assert.That(s.RecordWin(At(active2.AddDays(15).AddHours(1)), "win-1").Outcome, Is.EqualTo(RewardLedgerOutcome.Applied),
                "win ids are scoped per window");
            Assert.That(ledger.Count, Is.EqualTo(2));
        }

        [Test]
        public void Reinstall_LostLocalState_DerivesWinsAndWindowEndFromLedger()
        {
            var s = Service(out var ledger);
            s.Evaluate(At(Return), LastActive);
            s.RecordWin(At(Return.AddHours(1)), "win-1");
            s.RecordWin(At(Return.AddHours(2)), "win-2");
            var cloud = RewardLedger.FromSnapshot(ledger.ToSnapshot()).Ledger;

            var reinstalled = new ComebackCatchUpService(Profile(), cloud, CatchUpPolicy.Standard);
            var e = reinstalled.Evaluate(At(Return.AddDays(3)), LastActive);
            Assert.That(e.Status, Is.EqualTo(CatchUpStatus.Active));
            Assert.That(e.Explanation.WinsEarned, Is.EqualTo(2));
            Assert.That(e.Explanation.WindowEndsUtc, Is.EqualTo(Return.AddDays(7)), "window not extended by reinstall");
            Assert.That(reinstalled.RecordWin(At(Return.AddDays(3)), "win-1").Outcome, Is.EqualTo(RewardLedgerOutcome.Duplicate));
            Assert.That(reinstalled.RecordWin(At(Return.AddDays(3)), "win-3").Outcome, Is.EqualTo(RewardLedgerOutcome.Applied));
            Assert.That(reinstalled.RecordWin(At(Return.AddDays(3)), "win-4").Reason, Is.EqualTo(C.WinCapReached));
            Assert.That(cloud.Balance(CurrencyType.Coins), Is.EqualTo(300));

            // Reinstall after the window ended: the window is closed, not re-opened.
            var late = new ComebackCatchUpService(Profile(), cloud, CatchUpPolicy.Standard);
            Assert.That(late.Evaluate(At(Return.AddDays(9)), LastActive).Reason, Is.EqualTo(C.WindowExpired));
            Assert.That(late.Evaluate(At(Return.AddDays(10)), LastActive).Reason, Is.EqualTo(C.NewAbsenceRequired));
        }

        [Test]
        public void PersistedState_RoundTrip_PreventsSecondSignal()
        {
            var s = Service(out var ledger);
            Assert.That(s.Evaluate(At(Return), LastActive).EmitEligibleSignal, Is.True);
            var text = s.State.ToCanonical();
            Assert.That(CatchUpWindowState.TryParse(text, out var restored), Is.True);
            var resumed = new ComebackCatchUpService(Profile(), ledger, CatchUpPolicy.Standard, restored);
            Assert.That(resumed.Evaluate(At(Return.AddHours(1)), LastActive).EmitEligibleSignal, Is.False);
            Assert.That(CatchUpWindowState.TryParse(text.Replace(":true", ": true"), out _), Is.False);
            Assert.That(CatchUpWindowState.TryParse("{}", out _), Is.False);
        }

        [Test]
        public void ChildProfile_WonderPop_OffEntirely()
        {
            var s = Service(out var ledger, "Wonder_Pop");
            var e = s.Evaluate(At(Return), LastActive);
            Assert.That(e.Status, Is.EqualTo(CatchUpStatus.Off));
            Assert.That(e.Reason, Is.EqualTo(C.ChildProfileOff));
            Assert.That(e.EmitEligibleSignal, Is.False);
            Assert.That(e.Explanation, Is.Null);
            Assert.That(s.State, Is.Null);
            Assert.That(s.RecordWin(At(Return), "win-1").Reason, Is.EqualTo(C.ChildProfileOff));
            Assert.That(ledger.Count, Is.EqualTo(0));
        }

        [Test]
        public void FeatureFlagOff_ServiceOff()
        {
            var s = Service(out _, catchUp: false);
            var e = s.Evaluate(At(Return), LastActive);
            Assert.That(e.Status, Is.EqualTo(CatchUpStatus.Off));
            Assert.That(e.Reason, Is.EqualTo(C.FeatureDisabled));
        }

        [Test]
        public void TimezoneAndDst_Independent_AllUtc()
        {
            // Absence spanning the US DST end (2026-11-01) expressed in different offsets yields identical results.
            var lastLocal = new DateTimeOffset(2026, 10, 25, 1, 30, 0, TimeSpan.FromHours(-4));
            var nowLocal = new DateTimeOffset(2026, 11, 8, 0, 30, 0, TimeSpan.FromHours(-5)); // exactly 14 days later in UTC
            var a = Service(out _).Evaluate(At(nowLocal.UtcDateTime), lastLocal.UtcDateTime);
            var b = Service(out _).Evaluate(At(nowLocal.ToOffset(TimeSpan.FromHours(9)).UtcDateTime),
                lastLocal.ToOffset(TimeSpan.FromHours(14)).UtcDateTime);
            Assert.That(a.Status, Is.EqualTo(CatchUpStatus.Active), a.Reason);
            Assert.That(b.Explanation.WindowEndsUtc, Is.EqualTo(a.Explanation.WindowEndsUtc));
            Assert.That(b.Explanation.WindowId, Is.EqualTo(a.Explanation.WindowId));
            var justShort = Service(out _).Evaluate(At(nowLocal.UtcDateTime.AddTicks(-1)), lastLocal.UtcDateTime);
            Assert.That(justShort.Reason, Is.EqualTo(C.InsufficientAbsence), "local wall-clock hours (DST) never count");
        }

        [Test]
        public void Policy_RejectsPaidOrUncappedRewards_AndLedgerMismatch()
        {
            Assert.Throws<ArgumentException>(() => new CatchUpPolicy(14, 7, new Dictionary<string, long> { ["PremiumGems"] = 1 }));
            Assert.Throws<ArgumentException>(() => new CatchUpPolicy(14, 7,
                new Dictionary<string, long> { ["Coins"] = CatchUpPolicy.MaxRewardPerAssetPerWin + 1 }));
            Assert.Throws<ArgumentException>(() => new CatchUpPolicy(14, 7, new Dictionary<string, long>()));
            Assert.Throws<ArgumentOutOfRangeException>(() => new CatchUpPolicy(0, 7, new Dictionary<string, long> { ["Coins"] = 1 }));
            Assert.Throws<ArgumentException>(() =>
                new ComebackCatchUpService(Profile("Dino_Islands"), new RewardLedger("Coral_Rescue", Player), CatchUpPolicy.Standard));
        }
    }
}
