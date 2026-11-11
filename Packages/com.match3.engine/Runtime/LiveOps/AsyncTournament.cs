using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Match3.Engine.Runtime.Identity;
using Match3.Engine.Runtime.Privacy;

namespace Match3.Engine.Runtime.LiveOps
{
    // ENG-M20-015 optional asynchronous tournament (engine data model only; no networking, no UI).
    //
    // - Non-wagering: there is no entry price/fee field anywhere; prizes are in-game reward IDs emitted as
    //   grant requests for the ENG-M16-011 ledger. DEFAULT OFF: admission needs an enabled TournamentMode
    //   flag through FeatureFlagGate (kill switch, consent check, Wonder_Pop hard block).
    // - Admission: never Wonder_Pop; ChildAccountGate.CanUseSocialFeatures (no child / unverified minor /
    //   local-only profile, social policy must allow it); explicit consent granted, not a minor, not a
    //   restricted region; region allow-list; explicit leaderboard opt-in. Admitted == opted in.
    // - Every player plays the same server seed. A submission is the input log only: the score is
    //   recomputed by ModeSession.Replay; a claimed score that differs is rejected as tampered.
    // - Ranking: score desc, then earliest verified (server-receipt) completion UTC, then run ID ordinal.
    // - Window [StartUtc, EndUtc): nothing is accepted at or after EndUtc; Close only at/after EndUtc and
    //   emits each prize key exactly once. Rate limit: attempts per player and min interval; a run log may
    //   hold at most MaxInterruptionsPerRun pause/background/reconnect inputs (a frozen timer is thinking time).
    //
    // ponytail: in-memory, single tournament instance; persistence and the backend leaderboard come with
    // ENG-M21-007/011; mapping prize grants onto the ENG-M16-011 RewardLedger (source "tournament") is a follow-up.
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
        public uint Seed { get; }
        public DateTime StartUtc { get; }
        public DateTime EndUtc { get; }
        public IReadOnlyCollection<string> AllowedRegions { get; }
        public int MaxSubmissionsPerPlayer { get; }
        public TimeSpan MinSubmissionInterval { get; }
        /// <summary>Pause/Background/Reconnect inputs allowed per run: a frozen timer is free thinking
        /// time, so a tournament log may contain only a few of them.</summary>
        public int MaxInterruptionsPerRun { get; }
        public string PrizeRewardId { get; }
        public IReadOnlyList<TournamentPrizeTier> PrizeTiers { get; }

        public TournamentTemplate(string tournamentId, ModeTemplate mode, uint seed, DateTime startUtc,
            DateTime endUtc, IEnumerable<string> allowedRegions, int maxSubmissionsPerPlayer,
            TimeSpan minSubmissionInterval, int maxInterruptionsPerRun, string prizeRewardId,
            IEnumerable<TournamentPrizeTier> prizeTiers)
        {
            if (!ContentMetadataReceiver.IsValidId(tournamentId))
                throw new ArgumentException("Invalid tournament ID.", nameof(tournamentId));
            Mode = mode ?? throw new ArgumentNullException(nameof(mode));
            if (mode.Kind == GameModeKind.Classic)
                throw new ArgumentException("Tournaments run Time Attack or Endless templates.", nameof(mode));
            if (seed == 0) throw new ArgumentOutOfRangeException(nameof(seed));
            if (startUtc.Kind != DateTimeKind.Utc || endUtc.Kind != DateTimeKind.Utc || endUtc <= startUtc ||
                endUtc - startUtc > TimeSpan.FromDays(31))
                throw new ArgumentException("Window must be UTC, non-empty and at most 31 days.");
            var regions = (allowedRegions ?? Enumerable.Empty<string>()).ToList();
            if (regions.Count == 0 || regions.Any(r => r == null || r.Length != 2 || !r.All(c => c >= 'A' && c <= 'Z')))
                throw new ArgumentException("Allowed regions must be ISO 3166-1 alpha-2 codes.", nameof(allowedRegions));
            if (maxSubmissionsPerPlayer < 1 || maxSubmissionsPerPlayer > 50)
                throw new ArgumentOutOfRangeException(nameof(maxSubmissionsPerPlayer));
            if (minSubmissionInterval < TimeSpan.Zero || minSubmissionInterval > TimeSpan.FromDays(1))
                throw new ArgumentOutOfRangeException(nameof(minSubmissionInterval));
            if (maxInterruptionsPerRun < 0 || maxInterruptionsPerRun > 20)
                throw new ArgumentOutOfRangeException(nameof(maxInterruptionsPerRun));
            if (!ContentMetadataReceiver.IsValidId(prizeRewardId))
                throw new ArgumentException("Invalid prize reward ID.", nameof(prizeRewardId));
            var tiers = (prizeTiers ?? Enumerable.Empty<TournamentPrizeTier>()).ToList();
            if (tiers.Count == 0 || tiers.Count > 10 || tiers.Any(t => t == null) ||
                tiers.Zip(tiers.Skip(1), (a, b) => a.MaxRank < b.MaxRank && a.Amount >= b.Amount).Any(ok => !ok))
                throw new ArgumentException("Prize tiers: ranks strictly increase, amounts never increase.", nameof(prizeTiers));

            TournamentId = tournamentId;
            Seed = seed;
            StartUtc = startUtc;
            EndUtc = endUtc;
            AllowedRegions = new ReadOnlyCollection<string>(regions.Distinct(StringComparer.Ordinal).ToList());
            MaxSubmissionsPerPlayer = maxSubmissionsPerPlayer;
            MinSubmissionInterval = minSubmissionInterval;
            MaxInterruptionsPerRun = maxInterruptionsPerRun;
            PrizeRewardId = prizeRewardId;
            PrizeTiers = tiers.AsReadOnly();
        }
    }

    public sealed class TournamentStanding
    {
        public int Rank { get; internal set; }
        public string PlayerId { get; }
        public int Score { get; }
        public DateTime VerifiedCompletionUtc { get; }
        public string RunId { get; }

        internal TournamentStanding(string playerId, int score, DateTime completionUtc, string runId)
        {
            PlayerId = playerId;
            Score = score;
            VerifiedCompletionUtc = completionUtc;
            RunId = runId;
        }
    }

    public sealed class TournamentPrizeGrant
    {
        public string IdempotencyKey { get; }
        public string PlayerId { get; }
        public int Rank { get; }
        public string RewardId { get; }
        public int Amount { get; }

        internal TournamentPrizeGrant(string gameId, string tournamentId, string playerId, int rank,
            string rewardId, int amount)
        {
            IdempotencyKey = "tournament|" + gameId + "|" + tournamentId + "|" + playerId + "|prize";
            PlayerId = playerId;
            Rank = rank;
            RewardId = rewardId;
            Amount = amount;
        }
    }

    public enum TournamentSubmitStatus
    {
        Accepted, NotAdmitted, NotOpen, Closed, RateLimited, InvalidLog, Incomplete, TamperedScore, DuplicateRun,
        TooManyInterruptions
    }

    public sealed class TournamentCloseResult
    {
        public bool FirstClose { get; }
        public IReadOnlyList<TournamentStanding> Standings { get; }
        /// <summary>Non-empty only on the first close: every prize key is emitted exactly once.</summary>
        public IReadOnlyList<TournamentPrizeGrant> Grants { get; }

        internal TournamentCloseResult(bool firstClose, IList<TournamentStanding> standings,
            IList<TournamentPrizeGrant> grants)
        {
            FirstClose = firstClose;
            Standings = new ReadOnlyCollection<TournamentStanding>(standings);
            Grants = new ReadOnlyCollection<TournamentPrizeGrant>(grants);
        }
    }

    public sealed class TournamentService
    {
        private readonly Func<IReadOnlyDictionary<BoardPosition, string>, IReadOnlyList<MatchRunProjection>> _sdkMatchRuns;
        private readonly HashSet<string> _admitted = new HashSet<string>(StringComparer.Ordinal);
        private readonly Dictionary<string, List<DateTime>> _attempts = new Dictionary<string, List<DateTime>>(StringComparer.Ordinal);
        private readonly Dictionary<string, TournamentStanding> _best = new Dictionary<string, TournamentStanding>(StringComparer.Ordinal);
        private readonly HashSet<string> _runIds = new HashSet<string>(StringComparer.Ordinal);
        private TournamentCloseResult _closed;

        public string GameId { get; }
        public TournamentTemplate Template { get; }
        public bool IsClosed => _closed != null;

        public TournamentService(string gameId, TournamentTemplate template,
            Func<IReadOnlyDictionary<BoardPosition, string>, IReadOnlyList<MatchRunProjection>> sdkMatchRuns)
        {
            if (!GameProfileCatalog.IsKnownId(gameId)) throw new ArgumentException("Unknown game ID.", nameof(gameId));
            if (StringComparer.Ordinal.Equals(gameId, "Wonder_Pop"))
                throw new InvalidOperationException("WonderPopTournamentProhibited");
            GameId = gameId;
            Template = template ?? throw new ArgumentNullException(nameof(template));
            _sdkMatchRuns = sdkMatchRuns ?? throw new ArgumentNullException(nameof(sdkMatchRuns));
        }

        public bool TryAdmit(string playerId, ChildProfile profile, ConsentRecord consent, string regionCode,
            bool leaderboardOptIn, FeatureFlagRecord tournamentFlag, FeatureFlagPolicy flagPolicy,
            ChildAccountPolicy socialPolicy, DateTime serverNowUtc, out string reason)
        {
            if (!IsPlayerId(playerId)) { reason = "InvalidPlayerId"; return false; }
            if (tournamentFlag.Feature != LiveOpsFeature.TournamentMode) { reason = "WrongFeatureFlag"; return false; }
            if (!FeatureFlagGate.CanEnableFlag(GameId, tournamentFlag, flagPolicy, out reason)) return false;
            var social = ChildAccountGate.CanUseSocialFeatures(GameId, profile, socialPolicy);
            if (!social.eligible) { reason = social.reason; return false; }
            if (!StringComparer.Ordinal.Equals(consent.GameId, GameId)) { reason = "ConsentGameMismatch"; return false; }
            if (consent.IsMinor) { reason = "MinorProhibited"; return false; }
            if (consent.IsRestrictedRegion) { reason = "RestrictedRegionBlocked"; return false; }
            if (!consent.IsConsentActive) { reason = "ConsentRequired"; return false; }
            if (regionCode == null || !Template.AllowedRegions.Contains(regionCode)) { reason = "RegionNotAllowed"; return false; }
            if (!leaderboardOptIn) { reason = "OptInRequired"; return false; }
            if (serverNowUtc.Kind != DateTimeKind.Utc) { reason = "NonUtcInstant"; return false; }
            if (_closed != null || serverNowUtc >= Template.EndUtc) { reason = "Closed"; return false; }
            if (serverNowUtc < Template.StartUtc) { reason = "NotOpen"; return false; }
            _admitted.Add(playerId);
            reason = string.Empty;
            return true;
        }

        /// <param name="serverNowUtc">Trusted server receipt time; it is the verified completion instant.</param>
        public TournamentSubmitStatus Submit(string playerId, long startMs, IReadOnlyList<ModeInput> inputs,
            int claimedScore, DateTime serverNowUtc)
        {
            if (serverNowUtc.Kind != DateTimeKind.Utc) throw new ArgumentException("UTC required.", nameof(serverNowUtc));
            if (playerId == null || !_admitted.Contains(playerId)) return TournamentSubmitStatus.NotAdmitted;
            if (_closed != null || serverNowUtc >= Template.EndUtc) return TournamentSubmitStatus.Closed;
            if (serverNowUtc < Template.StartUtc) return TournamentSubmitStatus.NotOpen;

            // Every attempt counts, including rejected ones, so tampering cannot be retried for free.
            if (!_attempts.TryGetValue(playerId, out var attempts)) _attempts[playerId] = attempts = new List<DateTime>();
            if (attempts.Count >= Template.MaxSubmissionsPerPlayer ||
                attempts.Count > 0 && serverNowUtc - attempts[attempts.Count - 1] < Template.MinSubmissionInterval)
                return TournamentSubmitStatus.RateLimited;
            attempts.Add(serverNowUtc);

            if (inputs != null && inputs.Count(i => i.Kind == ModeInputKind.Pause ||
                    i.Kind == ModeInputKind.Background || i.Kind == ModeInputKind.Reconnect) > Template.MaxInterruptionsPerRun)
                return TournamentSubmitStatus.TooManyInterruptions;

            ModeSession run;
            try
            {
                run = ModeSession.Replay(GameId, Template.Mode, Template.Seed, startMs, inputs, _sdkMatchRuns);
            }
            catch (Exception e) when (e is FormatException || e is ArgumentException || e is InvalidOperationException)
            {
                return TournamentSubmitStatus.InvalidLog;
            }
            if (run.Phase != ModePhase.Ended) return TournamentSubmitStatus.Incomplete;
            if (run.Score != claimedScore) return TournamentSubmitStatus.TamperedScore;
            var runId = run.RunId;
            if (!_runIds.Add(runId)) return TournamentSubmitStatus.DuplicateRun;

            // Later submissions are always later in time, so only a strictly higher score replaces the best.
            if (!_best.TryGetValue(playerId, out var best) || run.Score > best.Score)
                _best[playerId] = new TournamentStanding(playerId, run.Score, serverNowUtc, runId);
            return TournamentSubmitStatus.Accepted;
        }

        public IReadOnlyList<TournamentStanding> Leaderboard()
        {
            var ordered = _best.Values
                .OrderByDescending(s => s.Score)
                .ThenBy(s => s.VerifiedCompletionUtc)
                .ThenBy(s => s.RunId, StringComparer.Ordinal)
                .ToList();
            for (var i = 0; i < ordered.Count; i++) ordered[i].Rank = i + 1;
            return ordered.AsReadOnly();
        }

        /// <returns>Null before EndUtc. The first call freezes standings and emits prize grants once.</returns>
        public TournamentCloseResult Close(DateTime serverNowUtc)
        {
            if (serverNowUtc.Kind != DateTimeKind.Utc) throw new ArgumentException("UTC required.", nameof(serverNowUtc));
            if (_closed != null)
                return new TournamentCloseResult(false, _closed.Standings.ToList(), new List<TournamentPrizeGrant>());
            if (serverNowUtc < Template.EndUtc) return null;
            var standings = Leaderboard().ToList();
            var grants = new List<TournamentPrizeGrant>();
            foreach (var s in standings)
            {
                var tier = Template.PrizeTiers.FirstOrDefault(t => s.Rank <= t.MaxRank);
                if (tier == null) break;
                grants.Add(new TournamentPrizeGrant(GameId, Template.TournamentId, s.PlayerId, s.Rank,
                    Template.PrizeRewardId, tier.Amount));
            }
            _closed = new TournamentCloseResult(true, standings, grants);
            return _closed;
        }

        private static bool IsPlayerId(string s) =>
            s != null && s.Length >= 1 && s.Length <= 128 && s.All(c => c > ' ' && c <= '~' && c != '|');
    }
}
