using System;
using System.Collections;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Match3.Engine.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Match3.Engine.Demo.Tests
{
    public sealed class NeutralUnityStreamingStarterPlayModeTests
    {
        private static readonly byte[] Content =
            Encoding.UTF8.GetBytes("{\"neutral_starter\":1}");

        private static RemotePublicSigningKey Key(RSACryptoServiceProvider signer)
        {
            var k = signer.ExportParameters(false);
            return new RemotePublicSigningKey("qa-pub", k.Modulus,
                k.Exponent, 0, 9000);
        }

        private static RemoteContentManifest Manifest() =>
            new RemoteContentManifest("Coral_Rescue",
                RemotePlatform.Android, RemoteChannel.FamilyTest,
                "qa-bundled-v1", "qa-pub", new EngineVersion(1, 0, 0),
                new EngineVersion(1, 1, 0), 1000, 8000,
                new[] {
                    new RemoteContentEntry("qa/neutral.json",
                        RemoteContentKind.Json, Content.Length,
                        RemoteContentSafety.Sha256(Content))
                });

        private static string Partition()
        {
            return Path.Combine(Application.streamingAssetsPath, "match3",
                "Coral_Rescue", "0", "1");
        }

        private static void WriteIsolated(RSACryptoServiceProvider signer,
            RemoteContentManifest manifest)
        {
            var root = Partition();
            if (Directory.Exists(root))
                throw new InvalidOperationException(
                    "Refusing to replace an existing StreamingAssets partition.");
            Directory.CreateDirectory(Path.Combine(root, "qa"));
            File.WriteAllBytes(Path.Combine(root, "manifest.canonical"),
                manifest.CanonicalBytes());
            File.WriteAllBytes(Path.Combine(root, "manifest.sig"),
                signer.SignData(manifest.CanonicalBytes(),
                    HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
            File.WriteAllBytes(Path.Combine(root, "qa", "neutral.json"),
                Content);
        }

        private static void DeleteOnlyOwnedFixture()
        {
            var root = Partition();
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }

        [UnityTest]
        public IEnumerator M11_006_ActualUnityStreamingAssetsLoadAuthenticatesBundledStarter()
        {
            using (var signer = new RSACryptoServiceProvider(2048))
            {
                var manifest = Manifest();
                WriteIsolated(signer, manifest);
                try
                {
                    var task = NeutralUnityStreamingStarterLoader.LoadAsync(
                        manifest.GameId, manifest.Platform, manifest.Channel,
                        new EngineVersion(1, 0, 0), new[] { Key(signer) },
                        2000, CancellationToken.None);
                    while (!task.IsCompleted) yield return null;
                    if (task.IsFaulted) throw task.Exception;
                    Assert.That(task.IsCanceled, Is.False);
                    Assert.That(task.Result.Manifest.GameId, Is.EqualTo("Coral_Rescue"));
                    Assert.That(task.Result.Manifest.ReleaseId,
                        Is.EqualTo("qa-bundled-v1"));
                    Assert.That(task.Result.FileDigests["qa/neutral.json"],
                        Is.EqualTo(RemoteContentSafety.Sha256(Content)));
                    Assert.That(task.Result.Signature.Length, Is.EqualTo(256));
                }
                finally { DeleteOnlyOwnedFixture(); }
            }
        }

        [UnityTest]
        public IEnumerator M11_006_UnityStreamingStarterRejectsTamperedContentsAndForgedKey()
        {
            using (var signer = new RSACryptoServiceProvider(2048))
            using (var outsider = new RSACryptoServiceProvider(2048))
            {
                var manifest = Manifest();
                WriteIsolated(signer, manifest);
                try
                {
                    var wrongKey = NeutralUnityStreamingStarterLoader.LoadAsync(
                        manifest.GameId, manifest.Platform, manifest.Channel,
                        new EngineVersion(1, 0, 0), new[] { Key(outsider) },
                        2000, CancellationToken.None);
                    while (!wrongKey.IsCompleted) yield return null;
                    Assert.That(wrongKey.IsFaulted, Is.True,
                        "Foreign key must never authorize bundled data.");

                    var file = Path.Combine(Partition(), "qa", "neutral.json");
                    File.WriteAllBytes(file,
                        Encoding.UTF8.GetBytes("{\"neutral_starter\":9}"));
                    var changed = NeutralUnityStreamingStarterLoader.LoadAsync(
                        manifest.GameId, manifest.Platform, manifest.Channel,
                        new EngineVersion(1, 0, 0), new[] { Key(signer) },
                        2000, CancellationToken.None);
                    while (!changed.IsCompleted) yield return null;
                    Assert.That(changed.IsFaulted, Is.True,
                        "Changed packaged bytes must fail even with a valid manifest.");
                }
                finally { DeleteOnlyOwnedFixture(); }
            }
        }
    }
}
