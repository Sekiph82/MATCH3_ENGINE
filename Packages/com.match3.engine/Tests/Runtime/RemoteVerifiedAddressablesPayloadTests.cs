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
    public sealed class RemoteVerifiedAddressablesPayloadTests
    {
        private static readonly byte[] Catalog =
            Encoding.UTF8.GetBytes("{\"m_InternalIds\":[]}");
        private static readonly byte[] Bundle =
            Encoding.ASCII.GetBytes("UnityFSsigned-neutral-fixture");

        private sealed class FixtureTransport : IRemoteReadTransport
        {
            public bool Corrupt { get; set; }
            public int Count { get; private set; }

            public Task<RemoteReadResponse> FetchAsync(
                Uri url, long maxBytes, CancellationToken cancellation)
            {
                cancellation.ThrowIfCancellationRequested();
                Count++;
                var bytes = url.AbsolutePath.EndsWith("catalog.json",
                    StringComparison.Ordinal) ? Catalog : Bundle;
                if (Corrupt && url.AbsolutePath.EndsWith(".bundle",
                    StringComparison.Ordinal))
                    bytes = Encoding.ASCII.GetBytes("BROKENsigned-neutral-fixture");
                return Task.FromResult(new RemoteReadResponse(200, bytes));
            }
        }

        private static RemoteContentManifest Manifest() =>
            new RemoteContentManifest(
                "Garage_Legends", RemotePlatform.Android,
                RemoteChannel.FamilyTest, "signed-a1", "neutral-addr",
                new EngineVersion(1, 0, 0),
                new EngineVersion(1, 2, 0), 1000, 9000,
                new[] {
                    new RemoteContentEntry("addressables/catalog.json",
                        RemoteContentKind.AddressablesCatalog,
                        Catalog.Length, RemoteContentSafety.Sha256(Catalog)),
                    new RemoteContentEntry("addressables/neutral.bundle",
                        RemoteContentKind.AddressablesBundle,
                        Bundle.Length, RemoteContentSafety.Sha256(Bundle))
                }, sequence: 1);

        private static RemotePublicSigningKey Key(RSACryptoServiceProvider signer)
        {
            var p = signer.ExportParameters(false);
            return new RemotePublicSigningKey("neutral-addr",
                p.Modulus, p.Exponent, 0, 10000);
        }

        private static async Task<RemoteFetchedPackage> Download(
            RemoteContentManifest manifest, RSACryptoServiceProvider signer,
            FixtureTransport transport)
        {
            var sig = signer.SignData(manifest.CanonicalBytes(),
                HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            return await RemoteDownloadCoordinator.FetchStageAsync(
                manifest, sig, new[] { Key(signer) },
                new RemoteReadOrigin("https://cdn.example.com/verified/"),
                transport, manifest.GameId, manifest.Platform,
                manifest.Channel, new EngineVersion(1, 0, 0), 2000,
                CancellationToken.None);
        }

        [Test]
        public async Task M11_008_SignedCatalogAndBundleDeliveredAsImmutableBytes()
        {
            using (var signer = new RSACryptoServiceProvider(2048))
            {
                var m = Manifest();
                var transport = new FixtureTransport();
                var verified = await Download(m, signer, transport);
                var ready = RemoteVerifiedAddressablesPayload.Prepare(
                    verified, "addressables/catalog.json",
                    new[] { "addressables/neutral.bundle" },
                    "Garage_Legends", RemotePlatform.Android);
                Assert.That(transport.Count, Is.EqualTo(2));
                Assert.That(ready.GameId, Is.EqualTo("Garage_Legends"));
                Assert.That(ready.Platform, Is.EqualTo(RemotePlatform.Android));
                Assert.That(ready.BundlePaths.Count, Is.EqualTo(1));
                Assert.That(ready.ReleaseDigest.Length, Is.EqualTo(64));
                Assert.That(ready.ReadVerifiedBytes(ready.CatalogPath),
                    Is.EqualTo(Catalog));
                var localCopy = ready.ReadVerifiedBytes(
                    "addressables/neutral.bundle");
                localCopy[0] ^= 0x01;
                Assert.That(ready.ReadVerifiedBytes(
                    "addressables/neutral.bundle"), Is.EqualTo(Bundle));
                Assert.Throws<InvalidOperationException>(() =>
                    ready.ReadVerifiedBytes("addressables/fake.dll"));
            }
        }

        [Test]
        public async Task M11_008_CrossTitleUnknownBundleAndChangedDownloadFailClosed()
        {
            using (var signer = new RSACryptoServiceProvider(2048))
            {
                var m = Manifest();
                var verified = await Download(m, signer,
                    new FixtureTransport());
                Assert.Throws<InvalidOperationException>(() =>
                    RemoteVerifiedAddressablesPayload.Prepare(
                        verified, "addressables/catalog.json",
                        new[] { "addressables/neutral.bundle" },
                        "Coral_Rescue", RemotePlatform.Android));
                Assert.Throws<InvalidOperationException>(() =>
                    RemoteVerifiedAddressablesPayload.Prepare(
                        verified, "addressables/catalog.json",
                        new[] { "addressables/unknown.bundle" },
                        "Garage_Legends", RemotePlatform.Android));
                Assert.Throws<ArgumentException>(() =>
                    RemoteVerifiedAddressablesPayload.Prepare(
                        verified, "addressables/catalog.json",
                        new string[0], "Garage_Legends",
                        RemotePlatform.Android));
                Assert.Throws<InvalidOperationException>(() =>
                    RemoteVerifiedAddressablesPayload.Prepare(
                        verified, "addressables/catalog.json",
                        new[] { "addressables/neutral.bundle" },
                        "Garage_Legends", RemotePlatform.Ios));
                Assert.ThrowsAsync<InvalidOperationException>(async () =>
                    await Download(m, signer,
                        new FixtureTransport { Corrupt = true }));
            }
        }
    }
}
