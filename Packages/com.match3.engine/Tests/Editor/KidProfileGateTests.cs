using System;
using NUnit.Framework;
using Match3.Engine.Runtime;
using Match3.Engine.Runtime.Commerce;

namespace Match3.Engine.Tests.Editor
{
    public sealed class KidProfileGateTests
    {
        private static readonly string[] AllGames =
            { "Wonder_Pop", "Coral_Rescue", "Dino_Islands", "Garage_Legends", "The_Fractured_Realms", "Some_Other_Game" };

#pragma warning disable 0618 // exercising obsolete relaxing API on purpose: it must stay ineffective
        private static KidProfilePolicy[] AllConstructiblePolicies => new[]
        {
            KidProfilePolicy.Default,
            KidProfilePolicy.Restricted("no review"),
            KidProfilePolicy.Unrestricted("expert review done"),
            new KidProfilePolicy(false, false, "relaxed via ctor"),
            default(KidProfilePolicy),
        };
#pragma warning restore 0618

        [Test]
        public void Policy_NullJustification_Throws()
        {
            Assert.Throws<ArgumentException>(() => KidProfilePolicy.Restricted(null));
            Assert.Throws<ArgumentException>(() => KidProfilePolicy.Restricted(string.Empty));
#pragma warning disable 0618
            Assert.Throws<ArgumentException>(() => new KidProfilePolicy(true, true, null));
            Assert.Throws<ArgumentException>(() => new KidProfilePolicy(true, true, string.Empty));
#pragma warning restore 0618
        }

        [Test]
        public void Policy_Default_HasJustification()
        {
            Assert.IsFalse(string.IsNullOrEmpty(KidProfilePolicy.Default.PolicyJustification));
        }

        [Test]
        public void Policy_ObsoleteFlags_AlwaysReportProhibited()
        {
#pragma warning disable 0618
            foreach (var p in AllConstructiblePolicies)
            {
                Assert.IsTrue(p.AdsProhibited);
                Assert.IsTrue(p.IapProhibited);
            }
#pragma warning restore 0618
        }

        [Test]
        public void KidGate_AdsIneligible_ForEveryGame_UnderEveryPolicy()
        {
            foreach (var gameId in AllGames)
                foreach (var policy in AllConstructiblePolicies)
                {
                    var result = KidProfileGate.CheckAdEligibility(gameId, policy);
                    Assert.IsFalse(result.IsEligible, gameId);
                    Assert.AreEqual("KidProfileAdsProhibited", result.Reason);
                }
        }

        [Test]
        public void KidGate_SdkLinkingNotAllowed_ForEveryGame_UnderEveryPolicy()
        {
            foreach (var gameId in AllGames)
                foreach (var policy in AllConstructiblePolicies)
                    Assert.IsFalse(KidProfileGate.IsSdkLinkingAllowed(gameId, policy), gameId);
        }

        [Test]
        public void KidGate_WonderPop_AlwaysIneligible_EvenWithUnrestrictedPolicy()
        {
            var profile = GameBusinessProfileCatalog.Require("Wonder_Pop");
#pragma warning disable 0618
            var unrestricted = KidProfilePolicy.Unrestricted("test override");
#pragma warning restore 0618
            var result = KidProfileGate.CheckAdEligibility(profile.GameId, unrestricted);
            Assert.IsFalse(result.IsEligible);
            Assert.AreEqual("KidProfileAdsProhibited", result.Reason);
            Assert.IsFalse(KidProfileGate.IsSdkLinkingAllowed(profile.GameId, unrestricted));
        }

        [Test]
        public void KidGate_UnrestrictedPolicy_NonWonderPop_StillIneligible()
        {
#pragma warning disable 0618
            var unrestricted = KidProfilePolicy.Unrestricted("expert review done");
#pragma warning restore 0618
            var result = KidProfileGate.CheckAdEligibility("Garage_Legends", unrestricted);
            Assert.IsFalse(result.IsEligible);
            Assert.AreEqual("KidProfileAdsProhibited", result.Reason);
            Assert.IsFalse(KidProfileGate.IsSdkLinkingAllowed("Garage_Legends", unrestricted));
        }

        [Test]
        public void KidGate_DefaultStructPolicy_FailsClosed()
        {
            var result = KidProfileGate.CheckAdEligibility("Coral_Rescue", default(KidProfilePolicy));
            Assert.IsFalse(result.IsEligible);
            Assert.AreEqual("KidProfileAdsProhibited", result.Reason);
            Assert.IsFalse(KidProfileGate.IsSdkLinkingAllowed("Coral_Rescue", default(KidProfilePolicy)));
        }
    }
}
