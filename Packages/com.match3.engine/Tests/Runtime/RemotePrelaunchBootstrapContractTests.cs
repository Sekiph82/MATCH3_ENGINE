using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Match3.Engine.Runtime;
using NUnit.Framework;

namespace Match3.Engine.Tests
{
    public sealed class RemotePrelaunchBootstrapContractTests
    {
        private static readonly byte[] Bytes = Encoding.UTF8.GetBytes(
            "{\"neutral\":true}");

        private sealed class ScriptedReader : IRemoteReadTransport
        {
            public int Calls;
            public bool NetworkFails;
            public bool Timeout;
            public Task<RemoteReadResponse> FetchAsync(Uri uri, long max,
                CancellationToken token)
            {
                token.ThrowIfCancellationRequested();
                Calls++;
                if (Timeout) throw new TaskCanceledException("Synthetic HTTP timeout");
                if (NetworkFails) throw new HttpRequestException("Simulated offline");
                return Task.FromResult(new RemoteReadResponse(200, Bytes));
            }
        }

        private sealed class ScriptedAtomicHost : IRemoteActivationHost
        {
            public int Prepared;
            public int Committed;
            public bool Refuse;
            public Task PrepareVerifiedAsync(RemoteVerifiedRelease release,
                IReadOnlyDictionary<string, byte[]> data,
                CancellationToken token)
            {
                token.ThrowIfCancellationRequested();
                Prepared++;
                Assert.That(data.Count, Is.EqualTo(1));
                Assert.That(data["levels/starter.json"], Is.EqualTo(Bytes));
                return Task.CompletedTask;
            }

            public Task<bool> ExchangeActiveAsync(string partition,
                string expected, string replacement, long sequence,
                CancellationToken token)
            {
                token.ThrowIfCancellationRequested();
                Committed++;
                Assert.That(partition, Is.EqualTo("Coral_Rescue/0/1"));
                Assert.That(replacement.Length, Is.EqualTo(64));
                Assert.That(sequence, Is.EqualTo(1));
                return Task.FromResult(!Refuse);
            }
        }

        private static RemoteContentManifest Manifest() =>
            new RemoteContentManifest("Coral_Rescue",
                RemotePlatform.Android, RemoteChannel.FamilyTest,
                "signed-v1", "pub-v1", new EngineVersion(1, 0, 0),
                new EngineVersion(1, 1, 0), 1000, 9000,
                new[] {
                    new RemoteContentEntry("levels/starter.json",
                        RemoteContentKind.Json, Bytes.Length,
                        RemoteContentSafety.Sha256(Bytes))
                });

        private static RemotePublicSigningKey Key(
            RSACryptoServiceProvider signer)
        {
            var k = signer.ExportParameters(false);
            return new RemotePublicSigningKey("pub-v1",
                k.Modulus, k.Exponent, 0, 10000);
        }

        private static byte[] Sign(RSACryptoServiceProvider signer,
            RemoteContentManifest manifest) =>
            signer.SignData(manifest.CanonicalBytes(),
                HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        private static RemoteReleaseState Empty() =>
            new RemoteReleaseState("Coral_Rescue",
                RemotePlatform.Android, RemoteChannel.FamilyTest);

        private static Task<RemotePrelaunchResult> Run(
            RemoteContentManifest manifest, byte[] signature,
            RemotePublicSigningKey key, ScriptedReader transport,
            ScriptedAtomicHost host, RemoteReleaseState state,
            RemotePackagedStarter starter = null,
            Action<RemotePatchUiSnapshot> onProgress = null,
            CancellationToken cancellation = default(CancellationToken)) =>
            RemotePrelaunchBootstrap.ExecuteAsync(
                manifest, signature, new[] { key },
                new RemoteReadOrigin("https://neutral.example.com/public/"),
                transport, state, starter, host,
                new EngineVersion(1, 0, 0), 2000,
                cancellation, onProgress);

        [Test]
        public async Task M11_013_PrelaunchSignedNetworkSuccessIsNotReadyBeforeAtomicCommit()
        {
            using (var signer = new RSACryptoServiceProvider(2048))
            {
                var m = Manifest();
                var key = Key(signer);
                var sig = Sign(signer, m);
                var network = new ScriptedReader();
                var host = new ScriptedAtomicHost();
                var snapshots = new List<RemotePatchUiSnapshot>();
                var result = await Run(m, sig, key, network,
                    host, Empty(), onProgress: x => snapshots.Add(x));
                Assert.That(result.CanPlay, Is.True);
                Assert.That(result.NewReleaseActivated, Is.True);
                Assert.That(result.FellBackOffline, Is.False);
                Assert.That(network.Calls, Is.EqualTo(1));
                Assert.That(host.Prepared, Is.EqualTo(1));
                Assert.That(host.Committed, Is.EqualTo(1));
                Assert.That(snapshots.Count, Is.EqualTo(3));
                Assert.That(snapshots[0].Stage,
                    Is.EqualTo(RemotePatchStage.Available));
                Assert.That(snapshots[1].Stage,
                    Is.EqualTo(RemotePatchStage.Verified));
                Assert.That(snapshots[1].CanPlay, Is.False);
                Assert.That(snapshots[2].Stage,
                    Is.EqualTo(RemotePatchStage.Ready));
                Assert.That(snapshots[2].CanPlay, Is.True);
                Assert.That(snapshots[2].CanClaimReward, Is.False);
            }
        }

        [Test]
        public async Task M11_006_PrelaunchRemoteFailureUsesGenuinelySignedPackagedFallback()
        {
            using (var signer = new RSACryptoServiceProvider(2048))
            {
                var m = Manifest();
                var key = Key(signer);
                var sig = Sign(signer, m);
                var starter = new RemotePackagedStarter(
                    m.CanonicalBytes(), sig,
                    new Dictionary<string, byte[]> {
                        { "levels/starter.json", Bytes }
                    });
                var host = new ScriptedAtomicHost();
                var network = new ScriptedReader { NetworkFails = true };
                var result = await Run(m, sig, key, network, host,
                    Empty(), starter);
                Assert.That(result.CanPlay, Is.True);
                Assert.That(result.FellBackOffline, Is.True);
                Assert.That(result.NewReleaseActivated, Is.False);
                Assert.That(result.Flow.Snapshot().Stage,
                    Is.EqualTo(RemotePatchStage.Offline));
                Assert.That(host.Prepared, Is.Zero);
                Assert.That(host.Committed, Is.Zero);

                var timeout = await Run(m, sig, key,
                    new ScriptedReader { Timeout = true },
                    new ScriptedAtomicHost(), Empty(), starter);
                Assert.That(timeout.FellBackOffline, Is.True);
                Assert.That(timeout.CanPlay, Is.True);
            }
        }

        [Test]
        public async Task M11_007_AtomicPointerRefusalCannotPretendContentActivated()
        {
            using (var signer = new RSACryptoServiceProvider(2048))
            {
                var m = Manifest();
                var key = Key(signer);
                var host = new ScriptedAtomicHost { Refuse = true };
                var result = await Run(m, Sign(signer, m), key,
                    new ScriptedReader(), host, Empty());
                Assert.That(result.CanPlay, Is.False);
                Assert.That(result.PlayableContent, Is.Null);
                Assert.That(result.Flow.Snapshot().Stage,
                    Is.EqualTo(RemotePatchStage.Failed));
                Assert.That(result.Flow.Snapshot().CanClaimReward, Is.False);
                Assert.That(host.Prepared, Is.EqualTo(1));
                Assert.That(host.Committed, Is.EqualTo(1));
            }
        }

        [Test]
        public async Task M11_013_IdenticalInstalledSignatureProducesNoUpdateNotOfflineLabel()
        {
            using (var signer = new RSACryptoServiceProvider(2048))
            {
                var m = Manifest();
                var sig = Sign(signer, m);
                var key = Key(signer);
                var installed = RemoteContentStager.VerifyComplete(
                    m, sig, new[] { key }, m.GameId,
                    m.Platform, m.Channel, new EngineVersion(1, 0, 0),
                    2000, new Dictionary<string, byte[]> {
                        { "levels/starter.json", Bytes }
                    });
                var state = new RemoteReleaseState(m.GameId,
                    m.Platform, m.Channel, active: installed,
                    highestSequence: 1);
                var host = new ScriptedAtomicHost();
                var result = await Run(m, sig, key,
                    new ScriptedReader(), host, state);
                Assert.That(result.CanPlay, Is.True);
                Assert.That(result.NewReleaseActivated, Is.False);
                Assert.That(result.FellBackOffline, Is.False);
                Assert.That(result.Flow.Snapshot().Stage,
                    Is.EqualTo(RemotePatchStage.NoUpdate));
                Assert.That(host.Prepared, Is.Zero);
                Assert.That(host.Committed, Is.Zero);
            }
        }

        [Test]
        public void M11_003_BadSignedManifestRejectedBeforeExternalNetworkAndStorage()
        {
            using (var signer = new RSACryptoServiceProvider(2048))
            {
                var m = Manifest();
                var key = Key(signer);
                var sig = Sign(signer, m);
                sig[0] ^= 0x01;
                var reader = new ScriptedReader();
                var host = new ScriptedAtomicHost();
                Assert.ThrowsAsync<InvalidOperationException>(async () =>
                    await Run(m, sig, key, reader, host, Empty()));
                Assert.That(reader.Calls, Is.Zero);
                Assert.That(host.Prepared, Is.Zero);
                Assert.That(host.Committed, Is.Zero);
            }
        }
    }
}
