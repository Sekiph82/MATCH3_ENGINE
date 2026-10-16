using System;
using System.Collections;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Match3.Engine.Runtime;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace Match3.Engine.Demo.Tests
{
    public sealed class NeutralSignedNativeBundleEditorTests
    {
        // A real Unity-built, Windows-native AssetBundle containing only a
        // neutral TextAsset. This is NOT the official Addressables API or a
        // signed remote Addressables catalog graph; that gate remains OPEN.
        private const string SourceAsset =
            "Assets/Match3Demo/Tests/EditMode/neutral-signed-bundle-qa.txt";
        private const string SignedBundle = "addressables/qa-signed-native.bundle";
        private const string SignedCatalog = "addressables/catalog.json";
        private const string AssetAddress =
            "assets/match3demo/tests/editmode/neutral-signed-bundle-qa.txt";
        private static readonly byte[] Catalog =
            Encoding.UTF8.GetBytes("{\"m_InternalIds\":[]}");

        private sealed class InMemoryRead : IRemoteReadTransport
        {
            private readonly byte[] _bundle;
            private readonly bool _corrupt;

            public InMemoryRead(byte[] bundle, bool corrupt)
            {
                _bundle = (byte[])bundle.Clone();
                _corrupt = corrupt;
            }

            public Task<RemoteReadResponse> FetchAsync(
                Uri origin, long maximumBytes, CancellationToken token)
            {
                token.ThrowIfCancellationRequested();
                byte[] bytes;
                if (origin.AbsolutePath.EndsWith(".bundle",
                    StringComparison.Ordinal))
                {
                    bytes = (byte[])_bundle.Clone();
                    if (_corrupt) bytes[0] ^= 0x01;
                }
                else bytes = (byte[])Catalog.Clone();
                if (bytes.LongLength > maximumBytes)
                    throw new InvalidOperationException("Fixture exceeds signed budget.");
                return Task.FromResult(new RemoteReadResponse(200, bytes));
            }
        }

        [UnityTest]
        public IEnumerator M11_008_ActualWindowsNativeBundleLoadsOnlyAfterSignedByteStaging()
        {
            Assert.That(Application.platform,
                Is.EqualTo(RuntimePlatform.WindowsEditor));
            Assert.That(File.Exists(SourceAsset), Is.True);

            // The build lives only in a new, uniquely named OS temp folder.
            // Do not modify asset import labels, clean worktrees, or delete
            // build output/evidence after this local test.
            var output = Path.Combine(Path.GetTempPath(),
                "MATCH3_M11_NATIVE_SIGNED_BUNDLE_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(output);
            var builds = new[] {
                new AssetBundleBuild {
                    assetBundleName = "qa-signed-native.bundle",
                    assetNames = new[] { SourceAsset }
                }
            };
            var nativeManifest = BuildPipeline.BuildAssetBundles(output,
                builds, BuildAssetBundleOptions.None, BuildTarget.StandaloneWindows64);
            Assert.That(nativeManifest, Is.Not.Null,
                "Real Windows platform native AssetBundle must build.");
            var bundleFile = Path.Combine(output, "qa-signed-native.bundle");
            Assert.That(File.Exists(bundleFile), Is.True);
            var bundleBytes = File.ReadAllBytes(bundleFile);
            Assert.That(bundleBytes.Length, Is.GreaterThan(100));
            Assert.That(bundleBytes.LongLength,
                Is.LessThanOrEqualTo(RemoteContentSafety.FileByteLimit));

            using (var rsa = new RSACryptoServiceProvider(2048))
            {
                var rsaPublic = rsa.ExportParameters(false);
                var key = new RemotePublicSigningKey("editor-native-public",
                    rsaPublic.Modulus, rsaPublic.Exponent, 0, 10000);
                var manifest = new RemoteContentManifest(
                    "Coral_Rescue", RemotePlatform.Windows,
                    RemoteChannel.Development, "qa-native-bundle",
                    "editor-native-public", new EngineVersion(1, 0, 0),
                    new EngineVersion(1, 1, 0), 1000, 9000,
                    new[] {
                        new RemoteContentEntry(SignedCatalog,
                            RemoteContentKind.AddressablesCatalog,
                            Catalog.Length, RemoteContentSafety.Sha256(Catalog)),
                        new RemoteContentEntry(SignedBundle,
                            RemoteContentKind.AddressablesBundle,
                            bundleBytes.Length,
                            RemoteContentSafety.Sha256(bundleBytes))
                    });
                var signature = rsa.SignData(manifest.CanonicalBytes(),
                    HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                var origin =
                    new RemoteReadOrigin("https://neutral.example.test/qa/");
                var good = RemoteDownloadCoordinator.FetchStageAsync(
                    manifest, signature, new[] { key }, origin,
                    new InMemoryRead(bundleBytes, false),
                    manifest.GameId, manifest.Platform, manifest.Channel,
                    new EngineVersion(1, 0, 0), 2000, CancellationToken.None);
                while (!good.IsCompleted) yield return null;
                Assert.That(good.IsFaulted, Is.False,
                    "A valid signed native bundle must stage.");
                var signed = RemoteVerifiedAddressablesPayload.Prepare(
                    good.Result, SignedCatalog, new[] { SignedBundle },
                    manifest.GameId, RemotePlatform.Windows);

                // This is the actual Unity native AssetBundle loader, not a
                // magic-string fake UnityFS byte fixture.
                var load = NeutralVerifiedBundleTextConsumer
                    .ReadSignedTextAssetAsync(signed, manifest.GameId,
                        SignedBundle, AssetAddress, CancellationToken.None);
                while (!load.IsCompleted) yield return null;
                Assert.That(load.IsFaulted, Is.False,
                    "Native loader must return the genuinely bundled TextAsset.");
                var text = Encoding.UTF8.GetString(load.Result);
                Assert.That(text, Does.Contain("neutral-asset-bundle:"));

                // Wrong digest/bytes may never reach AssetBundle.LoadFromMemory.
                var corrupted = RemoteDownloadCoordinator.FetchStageAsync(
                    manifest, signature, new[] { key }, origin,
                    new InMemoryRead(bundleBytes, true),
                    manifest.GameId, manifest.Platform, manifest.Channel,
                    new EngineVersion(1, 0, 0), 2000, CancellationToken.None);
                while (!corrupted.IsCompleted) yield return null;
                Assert.That(corrupted.IsFaulted, Is.True);
                Assert.That(corrupted.Exception, Is.Not.Null);
            }
        }
    }
}
