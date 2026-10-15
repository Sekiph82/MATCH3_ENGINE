using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Match3.Engine.Runtime
{
    public sealed class RemoteReadOrigin
    {
        private readonly Uri _base;
        public string Host => _base.Host;

        public RemoteReadOrigin(string approvedBaseUrl)
        {
            Uri uri;
            if (!Uri.TryCreate(approvedBaseUrl, UriKind.Absolute, out uri) ||
                uri.Scheme != Uri.UriSchemeHttps || uri.Port != 443 ||
                string.IsNullOrWhiteSpace(uri.Host) ||
                !string.IsNullOrEmpty(uri.UserInfo) ||
                !string.IsNullOrEmpty(uri.Query) ||
                !string.IsNullOrEmpty(uri.Fragment) ||
                uri.AbsolutePath.Contains("..") ||
                uri.AbsolutePath.Contains("%") ||
                !approvedBaseUrl.EndsWith("/", StringComparison.Ordinal))
                throw new ArgumentException(
                    "Use a pinned HTTPS host and path prefix ending with /.",
                    nameof(approvedBaseUrl));
            _base = uri;
        }

        public Uri ForPath(string path)
        {
            if (!RemoteContentSafety.IsSafePath(path))
                throw new ArgumentException("Unsafe untrusted content path.", nameof(path));
            var segments = path.Split('/')
                .Select(Uri.EscapeDataString);
            var target = new Uri(_base, string.Join("/", segments));
            if (target.Host != _base.Host || target.Scheme != Uri.UriSchemeHttps ||
                target.Port != 443 ||
                !target.AbsolutePath.StartsWith(_base.AbsolutePath,
                    StringComparison.Ordinal))
                throw new InvalidOperationException(
                    "Untrusted content path escaped approved origin.");
            return target;
        }
    }

    public sealed class RemoteReadResponse
    {
        private readonly byte[] _bytes;
        public int HttpStatus { get; }
        public byte[] Bytes => (byte[])_bytes.Clone();

        public RemoteReadResponse(int status, byte[] bytes)
        {
            if (status < 100 || status > 599 || bytes == null ||
                bytes.LongLength > RemoteContentSafety.FileByteLimit)
                throw new ArgumentOutOfRangeException(nameof(bytes));
            HttpStatus = status;
            _bytes = (byte[])bytes.Clone();
        }
    }

    public interface IRemoteReadTransport
    {
        Task<RemoteReadResponse> FetchAsync(Uri url, long maximumBytes,
            CancellationToken cancellation);
    }

    public sealed class RemoteHttpsReadTransport : IRemoteReadTransport
    {
        // Real read-only adapter. No auto-redirect or GitHub/R2 credentials.
        // The orchestrator pins the origin and passes a bounded manifest size.
        public async Task<RemoteReadResponse> FetchAsync(Uri url,
            long maximumBytes, CancellationToken cancellation)
        {
            if (url == null || url.Scheme != Uri.UriSchemeHttps ||
                url.Port != 443 || !string.IsNullOrEmpty(url.UserInfo))
                throw new ArgumentException("HTTPS without credentials required.", nameof(url));
            if (maximumBytes < 1 ||
                maximumBytes > RemoteContentSafety.FileByteLimit)
                throw new ArgumentOutOfRangeException(nameof(maximumBytes));
            using (var handler = new HttpClientHandler { AllowAutoRedirect = false })
            using (var http = new HttpClient(handler))
            using (var request = new HttpRequestMessage(HttpMethod.Get, url))
            {
                http.Timeout = TimeSpan.FromSeconds(15);
                using (var response = await http.SendAsync(request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellation).ConfigureAwait(false))
                {
                    var size = response.Content.Headers.ContentLength;
                    if (size.HasValue && size.Value > maximumBytes)
                        throw new InvalidOperationException(
                            "Remote payload exceeds signed byte budget.");
                    if (response.StatusCode != HttpStatusCode.OK)
                        return new RemoteReadResponse(
                            (int)response.StatusCode, new byte[0]);
                    using (var stream = await response.Content
                        .ReadAsStreamAsync().ConfigureAwait(false))
                    using (var buffer = new MemoryStream())
                    {
                        var chunk = new byte[8192];
                        while (true)
                        {
                            cancellation.ThrowIfCancellationRequested();
                            var n = await stream.ReadAsync(
                                chunk, 0, chunk.Length, cancellation)
                                .ConfigureAwait(false);
                            if (n <= 0) break;
                            if (buffer.Length + n > maximumBytes)
                                throw new InvalidOperationException(
                                    "Remote stream exceeded declared bytes.");
                            buffer.Write(chunk, 0, n);
                        }
                        return new RemoteReadResponse(200, buffer.ToArray());
                    }
                }
            }
        }
    }

    public sealed class RemoteDownloadReceipt
    {
        private readonly byte[] _payload;
        public int Attempts { get; }
        public string Path { get; }
        public byte[] Bytes => (byte[])_payload.Clone();

        internal RemoteDownloadReceipt(string path, byte[] bytes, int attempts)
        {
            Path = path;
            _payload = (byte[])bytes.Clone();
            Attempts = attempts;
        }
    }


    public sealed class RemoteFetchedPackage
    {
        private readonly IReadOnlyDictionary<string, byte[]> _bytes;
        public RemoteVerifiedRelease Verified { get; }
        public long TotalBytes { get; }

        internal RemoteFetchedPackage(RemoteVerifiedRelease verified,
            IReadOnlyDictionary<string, byte[]> bytes)
        {
            Verified = verified;
            var copy = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            foreach (var pair in bytes)
                copy.Add(pair.Key, (byte[])pair.Value.Clone());
            _bytes = new System.Collections.ObjectModel
                .ReadOnlyDictionary<string, byte[]>(copy);
            TotalBytes = copy.Sum(x => (long)x.Value.Length);
        }

        public byte[] GetVerifiedBytes(string path)
        {
            byte[] item;
            if (path == null || !_bytes.TryGetValue(path, out item))
                throw new ArgumentException("Unknown verified staged path.", nameof(path));
            return (byte[])item.Clone();
        }
    }

    public static class RemoteDownloadCoordinator
    {
        public static async Task<RemoteFetchedPackage> FetchStageAsync(
            RemoteContentManifest manifest, byte[] signature,
            IEnumerable<RemotePublicSigningKey> trusted,
            RemoteReadOrigin origin, IRemoteReadTransport transport,
            string gameId, RemotePlatform platform, RemoteChannel channel,
            EngineVersion engine, long nowUnixSeconds,
            CancellationToken cancellation, int maxParallel = 2,
            Action<long, long> onVerifiedProgress = null)
        {
            if (manifest == null) throw new ArgumentNullException(nameof(manifest));
            if (maxParallel < 1 || maxParallel > 3)
                throw new ArgumentOutOfRangeException(nameof(maxParallel));
            // Verify before any requests, preventing untrusted manifests
            // from sending reads to attacker-chosen URLs.
            RemoteManifestVerifier.Verify(manifest, signature, trusted,
                gameId, platform, channel, engine, nowUnixSeconds);
            var keys = trusted.ToArray();
            var total = manifest.Entries.Sum(x => x.Length);
            long verified = 0;
            var progressGate = new object();
            var output = new System.Collections.Concurrent
                .ConcurrentDictionary<string, byte[]>(StringComparer.Ordinal);
            using (var semaphore = new SemaphoreSlim(maxParallel, maxParallel))
            {
                var tasks = manifest.Entries.Select(async entry =>
                {
                    await semaphore.WaitAsync(cancellation).ConfigureAwait(false);
                    try
                    {
                        var receipt = await FetchVerifiedAsync(
                            manifest, signature, keys, origin, transport,
                            entry, gameId, platform, channel,
                            engine, nowUnixSeconds, cancellation)
                            .ConfigureAwait(false);
                        if (!output.TryAdd(receipt.Path, receipt.Bytes))
                            throw new InvalidOperationException(
                                "Duplicate staged path discovered.");
                        // The callback order must never show decreasing
                        // verified progress when parallel requests finish.
                        lock (progressGate)
                        {
                            verified = checked(verified + entry.Length);
                            if (onVerifiedProgress != null)
                                onVerifiedProgress(verified, total);
                        }
                    }
                    finally { semaphore.Release(); }
                }).ToArray();
                await Task.WhenAll(tasks).ConfigureAwait(false);
            }
            cancellation.ThrowIfCancellationRequested();
            var snapshot = new Dictionary<string, byte[]>(output,
                StringComparer.Ordinal);
            var sealedPackage = RemoteContentStager.VerifyComplete(
                manifest, signature, keys, gameId, platform, channel,
                engine, nowUnixSeconds, snapshot);
            return new RemoteFetchedPackage(sealedPackage, snapshot);
        }

        public static async Task<RemoteDownloadReceipt> FetchVerifiedAsync(
            RemoteContentManifest manifest, byte[] signature,
            IEnumerable<RemotePublicSigningKey> trusted,
            RemoteReadOrigin origin, IRemoteReadTransport transport,
            RemoteContentEntry entry, string gameId,
            RemotePlatform platform, RemoteChannel channel,
            EngineVersion engine, long nowUnixSeconds,
            CancellationToken cancellation, int maxAttempts = 3)
        {
            if (origin == null) throw new ArgumentNullException(nameof(origin));
            if (transport == null) throw new ArgumentNullException(nameof(transport));
            if (entry == null) throw new ArgumentNullException(nameof(entry));
            if (maxAttempts < 1 || maxAttempts > 3)
                throw new ArgumentOutOfRangeException(nameof(maxAttempts));
            RemoteManifestVerifier.Verify(manifest, signature, trusted,
                gameId, platform, channel, engine, nowUnixSeconds);
            if (!manifest.Entries.Any(f =>
                f.Path == entry.Path && f.Length == entry.Length &&
                f.Sha256 == entry.Sha256 && f.Kind == entry.Kind))
                throw new InvalidOperationException(
                    "Remote request is not bound to signed manifest.");
            var url = origin.ForPath(entry.Path);
            for (var attempt = 1; attempt <= maxAttempts; attempt++)
            {
                cancellation.ThrowIfCancellationRequested();
                try
                {
                    var response = await transport.FetchAsync(url, entry.Length,
                        cancellation).ConfigureAwait(false);
                    if (response.HttpStatus == 200)
                    {
                        var bytes = response.Bytes;
                        if (!RemoteContentSafety.Matches(
                            bytes, entry.Length, entry.Sha256))
                            throw new InvalidOperationException(
                                "Remote payload digest or length mismatch.");
                        RemoteContentSafety.RequireNonExecutable(bytes, entry.Kind);
                        return new RemoteDownloadReceipt(entry.Path, bytes, attempt);
                    }
                    if (response.HttpStatus != 408 &&
                        response.HttpStatus != 429 &&
                        response.HttpStatus < 500)
                        throw new InvalidOperationException(
                            "Non-retryable remote status: " + response.HttpStatus);
                }
                catch (OperationCanceledException)
                {
                    cancellation.ThrowIfCancellationRequested();
                    // Internal HttpClient timeout may be retried within cap.
                    if (attempt == maxAttempts) throw;
                }
                catch (HttpRequestException)
                {
                    if (attempt == maxAttempts) throw;
                }
                if (attempt == maxAttempts)
                    throw new InvalidOperationException(
                        "Remote content unavailable after bounded retries.");
                await Task.Delay(attempt * 150, cancellation).ConfigureAwait(false);
            }
            throw new InvalidOperationException("Unreachable download state.");
        }
    }
}
