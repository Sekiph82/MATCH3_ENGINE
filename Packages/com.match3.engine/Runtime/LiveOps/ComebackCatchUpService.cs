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
    //   A new window's absence must also be strictly after every live catch_up grant (the player was active then).
    // - Wins are ENG-M08-015 VerifiedProgressionResults (verified, won, same game/profile, completed inside the window
    //   by the server anchor, CanonicalWeekId = ISO week of that completion); the result id is the ledger Reference
    //   and pays at most one catch-up win across all windows (WinAlreadyRewarded). The string overload is [Obsolete].
    // - The service is bound to the server last-active (constructor); persisted state is validated against it and the
    //   ledger (see constructor doc); a window with ledger grants counts as already signalled. The constructor without
    //   last-active is [Obsolete] and fails closed. Callers persist State before showing the signal.
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
        public const string WinAlreadyRewarded = "WinAlreadyRewarded";
        public const string CompletionWeekMismatch = "CompletionWeekMismatch";
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
        /// <summary>Non-null when the service was built without a trusted server last-active: every call fails closed.</summary>
        private readonly string _bindingError;

        public CatchUpWindowState State { get; private set; }

        /// <summary>Superseded: a persisted window cannot be checked without the server last-active. Fails closed.</summary>
        [Obsolete("Pass the server-anchored last-active TrustedUtcAnchor; this overload always returns TrustedLastActiveRequired.")]
        public ComebackCatchUpService(GameProfile profile, RewardLedger ledger, CatchUpPolicy policy,
            bool playerIsChildProfile, CatchUpWindowState state = null)
            : this(profile, ledger, policy, playerIsChildProfile, default(TrustedUtcAnchor), null)
        {
        }

        /// <param name="lastActive">Server-recorded last activity BEFORE the current session (the absence start).</param>
        /// <param name="state">Persisted window record, validated against the policy, the server last-active and the
        /// ledger's catch_up history; an inconsistent record throws ArgumentException (InvalidWindowState).
        /// - With ledger grants for its windowId: EndsUtc must equal their expiry and OpenedUtc must not be after the
        ///   earliest grant (same derivation as Evaluate); a fully settled window cannot be restored. Signal counts as sent.
        /// - Without ledger grants: its absence must equal <paramref name="lastActive"/>, be strictly after every live
        ///   catch_up grant and at least one threshold after the latest recorded absence.
        /// - Always: windowId "w"+ticks, OpenedUtc >= absence + threshold, length <= policy window.</param>
        public ComebackCatchUpService(GameProfile profile, RewardLedger ledger, CatchUpPolicy policy,
            bool playerIsChildProfile, TrustedUtcAnchor lastActive, CatchUpWindowState state = null)
        {
            _profile = profile ?? throw new ArgumentNullException(nameof(profile));
            _ledger = ledger ?? throw new ArgumentNullException(nameof(ledger));
            _policy = policy ?? throw new ArgumentNullException(nameof(policy));
            if (!string.Equals(profile.Id, ledger.GameId, StringComparison.Ordinal))
                throw new ArgumentException("Ledger belongs to another game.", nameof(ledger));
            _childPlayer = playerIsChildProfile;
            if (!lastActive.IsServerAnchored || lastActive.UtcNow.Kind != DateTimeKind.Utc)
            {
                _bindingError = CatchUpReasons.TrustedLastActiveRequired;
                return;
            }
            if (state != null)
            {
                var absence = ParseAbsenceTicks(state.WindowId);
                var threshold = TimeSpan.FromDays(policy.InactivityThresholdDays).Ticks;
                if (!absence.HasValue || state.OpenedUtc.Ticks - absence.Value < threshold ||
                    state.WindowLength > TimeSpan.FromDays(policy.WindowLengthDays))
                    throw new ArgumentException(CatchUpReasons.InvalidWindowState, nameof(state));
                var keys = Enumerable.Range(1, CatchUpPolicy.MaxRecoveryWins).Select(i => SlotKey(state.WindowId, i)).ToList();
                var live = keys.Select(k => ledger.TryGetRecord(k, out var r) ? r : null).Where(r => r != null).ToList();
                if (keys.Any(ledger.ContainsKey))
                {
                    if (live.Count == 0 || live.Any(r => r.ExpiresUtc != state.EndsUtc) ||
                        state.OpenedUtc > live.Min(r => r.GrantedUtc))
                        throw new ArgumentException(CatchUpReasons.InvalidWindowState, nameof(state));
                    if (!state.SignalSent) state = new CatchUpWindowState(state.WindowId, state.OpenedUtc, state.EndsUtc, true);
                }
                else
                {
                    var latest = LatestRecordedAbsence();
                    var lastGrant = LatestLiveCatchUpGrantTicks();
                    if (absence.Value != lastActive.UtcNow.Ticks ||
                        lastGrant.HasValue && absence.Value <= lastGrant.Value ||
                        latest.HasValue && absence.Value < latest.Value + threshold)
                        throw new ArgumentException(CatchUpReasons.InvalidWindowState, nameof(state));
                }
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
            var lastGrant = LatestLiveCatchUpGrantTicks();
            if (latest.HasValue && last.Ticks < latest.Value + threshold.Ticks ||
                lastGrant.HasValue && last.Ticks <= lastGrant.Value)
                return Ineligible(CatchUpReasons.RecentCatchUpWindow);

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
            if (!string.Equals(result.CanonicalWeekId, IsoWeekWindow.WeekId(verifiedCompletionUtc), StringComparison.Ordinal))
                return RewardLedgerGrantResult.Rejected(CatchUpReasons.CompletionWeekMismatch);
            var winEventId = result.ResultId;

            var free = 0;
            for (var i = CatchUpPolicy.MaxRecoveryWins; i >= 1; i--)
            {
                var key = SlotKey(State.WindowId, i);
                if (_ledger.TryGetRecord(key, out var rec))
                {
                    if (string.Equals(rec.Reference, winEventId, StringComparison.Ordinal)) return RewardLedgerGrantResult.Duplicate(rec);
                }
                else if (!_ledger.ContainsKey(key)) free = i;
            }
            // One verified result pays at most one catch-up win across ALL windows. Settled (tombstoned) windows ended
            // at least retention ago, so their results cannot also satisfy this window's completion-week check.
            if (LiveCatchUpRecords().Any(r => string.Equals(r.Reference, winEventId, StringComparison.Ordinal)))
                return RewardLedgerGrantResult.Rejected(CatchUpReasons.WinAlreadyRewarded);
            if (free == 0) return RewardLedgerGrantResult.Rejected(CatchUpReasons.WinCapReached);
            return _ledger.TryGrant(new RewardLedgerRequest(_ledger.GameId, _ledger.PlayerId, RewardSources.CatchUp,
                SlotInstance(State.WindowId, free), _policy.RewardPerWin, now.UtcNow, State.EndsUtc, winEventId), now);
        }

        private CatchUpEvaluation Gate(TrustedUtcAnchor now, TrustedUtcAnchor lastActive)
        {
            if (_childPlayer) return new CatchUpEvaluation(CatchUpStatus.Off, CatchUpReasons.ChildProfileOff);
            if (!_profile.IsEnabled("catch_up", EngineCompatibility.MinimumCapability))
                return new CatchUpEvaluation(CatchUpStatus.Off, CatchUpReasons.FeatureDisabled);
            if (_bindingError != null) return Ineligible(_bindingError);
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

        private IEnumerable<RewardLedgerRecord> LiveCatchUpRecords() =>
            _ledger.InstanceIds(RewardSources.CatchUp)
                .Select(id => _ledger.TryGetRecord(RewardLedgerKey.Derive(_ledger.GameId, _ledger.PlayerId, RewardSources.CatchUp, id), out var r) ? r : null)
                .Where(r => r != null);

        /// <summary>Latest GrantedUtc (ticks) of any live catch_up grant: the player was active then.</summary>
        private long? LatestLiveCatchUpGrantTicks()
        {
            long? latest = null;
            foreach (var r in LiveCatchUpRecords())
                if (!latest.HasValue || r.GrantedUtc.Ticks > latest.Value) latest = r.GrantedUtc.Ticks;
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
