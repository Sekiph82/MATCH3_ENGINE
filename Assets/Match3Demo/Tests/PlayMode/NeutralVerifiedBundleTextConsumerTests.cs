using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Match3.Engine.Runtime;
using NUnit.Framework;

namespace Match3.Engine.Demo.Tests
{
    public sealed class NeutralVerifiedBundleTextConsumerTests
    {
        private static readonly byte[] Catalog =
            Encoding.UTF8.GetBytes("{\"m_InternalIds\":[]}");
        private static readonly byte[] Bundle =
            Encoding.ASCII.GetBytes("UnityFSsigned-neutral-placeholder");

        private sealed class FixedRead : IRemoteReadTransport
        {
            public Task<RemoteReadResponse> FetchAsync(
                Uri uri, long maximumBytes, CancellationToken token)
            {
                token.ThrowIfCancellationRequested();
                return Task.FromResult(new RemoteReadResponse(200,
                    uri.AbsolutePath.EndsWith(".bundle",
                        StringComparison.Ordinal) ? Bundle : Catalog));
            }
        }

        [Test]
        public async Task M11_008_OnlySignedPlatformAndAssetPathsCanReachNativeBundleReader()
        {
            using (var signer = new RSACryptoServiceProvider(2048))
            {
                var p = signer.ExportParameters(false);
                var key = new RemotePublicSigningKey("qa-pub",
                    p.Modulus, p.Exponent, 0, 9000);
                var manifest = new RemoteContentManifest(
                    "Garage_Legends", RemotePlatform.Windows,
                    RemoteChannel.FamilyTest, "neutral-bundle",
                    "qa-pub", new EngineVersion(1, 0, 0),
                    new EngineVersion(1, 1, 0), 1000, 8000,
                    new[] {
                        new RemoteContentEntry("addressables/catalog.json",
                            RemoteContentKind.AddressablesCatalog,
                            Catalog.Length, RemoteContentSafety.Sha256(Catalog)),
                        new RemoteContentEntry("addressables/qa.bundle",
                            RemoteContentKind.AddressablesBundle,
                            Bundle.Length, RemoteContentSafety.Sha256(Bundle))
                    });
                var signature = signer.SignData(manifest.CanonicalBytes(),
                    HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                var verified = await RemoteDownloadCoordinator.FetchStageAsync(
                    manifest, signature, new[] { key },
                    new RemoteReadOrigin("https://neutral.example.test/cdn/"),
                    new FixedRead(), manifest.GameId, manifest.Platform,
                    manifest.Channel, new EngineVersion(1, 0, 0),
                    2000, CancellationToken.None);
                var payload = RemoteVerifiedAddressablesPayload.Prepare(
                    verified, "addressables/catalog.json",
                    new[] { "addressables/qa.bundle" },
                    manifest.GameId, RemotePlatform.Windows);

                Assert.ThrowsAsync<InvalidOperationException>(async () =>
                    await NeutralVerifiedBundleTextConsumer.ReadSignedTextAssetAsync(
                        payload, "Coral_Rescue", "addressables/qa.bundle",
                        "qa/neutral.txt", CancellationToken.None));
                Assert.ThrowsAsync<InvalidOperationException>(async () =>
                    await NeutralVerifiedBundleTextConsumer.ReadSignedTextAssetAsync(
                        payload, manifest.GameId, "addressables/unknown.bundle",
                        "qa/neutral.txt", CancellationToken.None));
                Assert.ThrowsAsync<ArgumentException>(async () =>
                    await NeutralVerifiedBundleTextConsumer.ReadSignedTextAssetAsync(
                        payload, manifest.GameId, "addressables/qa.bundle",
                        "../unsafe", CancellationToken.None));

                using (var canceled = new CancellationTokenSource())
                {
                    canceled.Cancel();
                    Assert.ThrowsAsync<TaskCanceledException>(async () =>
                        await NeutralVerifiedBundleTextConsumer.ReadSignedTextAssetAsync(
                            payload, manifest.GameId, "addressables/qa.bundle",
                            "qa/neutral.txt", canceled.Token));
                }
                // The intentionally NON-bundle bytes are never passed to the
                // native loader in this negative contract test.
                // Positive native/platform bundle loading still needs a
                // genuine Android/iOS/Windows built bundle artifact and audit.
            }
        }
    }
}
