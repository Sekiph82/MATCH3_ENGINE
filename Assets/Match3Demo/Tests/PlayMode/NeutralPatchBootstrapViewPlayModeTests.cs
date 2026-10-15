using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using Match3.Engine.Runtime;
using NUnit.Framework;
using UnityEngine;

namespace Match3.Engine.Demo.Tests
{
    public sealed class NeutralPatchBootstrapViewPlayModeTests
    {
        private static readonly byte[] Contents =
            Encoding.UTF8.GetBytes("{\"level\":1}");

        private static RemoteContentManifest Manifest() =>
            new RemoteContentManifest("Coral_Rescue",
                RemotePlatform.Android, RemoteChannel.FamilyTest,
                "neutral-v1", "public-1",
                new EngineVersion(1, 0, 0),
                new EngineVersion(1, 1, 0), 1000, 9000,
                new[] { new RemoteContentEntry("levels/start.json",
                    RemoteContentKind.Json, Contents.Length,
                    RemoteContentSafety.Sha256(Contents)) });

        private static RemotePublicSigningKey Key(RSACryptoServiceProvider rsa)
        {
            var k = rsa.ExportParameters(false);
            return new RemotePublicSigningKey("public-1",
                k.Modulus, k.Exponent, 0, 10000);
        }

        private static byte[] Sign(
            RSACryptoServiceProvider rsa, RemoteContentManifest m) =>
            rsa.SignData(m.CanonicalBytes(),
                HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        [Test]
        public void M11_013_UnityPatchPanelNeverInventsSignedStatusOrGameAccess()
        {
            var root = new GameObject("neutral-signed-patch-test");
            try
            {
                var view = root.AddComponent<NeutralPatchBootstrapView>();
                Assert.That(view.HasVerifiedSession, Is.False);
                Assert.That(view.CanEnterGame, Is.False);
                Assert.That(view.TryRetry(), Is.False);
                Assert.That(view.Snapshot(), Is.Null);
                Assert.Throws<ArgumentException>(() =>
                    view.ShowUnavailable("https://secret.local/key"));
                view.ShowUnavailable("no-signed-starter");
                Assert.That(view.VisibleStatus,
                    Does.Contain("no-signed-starter"));

                using (var rsa = new RSACryptoServiceProvider(2048))
                {
                    var m = Manifest();
                    var key = Key(rsa);
                    var flow = new RemotePatchFlow(m, Sign(rsa, m),
                        new[] { key }, m.GameId, m.Platform, m.Channel,
                        new EngineVersion(1, 0, 0), 2000);
                    view.Bind(flow);
                    Assert.That(view.HasVerifiedSession, Is.True);
                    Assert.That(view.VisibleStatus,
                        Is.EqualTo("Signed update available"));
                    Assert.That(view.CanEnterGame, Is.False);
                    Assert.That(view.TryRetry(), Is.False);
                    Assert.Throws<InvalidOperationException>(() =>
                        view.Bind(flow));
                    Assert.Throws<InvalidOperationException>(() =>
                        view.ShowUnavailable("network-unavailable"));
                    flow.RecordFailure();
                    Assert.That(view.VisibleStatus, Does.Contain("Retry"));
                    Assert.That(view.TryRetry(), Is.True);
                    Assert.That(view.LastPublicAction,
                        Is.EqualTo("retry-requested"));
                    Assert.That(view.CanEnterGame, Is.False);
                    Assert.That(flow.Snapshot().CanClaimReward, Is.False);
                    Assert.That(flow.Snapshot().CanPurchase, Is.False);
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        [Test]
        public void M11_013_UnityPatchPanelShowsOnlyAuthenticatedOfflinePlay()
        {
            var root = new GameObject("neutral-verified-offline-view");
            try
            {
                var view = root.AddComponent<NeutralPatchBootstrapView>();
                using (var rsa = new RSACryptoServiceProvider(2048))
                {
                    var m = Manifest();
                    var sig = Sign(rsa, m);
                    var key = Key(rsa);
                    var flow = new RemotePatchFlow(m, sig, new[] { key },
                        m.GameId, m.Platform, m.Channel,
                        new EngineVersion(1, 0, 0), 2000);
                    view.Bind(flow);
                    Assert.That(view.CanEnterGame, Is.False);
                    var verified = RemoteContentStager.VerifyComplete(m,
                        sig, new[] { key }, m.GameId, m.Platform,
                        m.Channel, new EngineVersion(1, 0, 0), 2000,
                        new Dictionary<string, byte[]> {
                            { "levels/start.json", Contents }
                        });
                    flow.RecordOfflineFallback(verified, new[] { key },
                        new EngineVersion(1, 0, 0), 2000);
                    Assert.That(view.VisibleStatus,
                        Is.EqualTo("Offline: verified content available"));
                    Assert.That(view.CanEnterGame, Is.True);
                    Assert.That(view.Snapshot().CanPurchase, Is.False);
                    Assert.That(view.Snapshot().CanClaimReward, Is.False);
                    Assert.That(view.TryRetry(), Is.False);
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }
    }
}
