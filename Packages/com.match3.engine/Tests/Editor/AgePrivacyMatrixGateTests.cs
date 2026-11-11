using NUnit.Framework;
using Match3.Engine.Runtime.Privacy;
using System;

namespace Match3.Engine.Tests.Editor
{
    [TestFixture]
    public class AgePrivacyMatrixGateTests
    {
        [Test]
        public void TitleAgeRecord_Create_NullGameId_Throws()
        {
            Assert.Throws<ArgumentException>(() => TitleAgeRecord.Create(null, AgeClassification.AllAges));
        }

        [Test]
        public void TitleAgeRecord_Create_EmptyGameId_Throws()
        {
            Assert.Throws<ArgumentException>(() => TitleAgeRecord.Create(string.Empty, AgeClassification.AllAges));
        }

        [Test]
        public void TitleAgeRecord_Create_Valid_Succeeds()
        {
            var record = TitleAgeRecord.Create("Coral_Rescue", AgeClassification.Teen);
            Assert.AreEqual("Coral_Rescue", record.GameId);
            Assert.AreEqual(AgeClassification.Teen, record.Classification);
        }

        [Test]
        public void TitleAgeRecord_IsFamilyAppropriate_AllAges_True()
        {
            var record = TitleAgeRecord.Create("Coral_Rescue", AgeClassification.AllAges);
            Assert.IsTrue(record.IsFamilyAppropriate);
        }

        [Test]
        public void TitleAgeRecord_IsFamilyAppropriate_FamilyFriendly_True()
        {
            var record = TitleAgeRecord.Create("Coral_Rescue", AgeClassification.FamilyFriendly);
            Assert.IsTrue(record.IsFamilyAppropriate);
        }

        [Test]
        public void TitleAgeRecord_IsFamilyAppropriate_Teen_False()
        {
            var record = TitleAgeRecord.Create("Coral_Rescue", AgeClassification.Teen);
            Assert.IsFalse(record.IsFamilyAppropriate);
        }

        [Test]
        public void TitleAgeRecord_IsFamilyAppropriate_MatureOnly_False()
        {
            var record = TitleAgeRecord.Create("Coral_Rescue", AgeClassification.MatureOnly);
            Assert.IsFalse(record.IsFamilyAppropriate);
        }

        [Test]
        public void TitleAgeRecord_IsMatureContent_MatureOnly_True()
        {
            var record = TitleAgeRecord.Create("Coral_Rescue", AgeClassification.MatureOnly);
            Assert.IsTrue(record.IsMatureContent);
        }

        [Test]
        public void TitleAgeRecord_IsMatureContent_AllAges_False()
        {
            var record = TitleAgeRecord.Create("Coral_Rescue", AgeClassification.AllAges);
            Assert.IsFalse(record.IsMatureContent);
        }

        [Test]
        public void AgePrivacyPolicy_Safe_Values_Correct()
        {
            var policy = AgePrivacyPolicy.Safe("test desc");
            Assert.IsFalse(policy.AllowBehavioralProfiling);
            Assert.IsTrue(policy.BlockMatureContent);
        }

        [Test]
        public void AgePrivacyPolicy_Default_NotNull()
        {
            var policy = AgePrivacyPolicy.Default;
            Assert.IsNotNull(policy.PolicyDescription);
            Assert.IsNotEmpty(policy.PolicyDescription);
        }

        [Test]
        public void AgePrivacyPolicy_NullDescription_Throws()
        {
            Assert.Throws<ArgumentException>(() => AgePrivacyPolicy.Safe(null));
        }

        [Test]
        public void CanEnableFeature_WonderPop_Blocked()
        {
            var record = TitleAgeRecord.Create("Wonder_Pop", AgeClassification.AllAges);
            var policy = AgePrivacyPolicy.Default;
            bool result = AgePrivacyMatrixGate.CanEnableFeature("Wonder_Pop", record, policy, out string reason);
            Assert.IsFalse(result);
            Assert.AreEqual("WonderPopFeatureRestricted", reason);
        }

        [Test]
        public void CanEnableFeature_WonderPop_Lowercase_UnknownGame()
        {
            // Ordinal catalog lookup: a case variant of Wonder_Pop is not a catalog ID, so it fails closed with UnknownGame (never eligible, never the Wonder_Pop reason).
            var record = TitleAgeRecord.Create("wonder_pop", AgeClassification.AllAges);
            var policy = AgePrivacyPolicy.Default;
            bool result = AgePrivacyMatrixGate.CanEnableFeature("wonder_pop", record, policy, out string reason);
            Assert.IsFalse(result);
            Assert.AreEqual("UnknownGame", reason);
        }

        [Test]
        public void CanEnableFeature_SafePolicy_BehavioralProfilingDisabled_AllAges_Blocked()
        {
            var record = TitleAgeRecord.Create("Coral_Rescue", AgeClassification.AllAges);
            var policy = AgePrivacyPolicy.Safe("safe");
            bool result = AgePrivacyMatrixGate.CanEnableFeature("Coral_Rescue", record, policy, out string reason);
            Assert.IsFalse(result);
            Assert.AreEqual("BehavioralProfilingDisabled", reason);
        }

        [Test]
        public void CanEnableFeature_SafePolicy_BehavioralProfilingDisabled_FamilyFriendly_Blocked()
        {
            var record = TitleAgeRecord.Create("Garage_Legends", AgeClassification.FamilyFriendly);
            var policy = AgePrivacyPolicy.Safe("safe");
            bool result = AgePrivacyMatrixGate.CanEnableFeature("Garage_Legends", record, policy, out string reason);
            Assert.IsFalse(result);
            Assert.AreEqual("BehavioralProfilingDisabled", reason);
        }

        [Test]
        public void CanEnableFeature_AllowBehavioralProfiling_AllAges_MatureBlocked_False_Eligible()
        {
            var record = TitleAgeRecord.Create("Dino_Islands", AgeClassification.AllAges);
            var policy = AgePrivacyPolicy.Permissive("permissive", blockMatureContent: false);
            bool result = AgePrivacyMatrixGate.CanEnableFeature("Dino_Islands", record, policy, out string reason);
            Assert.IsTrue(result);
            Assert.AreEqual(string.Empty, reason);
        }

        [Test]
        public void CanEnableFeature_AllowBehavioralProfiling_MatureOnly_BlockMature_True_Blocked()
        {
            var record = TitleAgeRecord.Create("The_Fractured_Realms", AgeClassification.MatureOnly);
            var policy = AgePrivacyPolicy.Permissive("permissive-block-mature", blockMatureContent: true);
            bool result = AgePrivacyMatrixGate.CanEnableFeature("The_Fractured_Realms", record, policy, out string reason);
            Assert.IsFalse(result);
            Assert.AreEqual("MatureContentBlocked", reason);
        }

        [Test]
        public void CanEnableFeature_AllowBehavioralProfiling_MatureOnly_BlockMature_False_Eligible()
        {
            var record = TitleAgeRecord.Create("The_Fractured_Realms", AgeClassification.MatureOnly);
            var policy = AgePrivacyPolicy.Permissive("permissive-allow-mature", blockMatureContent: false);
            bool result = AgePrivacyMatrixGate.CanEnableFeature("The_Fractured_Realms", record, policy, out string reason);
            Assert.IsTrue(result);
            Assert.AreEqual(string.Empty, reason);
        }

        [Test]
        public void CanEnableFeature_AllowBehavioralProfiling_Teen_BlockMature_True_Eligible()
        {
            var record = TitleAgeRecord.Create("Dino_Islands", AgeClassification.Teen);
            var policy = AgePrivacyPolicy.Permissive("permissive-block-mature", blockMatureContent: true);
            bool result = AgePrivacyMatrixGate.CanEnableFeature("Dino_Islands", record, policy, out string reason);
            Assert.IsTrue(result);
            Assert.AreEqual(string.Empty, reason);
        }

        [Test]
        public void IsFamilyAppropriate_Helper_FamilyFriendly_True()
        {
            var record = TitleAgeRecord.Create("Coral_Rescue", AgeClassification.FamilyFriendly);
            Assert.IsTrue(AgePrivacyMatrixGate.IsFamilyAppropriate(record));
        }

        [Test]
        public void IsFamilyAppropriate_Helper_Teen_False()
        {
            var record = TitleAgeRecord.Create("Coral_Rescue", AgeClassification.Teen);
            Assert.IsFalse(AgePrivacyMatrixGate.IsFamilyAppropriate(record));
        }

        [Test]
        public void IsMatureContent_Helper_MatureOnly_True()
        {
            var record = TitleAgeRecord.Create("Coral_Rescue", AgeClassification.MatureOnly);
            Assert.IsTrue(AgePrivacyMatrixGate.IsMatureContent(record));
        }

        [Test]
        public void IsMatureContent_Helper_AllAges_False()
        {
            var record = TitleAgeRecord.Create("Coral_Rescue", AgeClassification.AllAges);
            Assert.IsFalse(AgePrivacyMatrixGate.IsMatureContent(record));
        }

        [Test]
        public void AgePrivacyLog_Record_And_Count()
        {
            var log = new AgePrivacyLog();
            log.Record("Coral_Rescue", "FeatureX");
            log.Record("Garage_Legends", "FeatureY");
            Assert.AreEqual(2, log.Count);
        }

        [Test]
        public void AgePrivacyLog_HasEntry_True()
        {
            var log = new AgePrivacyLog();
            log.Record("Coral_Rescue", "FeatureX");
            Assert.IsTrue(log.HasEntry("Coral_Rescue"));
        }

        [Test]
        public void AgePrivacyLog_Clear_ResetsCount()
        {
            var log = new AgePrivacyLog();
            log.Record("Coral_Rescue", "FeatureX");
            log.Clear();
            Assert.AreEqual(0, log.Count);
        }

        [Test]
        public void AgePrivacyLog_Record_NullGameId_Throws()
        {
            var log = new AgePrivacyLog();
            Assert.Throws<ArgumentException>(() => log.Record(null, "Feature"));
        }

        // QA remediation: fail-closed default policy, GameId mismatch guard, log argument validation
        [Test]
        public void CanEnableFeature_DefaultStructPolicy_FailsClosed()
        {
            var record = TitleAgeRecord.Create("Coral_Rescue", AgeClassification.AllAges);
            var expected = AgePrivacyMatrixGate.CanEnableFeature("Coral_Rescue", record, AgePrivacyPolicy.Default, out var expectedReason);
            var result = AgePrivacyMatrixGate.CanEnableFeature("Coral_Rescue", record, default(AgePrivacyPolicy), out var reason);
            Assert.IsFalse(expected);
            Assert.IsFalse(result);
            Assert.AreEqual("BehavioralProfilingDisabled", expectedReason);
            Assert.AreEqual(expectedReason, reason);
        }

        [Test]
        public void CanEnableFeature_GameIdMismatch_Blocked()
        {
            var record = TitleAgeRecord.Create("Wonder_Pop", AgeClassification.AllAges);
            var result = AgePrivacyMatrixGate.CanEnableFeature("Coral_Rescue", record, AgePrivacyPolicy.Default, out var reason);
            Assert.IsFalse(result);
            Assert.AreEqual("GameIdMismatch", reason);
        }

        [Test]
        public void AgePrivacyLog_Record_NullOrEmptySecondArg_Throws()
        {
            var log = new AgePrivacyLog();
            Assert.Throws<System.ArgumentException>(() => log.Record("Coral_Rescue", null));
            Assert.Throws<System.ArgumentException>(() => log.Record("Coral_Rescue", string.Empty));
            Assert.AreEqual(0, log.Count);
        }

        [Test]
        public void AgePrivacyLog_HasEntry_IsOrdinal()
        {
            var log = new AgePrivacyLog();
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
                Assert.IsFalse(AgePrivacyMatrixGate.CanEnableFeature(gameId, default(TitleAgeRecord), default(AgePrivacyPolicy), out var reason));
                Assert.AreEqual("GameIdMissing", reason);
            }
        }

        [Test]
        public void UnknownOrCaseVariantGameId_DefaultRecord_UnknownGame()
        {
            foreach (var gameId in new[] { "Unknown_Game", "coral_rescue", "CORAL_RESCUE", " Coral_Rescue", "Coral_Rescue ", "wonder_pop", "WONDER_POP" })
            {
                Assert.IsFalse(AgePrivacyMatrixGate.CanEnableFeature(gameId, default(TitleAgeRecord), default(AgePrivacyPolicy), out var reason));
                Assert.AreEqual("UnknownGame", reason);
            }
        }
    }
}
