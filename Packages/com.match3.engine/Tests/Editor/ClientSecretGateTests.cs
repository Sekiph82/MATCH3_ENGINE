using System;
using NUnit.Framework;
using Match3.Engine.Runtime.Commerce;

namespace Match3.Engine.Tests.Editor
{
    public sealed class ClientSecretGateTests
    {
        [Test]
        public void Policy_NullJustification_Throws()
        {
            Assert.Throws<ArgumentException>(() => new PaymentSecretPolicy(false, null));
        }

        [Test]
        public void Policy_EmptyJustification_Throws()
        {
            Assert.Throws<ArgumentException>(() => new PaymentSecretPolicy(false, string.Empty));
        }

        [Test]
        public void Policy_Default_NoClientSecrets()
        {
            var policy = PaymentSecretPolicy.Default;
            Assert.That(policy.ClientSecretsAllowed, Is.False);
        }

        [Test]
        public void Policy_Default_ClientSecretsNotAllowed()
        {
            var policy = PaymentSecretPolicy.Default;
            Assert.That(policy.PolicyJustification, Does.Contain("server-side"));
        }

        [Test]
        public void ClientGate_SecretsPolicyDisabled_EligibilityIneligible()
        {
            var policy = new PaymentSecretPolicy(true, "test-allowed");
            var result = ClientSecretGate.CheckPaymentFlowEligibility("Coral_Rescue", policy);
            Assert.That(result.IsEligible, Is.False);
            Assert.That(result.Reason, Is.EqualTo("ClientPaymentSecretsNotAllowed"));
        }

        [Test]
        public void ClientGate_WonderPop_AlwaysIneligible()
        {
            var policy = PaymentSecretPolicy.Default;
            var result = ClientSecretGate.CheckPaymentFlowEligibility("Wonder_Pop", policy);
            Assert.That(result.IsEligible, Is.False);
            Assert.That(result.Reason, Is.EqualTo("WonderPopPaymentProhibited"));
        }

        [Test]
        public void ClientGate_NoSecrets_NonWonderPop_Eligible()
        {
            var policy = PaymentSecretPolicy.Default;
            var result = ClientSecretGate.CheckPaymentFlowEligibility("Coral_Rescue", policy);
            Assert.That(result.IsEligible, Is.True);
        }

        [Test]
        public void ClientGate_IsEmbeddingAllowed_False_WhenNoSecrets()
        {
            var policy = PaymentSecretPolicy.Default;
            Assert.That(ClientSecretGate.IsClientEmbeddingAllowed(policy), Is.False);
        }

        [Test]
        public void ClientGate_AssertNoClientSecrets_ThrowsWhenAllowed()
        {
            var policy = new PaymentSecretPolicy(true, "test-allowed");
            Assert.Throws<InvalidOperationException>(() => ClientSecretGate.AssertNoClientSecrets(policy));
        }

        [Test]
        public void ClientGate_AssertNoClientSecrets_NoThrowWhenForbidden()
        {
            var policy = PaymentSecretPolicy.Default;
            Assert.DoesNotThrow(() => ClientSecretGate.AssertNoClientSecrets(policy));
        }
    }
}
