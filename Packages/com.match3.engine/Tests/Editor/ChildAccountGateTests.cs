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
            var p = ChildProfile.Create("Game1", AgeCategory.Adult, false);
            Assert.IsFalse(p.IsRestricted);
        }

        [Test]
        public void ChildProfile_IsRestricted_Adult_LocalOnlyTrue_IsRestricted()
        {
            var p = ChildProfile.Create("Game1", AgeCategory.Adult, true);
            Assert.IsTrue(p.IsRestricted);
        }

        [Test]
        public void ChildProfile_IsRestricted_MinorWithParent_IsRestricted()
        {
            var p = ChildProfile.Create("Game1", AgeCategory.MinorWithParent, false);
            Assert.IsTrue(p.IsRestricted);
        }

        [Test]
        public void ChildProfile_IsRestricted_MinorUnverified_IsRestricted()
        {
            var p = ChildProfile.Create("Game1", AgeCategory.MinorUnverified, false);
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
        public void CanUseSocialFeatures_WonderPopLowercase_NotBlocked()
        {
            // Ordinal check: "wonder_pop" != "Wonder_Pop"
            var profile = ChildProfile.Create("wonder_pop", AgeCategory.Adult, false);
            var policy = new ChildAccountPolicy(true, true, "open");
            var (eligible, _) = ChildAccountGate.CanUseSocialFeatures("wonder_pop", profile, policy);
            Assert.IsTrue(eligible);
        }

        [Test]
        public void CanUseSocialFeatures_RestrictedProfile_BlockedGuard2()
        {
            var profile = ChildProfile.Create("Game1", AgeCategory.MinorWithParent, false);
            var policy = new ChildAccountPolicy(true, true, "open");
            var (eligible, reason) = ChildAccountGate.CanUseSocialFeatures("Game1", profile, policy);
            Assert.IsFalse(eligible);
            Assert.AreEqual("RestrictedProfileProhibited", reason);
        }

        [Test]
        public void CanUseSocialFeatures_SocialDisabled_BlockedGuard3()
        {
            var profile = ChildProfile.Create("Game1", AgeCategory.Adult, false);
            var policy = new ChildAccountPolicy(false, true, "restricted");
            var (eligible, reason) = ChildAccountGate.CanUseSocialFeatures("Game1", profile, policy);
            Assert.IsFalse(eligible);
            Assert.AreEqual("SocialFeaturesDisabled", reason);
        }

        [Test]
        public void CanUseSocialFeatures_AllGuardsPassed_IsEligible()
        {
            var profile = ChildProfile.Create("Game1", AgeCategory.Adult, false);
            var policy = new ChildAccountPolicy(true, true, "open");
            var (eligible, reason) = ChildAccountGate.CanUseSocialFeatures("Game1", profile, policy);
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
            var (eligible, reason) = ChildAccountGate.CanUsePublicHandle("Wonder_Pop", policy);
            Assert.IsFalse(eligible);
            Assert.AreEqual("WonderPopHandleProhibited", reason);
        }

        [Test]
        public void CanUsePublicHandle_WonderPopLowercase_NotBlocked()
        {
            var policy = new ChildAccountPolicy(true, true, "open");
            var (eligible, _) = ChildAccountGate.CanUsePublicHandle("wonder_pop", policy);
            Assert.IsTrue(eligible);
        }

        [Test]
        public void CanUsePublicHandle_HandleDisabled_BlockedGuard2()
        {
            var policy = new ChildAccountPolicy(true, false, "restricted");
            var (eligible, reason) = ChildAccountGate.CanUsePublicHandle("Game1", policy);
            Assert.IsFalse(eligible);
            Assert.AreEqual("PublicHandleDisabled", reason);
        }

        [Test]
        public void CanUsePublicHandle_AllGuardsPassed_IsEligible()
        {
            var policy = new ChildAccountPolicy(true, true, "open");
            var (eligible, reason) = ChildAccountGate.CanUsePublicHandle("Game1", policy);
            Assert.IsTrue(eligible);
            Assert.IsEmpty(reason);
        }

        // --- IsChildRestricted ---

        [Test]
        public void IsChildRestricted_AdultNotLocalOnly_False()
        {
            var p = ChildProfile.Create("G", AgeCategory.Adult, false);
            Assert.IsFalse(ChildAccountGate.IsChildRestricted(p));
        }

        [Test]
        public void IsChildRestricted_AdultLocalOnly_True()
        {
            var p = ChildProfile.Create("G", AgeCategory.Adult, true);
            Assert.IsTrue(ChildAccountGate.IsChildRestricted(p));
        }

        [Test]
        public void IsChildRestricted_MinorWithParent_True()
        {
            var p = ChildProfile.Create("G", AgeCategory.MinorWithParent, false);
            Assert.IsTrue(ChildAccountGate.IsChildRestricted(p));
        }

        [Test]
        public void IsChildRestricted_MinorUnverified_True()
        {
            var p = ChildProfile.Create("G", AgeCategory.MinorUnverified, false);
            Assert.IsTrue(ChildAccountGate.IsChildRestricted(p));
        }

        // --- ChildAuditLog ---

        [Test]
        public void AuditLog_NullReason_Throws()
        {
            var log = new ChildAuditLog();
            var profile = ChildProfile.Create("G", AgeCategory.Adult, false);
            Assert.Throws<ArgumentException>(() => log.Record(profile, true, null));
        }

        [Test]
        public void AuditLog_EmptyReason_Throws()
        {
            var log = new ChildAuditLog();
            var profile = ChildProfile.Create("G", AgeCategory.Adult, false);
            Assert.Throws<ArgumentException>(() => log.Record(profile, true, string.Empty));
        }

        [Test]
        public void AuditLog_Record_IncrementsCount()
        {
            var log = new ChildAuditLog();
            var profile = ChildProfile.Create("G", AgeCategory.Adult, false);
            log.Record(profile, true, "ok");
            Assert.AreEqual(1, log.Count);
        }

        [Test]
        public void AuditLog_Clear_ResetsCount()
        {
            var log = new ChildAuditLog();
            var profile = ChildProfile.Create("G", AgeCategory.Adult, false);
            log.Record(profile, true, "ok");
            log.Clear();
            Assert.AreEqual(0, log.Count);
        }

        [Test]
        public void AuditLog_HasEntry_ReturnsTrueAfterRecord()
        {
            var log = new ChildAuditLog();
            var profile = ChildProfile.Create("MyGame", AgeCategory.Adult, false);
            log.Record(profile, false, "denied");
            Assert.IsTrue(log.HasEntry("MyGame"));
        }

        [Test]
        public void AuditLog_HasEntry_ReturnsFalseForMissingGame()
        {
            var log = new ChildAuditLog();
            Assert.IsFalse(log.HasEntry("NoGame"));
        }
    }
}
