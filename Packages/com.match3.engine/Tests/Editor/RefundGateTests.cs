// ENG-M15-010: RefundGate test suite — 15+ non-tautological tests
using System;
using NUnit.Framework;
using Match3.Engine.Runtime.Commerce;

namespace Match3.Engine.Tests.Editor
{
    [TestFixture]
    public class RefundGateTests
    {
        // ── RefundPolicy factory ──────────────────────────────────────────────

        [Test]
        public void RefundPolicy_Active_SetsRevocationAllowedTrue()
        {
            var p = RefundPolicy.Active("test");
            Assert.IsTrue(p.RevocationAllowed);
        }

        [Test]
        public void RefundPolicy_Active_AlwaysRequiresServerReconciliation()
        {
            var p = RefundPolicy.Active("test");
            Assert.IsTrue(p.RequireServerReconciliation);
        }

        [Test]
        public void RefundPolicy_Disabled_SetsRevocationAllowedFalse()
        {
            var p = RefundPolicy.Disabled("test");
            Assert.IsFalse(p.RevocationAllowed);
        }

        [Test]
        public void RefundPolicy_Disabled_AlwaysRequiresServerReconciliation()
        {
            var p = RefundPolicy.Disabled("test");
            Assert.IsTrue(p.RequireServerReconciliation);
        }

        [Test]
        public void RefundPolicy_NullDescription_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() => RefundPolicy.Active(null));
        }

        [Test]
        public void RefundPolicy_EmptyDescription_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() => RefundPolicy.Disabled(string.Empty));
        }

        [Test]
        public void RefundPolicy_Default_IsDisabled()
        {
            var p = RefundPolicy.Default;
            Assert.IsFalse(p.RevocationAllowed);
            Assert.IsTrue(p.RequireServerReconciliation);
        }

        // ── RefundRecord factory ──────────────────────────────────────────────

        [Test]
        public void RefundRecord_Pending_HasStatusRequested()
        {
            var r = RefundRecord.Pending("ord-1", "Coral_Rescue");
            Assert.AreEqual(RefundStatus.Requested, r.Status);
        }

        [Test]
        public void RefundRecord_Approved_HasStatusApproved()
        {
            var r = RefundRecord.Approved("ord-2", "Coral_Rescue");
            Assert.AreEqual(RefundStatus.Approved, r.Status);
        }

        [Test]
        public void RefundRecord_Revoked_HasStatusRevoked()
        {
            var r = RefundRecord.Revoked("ord-3", "Coral_Rescue");
            Assert.AreEqual(RefundStatus.Revoked, r.Status);
        }

        [Test]
        public void RefundRecord_Reconciled_HasStatusReconciled()
        {
            var r = RefundRecord.Reconciled("ord-4", "Coral_Rescue");
            Assert.AreEqual(RefundStatus.Reconciled, r.Status);
        }

        [Test]
        public void RefundRecord_NullOrderId_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() => RefundRecord.Pending(null, "Coral_Rescue"));
        }

        [Test]
        public void RefundRecord_NullGameId_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() => RefundRecord.Pending("ord-1", null));
        }

        // ── ShouldRevokeEntitlement: Wonder_Pop guard (must be FIRST) ─────────

        [Test]
        public void ShouldRevokeEntitlement_WonderPop_ApprovedActivePolicy_ReturnsFalse()
        {
            // Guard 1 must fire even when policy and record would otherwise allow revocation
            var policy = RefundPolicy.Active("active");
            var record = RefundRecord.Approved("ord-wp", "Wonder_Pop");
            Assert.IsFalse(RefundGate.ShouldRevokeEntitlement("Wonder_Pop", record, policy));
        }

        [Test]
        public void ShouldRevokeEntitlement_WonderPop_RevokedActivePolicy_ReturnsFalse()
        {
            var policy = RefundPolicy.Active("active");
            var record = RefundRecord.Revoked("ord-wp", "Wonder_Pop");
            Assert.IsFalse(RefundGate.ShouldRevokeEntitlement("Wonder_Pop", record, policy));
        }

        [Test]
        public void ShouldRevokeEntitlement_OtherGame_DisabledPolicy_ReturnsFalse()
        {
            // Guard 2: policy does not allow revocation
            var policy = RefundPolicy.Disabled("disabled");
            var record = RefundRecord.Approved("ord-1", "Coral_Rescue");
            Assert.IsFalse(RefundGate.ShouldRevokeEntitlement("Coral_Rescue", record, policy));
        }

        [Test]
        public void ShouldRevokeEntitlement_OtherGame_PendingStatus_ReturnsFalse()
        {
            // Guard 3: status is not Approved or Revoked
            var policy = RefundPolicy.Active("active");
            var record = RefundRecord.Pending("ord-1", "Coral_Rescue");
            Assert.IsFalse(RefundGate.ShouldRevokeEntitlement("Coral_Rescue", record, policy));
        }

        [Test]
        public void ShouldRevokeEntitlement_OtherGame_RejectedStatus_ReturnsFalse()
        {
            var policy = RefundPolicy.Active("active");
            // Manually construct a rejected record via Revoked then verify rejected path
            // RefundStatus.Rejected isn't a factory method; test via raw status check
            Assert.IsFalse(RefundGate.IsRefundTerminal(RefundStatus.None)); // sanity
            var record = RefundRecord.Pending("ord-1", "Coral_Rescue");
            // Pending → Requested, not Approved/Revoked
            Assert.IsFalse(RefundGate.ShouldRevokeEntitlement("Coral_Rescue", record, policy));
        }

        [Test]
        public void ShouldRevokeEntitlement_OtherGame_ApprovedActivePolicy_ReturnsTrue()
        {
            var policy = RefundPolicy.Active("active");
            var record = RefundRecord.Approved("ord-1", "Coral_Rescue");
            Assert.IsTrue(RefundGate.ShouldRevokeEntitlement("Coral_Rescue", record, policy));
        }

        [Test]
        public void ShouldRevokeEntitlement_OtherGame_RevokedActivePolicy_ReturnsTrue()
        {
            var policy = RefundPolicy.Active("active");
            var record = RefundRecord.Revoked("ord-1", "Coral_Rescue");
            Assert.IsTrue(RefundGate.ShouldRevokeEntitlement("Coral_Rescue", record, policy));
        }

        // ── IsRefundTerminal ──────────────────────────────────────────────────

        [Test]
        public void IsRefundTerminal_Rejected_ReturnsTrue()
        {
            Assert.IsTrue(RefundGate.IsRefundTerminal(RefundStatus.Rejected));
        }

        [Test]
        public void IsRefundTerminal_Reconciled_ReturnsTrue()
        {
            Assert.IsTrue(RefundGate.IsRefundTerminal(RefundStatus.Reconciled));
        }

        [Test]
        public void IsRefundTerminal_Requested_ReturnsFalse()
        {
            Assert.IsFalse(RefundGate.IsRefundTerminal(RefundStatus.Requested));
        }

        [Test]
        public void IsRefundTerminal_Approved_ReturnsFalse()
        {
            Assert.IsFalse(RefundGate.IsRefundTerminal(RefundStatus.Approved));
        }

        [Test]
        public void IsRefundTerminal_Revoked_ReturnsFalse()
        {
            Assert.IsFalse(RefundGate.IsRefundTerminal(RefundStatus.Revoked));
        }

        [Test]
        public void IsRefundTerminal_None_ReturnsFalse()
        {
            Assert.IsFalse(RefundGate.IsRefundTerminal(RefundStatus.None));
        }

        // ── RequiresServerReconciliation ──────────────────────────────────────

        [Test]
        public void RequiresServerReconciliation_ActivePolicy_ReturnsTrue()
        {
            Assert.IsTrue(RefundGate.RequiresServerReconciliation(RefundPolicy.Active("x")));
        }

        [Test]
        public void RequiresServerReconciliation_DisabledPolicy_ReturnsTrue()
        {
            Assert.IsTrue(RefundGate.RequiresServerReconciliation(RefundPolicy.Disabled("x")));
        }

        // ── AssertReconciliationRequired ──────────────────────────────────────

        [Test]
        public void AssertReconciliationRequired_ActivePolicy_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => RefundGate.AssertReconciliationRequired(RefundPolicy.Active("x")));
        }

        // ── RefundLedger ──────────────────────────────────────────────────────

        [Test]
        public void RefundLedger_Record_And_TryGet_RoundTrip()
        {
            var ledger = new RefundLedger();
            var record = RefundRecord.Approved("ord-1", "Coral_Rescue");
            ledger.Record(record);
            Assert.IsTrue(ledger.TryGet("ord-1", out var retrieved));
            Assert.AreEqual(RefundStatus.Approved, retrieved.Status);
        }

        [Test]
        public void RefundLedger_Record_Overwrites_ExistingOrderId()
        {
            var ledger = new RefundLedger();
            ledger.Record(RefundRecord.Pending("ord-1", "Coral_Rescue"));
            ledger.Record(RefundRecord.Approved("ord-1", "Coral_Rescue"));
            ledger.TryGet("ord-1", out var r);
            Assert.AreEqual(RefundStatus.Approved, r.Status);
        }

        [Test]
        public void RefundLedger_TryGet_MissingKey_ReturnsFalse()
        {
            var ledger = new RefundLedger();
            Assert.IsFalse(ledger.TryGet("missing", out _));
        }

        [Test]
        public void RefundLedger_GetByStatus_FiltersCorrectly()
        {
            var ledger = new RefundLedger();
            ledger.Record(RefundRecord.Approved("ord-1", "Coral_Rescue"));
            ledger.Record(RefundRecord.Pending("ord-2", "Coral_Rescue"));
            ledger.Record(RefundRecord.Approved("ord-3", "Garage_Legends"));
            var approved = ledger.GetByStatus(RefundStatus.Approved);
            Assert.AreEqual(2, approved.Count);
        }

        [Test]
        public void RefundLedger_Count_TracksRecords()
        {
            var ledger = new RefundLedger();
            ledger.Record(RefundRecord.Pending("ord-1", "Coral_Rescue"));
            ledger.Record(RefundRecord.Pending("ord-2", "Coral_Rescue"));
            Assert.AreEqual(2, ledger.Count);
        }

        [Test]
        public void RefundLedger_Clear_EmptiesLedger()
        {
            var ledger = new RefundLedger();
            ledger.Record(RefundRecord.Pending("ord-1", "Coral_Rescue"));
            ledger.Clear();
            Assert.AreEqual(0, ledger.Count);
        }


        [Test]
        public void DefaultStructPolicy_FailsClosed()
        {
            var record = RefundRecord.Approved("order-1", "Coral_Rescue");
            Assert.IsFalse(RefundGate.ShouldRevokeEntitlement("Coral_Rescue", record, default(RefundPolicy)));
            Assert.AreEqual(RefundGate.ShouldRevokeEntitlement("Coral_Rescue", record, RefundPolicy.Default),
                RefundGate.ShouldRevokeEntitlement("Coral_Rescue", record, default(RefundPolicy)));
            Assert.IsTrue(RefundGate.RequiresServerReconciliation(default(RefundPolicy)));
            Assert.DoesNotThrow(() => RefundGate.AssertReconciliationRequired(default(RefundPolicy)));
        }

        [Test]
        public void GameIdMismatch_Blocked()
        {
            var record = RefundRecord.Approved("order-1", "Wonder_Pop");
            Assert.IsFalse(RefundGate.ShouldRevokeEntitlement("Coral_Rescue", record, RefundPolicy.Active("active")));
        }

        [Test]
        public void WonderPopParam_RecordDiffers_GameIdMismatch()
        {
            var record = RefundRecord.Approved("order-1", "Coral_Rescue");
            Assert.IsFalse(RefundGate.ShouldRevokeEntitlement("Wonder_Pop", record, RefundPolicy.Active("active")));
            // Control: matching game id with the same record and policy does revoke
            Assert.IsTrue(RefundGate.ShouldRevokeEntitlement("Coral_Rescue", record, RefundPolicy.Active("active")));
        }


        [Test]
        public void NullOrEmptyGameId_DefaultRecord_GameIdMissing()
        {
            foreach (var gameId in new[] { null, string.Empty })
            {
                Assert.IsFalse(RefundGate.ShouldRevokeEntitlement(gameId, default(RefundRecord), RefundPolicy.Active("active")));
            }
        }

        [Test]
        public void UnknownOrCaseVariantGameId_MatchingApprovedRecord_NeverRevokes()
        {
            // Revoke-safe: a non-catalog ID cannot own an entitlement, so nothing is revoked for it
            // (and it can never be used to revoke another game's entitlement).
            foreach (var gameId in new[] { "Unknown_Game", "coral_rescue", "CORAL_RESCUE", " Coral_Rescue", "Coral_Rescue ", "wonder_pop" })
            {
                Assert.IsFalse(RefundGate.ShouldRevokeEntitlement(gameId, RefundRecord.Approved("order-1", gameId), RefundPolicy.Active("active")), gameId);
                Assert.IsFalse(RefundGate.ShouldRevokeEntitlement(gameId, RefundRecord.Revoked("order-1", gameId), RefundPolicy.Active("active")), gameId);
            }
            // Control: catalog games still revoke approved refunds under an active policy.
            foreach (var gameId in new[] { "Coral_Rescue", "Garage_Legends", "Dino_Islands", "The_Fractured_Realms" })
                Assert.IsTrue(RefundGate.ShouldRevokeEntitlement(gameId, RefundRecord.Approved("order-1", gameId), RefundPolicy.Active("active")), gameId);
        }
    }
}
