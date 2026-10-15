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
    public sealed class RemotePatchFlowContractTests
    {
        private static readonly byte[] First = Encoding.UTF8.GetBytes("{\"one\":1}");
        private static readonly byte[] Second = Encoding.UTF8.GetBytes("{\"two\":2}");

        private sealed class TestTransport : IRemoteReadTransport
        {
            public Task<RemoteReadResponse> FetchAsync(Uri url, long maximumBytes,
                CancellationToken cancellation)
            {
                cancellation.ThrowIfCancellationRequested();
                var bytes = url.AbsolutePath.EndsWith("first.json",
                    StringComparison.Ordinal) ? First : Second;
                return Task.FromResult(new RemoteReadResponse(200, bytes));
            }
        }

        private sealed class VerifiedHost : IRemoteActivationHost
        {
            public Task PrepareVerifiedAsync(RemoteVerifiedRelease candidate,
                IReadOnlyDictionary<string, byte[]> bytes,
                CancellationToken cancellation)
            {
                Assert.That(bytes.Count, Is.EqualTo(2));
                return Task.CompletedTask;
            }

            public Task<bool> ExchangeActiveAsync(string partition,
                string expectedEnvelopeDigest, string replacementEnvelopeDigest,
                long highestSequence, CancellationToken cancellation)
            {
                Assert.That(partition, Is.EqualTo("Coral_Rescue/0/1"));
                Assert.That(expectedEnvelopeDigest, Is.EqualTo(""));
                Assert.That(replacementEnvelopeDigest.Length, Is.EqualTo(64));
                return Task.FromResult(true);
            }
        }

        private static RemoteContentManifest Manifest() =>
            new RemoteContentManifest("Coral_Rescue",
                RemotePlatform.Android, RemoteChannel.FamilyTest,
                "patch-v1", "patch-key",
                new EngineVersion(1, 0, 0),
                new EngineVersion(1, 1, 0), 1000, 9000,
                new[] {
                    new RemoteContentEntry("levels/first.json",
                        RemoteContentKind.Json, First.Length,
                        RemoteContentSafety.Sha256(First)),
                    new RemoteContentEntry("levels/second.json",
                        RemoteContentKind.Json, Second.Length,
                        RemoteContentSafety.Sha256(Second))
                });

        private static RemotePublicSigningKey Trust(
            RSACryptoServiceProvider signer, bool revoked = false)
        {
            var k = signer.ExportParameters(false);
            return new RemotePublicSigningKey("patch-key",
                k.Modulus, k.Exponent, 0, 10000, revoked);
        }

        private static byte[] Sign(RSACryptoServiceProvider signer,
            RemoteContentManifest manifest) =>
            signer.SignData(manifest.CanonicalBytes(),
                HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        private static RemotePatchFlow NewFlow(
            RemoteContentManifest m, byte[] signature,
            RemotePublicSigningKey key) =>
            new RemotePatchFlow(m, signature, new[] { key },
                m.GameId, m.Platform, m.Channel,
                new EngineVersion(1, 0, 0), 2000);

        private static async Task<RemoteDownloadReceipt> Receipt(
            RemoteContentManifest m, byte[] signature,
            RemotePublicSigningKey key, RemoteContentEntry entry) =>
            await RemoteDownloadCoordinator.FetchVerifiedAsync(
                m, signature, new[] { key },
                new RemoteReadOrigin("https://cdn.example.com/public/"),
                new TestTransport(), entry, m.GameId, m.Platform,
                m.Channel, new EngineVersion(1, 0, 0), 2000,
                CancellationToken.None);

        [Test]
        public async Task M11_013_VerifiedProgressAndCommitAreSeparatelyHonest()
        {
            using (var signer = new RSACryptoServiceProvider(2048))
            {
                var manifest = Manifest();
                var key = Trust(signer);
                var signature = Sign(signer, manifest);
                var session = NewFlow(manifest, signature, key);
                Assert.That(session.Snapshot().Stage,
                    Is.EqualTo(RemotePatchStage.Available));
                Assert.That(session.Snapshot().CanPlay, Is.False);
                session.RecordVerified(await Receipt(manifest, signature,
                    key, manifest.Entries[0]));
                Assert.That(session.Snapshot().Stage,
                    Is.EqualTo(RemotePatchStage.Downloading));
                Assert.That(session.Snapshot().VerifiedPercent,
                    Is.GreaterThan(0));
                Assert.That(session.Snapshot().VerifiedPercent,
                    Is.LessThan(100));
                session.RecordVerified(await Receipt(manifest, signature,
                    key, manifest.Entries[1]));
                Assert.That(session.Snapshot().Stage,
                    Is.EqualTo(RemotePatchStage.Verified));
                Assert.That(session.Snapshot().VerifiedPercent, Is.EqualTo(100));
                Assert.That(session.Snapshot().CanPlay, Is.False,
                    "100% verified bytes is NOT a committed release.");
                var package = await RemoteDownloadCoordinator.FetchStageAsync(
                    manifest, signature, new[] { key },
                    new RemoteReadOrigin("https://cdn.example.com/public/"),
                    new TestTransport(), manifest.GameId,
                    manifest.Platform, manifest.Channel,
                    new EngineVersion(1, 0, 0), 2000,
                    CancellationToken.None);
                var empty = new RemoteReleaseState(manifest.GameId,
                    manifest.Platform, manifest.Channel);
                var committed = await RemoteActivationCoordinator.ApplyAsync(
                    empty, package, new VerifiedHost(), CancellationToken.None);
                session.RecordActivation(committed);
                Assert.That(session.Snapshot().Stage,
                    Is.EqualTo(RemotePatchStage.Ready));
                Assert.That(session.Snapshot().CanPlay, Is.True);
                Assert.That(session.Snapshot().CanClaimReward, Is.False);
                Assert.That(session.Snapshot().CanPurchase, Is.False);
                Assert.Throws<InvalidOperationException>(() =>
                    session.RecordFailure());
            }
        }

        [Test]
        public async Task M11_013_DuplicateReceiptCannotFabricateHundredPercentOrReward()
        {
            using (var signer = new RSACryptoServiceProvider(2048))
            {
                var m = Manifest();
                var key = Trust(signer);
                var sig = Sign(signer, m);
                var flow = NewFlow(m, sig, key);
                var first = await Receipt(m, sig, key, m.Entries[0]);
                flow.RecordVerified(first);
                var earlier = flow.Snapshot().VerifiedPercent;
                Assert.Throws<InvalidOperationException>(() =>
                    flow.RecordVerified(first));
                Assert.That(flow.Snapshot().VerifiedPercent, Is.EqualTo(earlier));
                flow.RecordFailure();
                Assert.That(flow.Snapshot().CanRetry, Is.True);
                Assert.That(flow.Snapshot().CanPlay, Is.False);
                Assert.That(flow.Snapshot().LabelKey, Is.EqualTo("patch.failed"));
                flow.Retry();
                Assert.That(flow.Snapshot().VerifiedPercent, Is.Zero);
                Assert.That(flow.Snapshot().CanClaimReward, Is.False);
                Assert.Throws<InvalidOperationException>(() =>
                    flow.Retry());
                var other = Trust(signer, revoked: true);
                Assert.Throws<InvalidOperationException>(() =>
                    NewFlow(m, sig, other));
            }
        }

        [Test]
        public async Task M11_013_OfflineFallbackRequiresFreshValidSignedContent()
        {
            using (var signer = new RSACryptoServiceProvider(2048))
            {
                var m = Manifest();
                var key = Trust(signer);
                var sig = Sign(signer, m);
                var signed = RemoteContentStager.VerifyComplete(
                    m, sig, new[] { key },
                    m.GameId, m.Platform, m.Channel,
                    new EngineVersion(1, 0, 0), 2000,
                    new Dictionary<string, byte[]> {
                        { "levels/first.json", First },
                        { "levels/second.json", Second }
                    });
                var flow = NewFlow(m, sig, key);
                Assert.Throws<InvalidOperationException>(() =>
                    flow.RecordOfflineFallback(signed,
                        new[] { Trust(signer, true) },
                        new EngineVersion(1, 0, 0), 2000));
                Assert.That(flow.Snapshot().CanPlay, Is.False);
                flow.RecordOfflineFallback(signed, new[] { key },
                    new EngineVersion(1, 0, 0), 2000);
                Assert.That(flow.Snapshot().Stage,
                    Is.EqualTo(RemotePatchStage.Offline));
                Assert.That(flow.Snapshot().CanPlay, Is.True);
                Assert.That(flow.Snapshot().CanClaimReward, Is.False);
                var rejectedAfterOffline = await Receipt(m, sig,
                    key, m.Entries[0]);
                Assert.Throws<InvalidOperationException>(() =>
                    flow.RecordVerified(rejectedAfterOffline));
            }
        }
    }
}
