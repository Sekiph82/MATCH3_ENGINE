using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using Match3.Engine.Runtime;
using NUnit.Framework;

namespace Match3.Engine.Tests
{
    public sealed class RemotePackagedStarterContractTests
    {
        private static readonly byte[] Content =
            Encoding.UTF8.GetBytes("{\"starter\":true}");

        private static RemoteContentManifest Manifest(
            string gameId, string keyId, long sequence = 1)
        {
            return new RemoteContentManifest(gameId,
                RemotePlatform.Android, RemoteChannel.FamilyTest,
                "bundled-" + sequence, keyId,
                new EngineVersion(1, 0, 0),
                new EngineVersion(1, 2, 0), 1000, 9000,
                new[] { new RemoteContentEntry("levels/starter.json",
                    RemoteContentKind.Json, Content.Length,
                    RemoteContentSafety.Sha256(Content)) },
                sequence: sequence);
        }

        private static RemotePublicSigningKey Trust(
            RSACryptoServiceProvider signer, string id, bool revoked = false)
        {
            var key = signer.ExportParameters(false);
            return new RemotePublicSigningKey(id, key.Modulus,
                key.Exponent, 0, 10000, revoked);
        }

        private static byte[] Sign(
            RSACryptoServiceProvider signer, RemoteContentManifest manifest) =>
            signer.SignData(manifest.CanonicalBytes(),
                HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        private static RemotePackagedStarter Starter(
            RSACryptoServiceProvider signer, RemoteContentManifest manifest,
            byte[] payload = null)
        {
            return new RemotePackagedStarter(manifest.CanonicalBytes(),
                Sign(signer, manifest), new Dictionary<string, byte[]> {
                    { "levels/starter.json", payload ?? Content }
                });
        }

        [Test]
        public void M11_006_SignedPackagedStarterLoadsForFiveNeutralProfiles()
        {
            using (var signer = new RSACryptoServiceProvider(2048))
            {
                foreach (var game in GameProfileCatalog.Ids)
                {
                    var manifest = Manifest(game, "starter-pub");
                    var starter = Starter(signer, manifest);
                    var empty = new RemoteReleaseState(game,
                        RemotePlatform.Android, RemoteChannel.FamilyTest);
                    var selected = RemoteOfflineBootstrap.Select(empty,
                        starter, new[] { Trust(signer, "starter-pub") },
                        new EngineVersion(1, 0, 0), 2000);
                    Assert.That(selected.Manifest.GameId, Is.EqualTo(game));
                    Assert.That(selected.Manifest.Sequence, Is.EqualTo(1));
                    Assert.That(selected.FileDigests.Count, Is.EqualTo(1));
                    Assert.That(selected.SignedEnvelopeDigest.Length,
                        Is.EqualTo(64));
                    Assert.Throws<InvalidOperationException>(() =>
                        starter.VerifyForOffline(
                            new[] { Trust(signer, "starter-pub") },
                            game == "Wonder_Pop" ? "Coral_Rescue" : "Wonder_Pop",
                            RemotePlatform.Android, RemoteChannel.FamilyTest,
                            new EngineVersion(1, 0, 0), 2000));
                }
            }
        }

        [Test]
        public void M11_006_PackagedPayloadTamperAndRevokedKeyCannotBoot()
        {
            using (var signer = new RSACryptoServiceProvider(2048))
            using (var outsider = new RSACryptoServiceProvider(2048))
            {
                var manifest = Manifest("Coral_Rescue", "starter-pub");
                var wrong = Starter(signer, manifest,
                    Encoding.UTF8.GetBytes("{\"starter\":false}"));
                var state = new RemoteReleaseState("Coral_Rescue",
                    RemotePlatform.Android, RemoteChannel.FamilyTest);
                Assert.Throws<InvalidOperationException>(() =>
                    RemoteOfflineBootstrap.Select(state, wrong,
                        new[] { Trust(signer, "starter-pub") },
                        new EngineVersion(1, 0, 0), 2000));
                var good = Starter(signer, manifest);
                Assert.Throws<InvalidOperationException>(() =>
                    RemoteOfflineBootstrap.Select(state, good,
                        new[] { Trust(signer, "starter-pub", true) },
                        new EngineVersion(1, 0, 0), 2000));
                Assert.Throws<InvalidOperationException>(() =>
                    RemoteOfflineBootstrap.Select(state, good,
                        new[] { Trust(outsider, "starter-pub") },
                        new EngineVersion(1, 0, 0), 2000));
                Assert.Throws<InvalidOperationException>(() =>
                    RemoteOfflineBootstrap.Select(state, null,
                        new[] { Trust(signer, "starter-pub") },
                        new EngineVersion(1, 0, 0), 2000));
                Assert.Throws<InvalidOperationException>(() =>
                    RemoteOfflineBootstrap.Select(state, good,
                        new[] { Trust(signer, "starter-pub") },
                        new EngineVersion(1, 0, 0), 9000));
            }
        }

        [Test]
        public void M11_006_RevokedInstalledReleaseFallsBackToSeparateSignedStarter()
        {
            using (var oldSigner = new RSACryptoServiceProvider(2048))
            using (var newSigner = new RSACryptoServiceProvider(2048))
            {
                var oldManifest = Manifest("Coral_Rescue", "retired", 4);
                var oldSig = Sign(oldSigner, oldManifest);
                var oldValid = RemoteContentStager.VerifyComplete(
                    oldManifest, oldSig, new[] { Trust(oldSigner, "retired") },
                    oldManifest.GameId, oldManifest.Platform,
                    oldManifest.Channel, new EngineVersion(1, 0, 0),
                    2000, new Dictionary<string, byte[]> {
                        { "levels/starter.json", Content }
                    });
                var persisted = new RemoteReleaseState("Coral_Rescue",
                    RemotePlatform.Android, RemoteChannel.FamilyTest,
                    active: oldValid, highestSequence: 4);
                var next = Manifest("Coral_Rescue", "starter-pub");
                var wrapped = Starter(newSigner, next);
                var candidate = RemoteOfflineBootstrap.Select(
                    persisted, wrapped, new[] {
                        Trust(oldSigner, "retired", true),
                        Trust(newSigner, "starter-pub")
                    }, new EngineVersion(1, 0, 0), 2000);
                Assert.That(candidate.Manifest.KeyId,
                    Is.EqualTo("starter-pub"));
                Assert.That(candidate.Manifest.Sequence, Is.EqualTo(1),
                    "Bundled offline fallback must not rewrite anti-rollback.");
                Assert.That(persisted.HighestSequence, Is.EqualTo(4));
                Assert.Throws<InvalidOperationException>(() =>
                    RemoteReleasePlanner.Promote(persisted, candidate),
                    "Bundled starter must not bypass anti-rollback on install.");
            }
        }
    }
}
