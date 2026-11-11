// ENG-M18-009 RevenueReconciliationGate tests
using NUnit.Framework;
using System;
using Match3.Engine.Runtime.Analytics;

namespace Match3.Engine.Tests.Editor
{
    public class RevenueReconciliationGateTests
    {
        // --- ReconciliationEntry factory and validation ---

        [Test]
        public void Entry_NullGameId_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() =>
                ReconciliationEntry.Create(null, ReconciliationSource.StoreReceipt, false));
        }

        [Test]
        public void Entry_EmptyGameId_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() =>
                ReconciliationEntry.Create("", ReconciliationSource.StoreReceipt, false));
        }

        [Test]
        public void Entry_Create_SetsProperties()
        {
            var e = ReconciliationEntry.Create("GameA", ReconciliationSource.MediationReport, true);
            Assert.AreEqual("GameA", e.GameId);
            Assert.AreEqual(ReconciliationSource.MediationReport, e.Source);
            Assert.IsTrue(e.IsRefund);
        }

        [Test]
        public void Entry_StoreReceipt_IsStoreAuthoritative_True()
        {
            var e = ReconciliationEntry.Create("G", ReconciliationSource.StoreReceipt, false);
            Assert.IsTrue(e.IsStoreAuthoritative);
            Assert.IsFalse(e.RequiresDedup);
        }

        [Test]
        public void Entry_MediationReport_IsStoreAuthoritative_False()
        {
            var e = ReconciliationEntry.Create("G", ReconciliationSource.MediationReport, false);
            Assert.IsFalse(e.IsStoreAuthoritative);
            Assert.IsTrue(e.RequiresDedup);
        }

        [Test]
        public void Entry_InternalEvent_IsStoreAuthoritative_False()
        {
            var e = ReconciliationEntry.Create("G", ReconciliationSource.InternalEvent, false);
            Assert.IsFalse(e.IsStoreAuthoritative);
            Assert.IsTrue(e.RequiresDedup);
        }

        // --- ReconciliationPolicy ---

        [Test]
        public void Policy_NullDesc_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() => ReconciliationPolicy.Safe(null));
        }

        [Test]
        public void Policy_EmptyDesc_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() => ReconciliationPolicy.Safe(""));
        }

        [Test]
        public void Policy_Safe_SetsDescription()
        {
            var p = ReconciliationPolicy.Safe("desc");
            Assert.AreEqual("desc", p.PolicyDescription);
        }

        [Test]
        public void Policy_Default_IsCorrect()
        {
            var p = ReconciliationPolicy.Default;
            Assert.IsFalse(string.IsNullOrEmpty(p.PolicyDescription));
        }

        // --- RevenueReconciliationGate.CanReconcile ---

        [Test]
        public void CanReconcile_WonderPop_Exact_Blocked_Guard1()
        {
            var entry = ReconciliationEntry.Create("Wonder_Pop", ReconciliationSource.StoreReceipt, false);
            var policy = new ReconciliationPolicy(); // default struct: normalized to Default
            var (eligible, reason) = RevenueReconciliationGate.CanReconcile("Wonder_Pop", entry, policy);
            Assert.IsFalse(eligible);
            Assert.AreEqual("WonderPopReconciliationProhibited", reason);
        }

        [Test]
        public void CanReconcile_WonderPop_Lowercase_NotBlocked()
        {
            // Ordinal: "wonder_pop" != "Wonder_Pop"
            var policy = ReconciliationPolicy.Safe("desc");
            // Need a store receipt so guard2/3 pass with this policy
            var entry = ReconciliationEntry.Create("wonder_pop", ReconciliationSource.StoreReceipt, false);
            var (eligible, _) = RevenueReconciliationGate.CanReconcile("wonder_pop", entry, policy);
            Assert.IsTrue(eligible);
        }

        [Test]
        public void CanReconcile_MediationDisabled_Blocked_Guard2()
        {
            var policy = ReconciliationPolicy.Safe("desc");
            var entry = ReconciliationEntry.Create("GameB", ReconciliationSource.MediationReport, false);
            var (eligible, reason) = RevenueReconciliationGate.CanReconcile("GameB", entry, policy);
            Assert.IsFalse(eligible);
            Assert.AreEqual("MediationReconciliationDisabled", reason);
        }

        [Test]
        public void CanReconcile_StoreAuthorityRequired_InternalEvent_Blocked_Guard3()
        {
            var policy = ReconciliationPolicy.Safe("desc");
            var entry = ReconciliationEntry.Create("GameC", ReconciliationSource.InternalEvent, false);
            var (eligible, reason) = RevenueReconciliationGate.CanReconcile("GameC", entry, policy);
            Assert.IsFalse(eligible);
            Assert.AreEqual("StoreAuthorityRequired", reason);
        }

        [Test]
        public void CanReconcile_StoreReceipt_AlwaysEligible_WithSafePolicy()
        {
            var policy = ReconciliationPolicy.Safe("desc");
            var entry = ReconciliationEntry.Create("GameD", ReconciliationSource.StoreReceipt, false);
            var (eligible, _) = RevenueReconciliationGate.CanReconcile("GameD", entry, policy);
            Assert.IsTrue(eligible);
        }

        private static readonly ReconciliationPolicy[] AllConstructiblePolicies =
            { ReconciliationPolicy.Safe("desc"), ReconciliationPolicy.Default, default(ReconciliationPolicy) };

        [Test]
        public void CanReconcile_NonStoreSources_BlockedUnderEveryConstructiblePolicy()
        {
            var mediation = ReconciliationEntry.Create("GameE", ReconciliationSource.MediationReport, false);
            var internalEvent = ReconciliationEntry.Create("GameE", ReconciliationSource.InternalEvent, false);
            foreach (var policy in AllConstructiblePolicies)
            {
                var (e1, r1) = RevenueReconciliationGate.CanReconcile("GameE", mediation, policy);
                Assert.IsFalse(e1);
                Assert.AreEqual("MediationReconciliationDisabled", r1);
                var (e2, r2) = RevenueReconciliationGate.CanReconcile("GameE", internalEvent, policy);
                Assert.IsFalse(e2);
                Assert.AreEqual("StoreAuthorityRequired", r2);
            }
        }

        [Test]
        public void CanReconcile_StoreReceipt_EligibleUnderEveryConstructiblePolicy()
        {
            var entry = ReconciliationEntry.Create("GameE", ReconciliationSource.StoreReceipt, false);
            foreach (var policy in AllConstructiblePolicies)
            {
                var (eligible, reason) = RevenueReconciliationGate.CanReconcile("GameE", entry, policy);
                Assert.IsTrue(eligible);
                Assert.AreEqual(string.Empty, reason);
            }
        }

        [Test]
        public void CanReconcile_DefaultStructPolicy_FailsClosed()
        {
            var entry = ReconciliationEntry.Create("GameE", ReconciliationSource.InternalEvent, false);
            var (e1, expected) = RevenueReconciliationGate.CanReconcile("GameE", entry, ReconciliationPolicy.Default);
            var (e2, reason) = RevenueReconciliationGate.CanReconcile("GameE", entry, default(ReconciliationPolicy));
            Assert.IsFalse(e1);
            Assert.IsFalse(e2);
            Assert.AreEqual(expected, reason);
        }

        [Test]
        public void CanReconcile_GameIdMismatch_Blocked()
        {
            var entry = ReconciliationEntry.Create("Wonder_Pop", ReconciliationSource.StoreReceipt, false);
            var (eligible, reason) = RevenueReconciliationGate.CanReconcile("Coral_Rescue", entry, ReconciliationPolicy.Default);
            Assert.IsFalse(eligible);
            Assert.AreEqual("GameIdMismatch", reason);
        }

        [Test]
        public void CanReconcile_WonderPopParam_EntryDiffers_GameIdMismatch()
        {
            var entry = ReconciliationEntry.Create("Coral_Rescue", ReconciliationSource.StoreReceipt, false);
            var (eligible, reason) = RevenueReconciliationGate.CanReconcile("Wonder_Pop", entry, ReconciliationPolicy.Default);
            Assert.IsFalse(eligible);
            Assert.AreEqual("GameIdMismatch", reason);
        }

        // --- Helpers ---

        [Test]
        public void IsStoreSource_StoreReceipt_True()
        {
            var entry = ReconciliationEntry.Create("G", ReconciliationSource.StoreReceipt, false);
            Assert.IsTrue(RevenueReconciliationGate.IsStoreSource(entry));
        }

        [Test]
        public void IsStoreSource_MediationReport_False()
        {
            var entry = ReconciliationEntry.Create("G", ReconciliationSource.MediationReport, false);
            Assert.IsFalse(RevenueReconciliationGate.IsStoreSource(entry));
        }

        [Test]
        public void RequiresDedup_InternalEvent_True()
        {
            var entry = ReconciliationEntry.Create("G", ReconciliationSource.InternalEvent, false);
            Assert.IsTrue(RevenueReconciliationGate.RequiresDedup(entry));
        }

        [Test]
        public void RequiresDedup_StoreReceipt_False()
        {
            var entry = ReconciliationEntry.Create("G", ReconciliationSource.StoreReceipt, false);
            Assert.IsFalse(RevenueReconciliationGate.RequiresDedup(entry));
        }

        // --- ReconciliationLog ---

        [Test]
        public void Log_Record_NullGameId_Throws()
        {
            var log = new ReconciliationLog();
            Assert.Throws<ArgumentException>(() => log.Record(null, ReconciliationSource.StoreReceipt));
        }

        [Test]
        public void Log_Record_EmptyGameId_Throws()
        {
            var log = new ReconciliationLog();
            Assert.Throws<ArgumentException>(() => log.Record("", ReconciliationSource.StoreReceipt));
        }

        [Test]
        public void Log_Record_IncreasesCount()
        {
            var log = new ReconciliationLog();
            log.Record("G1", ReconciliationSource.StoreReceipt);
            log.Record("G2", ReconciliationSource.MediationReport);
            Assert.AreEqual(2, log.Count);
        }

        [Test]
        public void Log_HasEntry_True_AfterRecord()
        {
            var log = new ReconciliationLog();
            log.Record("MyGame", ReconciliationSource.InternalEvent);
            Assert.IsTrue(log.HasEntry("MyGame"));
        }

        [Test]
        public void Log_HasEntry_False_ForUnrecorded()
        {
            var log = new ReconciliationLog();
            Assert.IsFalse(log.HasEntry("Unknown"));
        }

        [Test]
        public void Log_Clear_ResetsCount()
        {
            var log = new ReconciliationLog();
            log.Record("G", ReconciliationSource.StoreReceipt);
            log.Clear();
            Assert.AreEqual(0, log.Count);
            Assert.IsFalse(log.HasEntry("G"));
        }


        [Test]
        public void NullOrEmptyGameId_DefaultRecord_GameIdMissing()
        {
            foreach (var gameId in new[] { null, string.Empty })
            {
                var (eligible, reason) = RevenueReconciliationGate.CanReconcile(gameId, default(ReconciliationEntry), default(ReconciliationPolicy));
                Assert.IsFalse(eligible);
                Assert.AreEqual("GameIdMissing", reason);
            }
        }
    }
}
