// ENG-M13-002 | Monetization integrity: non-success results never expose an outcome.
using System;
using System.Threading;
using System.Threading.Tasks;
using Match3.Engine.Runtime;
using NUnit.Framework;

namespace Match3.Engine.Tests.Editor
{
    public sealed class ServiceResultSafetyTests
    {
        private static readonly ServiceResultStatus[] NonSuccess =
        {
            ServiceResultStatus.Disabled, ServiceResultStatus.Denied,
            ServiceResultStatus.Unavailable, ServiceResultStatus.Failed,
            ServiceResultStatus.NotFound
        };

        private static GameServiceContext Context(string gameId)
        {
            var profile = GameProfileCatalog.RequireProfile(
                GameProfileCatalog.LoadPlaceholders(), gameId);
            return new GameServiceContext(profile, new PrivacySnapshot(gameId));
        }

        [Test]
        public void EnumDefaultsAreNonSuccessSentinels()
        {
            Assert.That(default(AdOutcome), Is.EqualTo(AdOutcome.None));
            Assert.That(default(PurchaseOutcome), Is.EqualTo(PurchaseOutcome.None));
            Assert.That(default(ServiceResultStatus), Is.EqualTo(ServiceResultStatus.None));
            Assert.That((int)AdOutcome.Rewarded, Is.Not.EqualTo(0));
            Assert.That((int)PurchaseOutcome.Purchased, Is.Not.EqualTo(0));
            Assert.That((int)ServiceResultStatus.Succeeded, Is.Not.EqualTo(0));
        }

        [Test]
        public void RejectedResultsDoNotExposeAnOutcome()
        {
            foreach (var status in NonSuccess)
            {
                var purchase = ServiceResult<PurchaseOutcome>.Rejected(status, "test.reject");
                var ad = ServiceResult<AdOutcome>.Rejected(status, "test.reject");
                Assert.That(purchase.IsSuccess, Is.False);
                Assert.That(ad.IsSuccess, Is.False);
                Assert.Throws<InvalidOperationException>(() => { var _ = purchase.Value; });
                Assert.Throws<InvalidOperationException>(() => { var _ = ad.Value; });
            }
        }

        [Test]
        public async Task DisabledPurchaseAndAdResultsDoNotExposeAnOutcome()
        {
            var disabled = new DisabledCommercialServices();
            var context = Context("Coral_Rescue");
            var purchase = await disabled.PurchaseAsync(
                context, "test.product", CancellationToken.None);
            var ad = await disabled.ShowRewardedAsync(context, CancellationToken.None);
            var entitlement = await disabled.HasEntitlementAsync(
                context, "premium", CancellationToken.None);

            Assert.That(purchase.Status, Is.EqualTo(ServiceResultStatus.Disabled));
            Assert.Throws<InvalidOperationException>(() => { var _ = purchase.Value; });
            Assert.That(ad.Status, Is.EqualTo(ServiceResultStatus.Disabled));
            Assert.Throws<InvalidOperationException>(() => { var _ = ad.Value; });
            Assert.That(entitlement.IsSuccess, Is.False);
            Assert.Throws<InvalidOperationException>(() => { var _ = entitlement.Value; });
        }

        [Test]
        public void SuccessResultsReturnTheirValue()
        {
            Assert.That(ServiceResult<PurchaseOutcome>.Success(PurchaseOutcome.Purchased).Value,
                Is.EqualTo(PurchaseOutcome.Purchased));
            Assert.That(ServiceResult<PurchaseOutcome>.Success(PurchaseOutcome.Cancelled).Value,
                Is.EqualTo(PurchaseOutcome.Cancelled));
            Assert.That(ServiceResult<AdOutcome>.Success(AdOutcome.Rewarded).Value,
                Is.EqualTo(AdOutcome.Rewarded));
            Assert.That(ServiceResult<bool>.Success(false).Value, Is.False);
            Assert.That(ServiceResult<string>.Success("id").Value, Is.EqualTo("id"));
        }

        [Test]
        public void SentinelOrUndefinedOutcomesCannotBeSuccessful()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                ServiceResult<PurchaseOutcome>.Success(PurchaseOutcome.None));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                ServiceResult<AdOutcome>.Success(AdOutcome.None));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                ServiceResult<PurchaseOutcome>.Success((PurchaseOutcome)99));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                ServiceResult<bool>.Rejected(ServiceResultStatus.None, "test.none"));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                ServiceResult<bool>.Rejected((ServiceResultStatus)99, "test.undefined"));
        }

        [Test]
        public void DisabledPurchaseAdAndReadPreserveCancellation()
        {
            var disabled = new DisabledCommercialServices();
            var context = Context("Wonder_Pop");
            using (var source = new CancellationTokenSource())
            {
                source.Cancel();
                Assert.ThrowsAsync<TaskCanceledException>(async () =>
                    await disabled.PurchaseAsync(context, "test.product", source.Token));
                Assert.ThrowsAsync<TaskCanceledException>(async () =>
                    await disabled.ShowRewardedAsync(context, source.Token));
                Assert.ThrowsAsync<TaskCanceledException>(async () =>
                    await disabled.ReadAsync(context, "save.main", source.Token));
            }
        }

        [Test]
        public void DisabledAdaptersRejectNullContextAndOversizedCodes()
        {
            var disabled = new DisabledCommercialServices();
            var context = Context("Coral_Rescue");
            var none = CancellationToken.None;
            var tooLong = new string('a', 129);
            var maxLength = new string('a', 128);

            Assert.Throws<ArgumentNullException>(() => disabled.ShowRewardedAsync(null, none));
            Assert.Throws<ArgumentNullException>(() => disabled.PurchaseAsync(null, "test.product", none));
            Assert.Throws<ArgumentNullException>(() => disabled.Record(null, "level.started"));
            Assert.Throws<ArgumentNullException>(() => disabled.Evaluate(null, ConsentPurpose.Analytics));
            Assert.Throws<ArgumentNullException>(() => disabled.HasEntitlementAsync(null, "premium", none));
            Assert.Throws<ArgumentNullException>(() => disabled.RecordNonFatal(null, "gameplay.error"));

            Assert.Throws<ArgumentException>(() => disabled.PurchaseAsync(context, tooLong, none));
            Assert.Throws<ArgumentException>(() => disabled.Record(context, tooLong));
            Assert.Throws<ArgumentException>(() => disabled.HasEntitlementAsync(context, tooLong, none));
            Assert.Throws<ArgumentException>(() => disabled.GetStringAsync(context, tooLong, none));
            Assert.Throws<ArgumentException>(() =>
                disabled.ScheduleAsync(context, "daily.reminder", tooLong, 1, none));

            // Boundary: exactly 128 characters is accepted and still fails closed.
            Assert.That(disabled.Record(context, maxLength).Status,
                Is.EqualTo(ServiceResultStatus.Disabled));
        }
    }
}
