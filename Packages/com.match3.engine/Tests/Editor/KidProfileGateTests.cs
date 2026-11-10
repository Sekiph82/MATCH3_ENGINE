using System;
using NUnit.Framework;
using Match3.Engine.Runtime;
using Match3.Engine.Runtime.Commerce;

namespace Match3.Engine.Tests.Editor
{
    public sealed class KidProfileGateTests
    {
        [Test]
        public void Policy_NullJustification_Throws()
        {
            Assert.Throws<ArgumentException>(() => new KidProfilePolicy(true, true, null));
            Assert.Throws<ArgumentException>(() => new KidProfilePolicy(true, true, string.Empty));
        }

        [Test]
        public void Policy_Default_IsRestricted()
        {
            var p = KidProfilePolicy.Default;
            Assert.IsTrue(p.AdsProhibited);
            Assert.IsTrue(p.IapProhibited);
            Assert.IsFalse(string.IsNullOrEmpty(p.PolicyJustification));
        }

        [Test]
        public void Policy_Restricted_AdsProhibited()
        {
            var p = KidProfilePolicy.Restricted("test reason");
            Assert.IsTrue(p.AdsProhibited);
            Assert.IsTrue(p.IapProhibited);
        }

        [Test]
        public void Policy_Unrestricted_AdsNotProhibited()
        {
            var p = KidProfilePolicy.Unrestricted("expert review complete");
            Assert.IsFalse(p.AdsProhibited);
            Assert.IsFalse(p.IapProhibited);
        }

        [Test]
        public void KidGate_WonderPop_AlwaysIneligible_EvenWithUnrestrictedPolicy()
        {
            var profile = GameBusinessProfileCatalog.Require("Wonder_Pop");
            var unrestricted = KidProfilePolicy.Unrestricted("test override");
            var result = KidProfileGate.CheckAdEligibility(profile.GameId, unrestricted);
            Assert.IsFalse(result.IsEligible);
            Assert.AreEqual("WonderPopKidProfile", result.Reason);
        }

        [Test]
        public void KidGate_WonderPop_SdkLinkingNotAllowed_EvenWithUnrestrictedPolicy()
        {
            var profile = GameBusinessProfileCatalog.Require("Wonder_Pop");
            var unrestricted = KidProfilePolicy.Unrestricted("test override");
            Assert.IsFalse(KidProfileGate.IsSdkLinkingAllowed(profile.GameId, unrestricted));
        }

        [Test]
        public void KidGate_RestrictedPolicy_AnyGame_Ineligible()
        {
            var restricted = KidProfilePolicy.Restricted("no review");
            var result = KidProfileGate.CheckAdEligibility("Some_Other_Game", restricted);
            Assert.IsFalse(result.IsEligible);
            Assert.AreEqual("KidProfileAdsProhibited", result.Reason);
        }

        [Test]
        public void KidGate_RestrictedPolicy_SdkLinkingNotAllowed()
        {
            var restricted = KidProfilePolicy.Restricted("no review");
            Assert.IsFalse(KidProfileGate.IsSdkLinkingAllowed("Some_Other_Game", restricted));
        }

        [Test]
        public void KidGate_UnrestrictedPolicy_NonWonderPop_Eligible()
        {
            var unrestricted = KidProfilePolicy.Unrestricted("expert review done");
            var result = KidProfileGate.CheckAdEligibility("Garage_Legends", unrestricted);
            Assert.IsTrue(result.IsEligible);
        }

        [Test]
        public void KidGate_UnrestrictedPolicy_NonWonderPop_SdkLinkingAllowed()
        {
            var unrestricted = KidProfilePolicy.Unrestricted("expert review done");
            Assert.IsTrue(KidProfileGate.IsSdkLinkingAllowed("Garage_Legends", unrestricted));
        }
    }
}
