using System;
using NUnit.Framework;
using Match3.Engine.Runtime.Identity;

namespace Match3.Engine.Tests.Editor
{
    public sealed class ChildAccountGateTests
    {
        // --- ChildProfile ---

        [Test]
        public void ChildProfile_NullGameId_Throws()
        {
            Assert.Throws<ArgumentException>(() =>
                ChildProfile.Create(null, AgeCategory.Adult, false));
        }

        [Test]
        public void ChildProfile_EmptyGameId_Throws()
        {
            Assert.Throws<ArgumentException>(() =>
                ChildProfile.Create(string.Empty, AgeCategory.Adult, false));
        }

        [Test]
        public void ChildProfile_IsRestricted_Adult_LocalOnlyFalse_IsNotRestricted()
        {
            var p = ChildProfile.Create("Coral_Rescue", AgeCategory.Adult, false);
            Assert.IsFalse(p.IsRestricted);
        }

        [Test]
        public void ChildProfile_IsRestricted_Adult_LocalOnlyTrue_IsRestricted()
        {
            var p = ChildProfile.Create("Coral_Rescue", AgeCategory.Adult, true);
            Assert.IsTrue(p.IsRestricted);
        }

        [Test]
        public void ChildProfile_IsRestricted_MinorWithParent_IsRestricted()
        {
            var p = ChildProfile.Create("Coral_Rescue", AgeCategory.MinorWithParent, false);
            Assert.IsTrue(p.IsRestricted);
        }

        [Test]
        public void ChildProfile_IsRestricted_MinorUnverified_IsRestricted()
        {
            var p = ChildProfile.Create("Coral_Rescue", AgeCategory.MinorUnverified, false);
            Assert.IsTrue(p.IsRestricted);
        }

        // --- ChildAccountPolicy ---

        [Test]
        public void Policy_NullDescription_Throws()
        {
            Assert.Throws<ArgumentException>(() =>
                new ChildAccountPolicy(true, true, null));
        }

        [Test]
        public void Policy_EmptyDescription_Throws()
        {
            Assert.Throws<ArgumentException>(() =>
                new ChildAccountPolicy(true, true, string.Empty));
        }

        [Test]
        public void Policy_Safe_DisablesBoth()
        {
            var p = ChildAccountPolicy.Safe("desc");
            Assert.IsFalse(p.AllowSocialFeatures);
            Assert.IsFalse(p.AllowPublicHandle);
        }

        [Test]
        public void Policy_Default_IsFullySafe()
        {
            var p = ChildAccountPolicy.Default;
            Assert.IsFalse(p.AllowSocialFeatures);
            Assert.IsFalse(p.AllowPublicHandle);
            Assert.IsNotEmpty(p.PolicyDescription);
        }

        // --- ChildAccountGate.CanUseSocialFeatures ---

        [Test]
        public void CanUseSocialFeatures_WonderPop_BlockedGuard1()
        {
            var profile = ChildProfile.Create("Wonder_Pop", AgeCategory.Adult, false);
            var policy = new ChildAccountPolicy(true, true, "open");
            var (eligible, reason) = ChildAccountGate.CanUseSocialFeatures("Wonder_Pop", profile, policy);
            Assert.IsFalse(eligible);
            Assert.AreEqual("WonderPopSocialProhibited", reason);
        }

        [Test]
        public void CanUseSocialFeatures_WonderPopLowercase_UnknownGame()
        {
            // Ordinal catalog lookup: a case variant of Wonder_Pop is not a catalog ID, so it fails closed with UnknownGame (never eligible, never the Wonder_Pop reason).
            var profile = ChildProfile.Create("wonder_pop", AgeCategory.Adult, false);
            var policy = new ChildAccountPolicy(true, true, "open");
            var (eligible, reason) = ChildAccountGate.CanUseSocialFeatures("wonder_pop", profile, policy);
            Assert.IsFalse(eligible);
            Assert.AreEqual("UnknownGame", reason);
        }

        [Test]
        public void CanUseSocialFeatures_RestrictedProfile_BlockedGuard2()
        {
            var profile = ChildProfile.Create("Coral_Rescue", AgeCategory.MinorWithParent, false);
            var policy = new ChildAccountPolicy(true, true, "open");
            var (eligible, reason) = ChildAccountGate.CanUseSocialFeatures("Coral_Rescue", profile, policy);
            Assert.IsFalse(eligible);
            Assert.AreEqual("RestrictedProfileProhibited", reason);
        }

        [Test]
        public void CanUseSocialFeatures_SocialDisabled_BlockedGuard3()
        {
            var profile = ChildProfile.Create("Coral_Rescue", AgeCategory.Adult, false);
            var policy = new ChildAccountPolicy(false, true, "restricted");
            var (eligible, reason) = ChildAccountGate.CanUseSocialFeatures("Coral_Rescue", profile, policy);
            Assert.IsFalse(eligible);
            Assert.AreEqual("SocialFeaturesDisabled", reason);
        }

        [Test]
        public void CanUseSocialFeatures_AllGuardsPassed_IsEligible()
        {
            var profile = ChildProfile.Create("Coral_Rescue", AgeCategory.Adult, false);
            var policy = new ChildAccountPolicy(true, true, "open");
            var (eligible, reason) = ChildAccountGate.CanUseSocialFeatures("Coral_Rescue", profile, policy);
            Assert.IsTrue(eligible);
            Assert.IsEmpty(reason);
        }

        [Test]
        public void CanUseSocialFeatures_WonderPop_BeatsRestrictedCheck()
        {
            // Guard 1 must fire first even when profile would also be restricted
            var profile = ChildProfile.Create("Wonder_Pop", AgeCategory.MinorUnverified, true);
            var policy = new ChildAccountPolicy(false, false, "safe");
            var (eligible, reason) = ChildAccountGate.CanUseSocialFeatures("Wonder_Pop", profile, policy);
            Assert.IsFalse(eligible);
            Assert.AreEqual("WonderPopSocialProhibited", reason);
        }

        // --- ChildAccountGate.CanUsePublicHandle ---

        [Test]
        public void CanUsePublicHandle_WonderPop_BlockedGuard1()
        {
            var policy = new ChildAccountPolicy(true, true, "open");
            var profile = ChildProfile.Create("Wonder_Pop", AgeCategory.Adult, false);
            var (eligible, reason) = ChildAccountGate.CanUsePublicHandle("Wonder_Pop", profile, policy);
            Assert.IsFalse(eligible);
            Assert.AreEqual("WonderPopHandleProhibited", reason);
        }

        [Test]
        public void CanUsePublicHandle_WonderPopLowercase_UnknownGame()
        {
            // Ordinal catalog lookup: a case variant of Wonder_Pop is not a catalog ID, so it fails closed with UnknownGame (never eligible, never the Wonder_Pop reason).
            var policy = new ChildAccountPolicy(true, true, "open");
            var profile = ChildProfile.Create("wonder_pop", AgeCategory.Adult, false);
            var (eligible, reason) = ChildAccountGate.CanUsePublicHandle("wonder_pop", profile, policy);
            Assert.IsFalse(eligible);
            Assert.AreEqual("UnknownGame", reason);
        }

        [Test]
        public void CanUsePublicHandle_HandleDisabled_BlockedGuard2()
        {
            var policy = new ChildAccountPolicy(true, false, "restricted");
            var profile = ChildProfile.Create("Coral_Rescue", AgeCategory.Adult, false);
            var (eligible, reason) = ChildAccountGate.CanUsePublicHandle("Coral_Rescue", profile, policy);
            Assert.IsFalse(eligible);
            Assert.AreEqual("PublicHandleDisabled", reason);
        }

        [Test]
        public void CanUsePublicHandle_AllGuardsPassed_IsEligible()
        {
            var policy = new ChildAccountPolicy(true, true, "open");
            var profile = ChildProfile.Create("Coral_Rescue", AgeCategory.Adult, false);
            var (eligible, reason) = ChildAccountGate.CanUsePublicHandle("Coral_Rescue", profile, policy);
            Assert.IsTrue(eligible);
            Assert.IsEmpty(reason);
        }

        private static readonly ChildAccountPolicy[] AllConstructiblePolicies =
        {
            ChildAccountPolicy.Default,
            ChildAccountPolicy.Safe("safe"),
            new ChildAccountPolicy(true, true, "open"),
            default(ChildAccountPolicy),
        };

        [Test]
        public void CanUsePublicHandle_ChildOrUnverified_DeniedUnderEveryPolicy()
        {
            var restricted = new[]
            {
                ChildProfile.Create("Coral_Rescue", AgeCategory.MinorWithParent, false),
                ChildProfile.Create("Coral_Rescue", AgeCategory.MinorUnverified, false),
                ChildProfile.Create("Coral_Rescue", AgeCategory.MinorWithParent, true),
                ChildProfile.Create("Coral_Rescue", AgeCategory.MinorUnverified, true),
                ChildProfile.Create("Coral_Rescue", AgeCategory.Adult, true),
            };
            foreach (var policy in AllConstructiblePolicies)
                foreach (var profile in restricted)
                {
                    var (eligible, reason) = ChildAccountGate.CanUsePublicHandle("Coral_Rescue", profile, policy);
                    Assert.IsFalse(eligible);
                    Assert.AreEqual("RestrictedProfileProhibited", reason);
                }
        }

        [Test]
        public void CanUsePublicHandle_ObsoleteOverload_ProfileUnknown_FailsClosed()
        {
#pragma warning disable 0618
            foreach (var policy in AllConstructiblePolicies)
            {
                var (eligible, reason) = ChildAccountGate.CanUsePublicHandle("Coral_Rescue", policy);
                Assert.IsFalse(eligible);
                Assert.AreEqual("RestrictedProfileProhibited", reason);
            }
            var (wpEligible, wpReason) = ChildAccountGate.CanUsePublicHandle("Wonder_Pop", new ChildAccountPolicy(true, true, "open"));
            var open = new ChildAccountPolicy(true, true, "open");
            Assert.AreEqual("GameIdMissing", ChildAccountGate.CanUsePublicHandle(null, open).reason);
            Assert.AreEqual("GameIdMissing", ChildAccountGate.CanUsePublicHandle(string.Empty, open).reason);
            Assert.AreEqual("UnknownGame", ChildAccountGate.CanUsePublicHandle("wonder_pop", open).reason);
            Assert.AreEqual("UnknownGame", ChildAccountGate.CanUsePublicHandle("Unknown_Game", open).reason);
#pragma warning restore 0618
            Assert.IsFalse(wpEligible);
            Assert.AreEqual("WonderPopHandleProhibited", wpReason);
        }

        [Test]
        public void DefaultStructPolicy_FailsClosed()
        {
            var adult = ChildProfile.Create("Coral_Rescue", AgeCategory.Adult, false);
            var (h1, hExpected) = ChildAccountGate.CanUsePublicHandle("Coral_Rescue", adult, ChildAccountPolicy.Default);
            var (h2, hReason) = ChildAccountGate.CanUsePublicHandle("Coral_Rescue", adult, default(ChildAccountPolicy));
            Assert.IsFalse(h1);
            Assert.IsFalse(h2);
            Assert.AreEqual(hExpected, hReason);
            var (s1, sExpected) = ChildAccountGate.CanUseSocialFeatures("Coral_Rescue", adult, ChildAccountPolicy.Default);
            var (s2, sReason) = ChildAccountGate.CanUseSocialFeatures("Coral_Rescue", adult, default(ChildAccountPolicy));
            Assert.IsFalse(s1);
            Assert.IsFalse(s2);
            Assert.AreEqual(sExpected, sReason);
        }

        [Test]
        public void GameIdMismatch_Blocked()
        {
            var policy = new ChildAccountPolicy(true, true, "open");
            var profile = ChildProfile.Create("Wonder_Pop", AgeCategory.Adult, false);
            Assert.AreEqual("GameIdMismatch", ChildAccountGate.CanUsePublicHandle("Coral_Rescue", profile, policy).reason);
            Assert.AreEqual("GameIdMismatch", ChildAccountGate.CanUseSocialFeatures("Coral_Rescue", profile, policy).reason);
            Assert.IsFalse(ChildAccountGate.CanUsePublicHandle("Coral_Rescue", profile, policy).eligible);
            Assert.IsFalse(ChildAccountGate.CanUseSocialFeatures("Coral_Rescue", profile, policy).eligible);
        }

        [Test]
        public void WonderPopParam_ProfileDiffers_GameIdMismatch()
        {
            var policy = new ChildAccountPolicy(true, true, "open");
            var profile = ChildProfile.Create("Coral_Rescue", AgeCategory.Adult, false);
            Assert.AreEqual("GameIdMismatch", ChildAccountGate.CanUsePublicHandle("Wonder_Pop", profile, policy).reason);
            Assert.AreEqual("GameIdMismatch", ChildAccountGate.CanUseSocialFeatures("Wonder_Pop", profile, policy).reason);
        }

        [Test]
        public void AuditLog_Record_DefaultProfile_Throws()
        {
            var log = new ChildAuditLog();
            Assert.Throws<ArgumentException>(() => log.Record(default(ChildProfile), false, "denied"));
        }

        // --- IsChildRestricted ---

        [Test]
        public void IsChildRestricted_AdultNotLocalOnly_False()
        {
            var p = ChildProfile.Create("Coral_Rescue", AgeCategory.Adult, false);
            Assert.IsFalse(ChildAccountGate.IsChildRestricted(p));
        }

        [Test]
        public void IsChildRestricted_AdultLocalOnly_True()
        {
            var p = ChildProfile.Create("Coral_Rescue", AgeCategory.Adult, true);
            Assert.IsTrue(ChildAccountGate.IsChildRestricted(p));
        }

        [Test]
        public void IsChildRestricted_MinorWithParent_True()
        {
            var p = ChildProfile.Create("Coral_Rescue", AgeCategory.MinorWithParent, false);
            Assert.IsTrue(ChildAccountGate.IsChildRestricted(p));
        }

        [Test]
        public void IsChildRestricted_MinorUnverified_True()
        {
            var p = ChildProfile.Create("Coral_Rescue", AgeCategory.MinorUnverified, false);
            Assert.IsTrue(ChildAccountGate.IsChildRestricted(p));
        }

        // --- ChildAuditLog ---

        [Test]
        public void AuditLog_NullReason_Throws()
        {
            var log = new ChildAuditLog();
            var profile = ChildProfile.Create("Coral_Rescue", AgeCategory.Adult, false);
            Assert.Throws<ArgumentException>(() => log.Record(profile, true, null));
        }

        [Test]
        public void AuditLog_EmptyReason_Throws()
        {
            var log = new ChildAuditLog();
            var profile = ChildProfile.Create("Coral_Rescue", AgeCategory.Adult, false);
            Assert.Throws<ArgumentException>(() => log.Record(profile, true, string.Empty));
        }

        [Test]
        public void AuditLog_Record_IncrementsCount()
        {
            var log = new ChildAuditLog();
            var profile = ChildProfile.Create("Coral_Rescue", AgeCategory.Adult, false);
            log.Record(profile, true, "ok");
            Assert.AreEqual(1, log.Count);
        }

        [Test]
        public void AuditLog_Clear_ResetsCount()
        {
            var log = new ChildAuditLog();
            var profile = ChildProfile.Create("Coral_Rescue", AgeCategory.Adult, false);
            log.Record(profile, true, "ok");
            log.Clear();
            Assert.AreEqual(0, log.Count);
        }

        [Test]
        public void AuditLog_HasEntry_ReturnsTrueAfterRecord()
        {
            var log = new ChildAuditLog();
            var profile = ChildProfile.Create("Coral_Rescue", AgeCategory.Adult, false);
            log.Record(profile, false, "denied");
            Assert.IsTrue(log.HasEntry("Coral_Rescue"));
        }

        [Test]
        public void AuditLog_HasEntry_ReturnsFalseForMissingGame()
        {
            var log = new ChildAuditLog();
            Assert.IsFalse(log.HasEntry("NoGame"));
        }


        [Test]
        public void NullOrEmptyGameId_DefaultRecord_GameIdMissing()
        {
            foreach (var gameId in new[] { null, string.Empty })
            {
                var (eligible, reason) = ChildAccountGate.CanUsePublicHandle(gameId, default(ChildProfile), default(ChildAccountPolicy));
                Assert.IsFalse(eligible);
                Assert.AreEqual("GameIdMissing", reason);
                var (socialEligible, socialReason) = ChildAccountGate.CanUseSocialFeatures(gameId, default(ChildProfile), default(ChildAccountPolicy));
                Assert.IsFalse(socialEligible);
                Assert.AreEqual("GameIdMissing", socialReason);
            }
        }

        [Test]
        public void UnknownOrCaseVariantGameId_DefaultRecord_UnknownGame()
        {
            foreach (var gameId in new[] { "Unknown_Game", "coral_rescue", "CORAL_RESCUE", " Coral_Rescue", "Coral_Rescue ", "wonder_pop", "WONDER_POP" })
            {
                var (eligible, reason) = ChildAccountGate.CanUsePublicHandle(gameId, default(ChildProfile), default(ChildAccountPolicy));
                Assert.IsFalse(eligible);
                Assert.AreEqual("UnknownGame", reason);
                var (socialEligible, socialReason) = ChildAccountGate.CanUseSocialFeatures(gameId, default(ChildProfile), default(ChildAccountPolicy));
                Assert.IsFalse(socialEligible);
                Assert.AreEqual("UnknownGame", socialReason);
            }
        }
    }
}
