using System;
using System.Threading;
using System.Threading.Tasks;
using Match3.Engine.Runtime;

namespace Match3.Engine.Demo
{
    // Adds a bounded per-read lifetime without changing the shared Engine
    // package's default HTTP policy. The signed-reader transport receives the
    // linked token for both headers and body reads.
    public sealed class NeutralTimeoutRemoteReadTransport : IRemoteReadTransport
    {
        private readonly IRemoteReadTransport _inner;
        private readonly TimeSpan _timeout;

        public NeutralTimeoutRemoteReadTransport(
            IRemoteReadTransport inner, TimeSpan timeout)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            if (timeout <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(timeout));
            _timeout = timeout;
        }

        public async Task<RemoteReadResponse> FetchAsync(Uri url,
            long maximumBytes, CancellationToken cancellation)
        {
            using (var lifetime = CancellationTokenSource
                .CreateLinkedTokenSource(cancellation))
            {
                lifetime.CancelAfter(_timeout);
                try
                {
                    return await _inner.FetchAsync(url, maximumBytes,
                        lifetime.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when
                    (lifetime.IsCancellationRequested &&
                     !cancellation.IsCancellationRequested)
                {
                    throw new TimeoutException(
                        "Signed HTTPS read exceeded its configured timeout.");
                }
            }
        }
    }
}
