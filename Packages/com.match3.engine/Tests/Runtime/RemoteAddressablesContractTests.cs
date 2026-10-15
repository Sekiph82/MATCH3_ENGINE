using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Match3.Engine.Runtime;
using NUnit.Framework;

namespace Match3.Engine.Tests
{
    public sealed class RemoteAddressablesContractTests
    {
        [Test]
        public void M11_008_OnlySignedTitlePlatformCatalogAndBundlesCanBeBound()
        {
            var catalogBytes = Encoding.UTF8.GetBytes("{\"m_InternalIds\":[]}");
            var bundleBytes = Encoding.ASCII.GetBytes("UnityFSsigned-reference");
            using (var signer = new RSACryptoServiceProvider(2048))
            {
                var m = new RemoteContentManifest(
                    "Garage_Legends", RemotePlatform.Android,
                    RemoteChannel.FamilyTest, "signed-catalog",
                    "key", new EngineVersion(1, 0, 0),
                    new EngineVersion(1, 2, 0), 1000, 5000,
                    new[] {
                        new RemoteContentEntry("addressables/catalog.json",
                            RemoteContentKind.AddressablesCatalog,
                            catalogBytes.Length, RemoteContentSafety.Sha256(catalogBytes)),
                        new RemoteContentEntry("addressables/neutral.bundle",
                            RemoteContentKind.AddressablesBundle,
                            bundleBytes.Length, RemoteContentSafety.Sha256(bundleBytes))
                    });
                var k = signer.ExportParameters(false);
                var pinned = new RemotePublicSigningKey("key",
                    k.Modulus, k.Exponent, 0, 10000);
                var sig = signer.SignData(m.CanonicalBytes(),
                    HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                var verified = RemoteContentStager.VerifyComplete(
                    m, sig, new[] { pinned }, m.GameId, m.Platform,
                    m.Channel, new EngineVersion(1, 0, 0), 2000,
                    new Dictionary<string, byte[]> {
                        { "addressables/catalog.json", catalogBytes },
                        { "addressables/neutral.bundle", bundleBytes }
                    });
                var plan = RemoteAddressablesPlanner.Resolve(
                    verified, "addressables/catalog.json",
                    new[] { "addressables/neutral.bundle" },
                    m.GameId, m.Platform);
                Assert.That(plan.BundlePaths.Count, Is.EqualTo(1));
                Assert.That(plan.ReleaseId, Is.EqualTo("signed-catalog"));
                Assert.Throws<InvalidOperationException>(() =>
                    RemoteAddressablesPlanner.Resolve(
                        verified, "addressables/neutral.bundle",
                        new[] { "addressables/neutral.bundle" },
                        m.GameId, m.Platform));
                Assert.Throws<InvalidOperationException>(() =>
                    RemoteAddressablesPlanner.Resolve(
                        verified, "addressables/catalog.json",
                        new[] { "addressables/unknown.bundle" },
                        m.GameId, m.Platform));
                Assert.Throws<InvalidOperationException>(() =>
                    RemoteAddressablesPlanner.Resolve(
                        verified, "addressables/catalog.json",
                        new[] { "addressables/neutral.bundle" },
                        "Wonder_Pop", m.Platform));
                Assert.Throws<InvalidOperationException>(() =>
                    RemoteAddressablesPlanner.Resolve(
                        verified, "addressables/catalog.json",
                        new[] { "addressables/neutral.bundle" },
                        m.GameId, RemotePlatform.Ios));
            }
        }
    }
}
