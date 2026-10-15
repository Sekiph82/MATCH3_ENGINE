using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Match3.Engine.Runtime;
using NUnit.Framework;

namespace Match3.Engine.Tests
{
    public sealed class RemoteActivationHostContractTests
    {
        private static readonly byte[] Content =
            Encoding.UTF8.GetBytes("{\"level\":2}");

        private sealed class MemoryRead : IRemoteReadTransport
        {
            public Task<RemoteReadResponse> FetchAsync(
                Uri url, long limit, CancellationToken cancellation)
            {
                cancellation.ThrowIfCancellationRequested();
                return Task.FromResult(new RemoteReadResponse(200, Content));
            }
        }

        private sealed class MemoryAtomicHost : IRemoteActivationHost
        {
            public bool FailPrepare { get; set; }
            public bool RefuseExchange { get; set; }
            public bool Prepared { get; private set; }
            public int PointerCommits { get; private set; }
            public string PointerDigest { get; private set; } = "";

            public Task PrepareVerifiedAsync(RemoteVerifiedRelease candidate,
                IReadOnlyDictionary<string, byte[]> data,
                CancellationToken cancellation)
            {
                cancellation.ThrowIfCancellationRequested();
                if (FailPrepare)
                    throw new InvalidOperationException("Simulated staging interruption.");
                Assert.That(data.Count, Is.EqualTo(1));
                Assert.That(data["levels/second.json"], Is.EqualTo(Content));
                Prepared = true;
                return Task.CompletedTask;
            }

            public Task<bool> ExchangeActiveAsync(string partition,
                string expectedEnvelopeDigest, string replacementEnvelopeDigest,
                long highestSequence, CancellationToken cancellation)
            {
                cancellation.ThrowIfCancellationRequested();
                if (!Prepared) throw new InvalidOperationException("No fully staged bytes.");
                if (RefuseExchange ||
                    !StringComparer.Ordinal.Equals(
                        expectedEnvelopeDigest, PointerDigest))
                    return Task.FromResult(false);
                Assert.That(partition, Is.EqualTo("Coral_Rescue/0/1"));
                Assert.That(highestSequence, Is.EqualTo(2));
                PointerDigest = replacementEnvelopeDigest;
                PointerCommits++;
                return Task.FromResult(true);
            }
        }

        private static async Task<RemoteFetchedPackage> Download()
        {
            using (var signer = new RSACryptoServiceProvider(2048))
            {
                var manifest = new RemoteContentManifest(
                    "Coral_Rescue", RemotePlatform.Android,
                    RemoteChannel.FamilyTest, "v2", "key",
                    new EngineVersion(1, 0, 0),
                    new EngineVersion(1, 1, 0), 1000, 4000,
                    new[] { new RemoteContentEntry(
                        "levels/second.json", RemoteContentKind.Json,
                        Content.Length, RemoteContentSafety.Sha256(Content))
                    }, sequence: 2);
                var k = signer.ExportParameters(false);
                var publicKey = new RemotePublicSigningKey(
                    "key", k.Modulus, k.Exponent, 0, 5000);
                var signature = signer.SignData(
                    manifest.CanonicalBytes(),
                    HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                return await RemoteDownloadCoordinator.FetchStageAsync(
                    manifest, signature, new[] { publicKey },
                    new RemoteReadOrigin("https://cdn.example.com/starter/"),
                    new MemoryRead(), manifest.GameId, manifest.Platform,
                    manifest.Channel, new EngineVersion(1, 0, 0), 2000,
                    CancellationToken.None);
            }
        }

        [Test]
        public async Task M11_005_PlannerPublishesOnlyAfterVerifiedHostAcknowledges()
        {
            var package = await Download();
            var state = new RemoteReleaseState(
                "Coral_Rescue", RemotePlatform.Android,
                RemoteChannel.FamilyTest);
            var host = new MemoryAtomicHost();
            var result = await RemoteActivationCoordinator.ApplyAsync(
                state, package, host, CancellationToken.None);
            Assert.That(result.Activated, Is.True);
            Assert.That(result.State.HighestSequence, Is.EqualTo(2));
            Assert.That(result.State.Active, Is.SameAs(package.Verified));
            Assert.That(host.PointerCommits, Is.EqualTo(1));
            Assert.That(host.PointerDigest,
                Is.EqualTo(package.Verified.SignedEnvelopeDigest));
        }

        [Test]
        public async Task M11_007_PrepareFailurePointerConflictAndCancellationFailClosed()
        {
            var package = await Download();
            var state = new RemoteReleaseState(
                "Coral_Rescue", RemotePlatform.Android,
                RemoteChannel.FamilyTest);
            var prepare = new MemoryAtomicHost { FailPrepare = true };
            Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await RemoteActivationCoordinator.ApplyAsync(
                    state, package, prepare, CancellationToken.None));
            Assert.That(prepare.PointerCommits, Is.Zero);

            var refused = new MemoryAtomicHost { RefuseExchange = true };
            Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await RemoteActivationCoordinator.ApplyAsync(
                    state, package, refused, CancellationToken.None));
            Assert.That(refused.PointerCommits, Is.Zero);
            Assert.That(refused.PointerDigest, Is.EqualTo(""));

            using (var cts = new CancellationTokenSource())
            {
                cts.Cancel();
                var cancelled = new MemoryAtomicHost();
                Assert.CatchAsync<OperationCanceledException>(async () =>
                    await RemoteActivationCoordinator.ApplyAsync(
                        state, package, cancelled, cts.Token));
                Assert.That(cancelled.PointerCommits, Is.Zero);
            }
        }
    }
}
