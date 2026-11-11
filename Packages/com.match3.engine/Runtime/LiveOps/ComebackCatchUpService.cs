using System;
using System.Collections.Generic;
using System.Linq;
using Match3.Engine.Runtime.Economy;

namespace Match3.Engine.Runtime.LiveOps
{
    // ENG-M20-013 Comeback Catch-Up service.
    // - Time comes only from a server-anchored UTC anchor (no device clock); all inputs must be DateTimeKind.Utc,
    //   so DST/timezone never affect eligibility.
    // - Genuine absence: anchor - lastActiveUtc >= InactivityThresholdDays. lastActive after the anchor, or an
    //   anchor before the open window's start, is a clock rollback and is rejected.
    // - One window per absence: windowId = "w" + lastActiveUtc.Ticks. After a window, a new window needs fresh
    //   activity after that window opened AND a new full absence.
    // - Max three earned wins; rewards are granted ONLY through RewardLedger with keys
    //   (catch_up, windowId#winIndex) and Reference = win event id, so replays/reinstalls cannot double-grant:
    //   wins and window end are re-derived from the ledger when local state is lost.
    // - Transparent explanation data only (remaining wins, window end, reward per win); no urgency flags;
    //   EmitEligibleSignal is true at most once per window.
    // - Child profiles (Wonder_Pop): TASKS says "non-targeted/off by default" and "disabled targeted
    //   reactivation"; no approved non-targeted track is specified, so the whole service is OFF (no signal, no grant).

    public readonly struct TrustedUtcAnchor
    {
        public DateTime UtcNow { get; }
        public bool IsServerAnchored { get; }

        private TrustedUtcAnchor(DateTime utcNow, bool serverAnchored)
        {
            UtcNow = utcNow;
            IsServerAnchored = serverAnchored;
        }

        public static TrustedUtcAnchor Server(DateTime utcNow) => new TrustedUtcAnchor(utcNow, true);
        public static TrustedUtcAnchor DeviceClock(DateTime now) => new TrustedUtcAnchor(now, false);
    }

    public static class CatchUpReasons
    {
        public const string ChildProfileOff = "ChildProfileCatchUpOff";
        public const string FeatureDisabled = "CatchUpFeatureDisabled";
        public const string TrustedTimeAnchorRequired = "TrustedTimeAnchorRequired";
        public const string NonUtcTime = "NonUtcTime";
        public const string ClockRollbackDetected = "ClockRollbackDetected";
        public const string InsufficientAbsence = "InsufficientAbsence";
        public const string NewAbsenceRequired = "NewAbsenceRequired";
        public const string WindowExpired = "WindowExpired";
        public const string NoActiveWindow = "NoActiveWindow";
        public const string WinCapReached = "CatchUpWinCapReached";
        public const string InvalidWinEvent = "InvalidWinEvent";
    }

    public sealed class CatchUpPolicy
    {
        public const int MaxRecoveryWins = 3;
        public const long MaxRewardPerAssetPerWin = 1000;
        public int InactivityThresholdDays { get; }
        public int WindowLengthDays { get; }
        public IReadOnlyDictionary<string, long> RewardPerWin { get; }

        public CatchUpPolicy(int inactivityThresholdDays, int windowLengthDays, IReadOnlyDictionary<string, long> rewardPerWin)
        {
            if (inactivityThresholdDays < 1 || inactivityThresholdDays > 365) throw new ArgumentOutOfRangeException(nameof(inactivityThresholdDays));
            if (windowLengthDays < 1 || windowLengthDays > 30) throw new ArgumentOutOfRangeException(nameof(windowLengthDays));
            if (rewardPerWin == null || rewardPerWin.Count == 0 || rewardPerWin.Any(kv =>
                    !RewardLedger.IsAssetId(kv.Key) || kv.Key == RewardLedger.PaidAssetId ||
                    kv.Value < 1 || kv.Value > MaxRewardPerAssetPerWin))
                throw new ArgumentException("Catch-up rewards must be capped, non-paid assets.", nameof(rewardPerWin));
            InactivityThresholdDays = inactivityThresholdDays;
            WindowLengthDays = windowLengthDays;
            RewardPerWin = RewardLedger.SortedCopy(rewardPerWin);
        }

        public static CatchUpPolicy Standard { get; } = new CatchUpPolicy(14, 7,
            new Dictionary<string, long> { [nameof(CurrencyType.Coins)] = 100, [nameof(CurrencyType.Boosters)] = 1 });
    }

    /// <summary>Persisted per-player window record (persist ToCanonical(), restore with TryParse).</summary>
    public sealed class CatchUpWindowState
    {
        public string WindowId { get; }
        public DateTime OpenedUtc { get; }
        public DateTime EndsUtc { get; }
        public bool SignalSent { get; }

        public CatchUpWindowState(string windowId, DateTime openedUtc, DateTime endsUtc, bool signalSent)
        {
            if (string.IsNullOrEmpty(windowId)) throw new ArgumentException("windowId required.", nameof(windowId));
            if (openedUtc.Kind != DateTimeKind.Utc || endsUtc.Kind != DateTimeKind.Utc || endsUtc <= openedUtc)
                throw new ArgumentException("UTC window with end after start required.", nameof(endsUtc));
            WindowId = windowId;
            OpenedUtc = openedUtc;
            EndsUtc = endsUtc;
            SignalSent = signalSent;
        }

        public string ToCanonical() => CanonicalJson.Serialize(new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["windowId"] = WindowId,
            ["openedTicks"] = OpenedUtc.Ticks,
            ["endsTicks"] = EndsUtc.Ticks,
            ["signalSent"] = SignalSent
        });

        public static bool TryParse(string text, out CatchUpWindowState state)
        {
            state = null;
            try
            {
                var d = (Dictionary<string, object>)CanonicalJson.Parse(text);
                var s = new CatchUpWindowState((string)d["windowId"], new DateTime((long)d["openedTicks"], DateTimeKind.Utc),
                    new DateTime((long)d["endsTicks"], DateTimeKind.Utc), (bool)d["signalSent"]);
                if (s.ToCanonical() != text) return false;
                state = s;
                return true;
            }
            catch (Exception e) when (e is FormatException || e is ArgumentException || e is InvalidCastException ||
                                      e is KeyNotFoundException || e is NullReferenceException)
            {
                return false;
            }
        }
    }

    public enum CatchUpStatus { Off, Ineligible, Active, Completed }

    public sealed class CatchUpExplanation
    {
        public string WindowId { get; }
        public int WinsEarned { get; }
        public int RemainingWins { get; }
        public int MaxWins => CatchUpPolicy.MaxRecoveryWins;
        public DateTime WindowEndsUtc { get; }
        public IReadOnlyDictionary<string, long> RewardPerWin { get; }

        internal CatchUpExplanation(string windowId, int winsEarned, DateTime endsUtc, IReadOnlyDictionary<string, long> rewardPerWin)
        {
            WindowId = windowId;
            WinsEarned = winsEarned;
            RemainingWins = CatchUpPolicy.MaxRecoveryWins - winsEarned;
            WindowEndsUtc = endsUtc;
            RewardPerWin = rewardPerWin;
        }
    }

    public sealed class CatchUpEvaluation
    {
        public CatchUpStatus Status { get; }
        public string Reason { get; }
        /// <summary>True at most once per window; the only hook a notification layer may use.</summary>
        public bool EmitEligibleSignal { get; }
        /// <summary>Null unless Active/Completed.</summary>
        public CatchUpExplanation Explanation { get; }

        internal CatchUpEvaluation(CatchUpStatus status, string reason, bool signal = false, CatchUpExplanation explanation = null)
        {
            Status = status;
            Reason = reason ?? string.Empty;
            EmitEligibleSignal = signal;
            Explanation = explanation;
        }
    }

    public sealed class ComebackCatchUpService
    {
        private readonly GameProfile _profile;
        private readonly RewardLedger _ledger;
        private readonly CatchUpPolicy _policy;

        public CatchUpWindowState State { get; private set; }

        public ComebackCatchUpService(GameProfile profile, RewardLedger ledger, CatchUpPolicy policy, CatchUpWindowState state = null)
        {
            _profile = profile ?? throw new ArgumentNullException(nameof(profile));
            _ledger = ledger ?? throw new ArgumentNullException(nameof(ledger));
            _policy = policy ?? throw new ArgumentNullException(nameof(policy));
            if (!string.Equals(profile.Id, ledger.GameId, StringComparison.Ordinal))
                throw new ArgumentException("Ledger belongs to another game.", nameof(ledger));
            State = state;
        }

        public CatchUpEvaluation Evaluate(TrustedUtcAnchor anchor, DateTime lastActiveUtc)
        {
            var blocked = Gate(anchor, lastActiveUtc);
            if (blocked != null) return blocked;
            var now = anchor.UtcNow;
            if (lastActiveUtc > now) return Ineligible(CatchUpReasons.ClockRollbackDetected);

            if (State != null && now < State.EndsUtc) return Active();
            if (State != null && lastActiveUtc <= State.OpenedUtc) return Ineligible(CatchUpReasons.NewAbsenceRequired);
            if (now - lastActiveUtc < TimeSpan.FromDays(_policy.InactivityThresholdDays))
                return Ineligible(CatchUpReasons.InsufficientAbsence);

            var windowId = "w" + lastActiveUtc.Ticks;
            var length = TimeSpan.FromDays(_policy.WindowLengthDays);
            // Reinstall / lost local state: an existing grant for this absence pins the original window end.
            var pinned = Slots(windowId).FirstOrDefault(r => r != null);
            var ends = pinned?.ExpiresUtc ?? now + length;
            State = new CatchUpWindowState(windowId, pinned != null ? ends - length : now, ends, false);
            return now < ends ? Active() : Ineligible(CatchUpReasons.WindowExpired);
        }

        /// <summary>Records one genuine level win in the open window and grants its capped reward exactly once.</summary>
        public RewardLedgerGrantResult RecordWin(TrustedUtcAnchor anchor, string winEventId)
        {
            var blocked = Gate(anchor, anchor.UtcNow);
            if (blocked != null) return RewardLedgerGrantResult.Rejected(blocked.Reason);
            if (State == null) return RewardLedgerGrantResult.Rejected(CatchUpReasons.NoActiveWindow);
            if (string.IsNullOrEmpty(winEventId)) return RewardLedgerGrantResult.Rejected(CatchUpReasons.InvalidWinEvent);
            var now = anchor.UtcNow;
            if (now < State.OpenedUtc) return RewardLedgerGrantResult.Rejected(CatchUpReasons.ClockRollbackDetected);
            if (now >= State.EndsUtc) return RewardLedgerGrantResult.Rejected(CatchUpReasons.WindowExpired);

            var slots = Slots(State.WindowId);
            for (var i = 0; i < slots.Length; i++)
            {
                if (slots[i] == null)
                    return _ledger.TryGrant(new RewardLedgerRequest(_ledger.GameId, _ledger.PlayerId, RewardSources.CatchUp,
                        SlotInstance(State.WindowId, i + 1), _policy.RewardPerWin, now, State.EndsUtc, winEventId));
                if (string.Equals(slots[i].Reference, winEventId, StringComparison.Ordinal))
                    return RewardLedgerGrantResult.Duplicate(slots[i]);
            }
            return RewardLedgerGrantResult.Rejected(CatchUpReasons.WinCapReached);
        }

        private CatchUpEvaluation Gate(TrustedUtcAnchor anchor, DateTime lastActiveUtc)
        {
            if (EconomyBalanceSimulator.IsFamilySafeProfile(_profile.Id)) return new CatchUpEvaluation(CatchUpStatus.Off, CatchUpReasons.ChildProfileOff);
            if (!_profile.IsEnabled("catch_up", EngineCompatibility.MinimumCapability))
                return new CatchUpEvaluation(CatchUpStatus.Off, CatchUpReasons.FeatureDisabled);
            if (!anchor.IsServerAnchored) return Ineligible(CatchUpReasons.TrustedTimeAnchorRequired);
            if (anchor.UtcNow.Kind != DateTimeKind.Utc || lastActiveUtc.Kind != DateTimeKind.Utc)
                return Ineligible(CatchUpReasons.NonUtcTime);
            if (State != null && anchor.UtcNow < State.OpenedUtc) return Ineligible(CatchUpReasons.ClockRollbackDetected);
            return null;
        }

        private CatchUpEvaluation Active()
        {
            var wins = Slots(State.WindowId).Count(r => r != null);
            var explanation = new CatchUpExplanation(State.WindowId, wins, State.EndsUtc, _policy.RewardPerWin);
            if (wins >= CatchUpPolicy.MaxRecoveryWins)
                return new CatchUpEvaluation(CatchUpStatus.Completed, CatchUpReasons.WinCapReached, false, explanation);
            var signal = !State.SignalSent;
            if (signal) State = new CatchUpWindowState(State.WindowId, State.OpenedUtc, State.EndsUtc, true);
            return new CatchUpEvaluation(CatchUpStatus.Active, null, signal, explanation);
        }

        private RewardLedgerRecord[] Slots(string windowId)
        {
            var slots = new RewardLedgerRecord[CatchUpPolicy.MaxRecoveryWins];
            for (var i = 0; i < slots.Length; i++)
                _ledger.TryGetRecord(RewardLedgerKey.Derive(_ledger.GameId, _ledger.PlayerId, RewardSources.CatchUp,
                    SlotInstance(windowId, i + 1)), out slots[i]);
            return slots;
        }

        private static string SlotInstance(string windowId, int winIndex) => windowId + "#" + winIndex;

        private static CatchUpEvaluation Ineligible(string reason) => new CatchUpEvaluation(CatchUpStatus.Ineligible, reason);
    }
}
