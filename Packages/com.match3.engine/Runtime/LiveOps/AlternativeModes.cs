using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text;
using Match3.Engine.Runtime.Economy;

namespace Match3.Engine.Runtime.LiveOps
{
    // ENG-M20-015: optional alternative modes (Time Attack, Endless, Challenge) on the existing engine board pipeline.
    //
    // - Templates are declarative JSON (schema "alt-mode/1"), strictly validated, content-hashed with
    //   CanonicalJson. Unknown keys are rejected, so no price/entry-fee/IAP field can ride along. Reward assets
    //   must be registered, non-premium ledger assets (CurrencyType names except PremiumGems).
    // - ModeSession composes the EXISTING engine pieces; it adds no match rules of its own:
    //   SeededRandom, DeterministicBoardPlanning.TrySpawn/TryShuffle, GravityPlanner.PlanCardinal,
    //   WeightedRefillPlanner.Plan, CanonicalMoveCandidates.Find and DeterministicScorePlanner. Match detection is a
    //   ModeMatcher the GAME must inject: the pinned LibraStack SDK solver plus its pin ID (MatcherId). There is
    //   deliberately no engine default matcher. MatcherId is part of TemplateHash, StateHash and RunId, so a run
    //   verified with a different matcher can never be confused with the original.
    // - Time is an injected monotonic millisecond value carried on every input (no wall clock). A run is a pure
    //   function of (gameId, template, matcher, seed, startMs, input log). Replay() rebuilds it exactly; it is the
    //   save/restore path and the tournament verifier and intentionally BYPASSES AlternativeModeGate (verification
    //   must work even after a kill switch; only Start() opens new runs).
    // - Pause freezes the timer. Background keeps charging up to BackgroundGraceMs, then auto-pauses at
    //   exactly that point; an explicit Resume is always required after an auto-pause. Reconnect (new
    //   process / new monotonic base) charges whatever is left of the grace as if the app never returned,
    //   then auto-pauses: it can never give time back. Swaps closer than MinSwapIntervalMs (monotonic) are
    //   protocol violations, so a log cannot compress many moves into no time.
    // - RunId is the canonical run identity: game, template content hash, matcher, seed and the ordered APPLIED
    //   swaps only (no timestamps, ticks, pauses or ignored inputs). Shifting a log by a tick does not change it.
    // - Records and rewards are separate per mode. Rewards go through the ENG-M16-011 RewardLedger (source
    //   "alt_mode", strict TryGrant with a server TrustedUtcAnchor); the per-day cap lives in ledger slots, so a
    //   restart cannot reset it.
    //
    // ponytail: Classic levels keep their goal-based runner; Classic here is only a move-limited score run so
    // records/caps stay keyed per mode. No special pieces are spawned in alternative modes.
    public enum GameModeKind { Classic = 0, TimeAttack = 1, Endless = 2, Challenge = 3 }

    public sealed class ModeRewardTier
    {
        public int Score { get; }
        public int Amount { get; }

        internal ModeRewardTier(int score, int amount)
        {
            Score = score;
            Amount = amount;
        }
    }

    public sealed class ModeTemplate
    {
        public const string Schema = "alt-mode/1";
        public const int DefaultMinSwapIntervalMs = 150;
        public const int DefaultMaxPauseBudgetMs = 120000;

        private static readonly string[] Keys =
        {
            "schema", "templateId", "version", "kind", "rows", "columns", "palette", "startColors",
            "rampEveryTurns", "timeLimitMs", "moveLimit", "shuffleLives", "backgroundGraceMs", "pointsPerCell",
            "maxTurns", "targetScore", "rewardId", "rewardTiers", "perRunRewardCap", "perDayRewardCap"
        };

        private static readonly string[] OptionalKeys = { "minSwapIntervalMs", "maxPauseBudgetMs" };

        public string TemplateId { get; }
        public int Version { get; }
        public GameModeKind Kind { get; }
        public int Rows { get; }
        public int Columns { get; }
        public IReadOnlyList<string> Palette { get; }
        public int StartColors { get; }
        public int RampEveryTurns { get; }
        public long TimeLimitMs { get; }
        public int MoveLimit { get; }
        public int ShuffleLives { get; }
        public long BackgroundGraceMs { get; }
        public int PointsPerCell { get; }
        public int MaxTurns { get; }
        public int TargetScore { get; }
        public int MinSwapIntervalMs { get; }
        /// <summary>Most frozen (paused / auto-paused) monotonic ms a verified tournament run may declare.</summary>
        public int MaxPauseBudgetMs { get; }
        /// <summary>Ledger asset name (CurrencyType, never PremiumGems).</summary>
        public string RewardId { get; }
        public IReadOnlyList<ModeRewardTier> RewardTiers { get; }
        public int PerRunRewardCap { get; }
        public int PerDayRewardCap { get; }
        public string ContentHash { get; }

        private ModeTemplate(Dictionary<string, object> d)
        {
            foreach (var key in d.Keys)
                if (Array.IndexOf(Keys, key) < 0 && Array.IndexOf(OptionalKeys, key) < 0)
                    throw new FormatException("Unknown template key: " + key);
            foreach (var key in Keys)
                if (!d.ContainsKey(key)) throw new FormatException("Missing template key: " + key);
            if (!(d["schema"] is string schema) || schema != Schema) throw new FormatException("Unsupported schema.");

            TemplateId = Id(d, "templateId");
            Version = Int(d, "version", 1, 1000000);
            switch (d["kind"] as string)
            {
                case "classic": Kind = GameModeKind.Classic; break;
                case "timeAttack": Kind = GameModeKind.TimeAttack; break;
                case "endless": Kind = GameModeKind.Endless; break;
                case "challenge": Kind = GameModeKind.Challenge; break;
                default: throw new FormatException("Unknown mode kind.");
            }
            Rows = Int(d, "rows", 3, 16);
            Columns = Int(d, "columns", 3, 16);
            if (!(d["palette"] is List<object> palette) || palette.Count < 2 || palette.Count > 8)
                throw new FormatException("Palette must list 2..8 content IDs.");
            var colors = palette.Select(x => x as string).ToArray();
            if (colors.Any(x => string.IsNullOrWhiteSpace(x) || x.Length > 16 || x.IndexOfAny(new[] { '|', ',', '/' }) >= 0) ||
                colors.Distinct(StringComparer.Ordinal).Count() != colors.Length)
                throw new FormatException("Palette IDs must be distinct short tokens.");
            Palette = Array.AsReadOnly(colors);
            StartColors = Int(d, "startColors", 2, colors.Length);
            RampEveryTurns = Int(d, "rampEveryTurns", 0, 10000);
            TimeLimitMs = Int(d, "timeLimitMs", 0, 3600000);
            MoveLimit = Int(d, "moveLimit", 0, 1000);
            ShuffleLives = Int(d, "shuffleLives", 0, 100);
            BackgroundGraceMs = Int(d, "backgroundGraceMs", 0, 60000);
            PointsPerCell = Int(d, "pointsPerCell", 1, 1000);
            MaxTurns = Int(d, "maxTurns", 1, ReplayTrace.MaxSteps);
            TargetScore = Int(d, "targetScore", 0, 100000000);
            MinSwapIntervalMs = d.ContainsKey("minSwapIntervalMs")
                ? Int(d, "minSwapIntervalMs", 0, 5000)
                : DefaultMinSwapIntervalMs;
            MaxPauseBudgetMs = d.ContainsKey("maxPauseBudgetMs")
                ? Int(d, "maxPauseBudgetMs", 0, 3600000)
                : DefaultMaxPauseBudgetMs;

            // Mode-specific rules: each mode is distinct, not a relabelled Classic level.
            if (Kind == GameModeKind.Classic && (MoveLimit < 1 || TimeLimitMs != 0 || RampEveryTurns != 0))
                throw new FormatException("Classic needs a move limit and no timer/ramp.");
            if (Kind == GameModeKind.TimeAttack && (TimeLimitMs < 1000 || RampEveryTurns != 0))
                throw new FormatException("Time Attack needs a 1s..1h countdown and no ramp.");
            if (Kind == GameModeKind.Endless && (TimeLimitMs != 0 || MoveLimit != 0 || TargetScore != 0))
                throw new FormatException("Endless has no timer, move limit or target.");
            if (Kind == GameModeKind.Challenge &&
                (MoveLimit < 1 || TargetScore < 1 || TimeLimitMs != 0 || RampEveryTurns != 0))
                throw new FormatException("Challenge needs a target score within a move limit and no timer/ramp.");
            if (RampEveryTurns == 0 && StartColors != colors.Length && Kind == GameModeKind.Endless)
                throw new FormatException("Endless without a ramp must start with the whole palette.");

            RewardId = d["rewardId"] as string;
            if (!RewardLedger.IsKnownAsset(RewardId) || RewardId == RewardLedger.PaidAssetId)
                throw new FormatException("rewardId must be a registered non-premium ledger asset.");
            if (!(d["rewardTiers"] is List<object> tiers) || tiers.Count < 1 || tiers.Count > 10)
                throw new FormatException("1..10 reward tiers required.");
            var parsed = new List<ModeRewardTier>();
            foreach (var t in tiers)
            {
                if (!(t is Dictionary<string, object> tier) || tier.Count != 2)
                    throw new FormatException("Reward tier must be {score, amount}.");
                var next = new ModeRewardTier(Int(tier, "score", 1, 100000000), Int(tier, "amount", 1, 100000));
                if (parsed.Count > 0 && next.Score <= parsed[parsed.Count - 1].Score)
                    throw new FormatException("Reward tier scores must strictly increase.");
                parsed.Add(next);
            }
            RewardTiers = parsed.AsReadOnly();
            PerRunRewardCap = Int(d, "perRunRewardCap", 1, 100000);
            PerDayRewardCap = Int(d, "perDayRewardCap", PerRunRewardCap, 1000000);
            ContentHash = CanonicalJson.Sha256Hex(d);
        }

        public static ModeTemplate FromJson(string json)
        {
            if (!(CanonicalJson.Parse(json) is Dictionary<string, object> d))
                throw new FormatException("Template must be a JSON object.");
            return new ModeTemplate(d);
        }

        private static int Int(Dictionary<string, object> d, string key, int min, int max)
        {
            if (!d.TryGetValue(key, out var v) || !(v is long l) || l < min || l > max)
                throw new FormatException("Template field out of range: " + key);
            return (int)l;
        }

        private static string Id(Dictionary<string, object> d, string key)
        {
            if (!(d[key] is string s) || !ContentMetadataReceiver.IsValidId(s))
                throw new FormatException("Invalid template ID field: " + key);
            return s;
        }
    }

    /// <summary>The game-injected match authority: the pinned SDK solver and its pin ID. No engine default.</summary>
    public sealed class ModeMatcher
    {
        public string MatcherId { get; }
        public Func<IReadOnlyDictionary<BoardPosition, string>, IReadOnlyList<MatchRunProjection>> MatchRuns { get; }

        public ModeMatcher(string matcherId,
            Func<IReadOnlyDictionary<BoardPosition, string>, IReadOnlyList<MatchRunProjection>> matchRuns)
        {
            if (string.IsNullOrWhiteSpace(matcherId) || matcherId.Length > 128 || matcherId.Contains("|"))
                throw new ArgumentException("Matcher ID (SDK pin) required.", nameof(matcherId));
            MatcherId = matcherId;
            MatchRuns = matchRuns ?? throw new ArgumentNullException(nameof(matchRuns));
        }

        /// <summary>Template content hash bound to the matcher that verifies it.</summary>
        public string TemplateHash(ModeTemplate template) =>
            ReplayCodec.BoardHash("M3MODETPL1|" + (template ?? throw new ArgumentNullException(nameof(template))).ContentHash +
                                  "|" + MatcherId);
    }

    public static class AlternativeModeGate
    {
        /// <summary>
        /// Single source of truth for mode enablement. Classic is always available. Every alternative mode needs BOTH
        /// the title's GameProfile capability ("timed" for Time Attack and Challenge, "endless" for Endless; both OFF
        /// in every placeholder profile) AND the matching enabled LiveOps flag (TimedMode / EndlessMode) through
        /// FeatureFlagGate, which is the remote kill switch. LiveOpsFeature has no Challenge member yet
        /// (FeatureFlagGate.cs is outside ENG-M20-015), so Challenge rides "timed" + TimedMode.
        /// Note: GameProfile currently rejects "timed" together with "endless", so one title can offer Time Attack
        /// (and Challenge) OR Endless, not both; that rule is reported for an owner/lead decision, not changed here.
        /// </summary>
        public static bool CanStart(GameProfile profile, GameModeKind kind, FeatureFlagRecord flag,
            FeatureFlagPolicy policy, out string reason)
        {
            if (profile == null || !GameProfileCatalog.IsKnownId(profile.Id)) { reason = "UnknownGame"; return false; }
            if (kind == GameModeKind.Classic) { reason = string.Empty; return true; }
            var endless = kind == GameModeKind.Endless;
            if (!profile.IsEnabled(endless ? "endless" : "timed", EngineCompatibility.MinimumCapability))
            {
                reason = "ProfileFeatureDisabled";
                return false;
            }
            if (flag.Feature != (endless ? LiveOpsFeature.EndlessMode : LiveOpsFeature.TimedMode))
            {
                reason = "WrongFeatureFlag";
                return false;
            }
            return FeatureFlagGate.CanEnableFlag(profile.Id, flag, policy, out reason);
        }
    }

    public enum ModeInputKind { Tick = 0, Swap = 1, Pause = 2, Resume = 3, Background = 4, Foreground = 5, Reconnect = 6 }

    public readonly struct ModeInput
    {
        /// <summary>~31 years of milliseconds; keeps every elapsed sum far from overflow.</summary>
        public const long MaxMs = 1000000000000L;

        public ModeInputKind Kind { get; }
        /// <summary>Injected monotonic milliseconds (never wall-clock).</summary>
        public long AtMs { get; }
        public BoardPosition From { get; }
        public BoardPosition To { get; }

        private ModeInput(ModeInputKind kind, long atMs, BoardPosition from, BoardPosition to)
        {
            if (!Enum.IsDefined(typeof(ModeInputKind), kind)) throw new ArgumentOutOfRangeException(nameof(kind));
            if (atMs < 0 || atMs > MaxMs) throw new ArgumentOutOfRangeException(nameof(atMs));
            Kind = kind;
            AtMs = atMs;
            From = from;
            To = to;
        }

        public static ModeInput Swap(long atMs, BoardPosition from, BoardPosition to) =>
            new ModeInput(ModeInputKind.Swap, atMs, from, to);

        public static ModeInput Of(ModeInputKind kind, long atMs) =>
            kind == ModeInputKind.Swap
                ? throw new ArgumentException("Use ModeInput.Swap.", nameof(kind))
                : new ModeInput(kind, atMs, default(BoardPosition), default(BoardPosition));
    }

    public enum ModePhase { Running = 0, Paused = 1, Ended = 2 }

    public enum ModeEndReason
    {
        None = 0, TimeExpired = 1, MovesExhausted = 2, NoValidMoves = 3, TurnCap = 4,
        /// <summary>Challenge won: target score reached within the move limit.</summary>
        TargetReached = 5
    }

    public enum ModeInputStatus
    {
        Applied = 0,
        /// <summary>Geometrically legal swap the pinned SDK found no match for; nothing spent.</summary>
        NoMatch = 1,
        /// <summary>Not applicable in the current phase (e.g. swap while paused or after the end).</summary>
        Ignored = 2,
        /// <summary>Protocol violation (time moved backwards, non-adjacent or too-fast swap). Never logged.</summary>
        Rejected = 3
    }

    public sealed class ModeRunResult
    {
        public string RunId { get; }
        public string GameId { get; }
        public GameModeKind Kind { get; }
        public string TemplateId { get; }
        public string TemplateContentHash { get; }
        public string MatcherId { get; }
        /// <summary>Template content hash bound to MatcherId.</summary>
        public string TemplateHash { get; }
        public uint Seed { get; }
        public int Score { get; }
        public int Turns { get; }
        public long ElapsedMs { get; }
        /// <summary>Active ms at the last applied swap (idle time after it does not count).</summary>
        public long ElapsedAtLastSwapMs { get; }
        /// <summary>Active ms at which TargetScore was first reached, or -1.</summary>
        public long TimeToTargetMs { get; }
        public ModeEndReason EndReason { get; }

        internal ModeRunResult(ModeSession s)
        {
            RunId = s.RunId;
            GameId = s.GameId;
            Kind = s.Template.Kind;
            TemplateId = s.Template.TemplateId;
            TemplateContentHash = s.Template.ContentHash;
            MatcherId = s.Matcher.MatcherId;
            TemplateHash = s.Matcher.TemplateHash(s.Template);
            Seed = s.Seed;
            Score = s.Score;
            Turns = s.Turns;
            ElapsedMs = s.ElapsedMs;
            ElapsedAtLastSwapMs = s.ElapsedAtLastSwapMs;
            TimeToTargetMs = s.TimeToTargetMs;
            EndReason = s.EndReason;
        }
    }

    public sealed class ModeSession
    {
        private const int MaxCascadeWaves = 64;
        private const int MaxReshuffles = 8;

        private readonly SeededRandom _rng;
        private readonly BoardTopology _empty;
        private readonly List<ModeInput> _inputs = new List<ModeInput>();
        private readonly List<string> _appliedSwaps = new List<string>();
        private Dictionary<BoardPosition, string> _board;
        private long _lastMs;
        private long _backgroundSinceMs;
        private long _lastSwapAtMs;

        public string GameId { get; }
        public ModeTemplate Template { get; }
        public ModeMatcher Matcher { get; }
        public uint Seed { get; }
        public long StartMs { get; }
        public ModePhase Phase { get; private set; }
        public ModeEndReason EndReason { get; private set; }
        public bool IsBackgrounded { get; private set; }
        public bool IsAutoPaused { get; private set; }
        public int Score { get; private set; }
        public int Turns { get; private set; }
        public long ElapsedMs { get; private set; }
        public long ElapsedAtLastSwapMs { get; private set; }
        /// <summary>Monotonic ms the run declared as frozen (paused or auto-paused; not across a Reconnect, whose
        /// gap is unobservable). Derived from the log, so it is not part of StateHash.</summary>
        public long FrozenMs { get; private set; }
        public long TimeToTargetMs { get; private set; } = -1;
        public int ShuffleLivesRemaining { get; private set; }
        public int ActiveColors { get; private set; }
        public long RemainingMs => Template.TimeLimitMs > 0 ? Template.TimeLimitMs - ElapsedMs : -1;
        public int MovesRemaining => Template.MoveLimit > 0 ? Template.MoveLimit - Turns : -1;
        public IReadOnlyList<ModeInput> Inputs => _inputs.AsReadOnly();
        public IReadOnlyDictionary<BoardPosition, string> Board => new ReadOnlyDictionary<BoardPosition, string>(_board);

        private ModeSession(string gameId, ModeTemplate template, ModeMatcher matcher, uint seed, long startMs)
        {
            if (!GameProfileCatalog.IsKnownId(gameId)) throw new ArgumentException("Unknown game ID.", nameof(gameId));
            if (startMs < 0 || startMs > ModeInput.MaxMs) throw new ArgumentOutOfRangeException(nameof(startMs));
            GameId = gameId;
            Template = template ?? throw new ArgumentNullException(nameof(template));
            Matcher = matcher ?? throw new ArgumentNullException(nameof(matcher));
            _rng = new SeededRandom(seed); // rejects seed 0
            Seed = seed;
            StartMs = startMs;
            _lastMs = startMs;
            _lastSwapAtMs = startMs - template.MinSwapIntervalMs;
            _empty = new BoardTopology(template.Rows, template.Columns);
            ActiveColors = template.StartColors;
            ShuffleLivesRemaining = template.ShuffleLives;

            // Opening board: seeded spawn, settle any accidental matches unscored, free reshuffle if dead.
            var spawn = DeterministicBoardPlanning.TrySpawn(_empty, template.Palette.Take(ActiveColors).ToList(),
                _rng, _ => true, 1);
            _board = new Dictionary<BoardPosition, string>(spawn.Occupants);
            Cascade(false);
            if (!HasLegalMove() && !Reshuffle())
                throw new InvalidOperationException("Template/seed has no playable opening board.");
            Phase = ModePhase.Running;
        }

        /// <summary>Starts a new run after AlternativeModeGate (profile capability + LiveOps flag / kill switch).</summary>
        public static ModeSession Start(GameProfile profile, ModeTemplate template, uint seed, long startMs,
            ModeMatcher matcher, FeatureFlagRecord modeFlag, FeatureFlagPolicy flagPolicy)
        {
            if (template == null) throw new ArgumentNullException(nameof(template));
            if (!AlternativeModeGate.CanStart(profile, template.Kind, modeFlag, flagPolicy, out var reason))
                throw new InvalidOperationException("Mode not available: " + reason);
            return new ModeSession(profile.Id, template, matcher, seed, startMs);
        }

        /// <summary>Rebuilds a run from its log (restore after reconnect, or server-side verification).
        /// Deliberately bypasses AlternativeModeGate: it opens no new run, it only re-derives an existing one.
        /// Any protocol violation in the log fails the whole replay.</summary>
        public static ModeSession Replay(string gameId, ModeTemplate template, uint seed, long startMs,
            IEnumerable<ModeInput> inputs, ModeMatcher matcher)
        {
            if (inputs == null) throw new ArgumentNullException(nameof(inputs));
            var session = new ModeSession(gameId, template, matcher, seed, startMs);
            var count = 0;
            foreach (var input in inputs)
            {
                if (++count > ReplayTrace.MaxSteps * 20) throw new FormatException("Input log too large.");
                if (session.Apply(input) == ModeInputStatus.Rejected)
                    throw new FormatException("Input log protocol violation at input " + count + ".");
            }
            return session;
        }

        public ModeInputStatus Apply(ModeInput input)
        {
            var status = ApplyCore(input);
            if (status != ModeInputStatus.Rejected) _inputs.Add(input);
            return status;
        }

        private ModeInputStatus ApplyCore(ModeInput input)
        {
            var t = input.AtMs;
            if (input.Kind == ModeInputKind.Reconnect) return Reconnect(t);
            if (t < _lastMs) return ModeInputStatus.Rejected;
            if (input.Kind == ModeInputKind.Swap &&
                (!IsAdjacentOnBoard(input.From, input.To) || t - _lastSwapAtMs < Template.MinSwapIntervalMs))
                return ModeInputStatus.Rejected;
            Advance(t);
            switch (input.Kind)
            {
                case ModeInputKind.Tick:
                    return ModeInputStatus.Applied;
                case ModeInputKind.Pause:
                    if (Phase != ModePhase.Running) return ModeInputStatus.Ignored;
                    Phase = ModePhase.Paused;
                    IsAutoPaused = false;
                    return ModeInputStatus.Applied;
                case ModeInputKind.Resume:
                    if (Phase != ModePhase.Paused || IsBackgrounded) return ModeInputStatus.Ignored;
                    Phase = ModePhase.Running;
                    IsAutoPaused = false;
                    return ModeInputStatus.Applied;
                case ModeInputKind.Background:
                    if (Phase == ModePhase.Ended || IsBackgrounded) return ModeInputStatus.Ignored;
                    IsBackgrounded = true;
                    _backgroundSinceMs = t;
                    return ModeInputStatus.Applied;
                case ModeInputKind.Foreground:
                    if (!IsBackgrounded) return ModeInputStatus.Ignored;
                    IsBackgrounded = false; // an auto-pause stays until an explicit Resume
                    return ModeInputStatus.Applied;
                default:
                    return Swap(input.From, input.To, t);
            }
        }

        // Charges active time up to t. Background charges at most the grace, then auto-pauses exactly there.
        private void Advance(long t)
        {
            if (Phase == ModePhase.Running)
            {
                var graceEnd = _backgroundSinceMs + Template.BackgroundGraceMs;
                var until = IsBackgrounded ? Math.Min(t, graceEnd) : t;
                if (until > _lastMs) Charge(until - _lastMs);
                if (Phase == ModePhase.Running && IsBackgrounded && t > graceEnd)
                {
                    Phase = ModePhase.Paused;
                    IsAutoPaused = true;
                    FrozenMs = checked(FrozenMs + t - Math.Max(graceEnd, _lastMs));
                }
            }
            else if (Phase == ModePhase.Paused && t > _lastMs)
                FrozenMs = checked(FrozenMs + t - _lastMs);
            _lastMs = t;
        }

        private void Charge(long ms)
        {
            ElapsedMs = checked(ElapsedMs + ms);
            if (Template.TimeLimitMs > 0 && ElapsedMs >= Template.TimeLimitMs)
            {
                ElapsedMs = Template.TimeLimitMs;
                End(ModeEndReason.TimeExpired);
            }
        }

        private ModeInputStatus Reconnect(long newBaseMs)
        {
            if (Phase == ModePhase.Running)
            {
                // Same as a background that never came back: whatever grace is left is spent.
                var left = IsBackgrounded
                    ? Math.Max(0, _backgroundSinceMs + Template.BackgroundGraceMs - _lastMs)
                    : Template.BackgroundGraceMs;
                Charge(left);
            }
            _lastMs = newBaseMs;
            _lastSwapAtMs = newBaseMs; // first swap on the new base still waits MinSwapIntervalMs
            IsBackgrounded = false;
            if (Phase == ModePhase.Ended) return ModeInputStatus.Ignored;
            Phase = ModePhase.Paused;
            IsAutoPaused = true;
            return ModeInputStatus.Applied;
        }

        private ModeInputStatus Swap(BoardPosition from, BoardPosition to, long t)
        {
            if (Phase != ModePhase.Running) return ModeInputStatus.Ignored;
            var swapped = new Dictionary<BoardPosition, string>(_board);
            swapped[from] = _board[to];
            swapped[to] = _board[from];
            if (Runs(swapped).Count == 0) return ModeInputStatus.NoMatch;

            _board = swapped;
            _lastSwapAtMs = t;
            _appliedSwaps.Add(from + ">" + to);
            ElapsedAtLastSwapMs = ElapsedMs;
            Score = DeterministicScorePlanner.AddScore(Score, Cascade(true));
            Turns++;
            if (Template.TargetScore > 0 && TimeToTargetMs < 0 && Score >= Template.TargetScore)
                TimeToTargetMs = ElapsedMs;
            if (Template.RampEveryTurns > 0)
                ActiveColors = Math.Min(Template.Palette.Count, Template.StartColors + Turns / Template.RampEveryTurns);

            if (Template.Kind == GameModeKind.Challenge && Score >= Template.TargetScore) End(ModeEndReason.TargetReached);
            else if (Template.MoveLimit > 0 && Turns >= Template.MoveLimit) End(ModeEndReason.MovesExhausted);
            else if (Turns >= Template.MaxTurns) End(ModeEndReason.TurnCap);
            else if (!HasLegalMove())
            {
                if (ShuffleLivesRemaining == 0) End(ModeEndReason.NoValidMoves);
                else
                {
                    ShuffleLivesRemaining--;
                    if (!Reshuffle()) End(ModeEndReason.NoValidMoves);
                }
            }
            return ModeInputStatus.Applied;
        }

        private void End(ModeEndReason reason)
        {
            if (Phase == ModePhase.Ended) return;
            Phase = ModePhase.Ended;
            EndReason = reason;
            IsBackgrounded = false;
            IsAutoPaused = false;
        }

        // SDK match runs -> clear -> engine gravity -> engine seeded weighted refill, until settled.
        private int Cascade(bool scored)
        {
            var total = 0;
            for (var wave = 0; ; wave++)
            {
                var runs = Runs(_board);
                if (runs.Count == 0) return total;
                if (wave >= MaxCascadeWaves)
                    throw new InvalidOperationException("Cascade safety limit exceeded.");
                var cleared = new HashSet<BoardPosition>(runs.SelectMany(r => r.Cells));
                if (scored)
                    total = DeterministicScorePlanner.AddScore(total,
                        DeterministicScorePlanner.ScoreEvent(cleared.Count * Template.PointsPerCell, wave));
                var remaining = _board.Where(p => !cleared.Contains(p.Key)).Select(p => new BoardOccupant(p.Key, p.Value));
                var settled = GravityPlanner.PlanCardinal(new BoardTopology(Template.Rows, Template.Columns,
                    occupants: remaining)).Occupants;
                var catalog = Template.Palette.Take(ActiveColors).Select(c => new WeightedContent(c, 1)).ToList();
                _board = new Dictionary<BoardPosition, string>(
                    WeightedRefillPlanner.Plan(_empty, settled, catalog, _rng).Occupants);
            }
        }

        private bool Reshuffle()
        {
            for (var attempt = 0; attempt < MaxReshuffles; attempt++)
            {
                var plan = DeterministicBoardPlanning.TryShuffle(Occupied(_board), _rng, HasLegalMove, 32);
                if (!plan.Succeeded) return false;
                _board = new Dictionary<BoardPosition, string>(plan.Occupants);
                Cascade(false);
                if (HasLegalMove()) return true;
            }
            return false;
        }

        private bool HasLegalMove() => HasLegalMove(_board);

        private bool HasLegalMove(IReadOnlyDictionary<BoardPosition, string> board) =>
            CanonicalMoveCandidates.Find(Occupied(board), c =>
            {
                var swapped = board.ToDictionary(p => p.Key, p => p.Value);
                swapped[c.Source] = board[c.Target];
                swapped[c.Target] = board[c.Source];
                return Runs(swapped).Count > 0;
            }).HasMoves;

        private IReadOnlyList<MatchRunProjection> Runs(IDictionary<BoardPosition, string> board) =>
            Matcher.MatchRuns(new ReadOnlyDictionary<BoardPosition, string>(board)) ??
            throw new InvalidOperationException("SDK match adapter returned null.");

        private BoardTopology Occupied(IEnumerable<KeyValuePair<BoardPosition, string>> board) =>
            new BoardTopology(Template.Rows, Template.Columns,
                occupants: board.Select(p => new BoardOccupant(p.Key, p.Value)));

        private bool IsAdjacentOnBoard(BoardPosition a, BoardPosition b) =>
            a.Layer == 0 && b.Layer == 0 && _empty.Contains(a) && _empty.Contains(b) &&
            Math.Abs(a.Row - b.Row) + Math.Abs(a.Column - b.Column) == 1;

        public string BoardSnapshot()
        {
            var b = new StringBuilder();
            for (var r = 0; r < Template.Rows; r++)
            {
                if (r > 0) b.Append('/');
                for (var c = 0; c < Template.Columns; c++)
                {
                    if (c > 0) b.Append(',');
                    b.Append(_board[new BoardPosition(r, c)]);
                }
            }
            return b.ToString();
        }

        /// <summary>Hash of the complete logical state, including the RNG stream position and the matcher.</summary>
        public string StateHash() => ReplayCodec.BoardHash(string.Join("|",
            "M3MODESTATE2", GameId, Matcher.TemplateHash(Template), Seed.ToString(CultureInfo.InvariantCulture),
            ((int)Phase).ToString(CultureInfo.InvariantCulture), ((int)EndReason).ToString(CultureInfo.InvariantCulture),
            Score.ToString(CultureInfo.InvariantCulture), Turns.ToString(CultureInfo.InvariantCulture),
            ElapsedMs.ToString(CultureInfo.InvariantCulture), TimeToTargetMs.ToString(CultureInfo.InvariantCulture),
            ShuffleLivesRemaining.ToString(CultureInfo.InvariantCulture), ActiveColors.ToString(CultureInfo.InvariantCulture),
            IsBackgrounded ? "1" : "0", IsAutoPaused ? "1" : "0", _rng.State.ToString(CultureInfo.InvariantCulture),
            BoardSnapshot()));

        /// <summary>Canonical run identity: game, template, matcher, seed and the ordered applied swaps only.
        /// Timestamps, ticks, pauses and ignored/no-match inputs do not change it.</summary>
        public string RunId => ReplayCodec.BoardHash(string.Join("|",
            "M3MODERUN2", GameId, Template.ContentHash, Matcher.MatcherId, Seed.ToString(CultureInfo.InvariantCulture),
            string.Join(";", _appliedSwaps)));

        public ModeRunResult Result() =>
            Phase == ModePhase.Ended
                ? new ModeRunResult(this)
                : throw new InvalidOperationException("Run has not ended.");
    }

    public sealed class ModeRecord
    {
        public GameModeKind Kind { get; }
        public string TemplateId { get; }
        public int BestScore { get; internal set; }
        /// <summary>Time Attack: fastest ms to TargetScore. Endless: longest active ms up to the last applied
        /// swap (idle time after it never counts). -1 = none.</summary>
        public long BestTimeMs { get; internal set; } = -1;
        public int Runs { get; internal set; }

        internal ModeRecord(GameModeKind kind, string templateId)
        {
            Kind = kind;
            TemplateId = templateId;
        }
    }

    /// <summary>Per-player records, isolated per (mode, template). Only engine-produced ended results.</summary>
    public sealed class ModeRecordBook
    {
        private readonly Dictionary<string, ModeRecord> _records = new Dictionary<string, ModeRecord>(StringComparer.Ordinal);
        private readonly HashSet<string> _runIds = new HashSet<string>(StringComparer.Ordinal);

        public string GameId { get; }

        public ModeRecordBook(string gameId)
        {
            if (!GameProfileCatalog.IsKnownId(gameId)) throw new ArgumentException("Unknown game ID.", nameof(gameId));
            GameId = gameId;
        }

        /// <returns>True when the result was new (not a duplicate run).</returns>
        public bool Submit(ModeRunResult result)
        {
            if (result == null) throw new ArgumentNullException(nameof(result));
            if (!StringComparer.Ordinal.Equals(result.GameId, GameId))
                throw new ArgumentException("Result belongs to another game.", nameof(result));
            if (!_runIds.Add(result.RunId)) return false;
            var key = ((int)result.Kind).ToString(CultureInfo.InvariantCulture) + "|" + result.TemplateId;
            if (!_records.TryGetValue(key, out var record))
                _records[key] = record = new ModeRecord(result.Kind, result.TemplateId);
            record.Runs++;
            record.BestScore = Math.Max(record.BestScore, result.Score);
            if (result.Kind == GameModeKind.TimeAttack && result.TimeToTargetMs >= 0 &&
                (record.BestTimeMs < 0 || result.TimeToTargetMs < record.BestTimeMs))
                record.BestTimeMs = result.TimeToTargetMs;
            if (result.Kind == GameModeKind.Endless && result.ElapsedAtLastSwapMs > record.BestTimeMs)
                record.BestTimeMs = result.ElapsedAtLastSwapMs;
            return true;
        }

        public ModeRecord Get(GameModeKind kind, string templateId) =>
            _records.TryGetValue(((int)kind).ToString(CultureInfo.InvariantCulture) + "|" + templateId, out var r) ? r : null;
    }

    public enum ModeRewardStatus { Granted, NothingEarned, DuplicateRun, SeedReplayed, DailyCapReached, Rejected }

    public sealed class ModeRewardOutcome
    {
        public ModeRewardStatus Status { get; }
        public string Reason { get; }
        /// <summary>Ledger records applied by this call.</summary>
        public IReadOnlyList<RewardLedgerRecord> Grants { get; }

        internal ModeRewardOutcome(ModeRewardStatus status, string reason, IList<RewardLedgerRecord> grants = null)
        {
            Status = status;
            Reason = reason ?? string.Empty;
            Grants = new ReadOnlyCollection<RewardLedgerRecord>(grants ?? new List<RewardLedgerRecord>());
        }
    }

    /// <summary>
    /// Mode rewards through the ENG-M16-011 RewardLedger (source "alt_mode"). Everything that limits a reward is
    /// re-derived from the ledger, so losing local state (restart, reinstall) cannot re-open it:
    /// - per-day cap per mode: slots "kind|yyyy-MM-dd|t&lt;k&gt;", ExpiresUtc = end of that UTC day (server anchor);
    ///   today's total = sum of today's slots;
    /// - per-run cap: tiers of one run add up to at most PerRunRewardCap;
    /// - replay: each grant's Reference = "r" + RunId + "s" + seed key + "t" + tier; a run ID or (template, seed)
    ///   already present in live alt_mode records never earns again (settled tombstones drop references after
    ///   expiry + retention; the day cap still holds).
    /// Only a server-anchored TrustedUtcAnchor decides the day. The ledger rejects premium assets for every
    /// family-safe / non-monetized title.
    /// </summary>
    public sealed class ModeRewardIssuer
    {
        private readonly RewardLedger _ledger;

        public string GameId => _ledger.GameId;
        public string ProfileId => _ledger.PlayerId;

        public ModeRewardIssuer(RewardLedger ledger)
        {
            _ledger = ledger ?? throw new ArgumentNullException(nameof(ledger));
        }

        public static string SlotInstanceId(GameModeKind kind, DateTime utcDay, int slot) =>
            kind + "|" + utcDay.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + "|t" +
            slot.ToString(CultureInfo.InvariantCulture);

        /// <summary>Total granted for (mode, UTC day), read from the ledger.</summary>
        public long GrantedOn(GameModeKind kind, DateTime utcDay)
        {
            var prefix = kind + "|" + utcDay.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + "|t";
            long total = 0;
            foreach (var instance in _ledger.InstanceIds(RewardSources.AltMode))
            {
                if (!instance.StartsWith(prefix, StringComparison.Ordinal)) continue;
                var key = RewardLedgerKey.Derive(GameId, ProfileId, RewardSources.AltMode, instance);
                if (_ledger.TryGetRecord(key, out var r)) total += r.Amounts.Values.Sum();
                else if (_ledger.TryGetTombstone(key, out var t)) total += t.Amounts.Values.Sum();
            }
            return total;
        }

        public ModeRewardOutcome Issue(ModeRunResult result, ModeTemplate template, TrustedUtcAnchor anchor)
        {
            if (result == null) throw new ArgumentNullException(nameof(result));
            if (template == null) throw new ArgumentNullException(nameof(template));
            if (!anchor.IsServerAnchored || anchor.UtcNow.Kind != DateTimeKind.Utc)
                return new ModeRewardOutcome(ModeRewardStatus.Rejected, RewardLedgerReasons.TrustedTimeAnchorRequired);
            if (!StringComparer.Ordinal.Equals(result.GameId, GameId))
                return new ModeRewardOutcome(ModeRewardStatus.Rejected, "GameMismatch");
            if (!StringComparer.Ordinal.Equals(result.TemplateContentHash, template.ContentHash))
                return new ModeRewardOutcome(ModeRewardStatus.Rejected, "TemplateMismatch");

            var seedKey = ReplayCodec.BoardHash(template.TemplateId + "|" + result.Seed.ToString(CultureInfo.InvariantCulture))
                .Substring(0, 32);
            var runTag = "r" + result.RunId;
            var seedTag = "s" + seedKey;
            foreach (var instance in _ledger.InstanceIds(RewardSources.AltMode))
            {
                if (!_ledger.TryGetRecord(RewardLedgerKey.Derive(GameId, ProfileId, RewardSources.AltMode, instance), out var r) ||
                    r.Reference == null) continue;
                if (r.Reference.StartsWith(runTag, StringComparison.Ordinal))
                    return new ModeRewardOutcome(ModeRewardStatus.DuplicateRun, "RunAlreadyRewarded");
                if (r.Reference.IndexOf(seedTag, StringComparison.Ordinal) >= 0)
                    return new ModeRewardOutcome(ModeRewardStatus.SeedReplayed, "SeedAlreadyRewarded");
            }

            var day = anchor.UtcNow.Date;
            var expires = DateTime.SpecifyKind(day.AddDays(1).AddTicks(-1), DateTimeKind.Utc);
            var today = GrantedOn(result.Kind, day);
            var runLeft = (long)template.PerRunRewardCap;
            var grants = new List<RewardLedgerRecord>();
            var capped = false;
            var slot = 1;
            for (var i = 0; i < template.RewardTiers.Count; i++)
            {
                var tier = template.RewardTiers[i];
                if (result.Score < tier.Score) break;
                var amount = Math.Min(tier.Amount, Math.Min(runLeft, template.PerDayRewardCap - today));
                if (amount <= 0) { capped = true; break; }
                // Next free day slot; the ledger key is the cap's durable proof.
                RewardLedgerGrantResult granted;
                do
                {
                    if (slot > template.PerDayRewardCap)
                        return new ModeRewardOutcome(grants.Count > 0 ? ModeRewardStatus.Granted : ModeRewardStatus.DailyCapReached,
                            "NoFreeSlot", grants);
                    var instance = SlotInstanceId(result.Kind, day, slot++);
                    granted = _ledger.TryGrant(new RewardLedgerRequest(GameId, ProfileId, RewardSources.AltMode, instance,
                        new Dictionary<string, long> { [template.RewardId] = amount }, anchor.UtcNow, expires,
                        runTag + seedTag + "t" + (i + 1).ToString(CultureInfo.InvariantCulture)), anchor);
                } while (granted.Outcome != RewardLedgerOutcome.Applied &&
                         (granted.Outcome == RewardLedgerOutcome.Duplicate ||
                          granted.Reason == RewardLedgerReasons.IdempotencyConflict));
                if (granted.Outcome != RewardLedgerOutcome.Applied)
                    return new ModeRewardOutcome(grants.Count > 0 ? ModeRewardStatus.Granted : ModeRewardStatus.Rejected,
                        granted.Reason, grants);
                grants.Add(granted.Record);
                runLeft -= amount;
                today += amount;
            }
            if (grants.Count > 0) return new ModeRewardOutcome(ModeRewardStatus.Granted, capped ? "Capped" : "", grants);
            return capped && template.PerDayRewardCap - today <= 0
                ? new ModeRewardOutcome(ModeRewardStatus.DailyCapReached, "DailyCapReached")
                : new ModeRewardOutcome(ModeRewardStatus.NothingEarned, "BelowFirstTier");
        }
    }
}
