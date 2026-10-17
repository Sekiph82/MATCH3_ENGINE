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
        public void M11_013_UnavailablePlayerShowsHostRetryOnlyWhenRealHostIsRegistered()
        {
            var root = new GameObject("signed-unavailable-host-retry");
            try
            {
                var view = root.AddComponent<NeutralPatchBootstrapView>();
                view.ShowUnavailable("content-unavailable");
                Assert.That(view.CanEnterGame, Is.False);
                Assert.That(view.CanRequestHostRetry, Is.False);
                Assert.That(view.TryRequestHostRetry(), Is.False);
                Assert.That(view.VisibleStatus,
                    Does.Contain("content-unavailable"));

                var requested = 0;
                view.RegisterHostRetry(() =>
                {
                    requested++;
                    return true; // Test-only host scheduling receipt.
                });
                Assert.Throws<InvalidOperationException>(() =>
                    view.RegisterHostRetry(() => true));
                Assert.That(view.CanRequestHostRetry, Is.True);
                Assert.That(view.TryRequestHostRetry(), Is.True);
                Assert.That(requested, Is.EqualTo(1));
                Assert.That(view.HostRetryPending, Is.True);
                Assert.That(view.VisibleStatus,
                    Is.EqualTo("Checking signed content..."),
                    "Active real retry must not show stale unavailable status.");
                Assert.That(view.CanEnterGame, Is.False);
                Assert.That(view.LastPublicAction,
                    Is.EqualTo("host-retry-scheduled"));
                Assert.That(view.TryRequestHostRetry(), Is.False);
                Assert.That(requested, Is.EqualTo(1));

                // A host may re-enable another attempt only after reporting
                // an actual completed failure. This never unlocks the board.
                view.ShowUnavailable("network-unavailable");
                Assert.That(view.HostRetryPending, Is.False);
                Assert.That(view.CanRequestHostRetry, Is.True);
                Assert.That(view.TryRequestHostRetry(), Is.True);
                Assert.That(requested, Is.EqualTo(2));
                Assert.That(view.CanEnterGame, Is.False);
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        [Test]
        public void M11_013_SynchronousVerifiedHostRetryCannotLeaveFalsePendingStatus()
        {
            var root = new GameObject("signed-inline-host-retry");
            try
            {
                var view = root.AddComponent<NeutralPatchBootstrapView>();
                view.ShowUnavailable("content-unavailable");
                using (var rsa = new RSACryptoServiceProvider(2048))
                {
                    var manifest = Manifest();
                    var signature = Sign(rsa, manifest);
                    var key = Key(rsa);
                    var flow = new RemotePatchFlow(manifest, signature,
                        new[] { key }, manifest.GameId,
                        manifest.Platform, manifest.Channel,
                        new EngineVersion(1, 0, 0), 2000);
                    var release = RemoteContentStager.VerifyComplete(
                        manifest, signature, new[] { key },
                        manifest.GameId, manifest.Platform,
                        manifest.Channel, new EngineVersion(1, 0, 0),
                        2000, new Dictionary<string, byte[]> {
                            { "levels/start.json", Contents }
                        });
                    view.RegisterHostRetry(() =>
                    {
                        flow.RecordOfflineFallback(release, new[] { key },
                            new EngineVersion(1, 0, 0), 2000);
                        view.Bind(flow);
                        return true;
                    });

                    Assert.That(view.TryRequestHostRetry(), Is.True);
                    Assert.That(view.CanEnterGame, Is.True);
                    Assert.That(view.HasVerifiedSession, Is.True);
                    Assert.That(view.HostRetryPending, Is.False);
                    Assert.That(view.LastPublicAction,
                        Is.EqualTo("signed-session-bound"));
                    Assert.That(view.CanRequestHostRetry, Is.False);
                    Assert.That(view.TryRequestHostRetry(), Is.False);
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        [Test]
        public void M11_013_RejectedHostRetryCannotClaimProgressOrUnlockGame()
        {
            var root = new GameObject("signed-rejected-host-retry");
            try
            {
                var view = root.AddComponent<NeutralPatchBootstrapView>();
                view.ShowUnavailable("no-signed-starter");
                var attempts = 0;
                view.RegisterHostRetry(() => { attempts++; return false; });
                Assert.That(view.TryRequestHostRetry(), Is.False);
                Assert.That(attempts, Is.EqualTo(1));
                Assert.That(view.HostRetryPending, Is.False);
                Assert.That(view.CanEnterGame, Is.False);
                Assert.That(view.Snapshot(), Is.Null);
                Assert.That(view.VisibleStatus,
                    Does.Contain("no-signed-starter"));
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
