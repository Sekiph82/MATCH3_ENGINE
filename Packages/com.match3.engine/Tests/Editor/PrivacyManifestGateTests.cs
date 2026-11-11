using System;
using NUnit.Framework;
using Match3.Engine.Runtime.Privacy;

namespace Match3.Engine.Tests.Editor
{
    [TestFixture]
    public class PrivacyManifestGateTests
    {
        // PrivacyManifestRecord tests

        [Test]
        public void PrivacyManifestRecord_Create_NullGameId_Throws()
        {
            Assert.Throws<ArgumentException>(() =>
                PrivacyManifestRecord.Create(null, PrivacyDataCategory.UsageData, false, false));
        }

        [Test]
        public void PrivacyManifestRecord_Create_EmptyGameId_Throws()
        {
            Assert.Throws<ArgumentException>(() =>
                PrivacyManifestRecord.Create(string.Empty, PrivacyDataCategory.UsageData, false, false));
        }

        [Test]
        public void PrivacyManifestRecord_Create_Valid_Succeeds()
        {
            var record = PrivacyManifestRecord.Create("Coral_Rescue", PrivacyDataCategory.UsageData, true, false);
            Assert.AreEqual("Coral_Rescue", record.GameId);
            Assert.AreEqual(PrivacyDataCategory.UsageData, record.DataCategory);
            Assert.IsTrue(record.HasSdkDisclosure);
            Assert.IsFalse(record.IsRequiredReasonApi);
        }

        [Test]
        public void PrivacyManifestRecord_RequiresDisclosure_DeviceIdentifiers_True()
        {
            var record = PrivacyManifestRecord.Create("Coral_Rescue", PrivacyDataCategory.DeviceIdentifiers, false, false);
            Assert.IsTrue(record.RequiresDisclosure);
        }

        [Test]
        public void PrivacyManifestRecord_RequiresDisclosure_NoDataCollected_False()
        {
            var record = PrivacyManifestRecord.Create("Coral_Rescue", PrivacyDataCategory.NoDataCollected, false, false);
            Assert.IsFalse(record.RequiresDisclosure);
        }

        [Test]
        public void PrivacyManifestRecord_IsSdkCompliant_HasDisclosure_True()
        {
            var record = PrivacyManifestRecord.Create("Coral_Rescue", PrivacyDataCategory.UsageData, true, false);
            Assert.IsTrue(record.IsSdkCompliant);
        }

        [Test]
        public void PrivacyManifestRecord_IsSdkCompliant_NoData_True()
        {
            var record = PrivacyManifestRecord.Create("Coral_Rescue", PrivacyDataCategory.NoDataCollected, false, false);
            Assert.IsTrue(record.IsSdkCompliant);
        }

        [Test]
        public void PrivacyManifestRecord_IsSdkCompliant_NoDisclosure_UsageData_False()
        {
            var record = PrivacyManifestRecord.Create("Coral_Rescue", PrivacyDataCategory.UsageData, false, false);
            Assert.IsFalse(record.IsSdkCompliant);
        }

        // PrivacyManifestPolicy tests

        [Test]
        public void PrivacyManifestPolicy_Safe_Values_Correct()
        {
            var policy = PrivacyManifestPolicy.Safe("Test policy");
            Assert.IsTrue(policy.RequireSdkDisclosure);
            Assert.IsTrue(policy.BlockUndisclosedTracking);
            Assert.AreEqual("Test policy", policy.PolicyDescription);
        }

        [Test]
        public void PrivacyManifestPolicy_Default_NotNull()
        {
            var policy = PrivacyManifestPolicy.Default;
            Assert.IsNotNull(policy.PolicyDescription);
            Assert.IsTrue(policy.RequireSdkDisclosure);
            Assert.IsTrue(policy.BlockUndisclosedTracking);
        }

        [Test]
        public void PrivacyManifestPolicy_NullDescription_Throws()
        {
            Assert.Throws<ArgumentException>(() => PrivacyManifestPolicy.Safe(null));
        }

        // CanDeclareDataUsage tests

        [Test]
        public void CanDeclareDataUsage_WonderPop_Blocked()
        {
            var record = PrivacyManifestRecord.Create("Wonder_Pop", PrivacyDataCategory.NoDataCollected, true, false);
            var policy = PrivacyManifestPolicy.Default;
            var result = PrivacyManifestGate.CanDeclareDataUsage("Wonder_Pop", record, policy, out var reason);
            Assert.IsFalse(result);
            Assert.AreEqual("WonderPopManifestProhibited", reason);
        }

        [Test]
        public void CanDeclareDataUsage_WonderPop_Lowercase_UnknownGame()
        {
            // Ordinal catalog lookup: a case variant of Wonder_Pop is not a catalog ID, so it fails closed with UnknownGame (never eligible, never the Wonder_Pop reason).
            var record = PrivacyManifestRecord.Create("wonder_pop", PrivacyDataCategory.NoDataCollected, true, false);
            var policy = PrivacyManifestPolicy.Default;
            var result = PrivacyManifestGate.CanDeclareDataUsage("wonder_pop", record, policy, out var reason);
            Assert.IsFalse(result);
            Assert.AreEqual("UnknownGame", reason);
        }

        [Test]
        public void CanDeclareDataUsage_UndisclosedTracking_Blocked()
        {
            var record = PrivacyManifestRecord.Create("Coral_Rescue", PrivacyDataCategory.DeviceIdentifiers, false, false);
            var policy = PrivacyManifestPolicy.Safe("Block tracking policy");
            var result = PrivacyManifestGate.CanDeclareDataUsage("Coral_Rescue", record, policy, out var reason);
            Assert.IsFalse(result);
            Assert.AreEqual("UndisclosedTrackingBlocked", reason);
        }

        [Test]
        public void CanDeclareDataUsage_HasDisclosure_RequiresDisclosure_Eligible()
        {
            var record = PrivacyManifestRecord.Create("Coral_Rescue", PrivacyDataCategory.DeviceIdentifiers, true, false);
            var policy = PrivacyManifestPolicy.Safe("Safe policy");
            var result = PrivacyManifestGate.CanDeclareDataUsage("Coral_Rescue", record, policy, out var reason);
            Assert.IsTrue(result);
            Assert.AreEqual(string.Empty, reason);
        }

        [Test]
        public void CanDeclareDataUsage_RequiredReasonApi_NoDisclosure_Blocked()
        {
            var record = PrivacyManifestRecord.Create("Coral_Rescue", PrivacyDataCategory.NoDataCollected, false, true);
            var policy = PrivacyManifestPolicy.Safe("Require SDK disclosure policy");
            var result = PrivacyManifestGate.CanDeclareDataUsage("Coral_Rescue", record, policy, out var reason);
            Assert.IsFalse(result);
            Assert.AreEqual("SdkDisclosureRequired", reason);
        }

        [Test]
        public void CanDeclareDataUsage_RequiredReasonApi_HasDisclosure_Eligible()
        {
            var record = PrivacyManifestRecord.Create("Coral_Rescue", PrivacyDataCategory.NoDataCollected, true, true);
            var policy = PrivacyManifestPolicy.Safe("Safe policy");
            var result = PrivacyManifestGate.CanDeclareDataUsage("Coral_Rescue", record, policy, out var reason);
            Assert.IsTrue(result);
            Assert.AreEqual(string.Empty, reason);
        }

        [Test]
        public void CanDeclareDataUsage_NoDataCollected_SafePolicy_Eligible()
        {
            var record = PrivacyManifestRecord.Create("Coral_Rescue", PrivacyDataCategory.NoDataCollected, false, false);
            var policy = PrivacyManifestPolicy.Safe("Safe policy");
            var result = PrivacyManifestGate.CanDeclareDataUsage("Coral_Rescue", record, policy, out var reason);
            Assert.IsTrue(result);
            Assert.AreEqual(string.Empty, reason);
        }

        [Test]
        public void CanDeclareDataUsage_DiagnosticData_HasDisclosure_Eligible()
        {
            var record = PrivacyManifestRecord.Create("Coral_Rescue", PrivacyDataCategory.DiagnosticData, true, false);
            var policy = PrivacyManifestPolicy.Safe("Safe policy");
            var result = PrivacyManifestGate.CanDeclareDataUsage("Coral_Rescue", record, policy, out var reason);
            Assert.IsTrue(result);
            Assert.AreEqual(string.Empty, reason);
        }

        [Test]
        public void CanDeclareDataUsage_LocationData_NoDisclosure_Blocked()
        {
            var record = PrivacyManifestRecord.Create("Coral_Rescue", PrivacyDataCategory.LocationData, false, false);
            var policy = PrivacyManifestPolicy.Safe("Safe policy");
            var result = PrivacyManifestGate.CanDeclareDataUsage("Coral_Rescue", record, policy, out var reason);
            Assert.IsFalse(result);
            Assert.AreEqual("UndisclosedTrackingBlocked", reason);
        }

        // Helper method tests

        [Test]
        public void RequiresDisclosure_Helper_UsageData_True()
        {
            var record = PrivacyManifestRecord.Create("Coral_Rescue", PrivacyDataCategory.UsageData, false, false);
            Assert.IsTrue(PrivacyManifestGate.RequiresDisclosure(record));
        }

        [Test]
        public void RequiresDisclosure_Helper_NoDataCollected_False()
        {
            var record = PrivacyManifestRecord.Create("Coral_Rescue", PrivacyDataCategory.NoDataCollected, false, false);
            Assert.IsFalse(PrivacyManifestGate.RequiresDisclosure(record));
        }

        [Test]
        public void IsSdkCompliant_Helper_HasDisclosure_True()
        {
            var record = PrivacyManifestRecord.Create("Coral_Rescue", PrivacyDataCategory.ContactInfo, true, false);
            Assert.IsTrue(PrivacyManifestGate.IsSdkCompliant(record));
        }

        [Test]
        public void IsSdkCompliant_Helper_NoDataNoDisclosure_True()
        {
            var record = PrivacyManifestRecord.Create("Coral_Rescue", PrivacyDataCategory.NoDataCollected, false, false);
            Assert.IsTrue(PrivacyManifestGate.IsSdkCompliant(record));
        }

        // PrivacyManifestLog tests

        [Test]
        public void PrivacyManifestLog_Record_And_Count()
        {
            var log = new PrivacyManifestLog();
            log.Record("Coral_Rescue", "UsageData");
            log.Record("Garage_Legends", "DiagnosticData");
            Assert.AreEqual(2, log.Count);
        }

        [Test]
        public void PrivacyManifestLog_HasEntry_True()
        {
            var log = new PrivacyManifestLog();
            log.Record("Coral_Rescue", "UsageData");
            Assert.IsTrue(log.HasEntry("Coral_Rescue"));
            Assert.IsFalse(log.HasEntry("Garage_Legends"));
        }

        [Test]
        public void PrivacyManifestLog_Clear_ResetsCount()
        {
            var log = new PrivacyManifestLog();
            log.Record("Coral_Rescue", "UsageData");
            log.Clear();
            Assert.AreEqual(0, log.Count);
        }

        [Test]
        public void PrivacyManifestLog_Record_NullGameId_Throws()
        {
            var log = new PrivacyManifestLog();
            Assert.Throws<ArgumentException>(() => log.Record(null, "UsageData"));
        }

        // QA remediation: fail-closed default policy, GameId mismatch guard, log argument validation
        [Test]
        public void CanDeclareDataUsage_DefaultStructPolicy_FailsClosed()
        {
            var record = PrivacyManifestRecord.Create("Coral_Rescue", PrivacyDataCategory.UsageData, false, false);
            var expected = PrivacyManifestGate.CanDeclareDataUsage("Coral_Rescue", record, PrivacyManifestPolicy.Default, out var expectedReason);
            var result = PrivacyManifestGate.CanDeclareDataUsage("Coral_Rescue", record, default(PrivacyManifestPolicy), out var reason);
            Assert.IsFalse(expected);
            Assert.IsFalse(result);
            Assert.AreEqual("UndisclosedTrackingBlocked", expectedReason);
            Assert.AreEqual(expectedReason, reason);
        }

        [Test]
        public void CanDeclareDataUsage_GameIdMismatch_Blocked()
        {
            var record = PrivacyManifestRecord.Create("Wonder_Pop", PrivacyDataCategory.NoDataCollected, false, false);
            var result = PrivacyManifestGate.CanDeclareDataUsage("Coral_Rescue", record, PrivacyManifestPolicy.Default, out var reason);
            Assert.IsFalse(result);
            Assert.AreEqual("GameIdMismatch", reason);
        }

        [Test]
        public void PrivacyManifestLog_Record_NullOrEmptySecondArg_Throws()
        {
            var log = new PrivacyManifestLog();
            Assert.Throws<System.ArgumentException>(() => log.Record("Coral_Rescue", null));
            Assert.Throws<System.ArgumentException>(() => log.Record("Coral_Rescue", string.Empty));
            Assert.AreEqual(0, log.Count);
        }

        [Test]
        public void PrivacyManifestLog_HasEntry_IsOrdinal()
        {
            var log = new PrivacyManifestLog();
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
                Assert.IsFalse(PrivacyManifestGate.CanDeclareDataUsage(gameId, default(PrivacyManifestRecord), default(PrivacyManifestPolicy), out var reason));
                Assert.AreEqual("GameIdMissing", reason);
            }
        }

        [Test]
        public void UnknownOrCaseVariantGameId_DefaultRecord_UnknownGame()
        {
            foreach (var gameId in new[] { "Unknown_Game", "coral_rescue", "CORAL_RESCUE", " Coral_Rescue", "Coral_Rescue ", "wonder_pop", "WONDER_POP" })
            {
                Assert.IsFalse(PrivacyManifestGate.CanDeclareDataUsage(gameId, default(PrivacyManifestRecord), default(PrivacyManifestPolicy), out var reason));
                Assert.AreEqual("UnknownGame", reason);
            }
        }
    }
}
