using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using Match3.Engine.Runtime;
using NUnit.Framework;
using UnityEngine;

namespace Match3.Engine.Demo.Tests
{
    public sealed class NeutralSignedStarterPrelaunchTests
    {
        private static readonly byte[] Content =
            Encoding.UTF8.GetBytes("{\"neutral\":\"starter\"}");

        private static RemotePublicSigningKey Key(
            RSACryptoServiceProvider rsa)
        {
            var parameters = rsa.ExportParameters(false);
            return new RemotePublicSigningKey("studio-neutral-public",
                parameters.Modulus, parameters.Exponent, 0, 9000);
        }

        private static RemoteContentManifest Manifest() =>
            new RemoteContentManifest("Dino_Islands",
                RemotePlatform.Android, RemoteChannel.FamilyTest,
                "starter-signed-1", "studio-neutral-public",
                new EngineVersion(1, 0, 0),
                new EngineVersion(1, 1, 0), 1000, 8000,
                new[] { new RemoteContentEntry("levels/neutral.json",
                    RemoteContentKind.Json, Content.Length,
                    RemoteContentSafety.Sha256(Content)) });

        [Test]
        public void M11_006_013_VerifiedStarterEnablesRealPrelaunchOfflineGate()
        {
            var root = new GameObject("signed-starter-offline-qa");
            try
            {
                var view = root.AddComponent<NeutralPatchBootstrapView>();
                Assert.That(view.CanEnterGame, Is.False);
                using (var rsa = new RSACryptoServiceProvider(2048))
                {
                    var manifest = Manifest();
                    var signature = rsa.SignData(manifest.CanonicalBytes(),
                        HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                    var keys = new[] { Key(rsa) };
                    var release = RemoteContentStager.VerifyComplete(
                        manifest, signature, keys, manifest.GameId,
                        manifest.Platform, manifest.Channel,
                        new EngineVersion(1, 0, 0), 2000,
                        new Dictionary<string, byte[]> {
                            { "levels/neutral.json", Content }
                        });
                    NeutralSignedStarterPrelaunch.BindVerifiedOffline(
                        view, release, keys, manifest.GameId,
                        manifest.Platform, manifest.Channel,
                        new EngineVersion(1, 0, 0), 2000);
                    Assert.That(view.HasVerifiedSession, Is.True);
                    Assert.That(view.CanEnterGame, Is.True);
                    Assert.That(view.VisibleStatus,
                        Does.Contain("verified content available"));
                    Assert.That(view.Snapshot().CanPurchase, Is.False);
                    Assert.That(view.Snapshot().CanClaimReward, Is.False);
                    Assert.Throws<InvalidOperationException>(() =>
                        NeutralSignedStarterPrelaunch.BindVerifiedOffline(
                            view, release, keys, manifest.GameId,
                            manifest.Platform, manifest.Channel,
                            new EngineVersion(1, 0, 0), 2000));
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        [Test]
        public void M11_006_013_ForeignKeysAndProfilesNeverUnlockPrelaunch()
        {
            var root = new GameObject("signed-starter-reject-qa");
            try
            {
                var view = root.AddComponent<NeutralPatchBootstrapView>();
                using (var rsa = new RSACryptoServiceProvider(2048))
                using (var attacker = new RSACryptoServiceProvider(2048))
                {
                    var manifest = Manifest();
                    var signature = rsa.SignData(manifest.CanonicalBytes(),
                        HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                    var keys = new[] { Key(rsa) };
                    var release = RemoteContentStager.VerifyComplete(
                        manifest, signature, keys, manifest.GameId,
                        manifest.Platform, manifest.Channel,
                        new EngineVersion(1, 0, 0), 2000,
                        new Dictionary<string, byte[]> {
                            { "levels/neutral.json", Content }
                        });
                    Assert.Throws<InvalidOperationException>(() =>
                        NeutralSignedStarterPrelaunch.BindVerifiedOffline(
                            view, release, new[] { Key(attacker) },
                            manifest.GameId, manifest.Platform,
                            manifest.Channel,
                            new EngineVersion(1, 0, 0), 2000));
                    Assert.Throws<InvalidOperationException>(() =>
                        NeutralSignedStarterPrelaunch.BindVerifiedOffline(
                            view, release, keys, "Garage_Legends",
                            manifest.Platform, manifest.Channel,
                            new EngineVersion(1, 0, 0), 2000));
                    Assert.Throws<InvalidOperationException>(() =>
                        NeutralSignedStarterPrelaunch.BindVerifiedOffline(
                            view, release, keys, manifest.GameId,
                            RemotePlatform.Ios, manifest.Channel,
                            new EngineVersion(1, 0, 0), 2000));
                    Assert.That(view.CanEnterGame, Is.False);
                    Assert.That(view.HasVerifiedSession, Is.False);
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }
    }
}
