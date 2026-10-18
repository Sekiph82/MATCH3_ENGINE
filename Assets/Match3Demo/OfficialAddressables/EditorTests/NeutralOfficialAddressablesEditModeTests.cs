using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Match3.Engine.Runtime;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Build;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.ResourceLocations;
using UnityEngine.TestTools;

namespace Match3.Engine.Demo.OfficialAddressables.Tests
{
    // Source-only until the optional official com.unity.addressables
    // package is independently present in the local Unity project.
    // A fake JSON catalog is intentionally NEVER passed to native LoadAsset:
    // these are signed-receipt/firewall negative tests, not an E2E PASS.
    public sealed class NeutralOfficialAddressablesEditModeTests
    {
        private const string NativeTextPath =
            "Assets/Match3Demo/OfficialAddressables/EditorTests/neutral-native-addressable.txt";
        private const string NativeAddress = "match3/engine/neutral/m11-addressables";
        private const string CatalogPath = "addressables/catalog.json";

        private sealed class NativeBuildRead : IRemoteReadTransport
        {
            private readonly Dictionary<string, byte[]> _files;
            public int Calls { get; private set; }

            public NativeBuildRead(Dictionary<string, byte[]> files)
            { _files = files; }

            public Task<RemoteReadResponse> FetchAsync(Uri uri,
                long maximumBytes, CancellationToken token)
            {
                token.ThrowIfCancellationRequested();
                Calls++;
                var path = Uri.UnescapeDataString(uri.AbsolutePath);
                var root = "/neutral/";
                var offset = path.LastIndexOf(root, StringComparison.Ordinal);
                if (offset < 0 || !_files.TryGetValue(path.Substring(offset + root.Length),
                    out var bytes))
                    throw new InvalidOperationException("Unsigned native fixture path.");
                if (bytes.LongLength > maximumBytes)
                    throw new InvalidOperationException("Native fixture exceeds signed budget.");
                return Task.FromResult(new RemoteReadResponse(200, bytes));
            }
        }

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
                Assert.CatchAsync<OperationCanceledException>(async () =>
                    await NeutralOfficialAddressablesConsumer.ReadSignedTextAssetAsync(
                        signed, "Coral_Rescue", "neutral/text",
                        cancellation.Token));
            }
        }

        [Test]
        public async Task M11_008_OfficialNativeAdapterPreservesAnExistingGlobalTransform()
        {
            var signed = await SignedReceipt(RemotePlatform.Windows);
            Func<IResourceLocation, string> existing = location =>
                location == null ? string.Empty : location.InternalId;
            Addressables.InternalIdTransformFunc = existing;
            try
            {
                Assert.ThrowsAsync<InvalidOperationException>(async () =>
                    await NeutralOfficialAddressablesConsumer.ReadSignedTextAssetAsync(
                        signed, "Coral_Rescue", "neutral/text",
                        CancellationToken.None));
                Assert.That(Addressables.InternalIdTransformFunc,
                    Is.SameAs(existing));
            }
            finally
            {
                if (ReferenceEquals(Addressables.InternalIdTransformFunc, existing))
                    Addressables.InternalIdTransformFunc = null;
            }
        }

        [UnityTest]
        public IEnumerator M11_008_OfficialAddressablesBuildSignsAndStagesNativeTextAsset()
        {
            Assert.That(Application.platform,
                Is.EqualTo(RuntimePlatform.WindowsEditor));
            Assert.That(EditorUserBuildSettings.activeBuildTarget,
                Is.EqualTo(BuildTarget.StandaloneWindows64));

            const string expected = "match3-neutral-addressables-native-v1\n";
            var expectedBytes = new UTF8Encoding(false).GetBytes(expected);
            if (File.Exists(NativeTextPath))
                Assert.That(File.ReadAllBytes(NativeTextPath), Is.EqualTo(expectedBytes),
                    "Refuse to replace a different owner/test fixture.");
            else
            {
                Directory.CreateDirectory(Path.GetDirectoryName(NativeTextPath));
                using (var stream = new FileStream(NativeTextPath, FileMode.CreateNew,
                    FileAccess.Write, FileShare.None))
                {
                    stream.Write(expectedBytes, 0, expectedBytes.Length);
                    stream.Flush(true);
                }
            }
            AssetDatabase.ImportAsset(NativeTextPath,
                ImportAssetOptions.ForceSynchronousImport);
            var guid = AssetDatabase.AssetPathToGUID(NativeTextPath);
            Assert.That(guid, Is.Not.Null.And.Not.Empty);

            var settings = AddressableAssetSettingsDefaultObject.GetSettings(true);
            Assert.That(settings, Is.Not.Null);
            // The Engine remote manifest accepts .json catalogs only. Unity
            // Addressables 2.11 defaults to binary catalogs, so choose its
            // official JSON catalog setting for this signed-content contract.
            settings.EnableJsonCatalog = true;
            const string groupName = "M11_Official_Neutral_Native_QA";
            var group = settings.FindGroup(groupName);
            if (group == null)
                group = settings.CreateGroup(groupName, false, false, true,
                    null, typeof(BundledAssetGroupSchema));
            var schema = group.GetSchema<BundledAssetGroupSchema>();
            Assert.That(schema, Is.Not.Null);
            schema.BuildPath.SetVariableByName(settings,
                AddressableAssetSettings.kLocalBuildPath);
            schema.LoadPath.SetVariableByName(settings,
                AddressableAssetSettings.kLocalLoadPath);
            var entry = settings.CreateOrMoveEntry(guid, group);
            Assert.That(entry, Is.Not.Null);
            entry.address = NativeAddress;
            EditorUtility.SetDirty(group);
            EditorUtility.SetDirty(schema);
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();

            AddressableAssetSettings.BuildPlayerContent(
                out AddressablesPlayerBuildResult buildResult);
            Assert.That(buildResult, Is.Not.Null);
            Assert.That(buildResult.Error, Is.Null.Or.Empty,
                "Official Addressables native content build failed.");
            var profileId = settings.activeProfileId;
            var buildValue = settings.profileSettings.GetValueByName(profileId,
                AddressableAssetSettings.kLocalBuildPath);
            var buildRoot = Path.GetFullPath(settings.profileSettings.EvaluateString(
                profileId, buildValue));
            Assert.That(Directory.Exists(buildRoot), Is.True, buildRoot);
            var catalogRoot = Directory.GetParent(buildRoot).FullName;

            var catalogFile = Directory.GetFiles(catalogRoot, "catalog*.json",
                SearchOption.AllDirectories)
                .Where(path => File.ReadAllText(path).StartsWith(
                    "{\"m_LocatorId\"", StringComparison.Ordinal))
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();
            Assert.That(catalogFile, Is.Not.Null,
                "The actual Addressables catalog must contain the test address.");
            var catalogBytes = File.ReadAllBytes(catalogFile);
            Assert.That(catalogBytes.Length, Is.GreaterThan(100));
            var bundleFiles = Directory.GetFiles(buildRoot, "*.bundle",
                SearchOption.AllDirectories);
            Assert.That(bundleFiles.Length, Is.GreaterThan(0),
                "Official Addressables must emit a native bundle.");

            var stagedFiles = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            var signedBundlePaths = new List<string>();
            foreach (var file in bundleFiles)
            {
                var relative = "addressables/" + Path.GetFileName(file);
                Assert.That(stagedFiles.ContainsKey(relative), Is.False,
                    "Bundle leaf names must be unique for signed catalog binding.");
                stagedFiles.Add(relative, File.ReadAllBytes(file));
                signedBundlePaths.Add(relative);
                Assert.That(stagedFiles[relative].Length, Is.GreaterThan(100));
            }
            stagedFiles.Add(CatalogPath, catalogBytes);

            using (var rsa = new RSACryptoServiceProvider(2048))
            {
                var publicKey = rsa.ExportParameters(false);
                var key = new RemotePublicSigningKey("m11-addressables-editor",
                    publicKey.Modulus, publicKey.Exponent, 0, 10000);
                var entries = new List<RemoteContentEntry> {
                    new RemoteContentEntry(CatalogPath,
                        RemoteContentKind.AddressablesCatalog,
                        catalogBytes.Length, RemoteContentSafety.Sha256(catalogBytes))
                };
                foreach (var path in signedBundlePaths)
                {
                    var bytes = stagedFiles[path];
                    entries.Add(new RemoteContentEntry(path,
                        RemoteContentKind.AddressablesBundle, bytes.Length,
                        RemoteContentSafety.Sha256(bytes)));
                }
                var manifest = new RemoteContentManifest("Coral_Rescue",
                    RemotePlatform.Windows, RemoteChannel.Development,
                    "m11-native-addressables", "m11-addressables-editor",
                    new EngineVersion(1, 0, 0), new EngineVersion(1, 1, 0),
                    1000, 9000, entries);
                var signature = rsa.SignData(manifest.CanonicalBytes(),
                    HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                var transport = new NativeBuildRead(stagedFiles);
                var stage = RemoteDownloadCoordinator.FetchStageAsync(
                    manifest, signature, new[] { key },
                    new RemoteReadOrigin("https://signed.invalid/neutral/"),
                    transport, manifest.GameId, manifest.Platform,
                    manifest.Channel, new EngineVersion(1, 0, 0), 2000,
                    CancellationToken.None);
                while (!stage.IsCompleted) yield return null;
                Assert.That(stage.IsFaulted, Is.False,
                    "Genuine native catalog and bundles must pass signed staging.");
                Assert.That(transport.Calls,
                    Is.EqualTo(entries.Count),
                    "Staging reads only the enumerated signed fixture entries.");
                var approved = RemoteVerifiedAddressablesPayload.Prepare(
                    stage.Result, CatalogPath, signedBundlePaths,
                    manifest.GameId, RemotePlatform.Windows);
                Assert.That(approved.GameId, Is.EqualTo(manifest.GameId));
                Assert.That(approved.Platform, Is.EqualTo(RemotePlatform.Windows));
                Assert.That(approved.BundlePaths, Is.EquivalentTo(signedBundlePaths));
                Assert.That(approved.ReadVerifiedBytes(CatalogPath),
                    Is.EqualTo(catalogBytes));
                foreach (var path in signedBundlePaths)
                    Assert.That(approved.ReadVerifiedBytes(path),
                        Is.EqualTo(stagedFiles[path]));
            }
            Debug.Log("M11_008_OFFICIAL_ADDRESSABLES_NATIVE_BUILD_SIGNED_STAGING=PASS " +
                "TARGET=Windows64 CATALOG=UNITY_BUILT BUNDLES=UNITY_BUILT " +
                "RSA_SHA256=PASS STAGED_BYTES=EXACT " +
                "NATIVE_CATALOG_LOAD=NOT_VERIFIED");
        }
    }
}
