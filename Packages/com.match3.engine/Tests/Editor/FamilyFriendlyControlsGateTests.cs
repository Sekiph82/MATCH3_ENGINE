using System;
using NUnit.Framework;
using Match3.Engine.Runtime.Privacy;

namespace Match3.Engine.Tests.Editor
{
    [TestFixture]
    public class FamilyFriendlyControlsGateTests
    {
        // FamilyControlRecord tests

        [Test]
        public void FamilyControlRecord_Create_NullGameId_Throws()
        {
            Assert.Throws<ArgumentException>(() =>
                FamilyControlRecord.Create(null, FamilyControlFeature.ChatFeature, false, false));
        }

        [Test]
        public void FamilyControlRecord_Create_EmptyGameId_Throws()
        {
            Assert.Throws<ArgumentException>(() =>
                FamilyControlRecord.Create("", FamilyControlFeature.ChatFeature, false, false));
        }

        [Test]
        public void FamilyControlRecord_Create_Valid_Succeeds()
        {
            var r = FamilyControlRecord.Create("Coral_Rescue", FamilyControlFeature.ChatFeature, true, false);
            Assert.AreEqual("Coral_Rescue", r.GameId);
            Assert.AreEqual(FamilyControlFeature.ChatFeature, r.Feature);
            Assert.IsTrue(r.HasParentalConsent);
            Assert.IsFalse(r.IsKidsCategoryEnrolled);
        }

        [Test]
        public void FamilyControlRecord_RequiresParentalGate_ExternalLink_True()
        {
            var r = FamilyControlRecord.Create("Coral_Rescue", FamilyControlFeature.ParentalGateExternalLink, false, false);
            Assert.IsTrue(r.RequiresParentalGate);
        }

        [Test]
        public void FamilyControlRecord_RequiresParentalGate_Purchase_True()
        {
            var r = FamilyControlRecord.Create("Coral_Rescue", FamilyControlFeature.ParentalGatePurchase, false, false);
            Assert.IsTrue(r.RequiresParentalGate);
        }

        [Test]
        public void FamilyControlRecord_RequiresParentalGate_ChatFeature_False()
        {
            var r = FamilyControlRecord.Create("Coral_Rescue", FamilyControlFeature.ChatFeature, false, false);
            Assert.IsFalse(r.RequiresParentalGate);
        }

        [Test]
        public void FamilyControlRecord_IsThirdPartySdk_AdSdk_True()
        {
            var r = FamilyControlRecord.Create("Coral_Rescue", FamilyControlFeature.ThirdPartyAdSdk, false, false);
            Assert.IsTrue(r.IsThirdPartySdk);
        }

        [Test]
        public void FamilyControlRecord_IsThirdPartySdk_AnalyticsSdk_True()
        {
            var r = FamilyControlRecord.Create("Coral_Rescue", FamilyControlFeature.ThirdPartyAnalyticsSdk, false, false);
            Assert.IsTrue(r.IsThirdPartySdk);
        }

        [Test]
        public void FamilyControlRecord_IsThirdPartySdk_ParentalGate_False()
        {
            var r = FamilyControlRecord.Create("Coral_Rescue", FamilyControlFeature.ParentalGateExternalLink, false, false);
            Assert.IsFalse(r.IsThirdPartySdk);
        }

        // FamilyFriendlyPolicy tests

        [Test]
        public void FamilyFriendlyPolicy_Safe_Values_Correct()
        {
            var p = FamilyFriendlyPolicy.Safe("test");
            Assert.IsFalse(p.AllowThirdPartySdks);
            Assert.IsTrue(p.RequireParentalGateForExternalLinks);
            Assert.IsTrue(p.BlockSocialInKidsCategory);
            Assert.AreEqual("test", p.PolicyDescription);
        }

        [Test]
        public void FamilyFriendlyPolicy_Default_NotNull()
        {
            var p = FamilyFriendlyPolicy.Default;
            Assert.IsNotNull(p.PolicyDescription);
            Assert.IsNotEmpty(p.PolicyDescription);
        }

        [Test]
        public void FamilyFriendlyPolicy_NullDescription_Throws()
        {
            Assert.Throws<ArgumentException>(() => FamilyFriendlyPolicy.Safe(null));
        }

        // CanEnableFeature tests

        [Test]
        public void CanEnableFeature_WonderPop_ThirdPartyAdSdk_Blocked()
        {
            var record = FamilyControlRecord.Create("Wonder_Pop", FamilyControlFeature.ThirdPartyAdSdk, false, false);
            var policy = FamilyFriendlyPolicy.Default;
            var result = FamilyFriendlyControlsGate.CanEnableFeature("Wonder_Pop", record, policy, out var reason);
            Assert.IsFalse(result);
            Assert.AreEqual("WonderPopThirdPartySdkProhibited", reason);
        }

        [Test]
        public void CanEnableFeature_WonderPop_ThirdPartyAnalyticsSdk_Blocked()
        {
            var record = FamilyControlRecord.Create("Wonder_Pop", FamilyControlFeature.ThirdPartyAnalyticsSdk, false, false);
            var policy = FamilyFriendlyPolicy.Default;
            var result = FamilyFriendlyControlsGate.CanEnableFeature("Wonder_Pop", record, policy, out var reason);
            Assert.IsFalse(result);
            Assert.AreEqual("WonderPopThirdPartySdkProhibited", reason);
        }

        [Test]
        public void CanEnableFeature_WonderPop_ExternalLink_NotBlocked()
        {
            // Wonder_Pop + ParentalGateExternalLink goes through normal policy, not Guard 1
            // With Default policy (RequireParentalGate=true) and no consent => ParentalGateRequired
            var record = FamilyControlRecord.Create("Wonder_Pop", FamilyControlFeature.ParentalGateExternalLink, false, false);
            var policy = FamilyFriendlyPolicy.Default;
            var result = FamilyFriendlyControlsGate.CanEnableFeature("Wonder_Pop", record, policy, out var reason);
            Assert.IsFalse(result);
            Assert.AreEqual("ParentalGateRequired", reason); // not Guard 1
        }

        [Test]
        public void CanEnableFeature_WonderPop_Lowercase_ThirdPartySdk_UnknownGame()
        {
            // Ordinal catalog lookup: a case variant of Wonder_Pop is not a catalog ID, so it fails closed with UnknownGame (never eligible, never the Wonder_Pop reason).
            var record = FamilyControlRecord.Create("wonder_pop", FamilyControlFeature.ThirdPartyAdSdk, false, false);
            var policy = FamilyFriendlyPolicy.Default;
            var result = FamilyFriendlyControlsGate.CanEnableFeature("wonder_pop", record, policy, out var reason);
            Assert.IsFalse(result);
            Assert.AreEqual("UnknownGame", reason);
        }

        [Test]
        public void CanEnableFeature_SafePolicy_ThirdPartySdkDisabled_OtherGame_Blocked()
        {
            var record = FamilyControlRecord.Create("Garage_Legends", FamilyControlFeature.ThirdPartyAdSdk, false, false);
            var policy = FamilyFriendlyPolicy.Default; // AllowThirdPartySdks=false
            var result = FamilyFriendlyControlsGate.CanEnableFeature("Garage_Legends", record, policy, out var reason);
            Assert.IsFalse(result);
            Assert.AreEqual("ThirdPartySdkDisabled", reason);
        }

        [Test]
        public void CanEnableFeature_ParentalGateRequired_NoConsent_Blocked()
        {
            var record = FamilyControlRecord.Create("Coral_Rescue", FamilyControlFeature.ParentalGateExternalLink, false, false);
            var policy = FamilyFriendlyPolicy.Default;
            var result = FamilyFriendlyControlsGate.CanEnableFeature("Coral_Rescue", record, policy, out var reason);
            Assert.IsFalse(result);
            Assert.AreEqual("ParentalGateRequired", reason);
        }

        [Test]
        public void CanEnableFeature_ParentalGate_HasConsent_Eligible()
        {
            var record = FamilyControlRecord.Create("Coral_Rescue", FamilyControlFeature.ParentalGateExternalLink, true, false);
            var policy = FamilyFriendlyPolicy.Default;
            var result = FamilyFriendlyControlsGate.CanEnableFeature("Coral_Rescue", record, policy, out var reason);
            Assert.IsTrue(result);
            Assert.AreEqual(string.Empty, reason);
        }

        [Test]
        public void CanEnableFeature_KidsCategory_SocialBlocked()
        {
            var record = FamilyControlRecord.Create("Coral_Rescue", FamilyControlFeature.SocialFeature, false, true);
            var policy = FamilyFriendlyPolicy.Default;
            var result = FamilyFriendlyControlsGate.CanEnableFeature("Coral_Rescue", record, policy, out var reason);
            Assert.IsFalse(result);
            Assert.AreEqual("SocialBlockedInKidsCategory", reason);
        }

        [Test]
        public void CanEnableFeature_KidsCategory_ChatBlocked()
        {
            var record = FamilyControlRecord.Create("Coral_Rescue", FamilyControlFeature.ChatFeature, false, true);
            var policy = FamilyFriendlyPolicy.Default;
            var result = FamilyFriendlyControlsGate.CanEnableFeature("Coral_Rescue", record, policy, out var reason);
            Assert.IsFalse(result);
            Assert.AreEqual("SocialBlockedInKidsCategory", reason);
        }

        [Test]
        public void CanEnableFeature_AllowThirdPartySdks_True_NonWonderPop_Eligible()
        {
            var record = FamilyControlRecord.Create("Garage_Legends", FamilyControlFeature.ThirdPartyAdSdk, false, false);
            var policy = FamilyFriendlyPolicy.Create(true, false, false, "permissive policy");
            var result = FamilyFriendlyControlsGate.CanEnableFeature("Garage_Legends", record, policy, out var reason);
            Assert.IsTrue(result);
            Assert.AreEqual(string.Empty, reason);
        }

        // Helper method tests

        [Test]
        public void RequiresParentalGate_Helper_Purchase_True()
        {
            var record = FamilyControlRecord.Create("Coral_Rescue", FamilyControlFeature.ParentalGatePurchase, false, false);
            Assert.IsTrue(FamilyFriendlyControlsGate.RequiresParentalGate(record));
        }

        [Test]
        public void IsThirdPartySdk_Helper_AdSdk_True()
        {
            var record = FamilyControlRecord.Create("Coral_Rescue", FamilyControlFeature.ThirdPartyAdSdk, false, false);
            Assert.IsTrue(FamilyFriendlyControlsGate.IsThirdPartySdk(record));
        }

        // FamilyControlLog tests

        [Test]
        public void FamilyControlLog_Record_And_Count()
        {
            var log = new FamilyControlLog();
            log.Record("Coral_Rescue", "ChatFeature");
            log.Record("Coral_Rescue", "SocialFeature");
            Assert.AreEqual(2, log.Count);
        }

        [Test]
        public void FamilyControlLog_HasEntry_True()
        {
            var log = new FamilyControlLog();
            log.Record("Coral_Rescue", "ChatFeature");
            Assert.IsTrue(log.HasEntry("Coral_Rescue"));
            Assert.IsFalse(log.HasEntry("Garage_Legends"));
        }

        [Test]
        public void FamilyControlLog_Clear_ResetsCount()
        {
            var log = new FamilyControlLog();
            log.Record("Coral_Rescue", "ChatFeature");
            log.Clear();
            Assert.AreEqual(0, log.Count);
        }

        [Test]
        public void FamilyControlLog_Record_NullGameId_Throws()
        {
            var log = new FamilyControlLog();
            Assert.Throws<ArgumentException>(() => log.Record(null, "ChatFeature"));
        }

        // QA remediation: fail-closed default policy, GameId mismatch guard, log argument validation
        [Test]
        public void CanEnableFeature_DefaultStructPolicy_FailsClosed()
        {
            var record = FamilyControlRecord.Create("Coral_Rescue", FamilyControlFeature.ParentalGateExternalLink, false, false);
            var expected = FamilyFriendlyControlsGate.CanEnableFeature("Coral_Rescue", record, FamilyFriendlyPolicy.Default, out var expectedReason);
            var result = FamilyFriendlyControlsGate.CanEnableFeature("Coral_Rescue", record, default(FamilyFriendlyPolicy), out var reason);
            Assert.IsFalse(expected);
            Assert.IsFalse(result);
            Assert.AreEqual("ParentalGateRequired", expectedReason);
            Assert.AreEqual(expectedReason, reason);
        }

        [Test]
        public void CanEnableFeature_GameIdMismatch_Blocked()
        {
            var record = FamilyControlRecord.Create("Wonder_Pop", FamilyControlFeature.ThirdPartyAdSdk, true, false);
            var result = FamilyFriendlyControlsGate.CanEnableFeature("Coral_Rescue", record, FamilyFriendlyPolicy.Default, out var reason);
            Assert.IsFalse(result);
            Assert.AreEqual("GameIdMismatch", reason);
        }

        [Test]
        public void FamilyControlLog_Record_NullOrEmptySecondArg_Throws()
        {
            var log = new FamilyControlLog();
            Assert.Throws<System.ArgumentException>(() => log.Record("Coral_Rescue", null));
            Assert.Throws<System.ArgumentException>(() => log.Record("Coral_Rescue", string.Empty));
            Assert.AreEqual(0, log.Count);
        }

        [Test]
        public void FamilyControlLog_HasEntry_IsOrdinal()
        {
            var log = new FamilyControlLog();
            log.Record("Wonder_Pop", "evt");
            Assert.IsTrue(log.HasEntry("Wonder_Pop"));
            Assert.IsFalse(log.HasEntry("wonder_pop"));
            // Soft hyphen (U+00AD) is ignorable under culture-sensitive comparison; Ordinal must not match it.
            log.Record("Coral\u00AD_Rescue", "evt");
            Assert.IsTrue(log.HasEntry("Coral\u00AD_Rescue"));
            Assert.IsFalse(log.HasEntry("Coral_Rescue"));
        }


        [Test]
        public void NullOrEmptyGameId_DefaultRecord_GameIdMissing()
        {
            foreach (var gameId in new[] { null, string.Empty })
            {
                Assert.IsFalse(FamilyFriendlyControlsGate.CanEnableFeature(gameId, default(FamilyControlRecord), default(FamilyFriendlyPolicy), out var reason));
                Assert.AreEqual("GameIdMissing", reason);
            }
        }

        [Test]
        public void UnknownOrCaseVariantGameId_DefaultRecord_UnknownGame()
        {
            foreach (var gameId in new[] { "Unknown_Game", "coral_rescue", "CORAL_RESCUE", " Coral_Rescue", "Coral_Rescue ", "wonder_pop", "WONDER_POP" })
            {
                Assert.IsFalse(FamilyFriendlyControlsGate.CanEnableFeature(gameId, default(FamilyControlRecord), default(FamilyFriendlyPolicy), out var reason));
                Assert.AreEqual("UnknownGame", reason);
            }
        }
    }
}
