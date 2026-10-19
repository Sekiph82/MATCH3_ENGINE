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
        public async Task TimeoutCancelsTheUnderlyingReadAndReturnsInBound()
        {
            var transport = new CancellationAwarePendingTransport();
            var bounded = new NeutralTimeoutRemoteReadTransport(
                transport, TimeSpan.FromMilliseconds(100));
            var started = DateTime.UtcNow;

            TimeoutException timeout = null;
            try
            {
                await bounded.FetchAsync(new Uri("https://signed.invalid/test"),
                    1024, CancellationToken.None);
            }
            catch (TimeoutException exception)
            {
                timeout = exception;
            }
            Assert.That(timeout, Is.Not.Null);

            Assert.That(transport.ObservedCancellation, Is.True);
            Assert.That(DateTime.UtcNow - started,
                Is.LessThan(TimeSpan.FromSeconds(2)));
        }

        [Test]
        public async Task CallerCancellationRemainsCancellationRatherThanTimeout()
        {
            var transport = new CancellationAwarePendingTransport();
            var bounded = new NeutralTimeoutRemoteReadTransport(
                transport, TimeSpan.FromSeconds(5));
            using (var caller = new CancellationTokenSource())
            {
                caller.CancelAfter(50);
                OperationCanceledException canceled = null;
                try
                {
                    await bounded.FetchAsync(new Uri("https://signed.invalid/test"),
                        1024, caller.Token);
                }
                catch (OperationCanceledException exception)
                {
                    canceled = exception;
                }
                Assert.That(canceled, Is.Not.Null);
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
