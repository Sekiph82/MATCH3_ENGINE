using System;
using System.Threading;
using System.Threading.Tasks;
using Match3.Engine.Runtime;
using NUnit.Framework;

namespace Match3.Engine.Demo.Tests
{
    public sealed class NeutralTimeoutRemoteReadTransportEditModeTests
    {
        [Test]
        public void TimeoutCancelsTheUnderlyingReadAndReturnsInBound()
        {
            var transport = new CancellationAwarePendingTransport();
            var bounded = new NeutralTimeoutRemoteReadTransport(
                transport, TimeSpan.FromMilliseconds(100));
            var started = DateTime.UtcNow;

            Assert.ThrowsAsync<TimeoutException>(async () =>
                await bounded.FetchAsync(new Uri("https://signed.invalid/test"),
                    1024, CancellationToken.None));

            Assert.That(transport.ObservedCancellation, Is.True);
            Assert.That(DateTime.UtcNow - started,
                Is.LessThan(TimeSpan.FromSeconds(2)));
        }

        [Test]
        public void CallerCancellationRemainsCancellationRatherThanTimeout()
        {
            var transport = new CancellationAwarePendingTransport();
            var bounded = new NeutralTimeoutRemoteReadTransport(
                transport, TimeSpan.FromSeconds(5));
            using (var caller = new CancellationTokenSource())
            {
                caller.CancelAfter(50);
                Assert.CatchAsync<OperationCanceledException>(async () =>
                    await bounded.FetchAsync(new Uri("https://signed.invalid/test"),
                        1024, caller.Token));
            }

            Assert.That(transport.ObservedCancellation, Is.True);
        }

        private sealed class CancellationAwarePendingTransport : IRemoteReadTransport
        {
            public bool ObservedCancellation { get; private set; }

            public Task<RemoteReadResponse> FetchAsync(Uri url,
                long maximumBytes, CancellationToken cancellation)
            {
                var pending = new TaskCompletionSource<RemoteReadResponse>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                cancellation.Register(() =>
                {
                    ObservedCancellation = true;
                    pending.TrySetCanceled();
                });
                return pending.Task;
            }
        }
    }
}
