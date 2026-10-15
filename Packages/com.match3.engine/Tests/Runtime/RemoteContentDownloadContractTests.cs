using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Match3.Engine.Runtime;
using NUnit.Framework;

namespace Match3.Engine.Tests
{
    public sealed class RemoteContentDownloadContractTests
    {
        private sealed class FakeTransport : IRemoteReadTransport
        {
            private readonly Queue<RemoteReadResponse> _responses =
                new Queue<RemoteReadResponse>();
            public int Calls { get; private set; }
            public Uri LastUrl { get; private set; }
            public bool ThrowTransientOnce { get; set; }

            public FakeTransport(params RemoteReadResponse[] responses)
            {
                foreach (var r in responses) _responses.Enqueue(r);
            }

            public Task<RemoteReadResponse> FetchAsync(
                Uri url, long maxBytes, CancellationToken cancellation)
            {
                cancellation.ThrowIfCancellationRequested();
                LastUrl = url;
                Calls++;
                if (ThrowTransientOnce)
                {
                    ThrowTransientOnce = false;
                    throw new HttpRequestException("Simulated network disconnect.");
                }
                if (_responses.Count == 0)
                    throw new HttpRequestException("No response queued.");
                return Task.FromResult(_responses.Dequeue());
            }
        }

        private static readonly byte[] Data = Encoding.UTF8.GetBytes(
            "{\"chunk\":1}");
        private static RemoteContentManifest Manifest() =>
            new RemoteContentManifest("Coral_Rescue",
                RemotePlatform.Android, RemoteChannel.FamilyTest,
                "release-1", "key-1", new EngineVersion(1, 0, 0),
                new EngineVersion(1, 2, 0), 1000, 5000, new[] {
                    new RemoteContentEntry("levels/chunk.json",
                        RemoteContentKind.Json, Data.Length,
                        RemoteContentSafety.Sha256(Data))
                });

        private static async Task<RemoteDownloadReceipt> Fetch(
            FakeTransport transport, CancellationToken cancellation,
            bool corruptSignature = false, int maxAttempts = 3)
        {
            using (var signer = new RSACryptoServiceProvider(2048))
            {
                var m = Manifest();
                var k = signer.ExportParameters(false);
                var key = new RemotePublicSigningKey("key-1",
                    k.Modulus, k.Exponent, 0, 10000);
                var sig = signer.SignData(m.CanonicalBytes(),
                    HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                if (corruptSignature) sig[0] ^= 0x5a;
                return await RemoteDownloadCoordinator.FetchVerifiedAsync(
                    m, sig, new[] { key },
                    new RemoteReadOrigin("https://content.example.com/signed/"),
                    transport, m.Entries[0], m.GameId, m.Platform,
                    m.Channel, new EngineVersion(1, 0, 0), 2000,
                    cancellation, maxAttempts);
            }
        }

        [Test]
        public async Task M11_003_AuthenticatedBoundedDownloadNeverUsesWriteCredentials()
        {
            var transport = new FakeTransport(new RemoteReadResponse(200, Data));
            var result = await Fetch(transport, CancellationToken.None);
            Assert.That(result.Attempts, Is.EqualTo(1));
            Assert.That(result.Bytes, Is.EqualTo(Data));
            Assert.That(result.Path, Is.EqualTo("levels/chunk.json"));
            Assert.That(transport.LastUrl.ToString(), Is.EqualTo(
                "https://content.example.com/signed/levels/chunk.json"));
            Assert.That(transport.Calls, Is.EqualTo(1));
            var mutableCopy = result.Bytes;
            mutableCopy[0] = 0;
            Assert.That(result.Bytes, Is.EqualTo(Data),
                "A caller cannot silently modify previously verified bytes.");
        }

        [Test]
        public void M11_003_MalformedOriginPathAndPreCanceledRequestsFailClosed()
        {
            Assert.Throws<ArgumentException>(() =>
                new RemoteReadOrigin("http://insecure.example/"));
            Assert.Throws<ArgumentException>(() =>
                new RemoteReadOrigin("https://trusted.example@evil.example/"));
            var origin = new RemoteReadOrigin("https://cdn.example.com/path/");
            Assert.Throws<ArgumentException>(() =>
                origin.ForPath("../secrets.json"));
            Assert.Throws<ArgumentException>(() =>
                origin.ForPath("levels/%2e%2e/password.json"));
            using (var cts = new CancellationTokenSource())
            {
                cts.Cancel();
                var t = new FakeTransport(new RemoteReadResponse(200, Data));
                Assert.ThrowsAsync<OperationCanceledException>(async () =>
                    await Fetch(t, cts.Token));
                Assert.That(t.Calls, Is.EqualTo(0));
            }
        }

        [Test]
        public async Task M11_011_TransientNetworkFaultRetriesWithinFixedCap()
        {
            var t = new FakeTransport(
                new RemoteReadResponse(503, new byte[0]),
                new RemoteReadResponse(200, Data));
            var result = await Fetch(t, CancellationToken.None, maxAttempts: 3);
            Assert.That(result.Attempts, Is.EqualTo(2));
            Assert.That(t.Calls, Is.EqualTo(2));

            var transient = new FakeTransport(new RemoteReadResponse(200, Data)) {
                ThrowTransientOnce = true
            };
            Assert.That((await Fetch(transient, CancellationToken.None)).Attempts,
                Is.EqualTo(2));

            var forbidden = new FakeTransport(
                new RemoteReadResponse(403, new byte[0]));
            Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await Fetch(forbidden, CancellationToken.None));
            Assert.That(forbidden.Calls, Is.EqualTo(1));

            var corrupt = new FakeTransport(
                new RemoteReadResponse(200, Encoding.UTF8.GetBytes("{\"chunk\":2}")));
            Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await Fetch(corrupt, CancellationToken.None));
            Assert.That(corrupt.Calls, Is.EqualTo(1));
        }

        [Test]
        public void M11_011_BadSignatureIsRejectedBeforeAnyTransport()
        {
            var transport = new FakeTransport(new RemoteReadResponse(200, Data));
            Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await Fetch(transport, CancellationToken.None, corruptSignature: true));
            Assert.That(transport.Calls, Is.Zero);
        }

        [Test]
        public async Task M11_003_FullFetchStageIsBoundedAndProgressIsVerifiedOnly()
        {
            var second = Encoding.UTF8.GetBytes("1,2\n");
            var input = new Dictionary<string, byte[]> {
                { "levels/chunk.json", Data },
                { "levels/extra.csv", second }
            };
            var completed = new List<long>();
            using (var signer = new RSACryptoServiceProvider(2048))
            {
                var manifest = new RemoteContentManifest("Coral_Rescue",
                    RemotePlatform.Android, RemoteChannel.FamilyTest,
                    "release-2", "key-1", new EngineVersion(1, 0, 0),
                    new EngineVersion(1, 2, 0), 1000, 5000, new[] {
                        new RemoteContentEntry("levels/chunk.json",
                            RemoteContentKind.Json, Data.Length,
                            RemoteContentSafety.Sha256(Data)),
                        new RemoteContentEntry("levels/extra.csv",
                            RemoteContentKind.LevelsCsv, second.Length,
                            RemoteContentSafety.Sha256(second))
                    }, sequence: 2);
                var k = signer.ExportParameters(false);
                var pinned = new RemotePublicSigningKey("key-1",
                    k.Modulus, k.Exponent, 0, 10000);
                var signature = signer.SignData(manifest.CanonicalBytes(),
                    HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                var transport = new MappingTransport(input);
                var finished = await RemoteDownloadCoordinator.FetchStageAsync(
                    manifest, signature, new[] { pinned },
                    new RemoteReadOrigin("https://cdn.example.com/signed/"),
                    transport, manifest.GameId, manifest.Platform,
                    manifest.Channel, new EngineVersion(1, 0, 0),
                    2000, CancellationToken.None, maxParallel: 2,
                    onVerifiedProgress: (current, total) =>
                    {
                        lock (completed) completed.Add(current);
                        Assert.That(current, Is.LessThanOrEqualTo(total));
                    });
                Assert.That(finished.TotalBytes,
                    Is.EqualTo(Data.Length + second.Length));
                Assert.That(finished.Verified.FileDigests.Count, Is.EqualTo(2));
                Assert.That(transport.Calls, Is.EqualTo(2));
                Assert.That(transport.PeakConcurrency, Is.LessThanOrEqualTo(2));
                Assert.That(completed.Count, Is.EqualTo(2));
                Assert.That(completed.Max(), Is.EqualTo(finished.TotalBytes));
                var edited = finished.GetVerifiedBytes("levels/chunk.json");
                edited[0] = 0;
                Assert.That(finished.GetVerifiedBytes("levels/chunk.json"),
                    Is.EqualTo(Data));
                Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
                    await RemoteDownloadCoordinator.FetchStageAsync(
                        manifest, signature, new[] { pinned },
                        new RemoteReadOrigin("https://cdn.example.com/"),
                        transport, manifest.GameId, manifest.Platform,
                        manifest.Channel, new EngineVersion(1, 0, 0),
                        2000, CancellationToken.None, maxParallel: 4));
            }
        }

        private sealed class MappingTransport : IRemoteReadTransport
        {
            private readonly IReadOnlyDictionary<string, byte[]> _map;
            private int _current;
            private int _peak;
            private int _calls;
            public int PeakConcurrency => _peak;
            public int Calls => _calls;

            public MappingTransport(IReadOnlyDictionary<string, byte[]> map)
            {
                _map = map;
            }

            public async Task<RemoteReadResponse> FetchAsync(
                Uri url, long maximumBytes, CancellationToken cancellation)
            {
                Interlocked.Increment(ref _calls);
                var current = Interlocked.Increment(ref _current);
                if (current > _peak)
                    Interlocked.Exchange(ref _peak, current);
                try
                {
                    await Task.Delay(15, cancellation);
                    byte[] bytes;
                    var name = url.AbsolutePath.Split('/');
                    if (!_map.TryGetValue(
                        name[name.Length - 2] + "/" + name[name.Length - 1],
                        out bytes))
                        return new RemoteReadResponse(404, new byte[0]);
                    return new RemoteReadResponse(200, bytes);
                }
                finally { Interlocked.Decrement(ref _current); }
            }
        }
    }
}
