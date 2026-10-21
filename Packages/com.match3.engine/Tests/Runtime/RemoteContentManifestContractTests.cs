using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Match3.Engine.Runtime;
using NUnit.Framework;

namespace Match3.Engine.Tests
{
    public sealed class RemoteContentManifestContractTests
    {
        private static readonly byte[] LevelBytes = Encoding.UTF8.GetBytes(
            "{\"level\":1}");

        private static RemoteContentManifest Manifest(string game = "Coral_Rescue",
            string key = "release-2026", RemotePlatform platform = RemotePlatform.Android,
            RemoteChannel channel = RemoteChannel.FamilyTest,
            string release = "v1")
        {
            return new RemoteContentManifest(game, platform, channel,
                release, key, new EngineVersion(1, 0, 0),
                new EngineVersion(1, 2, 0), 1000, 6000, new[] {
                    new RemoteContentEntry("levels/first.json",
                        RemoteContentKind.Json,
                        LevelBytes.Length,
                        RemoteContentSafety.Sha256(LevelBytes))
                });
        }

        private static byte[] Sign(RSA signer, RemoteContentManifest manifest)
        {
            return signer.SignData(manifest.CanonicalBytes(),
                HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        }

        private static RemotePublicSigningKey Trust(RSA signer, string keyId,
            bool revoked = false)
        {
            var key = signer.ExportParameters(false);
            return new RemotePublicSigningKey(keyId, key.Modulus, key.Exponent,
                0, 10000, revoked);
        }

        private static void Verify(RemoteContentManifest manifest,
            byte[] signature, params RemotePublicSigningKey[] publicKeys)
        {
            RemoteManifestVerifier.Verify(manifest, signature, publicKeys,
                manifest.GameId, manifest.Platform, manifest.Channel,
                new EngineVersion(1, 0, 0), 2000);
        }

        [Test]
        public void M11_001_ManifestV1IsCanonicalAndRejectsInvalidSchemaAndPaths()
        {
            var canonicalA = Manifest().CanonicalBytes();
            var canonicalB = Manifest().CanonicalBytes();
            Assert.That(canonicalA, Is.EqualTo(canonicalB));
            Assert.That(Manifest().Entries.Count, Is.EqualTo(1));
            Assert.That(Manifest().PartitionKey, Is.EqualTo("Coral_Rescue/0/1"));
            Assert.Throws<NotSupportedException>(() =>
                new RemoteContentManifest("Coral_Rescue",
                    RemotePlatform.Android, RemoteChannel.FamilyTest,
                    "v1", "key", new EngineVersion(1, 0, 0),
                    new EngineVersion(1, 0, 0), 100, 200,
                    Manifest().Entries, schema: 2));
            Assert.Throws<ArgumentException>(() =>
                new RemoteContentManifest("Coral_Rescue",
                    RemotePlatform.Android, RemoteChannel.FamilyTest,
                    "v1", "key", new EngineVersion(1, 0, 0),
                    new EngineVersion(1, 0, 0), 100, 200,
                    Manifest().Entries.Concat(Manifest().Entries)));
            foreach (var bad in new [] {
                "../escape.json", "/absolute.json", "C:/win.json",
                "data\\evil.json", "data//file.json",
                "foo/%2e%2e.json", "dir/./x.json",
                "dir/../x.json", "file.dll", "file.js"
            })
                Assert.That(RemoteContentSafety.IsSafePath(bad) &&
                    RemoteContentSafety.AllowedKindPath(
                        RemoteContentKind.Json, bad), Is.False,
                    "Unsafe manifest content path: " + bad);
            Assert.Throws<ArgumentException>(() =>
                new RemoteContentEntry("levels/sneaky.dll",
                    RemoteContentKind.Json, 1, new string('0', 64)));
            Assert.Throws<ArgumentException>(() =>
                new RemoteContentEntry("levels/test.json",
                    RemoteContentKind.Json, 1, "NOT_A_DIGEST"));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new RemoteContentEntry("levels/test.json",
                    RemoteContentKind.Json,
                    RemoteContentSafety.FileByteLimit + 1, new string('0', 64)));
        }

        [Test]
        public void M11_002_AuthenticManifestSignatureAndKeyRotationAreRequired()
        {
            using (var signer = new RSACryptoServiceProvider(2048))
            using (var outsider = new RSACryptoServiceProvider(2048))
            {
                var manifest = Manifest();
                var signature = Sign(signer, manifest);
                Verify(manifest, signature, Trust(signer, "release-2026"));
                Verify(manifest, signature,
                    Trust(outsider, "rotated-2027"),
                    Trust(signer, "release-2026"));
                Assert.Throws<InvalidOperationException>(() =>
                    Verify(Manifest(release: "v2"), signature,
                        Trust(signer, "release-2026")));
                Assert.Throws<InvalidOperationException>(() =>
                    Verify(manifest, signature, Trust(outsider, "release-2026")));
                Assert.Throws<InvalidOperationException>(() =>
                    Verify(manifest, signature, Trust(signer, "release-2026", true)));
                Assert.Throws<InvalidOperationException>(() =>
                    Verify(manifest, signature, Trust(signer, "rotated-2027")));
                Assert.Throws<InvalidOperationException>(() =>
                    Verify(manifest, new byte[2], Trust(signer, "release-2026")));
                Assert.That(Assert.Throws<InvalidOperationException>(() =>
                    Verify(manifest, new byte[255], Trust(signer, "release-2026")))
                    .Message, Is.EqualTo("Manifest RSA-SHA256 verification failed."));
                Assert.That(Assert.Throws<InvalidOperationException>(() =>
                    Verify(manifest, new byte[257], Trust(signer, "release-2026")))
                    .Message, Is.EqualTo("Manifest RSA-SHA256 verification failed."));
            }
        }

        [Test]
        public void M11_009_ExecutablePayloadsAndUnsafeTypeMasqueradesAreRejected()
        {
            foreach (var payload in new[] {
                new byte[] { 0x4d, 0x5a, 0x01, 0x00 },
                new byte[] { 0x7f, 0x45, 0x4c, 0x46 },
                new byte[] { 0x23, 0x21, 0x2f, 0x62 }
            })
                Assert.Throws<InvalidOperationException>(() =>
                    RemoteContentSafety.RequireNonExecutable(payload,
                        RemoteContentKind.Json));
            Assert.Throws<InvalidOperationException>(() =>
                RemoteContentSafety.RequireNonExecutable(
                    Encoding.UTF8.GetBytes("eval('x')"), RemoteContentKind.Json));
            Assert.DoesNotThrow(() =>
                RemoteContentSafety.RequireNonExecutable(LevelBytes,
                    RemoteContentKind.Json));
            Assert.That(RemoteContentSafety.AllowedKindPath(
                RemoteContentKind.AddressablesBundle, "prefabs/a.dll"),
                Is.False);
            Assert.That(RemoteContentSafety.AllowedKindPath(
                RemoteContentKind.AddressablesBundle, "prefabs/a.bundle"),
                Is.True);
        }

        [Test]
        public void M11_010_GamePlatformAndChannelIsolationFailClosed()
        {
            using (var signer = new RSACryptoServiceProvider(2048))
            {
                foreach (var game in GameProfileCatalog.Ids)
                {
                    var m = Manifest(game);
                    var key = Trust(signer, "release-2026");
                    var sig = Sign(signer, m);
                    Verify(m, sig, key);
                    Assert.Throws<InvalidOperationException>(() =>
                        RemoteManifestVerifier.Verify(m, sig, new[] { key },
                            "Wonder_Pop" == game ? "Coral_Rescue" : "Wonder_Pop",
                            m.Platform, m.Channel, new EngineVersion(1, 0, 0), 2000));
                    Assert.Throws<InvalidOperationException>(() =>
                        RemoteManifestVerifier.Verify(m, sig, new[] { key },
                            game, RemotePlatform.Windows, m.Channel,
                            new EngineVersion(1, 0, 0), 2000));
                    Assert.Throws<InvalidOperationException>(() =>
                        RemoteManifestVerifier.Verify(m, sig, new[] { key },
                            game, m.Platform, RemoteChannel.Release,
                            new EngineVersion(1, 0, 0), 2000));
                }
            }
        }

        [Test]
        public void M11_011_IncompatibleVersionExpiryAndRevokedKeyFail()
        {
            using (var signer = new RSACryptoServiceProvider(2048))
            {
                var m = Manifest();
                var sig = Sign(signer, m);
                var key = Trust(signer, "release-2026");
                foreach (var time in new long[] { 999, 6000, 7000 })
                    Assert.Throws<InvalidOperationException>(() =>
                        RemoteManifestVerifier.Verify(m, sig, new[] { key },
                            m.GameId, m.Platform, m.Channel,
                            new EngineVersion(1, 0, 0), time));
                Assert.Throws<InvalidOperationException>(() =>
                    RemoteManifestVerifier.Verify(m, sig, new[] { key },
                        m.GameId, m.Platform, m.Channel,
                        new EngineVersion(1, 3, 0), 2000));
                Assert.Throws<ArgumentException>(() =>
                    new RemotePublicSigningKey("bad", new byte[10],
                        new byte[3], 0, 100));
            }
        }
    }
}
