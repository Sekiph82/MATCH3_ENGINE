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
            var record = TitleAgeRecord.Create("TestGame", AgeClassification.Teen);
            Assert.AreEqual("TestGame", record.GameId);
            Assert.AreEqual(AgeClassification.Teen, record.Classification);
        }

        [Test]
        public void TitleAgeRecord_IsFamilyAppropriate_AllAges_True()
        {
            var record = TitleAgeRecord.Create("G", AgeClassification.AllAges);
            Assert.IsTrue(record.IsFamilyAppropriate);
        }

        [Test]
        public void TitleAgeRecord_IsFamilyAppropriate_FamilyFriendly_True()
        {
            var record = TitleAgeRecord.Create("G", AgeClassification.FamilyFriendly);
            Assert.IsTrue(record.IsFamilyAppropriate);
        }

        [Test]
        public void TitleAgeRecord_IsFamilyAppropriate_Teen_False()
        {
            var record = TitleAgeRecord.Create("G", AgeClassification.Teen);
            Assert.IsFalse(record.IsFamilyAppropriate);
        }

        [Test]
        public void TitleAgeRecord_IsFamilyAppropriate_MatureOnly_False()
        {
            var record = TitleAgeRecord.Create("G", AgeClassification.MatureOnly);
            Assert.IsFalse(record.IsFamilyAppropriate);
        }

        [Test]
        public void TitleAgeRecord_IsMatureContent_MatureOnly_True()
        {
            var record = TitleAgeRecord.Create("G", AgeClassification.MatureOnly);
            Assert.IsTrue(record.IsMatureContent);
        }

        [Test]
        public void TitleAgeRecord_IsMatureContent_AllAges_False()
        {
            var record = TitleAgeRecord.Create("G", AgeClassification.AllAges);
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
        public void CanEnableFeature_WonderPop_Lowercase_NotBlocked()
        {
            // Ordinal: "wonder_pop" != "Wonder_Pop" so guard 1 does not fire
            // Safe policy blocks behavioral profiling, so guard 2 fires
            var record = TitleAgeRecord.Create("wonder_pop", AgeClassification.AllAges);
            var policy = AgePrivacyPolicy.Default;
            bool result = AgePrivacyMatrixGate.CanEnableFeature("wonder_pop", record, policy, out string reason);
            Assert.IsFalse(result);
            Assert.AreEqual("BehavioralProfilingDisabled", reason);
        }

        [Test]
        public void CanEnableFeature_SafePolicy_BehavioralProfilingDisabled_AllAges_Blocked()
        {
            var record = TitleAgeRecord.Create("GameA", AgeClassification.AllAges);
            var policy = AgePrivacyPolicy.Safe("safe");
            bool result = AgePrivacyMatrixGate.CanEnableFeature("GameA", record, policy, out string reason);
            Assert.IsFalse(result);
            Assert.AreEqual("BehavioralProfilingDisabled", reason);
        }

        [Test]
        public void CanEnableFeature_SafePolicy_BehavioralProfilingDisabled_FamilyFriendly_Blocked()
        {
            var record = TitleAgeRecord.Create("GameB", AgeClassification.FamilyFriendly);
            var policy = AgePrivacyPolicy.Safe("safe");
            bool result = AgePrivacyMatrixGate.CanEnableFeature("GameB", record, policy, out string reason);
            Assert.IsFalse(result);
            Assert.AreEqual("BehavioralProfilingDisabled", reason);
        }

        [Test]
        public void CanEnableFeature_AllowBehavioralProfiling_AllAges_MatureBlocked_False_Eligible()
        {
            var record = TitleAgeRecord.Create("GameC", AgeClassification.AllAges);
            var policy = AgePrivacyPolicy.Permissive("permissive", blockMatureContent: false);
            bool result = AgePrivacyMatrixGate.CanEnableFeature("GameC", record, policy, out string reason);
            Assert.IsTrue(result);
            Assert.AreEqual(string.Empty, reason);
        }

        [Test]
        public void CanEnableFeature_AllowBehavioralProfiling_MatureOnly_BlockMature_True_Blocked()
        {
            var record = TitleAgeRecord.Create("GameD", AgeClassification.MatureOnly);
            var policy = AgePrivacyPolicy.Permissive("permissive-block-mature", blockMatureContent: true);
            bool result = AgePrivacyMatrixGate.CanEnableFeature("GameD", record, policy, out string reason);
            Assert.IsFalse(result);
            Assert.AreEqual("MatureContentBlocked", reason);
        }

        [Test]
        public void CanEnableFeature_AllowBehavioralProfiling_MatureOnly_BlockMature_False_Eligible()
        {
            var record = TitleAgeRecord.Create("GameE", AgeClassification.MatureOnly);
            var policy = AgePrivacyPolicy.Permissive("permissive-allow-mature", blockMatureContent: false);
            bool result = AgePrivacyMatrixGate.CanEnableFeature("GameE", record, policy, out string reason);
            Assert.IsTrue(result);
            Assert.AreEqual(string.Empty, reason);
        }

        [Test]
        public void CanEnableFeature_AllowBehavioralProfiling_Teen_BlockMature_True_Eligible()
        {
            var record = TitleAgeRecord.Create("GameF", AgeClassification.Teen);
            var policy = AgePrivacyPolicy.Permissive("permissive-block-mature", blockMatureContent: true);
            bool result = AgePrivacyMatrixGate.CanEnableFeature("GameF", record, policy, out string reason);
            Assert.IsTrue(result);
            Assert.AreEqual(string.Empty, reason);
        }

        [Test]
        public void IsFamilyAppropriate_Helper_FamilyFriendly_True()
        {
            var record = TitleAgeRecord.Create("G", AgeClassification.FamilyFriendly);
            Assert.IsTrue(AgePrivacyMatrixGate.IsFamilyAppropriate(record));
        }

        [Test]
        public void IsFamilyAppropriate_Helper_Teen_False()
        {
            var record = TitleAgeRecord.Create("G", AgeClassification.Teen);
            Assert.IsFalse(AgePrivacyMatrixGate.IsFamilyAppropriate(record));
        }

        [Test]
        public void IsMatureContent_Helper_MatureOnly_True()
        {
            var record = TitleAgeRecord.Create("G", AgeClassification.MatureOnly);
            Assert.IsTrue(AgePrivacyMatrixGate.IsMatureContent(record));
        }

        [Test]
        public void IsMatureContent_Helper_AllAges_False()
        {
            var record = TitleAgeRecord.Create("G", AgeClassification.AllAges);
            Assert.IsFalse(AgePrivacyMatrixGate.IsMatureContent(record));
        }

        [Test]
        public void AgePrivacyLog_Record_And_Count()
        {
            var log = new AgePrivacyLog();
            log.Record("GameA", "FeatureX");
            log.Record("GameB", "FeatureY");
            Assert.AreEqual(2, log.Count);
        }

        [Test]
        public void AgePrivacyLog_HasEntry_True()
        {
            var log = new AgePrivacyLog();
            log.Record("GameA", "FeatureX");
            Assert.IsTrue(log.HasEntry("GameA"));
        }

        [Test]
        public void AgePrivacyLog_Clear_ResetsCount()
        {
            var log = new AgePrivacyLog();
            log.Record("GameA", "FeatureX");
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
            var record = TitleAgeRecord.Create("GameA", AgeClassification.AllAges);
            var expected = AgePrivacyMatrixGate.CanEnableFeature("GameA", record, AgePrivacyPolicy.Default, out var expectedReason);
            var result = AgePrivacyMatrixGate.CanEnableFeature("GameA", record, default(AgePrivacyPolicy), out var reason);
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
            Assert.Throws<System.ArgumentException>(() => log.Record("GameA", null));
            Assert.Throws<System.ArgumentException>(() => log.Record("GameA", string.Empty));
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
            log.Record("Game\u00ADA", "evt");
            Assert.IsTrue(log.HasEntry("Game\u00ADA"));
            Assert.IsFalse(log.HasEntry("GameA"));
        }
    }
}
