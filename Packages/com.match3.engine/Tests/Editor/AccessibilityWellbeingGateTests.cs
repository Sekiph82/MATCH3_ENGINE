using System;
using NUnit.Framework;
using Match3.Engine.Runtime.Privacy;

namespace Match3.Engine.Tests.Editor
{
    [TestFixture]
    public class AccessibilityWellbeingGateTests
    {
        [Test]
        public void WellbeingRecord_Create_NullGameId_Throws()
        {
            Assert.Throws<ArgumentException>(() => WellbeingRecord.Create(null, WellbeingFeature.PlaytimeReminder, true, false));
        }

        [Test]
        public void WellbeingRecord_Create_EmptyGameId_Throws()
        {
            Assert.Throws<ArgumentException>(() => WellbeingRecord.Create(string.Empty, WellbeingFeature.PlaytimeReminder, true, false));
        }

        [Test]
        public void WellbeingRecord_Create_Valid_Succeeds()
        {
            var r = WellbeingRecord.Create("TestGame", WellbeingFeature.PlaytimeReminder, true, false);
            Assert.AreEqual("TestGame", r.GameId);
            Assert.AreEqual(WellbeingFeature.PlaytimeReminder, r.Feature);
            Assert.IsTrue(r.IsOptional);
            Assert.IsFalse(r.IsUserRequested);
        }

        [Test]
        public void WellbeingRecord_IsProtectiveFeature_PressureLoopDisable_True()
        {
            var r = WellbeingRecord.Create("G", WellbeingFeature.PressureLoopDisable, false, false);
            Assert.IsTrue(r.IsProtectiveFeature);
        }

        [Test]
        public void WellbeingRecord_IsProtectiveFeature_DarkPatternBlock_True()
        {
            var r = WellbeingRecord.Create("G", WellbeingFeature.DarkPatternBlock, false, false);
            Assert.IsTrue(r.IsProtectiveFeature);
        }

        [Test]
        public void WellbeingRecord_IsProtectiveFeature_ContentRatingFilter_True()
        {
            var r = WellbeingRecord.Create("G", WellbeingFeature.ContentRatingFilter, false, false);
            Assert.IsTrue(r.IsProtectiveFeature);
        }

        [Test]
        public void WellbeingRecord_IsProtectiveFeature_PlaytimeReminder_False()
        {
            var r = WellbeingRecord.Create("G", WellbeingFeature.PlaytimeReminder, false, false);
            Assert.IsFalse(r.IsProtectiveFeature);
        }

        [Test]
        public void WellbeingRecord_IsProtectiveFeature_AgeAppropriateOffer_False()
        {
            var r = WellbeingRecord.Create("G", WellbeingFeature.AgeAppropriateOffer, false, false);
            Assert.IsFalse(r.IsProtectiveFeature);
        }

        [Test]
        public void WellbeingRecord_RequiresUserConsent_OptionalNotRequested_True()
        {
            var r = WellbeingRecord.Create("G", WellbeingFeature.PlaytimeReminder, true, false);
            Assert.IsTrue(r.RequiresUserConsent);
        }

        [Test]
        public void WellbeingRecord_RequiresUserConsent_OptionalUserRequested_False()
        {
            var r = WellbeingRecord.Create("G", WellbeingFeature.PlaytimeReminder, true, true);
            Assert.IsFalse(r.RequiresUserConsent);
        }

        [Test]
        public void WellbeingRecord_RequiresUserConsent_NotOptional_False()
        {
            var r = WellbeingRecord.Create("G", WellbeingFeature.PlaytimeReminder, false, false);
            Assert.IsFalse(r.RequiresUserConsent);
        }

        [Test]
        public void WellbeingPolicy_Safe_Values_Correct()
        {
            var p = WellbeingPolicy.Safe("Test policy");
            Assert.IsTrue(p.EnforceWellbeingFeatures);
            Assert.IsTrue(p.BlockDarkPatterns);
            Assert.AreEqual("Test policy", p.PolicyDescription);
        }

        [Test]
        public void WellbeingPolicy_Default_NotNull()
        {
            var p = WellbeingPolicy.Default;
            Assert.IsNotNull(p.PolicyDescription);
            Assert.IsTrue(p.EnforceWellbeingFeatures);
            Assert.IsTrue(p.BlockDarkPatterns);
        }

        [Test]
        public void WellbeingPolicy_NullDescription_Throws()
        {
            Assert.Throws<ArgumentException>(() => WellbeingPolicy.Safe(null));
        }

        [Test]
        public void CanEnableWellbeingFeature_WonderPop_PressureLoopDisable_NotOptional_Blocked()
        {
            var record = WellbeingRecord.Create("Wonder_Pop", WellbeingFeature.PressureLoopDisable, false, false);
            var policy = WellbeingPolicy.Default;
            var result = AccessibilityWellbeingGate.CanEnableWellbeingFeature("Wonder_Pop", record, policy, out var reason);
            Assert.IsFalse(result);
            Assert.AreEqual("WonderPopPressureLoopMustBeOptional", reason);
        }

        [Test]
        public void CanEnableWellbeingFeature_WonderPop_PressureLoopDisable_Optional_NotBlocked()
        {
            // IsOptional=true means Guard 1 is not triggered; falls to Guard 3 since RequiresUserConsent=true
            var record = WellbeingRecord.Create("Wonder_Pop", WellbeingFeature.PressureLoopDisable, true, true);
            var policy = WellbeingPolicy.Default;
            var result = AccessibilityWellbeingGate.CanEnableWellbeingFeature("Wonder_Pop", record, policy, out var reason);
            Assert.IsTrue(result);
            Assert.AreEqual(string.Empty, reason);
        }

        [Test]
        public void CanEnableWellbeingFeature_WonderPop_Lowercase_NotBlocked()
        {
            // Ordinal comparison: "wonder_pop" != "Wonder_Pop" so Guard 1 not triggered
            var record = WellbeingRecord.Create("wonder_pop", WellbeingFeature.PressureLoopDisable, false, true);
            var policy = WellbeingPolicy.Safe("Test");
            // Guard 1: gameId "wonder_pop" != "Wonder_Pop" (Ordinal) → skip
            // Guard 2: DarkPatternBlock? No. Skip.
            // Guard 3: EnforceWellbeingFeatures=true, RequiresUserConsent=IsOptional&&!IsUserRequested=false&&!true=false → skip
            var result = AccessibilityWellbeingGate.CanEnableWellbeingFeature("wonder_pop", record, policy, out var reason);
            Assert.IsTrue(result);
        }

        [Test]
        public void CanEnableWellbeingFeature_DarkPattern_NotUserRequested_Blocked()
        {
            var record = WellbeingRecord.Create("AnyGame", WellbeingFeature.DarkPatternBlock, false, false);
            var policy = WellbeingPolicy.Default;
            var result = AccessibilityWellbeingGate.CanEnableWellbeingFeature("AnyGame", record, policy, out var reason);
            Assert.IsFalse(result);
            Assert.AreEqual("DarkPatternBlockRequired", reason);
        }

        [Test]
        public void CanEnableWellbeingFeature_DarkPattern_UserRequested_Eligible()
        {
            var record = WellbeingRecord.Create("AnyGame", WellbeingFeature.DarkPatternBlock, false, true);
            var policy = WellbeingPolicy.Default;
            var result = AccessibilityWellbeingGate.CanEnableWellbeingFeature("AnyGame", record, policy, out var reason);
            Assert.IsTrue(result);
            Assert.AreEqual(string.Empty, reason);
        }

        [Test]
        public void CanEnableWellbeingFeature_OptionalNotRequested_Blocked()
        {
            var record = WellbeingRecord.Create("AnyGame", WellbeingFeature.PlaytimeReminder, true, false);
            var policy = WellbeingPolicy.Default;
            var result = AccessibilityWellbeingGate.CanEnableWellbeingFeature("AnyGame", record, policy, out var reason);
            Assert.IsFalse(result);
            Assert.AreEqual("UserConsentRequired", reason);
        }

        [Test]
        public void CanEnableWellbeingFeature_NonOptional_UserRequested_Eligible()
        {
            var record = WellbeingRecord.Create("AnyGame", WellbeingFeature.AccessibilityControl, false, true);
            var policy = WellbeingPolicy.Default;
            var result = AccessibilityWellbeingGate.CanEnableWellbeingFeature("AnyGame", record, policy, out var reason);
            Assert.IsTrue(result);
            Assert.AreEqual(string.Empty, reason);
        }

        [Test]
        public void CanEnableWellbeingFeature_PlaytimeReminder_Optional_UserRequested_Eligible()
        {
            var record = WellbeingRecord.Create("AnyGame", WellbeingFeature.PlaytimeReminder, true, true);
            var policy = WellbeingPolicy.Default;
            var result = AccessibilityWellbeingGate.CanEnableWellbeingFeature("AnyGame", record, policy, out var reason);
            Assert.IsTrue(result);
            Assert.AreEqual(string.Empty, reason);
        }

        [Test]
        public void IsProtectiveFeature_Helper_DarkPatternBlock_True()
        {
            var r = WellbeingRecord.Create("G", WellbeingFeature.DarkPatternBlock, false, false);
            Assert.IsTrue(AccessibilityWellbeingGate.IsProtectiveFeature(r));
        }

        [Test]
        public void IsProtectiveFeature_Helper_AccessibilityControl_False()
        {
            var r = WellbeingRecord.Create("G", WellbeingFeature.AccessibilityControl, false, false);
            Assert.IsFalse(AccessibilityWellbeingGate.IsProtectiveFeature(r));
        }

        [Test]
        public void RequiresUserConsent_Helper_OptionalNotRequested_True()
        {
            var r = WellbeingRecord.Create("G", WellbeingFeature.PlaytimeReminder, true, false);
            Assert.IsTrue(AccessibilityWellbeingGate.RequiresUserConsent(r));
        }

        [Test]
        public void WellbeingLog_Record_And_Count()
        {
            var log = new WellbeingLog();
            Assert.AreEqual(0, log.Count);
            log.Record("GameA", "PlaytimeReminder");
            log.Record("GameA", "DarkPatternBlock");
            Assert.AreEqual(2, log.Count);
        }

        [Test]
        public void WellbeingLog_HasEntry_True()
        {
            var log = new WellbeingLog();
            log.Record("GameA", "PlaytimeReminder");
            Assert.IsTrue(log.HasEntry("GameA"));
            Assert.IsFalse(log.HasEntry("GameB"));
        }

        [Test]
        public void WellbeingLog_Clear_ResetsCount()
        {
            var log = new WellbeingLog();
            log.Record("GameA", "PlaytimeReminder");
            Assert.AreEqual(1, log.Count);
            log.Clear();
            Assert.AreEqual(0, log.Count);
        }
    }
}
