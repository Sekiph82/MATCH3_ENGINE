using System.Threading;
using System.Threading.Tasks;
using Match3.Engine.Runtime;
using Match3.Engine.Runtime.Commerce;
using NUnit.Framework;

namespace Match3.Engine.Tests.Editor
{
    public sealed class CommerceArchitectureAuditTests
    {
        // ---- minimal inline mocks (no framework needed) ----

        private sealed class StubAds : IAdsService
        {
            public Task<ServiceResult<AdOutcome>> ShowRewardedAsync(
                GameServiceContext context, CancellationToken ct) =>
                Task.FromResult(ServiceResult<AdOutcome>.Success(AdOutcome.NotReady));
        }

        private sealed class StubIap : IIapService
        {
            public Task<ServiceResult<PurchaseOutcome>> PurchaseAsync(
                GameServiceContext context, string productId, CancellationToken ct) =>
                Task.FromResult(ServiceResult<PurchaseOutcome>.Success(PurchaseOutcome.Cancelled));
        }

        private sealed class StubAnalytics : IAnalyticsSink
        {
            public ServiceResult<bool> Record(GameServiceContext context, string eventCode) =>
                ServiceResult<bool>.Success(true);
        }

        // ---- tests ----

        [Test]
        public void WonderPop_WithNullServices_AuditPasses()
        {
            var result = CommerceArchitectureAudit.RunAudit("Wonder_Pop", null, null, null);
            Assert.That(result.Passed, Is.True);
        }

        [Test]
        public void WonderPop_WithAnyAdsService_AuditFails()
        {
            var result = CommerceArchitectureAudit.RunAudit("Wonder_Pop", new StubAds(), null, null);
            Assert.That(result.Passed, Is.False);
            Assert.That(result.Findings, Is.Not.Null.And.Not.Empty);
        }

        [Test]
        public void OtherGame_WithMockAds_AuditPasses()
        {
            var result = CommerceArchitectureAudit.RunAudit("Coral_Rescue", new StubAds(), null, null);
            Assert.That(result.Passed, Is.True);
        }

        [Test]
        public void OtherGame_WithNullServices_AuditPasses()
        {
            var result = CommerceArchitectureAudit.RunAudit("Garage_Legends", null, null, null);
            Assert.That(result.Passed, Is.True);
        }

        [Test]
        public void AuditResult_Ok_HasNullFindings()
        {
            var result = AuditResult.Ok();
            Assert.That(result.Passed, Is.True);
            Assert.That(result.Findings, Is.Null);
        }

        [Test]
        public void WonderPop_WithIapService_AuditFails()
        {
            var result = CommerceArchitectureAudit.RunAudit("Wonder_Pop", null, new StubIap(), null);
            Assert.That(result.Passed, Is.False);
        }

        [Test]
        public void IsWonderPopExcluded_NullServices_ReturnsTrue()
        {
            Assert.That(CommerceArchitectureAudit.IsWonderPopExcluded(null, null), Is.True);
        }

        [Test]
        public void IsWonderPopExcluded_NonNullAds_ReturnsFalse()
        {
            Assert.That(CommerceArchitectureAudit.IsWonderPopExcluded(new StubAds(), null), Is.False);
        }
    }
}
