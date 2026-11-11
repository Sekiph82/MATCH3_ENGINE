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
            var id = TransactionId.Generate("GameA", "purchase");
            Assert.IsFalse(id.IsEmpty, "Generated ID must not be empty.");
        }

        [Test]
        public void Generate_ValueIs32HexChars()
        {
            var id = TransactionId.Generate("GameA", "purchase");
            Assert.AreEqual(32, id.Value.Length, "Guid.ToString(\"N\") produces 32 hex chars.");
            Assert.IsTrue(System.Text.RegularExpressions.Regex.IsMatch(id.Value, @"^[0-9a-f]{32}$"),
                "Value must be lowercase hex.");
        }

        [Test]
        public void Generate_TwoCallsProduceDifferentValues()
        {
            var a = TransactionId.Generate("GameA", "purchase");
            var b = TransactionId.Generate("GameA", "purchase");
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
            Assert.Throws<ArgumentException>(() => TransactionId.Generate("GameA", null));
        }

        // ── TransactionId.FromExisting ────────────────────────────────────────

        [Test]
        public void FromExisting_PreservesValue()
        {
            var id = TransactionId.FromExisting("abc123", "GameA", "ad_reward");
            Assert.AreEqual("abc123", id.Value);
        }

        [Test]
        public void FromExisting_EmptyValue_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() => TransactionId.FromExisting("", "GameA", "purchase"));
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
            var id     = TransactionId.Generate("GameA", "purchase");
            var policy = IdempotencyPolicy.Default;
            var result = TransactionIdempotencyGate.CanProcessTransaction("GameA", id, policy);
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
            var id     = TransactionId.Generate("GameA", "task_claim");
            Assert.IsTrue(ledger.TryRecord(id), "First record must succeed.");
        }

        [Test]
        public void Ledger_TryRecord_Duplicate_ReturnsFalse()
        {
            var ledger = new IdempotencyLedger();
            var id     = TransactionId.Generate("GameA", "task_claim");
            ledger.TryRecord(id);
            Assert.IsFalse(ledger.TryRecord(id), "Duplicate record must be rejected.");
        }

        [Test]
        public void Ledger_IsRecorded_TrueAfterRecord()
        {
            var ledger = new IdempotencyLedger();
            var id     = TransactionId.Generate("GameA", "migration");
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
            ledger.TryRecord(TransactionId.Generate("GameA", "purchase"));
            ledger.TryRecord(TransactionId.Generate("GameA", "purchase"));
            ledger.Clear();
            Assert.AreEqual(0, ledger.Count);
        }

        [Test]
        public void Ledger_Clear_AllowsReRecording()
        {
            var ledger = new IdempotencyLedger();
            var id     = TransactionId.FromExisting("fixed-key", "GameA", "ad_reward");
            ledger.TryRecord(id);
            ledger.Clear();
            Assert.IsTrue(ledger.TryRecord(id), "After clear, same key must be accepted again.");
        }

        [Test]
        public void Ledger_Count_IncrementsOnUniqueRecords()
        {
            var ledger = new IdempotencyLedger();
            ledger.TryRecord(TransactionId.Generate("GameA", "purchase"));
            ledger.TryRecord(TransactionId.Generate("GameA", "purchase"));
            Assert.AreEqual(2, ledger.Count);
        }

        [Test]
        public void Ledger_Count_DoesNotIncrementOnDuplicate()
        {
            var ledger = new IdempotencyLedger();
            var id     = TransactionId.Generate("GameA", "task_claim");
            ledger.TryRecord(id);
            ledger.TryRecord(id);
            Assert.AreEqual(1, ledger.Count);
        }
    }
}
