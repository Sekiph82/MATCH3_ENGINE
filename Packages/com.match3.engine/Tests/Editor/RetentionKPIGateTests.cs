// RetentionKPIGateTests.cs — NUnit only; no Unity runtime dependency
using System;
using NUnit.Framework;
using Match3.Engine.Runtime.Analytics;

namespace Match3.Engine.Tests.Editor
{
    [TestFixture]
    public class RetentionKPIGateTests
    {
        private static ConsentRecord Consent(string gameId) =>
            ConsentRecord.Create(gameId, ConsentBasis.ExplicitConsent);

        private static readonly RetentionPolicy[] AllConstructiblePolicies =
            { RetentionPolicy.Safe("desc"), RetentionPolicy.Default, default(RetentionPolicy) };

        // ---- RetentionRecord argument guards ----

        [Test]
        public void RetentionRecord_NullGameId_Throws()
            => Assert.Throws<ArgumentException>(() => RetentionRecord.Create(null, RetentionWindow.Day1));

        [Test]
        public void RetentionRecord_EmptyGameId_Throws()
            => Assert.Throws<ArgumentException>(() => RetentionRecord.Create(string.Empty, RetentionWindow.Day7));

        [Test]
        public void RetentionRecord_CreateTestDevice_NullGameId_Throws()
            => Assert.Throws<ArgumentException>(() => RetentionRecord.CreateTestDevice(null, RetentionWindow.Day30));

        // ---- RetentionRecord factories ----

        [Test]
        public void Create_SetsIsTestDeviceFalse()
        {
            var r = RetentionRecord.Create("GameA", RetentionWindow.Day1);
            Assert.IsFalse(r.IsTestDevice);
        }

        [Test]
        public void CreateTestDevice_SetsIsTestDeviceTrue()
        {
            var r = RetentionRecord.CreateTestDevice("GameA", RetentionWindow.Day7);
            Assert.IsTrue(r.IsTestDevice);
        }

        [Test]
        public void IsEligible_FalseWhenTestDevice()
        {
            var r = RetentionRecord.CreateTestDevice("GameA", RetentionWindow.Day30);
            Assert.IsFalse(r.IsEligible);
        }

        [Test]
        public void IsEligible_TrueWhenNotTestDevice()
        {
            var r = RetentionRecord.Create("GameA", RetentionWindow.Day1);
            Assert.IsTrue(r.IsEligible);
        }

        // ---- All RetentionWindow values ----

        [Test]
        public void AllWindowValues_Work()
        {
            var d1 = RetentionRecord.Create("G", RetentionWindow.Day1);
            var d7 = RetentionRecord.Create("G", RetentionWindow.Day7);
            var d30 = RetentionRecord.Create("G", RetentionWindow.Day30);
            Assert.AreEqual(RetentionWindow.Day1, d1.Window);
            Assert.AreEqual(RetentionWindow.Day7, d7.Window);
            Assert.AreEqual(RetentionWindow.Day30, d30.Window);
        }

        // ---- RetentionPolicy argument guards ----

        [Test]
        public void RetentionPolicy_NullDescription_Throws()
            => Assert.Throws<ArgumentException>(() => RetentionPolicy.Safe(null));

        [Test]
        public void RetentionPolicy_EmptyDescription_Throws()
            => Assert.Throws<ArgumentException>(() => RetentionPolicy.Safe(string.Empty));

        // ---- RetentionPolicy Safe / Default ----

        [Test]
        public void Safe_SetsExcludeAndRequire()
        {
            var p = RetentionPolicy.Safe("desc");
            Assert.IsTrue(p.ExcludeTestDevices);
            Assert.IsTrue(RetentionKPIGate.RequiresLawfulBasis(p));
            Assert.AreEqual("desc", p.PolicyDescription);
        }

        [Test]
        public void Default_HasCorrectDescription()
        {
            var p = RetentionPolicy.Default;
            Assert.IsTrue(p.ExcludeTestDevices);
            Assert.IsFalse(string.IsNullOrEmpty(p.PolicyDescription));
        }

        // ---- Guard 1: Wonder_Pop blocked (Ordinal) ----

        [Test]
        public void WonderPop_ExactCase_Blocked()
        {
            var r = RetentionRecord.Create("Wonder_Pop", RetentionWindow.Day1);
            var (eligible, reason) = RetentionKPIGate.CanRecordRetention("Wonder_Pop", r, Consent("Wonder_Pop"), RetentionPolicy.Default);
            Assert.IsFalse(eligible);
            Assert.AreEqual("WonderPopRetentionProhibited", reason);
        }

        [Test]
        public void WonderPop_LowercaseNotBlocked()
        {
            var r = RetentionRecord.Create("wonder_pop", RetentionWindow.Day1);
            var (eligible, _) = RetentionKPIGate.CanRecordRetention("wonder_pop", r, Consent("wonder_pop"), RetentionPolicy.Default);
            // Ordinal: "wonder_pop" is not the Wonder_Pop guard; consented, non-test device → eligible
            Assert.IsTrue(eligible);
        }

        // ---- Guard 2: TestDeviceExcluded ----

        [Test]
        public void TestDevice_WithExcludePolicy_Blocked()
        {
            var r = RetentionRecord.CreateTestDevice("GameB", RetentionWindow.Day7);
            var (eligible, reason) = RetentionKPIGate.CanRecordRetention("GameB", r, Consent("GameB"), RetentionPolicy.Default);
            Assert.IsFalse(eligible);
            Assert.AreEqual("TestDeviceExcluded", reason);
        }

        [Test]
        public void TestDevice_ExcludedByDefaultStructPolicy()
        {
            var r = RetentionRecord.CreateTestDevice("GameB", RetentionWindow.Day7);
            var (eligible, reason) = RetentionKPIGate.CanRecordRetention("GameB", r, Consent("GameB"), default(RetentionPolicy));
            Assert.IsFalse(eligible);
            Assert.AreEqual("TestDeviceExcluded", reason);
        }

        // ---- Unconditional analytics consent ----

        [Test]
        public void NoAnalyticsConsent_BlockedUnderEveryConstructiblePolicy()
        {
            var r = RetentionRecord.Create("GameC", RetentionWindow.Day30);
            var noConsent = new[]
            {
                ConsentRecord.Create("GameC", ConsentBasis.NoneRequired),
                ConsentRecord.Create("GameC", ConsentBasis.ChildSafe),
                ConsentRecord.Create("GameC", ConsentBasis.LegitimateInterest),
                ConsentRecord.Revoke(ConsentRecord.Create("GameC", ConsentBasis.ExplicitConsent)),
            };
            foreach (var policy in AllConstructiblePolicies)
                foreach (var consent in noConsent)
                {
                    var (eligible, reason) = RetentionKPIGate.CanRecordRetention("GameC", r, consent, policy);
                    Assert.IsFalse(eligible);
                    Assert.AreEqual("AnalyticsConsentRequired", reason);
                }
        }

        [Test]
        public void ObsoleteOverload_ConsentUnknown_FailsClosed()
        {
            var r = RetentionRecord.Create("GameC", RetentionWindow.Day1);
#pragma warning disable 0618
            foreach (var policy in AllConstructiblePolicies)
            {
                var (eligible, reason) = RetentionKPIGate.CanRecordRetention("GameC", r, policy);
                Assert.IsFalse(eligible);
                Assert.AreEqual("AnalyticsConsentRequired", reason);
            }
#pragma warning restore 0618
        }

        [Test]
        public void DefaultStructPolicy_FailsClosed()
        {
            var r = RetentionRecord.CreateTestDevice("GameC", RetentionWindow.Day1);
            var (e1, expected) = RetentionKPIGate.CanRecordRetention("GameC", r, Consent("GameC"), RetentionPolicy.Default);
            var (e2, reason) = RetentionKPIGate.CanRecordRetention("GameC", r, Consent("GameC"), default(RetentionPolicy));
            Assert.IsFalse(e1);
            Assert.IsFalse(e2);
            Assert.AreEqual(expected, reason);
        }

        [Test]
        public void GameIdMismatch_Blocked()
        {
            var r = RetentionRecord.Create("Wonder_Pop", RetentionWindow.Day1);
            var (eligible, reason) = RetentionKPIGate.CanRecordRetention("Coral_Rescue", r, Consent("Coral_Rescue"), RetentionPolicy.Default);
            Assert.IsFalse(eligible);
            Assert.AreEqual("GameIdMismatch", reason);
        }

        [Test]
        public void WonderPopParam_RecordDiffers_GameIdMismatch()
        {
            var r = RetentionRecord.Create("Coral_Rescue", RetentionWindow.Day1);
            var (eligible, reason) = RetentionKPIGate.CanRecordRetention("Wonder_Pop", r, Consent("Wonder_Pop"), RetentionPolicy.Default);
            Assert.IsFalse(eligible);
            Assert.AreEqual("GameIdMismatch", reason);
        }

        [Test]
        public void ConsentForOtherGame_GameIdMismatch()
        {
            var r = RetentionRecord.Create("Coral_Rescue", RetentionWindow.Day1);
            var (eligible, reason) = RetentionKPIGate.CanRecordRetention("Coral_Rescue", r, Consent("Wonder_Pop"), RetentionPolicy.Default);
            Assert.IsFalse(eligible);
            Assert.AreEqual("GameIdMismatch", reason);
        }

        // ---- Helpers ----

        [Test]
        public void IsEligibleDevice_ReturnsRecordIsEligible()
        {
            var r = RetentionRecord.Create("GameD", RetentionWindow.Day1);
            Assert.IsTrue(RetentionKPIGate.IsEligibleDevice(r));
        }

        [Test]
        public void IsEligibleDevice_FalseForTestDevice()
        {
            var r = RetentionRecord.CreateTestDevice("GameD", RetentionWindow.Day1);
            Assert.IsFalse(RetentionKPIGate.IsEligibleDevice(r));
        }

        [Test]
        public void RequiresLawfulBasis_AlwaysTrue()
        {
            foreach (var policy in AllConstructiblePolicies)
                Assert.IsTrue(RetentionKPIGate.RequiresLawfulBasis(policy));
        }

        // ---- RetentionLog ----

        [Test]
        public void RetentionLog_Record_IncreasesCount()
        {
            var log = new RetentionLog();
            log.Record("GameX", RetentionWindow.Day1);
            Assert.AreEqual(1, log.Count);
        }

        [Test]
        public void RetentionLog_HasEntry_TrueAfterRecord()
        {
            var log = new RetentionLog();
            log.Record("GameX", RetentionWindow.Day7);
            Assert.IsTrue(log.HasEntry("GameX"));
        }

        [Test]
        public void RetentionLog_HasEntry_FalseForUnrecorded()
        {
            var log = new RetentionLog();
            Assert.IsFalse(log.HasEntry("GameX"));
        }

        [Test]
        public void RetentionLog_Clear_ResetsCount()
        {
            var log = new RetentionLog();
            log.Record("GameX", RetentionWindow.Day30);
            log.Clear();
            Assert.AreEqual(0, log.Count);
        }

        [Test]
        public void RetentionLog_Record_NullGameId_Throws()
        {
            var log = new RetentionLog();
            Assert.Throws<ArgumentException>(() => log.Record(null, RetentionWindow.Day1));
        }

        [Test]
        public void RetentionLog_Record_EmptyGameId_Throws()
        {
            var log = new RetentionLog();
            Assert.Throws<ArgumentException>(() => log.Record(string.Empty, RetentionWindow.Day7));
        }

        // ---- Eligible non-WonderPop non-testdevice passes all guards ----

        [Test]
        public void EligibleRecord_PassesAllGuards()
        {
            var r = RetentionRecord.Create("GameZ", RetentionWindow.Day30);
            var (eligible, reason) = RetentionKPIGate.CanRecordRetention("GameZ", r, Consent("GameZ"), RetentionPolicy.Default);
            Assert.IsTrue(eligible);
            Assert.AreEqual(string.Empty, reason);
        }


        [Test]
        public void NullOrEmptyGameId_DefaultRecord_GameIdMissing()
        {
            foreach (var gameId in new[] { null, string.Empty })
            {
                var (eligible, reason) = RetentionKPIGate.CanRecordRetention(gameId, default(RetentionRecord), default(ConsentRecord), default(RetentionPolicy));
                Assert.IsFalse(eligible);
                Assert.AreEqual("GameIdMissing", reason);
                #pragma warning disable 0618
                var (oldEligible, oldReason) = RetentionKPIGate.CanRecordRetention(gameId, default(RetentionRecord), default(RetentionPolicy));
                #pragma warning restore 0618
                Assert.IsFalse(oldEligible);
                Assert.AreEqual("GameIdMissing", oldReason);
            }
        }
    }
}
