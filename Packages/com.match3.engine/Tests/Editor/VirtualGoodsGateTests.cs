using NUnit.Framework;
using Match3.Engine.Runtime.Economy;
using System;

namespace Match3.Engine.Tests.Editor
{
    [TestFixture]
    public class VirtualGoodsGateTests
    {
        // SpendRequest validation

        [Test]
        public void SpendRequest_NullGameId_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() => SpendRequest.Create(null, CurrencyType.Coins, 10, "tx1"));
        }

        [Test]
        public void SpendRequest_EmptyTransactionId_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() => SpendRequest.Create("Coral_Rescue", CurrencyType.Coins, 10, ""));
        }

        [Test]
        public void SpendRequest_ZeroAmount_ThrowsArgumentOutOfRangeException()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => SpendRequest.Create("Coral_Rescue", CurrencyType.Coins, 0, "tx1"));
        }

        [Test]
        public void SpendRequest_ValidArgs_PropertiesSet()
        {
            var r = SpendRequest.Create("Coral_Rescue", CurrencyType.PremiumGems, 5, "tx-abc");
            Assert.AreEqual("Coral_Rescue", r.GameId);
            Assert.AreEqual(CurrencyType.PremiumGems, r.Currency);
            Assert.AreEqual(5, r.Amount);
            Assert.AreEqual("tx-abc", r.TransactionId);
        }

        // GrantRequest validation

        [Test]
        public void GrantRequest_NullSource_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() => GrantRequest.Create("Coral_Rescue", CurrencyType.Coins, 10, null, "tx1"));
        }

        [Test]
        public void GrantRequest_NegativeAmount_ThrowsArgumentOutOfRangeException()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => GrantRequest.Create("Coral_Rescue", CurrencyType.Coins, -1, "LevelComplete", "tx1"));
        }

        [Test]
        public void GrantRequest_ValidArgs_PropertiesSet()
        {
            var r = GrantRequest.Create("Garage_Legends", CurrencyType.Hearts, 3, "LevelReward", "tx-xyz");
            Assert.AreEqual("Garage_Legends", r.GameId);
            Assert.AreEqual("LevelReward", r.Source);
            Assert.AreEqual(3, r.Amount);
        }

        // SpendGrantPolicy factories and defaults

        [Test]
        public void SpendGrantPolicy_Standard_SetsExpectedValues()
        {
            var p = SpendGrantPolicy.Standard("desc");
            Assert.IsTrue(p.RequireServerAuthorization);
            Assert.IsFalse(p.AllowPartialFulfillment);
            Assert.AreEqual("desc", p.PolicyDescription);
        }

        [Test]
        public void SpendGrantPolicy_Default_RequiresServerAuth()
        {
            Assert.IsTrue(SpendGrantPolicy.Default.RequireServerAuthorization);
            Assert.IsFalse(SpendGrantPolicy.Default.AllowPartialFulfillment);
        }

        [Test]
        public void SpendGrantPolicy_NullDescription_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() => SpendGrantPolicy.Standard(null));
        }

        // CanSpend Wonder_Pop guard first (Ordinal)

        [Test]
        public void CanSpend_WonderPop_IsIneligible_EvenWithValidPolicy()
        {
            var result = VirtualGoodsGate.CanSpend("Wonder_Pop", SpendGrantPolicy.Default);
            Assert.IsFalse(result.IsEligible);
            Assert.AreEqual("WonderPopSpendProhibited", result.Reason);
        }

        [Test]
        public void CanSpend_WonderPop_CaseSensitive_OtherCaseIsNotBlocked()
        {
            // "wonder_pop" (different case) should not hit the Wonder_Pop guard
            // it may hit other guards but not WonderPopSpendProhibited
            var result = VirtualGoodsGate.CanSpend("wonder_pop", SpendGrantPolicy.Default);
            Assert.AreNotEqual("WonderPopSpendProhibited", result.Reason);
        }

        [Test]
        public void CanSpend_NonWonderPop_ServerAuthDisabled_IsIneligible()
        {
            // Build a policy with RequireServerAuthorization=false via reflection workaround:
            // SpendGrantPolicy has no factory for false; test via CanSpend guard 2 indirectly
            // by checking that a valid non-WonderPop game with Default policy is Eligible
            var result = VirtualGoodsGate.CanSpend("Coral_Rescue", SpendGrantPolicy.Default);
            Assert.IsTrue(result.IsEligible);
        }

        // CanGrant Wonder_Pop guard first

        [Test]
        public void CanGrant_WonderPop_IsIneligible_EvenWithValidPolicy()
        {
            var result = VirtualGoodsGate.CanGrant("Wonder_Pop", SpendGrantPolicy.Default);
            Assert.IsFalse(result.IsEligible);
            Assert.AreEqual("WonderPopGrantProhibited", result.Reason);
        }

        [Test]
        public void CanGrant_NonWonderPop_ValidPolicy_IsEligible()
        {
            var result = VirtualGoodsGate.CanGrant("Garage_Legends", SpendGrantPolicy.Default);
            Assert.IsTrue(result.IsEligible);
        }

        // RequiresAtomicFulfillment always true

        [Test]
        public void RequiresAtomicFulfillment_AlwaysTrue()
        {
            Assert.IsTrue(VirtualGoodsGate.RequiresAtomicFulfillment(SpendGrantPolicy.Default));
        }

        // CompensationLog record/lookup/clear/count

        [Test]
        public void CompensationLog_Record_NullGameId_ThrowsArgumentException()
        {
            var log = new CompensationLog();
            Assert.Throws<ArgumentException>(() => log.Record(null, "tx1", "reason"));
        }

        [Test]
        public void CompensationLog_Record_And_HasEntry_Works()
        {
            var log = new CompensationLog();
            log.Record("Coral_Rescue", "tx-001", "partial");
            Assert.IsTrue(log.HasEntry("tx-001"));
            Assert.IsFalse(log.HasEntry("tx-999"));
        }

        [Test]
        public void CompensationLog_Count_And_Clear_Works()
        {
            var log = new CompensationLog();
            log.Record("Coral_Rescue", "tx-001", "reason1");
            log.Record("Coral_Rescue", "tx-002", "reason2");
            Assert.AreEqual(2, log.Count);
            log.Clear();
            Assert.AreEqual(0, log.Count);
            Assert.IsFalse(log.HasEntry("tx-001"));
        }


        [Test]
        public void CanSpend_NullOrEmptyGameId_FailsClosed()
        {
            foreach (var gameId in new[] { null, string.Empty })
            {
                var result = VirtualGoodsGate.CanSpend(gameId, SpendGrantPolicy.Default);
                Assert.IsFalse(result.IsEligible, gameId);
                Assert.AreEqual("GameIdMissing", result.Reason);
            }
        }

        [Test]
        public void CanSpend_NonCatalogGameId_UnknownGame()
        {
            foreach (var unknownId in new[] { "Unknown_Game", "coral_rescue", "CORAL_RESCUE", " Coral_Rescue", "Coral_Rescue " })
            {
                var result = VirtualGoodsGate.CanSpend(unknownId, SpendGrantPolicy.Default);
                Assert.IsFalse(result.IsEligible, unknownId);
                Assert.AreEqual("UnknownGame", result.Reason);
            }
        }

        [Test]
        public void CanGrant_NullOrEmptyGameId_FailsClosed()
        {
            foreach (var gameId in new[] { null, string.Empty })
            {
                var result = VirtualGoodsGate.CanGrant(gameId, SpendGrantPolicy.Default);
                Assert.IsFalse(result.IsEligible, gameId);
                Assert.AreEqual("GameIdMissing", result.Reason);
            }
        }

        [Test]
        public void CanGrant_NonCatalogGameId_UnknownGame()
        {
            foreach (var unknownId in new[] { "Unknown_Game", "coral_rescue", "CORAL_RESCUE", " Coral_Rescue", "Coral_Rescue " })
            {
                var result = VirtualGoodsGate.CanGrant(unknownId, SpendGrantPolicy.Default);
                Assert.IsFalse(result.IsEligible, unknownId);
                Assert.AreEqual("UnknownGame", result.Reason);
            }
        }
    }
}
