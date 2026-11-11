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
            var r = OfferRecord.Create("coral_rescue", OfferType.ShopPromotion, true, false, false);
            Assert.AreEqual("coral_rescue", r.GameId);
            Assert.AreEqual(OfferType.ShopPromotion, r.Type);
        }

        [Test]
        public void OfferRecord_RequiresStoreAuth_RealMoney_NoAuth_True()
        {
            var r = OfferRecord.Create("coral_rescue", OfferType.StarterBundle, true, true, false);
            Assert.IsTrue(r.RequiresStoreAuth);
        }

        [Test]
        public void OfferRecord_RequiresStoreAuth_NotRealMoney_False()
        {
            var r = OfferRecord.Create("coral_rescue", OfferType.BonusBoosterEvent, true, false, false);
            Assert.IsFalse(r.RequiresStoreAuth);
        }

        [Test]
        public void OfferRecord_RequiresStoreAuth_RealMoney_HasAuth_False()
        {
            var r = OfferRecord.Create("coral_rescue", OfferType.StarterBundle, true, true, true);
            Assert.IsFalse(r.RequiresStoreAuth);
        }

        [Test]
        public void OfferRecord_IsEligibleForPresentation_Approved_NotRealMoney_True()
        {
            var r = OfferRecord.Create("coral_rescue", OfferType.ContentAvailability, true, false, false);
            Assert.IsTrue(r.IsEligibleForPresentation);
        }

        [Test]
        public void OfferRecord_IsEligibleForPresentation_Approved_RealMoney_HasAuth_True()
        {
            var r = OfferRecord.Create("coral_rescue", OfferType.LimitedTimeBundle, true, true, true);
            Assert.IsTrue(r.IsEligibleForPresentation);
        }

        [Test]
        public void OfferRecord_IsEligibleForPresentation_NotApproved_False()
        {
            var r = OfferRecord.Create("coral_rescue", OfferType.SeasonalOffer, false, false, false);
            Assert.IsFalse(r.IsEligibleForPresentation);
        }

        [Test]
        public void OfferRecord_IsEligibleForPresentation_RealMoney_NoAuth_False()
        {
            var r = OfferRecord.Create("coral_rescue", OfferType.StarterBundle, true, true, false);
            Assert.IsFalse(r.IsEligibleForPresentation);
        }

        // OfferPolicy tests
        [Test]
        public void OfferPolicy_Safe_Values_Correct()
        {
            var p = OfferPolicy.Safe("test policy");
            Assert.AreEqual("test policy", p.PolicyDescription);
        }

        [Test]
        public void OfferPolicy_Default_NotNull()
        {
            var p = OfferPolicy.Default;
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
        public void CanPresentOffer_WonderPop_Lowercase_RealMoney_NoAuth_UnknownGame()
        {
            // Ordinal catalog lookup: a case variant of Wonder_Pop is not a catalog ID, so it fails closed with UnknownGame (never eligible, never the Wonder_Pop reason).
            var record = OfferRecord.Create("wonder_pop", OfferType.StarterBundle, true, true, false);
            var policy = OfferPolicy.Default;
            var result = OffersAndPricingGate.CanPresentOffer("wonder_pop", record, policy, out string reason);
            Assert.IsFalse(result);
            Assert.AreEqual("UnknownGame", reason);
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
        public void CanPresentOffer_AnyPolicy_RealMoneyNoAuth_AlwaysBlocked()
        {
            var record = OfferRecord.Create("Coral_Rescue", OfferType.StarterBundle, true, true, false);
            Assert.IsFalse(OffersAndPricingGate.CanPresentOffer("Coral_Rescue", record, new OfferPolicy("custom policy"), out string reason));
            Assert.AreEqual("RealMoneyOfferRequiresStoreAuth", reason);
            Assert.IsFalse(OffersAndPricingGate.CanPresentOffer("Coral_Rescue", record, default(OfferPolicy), out reason));
            Assert.AreEqual("RealMoneyOfferRequiresStoreAuth", reason);
        }

        [Test]
        public void CanPresentOffer_AnyPolicy_NotOwnerApproved_AlwaysBlocked()
        {
            var record = OfferRecord.Create("Coral_Rescue", OfferType.ShopPromotion, false, false, false);
            Assert.IsFalse(OffersAndPricingGate.CanPresentOffer("Coral_Rescue", record, new OfferPolicy("custom policy"), out string reason));
            Assert.AreEqual("OfferRequiresOwnerApproval", reason);
            Assert.IsFalse(OffersAndPricingGate.CanPresentOffer("Coral_Rescue", record, default(OfferPolicy), out reason));
            Assert.AreEqual("OfferRequiresOwnerApproval", reason);
        }

        [Test]
        public void CanPresentOffer_WonderPop_RealMoneyNoAuth_MostRelaxedPolicy_Blocked()
        {
            var record = OfferRecord.Create("Wonder_Pop", OfferType.StarterBundle, true, true, false);
            Assert.IsFalse(OffersAndPricingGate.CanPresentOffer("Wonder_Pop", record, new OfferPolicy("most relaxed constructible policy"), out string reason));
            Assert.AreEqual("WonderPopRealMoneyRequiresStoreAuth", reason);
            Assert.IsFalse(OffersAndPricingGate.CanPresentOffer("Wonder_Pop", record, default(OfferPolicy), out reason));
            Assert.AreEqual("WonderPopRealMoneyRequiresStoreAuth", reason);
        }

        // Helper method tests
        [Test]
        public void RequiresStoreAuth_Helper_True()
        {
            var r = OfferRecord.Create("coral_rescue", OfferType.StarterBundle, true, true, false);
            Assert.IsTrue(OffersAndPricingGate.RequiresStoreAuth(r));
        }

        [Test]
        public void RequiresStoreAuth_Helper_False()
        {
            var r = OfferRecord.Create("coral_rescue", OfferType.StarterBundle, true, false, false);
            Assert.IsFalse(OffersAndPricingGate.RequiresStoreAuth(r));
        }

        [Test]
        public void IsEligibleForPresentation_Helper_True()
        {
            var r = OfferRecord.Create("coral_rescue", OfferType.ContentAvailability, true, false, false);
            Assert.IsTrue(OffersAndPricingGate.IsEligibleForPresentation(r));
        }

        [Test]
        public void IsEligibleForPresentation_Helper_False()
        {
            var r = OfferRecord.Create("coral_rescue", OfferType.ContentAvailability, false, false, false);
            Assert.IsFalse(OffersAndPricingGate.IsEligibleForPresentation(r));
        }

        // OfferLog tests
        [Test]
        public void OfferLog_Record_And_Count()
        {
            var log = new OfferLog();
            log.Record("coral_rescue", "StarterBundle");
            log.Record("game2", "ShopPromotion");
            Assert.AreEqual(2, log.Count);
        }

        [Test]
        public void OfferLog_HasEntry_True()
        {
            var log = new OfferLog();
            log.Record("coral_rescue", "StarterBundle");
            Assert.IsTrue(log.HasEntry("coral_rescue"));
        }

        [Test]
        public void OfferLog_Clear_ResetsCount()
        {
            var log = new OfferLog();
            log.Record("coral_rescue", "StarterBundle");
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
            Assert.Throws<ArgumentException>(() => log.Record("coral_rescue", null));
            Assert.Throws<ArgumentException>(() => log.Record("coral_rescue", ""));
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

        [Test]
        public void CanPresentOffer_WonderPopParam_RecordDiffers_GameIdMismatch()
        {
            var record = OfferRecord.Create("Coral_Rescue", OfferType.StarterBundle, true, true, false);
            Assert.IsFalse(OffersAndPricingGate.CanPresentOffer("Wonder_Pop", record, OfferPolicy.Default, out string reason));
            Assert.AreEqual("GameIdMismatch", reason);
        }

        [Test]
        public void OfferLog_HasEntry_IsOrdinal()
        {
            var log = new OfferLog();
            log.Record("coral_rescue", "StarterBundle");
            Assert.IsFalse(log.HasEntry("CORAL_RESCUE"));
            Assert.IsFalse(log.HasEntry("coral"));
            // Soft hyphen (U+00AD) is ignorable under culture-sensitive comparison; Ordinal must not match it.
            var softHyphenLog = new OfferLog();
            softHyphenLog.Record("Coral\u00AD_Rescue", "evt");
            Assert.IsFalse(softHyphenLog.HasEntry("Coral_Rescue"));
        }

        [Test]
        public void OfferPolicy_Ctor_NullOrEmptyDescription_Throws()
        {
            Assert.Throws<ArgumentException>(() => new OfferPolicy(null));
            Assert.Throws<ArgumentException>(() => new OfferPolicy(""));
        }


        [Test]
        public void NullOrEmptyGameId_DefaultRecord_GameIdMissing()
        {
            foreach (var gameId in new[] { null, string.Empty })
            {
                Assert.IsFalse(OffersAndPricingGate.CanPresentOffer(gameId, default(OfferRecord), default(OfferPolicy), out var reason));
                Assert.AreEqual("GameIdMissing", reason);
            }
        }

        [Test]
        public void UnknownOrCaseVariantGameId_DefaultRecord_UnknownGame()
        {
            foreach (var gameId in new[] { "Unknown_Game", "coral_rescue", "CORAL_RESCUE", " Coral_Rescue", "Coral_Rescue ", "wonder_pop", "WONDER_POP" })
            {
                Assert.IsFalse(OffersAndPricingGate.CanPresentOffer(gameId, default(OfferRecord), default(OfferPolicy), out var reason));
                Assert.AreEqual("UnknownGame", reason);
            }
        }
    }
}
