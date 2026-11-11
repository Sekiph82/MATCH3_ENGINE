using System;
using NUnit.Framework;
using Match3.Engine.Runtime.LiveOps;

namespace Match3.Engine.Tests.Editor
{
    public class OffersAndPricingGateTests
    {
        // OfferRecord tests
        [Test]
        public void OfferRecord_Create_NullGameId_Throws()
        {
            Assert.Throws<ArgumentException>(() => OfferRecord.Create(null, OfferType.StarterBundle, true, false, false));
        }

        [Test]
        public void OfferRecord_Create_EmptyGameId_Throws()
        {
            Assert.Throws<ArgumentException>(() => OfferRecord.Create("", OfferType.StarterBundle, true, false, false));
        }

        [Test]
        public void OfferRecord_Create_Valid_Succeeds()
        {
            var r = OfferRecord.Create("game1", OfferType.ShopPromotion, true, false, false);
            Assert.AreEqual("game1", r.GameId);
            Assert.AreEqual(OfferType.ShopPromotion, r.Type);
        }

        [Test]
        public void OfferRecord_RequiresStoreAuth_RealMoney_NoAuth_True()
        {
            var r = OfferRecord.Create("game1", OfferType.StarterBundle, true, true, false);
            Assert.IsTrue(r.RequiresStoreAuth);
        }

        [Test]
        public void OfferRecord_RequiresStoreAuth_NotRealMoney_False()
        {
            var r = OfferRecord.Create("game1", OfferType.BonusBoosterEvent, true, false, false);
            Assert.IsFalse(r.RequiresStoreAuth);
        }

        [Test]
        public void OfferRecord_RequiresStoreAuth_RealMoney_HasAuth_False()
        {
            var r = OfferRecord.Create("game1", OfferType.StarterBundle, true, true, true);
            Assert.IsFalse(r.RequiresStoreAuth);
        }

        [Test]
        public void OfferRecord_IsEligibleForPresentation_Approved_NotRealMoney_True()
        {
            var r = OfferRecord.Create("game1", OfferType.ContentAvailability, true, false, false);
            Assert.IsTrue(r.IsEligibleForPresentation);
        }

        [Test]
        public void OfferRecord_IsEligibleForPresentation_Approved_RealMoney_HasAuth_True()
        {
            var r = OfferRecord.Create("game1", OfferType.LimitedTimeBundle, true, true, true);
            Assert.IsTrue(r.IsEligibleForPresentation);
        }

        [Test]
        public void OfferRecord_IsEligibleForPresentation_NotApproved_False()
        {
            var r = OfferRecord.Create("game1", OfferType.SeasonalOffer, false, false, false);
            Assert.IsFalse(r.IsEligibleForPresentation);
        }

        [Test]
        public void OfferRecord_IsEligibleForPresentation_RealMoney_NoAuth_False()
        {
            var r = OfferRecord.Create("game1", OfferType.StarterBundle, true, true, false);
            Assert.IsFalse(r.IsEligibleForPresentation);
        }

        // OfferPolicy tests
        [Test]
        public void OfferPolicy_Safe_Values_Correct()
        {
            var p = OfferPolicy.Safe("test policy");
            Assert.IsTrue(p.RequireOwnerApproval);
            Assert.IsTrue(p.BlockUnapprovedRealMoney);
            Assert.AreEqual("test policy", p.PolicyDescription);
        }

        [Test]
        public void OfferPolicy_Default_NotNull()
        {
            var p = OfferPolicy.Default;
            Assert.IsTrue(p.RequireOwnerApproval);
            Assert.IsTrue(p.BlockUnapprovedRealMoney);
            Assert.IsNotEmpty(p.PolicyDescription);
        }

        [Test]
        public void OfferPolicy_NullDescription_Throws()
        {
            Assert.Throws<ArgumentException>(() => OfferPolicy.Safe(null));
        }

        // CanPresentOffer tests
        [Test]
        public void CanPresentOffer_WonderPop_RealMoney_NoStoreAuth_Guard1()
        {
            var record = OfferRecord.Create("Wonder_Pop", OfferType.StarterBundle, true, true, false);
            var policy = OfferPolicy.Default;
            var result = OffersAndPricingGate.CanPresentOffer("Wonder_Pop", record, policy, out string reason);
            Assert.IsFalse(result);
            Assert.AreEqual("WonderPopRealMoneyRequiresStoreAuth", reason);
        }

        [Test]
        public void CanPresentOffer_WonderPop_RealMoney_HasStoreAuth_Approved_Eligible()
        {
            var record = OfferRecord.Create("Wonder_Pop", OfferType.StarterBundle, true, true, true);
            var policy = OfferPolicy.Default;
            var result = OffersAndPricingGate.CanPresentOffer("Wonder_Pop", record, policy, out string reason);
            Assert.IsTrue(result);
            Assert.AreEqual(string.Empty, reason);
        }

        [Test]
        public void CanPresentOffer_WonderPop_FreeOffer_NotApproved_Guard3()
        {
            var record = OfferRecord.Create("Wonder_Pop", OfferType.BonusBoosterEvent, false, false, false);
            var policy = OfferPolicy.Default;
            var result = OffersAndPricingGate.CanPresentOffer("Wonder_Pop", record, policy, out string reason);
            Assert.IsFalse(result);
            Assert.AreEqual("OfferRequiresOwnerApproval", reason);
        }

        [Test]
        public void CanPresentOffer_WonderPop_FreeOffer_Approved_Eligible()
        {
            var record = OfferRecord.Create("Wonder_Pop", OfferType.ContentAvailability, true, false, false);
            var policy = OfferPolicy.Default;
            var result = OffersAndPricingGate.CanPresentOffer("Wonder_Pop", record, policy, out string reason);
            Assert.IsTrue(result);
            Assert.AreEqual(string.Empty, reason);
        }

        [Test]
        public void CanPresentOffer_WonderPop_Lowercase_RealMoney_NoAuth_Guard2()
        {
            // "wonder_pop" (lowercase) does NOT match Guard 1 (Ordinal), falls through to Guard 2
            var record = OfferRecord.Create("wonder_pop", OfferType.StarterBundle, true, true, false);
            var policy = OfferPolicy.Default;
            var result = OffersAndPricingGate.CanPresentOffer("wonder_pop", record, policy, out string reason);
            Assert.IsFalse(result);
            Assert.AreEqual("RealMoneyOfferRequiresStoreAuth", reason);
        }

        [Test]
        public void CanPresentOffer_NonWonderPop_RealMoney_NoStoreAuth_Guard2()
        {
            var record = OfferRecord.Create("Coral_Rescue", OfferType.LimitedTimeBundle, true, true, false);
            var policy = OfferPolicy.Default;
            var result = OffersAndPricingGate.CanPresentOffer("Coral_Rescue", record, policy, out string reason);
            Assert.IsFalse(result);
            Assert.AreEqual("RealMoneyOfferRequiresStoreAuth", reason);
        }

        [Test]
        public void CanPresentOffer_NonWonderPop_RealMoney_HasAuth_NotApproved_Guard3()
        {
            var record = OfferRecord.Create("Dino_Islands", OfferType.ShopPromotion, false, true, true);
            var policy = OfferPolicy.Default;
            var result = OffersAndPricingGate.CanPresentOffer("Dino_Islands", record, policy, out string reason);
            Assert.IsFalse(result);
            Assert.AreEqual("OfferRequiresOwnerApproval", reason);
        }

        [Test]
        public void CanPresentOffer_NonWonderPop_RealMoney_HasAuth_Approved_Eligible()
        {
            var record = OfferRecord.Create("Dino_Islands", OfferType.ShopPromotion, true, true, true);
            var policy = OfferPolicy.Default;
            var result = OffersAndPricingGate.CanPresentOffer("Dino_Islands", record, policy, out string reason);
            Assert.IsTrue(result);
            Assert.AreEqual(string.Empty, reason);
        }

        [Test]
        public void CanPresentOffer_NonWonderPop_FreeOffer_NotApproved_Guard3()
        {
            var record = OfferRecord.Create("Garage_Legends", OfferType.SeasonalOffer, false, false, false);
            var policy = OfferPolicy.Default;
            var result = OffersAndPricingGate.CanPresentOffer("Garage_Legends", record, policy, out string reason);
            Assert.IsFalse(result);
            Assert.AreEqual("OfferRequiresOwnerApproval", reason);
        }

        [Test]
        public void CanPresentOffer_NonWonderPop_FreeOffer_Approved_Eligible()
        {
            var record = OfferRecord.Create("Garage_Legends", OfferType.BonusBoosterEvent, true, false, false);
            var policy = OfferPolicy.Default;
            var result = OffersAndPricingGate.CanPresentOffer("Garage_Legends", record, policy, out string reason);
            Assert.IsTrue(result);
            Assert.AreEqual(string.Empty, reason);
        }

        [Test]
        public void CanPresentOffer_NoPolicyRequirements_Eligible()
        {
            // Non-Wonder_Pop, real-money, no auth, not approved: only policy guards could block it.
            var record = OfferRecord.Create("SomeGame", OfferType.StarterBundle, false, true, false);
            var policy = new OfferPolicy(false, false, "open policy");
            var result = OffersAndPricingGate.CanPresentOffer("SomeGame", record, policy, out string reason);
            Assert.IsTrue(result);
            Assert.AreEqual(string.Empty, reason);
        }

        // Helper method tests
        [Test]
        public void RequiresStoreAuth_Helper_True()
        {
            var r = OfferRecord.Create("game1", OfferType.StarterBundle, true, true, false);
            Assert.IsTrue(OffersAndPricingGate.RequiresStoreAuth(r));
        }

        [Test]
        public void RequiresStoreAuth_Helper_False()
        {
            var r = OfferRecord.Create("game1", OfferType.StarterBundle, true, false, false);
            Assert.IsFalse(OffersAndPricingGate.RequiresStoreAuth(r));
        }

        [Test]
        public void IsEligibleForPresentation_Helper_True()
        {
            var r = OfferRecord.Create("game1", OfferType.ContentAvailability, true, false, false);
            Assert.IsTrue(OffersAndPricingGate.IsEligibleForPresentation(r));
        }

        [Test]
        public void IsEligibleForPresentation_Helper_False()
        {
            var r = OfferRecord.Create("game1", OfferType.ContentAvailability, false, false, false);
            Assert.IsFalse(OffersAndPricingGate.IsEligibleForPresentation(r));
        }

        // OfferLog tests
        [Test]
        public void OfferLog_Record_And_Count()
        {
            var log = new OfferLog();
            log.Record("game1", "StarterBundle");
            log.Record("game2", "ShopPromotion");
            Assert.AreEqual(2, log.Count);
        }

        [Test]
        public void OfferLog_HasEntry_True()
        {
            var log = new OfferLog();
            log.Record("game1", "StarterBundle");
            Assert.IsTrue(log.HasEntry("game1"));
        }

        [Test]
        public void OfferLog_Clear_ResetsCount()
        {
            var log = new OfferLog();
            log.Record("game1", "StarterBundle");
            log.Clear();
            Assert.AreEqual(0, log.Count);
        }

        [Test]
        public void OfferLog_Record_NullGameId_Throws()
        {
            var log = new OfferLog();
            Assert.Throws<ArgumentException>(() => log.Record(null, "StarterBundle"));
        }

        [Test]
        public void OfferLog_Record_NullOrEmptyOfferType_Throws()
        {
            var log = new OfferLog();
            Assert.Throws<ArgumentException>(() => log.Record("game1", null));
            Assert.Throws<ArgumentException>(() => log.Record("game1", ""));
        }

        [Test]
        public void CanPresentOffer_DefaultStructPolicy_FailsClosed()
        {
            var record = OfferRecord.Create("Coral_Rescue", OfferType.LimitedTimeBundle, true, true, false);
            Assert.IsFalse(OffersAndPricingGate.CanPresentOffer("Coral_Rescue", record, OfferPolicy.Default, out string expected));
            Assert.IsFalse(OffersAndPricingGate.CanPresentOffer("Coral_Rescue", record, default(OfferPolicy), out string reason));
            Assert.AreEqual(expected, reason);
            Assert.AreEqual("RealMoneyOfferRequiresStoreAuth", reason);
        }

        [Test]
        public void CanPresentOffer_GameIdMismatch_Blocked()
        {
            var record = OfferRecord.Create("Wonder_Pop", OfferType.ContentAvailability, true, false, false);
            Assert.IsFalse(OffersAndPricingGate.CanPresentOffer("Coral_Rescue", record, OfferPolicy.Default, out string reason));
            Assert.AreEqual("GameIdMismatch", reason);
        }
    }
}
