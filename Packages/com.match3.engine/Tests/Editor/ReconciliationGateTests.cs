using NUnit.Framework;
using Match3.Engine.Runtime.Economy;
using System;

namespace Match3.Engine.Tests.Editor
{
    [TestFixture]
    public class ReconciliationGateTests
    {
        // 1. ReconciliationEvent validation: null/empty GameId throws
        [Test]
        public void ReconciliationEvent_NullGameId_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() =>
                new ReconciliationEvent(null, "tx1", "FailedGrant", 1000L, false));
        }

        // 2. ReconciliationEvent validation: null TransactionId throws
        [Test]
        public void ReconciliationEvent_NullTransactionId_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() =>
                new ReconciliationEvent("Coral_Rescue", null, "FailedGrant", 1000L, false));
        }

        // 3. ReconciliationEvent validation: null EventType throws
        [Test]
        public void ReconciliationEvent_NullEventType_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() =>
                new ReconciliationEvent("Coral_Rescue", "tx1", null, 1000L, false));
        }

        // 4. ReconciliationEvent validation: Timestamp <= 0 throws
        [Test]
        public void ReconciliationEvent_ZeroTimestamp_ThrowsArgumentOutOfRangeException()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new ReconciliationEvent("Coral_Rescue", "tx1", "FailedGrant", 0L, false));
        }

        // 5. FailedGrant factory produces correct EventType and IsResolved=false
        [Test]
        public void FailedGrant_Factory_SetsEventTypeAndIsResolved()
        {
            var ev = ReconciliationEvent.FailedGrant("Coral_Rescue", "tx1", 1000L);
            Assert.AreEqual("FailedGrant", ev.EventType);
            Assert.IsFalse(ev.IsResolved);
        }

        // 6. Duplicate factory produces correct EventType and IsResolved=false
        [Test]
        public void Duplicate_Factory_SetsEventTypeAndIsResolved()
        {
            var ev = ReconciliationEvent.Duplicate("Coral_Rescue", "tx2", 2000L);
            Assert.AreEqual("Duplicate", ev.EventType);
            Assert.IsFalse(ev.IsResolved);
        }

        // 7. Resolved factory copies all fields with IsResolved=true
        [Test]
        public void Resolved_Factory_CopiesAllFieldsWithIsResolvedTrue()
        {
            var original = ReconciliationEvent.FailedGrant("Coral_Rescue", "tx3", 3000L);
            var resolved = ReconciliationEvent.Resolved(original);
            Assert.AreEqual(original.GameId, resolved.GameId);
            Assert.AreEqual(original.TransactionId, resolved.TransactionId);
            Assert.AreEqual(original.EventType, resolved.EventType);
            Assert.AreEqual(original.Timestamp, resolved.Timestamp);
            Assert.IsTrue(resolved.IsResolved);
        }

        // 8. ReconciliationPolicy Default has correct values
        [Test]
        public void ReconciliationPolicy_Default_HasCorrectValues()
        {
            var policy = ReconciliationPolicy.Default;
            Assert.IsTrue(policy.RequireOwnerAuthorization);
            Assert.IsTrue(policy.LeastPrivilegeAccess);
            Assert.IsFalse(string.IsNullOrEmpty(policy.PolicyDescription));
        }

        // 9. CanRunReport: Wonder_Pop is FIRST guard — blocked even with owner auth
        [Test]
        public void CanRunReport_WonderPop_IsIneligibleFirst()
        {
            var policy = ReconciliationPolicy.Default;
            var result = ReconciliationGate.CanRunReport("Wonder_Pop", policy);
            Assert.IsFalse(result.IsEligible);
            Assert.AreEqual("WonderPopReconciliationProhibited", result.Reason);
        }

        // 10. CanRunReport: Wonder_Pop guard is Ordinal (case-sensitive) — wonder_pop is NOT blocked
        [Test]
        public void CanRunReport_WonderPopCaseMismatch_IsUnknownGame()
        {
            var policy = ReconciliationPolicy.Default;
            var result = ReconciliationGate.CanRunReport("wonder_pop", policy);
            Assert.IsFalse(result.IsEligible);
            Assert.AreEqual("UnknownGame", result.Reason);
        }

        // 11. CanRunReport: disabled owner auth blocks non-Wonder_Pop
        [Test]
        public void CanRunReport_OwnerAuthDisabled_IsIneligible()
        {
            var policy = new ReconciliationPolicy(false, true, "No auth policy");
            var result = ReconciliationGate.CanRunReport("Coral_Rescue", policy);
            Assert.IsFalse(result.IsEligible);
            Assert.AreEqual("OwnerAuthorizationDisabled", result.Reason);
        }

        // 12. CanApplyCorrection: Wonder_Pop is FIRST guard
        [Test]
        public void CanApplyCorrection_WonderPop_IsIneligibleFirst()
        {
            var policy = ReconciliationPolicy.Default;
            var result = ReconciliationGate.CanApplyCorrection("Wonder_Pop", policy);
            Assert.IsFalse(result.IsEligible);
            Assert.AreEqual("WonderPopReconciliationProhibited", result.Reason);
        }

        // 13. CanApplyCorrection: eligible with standard policy for other game
        [Test]
        public void CanApplyCorrection_ValidGame_IsEligible()
        {
            var policy = ReconciliationPolicy.Default;
            var result = ReconciliationGate.CanApplyCorrection("Coral_Rescue", policy);
            Assert.IsTrue(result.IsEligible);
        }

        // 14. RequiresAuditTrail always returns true
        [Test]
        public void RequiresAuditTrail_AlwaysTrue()
        {
            var policyAuth = ReconciliationPolicy.Default;
            var policyNoAuth = new ReconciliationPolicy(false, false, "No auth");
            Assert.IsTrue(ReconciliationGate.RequiresAuditTrail(policyAuth));
            Assert.IsTrue(ReconciliationGate.RequiresAuditTrail(policyNoAuth));
        }

        // 15. ReconciliationLog: Record/TryGet/Count
        [Test]
        public void ReconciliationLog_RecordAndTryGet()
        {
            var log = new ReconciliationLog();
            var ev = ReconciliationEvent.FailedGrant("Coral_Rescue", "tx1", 1000L);
            log.Record(ev);
            Assert.AreEqual(1, log.Count);
            Assert.IsTrue(log.TryGet("tx1", out var found));
            Assert.AreEqual("FailedGrant", found.EventType);
        }

        // 16. ReconciliationLog: GetUnresolved filters resolved
        [Test]
        public void ReconciliationLog_GetUnresolved_FiltersResolved()
        {
            var log = new ReconciliationLog();
            var ev1 = ReconciliationEvent.FailedGrant("Coral_Rescue", "tx1", 1000L);
            var ev2 = ReconciliationEvent.Resolved(ReconciliationEvent.Duplicate("Coral_Rescue", "tx2", 2000L));
            log.Record(ev1);
            log.Record(ev2);
            var unresolved = log.GetUnresolved();
            Assert.AreEqual(1, unresolved.Count);
            Assert.AreEqual("tx1", unresolved[0].TransactionId);
        }

        // 17. ReconciliationLog: Clear empties the log
        [Test]
        public void ReconciliationLog_Clear_EmptiesLog()
        {
            var log = new ReconciliationLog();
            log.Record(ReconciliationEvent.FailedGrant("Coral_Rescue", "tx1", 1000L));
            log.Clear();
            Assert.AreEqual(0, log.Count);
            Assert.IsFalse(log.TryGet("tx1", out _));
        }

        // 18. ReconciliationPolicy: null PolicyDescription throws
        [Test]
        public void ReconciliationPolicy_NullDescription_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() =>
                new ReconciliationPolicy(true, true, null));
        }


        [Test]
        public void CanRunReport_NullOrEmptyGameId_FailsClosed()
        {
            foreach (var gameId in new[] { null, string.Empty })
            {
                var result = ReconciliationGate.CanRunReport(gameId, ReconciliationPolicy.Default);
                Assert.IsFalse(result.IsEligible, gameId);
                Assert.AreEqual("GameIdMissing", result.Reason);
            }
        }

        [Test]
        public void CanRunReport_NonCatalogGameId_UnknownGame()
        {
            foreach (var unknownId in new[] { "Unknown_Game", "coral_rescue", "CORAL_RESCUE", " Coral_Rescue", "Coral_Rescue " })
            {
                var result = ReconciliationGate.CanRunReport(unknownId, ReconciliationPolicy.Default);
                Assert.IsFalse(result.IsEligible, unknownId);
                Assert.AreEqual("UnknownGame", result.Reason);
            }
        }

        [Test]
        public void CanApplyCorrection_NullOrEmptyGameId_FailsClosed()
        {
            foreach (var gameId in new[] { null, string.Empty })
            {
                var result = ReconciliationGate.CanApplyCorrection(gameId, ReconciliationPolicy.Default);
                Assert.IsFalse(result.IsEligible, gameId);
                Assert.AreEqual("GameIdMissing", result.Reason);
            }
        }

        [Test]
        public void CanApplyCorrection_NonCatalogGameId_UnknownGame()
        {
            foreach (var unknownId in new[] { "Unknown_Game", "coral_rescue", "CORAL_RESCUE", " Coral_Rescue", "Coral_Rescue " })
            {
                var result = ReconciliationGate.CanApplyCorrection(unknownId, ReconciliationPolicy.Default);
                Assert.IsFalse(result.IsEligible, unknownId);
                Assert.AreEqual("UnknownGame", result.Reason);
            }
        }
    }
}
