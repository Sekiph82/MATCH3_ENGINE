using NUnit.Framework;
using System;
using Match3.Engine.Runtime.Economy;

namespace Match3.Engine.Tests.Editor
{
    /// <summary>
    /// ENG-M16-002: TransactionIdempotency unit tests.
    /// 15+ non-tautological [Test] methods.
    /// </summary>
    [TestFixture]
    public class TransactionIdempotencyTests
    {
        // ── TransactionId.Generate ────────────────────────────────────────────

        [Test]
        public void Generate_ReturnsNonEmptyValue()
        {
            var id = TransactionId.Generate("Coral_Rescue", "purchase");
            Assert.IsFalse(id.IsEmpty, "Generated ID must not be empty.");
        }

        [Test]
        public void Generate_ValueIs32HexChars()
        {
            var id = TransactionId.Generate("Coral_Rescue", "purchase");
            Assert.AreEqual(32, id.Value.Length, "Guid.ToString(\"N\") produces 32 hex chars.");
            Assert.IsTrue(System.Text.RegularExpressions.Regex.IsMatch(id.Value, @"^[0-9a-f]{32}$"),
                "Value must be lowercase hex.");
        }

        [Test]
        public void Generate_TwoCallsProduceDifferentValues()
        {
            var a = TransactionId.Generate("Coral_Rescue", "purchase");
            var b = TransactionId.Generate("Coral_Rescue", "purchase");
            Assert.AreNotEqual(a.Value, b.Value, "Each generate call must yield a unique GUID.");
        }

        [Test]
        public void Generate_NullGameId_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() => TransactionId.Generate(null, "purchase"));
        }

        [Test]
        public void Generate_NullSource_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() => TransactionId.Generate("Coral_Rescue", null));
        }

        // ── TransactionId.FromExisting ────────────────────────────────────────

        [Test]
        public void FromExisting_PreservesValue()
        {
            var id = TransactionId.FromExisting("abc123", "Coral_Rescue", "ad_reward");
            Assert.AreEqual("abc123", id.Value);
        }

        [Test]
        public void FromExisting_EmptyValue_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() => TransactionId.FromExisting("", "Coral_Rescue", "purchase"));
        }

        // ── IdempotencyPolicy ─────────────────────────────────────────────────

        [Test]
        public void DefaultPolicy_RejectDuplicatesIsTrue()
        {
            Assert.IsTrue(IdempotencyPolicy.Default.RejectDuplicates);
        }

        [Test]
        public void DefaultPolicy_HasNonEmptyDescription()
        {
            Assert.IsFalse(string.IsNullOrEmpty(IdempotencyPolicy.Default.PolicyDescription));
        }

        [Test]
        public void Strict_NullDescription_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() => IdempotencyPolicy.Strict(null));
        }

        // ── TransactionIdempotencyGate ────────────────────────────────────────

        [Test]
        public void Gate_WonderPop_IsIneligible_RegardlessOfOtherGuards()
        {
            var id     = TransactionId.Generate("Wonder_Pop", "purchase");
            var policy = IdempotencyPolicy.Default;
            var result = TransactionIdempotencyGate.CanProcessTransaction("Wonder_Pop", id, policy);
            Assert.IsFalse(result.IsEligible);
            Assert.AreEqual("WonderPopTransactionProhibited", result.Reason);
        }

        [Test]
        public void Gate_ValidGame_ValidId_DefaultPolicy_IsEligible()
        {
            var id     = TransactionId.Generate("Coral_Rescue", "purchase");
            var policy = IdempotencyPolicy.Default;
            var result = TransactionIdempotencyGate.CanProcessTransaction("Coral_Rescue", id, policy);
            Assert.IsTrue(result.IsEligible);
        }

        [Test]
        public void Gate_RequiresIdempotencyKey_AlwaysTrue()
        {
            Assert.IsTrue(TransactionIdempotencyGate.RequiresIdempotencyKey(IdempotencyPolicy.Default));
        }

        // ── IdempotencyLedger ─────────────────────────────────────────────────

        [Test]
        public void Ledger_TryRecord_FirstTime_ReturnsTrue()
        {
            var ledger = new IdempotencyLedger();
            var id     = TransactionId.Generate("Coral_Rescue", "task_claim");
            Assert.IsTrue(ledger.TryRecord(id), "First record must succeed.");
        }

        [Test]
        public void Ledger_TryRecord_Duplicate_ReturnsFalse()
        {
            var ledger = new IdempotencyLedger();
            var id     = TransactionId.Generate("Coral_Rescue", "task_claim");
            ledger.TryRecord(id);
            Assert.IsFalse(ledger.TryRecord(id), "Duplicate record must be rejected.");
        }

        [Test]
        public void Ledger_IsRecorded_TrueAfterRecord()
        {
            var ledger = new IdempotencyLedger();
            var id     = TransactionId.Generate("Coral_Rescue", "migration");
            ledger.TryRecord(id);
            Assert.IsTrue(ledger.IsRecorded(id.Value));
        }

        [Test]
        public void Ledger_IsRecorded_FalseForUnknownValue()
        {
            var ledger = new IdempotencyLedger();
            Assert.IsFalse(ledger.IsRecorded("nonexistent"));
        }

        [Test]
        public void Ledger_Clear_ResetsCount()
        {
            var ledger = new IdempotencyLedger();
            ledger.TryRecord(TransactionId.Generate("Coral_Rescue", "purchase"));
            ledger.TryRecord(TransactionId.Generate("Coral_Rescue", "purchase"));
            ledger.Clear();
            Assert.AreEqual(0, ledger.Count);
        }

        [Test]
        public void Ledger_Clear_AllowsReRecording()
        {
            var ledger = new IdempotencyLedger();
            var id     = TransactionId.FromExisting("fixed-key", "Coral_Rescue", "ad_reward");
            ledger.TryRecord(id);
            ledger.Clear();
            Assert.IsTrue(ledger.TryRecord(id), "After clear, same key must be accepted again.");
        }

        [Test]
        public void Ledger_Count_IncrementsOnUniqueRecords()
        {
            var ledger = new IdempotencyLedger();
            ledger.TryRecord(TransactionId.Generate("Coral_Rescue", "purchase"));
            ledger.TryRecord(TransactionId.Generate("Coral_Rescue", "purchase"));
            Assert.AreEqual(2, ledger.Count);
        }

        [Test]
        public void Ledger_Count_DoesNotIncrementOnDuplicate()
        {
            var ledger = new IdempotencyLedger();
            var id     = TransactionId.Generate("Coral_Rescue", "task_claim");
            ledger.TryRecord(id);
            ledger.TryRecord(id);
            Assert.AreEqual(1, ledger.Count);
        }


        [Test]
        public void CanProcessTransaction_NullOrEmptyGameId_FailsClosed()
        {
            foreach (var gameId in new[] { null, string.Empty })
            {
                var result = TransactionIdempotencyGate.CanProcessTransaction(gameId, TransactionId.Generate("Coral_Rescue", "src"), IdempotencyPolicy.Default);
                Assert.IsFalse(result.IsEligible, gameId);
                Assert.AreEqual("GameIdMissing", result.Reason);
            }
        }

        [Test]
        public void CanProcessTransaction_NonCatalogGameId_UnknownGame()
        {
            foreach (var unknownId in new[] { "Unknown_Game", "coral_rescue", "CORAL_RESCUE", " Coral_Rescue", "Coral_Rescue " })
            {
                var result = TransactionIdempotencyGate.CanProcessTransaction(unknownId, TransactionId.Generate("Coral_Rescue", "src"), IdempotencyPolicy.Default);
                Assert.IsFalse(result.IsEligible, unknownId);
                Assert.AreEqual("UnknownGame", result.Reason);
            }
        }

        [Test]
        public void CanProcessTransaction_TxIdForOtherGame_GameIdMismatch()
        {
            var r1 = TransactionIdempotencyGate.CanProcessTransaction(
                "Coral_Rescue", TransactionId.Generate("Garage_Legends", "src"), IdempotencyPolicy.Default);
            Assert.IsFalse(r1.IsEligible);
            Assert.AreEqual("GameIdMismatch", r1.Reason);
            var r2 = TransactionIdempotencyGate.CanProcessTransaction(
                "Wonder_Pop", TransactionId.Generate("Coral_Rescue", "src"), IdempotencyPolicy.Default);
            Assert.IsFalse(r2.IsEligible);
            Assert.AreEqual("GameIdMismatch", r2.Reason);
            // Control: same game id is eligible
            Assert.IsTrue(TransactionIdempotencyGate.CanProcessTransaction(
                "Coral_Rescue", TransactionId.Generate("Coral_Rescue", "src"), IdempotencyPolicy.Default).IsEligible);
        }
    }
}
