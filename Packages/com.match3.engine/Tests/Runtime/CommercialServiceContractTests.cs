using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Match3.Engine.Runtime;
using NUnit.Framework;

namespace Match3.Engine.Tests
{
    public sealed class CommercialServiceContractTests
    {
        private static GameServiceContext Context(string gameId)
        {
            var profile = GameProfileCatalog.RequireProfile(
                GameProfileCatalog.LoadPlaceholders(), gameId);
            return new GameServiceContext(profile,
                new PrivacySnapshot(gameId));
        }

        [Test]
        public void ServiceContextRejectsCrossTitlePrivacyState()
        {
            var profile = GameProfileCatalog.RequireProfile(
                GameProfileCatalog.LoadPlaceholders(), "Coral_Rescue");
            Assert.Throws<ArgumentException>(() =>
                new GameServiceContext(profile,
                    new PrivacySnapshot("Wonder_Pop")));
            Assert.Throws<ArgumentNullException>(() =>
                new GameServiceContext(profile, null));
        }

        [Test]
        public async Task DisabledServiceAdaptersFailClosedAcrossAllContracts()
        {
            var disabled = new DisabledCommercialServices();
            var context = Context("Wonder_Pop");
            var cancellation = CancellationToken.None;
            var envelope = new SaveEnvelope(1, "{} ");

            AssertDisabled(await disabled.ShowRewardedAsync(context, cancellation));
            AssertDisabled(await disabled.PurchaseAsync(
                context, "test.product", cancellation));
            AssertDisabled(disabled.Record(context, "level.started"));
            AssertDisabled(await disabled.GetCampaignTokenAsync(
                context, cancellation));
            Assert.That(disabled.Evaluate(context, ConsentPurpose.Analytics).Status,
                Is.EqualTo(ConsentDecisionStatus.Unknown));
            Assert.That(disabled.Evaluate(context, ConsentPurpose.Advertising).IsAllowed,
                Is.False);
            AssertDisabled(await disabled.GetPlayerIdAsync(context, cancellation));
            AssertDisabled(await disabled.ReadAsync(
                context, "save.main", cancellation));
            AssertDisabled(await disabled.WriteAsync(
                context, "save.main", envelope, cancellation));
            AssertDisabled(await disabled.HasEntitlementAsync(
                context, "premium", cancellation));
            AssertDisabled(await disabled.GetStringAsync(
                context, "feature.enabled", cancellation));
            AssertDisabled(await disabled.ScheduleAsync(
                context, "daily.reminder", "notification.daily", 1, cancellation));
            AssertDisabled(await disabled.CancelAsync(
                context, "daily.reminder", cancellation));
            AssertDisabled(disabled.RecordNonFatal(context, "gameplay.invalid_state"));
        }

        [Test]
        public void DisabledAsyncServicesPreserveCancellation()
        {
            var disabled = new DisabledCommercialServices();
            using (var source = new CancellationTokenSource())
            {
                source.Cancel();
                Assert.ThrowsAsync<TaskCanceledException>(async () =>
                    await disabled.GetPlayerIdAsync(Context("Wonder_Pop"),
                        source.Token));
            }
        }

        [Test]
        public async Task MockServicesProvideDeterministicPositiveNegativeAndTitleIsolationCases()
        {
            var mocks = new MockCommercialServices();
            var coral = Context("Coral_Rescue");
            var wonder = Context("Wonder_Pop");
            var envelope = new SaveEnvelope(1, "coral-save");

            Assert.That((await mocks.PurchaseAsync(
                coral, "test.product", CancellationToken.None)).Value,
                Is.EqualTo(PurchaseOutcome.Purchased));
            mocks.PurchaseResult = ServiceResult<PurchaseOutcome>.Rejected(
                ServiceResultStatus.Failed, "mock.purchase.failed");
            Assert.That((await mocks.PurchaseAsync(
                coral, "test.product", CancellationToken.None)).Status,
                Is.EqualTo(ServiceResultStatus.Failed));

            Assert.That((await mocks.WriteAsync(
                coral, "save.main", envelope, CancellationToken.None)).Value,
                Is.True);
            Assert.That((await mocks.ReadAsync(
                coral, "save.main", CancellationToken.None)).Value.Payload,
                Is.EqualTo("coral-save"));
            Assert.That((await mocks.ReadAsync(
                wonder, "save.main", CancellationToken.None)).Status,
                Is.EqualTo(ServiceResultStatus.NotFound));

            mocks.Consent = ConsentDecision.Allow();
            Assert.That(mocks.Evaluate(coral, ConsentPurpose.Analytics).IsAllowed,
                Is.True);
        }

        [Test]
        public void InvalidServiceResultsCannotMasqueradeAsSuccess()
        {
            Assert.Throws<ArgumentNullException>(() =>
                ServiceResult<string>.Success(null));
            Assert.Throws<ArgumentException>(() =>
                ServiceResult<bool>.Rejected(
                    ServiceResultStatus.Succeeded, "invalid"));
            Assert.Throws<ArgumentException>(() =>
                ServiceResult<bool>.Rejected(
                    ServiceResultStatus.Disabled, " "));
            Assert.Throws<ArgumentException>(() =>
                ConsentDecision.Deny(" "));
        }

        private static void AssertDisabled<T>(ServiceResult<T> result)
        {
            Assert.That(result.Status, Is.EqualTo(ServiceResultStatus.Disabled));
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.ReasonCode, Is.EqualTo("service.disabled"));
        }

        private sealed class MockCommercialServices : IAdsService,
            IIapService, IAnalyticsSink, IAttributionService, IConsentGate,
            IPlayerIdentity, ICloudSave, IEntitlementService, IRemoteConfig,
            INotification, ICrashReporter
        {
            private readonly Dictionary<string, SaveEnvelope> _saves =
                new Dictionary<string, SaveEnvelope>(StringComparer.Ordinal);

            public ServiceResult<PurchaseOutcome> PurchaseResult { get; set; } =
                ServiceResult<PurchaseOutcome>.Success(PurchaseOutcome.Purchased);
            public ConsentDecision Consent { get; set; } =
                ConsentDecision.Unknown("mock.consent.unknown");

            public Task<ServiceResult<AdOutcome>> ShowRewardedAsync(
                GameServiceContext context, CancellationToken cancellationToken) =>
                Result<AdOutcome>(AdOutcome.Rewarded, cancellationToken);

            public Task<ServiceResult<PurchaseOutcome>> PurchaseAsync(
                GameServiceContext context, string productId,
                CancellationToken cancellationToken)
            {
                if (cancellationToken.IsCancellationRequested)
                    return Task.FromCanceled<ServiceResult<PurchaseOutcome>>(
                        cancellationToken);
                return Task.FromResult(PurchaseResult);
            }

            public ServiceResult<bool> Record(GameServiceContext context,
                string eventCode) => ServiceResult<bool>.Success(true);

            public Task<ServiceResult<string>> GetCampaignTokenAsync(
                GameServiceContext context, CancellationToken cancellationToken) =>
                Result("mock.campaign", cancellationToken);

            public ConsentDecision Evaluate(GameServiceContext context,
                ConsentPurpose purpose) => Consent;

            public Task<ServiceResult<string>> GetPlayerIdAsync(
                GameServiceContext context, CancellationToken cancellationToken) =>
                Result("mock:" + context.Profile.Id, cancellationToken);

            public Task<ServiceResult<SaveEnvelope>> ReadAsync(
                GameServiceContext context, string key,
                CancellationToken cancellationToken)
            {
                if (cancellationToken.IsCancellationRequested)
                    return Task.FromCanceled<ServiceResult<SaveEnvelope>>(
                        cancellationToken);
                SaveEnvelope envelope;
                var mapKey = context.Profile.Id + "/" + key;
                return Task.FromResult(_saves.TryGetValue(mapKey, out envelope)
                    ? ServiceResult<SaveEnvelope>.Success(envelope)
                    : ServiceResult<SaveEnvelope>.Rejected(
                        ServiceResultStatus.NotFound, "mock.save.not_found"));
            }

            public Task<ServiceResult<bool>> WriteAsync(
                GameServiceContext context, string key, SaveEnvelope envelope,
                CancellationToken cancellationToken)
            {
                if (cancellationToken.IsCancellationRequested)
                    return Task.FromCanceled<ServiceResult<bool>>(
                        cancellationToken);
                _saves[context.Profile.Id + "/" + key] = envelope;
                return Task.FromResult(ServiceResult<bool>.Success(true));
            }

            public Task<ServiceResult<bool>> HasEntitlementAsync(
                GameServiceContext context, string entitlementId,
                CancellationToken cancellationToken) =>
                Result(false, cancellationToken);

            public Task<ServiceResult<string>> GetStringAsync(
                GameServiceContext context, string key,
                CancellationToken cancellationToken) =>
                Result("mock.config.value", cancellationToken);

            public Task<ServiceResult<bool>> ScheduleAsync(
                GameServiceContext context, string notificationId,
                string messageCode, long scheduledAtUnixSeconds,
                CancellationToken cancellationToken) =>
                Result(true, cancellationToken);

            public Task<ServiceResult<bool>> CancelAsync(
                GameServiceContext context, string notificationId,
                CancellationToken cancellationToken) =>
                Result(true, cancellationToken);

            public ServiceResult<bool> RecordNonFatal(
                GameServiceContext context, string errorCode) =>
                ServiceResult<bool>.Success(true);

            private static Task<ServiceResult<T>> Result<T>(T value,
                CancellationToken cancellationToken)
            {
                if (cancellationToken.IsCancellationRequested)
                    return Task.FromCanceled<ServiceResult<T>>(
                        cancellationToken);
                return Task.FromResult(ServiceResult<T>.Success(value));
            }
        }
    }
}
