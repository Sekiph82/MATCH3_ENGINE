using System;

namespace Match3.Engine.Runtime.Commerce
{
    public readonly struct PaymentSecretPolicy
    {
        public bool ClientSecretsAllowed { get; }
        public string PolicyJustification { get; }

        public PaymentSecretPolicy(bool clientSecretsAllowed, string policyJustification)
        {
            if (string.IsNullOrEmpty(policyJustification))
                throw new ArgumentException("PolicyJustification must not be null or empty.", nameof(policyJustification));
            ClientSecretsAllowed = clientSecretsAllowed;
            PolicyJustification = policyJustification;
        }

        public static PaymentSecretPolicy NoClientSecrets(string justification) =>
            new PaymentSecretPolicy(false, justification);

        public static PaymentSecretPolicy Default =>
            NoClientSecrets("All payment verification is server-side; never embed keys in client builds");
    }

    public static class ClientSecretGate
    {
        public static bool IsClientEmbeddingAllowed(PaymentSecretPolicy policy) =>
            policy.ClientSecretsAllowed;

        public static AdEligibilityResult CheckPaymentFlowEligibility(string gameId, PaymentSecretPolicy policy)
        {
            // Fail closed: a missing game context is never eligible
            if (string.IsNullOrEmpty(gameId))
                return AdEligibilityResult.Ineligible("GameIdMissing");

            // Fail closed: only catalog game IDs (Ordinal) are eligible
            if (!GameProfileCatalog.IsKnownId(gameId))
                return AdEligibilityResult.Ineligible("UnknownGame");

            if (policy.ClientSecretsAllowed)
                return AdEligibilityResult.Ineligible("ClientPaymentSecretsNotAllowed");

            if (string.Equals(gameId, "Wonder_Pop", StringComparison.Ordinal))
                return AdEligibilityResult.Ineligible("WonderPopPaymentProhibited");

            return AdEligibilityResult.Eligible();
        }

        public static void AssertNoClientSecrets(PaymentSecretPolicy policy)
        {
            if (policy.ClientSecretsAllowed)
                throw new InvalidOperationException("Client payment secrets are prohibited");
        }
    }
}
