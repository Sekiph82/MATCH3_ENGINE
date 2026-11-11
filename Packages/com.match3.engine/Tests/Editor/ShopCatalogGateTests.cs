using NUnit.Framework;
using System;
using Match3.Engine.Runtime.Economy;

namespace Match3.Engine.Tests.Editor
{
    [TestFixture]
    public class ShopCatalogGateTests
    {
        private const string GameA = "Game_A";
        private const string WonderPop = "Wonder_Pop";

        // --- ShopOffer ---

        [Test]
        public void ShopOffer_Active_IsEnabled()
        {
            var offer = ShopOffer.Active("o1", GameA, OfferType.CoinPack);
            Assert.IsTrue(offer.IsEnabled);
        }

        [Test]
        public void ShopOffer_Disabled_IsNotEnabled()
        {
            var offer = ShopOffer.Disabled("o1", GameA, OfferType.CoinPack);
            Assert.IsFalse(offer.IsEnabled);
        }

        [Test]
        public void ShopOffer_NullOfferId_Throws()
        {
            Assert.Throws<ArgumentException>(() => ShopOffer.Active(null, GameA, OfferType.CoinPack));
        }

        [Test]
        public void ShopOffer_EmptyGameId_Throws()
        {
            Assert.Throws<ArgumentException>(() => ShopOffer.Active("o1", "", OfferType.CoinPack));
        }

        // --- ShopCatalogPolicy ---

        [Test]
        public void Policy_Enabled_OffersEnabled_True()
        {
            var p = ShopCatalogPolicy.Enabled("Active");
            Assert.IsTrue(p.OffersEnabled);
        }

        [Test]
        public void Policy_Disabled_OffersEnabled_False()
        {
            var p = ShopCatalogPolicy.Disabled("Inactive");
            Assert.IsFalse(p.OffersEnabled);
        }

        [Test]
        public void Policy_AlwaysPlatformTaxTrue()
        {
            var e = ShopCatalogPolicy.Enabled("x");
            var d = ShopCatalogPolicy.Disabled("x");
            Assert.IsTrue(e.PlatformTaxHandledByStore);
            Assert.IsTrue(d.PlatformTaxHandledByStore);
        }

        [Test]
        public void Policy_Default_OffersDisabled()
        {
            Assert.IsFalse(ShopCatalogPolicy.Default.OffersEnabled);
        }

        [Test]
        public void Policy_NullDescription_Throws()
        {
            Assert.Throws<ArgumentException>(() => ShopCatalogPolicy.Enabled(null));
        }

        // --- ShopCatalogGate ---

        [Test]
        public void CanShowOffer_WonderPop_IneligibleFirst()
        {
            var policy = ShopCatalogPolicy.Enabled("on");
            var offer = ShopOffer.Active("o1", WonderPop, OfferType.CoinPack);
            var result = ShopCatalogGate.CanShowOffer(WonderPop, offer, policy);
            Assert.IsFalse(result.IsEligible);
            Assert.AreEqual("WonderPopShopProhibited", result.Reason);
        }

        [Test]
        public void CanShowOffer_PolicyDisabled_Ineligible()
        {
            var policy = ShopCatalogPolicy.Disabled("off");
            var offer = ShopOffer.Active("o1", GameA, OfferType.CoinPack);
            var result = ShopCatalogGate.CanShowOffer(GameA, offer, policy);
            Assert.IsFalse(result.IsEligible);
            Assert.AreEqual("ShopOffersDisabled", result.Reason);
        }

        [Test]
        public void CanShowOffer_OfferDisabled_Ineligible()
        {
            var policy = ShopCatalogPolicy.Enabled("on");
            var offer = ShopOffer.Disabled("o1", GameA, OfferType.CoinPack);
            var result = ShopCatalogGate.CanShowOffer(GameA, offer, policy);
            Assert.IsFalse(result.IsEligible);
            Assert.AreEqual("OfferDisabled", result.Reason);
        }

        [Test]
        public void CanShowOffer_EligibleWhenAllPass()
        {
            var policy = ShopCatalogPolicy.Enabled("on");
            var offer = ShopOffer.Active("o1", GameA, OfferType.CoinPack);
            var result = ShopCatalogGate.CanShowOffer(GameA, offer, policy);
            Assert.IsTrue(result.IsEligible);
        }

        [Test]
        public void IsPlatformPriceAuthoritative_AlwaysTrue()
        {
            Assert.IsTrue(ShopCatalogGate.IsPlatformPriceAuthoritative(ShopCatalogPolicy.Enabled("x")));
            Assert.IsTrue(ShopCatalogGate.IsPlatformPriceAuthoritative(ShopCatalogPolicy.Disabled("x")));
        }

        [Test]
        public void CanShowNoAds_WonderPop_False()
        {
            Assert.IsFalse(ShopCatalogGate.CanShowNoAds(WonderPop, ShopCatalogPolicy.Enabled("on")));
        }

        [Test]
        public void CanShowNoAds_PolicyDisabled_False()
        {
            Assert.IsFalse(ShopCatalogGate.CanShowNoAds(GameA, ShopCatalogPolicy.Disabled("off")));
        }

        [Test]
        public void CanShowNoAds_OtherGame_PolicyEnabled_True()
        {
            Assert.IsTrue(ShopCatalogGate.CanShowNoAds(GameA, ShopCatalogPolicy.Enabled("on")));
        }

        // --- ShopCatalog ---

        [Test]
        public void ShopCatalog_AddAndCount()
        {
            var catalog = new ShopCatalog();
            catalog.AddOrUpdate(ShopOffer.Active("o1", GameA, OfferType.CoinPack));
            Assert.AreEqual(1, catalog.Count);
        }

        [Test]
        public void ShopCatalog_AddOrUpdate_Overwrites()
        {
            var catalog = new ShopCatalog();
            catalog.AddOrUpdate(ShopOffer.Active("o1", GameA, OfferType.CoinPack));
            catalog.AddOrUpdate(ShopOffer.Disabled("o1", GameA, OfferType.CoinPack));
            catalog.TryGet("o1", out var stored);
            Assert.IsFalse(stored.IsEnabled);
            Assert.AreEqual(1, catalog.Count);
        }

        [Test]
        public void ShopCatalog_TryGet_Missing_ReturnsFalse()
        {
            var catalog = new ShopCatalog();
            Assert.IsFalse(catalog.TryGet("missing", out _));
        }

        [Test]
        public void ShopCatalog_GetForGame_FiltersDisabled()
        {
            var catalog = new ShopCatalog();
            catalog.AddOrUpdate(ShopOffer.Active("o1", GameA, OfferType.CoinPack));
            catalog.AddOrUpdate(ShopOffer.Disabled("o2", GameA, OfferType.BoosterPack));
            var list = catalog.GetForGame(GameA);
            Assert.AreEqual(1, list.Count);
            Assert.AreEqual("o1", list[0].OfferId);
        }

        [Test]
        public void ShopCatalog_Clear_ResetsCount()
        {
            var catalog = new ShopCatalog();
            catalog.AddOrUpdate(ShopOffer.Active("o1", GameA, OfferType.CoinPack));
            catalog.Clear();
            Assert.AreEqual(0, catalog.Count);
        }

        [Test]
        public void WonderPop_Guard_CaseInsensitiveOrdinal_Does_Not_Bypass()
        {
            // "wonder_pop" lowercase must NOT bypass the Wonder_Pop Ordinal guard
            var policy = ShopCatalogPolicy.Enabled("on");
            var offer = ShopOffer.Active("o1", "wonder_pop", OfferType.NoAdsPermanent);
            var result = ShopCatalogGate.CanShowOffer("wonder_pop", offer, policy);
            // Ordinal compare: "wonder_pop" != "Wonder_Pop" → eligible (not prohibited)
            Assert.IsTrue(result.IsEligible);
        }

        [Test]
        public void WonderPop_ExactId_BlocksNoAds_EvenWithEnabledPolicy()
        {
            var policy = ShopCatalogPolicy.Enabled("on");
            Assert.IsFalse(ShopCatalogGate.CanShowNoAds(WonderPop, policy));
        }
    }
}
