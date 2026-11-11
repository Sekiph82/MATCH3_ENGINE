using System;
using NUnit.Framework;
using Match3.Engine.Runtime.Identity;

namespace Match3.Engine.Tests.Editor
{
    public class SaveSnapshotGateTests
    {
        // SaveSnapshot construction

        [Test]
        public void SaveSnapshot_NullGameId_Throws()
        {
            Assert.Throws<ArgumentException>(() => SaveSnapshot.Create(null, 1, 0));
        }

        [Test]
        public void SaveSnapshot_EmptyGameId_Throws()
        {
            Assert.Throws<ArgumentException>(() => SaveSnapshot.Create("", 1, 0));
        }

        [Test]
        public void SaveSnapshot_SchemaVersionZero_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => SaveSnapshot.Create("game1", 0, 0));
        }

        [Test]
        public void SaveSnapshot_NegativeRevision_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => SaveSnapshot.Create("game1", 1, -1));
        }

        [Test]
        public void SaveSnapshot_Create_IsEntitlementAuthoritativeFalse()
        {
            var snap = SaveSnapshot.Create("game1", 1, 0);
            Assert.IsFalse(snap.IsEntitlementAuthoritative);
        }

        [Test]
        public void SaveSnapshot_Create_PropertiesRoundtrip()
        {
            var snap = SaveSnapshot.Create("MyGame", 2, 42);
            Assert.AreEqual("MyGame", snap.GameId);
            Assert.AreEqual(2, snap.SchemaVersion);
            Assert.AreEqual(42, snap.Revision);
        }

        [Test]
        public void SaveSnapshot_WithRevision_SameOrHigherAllowed()
        {
            var snap = SaveSnapshot.Create("g", 1, 5);
            var updated = SaveSnapshot.WithRevision(snap, 10);
            Assert.AreEqual(10, updated.Revision);
            Assert.AreEqual("g", updated.GameId);
        }

        [Test]
        public void SaveSnapshot_WithRevision_LowerThanOriginalThrows()
        {
            var snap = SaveSnapshot.Create("g", 1, 10);
            Assert.Throws<ArgumentOutOfRangeException>(() => SaveSnapshot.WithRevision(snap, 9));
        }

        [Test]
        public void SaveSnapshot_WithRevision_SameRevisionAllowed()
        {
            var snap = SaveSnapshot.Create("g", 1, 5);
            var same = SaveSnapshot.WithRevision(snap, 5);
            Assert.AreEqual(5, same.Revision);
        }

        // SaveSchemaPolicy

        [Test]
        public void SaveSchemaPolicy_NullDescription_Throws()
        {
            Assert.Throws<ArgumentException>(() => new SaveSchemaPolicy(1, true, null));
        }

        [Test]
        public void SaveSchemaPolicy_VersionZero_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new SaveSchemaPolicy(0, true, "desc"));
        }

        [Test]
        public void SaveSchemaPolicy_V1_HasCorrectVersion()
        {
            var p = SaveSchemaPolicy.V1("test");
            Assert.AreEqual(1, p.CurrentSchemaVersion);
            Assert.IsTrue(p.EntitlementsServerOnly);
        }

        [Test]
        public void SaveSchemaPolicy_Default_EntitlementsServerOnly()
        {
            Assert.IsTrue(SaveSchemaPolicy.Default.EntitlementsServerOnly);
        }

        // SaveSnapshotGate.CanSaveLocally

        [Test]
        public void CanSaveLocally_WonderPop_Ineligible()
        {
            var result = SaveSnapshotGate.CanSaveLocally("Wonder_Pop", SaveField.Stars, SaveSchemaPolicy.Default);
            Assert.IsFalse(result.IsEligible);
            Assert.AreEqual("WonderPopSaveProhibited", result.Reason);
        }

        [Test]
        public void CanSaveLocally_WonderPop_CaseSensitive_Eligible()
        {
            // "wonder_pop" (lowercase) is NOT Wonder_Pop — Ordinal compare
            var result = SaveSnapshotGate.CanSaveLocally("wonder_pop", SaveField.Stars, SaveSchemaPolicy.Default);
            Assert.IsTrue(result.IsEligible);
        }

        [Test]
        public void CanSaveLocally_NormalGame_AllFieldsEligible()
        {
            var policy = SaveSchemaPolicy.Default;
            foreach (SaveField field in Enum.GetValues(typeof(SaveField)))
            {
                var result = SaveSnapshotGate.CanSaveLocally("OtherGame", field, policy);
                Assert.IsTrue(result.IsEligible, $"Expected eligible for field {field}");
            }
        }

        // SaveSnapshotGate.RequiresServerEntitlements

        [Test]
        public void RequiresServerEntitlements_DefaultPolicy_True()
        {
            Assert.IsTrue(SaveSnapshotGate.RequiresServerEntitlements(SaveSchemaPolicy.Default));
        }

        [Test]
        public void RequiresServerEntitlements_FalsePolicy_False()
        {
            var p = new SaveSchemaPolicy(1, false, "no server");
            Assert.IsFalse(SaveSnapshotGate.RequiresServerEntitlements(p));
        }

        // SaveSnapshotGate.IsSchemaCompatible

        [Test]
        public void IsSchemaCompatible_SameVersion_True()
        {
            var snap = SaveSnapshot.Create("g", 1, 0);
            Assert.IsTrue(SaveSnapshotGate.IsSchemaCompatible(snap, SaveSchemaPolicy.Default));
        }

        [Test]
        public void IsSchemaCompatible_OlderSnapshotVersion_True()
        {
            var snap = SaveSnapshot.Create("g", 1, 0);
            var policy = new SaveSchemaPolicy(2, true, "v2");
            Assert.IsTrue(SaveSnapshotGate.IsSchemaCompatible(snap, policy));
        }

        [Test]
        public void IsSchemaCompatible_NewerSnapshotVersion_False()
        {
            var snap = SaveSnapshot.Create("g", 2, 0);
            var policy = new SaveSchemaPolicy(1, true, "v1");
            Assert.IsFalse(SaveSnapshotGate.IsSchemaCompatible(snap, policy));
        }

        // SaveRevisionLog

        [Test]
        public void SaveRevisionLog_RecordAndGet()
        {
            var log = new SaveRevisionLog();
            var snap = SaveSnapshot.Create("game1", 1, 7);
            log.Record(snap);
            Assert.IsTrue(log.TryGet("game1", out var retrieved));
            Assert.AreEqual(7, retrieved.Revision);
        }

        [Test]
        public void SaveRevisionLog_OverwriteOnNewRevision()
        {
            var log = new SaveRevisionLog();
            log.Record(SaveSnapshot.Create("game1", 1, 1));
            log.Record(SaveSnapshot.Create("game1", 1, 5));
            log.TryGet("game1", out var snap);
            Assert.AreEqual(5, snap.Revision);
        }

        [Test]
        public void SaveRevisionLog_TryGet_MissingKey_False()
        {
            var log = new SaveRevisionLog();
            Assert.IsFalse(log.TryGet("nope", out _));
        }

        [Test]
        public void SaveRevisionLog_Clear_ResetsCount()
        {
            var log = new SaveRevisionLog();
            log.Record(SaveSnapshot.Create("g1", 1, 0));
            log.Record(SaveSnapshot.Create("g2", 1, 0));
            log.Clear();
            Assert.AreEqual(0, log.Count);
        }

        [Test]
        public void SaveRevisionLog_Count_Accurate()
        {
            var log = new SaveRevisionLog();
            log.Record(SaveSnapshot.Create("a", 1, 0));
            log.Record(SaveSnapshot.Create("b", 1, 0));
            Assert.AreEqual(2, log.Count);
        }
    }
}
