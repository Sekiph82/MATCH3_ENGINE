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
            var r = WellbeingRecord.Create("Coral_Rescue", WellbeingFeature.PlaytimeReminder, true, false);
            Assert.AreEqual("Coral_Rescue", r.GameId);
            Assert.AreEqual(WellbeingFeature.PlaytimeReminder, r.Feature);
            Assert.IsTrue(r.IsOptional);
            Assert.IsFalse(r.IsUserRequested);
        }

        [Test]
        public void WellbeingRecord_IsProtectiveFeature_PressureLoopDisable_True()
        {
            var r = WellbeingRecord.Create("Coral_Rescue", WellbeingFeature.PressureLoopDisable, false, false);
            Assert.IsTrue(r.IsProtectiveFeature);
        }

        [Test]
        public void WellbeingRecord_IsProtectiveFeature_DarkPatternBlock_True()
        {
            var r = WellbeingRecord.Create("Coral_Rescue", WellbeingFeature.DarkPatternBlock, false, false);
            Assert.IsTrue(r.IsProtectiveFeature);
        }

        [Test]
        public void WellbeingRecord_IsProtectiveFeature_ContentRatingFilter_True()
        {
            var r = WellbeingRecord.Create("Coral_Rescue", WellbeingFeature.ContentRatingFilter, false, false);
            Assert.IsTrue(r.IsProtectiveFeature);
        }

        [Test]
        public void WellbeingRecord_IsProtectiveFeature_PlaytimeReminder_False()
        {
            var r = WellbeingRecord.Create("Coral_Rescue", WellbeingFeature.PlaytimeReminder, false, false);
            Assert.IsFalse(r.IsProtectiveFeature);
        }

        [Test]
        public void WellbeingRecord_IsProtectiveFeature_AgeAppropriateOffer_False()
        {
            var r = WellbeingRecord.Create("Coral_Rescue", WellbeingFeature.AgeAppropriateOffer, false, false);
            Assert.IsFalse(r.IsProtectiveFeature);
        }

        [Test]
        public void WellbeingRecord_RequiresUserConsent_OptionalNotRequested_True()
        {
            var r = WellbeingRecord.Create("Coral_Rescue", WellbeingFeature.PlaytimeReminder, true, false);
            Assert.IsTrue(r.RequiresUserConsent);
        }

        [Test]
        public void WellbeingRecord_RequiresUserConsent_OptionalUserRequested_False()
        {
            var r = WellbeingRecord.Create("Coral_Rescue", WellbeingFeature.PlaytimeReminder, true, true);
            Assert.IsFalse(r.RequiresUserConsent);
        }

        [Test]
        public void WellbeingRecord_RequiresUserConsent_NotOptional_False()
        {
            var r = WellbeingRecord.Create("Coral_Rescue", WellbeingFeature.PlaytimeReminder, false, false);
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
        public void CanEnableWellbeingFeature_WonderPop_Lowercase_UnknownGame()
        {
            // Ordinal catalog lookup: a case variant of Wonder_Pop is not a catalog ID, so it fails closed with UnknownGame (never eligible, never the Wonder_Pop reason).
            var record = WellbeingRecord.Create("wonder_pop", WellbeingFeature.PressureLoopDisable, false, true);
            var policy = WellbeingPolicy.Safe("Test");
            var result = AccessibilityWellbeingGate.CanEnableWellbeingFeature("wonder_pop", record, policy, out var reason);
            Assert.IsFalse(result);
            Assert.AreEqual("UnknownGame", reason);
        }

        [Test]
        public void CanEnableWellbeingFeature_DarkPattern_NotUserRequested_Blocked()
        {
            var record = WellbeingRecord.Create("Coral_Rescue", WellbeingFeature.DarkPatternBlock, false, false);
            var policy = WellbeingPolicy.Default;
            var result = AccessibilityWellbeingGate.CanEnableWellbeingFeature("Coral_Rescue", record, policy, out var reason);
            Assert.IsFalse(result);
            Assert.AreEqual("DarkPatternBlockRequired", reason);
        }

        [Test]
        public void CanEnableWellbeingFeature_DarkPattern_UserRequested_Eligible()
        {
            var record = WellbeingRecord.Create("Coral_Rescue", WellbeingFeature.DarkPatternBlock, false, true);
            var policy = WellbeingPolicy.Default;
            var result = AccessibilityWellbeingGate.CanEnableWellbeingFeature("Coral_Rescue", record, policy, out var reason);
            Assert.IsTrue(result);
            Assert.AreEqual(string.Empty, reason);
        }

        [Test]
        public void CanEnableWellbeingFeature_OptionalNotRequested_Blocked()
        {
            var record = WellbeingRecord.Create("Coral_Rescue", WellbeingFeature.PlaytimeReminder, true, false);
            var policy = WellbeingPolicy.Default;
            var result = AccessibilityWellbeingGate.CanEnableWellbeingFeature("Coral_Rescue", record, policy, out var reason);
            Assert.IsFalse(result);
            Assert.AreEqual("UserConsentRequired", reason);
        }

        [Test]
        public void CanEnableWellbeingFeature_NonOptional_UserRequested_Eligible()
        {
            var record = WellbeingRecord.Create("Coral_Rescue", WellbeingFeature.AccessibilityControl, false, true);
            var policy = WellbeingPolicy.Default;
            var result = AccessibilityWellbeingGate.CanEnableWellbeingFeature("Coral_Rescue", record, policy, out var reason);
            Assert.IsTrue(result);
            Assert.AreEqual(string.Empty, reason);
        }

        [Test]
        public void CanEnableWellbeingFeature_PlaytimeReminder_Optional_UserRequested_Eligible()
        {
            var record = WellbeingRecord.Create("Coral_Rescue", WellbeingFeature.PlaytimeReminder, true, true);
            var policy = WellbeingPolicy.Default;
            var result = AccessibilityWellbeingGate.CanEnableWellbeingFeature("Coral_Rescue", record, policy, out var reason);
            Assert.IsTrue(result);
            Assert.AreEqual(string.Empty, reason);
        }

        [Test]
        public void IsProtectiveFeature_Helper_DarkPatternBlock_True()
        {
            var r = WellbeingRecord.Create("Coral_Rescue", WellbeingFeature.DarkPatternBlock, false, false);
            Assert.IsTrue(AccessibilityWellbeingGate.IsProtectiveFeature(r));
        }

        [Test]
        public void IsProtectiveFeature_Helper_AccessibilityControl_False()
        {
            var r = WellbeingRecord.Create("Coral_Rescue", WellbeingFeature.AccessibilityControl, false, false);
            Assert.IsFalse(AccessibilityWellbeingGate.IsProtectiveFeature(r));
        }

        [Test]
        public void RequiresUserConsent_Helper_OptionalNotRequested_True()
        {
            var r = WellbeingRecord.Create("Coral_Rescue", WellbeingFeature.PlaytimeReminder, true, false);
            Assert.IsTrue(AccessibilityWellbeingGate.RequiresUserConsent(r));
        }

        [Test]
        public void WellbeingLog_Record_And_Count()
        {
            var log = new WellbeingLog();
            Assert.AreEqual(0, log.Count);
            log.Record("Coral_Rescue", "PlaytimeReminder");
            log.Record("Coral_Rescue", "DarkPatternBlock");
            Assert.AreEqual(2, log.Count);
        }

        [Test]
        public void WellbeingLog_HasEntry_True()
        {
            var log = new WellbeingLog();
            log.Record("Coral_Rescue", "PlaytimeReminder");
            Assert.IsTrue(log.HasEntry("Coral_Rescue"));
            Assert.IsFalse(log.HasEntry("Garage_Legends"));
        }

        [Test]
        public void WellbeingLog_Clear_ResetsCount()
        {
            var log = new WellbeingLog();
            log.Record("Coral_Rescue", "PlaytimeReminder");
            Assert.AreEqual(1, log.Count);
            log.Clear();
            Assert.AreEqual(0, log.Count);
        }

        // QA remediation: fail-closed default policy, GameId mismatch guard, log argument validation
        [Test]
        public void CanEnableWellbeingFeature_DefaultStructPolicy_FailsClosed()
        {
            var record = WellbeingRecord.Create("Coral_Rescue", WellbeingFeature.DarkPatternBlock, false, false);
            var expected = AccessibilityWellbeingGate.CanEnableWellbeingFeature("Coral_Rescue", record, WellbeingPolicy.Default, out var expectedReason);
            var result = AccessibilityWellbeingGate.CanEnableWellbeingFeature("Coral_Rescue", record, default(WellbeingPolicy), out var reason);
            Assert.IsFalse(expected);
            Assert.IsFalse(result);
            Assert.AreEqual("DarkPatternBlockRequired", expectedReason);
            Assert.AreEqual(expectedReason, reason);
        }

        [Test]
        public void CanEnableWellbeingFeature_GameIdMismatch_Blocked()
        {
            var record = WellbeingRecord.Create("Wonder_Pop", WellbeingFeature.PressureLoopDisable, false, true);
            var result = AccessibilityWellbeingGate.CanEnableWellbeingFeature("Coral_Rescue", record, WellbeingPolicy.Default, out var reason);
            Assert.IsFalse(result);
            Assert.AreEqual("GameIdMismatch", reason);
        }

        [Test]
        public void WellbeingLog_Record_NullOrEmptySecondArg_Throws()
        {
            var log = new WellbeingLog();
            Assert.Throws<System.ArgumentException>(() => log.Record("Coral_Rescue", null));
            Assert.Throws<System.ArgumentException>(() => log.Record("Coral_Rescue", string.Empty));
            Assert.AreEqual(0, log.Count);
        }

        [Test]
        public void WellbeingLog_HasEntry_IsOrdinal()
        {
            var log = new WellbeingLog();
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
                Assert.IsFalse(AccessibilityWellbeingGate.CanEnableWellbeingFeature(gameId, default(WellbeingRecord), default(WellbeingPolicy), out var reason));
                Assert.AreEqual("GameIdMissing", reason);
            }
        }

        [Test]
        public void UnknownOrCaseVariantGameId_DefaultRecord_UnknownGame()
        {
            foreach (var gameId in new[] { "Unknown_Game", "coral_rescue", "CORAL_RESCUE", " Coral_Rescue", "Coral_Rescue ", "wonder_pop", "WONDER_POP" })
            {
                Assert.IsFalse(AccessibilityWellbeingGate.CanEnableWellbeingFeature(gameId, default(WellbeingRecord), default(WellbeingPolicy), out var reason));
                Assert.AreEqual("UnknownGame", reason);
            }
        }
    }
}
