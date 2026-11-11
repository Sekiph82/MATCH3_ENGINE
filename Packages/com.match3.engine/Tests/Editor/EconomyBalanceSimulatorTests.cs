using System;
using System.Collections.Generic;
using System.Linq;
using Match3.Engine.Runtime;
using Match3.Engine.Runtime.Economy;
using NUnit.Framework;
using V = Match3.Engine.Runtime.Economy.EconomyPolicyViolations;

namespace Match3.Engine.Tests.Editor
{
    // ENG-M20-008.
    public sealed class EconomyBalanceSimulatorTests
    {
        private static string Describe(EconomySimulationResult r) =>
            r.GameId + " violations=[" + string.Join(",", r.Violations) + "] progressBp=" + r.NonPayerProgressFractionBp +
            " npLevelsX100=" + r.NonPayerMeanLevelsX100 + " pLevelsX100=" + r.PayerMeanLevelsX100 +
            " maxWait=" + r.MaxHeartWaitMinutes + " paid=" + r.PaidPurchases + " stall=" + r.MedianNonPayerTimeToStallDay +
            " sink/earn=" + r.SinkToEarnRatio.ToString("F4") + " event=" + r.CoinsEarnedFromEvents + "/" + r.CoinsEarnedFromWins +
            " fp=" + r.Fingerprint;

        // ---- PRNG ----------------------------------------------------------------

        [Test]
        public void SplitMix64_MatchesReferenceSequence()
        {
            // Reference SplitMix64 outputs for seed 0 (Vigna's splitmix64.c).
            var rng = new SplitMix64(0);
            Assert.That(rng.Next(), Is.EqualTo(0xE220A8397B1DCDAFUL));
            Assert.That(rng.Next(), Is.EqualTo(0x6E789E6AA1B965F4UL));
            Assert.That(rng.Next(), Is.EqualTo(0x06C45D188009454FUL));
        }

        // ---- determinism and golden ------------------------------------------------

        [Test]
        public void SameSeed_IdenticalOutput_DifferentSeed_DifferentOutput()
        {
            var a = EconomyBalanceSimulator.Run(EconomyParameters.DefaultFor("Coral_Rescue"));
            var b = EconomyBalanceSimulator.Run(EconomyParameters.DefaultFor("Coral_Rescue"));
            Assert.That(b.Fingerprint, Is.EqualTo(a.Fingerprint));
            Assert.That(b.Days.Select(d => d.SumCoins), Is.EqualTo(a.Days.Select(d => d.SumCoins)));
            Assert.That(b.Days.Select(d => d.SumLevelsCompleted), Is.EqualTo(a.Days.Select(d => d.SumLevelsCompleted)));

            var other = EconomyParameters.DefaultFor("Coral_Rescue");
            other.Seed = 7;
            Assert.That(EconomyBalanceSimulator.Run(other).Fingerprint, Is.Not.EqualTo(a.Fingerprint));
        }

        [Test]
        public void CoralRescueDefault_GoldenValues()
        {
            var r = EconomyBalanceSimulator.Run(EconomyParameters.DefaultFor("Coral_Rescue"));
            // Pinned from the first Unity/.NET run at seed 20261111, 200 players, 30 days.
            Assert.That(r.Fingerprint, Is.EqualTo("8ba9c88eaa136d4b92a10a3fc62c7e4e9444cdaf25123704dbf7983340a16b4c"), Describe(r));
            Assert.That(r.NonPayerMeanLevelsX100, Is.EqualTo(19083));
            Assert.That(r.PayerMeanLevelsX100, Is.EqualTo(21810));
            Assert.That(r.PaidPurchases, Is.EqualTo(117));
            Assert.That(r.CoinsEarnedFromWins, Is.EqualTo(2306239));
            Assert.That(r.CoinsEarnedFromEvents, Is.EqualTo(479650));
            Assert.That(r.MaxHeartWaitMinutes, Is.EqualTo(18));
            Assert.That(r.MedianNonPayerTimeToStallDay, Is.EqualTo(26));
            Assert.That(r.NonPayerProgressFractionBp, Is.EqualTo(10000));
        }

        // ---- five profiles ----------------------------------------------------------

        private static IEnumerable<string> GameIds() => GameProfileCatalog.Ids;

        [TestCaseSource(nameof(GameIds))]
        public void DefaultProfile_PassesAllPolicies(string gameId)
        {
            var r = EconomyBalanceSimulator.Run(EconomyParameters.DefaultFor(gameId));
            Assert.That(r.Violations, Is.Empty, Describe(r));
            Assert.That(r.Days.Count, Is.EqualTo(30));
            Assert.That(r.NonPayerProgressFractionBp, Is.GreaterThanOrEqualTo(8000), Describe(r));
        }

        [Test]
        public void WonderPopDefault_HasNoPaidFlowFixedRewardsAndShortHeartWait()
        {
            var r = EconomyBalanceSimulator.Run(EconomyParameters.DefaultFor("Wonder_Pop"));
            Assert.That(r.ChildSafe, Is.True);
            Assert.That(r.Payers, Is.Zero);
            Assert.That(r.PaidPurchases, Is.Zero);
            Assert.That(r.CoinsBought, Is.Zero);
            Assert.That(r.MinWinReward, Is.EqualTo(r.MaxWinReward));
            Assert.That(r.MaxHeartWaitMinutes, Is.LessThanOrEqualTo(15));
        }

        // ---- accounting invariants ------------------------------------------------

        [TestCaseSource(nameof(GameIds))]
        public void CoinLedgerBalances_AndHeartsStayInRange(string gameId)
        {
            var p = EconomyParameters.DefaultFor(gameId);
            var r = EconomyBalanceSimulator.Run(p);
            var last = r.Days[r.Days.Count - 1];
            Assert.That(last.SumCoins,
                Is.EqualTo((long)p.StartingCoins * p.Players + r.CoinsEarned + r.CoinsBought - r.CoinsSpent));
            Assert.That(r.Days.All(d => d.SumHearts >= 0 && d.SumHearts <= (long)p.MaxHearts * p.Players), Is.True);
            Assert.That(r.Days.All(d => d.SumCoins >= 0 && d.SumBoosters >= 0), Is.True);
            Assert.That(r.Days.Zip(r.Days.Skip(1), (x, y) => y.SumLevelsCompleted >= x.SumLevelsCompleted).All(ok => ok), Is.True);
            Assert.That(r.Payers + r.NonPayers, Is.EqualTo(p.Players));
        }

        // ---- policy violations -------------------------------------------------------

        [Test]
        public void PayToProgressWall_Detected()
        {
            var p = EconomyParameters.DefaultFor("Dino_Islands");
            p.BaseWinBp = 1500;
            p.MinWinBp = 300;
            p.MaxHearts = 1;
            p.HeartRegenMinutes = 720;
            p.HeartRefillCoinCost = 5000;
            p.StartingCoins = 0;
            p.StartingBoosters = 0;
            p.DailyLoginCoins = 0;
            p.PayerFractionBp = 2000;
            p.PaidCoinPack = 5000;
            var r = EconomyBalanceSimulator.Run(p);
            Assert.That(r.Violations, Does.Contain(V.PayToProgressWall), Describe(r));
            Assert.That(r.PayerMeanLevelsX100, Is.GreaterThan(r.NonPayerMeanLevelsX100), Describe(r));
            Assert.That(r.MedianNonPayerTimeToStallDay, Is.InRange(0, 29), Describe(r));
        }

        [Test]
        public void AllPlayersPaying_IsAlsoAPayWall()
        {
            var p = EconomyParameters.DefaultFor("Coral_Rescue");
            p.PayerFractionBp = 10000;
            var r = EconomyBalanceSimulator.Run(p);
            Assert.That(r.NonPayers, Is.Zero);
            Assert.That(r.Violations, Does.Contain(V.PayToProgressWall));
        }

        [Test]
        public void ChildPaidCurrencyFlow_Detected()
        {
            var p = EconomyParameters.DefaultFor("Wonder_Pop");
            p.PaidCurrencyEnabled = true;
            p.PayerFractionBp = 5000;
            p.MaxHearts = 1;
            p.BaseWinBp = 2000;
            p.StartingCoins = 0;
            p.HeartRefillCoinCost = 2000;
            p.PaidCoinPack = 5000;
            var r = EconomyBalanceSimulator.Run(p);
            Assert.That(r.Violations, Does.Contain(V.ChildPaidCurrencyFlow), Describe(r));
            Assert.That(r.PaidPurchases, Is.GreaterThan(0), Describe(r));
        }

        [Test]
        public void ChildPolicy_IsNotRelaxableForWonderPop()
        {
            var p = EconomyParameters.DefaultFor("Wonder_Pop");
            p.ChildSafe = false;
            p.WinCoinsRandomBonus = 30;
            var r = EconomyBalanceSimulator.Run(p);
            Assert.That(r.ChildSafe, Is.True);
            Assert.That(r.Violations, Does.Contain(V.ChildRandomReward), Describe(r));
        }

        [Test]
        public void ChildRandomReward_Detected_ButAllowedForGeneralProfile()
        {
            var child = EconomyParameters.DefaultFor("Wonder_Pop");
            child.WinCoinsRandomBonus = 10;
            var rc = EconomyBalanceSimulator.Run(child);
            Assert.That(rc.Violations, Does.Contain(V.ChildRandomReward), Describe(rc));
            Assert.That(rc.MaxWinReward, Is.GreaterThan(rc.MinWinReward));

            var general = EconomyBalanceSimulator.Run(EconomyParameters.DefaultFor("Coral_Rescue"));
            Assert.That(general.MaxWinReward, Is.GreaterThan(general.MinWinReward));
            Assert.That(general.Violations, Does.Not.Contain(V.ChildRandomReward));
        }

        [Test]
        public void ChildHeartWaitExceeded_Detected()
        {
            var p = EconomyParameters.DefaultFor("Wonder_Pop");
            p.HeartRegenMinutes = 60;
            p.MaxHearts = 3;
            p.SessionMinutes = 60;
            p.HeartRefillCoinCost = 100000;
            p.BaseWinBp = 3000;
            var r = EconomyBalanceSimulator.Run(p);
            Assert.That(r.Violations, Does.Contain(V.ChildHeartWaitExceeded), Describe(r));
            Assert.That(r.MaxHeartWaitMinutes, Is.GreaterThan(15));
        }

        [Test]
        public void ChildHeartWaitCap_IsConfigurable()
        {
            var p = EconomyParameters.DefaultFor("Wonder_Pop");
            p.ChildMaxHeartWaitMinutes = 5;
            p.MaxHearts = 2;
            p.SessionMinutes = 60;
            p.HeartRefillCoinCost = 100000;
            p.BaseWinBp = 3000;
            var r = EconomyBalanceSimulator.Run(p);
            Assert.That(r.MaxHeartWaitMinutes, Is.GreaterThan(5), Describe(r));
            Assert.That(r.Violations, Does.Contain(V.ChildHeartWaitExceeded));
        }

        [Test]
        public void EventIncentiveUnbounded_Detected()
        {
            var p = EconomyParameters.DefaultFor("The_Fractured_Realms");
            p.EventWinBonusCoins = 200;
            var r = EconomyBalanceSimulator.Run(p);
            Assert.That(r.Violations, Does.Contain(V.EventIncentiveUnbounded), Describe(r));
            Assert.That(r.CoinsEarnedFromEvents * 2, Is.GreaterThan(r.CoinsEarnedFromWins));

            p.EventEveryNDays = 0;
            p.EventLengthDays = 0;
            var none = EconomyBalanceSimulator.Run(p);
            Assert.That(none.CoinsEarnedFromEvents, Is.Zero);
            Assert.That(none.Violations, Does.Not.Contain(V.EventIncentiveUnbounded));
        }

        // ---- outputs ---------------------------------------------------------------

        [Test]
        public void Outputs_ReportInflationSinksAndPerDayBalances()
        {
            var r = EconomyBalanceSimulator.Run(EconomyParameters.DefaultFor("Garage_Legends"));
            Assert.That(r.Days.Count, Is.EqualTo(30));
            Assert.That(r.Days.Count(d => d.EventActive), Is.EqualTo(14)); // days 0-2, 7-9, 14-16, 21-23, 28-29
            Assert.That(r.CoinsEarned, Is.GreaterThan(0));
            Assert.That(r.CoinsSpent, Is.GreaterThan(0));
            Assert.That(r.SinkToEarnRatio, Is.GreaterThan(0).And.LessThan(10));
            var expectedInflation = (r.Days[29].MeanCoins - 500) / 30;
            Assert.That(r.CoinInflationPerDay, Is.EqualTo(expectedInflation));
        }

        [Test]
        public void InvalidParameters_Rejected()
        {
            var p = EconomyParameters.DefaultFor("Coral_Rescue");
            p.Days = 0;
            Assert.Throws<ArgumentOutOfRangeException>(() => EconomyBalanceSimulator.Run(p));
            var q = EconomyParameters.DefaultFor("Coral_Rescue");
            q.PayerFractionBp = 10001;
            Assert.Throws<ArgumentOutOfRangeException>(() => EconomyBalanceSimulator.Run(q));
            Assert.Throws<ArgumentException>(() => EconomyParameters.DefaultFor("Unknown_Game"));
            var g = EconomyParameters.DefaultFor("Coral_Rescue");
            g.GameId = "Unknown_Game";
            Assert.Throws<ArgumentException>(() => EconomyBalanceSimulator.Run(g));
            Assert.Throws<ArgumentNullException>(() => EconomyBalanceSimulator.Run(null));
        }

        [Test]
        public void Run_DoesNotMutateInputParameters()
        {
            var p = EconomyParameters.DefaultFor("Wonder_Pop");
            p.ChildSafe = false;
            EconomyBalanceSimulator.Run(p);
            Assert.That(p.ChildSafe, Is.False);
        }
    }
}
