using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Match3.Engine.Runtime.Economy
{
    // ENG-M20-008: deterministic day-by-day coin/heart/booster model for a cohort
    // of simulated players, with policy checks that fail on violation.
    // All gameplay math is integer (basis points, minutes, coins) and randomness
    // comes from SplitMix64, so results are bit-identical on every platform.
    // Default numbers are engineering placeholders pending owner economy approval
    // (ENG-M20-009); the policy checks are what the defaults are validated against.
    // The four general-audience profiles (Coral_Rescue, Garage_Legends, Dino_Islands,
    // The_Fractured_Realms) deliberately share IDENTICAL placeholder parameters: no
    // per-title tuning has been approved, so none is invented here.
    // Wonder_Pop is a general/family-friendly title (owner decision, GameBusinessProfiles)
    // with ads/IAP/telemetry off; it always runs under the forced family-safe /
    // monetization-off policy set below. Reason codes keep their historical "Child" prefix.

    /// <summary>SplitMix64 (Steele/Lea/Flood). Small, fast, platform-independent.</summary>
    public sealed class SplitMix64
    {
        private ulong _state;

        public SplitMix64(ulong seed) { _state = seed; }

        public ulong Next()
        {
            unchecked
            {
                var z = _state += 0x9E3779B97F4A7C15UL;
                z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
                z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
                return z ^ (z >> 31);
            }
        }

        /// <summary>Uniform-enough integer in [0, n). ponytail: modulo bias &lt; 1e-15 for n &lt;= 10000.</summary>
        public int NextInt(int n)
        {
            if (n <= 0) throw new ArgumentOutOfRangeException(nameof(n));
            return (int)(Next() % (ulong)n);
        }
    }

    public static class EconomyPolicyViolations
    {
        public const string PayToProgressWall = "PayToProgressWall";
        /// <summary>Family-safe profile: paid currency enabled or any purchase observed.</summary>
        public const string ChildPaidCurrencyFlow = "ChildPaidCurrencyFlow";
        /// <summary>Family-safe profile: random reward configured or variable rewards observed.</summary>
        public const string ChildRandomReward = "ChildRandomReward";
        /// <summary>Family-safe profile: per-heart wait configured or observed above the cap.</summary>
        public const string ChildHeartWaitExceeded = "ChildHeartWaitExceeded";
        public const string EventIncentiveUnbounded = "EventIncentiveUnbounded";
    }

    /// <summary>Per-game economy inputs. Probabilities are basis points (0..10000).</summary>
    public sealed class EconomyParameters
    {
        public string GameId { get; set; }
        // Policy defaults. For family-safe runs callers may only tighten these (Run clamps them).
        public const int DefaultMinNonPayerLevelsPerDayX100 = 100;
        public const int DefaultMinNonPayerProgressFractionBp = 8000;
        public const int DefaultMaxEventIncentiveBp = 5000;
        public const int FamilySafeHeartWaitCapMinutes = 15;

        /// <summary>Family-safe / monetization-off policy set. Always forced on for Wonder_Pop (non-relaxable).</summary>
        public bool FamilySafe { get; set; }

        public int Days { get; set; } = 30;
        public int Players { get; set; } = 200;
        public ulong Seed { get; set; } = 20261111UL;

        public int SessionsPerDay { get; set; } = 3;
        public int SessionMinutes { get; set; } = 20;
        public int AttemptMinutes { get; set; } = 3;

        public int MaxHearts { get; set; } = 5;
        public int HeartRegenMinutes { get; set; } = 30;

        public int StartingCoins { get; set; } = 500;
        public int StartingBoosters { get; set; } = 3;
        public int WinCoins { get; set; } = 50;
        /// <summary>Extra uniform random 0..N coins per win (chest-style). Must be 0 for family-safe.</summary>
        public int WinCoinsRandomBonus { get; set; } = 20;
        public int DailyLoginCoins { get; set; } = 20;

        public int HeartRefillCoinCost { get; set; } = 300;
        public int ContinueCoinCost { get; set; } = 100;
        public int BoosterCoinCost { get; set; } = 150;
        public int BoosterAfterFails { get; set; } = 2;
        public int BoosterWinBonusBp { get; set; } = 1500;

        public int BaseWinBp { get; set; } = 6500;
        public int DifficultyRampBpPerLevel { get; set; } = 40;
        public int MinWinBp { get; set; } = 1500;
        public int SkillSpreadBp { get; set; } = 1000;
        public int ContinueWinBp { get; set; } = 5000;

        /// <summary>Event active on days where day % EventEveryNDays &lt; EventLengthDays (0 = no events).</summary>
        public int EventEveryNDays { get; set; } = 7;
        public int EventLengthDays { get; set; } = 3;
        public int EventWinBonusCoins { get; set; } = 25;

        public bool PaidCurrencyEnabled { get; set; }
        public int PayerFractionBp { get; set; } = 500;
        public int PaidCoinPack { get; set; } = 1000;

        /// <summary>Non-payer must average at least this many levels/day x100 to count as progressing.</summary>
        public int MinNonPayerLevelsPerDayX100 { get; set; } = DefaultMinNonPayerLevelsPerDayX100;
        /// <summary>Fraction (bp) of non-payers that must be progressing; below = pay-to-progress wall.</summary>
        public int MinNonPayerProgressFractionBp { get; set; } = DefaultMinNonPayerProgressFractionBp;
        /// <summary>Family-safe cap on any heart wait; never above <see cref="FamilySafeHeartWaitCapMinutes"/>.</summary>
        public int FamilySafeMaxHeartWaitMinutes { get; set; } = FamilySafeHeartWaitCapMinutes;
        /// <summary>Event coins may not exceed this fraction (bp) of core level-win coins.</summary>
        public int MaxEventIncentiveBp { get; set; } = DefaultMaxEventIncentiveBp;

        public static EconomyParameters DefaultFor(string gameId)
        {
            var business = GameBusinessProfileCatalog.Require(gameId);
            var familySafe = EconomyBalanceSimulator.IsFamilySafeProfile(gameId);
            // Identical placeholder numbers for every general profile (pending ENG-M20-009);
            // only the family-safe policy differences below are applied.
            var p = new EconomyParameters
            {
                GameId = gameId,
                FamilySafe = familySafe,
                PaidCurrencyEnabled = business.MonetizationEnabled && !familySafe
            };
            if (familySafe)
            {
                p.PayerFractionBp = 0;
                p.WinCoinsRandomBonus = 0;
                p.HeartRegenMinutes = 10;
            }
            return p;
        }

        public EconomyParameters Clone() => (EconomyParameters)MemberwiseClone();

        internal void Validate()
        {
            if (!GameProfileCatalog.IsKnownId(GameId)) throw new ArgumentException("Unknown game ID.", nameof(GameId));
            Range(Days, 1, 3650, nameof(Days));
            Range(Players, 1, 100000, nameof(Players));
            Range(SessionsPerDay, 1, 24, nameof(SessionsPerDay));
            Range(SessionMinutes, 1, 600, nameof(SessionMinutes));
            Range(AttemptMinutes, 1, SessionMinutes, nameof(AttemptMinutes));
            Range(MaxHearts, 1, 100, nameof(MaxHearts));
            Range(HeartRegenMinutes, 1, 1440, nameof(HeartRegenMinutes));
            Range(StartingCoins, 0, 1000000, nameof(StartingCoins));
            Range(StartingBoosters, 0, 1000, nameof(StartingBoosters));
            Range(WinCoins, 0, 100000, nameof(WinCoins));
            Range(WinCoinsRandomBonus, 0, 100000, nameof(WinCoinsRandomBonus));
            Range(DailyLoginCoins, 0, 100000, nameof(DailyLoginCoins));
            Range(HeartRefillCoinCost, 1, 1000000, nameof(HeartRefillCoinCost));
            Range(ContinueCoinCost, 1, 1000000, nameof(ContinueCoinCost));
            Range(BoosterCoinCost, 1, 1000000, nameof(BoosterCoinCost));
            Range(BoosterAfterFails, 0, 100, nameof(BoosterAfterFails));
            Range(BoosterWinBonusBp, 0, 10000, nameof(BoosterWinBonusBp));
            Range(BaseWinBp, 0, 10000, nameof(BaseWinBp));
            Range(DifficultyRampBpPerLevel, 0, 10000, nameof(DifficultyRampBpPerLevel));
            Range(MinWinBp, 0, 10000, nameof(MinWinBp));
            Range(SkillSpreadBp, 0, 5000, nameof(SkillSpreadBp));
            Range(ContinueWinBp, 0, 10000, nameof(ContinueWinBp));
            Range(EventEveryNDays, 0, 365, nameof(EventEveryNDays));
            Range(EventLengthDays, 0, Math.Max(EventEveryNDays, 0), nameof(EventLengthDays));
            Range(EventWinBonusCoins, 0, 100000, nameof(EventWinBonusCoins));
            Range(PayerFractionBp, 0, 10000, nameof(PayerFractionBp));
            Range(PaidCoinPack, 0, 1000000, nameof(PaidCoinPack));
            Range(MinNonPayerLevelsPerDayX100, 0, 10000, nameof(MinNonPayerLevelsPerDayX100));
            Range(MinNonPayerProgressFractionBp, 0, 10000, nameof(MinNonPayerProgressFractionBp));
            Range(FamilySafeMaxHeartWaitMinutes, 0, 1440, nameof(FamilySafeMaxHeartWaitMinutes));
            Range(MaxEventIncentiveBp, 0, 100000, nameof(MaxEventIncentiveBp));
        }

        private static void Range(long v, long min, long max, string name)
        {
            if (v < min || v > max) throw new ArgumentOutOfRangeException(name, v, "allowed " + min + ".." + max);
        }
    }

    /// <summary>End-of-day cohort totals (sums over all players; means are derived).</summary>
    public sealed class EconomyDaySnapshot
    {
        public int Day { get; internal set; }
        public bool EventActive { get; internal set; }
        public long SumCoins { get; internal set; }
        public long SumHearts { get; internal set; }
        public long SumBoosters { get; internal set; }
        public long SumLevelsCompleted { get; internal set; }
        public int StalledPlayers { get; internal set; }
        public int PaidPurchases { get; internal set; }
        internal int Players { get; set; }

        public double MeanCoins => (double)SumCoins / Players;
        public double MeanHearts => (double)SumHearts / Players;
        public double MeanBoosters => (double)SumBoosters / Players;
        public double MeanLevelsCompleted => (double)SumLevelsCompleted / Players;
    }

    public sealed class EconomySimulationResult
    {
        public string GameId { get; internal set; }
        public bool FamilySafe { get; internal set; }
        public IReadOnlyList<EconomyDaySnapshot> Days { get; internal set; }

        public int Payers { get; internal set; }
        public int NonPayers { get; internal set; }

        public long CoinsEarnedFromWins { get; internal set; }
        public long CoinsEarnedFromEvents { get; internal set; }
        public long CoinsEarnedFromLogin { get; internal set; }
        public long CoinsBought { get; internal set; }
        public long CoinsSpentOnRefills { get; internal set; }
        public long CoinsSpentOnContinues { get; internal set; }
        public long CoinsSpentOnBoosters { get; internal set; }
        public long PaidPurchases { get; internal set; }
        public int MinWinReward { get; internal set; }
        public int MaxWinReward { get; internal set; }

        public long CoinsEarned => CoinsEarnedFromWins + CoinsEarnedFromEvents + CoinsEarnedFromLogin;
        public long CoinsSpent => CoinsSpentOnRefills + CoinsSpentOnContinues + CoinsSpentOnBoosters;
        /// <summary>Spent / earned (excluding bought coins). Below 1 = net coin inflation.</summary>
        public double SinkToEarnRatio => CoinsEarned == 0 ? 0 : (double)CoinsSpent / CoinsEarned;
        /// <summary>Mean coin balance growth per player per day over the horizon.</summary>
        public double CoinInflationPerDay { get; internal set; }

        /// <summary>Median day (0-based) of a non-payer's first zero-win day; -1 when most never stall.</summary>
        public int MedianNonPayerTimeToStallDay { get; internal set; }
        /// <summary>Fraction (bp) of non-payers meeting MinNonPayerLevelsPerDayX100 without paying.</summary>
        public int NonPayerProgressFractionBp { get; internal set; }
        public long NonPayerMeanLevelsX100 { get; internal set; }
        public long PayerMeanLevelsX100 { get; internal set; }
        /// <summary>Longest time any player waited for a heart while wanting to play.</summary>
        public int MaxHeartWaitMinutes { get; internal set; }

        /// <summary>Policy limits actually applied (after family-safe clamping).</summary>
        public int EffectiveFamilySafeMaxHeartWaitMinutes { get; internal set; }
        public int EffectiveMaxEventIncentiveBp { get; internal set; }
        public int EffectiveMinNonPayerProgressFractionBp { get; internal set; }
        public int EffectiveMinNonPayerLevelsPerDayX100 { get; internal set; }

        public IReadOnlyList<string> Violations { get; internal set; }
        public bool Passed => Violations.Count == 0;
        /// <summary>SHA-256 over the canonical daily integer series; golden-pinnable.</summary>
        public string Fingerprint { get; internal set; }
    }

    public static class EconomyBalanceSimulator
    {
        private const int DayMinutes = 1440;
        private const int WakeMinute = 8 * 60;
        private const int AwakeMinutes = 14 * 60;

        public static bool IsFamilySafeProfile(string gameId) => StringComparer.Ordinal.Equals(gameId, "Wonder_Pop");

        public static EconomySimulationResult Run(EconomyParameters input)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));
            var p = input.Clone();
            p.Validate();
            var child = p.FamilySafe || IsFamilySafeProfile(p.GameId);
            if (child)
            {
                // Family-safe limits can only be tightened by callers, never loosened.
                p.FamilySafeMaxHeartWaitMinutes = Math.Min(p.FamilySafeMaxHeartWaitMinutes, EconomyParameters.FamilySafeHeartWaitCapMinutes);
                p.MaxEventIncentiveBp = Math.Min(p.MaxEventIncentiveBp, EconomyParameters.DefaultMaxEventIncentiveBp);
                p.MinNonPayerProgressFractionBp = Math.Max(p.MinNonPayerProgressFractionBp, EconomyParameters.DefaultMinNonPayerProgressFractionBp);
                p.MinNonPayerLevelsPerDayX100 = Math.Max(p.MinNonPayerLevelsPerDayX100, EconomyParameters.DefaultMinNonPayerLevelsPerDayX100);
            }

            var days = new EconomyDaySnapshot[p.Days];
            for (var d = 0; d < p.Days; d++)
                days[d] = new EconomyDaySnapshot { Day = d, Players = p.Players, EventActive = IsEventDay(p, d) };

            var r = new EconomySimulationResult { GameId = p.GameId, FamilySafe = child, MinWinReward = int.MaxValue };
            var stallDays = new List<int>();
            long nonPayerLevels = 0, payerLevels = 0;
            var progressing = 0;

            for (var player = 0; player < p.Players; player++)
            {
                var s = SimulatePlayer(p, player, days, r);
                if (s.Payer)
                {
                    r.Payers++;
                    payerLevels += s.Levels;
                }
                else
                {
                    r.NonPayers++;
                    nonPayerLevels += s.Levels;
                    stallDays.Add(s.FirstStallDay < 0 ? int.MaxValue : s.FirstStallDay);
                    if (s.Levels * 100L >= (long)p.MinNonPayerLevelsPerDayX100 * p.Days) progressing++;
                }
            }

            if (r.MinWinReward == int.MaxValue) r.MinWinReward = 0;
            r.Days = Array.AsReadOnly(days);
            r.NonPayerProgressFractionBp = r.NonPayers == 0 ? 0 : (int)(progressing * 10000L / r.NonPayers);
            r.NonPayerMeanLevelsX100 = r.NonPayers == 0 ? 0 : nonPayerLevels * 100 / r.NonPayers;
            r.PayerMeanLevelsX100 = r.Payers == 0 ? 0 : payerLevels * 100 / r.Payers;
            stallDays.Sort();
            var median = stallDays.Count == 0 ? int.MaxValue : stallDays[(stallDays.Count - 1) / 2];
            r.MedianNonPayerTimeToStallDay = median == int.MaxValue ? -1 : median;
            r.CoinInflationPerDay = (days[p.Days - 1].MeanCoins - p.StartingCoins) / p.Days;

            var v = new List<string>();
            if (r.NonPayers == 0 || r.NonPayerProgressFractionBp < p.MinNonPayerProgressFractionBp)
                v.Add(EconomyPolicyViolations.PayToProgressWall);
            if (child && (p.PaidCurrencyEnabled || r.PaidPurchases > 0 || r.CoinsBought > 0))
                v.Add(EconomyPolicyViolations.ChildPaidCurrencyFlow);
            // Configuration and observation both count: a cohort that never wins or never runs out
            // of hearts must not hide a random reward or a long per-heart wait.
            if (child && (p.WinCoinsRandomBonus > 0 || r.MaxWinReward != r.MinWinReward))
                v.Add(EconomyPolicyViolations.ChildRandomReward);
            if (child && (p.HeartRegenMinutes > p.FamilySafeMaxHeartWaitMinutes ||
                          r.MaxHeartWaitMinutes > p.FamilySafeMaxHeartWaitMinutes))
                v.Add(EconomyPolicyViolations.ChildHeartWaitExceeded);
            r.EffectiveFamilySafeMaxHeartWaitMinutes = p.FamilySafeMaxHeartWaitMinutes;
            r.EffectiveMaxEventIncentiveBp = p.MaxEventIncentiveBp;
            r.EffectiveMinNonPayerProgressFractionBp = p.MinNonPayerProgressFractionBp;
            r.EffectiveMinNonPayerLevelsPerDayX100 = p.MinNonPayerLevelsPerDayX100;
            if (r.CoinsEarnedFromEvents * 10000L > r.CoinsEarnedFromWins * (long)p.MaxEventIncentiveBp)
                v.Add(EconomyPolicyViolations.EventIncentiveUnbounded);
            r.Violations = v.AsReadOnly();
            r.Fingerprint = Fingerprint(r);
            return r;
        }

        private static bool IsEventDay(EconomyParameters p, int day) =>
            p.EventEveryNDays > 0 && day % p.EventEveryNDays < p.EventLengthDays;

        private struct PlayerOutcome
        {
            public bool Payer;
            public long Levels;
            public int FirstStallDay;
        }

        private static PlayerOutcome SimulatePlayer(EconomyParameters p, int index, EconomyDaySnapshot[] days, EconomySimulationResult r)
        {
            // Independent per-player stream: results do not depend on player iteration order.
            var rng = new SplitMix64(p.Seed ^ unchecked((ulong)(index + 1) * 0xD1B54A32D192ED03UL));
            var skill = rng.NextInt(2 * p.SkillSpreadBp + 1) - p.SkillSpreadBp;
            var payer = p.PaidCurrencyEnabled && rng.NextInt(10000) < p.PayerFractionBp;

            long coins = p.StartingCoins;
            var hearts = p.MaxHearts;
            var regenAcc = 0;
            long clock = 0;
            var boosters = p.StartingBoosters;
            var level = 0L;
            var fails = 0;
            var firstStall = -1;

            void Regen(long to)
            {
                if (to <= clock) return;
                hearts = RegenerateHearts(hearts, regenAcc, to - clock, p.MaxHearts, p.HeartRegenMinutes, out regenAcc);
                clock = to;
            }

            for (var d = 0; d < p.Days; d++)
            {
                var snap = days[d];
                var winsToday = 0;
                coins += p.DailyLoginCoins;
                r.CoinsEarnedFromLogin += p.DailyLoginCoins;

                for (var session = 0; session < p.SessionsPerDay; session++)
                {
                    var t = (long)d * DayMinutes + WakeMinute + session * (AwakeMinutes / p.SessionsPerDay);
                    var end = t + p.SessionMinutes;
                    Regen(t);
                    while (t + p.AttemptMinutes <= end)
                    {
                        if (hearts == 0)
                        {
                            if (coins < p.HeartRefillCoinCost && payer && p.PaidCoinPack > 0)
                            {
                                coins += p.PaidCoinPack;
                                r.CoinsBought += p.PaidCoinPack;
                                r.PaidPurchases++;
                                snap.PaidPurchases++;
                            }
                            if (coins >= p.HeartRefillCoinCost)
                            {
                                coins -= p.HeartRefillCoinCost;
                                r.CoinsSpentOnRefills += p.HeartRefillCoinCost;
                                hearts = p.MaxHearts;
                                regenAcc = 0;
                                continue;
                            }
                            var wait = p.HeartRegenMinutes - regenAcc;
                            r.MaxHeartWaitMinutes = Math.Max(r.MaxHeartWaitMinutes, wait);
                            t += wait;
                            Regen(t);
                            continue;
                        }

                        var boosted = false;
                        if (fails >= p.BoosterAfterFails)
                        {
                            if (boosters == 0 && coins >= p.BoosterCoinCost)
                            {
                                coins -= p.BoosterCoinCost;
                                r.CoinsSpentOnBoosters += p.BoosterCoinCost;
                                boosters++;
                            }
                            if (boosters > 0)
                            {
                                boosters--;
                                boosted = true;
                            }
                        }

                        var winBp = Clamp(p.BaseWinBp - (int)Math.Min(level * p.DifficultyRampBpPerLevel, 10000) + skill, p.MinWinBp, 10000);
                        if (boosted) winBp = Math.Min(10000, winBp + p.BoosterWinBonusBp);
                        t += p.AttemptMinutes;
                        Regen(t);

                        var win = rng.NextInt(10000) < winBp;
                        if (!win && fails >= 1 && coins >= p.ContinueCoinCost)
                        {
                            coins -= p.ContinueCoinCost;
                            r.CoinsSpentOnContinues += p.ContinueCoinCost;
                            win = rng.NextInt(10000) < p.ContinueWinBp;
                        }

                        if (win)
                        {
                            level++;
                            winsToday++;
                            fails = 0;
                            var reward = p.WinCoins + (p.WinCoinsRandomBonus > 0 ? rng.NextInt(p.WinCoinsRandomBonus + 1) : 0);
                            coins += reward;
                            r.CoinsEarnedFromWins += reward;
                            r.MinWinReward = Math.Min(r.MinWinReward, reward);
                            r.MaxWinReward = Math.Max(r.MaxWinReward, reward);
                            if (snap.EventActive)
                            {
                                coins += p.EventWinBonusCoins;
                                r.CoinsEarnedFromEvents += p.EventWinBonusCoins;
                            }
                        }
                        else
                        {
                            hearts--;
                            fails++;
                        }
                    }
                }

                if (winsToday == 0)
                {
                    snap.StalledPlayers++;
                    if (firstStall < 0 && !payer) firstStall = d;
                }
                Regen((long)(d + 1) * DayMinutes);
                snap.SumCoins += coins;
                snap.SumHearts += hearts;
                snap.SumBoosters += boosters;
                snap.SumLevelsCompleted += level;
            }

            return new PlayerOutcome { Payer = payer, Levels = level, FirstStallDay = firstStall };
        }

        /// <summary>
        /// Heart timer: one heart per <paramref name="regenMinutes"/> of accumulated time while below
        /// max; the partial-heart remainder carries over and resets to 0 once hearts are full.
        /// </summary>
        public static int RegenerateHearts(int hearts, int accumulatedMinutes, long elapsedMinutes,
            int maxHearts, int regenMinutes, out int newAccumulatedMinutes)
        {
            if (regenMinutes <= 0) throw new ArgumentOutOfRangeException(nameof(regenMinutes));
            if (elapsedMinutes < 0) throw new ArgumentOutOfRangeException(nameof(elapsedMinutes));
            if (hearts >= maxHearts)
            {
                newAccumulatedMinutes = 0;
                return hearts;
            }
            var total = accumulatedMinutes + elapsedMinutes;
            var gained = total / regenMinutes;
            newAccumulatedMinutes = (int)(total % regenMinutes);
            if (hearts + gained >= maxHearts)
            {
                newAccumulatedMinutes = 0;
                return maxHearts;
            }
            return hearts + (int)gained;
        }

        private static int Clamp(int v, int min, int max) => v < min ? min : v > max ? max : v;

        private static string Fingerprint(EconomySimulationResult r)
        {
            var sb = new StringBuilder();
            sb.Append(r.GameId).Append('|').Append(r.Payers).Append('|').Append(r.NonPayers).Append('\n');
            foreach (var d in r.Days)
                sb.Append(d.Day.ToString(CultureInfo.InvariantCulture)).Append(',')
                  .Append(d.SumCoins.ToString(CultureInfo.InvariantCulture)).Append(',')
                  .Append(d.SumHearts.ToString(CultureInfo.InvariantCulture)).Append(',')
                  .Append(d.SumBoosters.ToString(CultureInfo.InvariantCulture)).Append(',')
                  .Append(d.SumLevelsCompleted.ToString(CultureInfo.InvariantCulture)).Append(',')
                  .Append(d.StalledPlayers.ToString(CultureInfo.InvariantCulture)).Append(',')
                  .Append(d.PaidPurchases.ToString(CultureInfo.InvariantCulture)).Append('\n');
            using (var sha = SHA256.Create())
                return string.Concat(sha.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString())).Select(b => b.ToString("x2", CultureInfo.InvariantCulture)));
        }
    }
}
