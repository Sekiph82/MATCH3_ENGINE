using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Match3.Engine.Runtime;
using NUnit.Framework;

namespace Match3.Engine.Demo.OfficialAddressables.Tests
{
    // Source-only until the optional official com.unity.addressables
    // package is independently present in the local Unity project.
    // A fake JSON catalog is intentionally NEVER passed to native LoadAsset:
    // these are signed-receipt/firewall negative tests, not an E2E PASS.
    public sealed class NeutralOfficialAddressablesEditModeTests
    {
        private sealed class SignedRead : IRemoteReadTransport
        {
            private readonly byte[] _catalog;
            private readonly byte[] _bundle;
            public SignedRead(byte[] catalog, byte[] bundle)
            { _catalog = catalog; _bundle = bundle; }
            public Task<RemoteReadResponse> FetchAsync(
                Uri uri, long maximumBytes, CancellationToken token)
            {
                token.ThrowIfCancellationRequested();
                var data = uri.AbsolutePath.EndsWith(".bundle",
                    StringComparison.Ordinal) ? _bundle : _catalog;
                if (data.LongLength > maximumBytes)
                    throw new InvalidOperationException("Signed fixture limit.");
                return Task.FromResult(new RemoteReadResponse(200,
                    (byte[])data.Clone()));
            }
        }

        private static async Task<RemoteVerifiedAddressablesPayload>
            SignedReceipt(RemotePlatform platform)
        {
            var catalog = Encoding.UTF8.GetBytes("{\"m_InternalIds\":[]}");
            var bundle = Encoding.ASCII.GetBytes("signed-fixture-not-native");
            using (var rsa = new RSACryptoServiceProvider(2048))
            {
                var pub = rsa.ExportParameters(false);
                var key = new RemotePublicSigningKey("official-test-public",
                    pub.Modulus, pub.Exponent, 0, 10000);
                var manifest = new RemoteContentManifest(
                    "Coral_Rescue", platform, RemoteChannel.Development,
                    "official-negative-test", "official-test-public",
                    new EngineVersion(1, 0, 0),
                    new EngineVersion(1, 1, 0), 1000, 9000,
                    new[] {
                        new RemoteContentEntry("addressables/catalog.json",
                            RemoteContentKind.AddressablesCatalog,
                            catalog.Length, RemoteContentSafety.Sha256(catalog)),
                        new RemoteContentEntry("addressables/neutral.bundle",
                            RemoteContentKind.AddressablesBundle,
                            bundle.Length, RemoteContentSafety.Sha256(bundle))
                    });
                var signature = rsa.SignData(manifest.CanonicalBytes(),
                    HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                var staged = await RemoteDownloadCoordinator.FetchStageAsync(
                    manifest, signature, new[] { key },
                    new RemoteReadOrigin("https://signed.invalid/neutral/"),
                    new SignedRead(catalog, bundle),
                    manifest.GameId, manifest.Platform, manifest.Channel,
                    new EngineVersion(1, 0, 0), 2000,
                    CancellationToken.None);
                return RemoteVerifiedAddressablesPayload.Prepare(
                    staged, "addressables/catalog.json",
                    new[] { "addressables/neutral.bundle" },
                    manifest.GameId, platform);
            }
        }

        [Test]
        public async Task M11_008_OfficialNativeAdapterRejectsWrongTitleBeforeAnyDiskOrCatalogLoad()
        {
            var signed = await SignedReceipt(RemotePlatform.Windows);
            Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await NeutralOfficialAddressablesConsumer.ReadSignedTextAssetAsync(
                    signed, "Garage_Legends", "neutral/text",
                    CancellationToken.None));
        }

        [Test]
        public async Task M11_008_OfficialNativeAdapterRejectsUnsafeAddressBeforeCatalogLoad()
        {
            var signed = await SignedReceipt(RemotePlatform.Windows);
            Assert.ThrowsAsync<ArgumentException>(async () =>
                await NeutralOfficialAddressablesConsumer.ReadSignedTextAssetAsync(
                    signed, "Coral_Rescue", "../outside",
                    CancellationToken.None));
        }

        [Test]
        public async Task M11_008_OfficialNativeAdapterRejectsOtherPlayerPlatform()
        {
            var signed = await SignedReceipt(RemotePlatform.Android);
            Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await NeutralOfficialAddressablesConsumer.ReadSignedTextAssetAsync(
                    signed, "Coral_Rescue", "neutral/text",
                    CancellationToken.None));
        }

        [Test]
        public async Task M11_008_OfficialNativeAdapterRejectsCancelledRequest()
        {
            var signed = await SignedReceipt(RemotePlatform.Windows);
            using (var cancellation = new CancellationTokenSource())
            {
                cancellation.Cancel();
                Assert.ThrowsAsync<OperationCanceledException>(async () =>
                    await NeutralOfficialAddressablesConsumer.ReadSignedTextAssetAsync(
                        signed, "Coral_Rescue", "neutral/text",
                        cancellation.Token));
            }
        }
    }
}
