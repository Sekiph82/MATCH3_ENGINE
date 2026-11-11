using System;
using System.Collections.Generic;

namespace Match3.Engine.Runtime.Economy
{
    /// <summary>
    /// Immutable identifier for a single economy transaction.
    /// ENG-M16-002: idempotency key contract.
    /// </summary>
    public readonly struct TransactionId
    {
        public string Value   { get; }
        public string GameId  { get; }
        public string Source  { get; }

        public bool IsEmpty => string.IsNullOrEmpty(Value);

        private TransactionId(string value, string gameId, string source)
        {
            if (string.IsNullOrEmpty(value))   throw new ArgumentException("Value must not be null or empty.",  nameof(value));
            if (string.IsNullOrEmpty(gameId))  throw new ArgumentException("GameId must not be null or empty.", nameof(gameId));
            if (string.IsNullOrEmpty(source))  throw new ArgumentException("Source must not be null or empty.", nameof(source));
            Value  = value;
            GameId = gameId;
            Source = source;
        }

        /// <summary>Creates a new transaction ID using a fresh GUID (stdlib only).</summary>
        public static TransactionId Generate(string gameId, string source)
            => new TransactionId(Guid.NewGuid().ToString("N"), gameId, source);

        /// <summary>Wraps an existing opaque key (e.g. from a purchase receipt).</summary>
        public static TransactionId FromExisting(string value, string gameId, string source)
            => new TransactionId(value, gameId, source);

        public override string ToString() => $"{GameId}/{Source}/{Value}";
    }

    /// <summary>
    /// Policy that governs duplicate-rejection behaviour.
    /// ENG-M16-002.
    /// </summary>
    public readonly struct IdempotencyPolicy
    {
        public bool   RejectDuplicates   { get; }
        public string PolicyDescription  { get; }

        private IdempotencyPolicy(bool rejectDuplicates, string policyDescription)
        {
            if (string.IsNullOrEmpty(policyDescription))
                throw new ArgumentException("PolicyDescription must not be null or empty.", nameof(policyDescription));
            RejectDuplicates  = rejectDuplicates;
            PolicyDescription = policyDescription;
        }

        public static IdempotencyPolicy Strict(string desc) => new IdempotencyPolicy(true, desc);

        public static readonly IdempotencyPolicy Default = Strict(
            "Every transaction must have a unique idempotency key; duplicates rejected");
    }

    /// <summary>
    /// Result of a gate evaluation.
    /// </summary>
    public readonly struct TransactionEligibility
    {
        public bool   IsEligible { get; }
        public string Reason     { get; }

        private TransactionEligibility(bool eligible, string reason)
        {
            IsEligible = eligible;
            Reason     = reason ?? string.Empty;
        }

        public static TransactionEligibility Eligible()                  => new TransactionEligibility(true,  "Eligible");
        public static TransactionEligibility Ineligible(string reason)   => new TransactionEligibility(false, reason);
    }

    /// <summary>
    /// Gate that checks whether a transaction may be processed.
    /// Guard ordering: Wonder_Pop block first, then missing ID, then policy check.
    /// ENG-M16-002.
    /// </summary>
    public static class TransactionIdempotencyGate
    {
        private const string WonderPopId = "Wonder_Pop";

        public static TransactionEligibility CanProcessTransaction(
            string gameId, TransactionId txId, IdempotencyPolicy policy)
        {
            // Guard 1 (FIRST): Wonder_Pop is permanently ineligible.
            if (string.Equals(gameId, WonderPopId, StringComparison.Ordinal))
                return TransactionEligibility.Ineligible("WonderPopTransactionProhibited");

            // Guard 2: missing transaction ID.
            if (txId.IsEmpty)
                return TransactionEligibility.Ineligible("MissingTransactionId");

            // Guard 3: policy does not enforce idempotency (defensive – factory always sets true).
            if (!policy.RejectDuplicates)
                return TransactionEligibility.Ineligible("IdempotencyNotEnforced");

            return TransactionEligibility.Eligible();
        }

        /// <summary>Always true; every caller must supply an idempotency key.</summary>
        public static bool RequiresIdempotencyKey(IdempotencyPolicy policy) => true;
    }

    /// <summary>
    /// Exactly-once ledger keyed by transaction ID value.
    /// TryRecord returns false for duplicates; true when newly recorded.
    /// ENG-M16-002.
    /// </summary>
    public sealed class IdempotencyLedger
    {
        private readonly HashSet<string> _recorded = new HashSet<string>(StringComparer.Ordinal);

        public int Count => _recorded.Count;

        public bool TryRecord(TransactionId txId)
        {
            if (txId.IsEmpty) return false;
            return _recorded.Add(txId.Value);
        }

        public bool IsRecorded(string transactionIdValue)
            => !string.IsNullOrEmpty(transactionIdValue) && _recorded.Contains(transactionIdValue);

        public void Clear() => _recorded.Clear();
    }
}
