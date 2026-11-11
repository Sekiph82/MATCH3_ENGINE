using System;
using NUnit.Framework;
using Match3.Engine.Runtime.Identity;

namespace Match3.Engine.Tests.Editor
{
    [TestFixture]
    public class SensitiveStorageGateTests
    {
        // ---- StorageEntry validation ----

        [Test]
        public void StorageEntry_NullGameId_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() =>
                StorageEntry.Create(null, StorageClassification.Public, "key"));
        }

        [Test]
        public void StorageEntry_EmptyGameId_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() =>
                StorageEntry.Create("", StorageClassification.Public, "key"));
        }

        [Test]
        public void StorageEntry_NullKeyName_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() =>
                StorageEntry.Create("Coral_Rescue", StorageClassification.Public, null));
        }

        [Test]
        public void StorageEntry_EmptyKeyName_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() =>
                StorageEntry.Create("Coral_Rescue", StorageClassification.Public, ""));
        }

        [Test]
        public void StorageEntry_IsSensitive_TrueForSensitive()
        {
            var entry = StorageEntry.Create("Coral_Rescue", StorageClassification.Sensitive, "tok");
            Assert.IsTrue(entry.IsSensitive);
        }

        [Test]
        public void StorageEntry_IsSensitive_FalseForPublic()
        {
            var entry = StorageEntry.Create("Coral_Rescue", StorageClassification.Public, "tok");
            Assert.IsFalse(entry.IsSensitive);
        }

        [Test]
        public void StorageEntry_IsSensitive_FalseForInternal()
        {
            var entry = StorageEntry.Create("Coral_Rescue", StorageClassification.Internal, "tok");
            Assert.IsFalse(entry.IsSensitive);
        }

        // ---- SensitiveStoragePolicy validation ----

        [Test]
        public void SensitiveStoragePolicy_NullDescription_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() =>
                SensitiveStoragePolicy.Safe(null));
        }

        [Test]
        public void SensitiveStoragePolicy_EmptyDescription_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() =>
                SensitiveStoragePolicy.Safe(""));
        }

        [Test]
        public void SensitiveStoragePolicy_Safe_SetsDescription()
        {
            var p = SensitiveStoragePolicy.Safe("desc");
            Assert.AreEqual("desc", p.PolicyDescription);
        }

        [Test]
        public void SensitiveStoragePolicy_ObsoleteFlags_AlwaysEncryptionRequired()
        {
#pragma warning disable 0618
            foreach (var p in AllConstructiblePolicies)
            {
                Assert.IsTrue(p.RequireEncryption);
                Assert.IsFalse(p.AllowPlaintextSensitive);
            }
#pragma warning restore 0618
        }

        private static readonly SensitiveStoragePolicy[] AllConstructiblePolicies =
            { SensitiveStoragePolicy.Safe("desc"), SensitiveStoragePolicy.Default, default(SensitiveStoragePolicy) };

        [Test]
        public void StorageEntry_Create_DefaultsToUnencrypted()
        {
            Assert.IsFalse(StorageEntry.Create("Coral_Rescue", StorageClassification.Sensitive, "k").IsEncrypted);
            Assert.IsTrue(StorageEntry.Create("Coral_Rescue", StorageClassification.Sensitive, "k", true).IsEncrypted);
        }

        // ---- Guard 1: Wonder_Pop blocked (Ordinal) ----

        [Test]
        public void CanStoreSensitive_WonderPop_IsIneligible()
        {
            var entry = StorageEntry.Create("Wonder_Pop", StorageClassification.Public, "k");
            var policy = SensitiveStoragePolicy.Default;
            var (eligible, reason) = SensitiveStorageGate.CanStoreSensitive("Wonder_Pop", entry, policy);
            Assert.IsFalse(eligible);
            Assert.AreEqual("WonderPopStorageProhibited", reason);
        }

        [Test]
        public void CanStoreSensitive_WonderPop_OrdinalCaseDifferent_UnknownGame()
        {
            // Ordinal catalog lookup: a case variant of Wonder_Pop is not a catalog ID, so it fails closed with UnknownGame (never eligible, never the Wonder_Pop reason).
            var entry = StorageEntry.Create("wonder_pop", StorageClassification.Public, "k");
            var policy = SensitiveStoragePolicy.Default;
            var (eligible, reason) = SensitiveStorageGate.CanStoreSensitive("wonder_pop", entry, policy);
            Assert.IsFalse(eligible);
            Assert.AreEqual("UnknownGame", reason);
        }

        [Test]
        public void CanStoreSensitive_WonderPopMixedCase_UnknownGame()
        {
            // Ordinal catalog lookup: a case variant of Wonder_Pop is not a catalog ID, so it fails closed with UnknownGame (never eligible, never the Wonder_Pop reason).
            var entry = StorageEntry.Create("WONDER_POP", StorageClassification.Public, "k");
            var policy = SensitiveStoragePolicy.Default;
            var (eligible, reason) = SensitiveStorageGate.CanStoreSensitive("WONDER_POP", entry, policy);
            Assert.IsFalse(eligible);
            Assert.AreEqual("UnknownGame", reason);
        }

        // ---- Guard 2: PlaintextSensitiveProhibited ----

        [Test]
        public void CanStoreSensitive_UnencryptedSensitive_RejectedUnderEveryPolicy()
        {
            var entry = StorageEntry.Create("Coral_Rescue", StorageClassification.Sensitive, "k");
            foreach (var policy in AllConstructiblePolicies)
            {
                var (eligible, reason) = SensitiveStorageGate.CanStoreSensitive("Coral_Rescue", entry, policy);
                Assert.IsFalse(eligible);
                Assert.AreEqual("PlaintextSensitiveProhibited", reason);
            }
        }

        [Test]
        public void CanStoreSensitive_ExplicitlyUnencryptedSensitive_RejectedUnderEveryPolicy()
        {
            var entry = StorageEntry.Create("Coral_Rescue", StorageClassification.Sensitive, "k", isEncrypted: false);
            foreach (var policy in AllConstructiblePolicies)
                Assert.IsFalse(SensitiveStorageGate.CanStoreSensitive("Coral_Rescue", entry, policy).eligible);
        }

        [Test]
        public void CanStoreSensitive_EncryptedSensitive_EligibleUnderEveryPolicy()
        {
            var entry = StorageEntry.Create("Coral_Rescue", StorageClassification.Sensitive, "k", isEncrypted: true);
            foreach (var policy in AllConstructiblePolicies)
            {
                var (eligible, reason) = SensitiveStorageGate.CanStoreSensitive("Coral_Rescue", entry, policy);
                Assert.IsTrue(eligible);
                Assert.AreEqual("Eligible", reason);
            }
        }

        [Test]
        public void CanStoreSensitive_DefaultStructPolicy_FailsClosed()
        {
            var entry = StorageEntry.Create("Coral_Rescue", StorageClassification.Sensitive, "k");
            var (e1, expected) = SensitiveStorageGate.CanStoreSensitive("Coral_Rescue", entry, SensitiveStoragePolicy.Default);
            var (e2, reason) = SensitiveStorageGate.CanStoreSensitive("Coral_Rescue", entry, default(SensitiveStoragePolicy));
            Assert.IsFalse(e1);
            Assert.IsFalse(e2);
            Assert.AreEqual(expected, reason);
        }

        [Test]
        public void CanStoreSensitive_GameIdMismatch_Blocked()
        {
            var entry = StorageEntry.Create("Wonder_Pop", StorageClassification.Public, "k");
            var (eligible, reason) = SensitiveStorageGate.CanStoreSensitive("Coral_Rescue", entry, SensitiveStoragePolicy.Default);
            Assert.IsFalse(eligible);
            Assert.AreEqual("GameIdMismatch", reason);
        }

        [Test]
        public void CanStoreSensitive_WonderPopParam_EntryDiffers_GameIdMismatch()
        {
            var entry = StorageEntry.Create("Coral_Rescue", StorageClassification.Public, "k");
            var (eligible, reason) = SensitiveStorageGate.CanStoreSensitive("Wonder_Pop", entry, SensitiveStoragePolicy.Default);
            Assert.IsFalse(eligible);
            Assert.AreEqual("GameIdMismatch", reason);
        }

        [Test]
        public void CanStoreSensitive_PublicAnyPolicy_IsEligible()
        {
            var entry = StorageEntry.Create("Coral_Rescue", StorageClassification.Public, "k");
            var policy = SensitiveStoragePolicy.Default;
            var (eligible, _) = SensitiveStorageGate.CanStoreSensitive("Coral_Rescue", entry, policy);
            Assert.IsTrue(eligible);
        }

        // ---- RequiresEncryption ----

        [Test]
        public void RequiresEncryption_EveryPolicy_ReturnsTrue()
        {
            foreach (var policy in AllConstructiblePolicies)
                Assert.IsTrue(SensitiveStorageGate.RequiresEncryption(policy));
        }

        // ---- IsSecureEntry ----

        [Test]
        public void IsSecureEntry_NonSensitive_AlwaysTrue()
        {
            var entry = StorageEntry.Create("Coral_Rescue", StorageClassification.Public, "k");
            var policy = SensitiveStoragePolicy.Safe("desc");
            Assert.IsTrue(SensitiveStorageGate.IsSecureEntry(entry, policy));
        }

        [Test]
        public void IsSecureEntry_SensitiveWithEncryption_IsTrue()
        {
            var entry = StorageEntry.Create("Coral_Rescue", StorageClassification.Sensitive, "k", isEncrypted: true);
            foreach (var policy in AllConstructiblePolicies)
                Assert.IsTrue(SensitiveStorageGate.IsSecureEntry(entry, policy));
        }

        [Test]
        public void IsSecureEntry_SensitiveWithoutEncryption_IsFalseUnderEveryPolicy()
        {
            var entry = StorageEntry.Create("Coral_Rescue", StorageClassification.Sensitive, "k");
            foreach (var policy in AllConstructiblePolicies)
                Assert.IsFalse(SensitiveStorageGate.IsSecureEntry(entry, policy));
        }

        [Test]
        public void StorageAccessLog_Record_DefaultEntry_Throws()
        {
            var log = new StorageAccessLog();
            Assert.Throws<ArgumentException>(() => log.Record(default(StorageEntry), false, "denied"));
        }

        // ---- StorageAccessLog ----

        [Test]
        public void StorageAccessLog_Record_NullReason_ThrowsArgumentException()
        {
            var log = new StorageAccessLog();
            var entry = StorageEntry.Create("Coral_Rescue", StorageClassification.Public, "k");
            Assert.Throws<ArgumentException>(() => log.Record(entry, true, null));
        }

        [Test]
        public void StorageAccessLog_Record_EmptyReason_ThrowsArgumentException()
        {
            var log = new StorageAccessLog();
            var entry = StorageEntry.Create("Coral_Rescue", StorageClassification.Public, "k");
            Assert.Throws<ArgumentException>(() => log.Record(entry, true, ""));
        }

        [Test]
        public void StorageAccessLog_Count_IncrementsOnRecord()
        {
            var log = new StorageAccessLog();
            var entry = StorageEntry.Create("Coral_Rescue", StorageClassification.Public, "k");
            log.Record(entry, true, "ok");
            log.Record(entry, false, "denied");
            Assert.AreEqual(2, log.Count);
        }

        [Test]
        public void StorageAccessLog_Clear_ResetsCount()
        {
            var log = new StorageAccessLog();
            var entry = StorageEntry.Create("Coral_Rescue", StorageClassification.Public, "k");
            log.Record(entry, true, "ok");
            log.Clear();
            Assert.AreEqual(0, log.Count);
        }

        [Test]
        public void StorageAccessLog_HasEntry_TrueAfterRecord()
        {
            var log = new StorageAccessLog();
            var entry = StorageEntry.Create("Coral_Rescue", StorageClassification.Public, "k");
            log.Record(entry, true, "ok");
            Assert.IsTrue(log.HasEntry("Coral_Rescue"));
        }

        [Test]
        public void StorageAccessLog_HasEntry_FalseForMissingGame()
        {
            var log = new StorageAccessLog();
            var entry = StorageEntry.Create("Coral_Rescue", StorageClassification.Public, "k");
            log.Record(entry, true, "ok");
            Assert.IsFalse(log.HasEntry("Garage_Legends"));
        }

        [Test]
        public void StorageAccessLog_HasEntry_FalseAfterClear()
        {
            var log = new StorageAccessLog();
            var entry = StorageEntry.Create("Coral_Rescue", StorageClassification.Public, "k");
            log.Record(entry, true, "ok");
            log.Clear();
            Assert.IsFalse(log.HasEntry("Coral_Rescue"));
        }


        [Test]
        public void NullOrEmptyGameId_DefaultRecord_GameIdMissing()
        {
            foreach (var gameId in new[] { null, string.Empty })
            {
                var (eligible, reason) = SensitiveStorageGate.CanStoreSensitive(gameId, default(StorageEntry), default(SensitiveStoragePolicy));
                Assert.IsFalse(eligible);
                Assert.AreEqual("GameIdMissing", reason);
            }
        }

        [Test]
        public void UnknownOrCaseVariantGameId_DefaultRecord_UnknownGame()
        {
            foreach (var gameId in new[] { "Unknown_Game", "coral_rescue", "CORAL_RESCUE", " Coral_Rescue", "Coral_Rescue ", "wonder_pop", "WONDER_POP" })
            {
                var (eligible, reason) = SensitiveStorageGate.CanStoreSensitive(gameId, default(StorageEntry), default(SensitiveStoragePolicy));
                Assert.IsFalse(eligible);
                Assert.AreEqual("UnknownGame", reason);
            }
        }
    }
}
