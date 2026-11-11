using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Security.Cryptography;
using Match3.Engine.Runtime.Economy;
using Match3.Engine.Runtime.Identity;
using Match3.Engine.Runtime.Privacy;

namespace Match3.Engine.Runtime.LiveOps
{
    // ENG-M20-015 optional asynchronous tournament (engine data model only; no networking, no UI).
    //
    // - Non-wagering: there is no entry price/fee field anywhere; prizes are a registered non-premium ledger asset
    //   granted through the ENG-M16-011 RewardLedger (source "tournament", instance "tournamentId|prize",
    //   ExpiresUtc = EndUtc + PrizeClaimWindow, strict TryGrant with a server TrustedUtcAnchor).
    // - DEFAULT OFF: every step (admission, attempt, submission, prize issue) re-checks an enabled TournamentMode
    //   flag through FeatureFlagGate (kill switch, consent check, Wonder_Pop hard block) and the player's
    //   eligibility: ChildAccountGate.CanUseSocialFeatures (no child / unverified minor / local-only profile),
    //   explicit consent granted, not a minor, not a restricted region, region allow-list. Wonder_Pop cannot even
    //   construct a service.
    // - Attempts: IssueAttempt hands out a server AttemptTicket (tournament, player, attempt number, server issue
    //   time, random nonce, seed). By default each attempt gets its own random server seed, so a board cannot be
    //   solved offline in advance; a shared seed is opt-in per template. Submit needs the ticket and consumes it once.
    // - Verification: the score is recomputed by ModeSession.Replay (injected pinned-SDK matcher); a different
    //   claimed score is TamperedScore. The run's active time may not exceed the server wall time between ticket
    //   issue and receipt (+ ClockToleranceMs), and ModeSession rejects swaps closer than MinSwapIntervalMs.
    //   The canonical RunId (applied swaps only) dedupes: the same run from another player is Copied.
    // - Ranking: score desc, then earliest verified (server-receipt) completion UTC, then run ID ordinal.
    // - Window [StartUtc, EndUtc): nothing is accepted at or after EndUtc. Close (server anchor >= EndUtc) freezes
    //   standings once; every Close call (re)issues prizes through the ledger, which grants each key exactly once.
    // - Withheld-prize policy: a ranked player who is ineligible at Close (flag killed, consent revoked, profile now
    //   restricted, region excluded) gets nothing in that call; ranks are NOT shifted to others. A later Close
    //   within the claim window re-evaluates and may grant it once.
    // - Leaderboard view: never the raw player ID; only an opt-in display handle allowed by
    //   ChildAccountGate.CanUsePublicHandle (otherwise null = anonymous).
    //
    // ponytail: in-memory, single tournament instance; persistence and the backend leaderboard come with
    // ENG-M21-007/011.
    public sealed class TournamentPrizeTier
    {
        public int MaxRank { get; }
        public int Amount { get; }

        public TournamentPrizeTier(int maxRank, int amount)
        {
            if (maxRank < 1 || maxRank > 100000) throw new ArgumentOutOfRangeException(nameof(maxRank));
            if (amount < 1 || amount > 100000) throw new ArgumentOutOfRangeException(nameof(amount));
            MaxRank = maxRank;
            Amount = amount;
        }
    }

    public sealed class TournamentTemplate
    {
        public string TournamentId { get; }
        public ModeTemplate Mode { get; }
        /// <summary>0 = a fresh server seed per attempt (default). Non-zero = every player gets this seed.</summary>
        public uint SharedSeed { get; }
        public bool PerAttemptSeed => SharedSeed == 0;
        public DateTime StartUtc { get; }
        public DateTime EndUtc { get; }
        public IReadOnlyCollection<string> AllowedRegions { get; }
        public int MaxAttemptsPerPlayer { get; }
        public TimeSpan MinAttemptInterval { get; }
        /// <summary>Pause/Background/Reconnect inputs allowed per run: a frozen timer is free thinking time.</summary>
        public int MaxInterruptionsPerRun { get; }
        public long ClockToleranceMs { get; }
        public TimeSpan PrizeClaimWindow { get; }
        /// <summary>Registered non-premium ledger asset (CurrencyType name).</summary>
        public string PrizeAssetId { get; }
        public IReadOnlyList<TournamentPrizeTier> PrizeTiers { get; }

        public TournamentTemplate(string tournamentId, ModeTemplate mode, DateTime startUtc, DateTime endUtc,
            IEnumerable<string> allowedRegions, int maxAttemptsPerPlayer, TimeSpan minAttemptInterval,
            int maxInterruptionsPerRun, string prizeAssetId, IEnumerable<TournamentPrizeTier> prizeTiers,
            uint sharedSeed = 0, long clockToleranceMs = 2000, TimeSpan? prizeClaimWindow = null)
        {
            if (!ContentMetadataReceiver.IsValidId(tournamentId))
                throw new ArgumentException("Invalid tournament ID.", nameof(tournamentId));
            Mode = mode ?? throw new ArgumentNullException(nameof(mode));
            if (mode.Kind == GameModeKind.Classic)
                throw new ArgumentException("Tournaments run alternative-mode templates, not Classic.", nameof(mode));
            if (startUtc.Kind != DateTimeKind.Utc || endUtc.Kind != DateTimeKind.Utc || endUtc <= startUtc ||
                endUtc - startUtc > TimeSpan.FromDays(31))
                throw new ArgumentException("Window must be UTC, non-empty and at most 31 days.");
            var regions = (allowedRegions ?? Enumerable.Empty<string>()).ToList();
            if (regions.Count == 0 || regions.Any(r => r == null || r.Length != 2 || !r.All(c => c >= 'A' && c <= 'Z')))
                throw new ArgumentException("Allowed regions must be ISO 3166-1 alpha-2 codes.", nameof(allowedRegions));
            if (maxAttemptsPerPlayer < 1 || maxAttemptsPerPlayer > 50)
                throw new ArgumentOutOfRangeException(nameof(maxAttemptsPerPlayer));
            if (minAttemptInterval < TimeSpan.Zero || minAttemptInterval > TimeSpan.FromDays(1))
                throw new ArgumentOutOfRangeException(nameof(minAttemptInterval));
            if (maxInterruptionsPerRun < 0 || maxInterruptionsPerRun > 20)
                throw new ArgumentOutOfRangeException(nameof(maxInterruptionsPerRun));
            if (clockToleranceMs < 0 || clockToleranceMs > 60000)
                throw new ArgumentOutOfRangeException(nameof(clockToleranceMs));
            var claim = prizeClaimWindow ?? TimeSpan.FromDays(30);
            if (claim < TimeSpan.FromDays(1) || claim > TimeSpan.FromDays(365))
                throw new ArgumentOutOfRangeException(nameof(prizeClaimWindow));
            if (!RewardLedger.IsKnownAsset(prizeAssetId) || prizeAssetId == RewardLedger.PaidAssetId)
                throw new ArgumentException("Prize must be a registered non-premium ledger asset.", nameof(prizeAssetId));
            var tiers = (prizeTiers ?? Enumerable.Empty<TournamentPrizeTier>()).ToList();
            if (tiers.Count == 0 || tiers.Count > 10 || tiers.Any(t => t == null) ||
                tiers.Zip(tiers.Skip(1), (a, b) => a.MaxRank < b.MaxRank && a.Amount >= b.Amount).Any(ok => !ok))
                throw new ArgumentException("Prize tiers: ranks strictly increase, amounts never increase.", nameof(prizeTiers));

            TournamentId = tournamentId;
            SharedSeed = sharedSeed;
            StartUtc = startUtc;
            EndUtc = endUtc;
            AllowedRegions = new ReadOnlyCollection<string>(regions.Distinct(StringComparer.Ordinal).ToList());
            MaxAttemptsPerPlayer = maxAttemptsPerPlayer;
            MinAttemptInterval = minAttemptInterval;
            MaxInterruptionsPerRun = maxInterruptionsPerRun;
            ClockToleranceMs = clockToleranceMs;
            PrizeClaimWindow = claim;
            PrizeAssetId = prizeAssetId;
            PrizeTiers = tiers.AsReadOnly();
        }
    }

    /// <summary>Per-player eligibility facts, re-checked at admission, attempt, submission and prize issue.</summary>
    public sealed class TournamentEligibility
    {
        public ChildProfile Profile { get; }
        public ConsentRecord Consent { get; }
        public string RegionCode { get; }
        /// <summary>Social policy: AllowSocialFeatures gates entry, AllowPublicHandle gates the display handle.</summary>
        public ChildAccountPolicy SocialPolicy { get; }

        public TournamentEligibility(ChildProfile profile, ConsentRecord consent, string regionCode,
            ChildAccountPolicy socialPolicy)
        {
            Profile = profile;
            Consent = consent;
            RegionCode = regionCode;
            SocialPolicy = socialPolicy;
        }
    }

    /// <summary>Server-issued, single-use permission for one tournament attempt.</summary>
    public sealed class AttemptTicket
    {
        public string TournamentId { get; }
        public string PlayerId { get; }
        public int AttemptNo { get; }
        public DateTime ServerIssuedUtc { get; }
        public string Nonce { get; }
        public uint Seed { get; }

        public AttemptTicket(string tournamentId, string playerId, int attemptNo, DateTime serverIssuedUtc,
            string nonce, uint seed)
        {
            TournamentId = tournamentId;
            PlayerId = playerId;
            AttemptNo = attemptNo;
            ServerIssuedUtc = serverIssuedUtc;
            Nonce = nonce;
            Seed = seed;
        }
    }

    /// <summary>Public leaderboard row: no player ID, only an allowed opt-in display handle (null = anonymous).</summary>
    public sealed class TournamentStandingView
    {
        public int Rank { get; }
        public string DisplayHandle { get; }
        public int Score { get; }
        public DateTime VerifiedCompletionUtc { get; }
        public string RunId { get; }

        internal TournamentStandingView(int rank, string handle, int score, DateTime completionUtc, string runId)
        {
            Rank = rank;
            DisplayHandle = handle;
            Score = score;
            VerifiedCompletionUtc = completionUtc;
            RunId = runId;
        }
    }

    public sealed class TournamentWithheldPrize
    {
        public int Rank { get; }
        public string Reason { get; }

        internal TournamentWithheldPrize(int rank, string reason)
        {
            Rank = rank;
            Reason = reason;
        }
    }

    public enum TournamentSubmitStatus
    {
        Accepted, NotAdmitted, NotEligible, NotOpen, Closed, UnknownTicket, TicketUsed, InvalidLog, Incomplete,
        TamperedScore, TooFast, TooManyInterruptions, DuplicateRun, Copied
    }

    public sealed class TournamentCloseResult
    {
        public bool FirstClose { get; }
        public IReadOnlyList<TournamentStandingView> Standings { get; }
        /// <summary>Ledger records applied by THIS call (each prize key is applied at most once ever).</summary>
        public IReadOnlyList<RewardLedgerRecord> Granted { get; }
        public int AlreadyGranted { get; }
        public IReadOnlyList<TournamentWithheldPrize> Withheld { get; }

        internal TournamentCloseResult(bool firstClose, IList<TournamentStandingView> standings,
            IList<RewardLedgerRecord> granted, int alreadyGranted, IList<TournamentWithheldPrize> withheld)
        {
            FirstClose = firstClose;
            Standings = new ReadOnlyCollection<TournamentStandingView>(standings);
            Granted = new ReadOnlyCollection<RewardLedgerRecord>(granted);
            AlreadyGranted = alreadyGranted;
            Withheld = new ReadOnlyCollection<TournamentWithheldPrize>(withheld);
        }
    }

    public sealed class TournamentService
    {
        private sealed class Entry
        {
            public string PlayerId;
            public int Score;
            public DateTime CompletionUtc;
            public string RunId;
        }

        private readonly ModeMatcher _matcher;
        private readonly Dictionary<string, string> _handles = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<string, List<DateTime>> _attempts = new Dictionary<string, List<DateTime>>(StringComparer.Ordinal);
        private readonly Dictionary<string, AttemptTicket> _openTickets = new Dictionary<string, AttemptTicket>(StringComparer.Ordinal);
        private readonly HashSet<string> _usedNonces = new HashSet<string>(StringComparer.Ordinal);
        private readonly Dictionary<string, Entry> _best = new Dictionary<string, Entry>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _runOwners = new Dictionary<string, string>(StringComparer.Ordinal);
        private List<Entry> _frozen;

        public string GameId { get; }
        public TournamentTemplate Template { get; }
        public bool IsClosed => _frozen != null;

        public TournamentService(string gameId, TournamentTemplate template, ModeMatcher matcher)
        {
            if (!GameProfileCatalog.IsKnownId(gameId)) throw new ArgumentException("Unknown game ID.", nameof(gameId));
            if (StringComparer.Ordinal.Equals(gameId, "Wonder_Pop"))
                throw new InvalidOperationException("WonderPopTournamentProhibited");
            GameId = gameId;
            Template = template ?? throw new ArgumentNullException(nameof(template));
            _matcher = matcher ?? throw new ArgumentNullException(nameof(matcher));
        }

        /// <summary>Flag + per-player eligibility; shared by every step.</summary>
        public bool IsEligible(TournamentEligibility e, FeatureFlagRecord tournamentFlag, FeatureFlagPolicy flagPolicy,
            out string reason)
        {
            if (e == null) { reason = "EligibilityMissing"; return false; }
            if (tournamentFlag.Feature != LiveOpsFeature.TournamentMode) { reason = "WrongFeatureFlag"; return false; }
            if (!FeatureFlagGate.CanEnableFlag(GameId, tournamentFlag, flagPolicy, out reason)) return false;
            var social = ChildAccountGate.CanUseSocialFeatures(GameId, e.Profile, e.SocialPolicy);
            if (!social.eligible) { reason = social.reason; return false; }
            if (!StringComparer.Ordinal.Equals(e.Consent.GameId, GameId)) { reason = "ConsentGameMismatch"; return false; }
            if (e.Consent.IsMinor) { reason = "MinorProhibited"; return false; }
            if (e.Consent.IsRestrictedRegion) { reason = "RestrictedRegionBlocked"; return false; }
            if (!e.Consent.IsConsentActive) { reason = "ConsentRequired"; return false; }
            if (e.RegionCode == null || !Template.AllowedRegions.Contains(e.RegionCode)) { reason = "RegionNotAllowed"; return false; }
            reason = string.Empty;
            return true;
        }

        public bool TryAdmit(string playerId, TournamentEligibility eligibility, bool leaderboardOptIn,
            string displayHandle, FeatureFlagRecord tournamentFlag, FeatureFlagPolicy flagPolicy,
            DateTime serverNowUtc, out string reason)
        {
            if (!IsPlayerId(playerId)) { reason = "InvalidPlayerId"; return false; }
            if (!IsEligible(eligibility, tournamentFlag, flagPolicy, out reason)) return false;
            if (!leaderboardOptIn) { reason = "OptInRequired"; return false; }
            if (serverNowUtc.Kind != DateTimeKind.Utc) { reason = "NonUtcInstant"; return false; }
            if (_frozen != null || serverNowUtc >= Template.EndUtc) { reason = "Closed"; return false; }
            if (serverNowUtc < Template.StartUtc) { reason = "NotOpen"; return false; }
            var handleOk = displayHandle != null && displayHandle.Length >= 1 && displayHandle.Length <= 24 &&
                           displayHandle.All(c => c > ' ' && c <= '~') &&
                           ChildAccountGate.CanUsePublicHandle(GameId, eligibility.Profile, eligibility.SocialPolicy).eligible;
            _handles[playerId] = handleOk ? displayHandle : null;
            reason = string.Empty;
            return true;
        }

        /// <summary>Issues one single-use attempt ticket (rate limited: count per player and min interval).</summary>
        public AttemptTicket IssueAttempt(string playerId, TournamentEligibility eligibility,
            FeatureFlagRecord tournamentFlag, FeatureFlagPolicy flagPolicy, DateTime serverNowUtc, out string reason)
        {
            if (serverNowUtc.Kind != DateTimeKind.Utc) { reason = "NonUtcInstant"; return null; }
            if (playerId == null || !_handles.ContainsKey(playerId)) { reason = "NotAdmitted"; return null; }
            if (!IsEligible(eligibility, tournamentFlag, flagPolicy, out reason)) return null;
            if (_frozen != null || serverNowUtc >= Template.EndUtc) { reason = "Closed"; return null; }
            if (serverNowUtc < Template.StartUtc) { reason = "NotOpen"; return null; }
            if (!_attempts.TryGetValue(playerId, out var attempts)) _attempts[playerId] = attempts = new List<DateTime>();
            if (attempts.Count >= Template.MaxAttemptsPerPlayer ||
                attempts.Count > 0 && serverNowUtc - attempts[attempts.Count - 1] < Template.MinAttemptInterval)
            {
                reason = "RateLimited";
                return null;
            }
            attempts.Add(serverNowUtc);
            var bytes = new byte[20];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(bytes);
            var nonce = string.Concat(bytes.Take(16).Select(b => b.ToString("x2")));
            var seed = Template.PerAttemptSeed ? Math.Max(1u, BitConverter.ToUInt32(bytes, 16)) : Template.SharedSeed;
            var ticket = new AttemptTicket(Template.TournamentId, playerId, attempts.Count, serverNowUtc, nonce, seed);
            _openTickets[nonce] = ticket;
            reason = string.Empty;
            return ticket;
        }

        /// <param name="receiptUtc">Trusted server receipt time; it is the verified completion instant.</param>
        public TournamentSubmitStatus Submit(AttemptTicket ticket, long startMs, IReadOnlyList<ModeInput> inputs,
            int claimedScore, DateTime receiptUtc, TournamentEligibility eligibility,
            FeatureFlagRecord tournamentFlag, FeatureFlagPolicy flagPolicy)
        {
            if (receiptUtc.Kind != DateTimeKind.Utc) throw new ArgumentException("UTC required.", nameof(receiptUtc));
            if (_frozen != null || receiptUtc >= Template.EndUtc) return TournamentSubmitStatus.Closed;
            if (receiptUtc < Template.StartUtc) return TournamentSubmitStatus.NotOpen;
            if (ticket == null || ticket.Nonce == null) return TournamentSubmitStatus.UnknownTicket;
            if (_usedNonces.Contains(ticket.Nonce)) return TournamentSubmitStatus.TicketUsed;
            // The ticket must be exactly the one this server issued (no field may be edited client-side).
            if (!_openTickets.TryGetValue(ticket.Nonce, out var issued) ||
                issued.PlayerId != ticket.PlayerId || issued.TournamentId != ticket.TournamentId ||
                issued.AttemptNo != ticket.AttemptNo || issued.ServerIssuedUtc != ticket.ServerIssuedUtc ||
                issued.Seed != ticket.Seed || receiptUtc < issued.ServerIssuedUtc)
                return TournamentSubmitStatus.UnknownTicket;
            if (!IsEligible(eligibility, tournamentFlag, flagPolicy, out _)) return TournamentSubmitStatus.NotEligible;

            // Consumed exactly once from here on, whatever the verdict.
            _openTickets.Remove(issued.Nonce);
            _usedNonces.Add(issued.Nonce);

            if (inputs != null && inputs.Count(i => i.Kind == ModeInputKind.Pause ||
                    i.Kind == ModeInputKind.Background || i.Kind == ModeInputKind.Reconnect) > Template.MaxInterruptionsPerRun)
                return TournamentSubmitStatus.TooManyInterruptions;

            ModeSession run;
            try
            {
                run = ModeSession.Replay(GameId, Template.Mode, issued.Seed, startMs, inputs, _matcher);
            }
            catch (Exception e) when (e is FormatException || e is ArgumentException || e is InvalidOperationException)
            {
                return TournamentSubmitStatus.InvalidLog;
            }
            if (run.Phase != ModePhase.Ended) return TournamentSubmitStatus.Incomplete;
            if (run.Score != claimedScore) return TournamentSubmitStatus.TamperedScore;
            var wallMs = (receiptUtc - issued.ServerIssuedUtc).Ticks / TimeSpan.TicksPerMillisecond;
            if (wallMs < run.ElapsedMs - Template.ClockToleranceMs) return TournamentSubmitStatus.TooFast;

            var runId = run.RunId;
            if (_runOwners.TryGetValue(runId, out var owner))
                return owner == issued.PlayerId ? TournamentSubmitStatus.DuplicateRun : TournamentSubmitStatus.Copied;
            _runOwners[runId] = issued.PlayerId;

            // Later submissions are always later in time, so only a strictly higher score replaces the best.
            if (!_best.TryGetValue(issued.PlayerId, out var best) || run.Score > best.Score)
                _best[issued.PlayerId] = new Entry
                    { PlayerId = issued.PlayerId, Score = run.Score, CompletionUtc = receiptUtc, RunId = runId };
            return TournamentSubmitStatus.Accepted;
        }

        public IReadOnlyList<TournamentStandingView> Leaderboard() =>
            Views(_frozen ?? Ranked()).AsReadOnly();

        /// <summary>Null before EndUtc. Freezes standings on the first call; every call (re)issues prizes through
        /// each winner's ledger, which applies every prize key at most once (restart/restore safe).</summary>
        public TournamentCloseResult Close(TrustedUtcAnchor anchor, FeatureFlagRecord tournamentFlag,
            FeatureFlagPolicy flagPolicy, Func<string, TournamentEligibility> eligibilityOf,
            Func<string, RewardLedger> ledgerOf)
        {
            if (!anchor.IsServerAnchored || anchor.UtcNow.Kind != DateTimeKind.Utc)
                throw new ArgumentException(RewardLedgerReasons.TrustedTimeAnchorRequired, nameof(anchor));
            if (eligibilityOf == null) throw new ArgumentNullException(nameof(eligibilityOf));
            if (ledgerOf == null) throw new ArgumentNullException(nameof(ledgerOf));
            if (anchor.UtcNow < Template.EndUtc) return null;
            var first = _frozen == null;
            if (first) _frozen = Ranked();

            var granted = new List<RewardLedgerRecord>();
            var withheld = new List<TournamentWithheldPrize>();
            var already = 0;
            for (var i = 0; i < _frozen.Count; i++)
            {
                var rank = i + 1;
                var tier = Template.PrizeTiers.FirstOrDefault(t => rank <= t.MaxRank);
                if (tier == null) break;
                var entry = _frozen[i];
                if (!IsEligible(eligibilityOf(entry.PlayerId), tournamentFlag, flagPolicy, out var why))
                {
                    withheld.Add(new TournamentWithheldPrize(rank, why));
                    continue;
                }
                var ledger = ledgerOf(entry.PlayerId);
                if (ledger == null || ledger.GameId != GameId || ledger.PlayerId != entry.PlayerId)
                {
                    withheld.Add(new TournamentWithheldPrize(rank, RewardLedgerReasons.LedgerOwnerMismatch));
                    continue;
                }
                // Deterministic payload (GrantedUtc = EndUtc, Reference = run) so a re-issue is a ledger Duplicate.
                var result = ledger.TryGrant(new RewardLedgerRequest(GameId, entry.PlayerId, RewardSources.Tournament,
                    PrizeInstanceId(Template.TournamentId), new Dictionary<string, long> { [Template.PrizeAssetId] = tier.Amount },
                    Template.EndUtc, Template.EndUtc + Template.PrizeClaimWindow, entry.RunId), anchor);
                if (result.Outcome == RewardLedgerOutcome.Applied) granted.Add(result.Record);
                else if (result.Outcome == RewardLedgerOutcome.Duplicate ||
                         result.Reason == RewardLedgerReasons.KeySettled) already++;
                else withheld.Add(new TournamentWithheldPrize(rank, result.Reason));
            }
            return new TournamentCloseResult(first, Views(_frozen), granted, already, withheld);
        }

        public static string PrizeInstanceId(string tournamentId) => tournamentId + "|prize";

        private List<Entry> Ranked() =>
            _best.Values
                .OrderByDescending(s => s.Score)
                .ThenBy(s => s.CompletionUtc)
                .ThenBy(s => s.RunId, StringComparer.Ordinal)
                .ToList();

        private List<TournamentStandingView> Views(List<Entry> ranked) =>
            ranked.Select((e, i) => new TournamentStandingView(i + 1,
                _handles.TryGetValue(e.PlayerId, out var h) ? h : null, e.Score, e.CompletionUtc, e.RunId)).ToList();

        private static bool IsPlayerId(string s) =>
            s != null && s.Length >= 1 && s.Length <= 128 && s.All(c => c > ' ' && c <= '~' && c != '|');
    }
}
