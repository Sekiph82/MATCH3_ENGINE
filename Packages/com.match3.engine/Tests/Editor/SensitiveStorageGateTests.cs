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
                StorageEntry.Create("GameA", StorageClassification.Public, null));
        }

        [Test]
        public void StorageEntry_EmptyKeyName_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() =>
                StorageEntry.Create("GameA", StorageClassification.Public, ""));
        }

        [Test]
        public void StorageEntry_IsSensitive_TrueForSensitive()
        {
            var entry = StorageEntry.Create("GameA", StorageClassification.Sensitive, "tok");
            Assert.IsTrue(entry.IsSensitive);
        }

        [Test]
        public void StorageEntry_IsSensitive_FalseForPublic()
        {
            var entry = StorageEntry.Create("GameA", StorageClassification.Public, "tok");
            Assert.IsFalse(entry.IsSensitive);
        }

        [Test]
        public void StorageEntry_IsSensitive_FalseForInternal()
        {
            var entry = StorageEntry.Create("GameA", StorageClassification.Internal, "tok");
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
        public void SensitiveStoragePolicy_Safe_SetsFlags()
        {
            var p = SensitiveStoragePolicy.Safe("desc");
            Assert.IsTrue(p.RequireEncryption);
            Assert.IsFalse(p.AllowPlaintextSensitive);
            Assert.AreEqual("desc", p.PolicyDescription);
        }

        [Test]
        public void SensitiveStoragePolicy_Default_IsEncryptionRequired()
        {
            var p = SensitiveStoragePolicy.Default;
            Assert.IsTrue(p.RequireEncryption);
            Assert.IsFalse(p.AllowPlaintextSensitive);
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
        public void CanStoreSensitive_WonderPop_OrdinalCaseDifferent_NotBlocked()
        {
            // "wonder_pop" lowercase is not "Wonder_Pop" under Ordinal — must pass through
            var entry = StorageEntry.Create("wonder_pop", StorageClassification.Public, "k");
            var policy = SensitiveStoragePolicy.Default;
            var (eligible, _) = SensitiveStorageGate.CanStoreSensitive("wonder_pop", entry, policy);
            Assert.IsTrue(eligible);
        }

        [Test]
        public void CanStoreSensitive_WonderPopMixedCase_NotBlocked()
        {
            var entry = StorageEntry.Create("WONDER_POP", StorageClassification.Public, "k");
            var policy = SensitiveStoragePolicy.Default;
            var (eligible, _) = SensitiveStorageGate.CanStoreSensitive("WONDER_POP", entry, policy);
            Assert.IsTrue(eligible);
        }

        // ---- Guard 2: PlaintextSensitiveProhibited ----

        [Test]
        public void CanStoreSensitive_SensitiveAllowPlaintext_IsIneligible()
        {
            var entry = StorageEntry.Create("GameA", StorageClassification.Sensitive, "k");
            // Build a policy that allows plaintext
            var policy = new SensitiveStoragePolicy_TestHelper(true, true, "permissive");
            var (eligible, reason) = SensitiveStorageGate.CanStoreSensitive("GameA", entry, policy.Build());
            Assert.IsFalse(eligible);
            Assert.AreEqual("PlaintextSensitiveProhibited", reason);
        }

        [Test]
        public void CanStoreSensitive_SensitiveSafePolicy_IsEligible()
        {
            var entry = StorageEntry.Create("GameA", StorageClassification.Sensitive, "k");
            var policy = SensitiveStoragePolicy.Default;
            var (eligible, _) = SensitiveStorageGate.CanStoreSensitive("GameA", entry, policy);
            Assert.IsTrue(eligible);
        }

        [Test]
        public void CanStoreSensitive_PublicAnyPolicy_IsEligible()
        {
            var entry = StorageEntry.Create("GameA", StorageClassification.Public, "k");
            var policy = SensitiveStoragePolicy.Default;
            var (eligible, _) = SensitiveStorageGate.CanStoreSensitive("GameA", entry, policy);
            Assert.IsTrue(eligible);
        }

        // ---- RequiresEncryption ----

        [Test]
        public void RequiresEncryption_SafePolicy_ReturnsTrue()
        {
            Assert.IsTrue(SensitiveStorageGate.RequiresEncryption(SensitiveStoragePolicy.Default));
        }

        // ---- IsSecureEntry ----

        [Test]
        public void IsSecureEntry_NonSensitive_AlwaysTrue()
        {
            var entry = StorageEntry.Create("GameA", StorageClassification.Public, "k");
            var policy = SensitiveStoragePolicy.Safe("desc");
            Assert.IsTrue(SensitiveStorageGate.IsSecureEntry(entry, policy));
        }

        [Test]
        public void IsSecureEntry_SensitiveWithEncryption_IsTrue()
        {
            var entry = StorageEntry.Create("GameA", StorageClassification.Sensitive, "k");
            var policy = SensitiveStoragePolicy.Safe("desc"); // RequireEncryption=true
            Assert.IsTrue(SensitiveStorageGate.IsSecureEntry(entry, policy));
        }

        [Test]
        public void IsSecureEntry_SensitiveWithoutEncryption_IsFalse()
        {
            var entry = StorageEntry.Create("GameA", StorageClassification.Sensitive, "k");
            // Build policy with RequireEncryption=false, AllowPlaintextSensitive=true
            var policy = new SensitiveStoragePolicy_TestHelper(false, true, "open").Build();
            Assert.IsFalse(SensitiveStorageGate.IsSecureEntry(entry, policy));
        }

        // ---- StorageAccessLog ----

        [Test]
        public void StorageAccessLog_Record_NullReason_ThrowsArgumentException()
        {
            var log = new StorageAccessLog();
            var entry = StorageEntry.Create("GameA", StorageClassification.Public, "k");
            Assert.Throws<ArgumentException>(() => log.Record(entry, true, null));
        }

        [Test]
        public void StorageAccessLog_Record_EmptyReason_ThrowsArgumentException()
        {
            var log = new StorageAccessLog();
            var entry = StorageEntry.Create("GameA", StorageClassification.Public, "k");
            Assert.Throws<ArgumentException>(() => log.Record(entry, true, ""));
        }

        [Test]
        public void StorageAccessLog_Count_IncrementsOnRecord()
        {
            var log = new StorageAccessLog();
            var entry = StorageEntry.Create("GameA", StorageClassification.Public, "k");
            log.Record(entry, true, "ok");
            log.Record(entry, false, "denied");
            Assert.AreEqual(2, log.Count);
        }

        [Test]
        public void StorageAccessLog_Clear_ResetsCount()
        {
            var log = new StorageAccessLog();
            var entry = StorageEntry.Create("GameA", StorageClassification.Public, "k");
            log.Record(entry, true, "ok");
            log.Clear();
            Assert.AreEqual(0, log.Count);
        }

        [Test]
        public void StorageAccessLog_HasEntry_TrueAfterRecord()
        {
            var log = new StorageAccessLog();
            var entry = StorageEntry.Create("GameA", StorageClassification.Public, "k");
            log.Record(entry, true, "ok");
            Assert.IsTrue(log.HasEntry("GameA"));
        }

        [Test]
        public void StorageAccessLog_HasEntry_FalseForMissingGame()
        {
            var log = new StorageAccessLog();
            var entry = StorageEntry.Create("GameA", StorageClassification.Public, "k");
            log.Record(entry, true, "ok");
            Assert.IsFalse(log.HasEntry("GameB"));
        }

        [Test]
        public void StorageAccessLog_HasEntry_FalseAfterClear()
        {
            var log = new StorageAccessLog();
            var entry = StorageEntry.Create("GameA", StorageClassification.Public, "k");
            log.Record(entry, true, "ok");
            log.Clear();
            Assert.IsFalse(log.HasEntry("GameA"));
        }

        // Helper: build a SensitiveStoragePolicy via reflection to bypass the Safe-only factory
        private struct SensitiveStoragePolicy_TestHelper
        {
            private readonly bool _req;
            private readonly bool _allow;
            private readonly string _desc;
            public SensitiveStoragePolicy_TestHelper(bool req, bool allow, string desc)
            { _req = req; _allow = allow; _desc = desc; }

            public SensitiveStoragePolicy Build()
            {
                // Use reflection to call the private constructor
                var ctor = typeof(SensitiveStoragePolicy).GetConstructors(
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                return (SensitiveStoragePolicy)ctor[0].Invoke(new object[] { _req, _allow, _desc });
            }
        }
    }
}
