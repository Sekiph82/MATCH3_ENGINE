using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Match3.Engine.Runtime.Economy;

namespace Match3.Engine.Runtime.LiveOps
{
    // ENG-M20-013 Comeback Catch-Up service.
    // - Both "now" and the player's last-active time must be server-anchored TrustedUtcAnchor values (no device
    //   clock); all times are DateTimeKind.Utc so DST/timezone never affect eligibility.
    // - Genuine absence: now - lastActive >= InactivityThresholdDays. lastActive after now, or now before the open
    //   window's start, is a clock rollback and is rejected.
    // - One window per absence: windowId = "w" + lastActive.Ticks. A new window needs activity after the previous
    //   window opened and a new full absence. Independently of local state, the ledger's catch_up keys (live and
    //   settled) are consulted: a new absence must start >= latest recorded absence + threshold, so lost state plus
    //   a fabricated older/near lastActive cannot open a second track.
    // - Max three earned wins, granted ONLY through RewardLedger with keys (catch_up, windowId#winIndex),
    //   Reference = win event id and ExpiresUtc = window end, so replays/reinstalls cannot double-grant; when local
    //   state is lost the window end and its start (earliest win) are re-derived from the ledger, never from policy.
    // - Wins are ENG-M08-015 VerifiedProgressionResults (verified, won, same game/profile, completed inside the window
    //   by the server anchor); the result id is the ledger Reference. The string overload is [Obsolete], fails closed.
    // - Persisted state is validated on construction (windowId "w"+ticks, opened >= absence + threshold, length <=
    //   policy, and no evidence-free window starting within threshold of the latest recorded absence); a window with
    //   ledger grants counts as already signalled. Callers persist State before showing the signal.
    // - Transparent explanation data only (remaining wins, window end, reward per win); no urgency flags.
    // - Gated by the per-title "catch_up" feature flag (default OFF). Child players (per-player input): OFF - no
    //   window, no signal, no grants. Family-safe titles (Wonder_Pop, general/family audience): non-targeted track
    //   only - the window works but EmitEligibleSignal is never raised. Otherwise at most one signal per window.

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
        public const string TrustedLastActiveRequired = "TrustedLastActiveRequired";
        public const string NonUtcTime = "NonUtcTime";
        public const string ClockRollbackDetected = "ClockRollbackDetected";
        public const string InsufficientAbsence = "InsufficientAbsence";
        public const string NewAbsenceRequired = "NewAbsenceRequired";
        public const string RecentCatchUpWindow = "RecentCatchUpWindow";
        public const string WindowExpired = "WindowExpired";
        public const string NoActiveWindow = "NoActiveWindow";
        public const string WinCapReached = "CatchUpWinCapReached";
        public const string VerifiedWinRequired = "VerifiedWinRequired";
        public const string UnverifiedWin = "UnverifiedWin";
        public const string NotAWin = "NotAWin";
        public const string WinOwnerMismatch = "WinOwnerMismatch";
        public const string CompletionOutsideWindow = "CompletionOutsideWindow";
        public const string InvalidWindowState = "InvalidWindowState";
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
                    !RewardLedger.IsKnownAsset(kv.Key) || kv.Key == RewardLedger.PaidAssetId ||
                    kv.Value < 1 || kv.Value > MaxRewardPerAssetPerWin))
                throw new ArgumentException("Catch-up rewards must be capped, non-paid registered assets.", nameof(rewardPerWin));
            InactivityThresholdDays = inactivityThresholdDays;
            WindowLengthDays = windowLengthDays;
            RewardPerWin = RewardLedger.SortedCopy(rewardPerWin);
        }

        public static CatchUpPolicy Standard { get; } = new CatchUpPolicy(14, 7,
            new Dictionary<string, long> { [nameof(CurrencyType.Coins)] = 100, [nameof(CurrencyType.Boosters)] = 1 });
    }

    /// <summary>Persisted per-player window record (persist ToCanonical(), restore with TryParse).
    /// The window length is persisted as EndsUtc - OpenedUtc and never re-derived from the current policy.</summary>
    public sealed class CatchUpWindowState
    {
        public string WindowId { get; }
        public DateTime OpenedUtc { get; }
        public DateTime EndsUtc { get; }
        public bool SignalSent { get; }
        public TimeSpan WindowLength => EndsUtc - OpenedUtc;

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
        /// <summary>True at most once per window and never for family-safe titles; the only notification hook.
        /// Callers must persist <see cref="ComebackCatchUpService.State"/> (SignalSent) before showing the signal.</summary>
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
        private readonly bool _childPlayer;

        public CatchUpWindowState State { get; private set; }

        /// <param name="state">Persisted window record. Validated against the policy and the ledger's catch_up history;
        /// an inconsistent record throws ArgumentException (InvalidWindowState). If the window already has ledger
        /// grants its signal counts as sent.</param>
        public ComebackCatchUpService(GameProfile profile, RewardLedger ledger, CatchUpPolicy policy,
            bool playerIsChildProfile, CatchUpWindowState state = null)
        {
            _profile = profile ?? throw new ArgumentNullException(nameof(profile));
            _ledger = ledger ?? throw new ArgumentNullException(nameof(ledger));
            _policy = policy ?? throw new ArgumentNullException(nameof(policy));
            if (!string.Equals(profile.Id, ledger.GameId, StringComparison.Ordinal))
                throw new ArgumentException("Ledger belongs to another game.", nameof(ledger));
            _childPlayer = playerIsChildProfile;
            if (state != null)
            {
                var absence = ParseAbsenceTicks(state.WindowId);
                var threshold = TimeSpan.FromDays(policy.InactivityThresholdDays).Ticks;
                var hasEvidence = Enumerable.Range(1, CatchUpPolicy.MaxRecoveryWins).Any(i => ledger.ContainsKey(SlotKey(state.WindowId, i)));
                var latest = LatestRecordedAbsence();
                if (!absence.HasValue || state.OpenedUtc.Ticks - absence.Value < threshold ||
                    state.WindowLength > TimeSpan.FromDays(policy.WindowLengthDays) ||
                    !hasEvidence && latest.HasValue && absence.Value < latest.Value + threshold)
                    throw new ArgumentException(CatchUpReasons.InvalidWindowState, nameof(state));
                if (hasEvidence && !state.SignalSent)
                    state = new CatchUpWindowState(state.WindowId, state.OpenedUtc, state.EndsUtc, true);
            }
            State = state;
        }

        public CatchUpEvaluation Evaluate(TrustedUtcAnchor now, TrustedUtcAnchor lastActive)
        {
            var blocked = Gate(now, lastActive);
            if (blocked != null) return blocked;
            var t = now.UtcNow;
            var last = lastActive.UtcNow;
            if (last > t) return Ineligible(CatchUpReasons.ClockRollbackDetected);

            if (State != null && t < State.EndsUtc) return Active();
            if (State != null && last <= State.OpenedUtc) return Ineligible(CatchUpReasons.NewAbsenceRequired);
            var threshold = TimeSpan.FromDays(_policy.InactivityThresholdDays);
            if (t - last < threshold) return Ineligible(CatchUpReasons.InsufficientAbsence);

            var windowId = "w" + last.Ticks.ToString(CultureInfo.InvariantCulture);
            var used = Enumerable.Range(1, CatchUpPolicy.MaxRecoveryWins).Select(i => SlotKey(windowId, i)).Where(_ledger.ContainsKey).ToList();
            if (used.Count > 0)
            {
                // Lost local state: re-derive the window from its live grants (end = expiry, start = earliest win).
                var live = used.Select(k => _ledger.TryGetRecord(k, out var r) ? r : null).Where(r => r != null).ToList();
                if (live.Count == 0) return Ineligible(CatchUpReasons.WindowExpired); // settled long ago
                State = new CatchUpWindowState(windowId, live.Min(r => r.GrantedUtc), live[0].ExpiresUtc.Value, true);
                return t < State.EndsUtc ? Active() : Ineligible(CatchUpReasons.WindowExpired);
            }
            var latest = LatestRecordedAbsence();
            if (latest.HasValue && last.Ticks < latest.Value + threshold.Ticks) return Ineligible(CatchUpReasons.RecentCatchUpWindow);

            State = new CatchUpWindowState(windowId, t, t + TimeSpan.FromDays(_policy.WindowLengthDays), false);
            return Active();
        }

        /// <summary>Superseded: an arbitrary string is not a verified win. Always fails closed.</summary>
        [Obsolete("Pass the ENG-M08-015 VerifiedProgressionResult; this overload always returns VerifiedWinRequired.")]
        public RewardLedgerGrantResult RecordWin(TrustedUtcAnchor now, string winEventId) =>
            RewardLedgerGrantResult.Rejected(CatchUpReasons.VerifiedWinRequired);

        /// <summary>
        /// Records one verified, won level result (ENG-M08-015) completed inside the open window and grants its capped
        /// reward exactly once; the result id is the ledger Reference. Trust contract as ENG-M08-015: results come
        /// from the server-side verifier only.
        /// </summary>
        /// <param name="verifiedCompletionUtc">Server-anchored completion instant of the result.</param>
        public RewardLedgerGrantResult RecordWin(TrustedUtcAnchor now, VerifiedProgressionResult result, DateTime verifiedCompletionUtc)
        {
            var blocked = Gate(now, now);
            if (blocked != null) return RewardLedgerGrantResult.Rejected(blocked.Reason);
            if (State == null) return RewardLedgerGrantResult.Rejected(CatchUpReasons.NoActiveWindow);
            if (result == null || !result.Verified) return RewardLedgerGrantResult.Rejected(CatchUpReasons.UnverifiedWin);
            if (!result.Won) return RewardLedgerGrantResult.Rejected(CatchUpReasons.NotAWin);
            if (!string.Equals(result.GameId, _ledger.GameId, StringComparison.Ordinal) ||
                !string.Equals(result.ProfileId, _ledger.PlayerId, StringComparison.Ordinal))
                return RewardLedgerGrantResult.Rejected(CatchUpReasons.WinOwnerMismatch);
            if (verifiedCompletionUtc.Kind != DateTimeKind.Utc) return RewardLedgerGrantResult.Rejected(CatchUpReasons.NonUtcTime);
            if (now.UtcNow >= State.EndsUtc) return RewardLedgerGrantResult.Rejected(CatchUpReasons.WindowExpired);
            if (verifiedCompletionUtc < State.OpenedUtc || verifiedCompletionUtc >= State.EndsUtc || verifiedCompletionUtc > now.UtcNow)
                return RewardLedgerGrantResult.Rejected(CatchUpReasons.CompletionOutsideWindow);
            var winEventId = result.ResultId;

            for (var i = 1; i <= CatchUpPolicy.MaxRecoveryWins; i++)
            {
                var key = SlotKey(State.WindowId, i);
                if (_ledger.TryGetRecord(key, out var rec))
                {
                    if (string.Equals(rec.Reference, winEventId, StringComparison.Ordinal)) return RewardLedgerGrantResult.Duplicate(rec);
                    continue;
                }
                if (_ledger.ContainsKey(key)) continue;
                return _ledger.TryGrant(new RewardLedgerRequest(_ledger.GameId, _ledger.PlayerId, RewardSources.CatchUp,
                    SlotInstance(State.WindowId, i), _policy.RewardPerWin, now.UtcNow, State.EndsUtc, winEventId), now);
            }
            return RewardLedgerGrantResult.Rejected(CatchUpReasons.WinCapReached);
        }

        private CatchUpEvaluation Gate(TrustedUtcAnchor now, TrustedUtcAnchor lastActive)
        {
            if (_childPlayer) return new CatchUpEvaluation(CatchUpStatus.Off, CatchUpReasons.ChildProfileOff);
            if (!_profile.IsEnabled("catch_up", EngineCompatibility.MinimumCapability))
                return new CatchUpEvaluation(CatchUpStatus.Off, CatchUpReasons.FeatureDisabled);
            if (!now.IsServerAnchored) return Ineligible(CatchUpReasons.TrustedTimeAnchorRequired);
            if (!lastActive.IsServerAnchored) return Ineligible(CatchUpReasons.TrustedLastActiveRequired);
            if (now.UtcNow.Kind != DateTimeKind.Utc || lastActive.UtcNow.Kind != DateTimeKind.Utc)
                return Ineligible(CatchUpReasons.NonUtcTime);
            if (State != null && now.UtcNow < State.OpenedUtc) return Ineligible(CatchUpReasons.ClockRollbackDetected);
            return null;
        }

        private CatchUpEvaluation Active()
        {
            var wins = Enumerable.Range(1, CatchUpPolicy.MaxRecoveryWins).Count(i => _ledger.ContainsKey(SlotKey(State.WindowId, i)));
            var explanation = new CatchUpExplanation(State.WindowId, wins, State.EndsUtc, _policy.RewardPerWin);
            if (wins >= CatchUpPolicy.MaxRecoveryWins)
                return new CatchUpEvaluation(CatchUpStatus.Completed, CatchUpReasons.WinCapReached, false, explanation);
            var signal = !State.SignalSent && !EconomyBalanceSimulator.IsFamilySafeProfile(_profile.Id);
            if (!State.SignalSent) State = new CatchUpWindowState(State.WindowId, State.OpenedUtc, State.EndsUtc, true);
            return new CatchUpEvaluation(CatchUpStatus.Active, null, signal, explanation);
        }

        /// <summary>Latest absence start (ticks) of any catch_up window recorded in the ledger, live or settled.</summary>
        private long? LatestRecordedAbsence()
        {
            long? latest = null;
            foreach (var id in _ledger.InstanceIds(RewardSources.CatchUp))
            {
                var hash = id.IndexOf('#');
                var ticks = hash > 0 ? ParseAbsenceTicks(id.Substring(0, hash)) : null;
                if (ticks.HasValue && (!latest.HasValue || ticks.Value > latest.Value)) latest = ticks;
            }
            return latest;
        }

        /// <summary>"w" + absence-start ticks (digits only, within DateTime range) -> ticks; otherwise null.</summary>
        private static long? ParseAbsenceTicks(string windowId) =>
            windowId != null && windowId.Length > 1 && windowId[0] == 'w' &&
            long.TryParse(windowId.Substring(1), NumberStyles.None, CultureInfo.InvariantCulture, out var ticks) &&
            ticks <= DateTime.MaxValue.Ticks &&
            windowId == "w" + ticks.ToString(CultureInfo.InvariantCulture)
                ? ticks : (long?)null;

        private string SlotKey(string windowId, int winIndex) =>
            RewardLedgerKey.Derive(_ledger.GameId, _ledger.PlayerId, RewardSources.CatchUp, SlotInstance(windowId, winIndex));

        private static string SlotInstance(string windowId, int winIndex) => windowId + "#" + winIndex;

        private static CatchUpEvaluation Ineligible(string reason) => new CatchUpEvaluation(CatchUpStatus.Ineligible, reason);
    }
}
