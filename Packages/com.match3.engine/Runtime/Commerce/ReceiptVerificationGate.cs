using System;

namespace Match3.Engine.Runtime.Commerce
{
    public enum ReceiptVerificationStatus
    {
        Unverified,
        VerifiedValid,
        VerifiedInvalid,
        VerificationFailed,
        Expired,
        Refunded
    }

    public readonly struct ReceiptVerificationPolicy
    {
        public bool ServerVerificationRequired { get; }
        public string PolicyDescription { get; }

        public ReceiptVerificationPolicy(bool serverVerificationRequired, string policyDescription)
        {
            if (string.IsNullOrEmpty(policyDescription))
                throw new ArgumentException("PolicyDescription must not be null or empty.", nameof(policyDescription));
            ServerVerificationRequired = serverVerificationRequired;
            PolicyDescription = policyDescription;
        }

        public static ReceiptVerificationPolicy Required(string desc) =>
            new ReceiptVerificationPolicy(true, desc);

        public static ReceiptVerificationPolicy Default =>
            Required("All receipts must be validated server-side; never trust client assertion");
    }

    public readonly struct VerificationRecord
    {
        public string ReceiptId { get; }
        public string GameId { get; }
        public ReceiptVerificationStatus Status { get; }

        public VerificationRecord(string receiptId, string gameId, ReceiptVerificationStatus status)
        {
            if (string.IsNullOrEmpty(receiptId))
                throw new ArgumentException("ReceiptId must not be null or empty.", nameof(receiptId));
            if (string.IsNullOrEmpty(gameId))
                throw new ArgumentException("GameId must not be null or empty.", nameof(gameId));
            ReceiptId = receiptId;
            GameId = gameId;
            Status = status;
        }

        public static VerificationRecord Pending(string receiptId, string gameId) =>
            new VerificationRecord(receiptId, gameId, ReceiptVerificationStatus.Unverified);

        public static VerificationRecord Valid(string receiptId, string gameId) =>
            new VerificationRecord(receiptId, gameId, ReceiptVerificationStatus.VerifiedValid);

        public static VerificationRecord Invalid(string receiptId, string gameId) =>
            new VerificationRecord(receiptId, gameId, ReceiptVerificationStatus.VerifiedInvalid);
    }

    public static class ReceiptVerificationGate
    {
        public static bool CanGrantAfterVerification(string gameId, ReceiptVerificationStatus status)
        {
            if (string.Equals(gameId, "Wonder_Pop", StringComparison.Ordinal))
                return false;

            return status == ReceiptVerificationStatus.VerifiedValid;
        }

        public static bool IsClientAssertionAcceptable() => false;

        public static void AssertServerVerified(ReceiptVerificationStatus status)
        {
            if (status != ReceiptVerificationStatus.VerifiedValid)
                throw new InvalidOperationException("Receipt must be server-verified before granting items");
        }

        public static bool RequiresServerCall(ReceiptVerificationPolicy policy) => true;
    }
}
