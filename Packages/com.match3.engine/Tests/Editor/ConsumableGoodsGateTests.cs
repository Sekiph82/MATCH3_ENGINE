using System;
using NUnit.Framework;
using Match3.Engine.Runtime.Commerce;

namespace Match3.Engine.Tests.Editor
{
    [TestFixture]
    public class ConsumableGoodsGateTests
    {
        // ConsumablePolicy tests

        [Test]
        public void ConsumablePolicy_NullDescription_Throws()
        {
            Assert.Throws<ArgumentException>(() => ConsumablePolicy.Enabled(null));
        }

        [Test]
        public void ConsumablePolicy_EmptyDescription_Throws()
        {
            Assert.Throws<ArgumentException>(() => ConsumablePolicy.Disabled(string.Empty));
        }

        [Test]
        public void ConsumablePolicy_Default_IsDisabled()
        {
            var p = ConsumablePolicy.Default;
            Assert.IsFalse(p.ConsumablesEnabled);
        }

        [Test]
        public void ConsumablePolicy_RequireServerLedger_AlwaysTrue_WhenEnabled()
        {
            var p = ConsumablePolicy.Enabled("test");
            Assert.IsTrue(p.RequireServerLedger);
        }

        [Test]
        public void ConsumablePolicy_RequireServerLedger_AlwaysTrue_WhenDisabled()
        {
            var p = ConsumablePolicy.Disabled("test");
            Assert.IsTrue(p.RequireServerLedger);
        }

        // ConsumableItem tests

        [Test]
        public void ConsumableItem_NullItemId_Throws()
        {
            Assert.Throws<ArgumentException>(() => ConsumableItem.Bundle(null, "game1", 1));
        }

        [Test]
        public void ConsumableItem_EmptyGameId_Throws()
        {
            Assert.Throws<ArgumentException>(() => ConsumableItem.Bundle("item1", string.Empty, 1));
        }

        [Test]
        public void ConsumableItem_ZeroQuantity_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => ConsumableItem.Bundle("item1", "game1", 0));
        }

        [Test]
        public void ConsumableItem_NegativeQuantity_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => ConsumableItem.Bundle("item1", "game1", -1));
        }

        [Test]
        public void ConsumableItem_Single_HasQuantityOne()
        {
            var item = ConsumableItem.Single("coins_100", "Garage_Legends");
            Assert.AreEqual(1, item.Quantity);
        }

        // ConsumableGoodsGate tests

        [Test]
        public void CheckPurchaseEligibility_WonderPop_AlwaysIneligible_EvenWithEnabledPolicy()
        {
            var policy = ConsumablePolicy.Enabled("enabled");
            var kid = KidProfilePolicy.Unrestricted("ok");
            var result = ConsumableGoodsGate.CheckPurchaseEligibility("Wonder_Pop", policy, kid);
            Assert.IsFalse(result.IsEligible);
            Assert.AreEqual("WonderPopConsumablesProhibited", result.Reason);
        }

        [Test]
        public void CheckPurchaseEligibility_DisabledPolicy_Ineligible()
        {
            var policy = ConsumablePolicy.Default;
            var kid = KidProfilePolicy.Unrestricted("ok");
            var result = ConsumableGoodsGate.CheckPurchaseEligibility("Garage_Legends", policy, kid);
            Assert.IsFalse(result.IsEligible);
            Assert.AreEqual("ConsumablesDisabled", result.Reason);
        }

        [Test]
        public void CheckPurchaseEligibility_KidProfileProhibited_Ineligible()
        {
            var policy = ConsumablePolicy.Enabled("enabled");
            var kid = KidProfilePolicy.Restricted("under review");
            var result = ConsumableGoodsGate.CheckPurchaseEligibility("Garage_Legends", policy, kid);
            Assert.IsFalse(result.IsEligible);
            Assert.AreEqual("KidProfileConsumablesProhibited", result.Reason);
        }

        [Test]
        public void CheckPurchaseEligibility_AllPass_Eligible()
        {
            var policy = ConsumablePolicy.Enabled("enabled");
            var kid = KidProfilePolicy.Unrestricted("ok");
            var result = ConsumableGoodsGate.CheckPurchaseEligibility("Garage_Legends", policy, kid);
            Assert.IsTrue(result.IsEligible);
        }

        [Test]
        public void IsServerLedgerRequired_AlwaysTrue()
        {
            Assert.IsTrue(ConsumableGoodsGate.IsServerLedgerRequired(ConsumablePolicy.Enabled("e")));
            Assert.IsTrue(ConsumableGoodsGate.IsServerLedgerRequired(ConsumablePolicy.Disabled("d")));
        }

        // ConsumptionTracker tests

        [Test]
        public void ConsumptionTracker_MarkAndCheck()
        {
            var tracker = new ConsumptionTracker();
            Assert.IsFalse(tracker.IsConsumed("item_a"));
            tracker.MarkConsumed("item_a");
            Assert.IsTrue(tracker.IsConsumed("item_a"));
        }

        [Test]
        public void ConsumptionTracker_MarkConsumed_NullThrows()
        {
            var tracker = new ConsumptionTracker();
            Assert.Throws<ArgumentException>(() => tracker.MarkConsumed(null));
        }

        [Test]
        public void ConsumptionTracker_IsConsumed_EmptyThrows()
        {
            var tracker = new ConsumptionTracker();
            Assert.Throws<ArgumentException>(() => tracker.IsConsumed(string.Empty));
        }

        [Test]
        public void ConsumptionTracker_DuplicateMark_IsNoOp()
        {
            var tracker = new ConsumptionTracker();
            tracker.MarkConsumed("item_b");
            tracker.MarkConsumed("item_b"); // no-op, no throw
            Assert.AreEqual(1, tracker.Count);
        }

        [Test]
        public void ConsumptionTracker_Reset_ClearsAll()
        {
            var tracker = new ConsumptionTracker();
            tracker.MarkConsumed("item_c");
            tracker.MarkConsumed("item_d");
            tracker.Reset();
            Assert.AreEqual(0, tracker.Count);
            Assert.IsFalse(tracker.IsConsumed("item_c"));
        }

        [Test]
        public void ConsumptionTracker_Count_ReflectsUniqueItems()
        {
            var tracker = new ConsumptionTracker();
            tracker.MarkConsumed("x");
            tracker.MarkConsumed("y");
            tracker.MarkConsumed("x"); // duplicate
            Assert.AreEqual(2, tracker.Count);
        }
    }
}
