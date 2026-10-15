using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Match3.Engine.Runtime;
using NUnit.Framework;

namespace Match3.Engine.Tests
{
    public sealed class RemoteDurableActivationHostTests
    {
        // Test-owned, bounded sandbox only. Never uses owner project folders.
        private string _sandbox;

        [SetUp]
        public void SetUp()
        {
            _sandbox = Path.Combine(Path.GetTempPath(),
                "match3-signed-storage-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_sandbox);
        }

        [TearDown]
        public void TearDown()
        {
            if (_sandbox != null && Directory.Exists(_sandbox))
                Directory.Delete(_sandbox, recursive: true);
        }

        private sealed class MemoryRead : IRemoteReadTransport
        {
            private readonly byte[] _bytes;
            public MemoryRead(byte[] bytes) { _bytes = bytes; }
            public Task<RemoteReadResponse> FetchAsync(
                Uri url, long maximumBytes, CancellationToken cancellation)
            {
                cancellation.ThrowIfCancellationRequested();
                if (_bytes.LongLength > maximumBytes)
                    throw new InvalidOperationException("Too many source bytes.");
                return Task.FromResult(new RemoteReadResponse(200, _bytes));
            }
        }

        private static async Task<RemoteFetchedPackage> Package(
            RSACryptoServiceProvider signer, RemotePublicSigningKey key,
            long sequence)
        {
            var body = Encoding.UTF8.GetBytes(
                "{\"level\":" + sequence + "}");
            var manifest = new RemoteContentManifest(
                "Coral_Rescue", RemotePlatform.Android,
                RemoteChannel.FamilyTest, "test-release-" + sequence, "key-1",
                new EngineVersion(1, 0, 0), new EngineVersion(1, 2, 0),
                1000, 8000, new[] {
                    new RemoteContentEntry("levels/sample.json",
                        RemoteContentKind.Json, body.Length,
                        RemoteContentSafety.Sha256(body))
                }, sequence: sequence);
            var sig = signer.SignData(manifest.CanonicalBytes(),
                HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            return await RemoteDownloadCoordinator.FetchStageAsync(
                manifest, sig, new[] { key },
                new RemoteReadOrigin("https://cdn.example.com/public/"),
                new MemoryRead(body), manifest.GameId, manifest.Platform,
                manifest.Channel, new EngineVersion(1, 0, 0), 2000,
                CancellationToken.None);
        }

        private static RemotePublicSigningKey Trust(RSACryptoServiceProvider rsa)
        {
            var pub = rsa.ExportParameters(false);
            return new RemotePublicSigningKey("key-1", pub.Modulus,
                pub.Exponent, 0, 10000);
        }

        private RemoteDurableActivationHost Host() =>
            new RemoteDurableActivationHost(_sandbox, "Coral_Rescue",
                RemotePlatform.Android, RemoteChannel.FamilyTest);

        [Test]
        public async Task M11_005_DurableFilesAreRevalidatedAfterFreshHostRestart()
        {
            using (var signer = new RSACryptoServiceProvider(2048))
            {
                var trust = Trust(signer);
                var pack = await Package(signer, trust, 1);
                var empty = new RemoteReleaseState("Coral_Rescue",
                    RemotePlatform.Android, RemoteChannel.FamilyTest);
                var host = Host();
                Assert.That(host.Restore(new[] { trust },
                    new EngineVersion(1, 0, 0), 2000).Active, Is.Null);
                var committed = await RemoteActivationCoordinator.ApplyAsync(
                    empty, pack, host, CancellationToken.None);
                Assert.That(committed.Activated, Is.True);
                var restarted = Host().Restore(new[] { trust },
                    new EngineVersion(1, 0, 0), 2000);
                Assert.That(restarted.Active, Is.Not.Null);
                Assert.That(restarted.Active.SignedEnvelopeDigest,
                    Is.EqualTo(pack.Verified.SignedEnvelopeDigest));
                Assert.That(restarted.HighestSequence, Is.EqualTo(1));
                Assert.That(restarted.PreviousValid, Is.Null);
                Assert.That(Directory.GetFiles(_sandbox, "active.pointer",
                    SearchOption.AllDirectories).Length, Is.EqualTo(1));
            }
        }

        [Test]
        public async Task M11_007_DurableCrashFallbackPreservesAntiRollbackSequence()
        {
            using (var signer = new RSACryptoServiceProvider(2048))
            {
                var trust = Trust(signer);
                var start = new RemoteReleaseState("Coral_Rescue",
                    RemotePlatform.Android, RemoteChannel.FamilyTest);
                var host = Host();
                var pack1 = await Package(signer, trust, 1);
                var first = await RemoteActivationCoordinator.ApplyAsync(
                    start, pack1, host, CancellationToken.None);
                var pack2 = await Package(signer, trust, 2);
                var second = await RemoteActivationCoordinator.ApplyAsync(
                    first.State, pack2, host, CancellationToken.None);
                Assert.That(second.State.HighestSequence, Is.EqualTo(2));
                Assert.That(Host().Restore(new[] { trust },
                    new EngineVersion(1, 0, 0), 2000)
                    .Active.Manifest.ReleaseId, Is.EqualTo("test-release-2"));
                var secondBody = Directory.GetFiles(_sandbox,
                    "sample.json", SearchOption.AllDirectories)
                    .Single(x => x.Contains("release-2-"));
                // Corrupt only this test-owned, signed fixture.
                File.WriteAllBytes(secondBody,
                    Encoding.UTF8.GetBytes("{\"level\":999}"));
                var rolledBack = Host().Restore(new[] { trust },
                    new EngineVersion(1, 0, 0), 2000);
                Assert.That(rolledBack.Active.Manifest.ReleaseId,
                    Is.EqualTo("test-release-1"));
                Assert.That(rolledBack.HighestSequence, Is.EqualTo(2));
                var restarted = Host().Restore(new[] { trust },
                    new EngineVersion(1, 0, 0), 2000);
                Assert.That(restarted.Active.Manifest.Sequence, Is.EqualTo(1));
                Assert.That(restarted.HighestSequence, Is.EqualTo(2));
                Assert.Throws<InvalidOperationException>(() =>
                    RemoteReleasePlanner.Promote(restarted, pack2.Verified));
                // Within this isolated test sandbox, a missing active
                // pointer with a retained prior pointer must NOT reset
                // the persisted anti-rollback high-water to zero.
                var activePointer = Directory.GetFiles(_sandbox,
                    "active.pointer", SearchOption.AllDirectories).Single();
                File.Delete(activePointer);
                Assert.Throws<InvalidOperationException>(() =>
                    Host().Restore(new[] { trust },
                        new EngineVersion(1, 0, 0), 2000));
            }
        }

        [Test]
        public async Task M11_011_InvalidStageDoesNotTouchDiskAndOrphanQuotaFailsClosed()
        {
            using (var signer = new RSACryptoServiceProvider(2048))
            {
                var trust = Trust(signer);
                var pack = await Package(signer, trust, 1);
                var host = Host();
                var wrong = new Dictionary<string, byte[]> {
                    { "levels/sample.json", Encoding.UTF8.GetBytes("{\"level\":9}") }
                };
                Assert.Throws<InvalidOperationException>(() =>
                    host.PrepareVerifiedAsync(pack.Verified, wrong,
                        CancellationToken.None).GetAwaiter().GetResult());
                var partitionDir = Path.Combine(_sandbox,
                    "Coral_Rescue", "0", "1");
                Assert.That(Directory.GetDirectories(partitionDir,
                    "stage-*").Length, Is.Zero,
                    "Invalid input must not write a single orphan stage.");
                for (var i = 0; i < 4; i++)
                    Directory.CreateDirectory(Path.Combine(partitionDir,
                        "stage-owner-review-" + i));
                var valid = new Dictionary<string, byte[]> {
                    { "levels/sample.json", pack.GetVerifiedBytes("levels/sample.json") }
                };
                Assert.Throws<InvalidOperationException>(() =>
                    host.PrepareVerifiedAsync(pack.Verified, valid,
                        CancellationToken.None).GetAwaiter().GetResult());
                Assert.That(Directory.GetDirectories(partitionDir,
                    "stage-*").Length, Is.EqualTo(4));
                Assert.That(Host().Restore(new[] { trust },
                    new EngineVersion(1, 0, 0), 2000).Active, Is.Null);
            }
        }

        [Test]
        public async Task M11_011_InterruptedStagingAndPointerTamperNeverActivateContent()
        {
            using (var signer = new RSACryptoServiceProvider(2048))
            {
                var trust = Trust(signer);
                var pack = await Package(signer, trust, 1);
                var host = Host();
                var data = new Dictionary<string, byte[]> {
                    { "levels/sample.json", pack.GetVerifiedBytes("levels/sample.json") }
                };
                await host.PrepareVerifiedAsync(pack.Verified, data,
                    CancellationToken.None);
                Assert.That(Host().Restore(new[] { trust },
                    new EngineVersion(1, 0, 0), 2000).Active, Is.Null);
                var empty = new RemoteReleaseState("Coral_Rescue",
                    RemotePlatform.Android, RemoteChannel.FamilyTest);
                var otherHost = Host();
                var published = await RemoteActivationCoordinator.ApplyAsync(
                    empty, pack, otherHost, CancellationToken.None);
                Assert.That(published.Activated, Is.True);
                var pointer = Directory.GetFiles(_sandbox,
                    "active.pointer", SearchOption.AllDirectories).Single();
                File.WriteAllText(pointer, "not-signed|999999|unsafe\n");
                Assert.Throws<InvalidOperationException>(() =>
                    Host().Restore(new[] { trust },
                        new EngineVersion(1, 0, 0), 2000));
            }
        }
    }
}
