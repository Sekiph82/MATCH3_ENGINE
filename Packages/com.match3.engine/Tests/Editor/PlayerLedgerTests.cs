using NUnit.Framework;
using Match3.Engine.Runtime.Economy;
using System;

namespace Match3.Engine.Tests.Editor
{
    [TestFixture]
    public class PlayerLedgerTests
    {
        // LedgerEntry tests
        [Test]
        public void LedgerEntry_Credit_ValidArgs_Succeeds()
        {
            var e = LedgerEntry.Credit("Garage_Legends", CurrencyType.Coins, 100, "tx1");
            Assert.AreEqual(100L, e.Amount);
            Assert.AreEqual("Garage_Legends", e.GameId);
            Assert.AreEqual(CurrencyType.Coins, e.Currency);
        }

        [Test]
        public void LedgerEntry_NullGameId_Throws()
        {
            Assert.Throws<ArgumentException>(() => LedgerEntry.Credit(null, CurrencyType.Coins, 1, "tx1"));
        }

        [Test]
        public void LedgerEntry_EmptyTransactionId_Throws()
        {
            Assert.Throws<ArgumentException>(() => LedgerEntry.Credit("Garage_Legends", CurrencyType.Coins, 1, ""));
        }

        [Test]
        public void LedgerEntry_NegativeAmount_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => LedgerEntry.Credit("Garage_Legends", CurrencyType.Coins, -1, "tx1"));
        }

        [Test]
        public void LedgerEntry_Debit_ZeroAmount_Allowed()
        {
            var e = LedgerEntry.Debit("Garage_Legends", CurrencyType.Hearts, 0, "tx2");
            Assert.AreEqual(0L, e.Amount);
        }

        // LedgerPolicy tests
        [Test]
        public void LedgerPolicy_Default_IsServerAuthoritative()
        {
            Assert.IsTrue(LedgerPolicy.Default.ServerAuthoritative);
        }

        [Test]
        public void LedgerPolicy_Default_AllowsOfflineNonPremium()
        {
            Assert.IsTrue(LedgerPolicy.Default.AllowOfflineNonPremium);
        }

        [Test]
        public void LedgerPolicy_Standard_SetsDescription()
        {
            var p = LedgerPolicy.Standard("test desc");
            Assert.AreEqual("test desc", p.PolicyDescription);
        }

        [Test]
        public void LedgerPolicy_NullDescription_Throws()
        {
            Assert.Throws<ArgumentException>(() => LedgerPolicy.Standard(null));
        }

        // PlayerLedgerGate tests
        [Test]
        public void Gate_CanCreditPremium_WonderPop_IsIneligible()
        {
            var (eligible, reason) = PlayerLedgerGate.CanCreditPremium("Wonder_Pop", LedgerPolicy.Default);
            Assert.IsFalse(eligible);
            Assert.AreEqual("WonderPopPremiumProhibited", reason);
        }

        [Test]
        public void Gate_CanCreditPremium_NormalGame_IsEligible()
        {
            var (eligible, _) = PlayerLedgerGate.CanCreditPremium("Coral_Rescue", LedgerPolicy.Default);
            Assert.IsTrue(eligible);
        }

        [Test]
        public void Gate_IsOfflinePlayAllowed_WonderPop_ReturnsFalse()
        {
            Assert.IsFalse(PlayerLedgerGate.IsOfflinePlayAllowed("Wonder_Pop", LedgerPolicy.Default));
        }

        [Test]
        public void Gate_IsOfflinePlayAllowed_NormalGame_ReturnsTrue()
        {
            Assert.IsTrue(PlayerLedgerGate.IsOfflinePlayAllowed("Dino_Islands", LedgerPolicy.Default));
        }

        [Test]
        public void Gate_RequiresServerSync_AlwaysTrue()
        {
            Assert.IsTrue(PlayerLedgerGate.RequiresServerSync(LedgerPolicy.Default));
        }

        // PlayerWallet tests
        [Test]
        public void Wallet_InitialBalance_IsZero()
        {
            var w = new PlayerWallet();
            Assert.AreEqual(0L, w.GetBalance(CurrencyType.Coins));
        }

        [Test]
        public void Wallet_Credit_IncreasesBalance()
        {
            var w = new PlayerWallet();
            w.Credit(CurrencyType.PremiumGems, 50);
            Assert.AreEqual(50L, w.GetBalance(CurrencyType.PremiumGems));
        }

        [Test]
        public void Wallet_TryDebit_SufficientFunds_Succeeds()
        {
            var w = new PlayerWallet();
            w.Credit(CurrencyType.Boosters, 10);
            Assert.IsTrue(w.TryDebit(CurrencyType.Boosters, 5));
            Assert.AreEqual(5L, w.GetBalance(CurrencyType.Boosters));
        }

        [Test]
        public void Wallet_TryDebit_InsufficientFunds_ReturnsFalse()
        {
            var w = new PlayerWallet();
            w.Credit(CurrencyType.Coins, 3);
            Assert.IsFalse(w.TryDebit(CurrencyType.Coins, 10));
            Assert.AreEqual(3L, w.GetBalance(CurrencyType.Coins));
        }

        [Test]
        public void Wallet_Credit_ZeroAmount_Throws()
        {
            var w = new PlayerWallet();
            Assert.Throws<ArgumentOutOfRangeException>(() => w.Credit(CurrencyType.Hearts, 0));
        }

        [Test]
        public void Wallet_TryDebit_ZeroAmount_Throws()
        {
            var w = new PlayerWallet();
            Assert.Throws<ArgumentOutOfRangeException>(() => w.TryDebit(CurrencyType.Hearts, 0));
        }

        [Test]
        public void Wallet_Reset_ClearsAllBalances()
        {
            var w = new PlayerWallet();
            w.Credit(CurrencyType.Coins, 100);
            w.Credit(CurrencyType.Hearts, 5);
            w.Reset();
            Assert.AreEqual(0L, w.GetBalance(CurrencyType.Coins));
            Assert.AreEqual(0L, w.GetBalance(CurrencyType.Hearts));
        }

        [Test]
        public void Wallet_MultipleCredits_Accumulate()
        {
            var w = new PlayerWallet();
            w.Credit(CurrencyType.Coins, 10);
            w.Credit(CurrencyType.Coins, 20);
            Assert.AreEqual(30L, w.GetBalance(CurrencyType.Coins));
        }

        [Test]
        public void Gate_WonderPop_OtherCasing_IsUnknownGame()
        {
            // Ordinal: "wonder_pop" is not a catalog ID, so it fails closed as UnknownGame
            var (eligible, reason) = PlayerLedgerGate.CanCreditPremium("wonder_pop", LedgerPolicy.Default);
            Assert.IsFalse(eligible);
            Assert.AreEqual("UnknownGame", reason);
        }


        [Test]
        public void CanCreditPremium_NullOrEmptyGameId_FailsClosed()
        {
            foreach (var gameId in new[] { null, string.Empty })
            {
                var (eligible, reason) = PlayerLedgerGate.CanCreditPremium(gameId, LedgerPolicy.Standard("std"));
                Assert.IsFalse(eligible, gameId);
                Assert.AreEqual("GameIdMissing", reason);
            }
        }

        [Test]
        public void CanCreditPremium_NonCatalogGameId_UnknownGame()
        {
            foreach (var unknownId in new[] { "Unknown_Game", "coral_rescue", "CORAL_RESCUE", " Coral_Rescue", "Coral_Rescue " })
            {
                var (eligible, reason) = PlayerLedgerGate.CanCreditPremium(unknownId, LedgerPolicy.Standard("std"));
                Assert.IsFalse(eligible, unknownId);
                Assert.AreEqual("UnknownGame", reason);
            }
        }

        [Test]
        public void IsOfflinePlayAllowed_NullOrEmptyGameId_FailsClosed()
        {
            foreach (var gameId in new[] { null, string.Empty })
            {
                Assert.IsFalse(PlayerLedgerGate.IsOfflinePlayAllowed(gameId, LedgerPolicy.Standard("std")), gameId);
            }
        }

        [Test]
        public void IsOfflinePlayAllowed_NonCatalogGameId_UnknownGame()
        {
            foreach (var unknownId in new[] { "Unknown_Game", "coral_rescue", "CORAL_RESCUE", " Coral_Rescue", "Coral_Rescue " })
            {
                Assert.IsFalse(PlayerLedgerGate.IsOfflinePlayAllowed(unknownId, LedgerPolicy.Standard("std")), unknownId);
            }
        }
    }
}
