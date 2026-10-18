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
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Networking;
using UnityEngine.ResourceManagement.ResourceLocations;
using UnityEngine.TestTools;

namespace Match3.Engine.Demo.OfficialAddressables.PlayModeTests
{
    public sealed class NeutralOfficialAddressablesPlayModeTests
    {
        private const string AssetPath = "Assets/Match3Demo/OfficialAddressables/EditorTests/neutral-native-addressable.txt";
        private const string AssetAddress = "match3/engine/neutral/m11-addressables";
        private const string CatalogPath = "addressables/catalog.json";
        private static readonly byte[] Expected = new UTF8Encoding(false).GetBytes("match3-neutral-addressables-native-v1\n");

        private sealed class BuiltContentTransport : IRemoteReadTransport
        {
            private readonly Dictionary<string, byte[]> _files;
            public int Calls { get; private set; }
            public BuiltContentTransport(Dictionary<string, byte[]> files) { _files = files; }
            public Task<RemoteReadResponse> FetchAsync(Uri uri, long maximumBytes, CancellationToken token)
            {
                token.ThrowIfCancellationRequested();
                Calls++;
                var path = Uri.UnescapeDataString(uri.AbsolutePath);
                const string prefix = "/m11-native/";
                var index = path.LastIndexOf(prefix, StringComparison.Ordinal);
                if (index < 0 || !_files.TryGetValue(path.Substring(index + prefix.Length), out var bytes))
                    throw new InvalidOperationException("Unlisted native Addressables fixture path.");
                if (bytes.LongLength > maximumBytes)
                    throw new InvalidOperationException("Native Addressables fixture exceeds byte budget.");
                return Task.FromResult(new RemoteReadResponse(200, (byte[])bytes.Clone()));
            }
        }

        [UnityTest]
        public IEnumerator M11_008_PlayModeLoadsExactRsaSignedNativeAddressablesTextAssetTwice()
        {
            Assert.That(Application.platform == RuntimePlatform.WindowsEditor || Application.platform == RuntimePlatform.WindowsPlayer);
            Assert.That(File.Exists(AssetPath), Is.True, "Run the official Addressables EditMode build/stage test first.");
            Assert.That(File.ReadAllBytes(AssetPath), Is.EqualTo(Expected), "Native source TextAsset changed.");
            var projectRoot = Directory.GetParent(Application.dataPath).FullName;
            var addressablesRoot = Path.Combine(projectRoot, "Library", "com.unity.addressables", "aa", "Windows");
            Assert.That(Directory.Exists(addressablesRoot), Is.True, "Official Windows Addressables build output is missing.");
            var catalogFile = Directory.GetFiles(addressablesRoot, "catalog*.json", SearchOption.AllDirectories)
                .Where(path => File.ReadAllText(path).StartsWith("{\"m_LocatorId\"", StringComparison.Ordinal))
                .OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
            Assert.That(catalogFile, Is.Not.Null, "No real Unity catalog references the exact neutral TextAsset address.");
            var catalogBytes = File.ReadAllBytes(catalogFile);
            var files = new Dictionary<string, byte[]>(StringComparer.Ordinal) { [CatalogPath] = catalogBytes };
            var bundlePaths = new List<string>();
            foreach (var file in Directory.GetFiles(Path.GetDirectoryName(catalogFile), "*.bundle", SearchOption.AllDirectories))
            {
                var relative = "addressables/" + Path.GetFileName(file);
                Assert.That(files.ContainsKey(relative), Is.False, "Bundle leaf names must be unique.");
                files.Add(relative, File.ReadAllBytes(file));
                bundlePaths.Add(relative);
            }
            Assert.That(bundlePaths.Count, Is.GreaterThan(0), "Unity did not emit an Addressables bundle.");
            Assert.That(catalogBytes, Is.Not.Empty);

            RemoteVerifiedAddressablesPayload approved;
            var transport = new BuiltContentTransport(files);
            using (var rsa = new RSACryptoServiceProvider(2048))
            {
                var parameters = rsa.ExportParameters(false);
                var key = new RemotePublicSigningKey("m11-playmode-key", parameters.Modulus, parameters.Exponent, 0, 10000);
                var entries = new List<RemoteContentEntry>
                {
                    new RemoteContentEntry(CatalogPath, RemoteContentKind.AddressablesCatalog, catalogBytes.Length, RemoteContentSafety.Sha256(catalogBytes))
                };
                foreach (var path in bundlePaths)
                {
                    var bytes = files[path];
                    Assert.That(bytes.Length, Is.GreaterThan(100));
                    entries.Add(new RemoteContentEntry(path, RemoteContentKind.AddressablesBundle, bytes.Length, RemoteContentSafety.Sha256(bytes)));
                }
                var manifest = new RemoteContentManifest("Coral_Rescue", RemotePlatform.Windows, RemoteChannel.Development,
                    "m11-native-playmode", "m11-playmode-key", new EngineVersion(1, 0, 0), new EngineVersion(1, 1, 0), 1000, 9000, entries);
                var signature = rsa.SignData(manifest.CanonicalBytes(), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                var stage = RemoteDownloadCoordinator.FetchStageAsync(manifest, signature, new[] { key },
                    new RemoteReadOrigin("https://signed.invalid/m11-native/"), transport,
                    manifest.GameId, manifest.Platform, manifest.Channel, new EngineVersion(1, 0, 0), 2000, CancellationToken.None);
                while (!stage.IsCompleted) yield return null;
                Assert.That(stage.IsFaulted, Is.False, stage.Exception == null ? "Signed staging failed." : stage.Exception.ToString());
                Assert.That(transport.Calls, Is.EqualTo(entries.Count), "Only signed listed fixture bytes may be read.");
                approved = RemoteVerifiedAddressablesPayload.Prepare(stage.Result, CatalogPath, bundlePaths, manifest.GameId, RemotePlatform.Windows);
                Assert.That(approved.ReadVerifiedBytes(CatalogPath), Is.EqualTo(catalogBytes));
                foreach (var path in bundlePaths) Assert.That(approved.ReadVerifiedBytes(path), Is.EqualTo(files[path]));
            var initialization = Addressables.InitializeAsync(false);
            yield return initialization;
            Assert.That(initialization.Status, Is.EqualTo(UnityEngine.ResourceManagement.AsyncOperations.AsyncOperationStatus.Succeeded));
            Addressables.Release(initialization);
            var baselineLocators = Addressables.ResourceLocators.ToArray();
            var baselineLocatorCount = baselineLocators.Length;
            for (var pass = 1; pass <= 2; pass++)
            {
                var operation = NeutralOfficialAddressablesConsumer.ReadSignedTextAssetAsync(
                    approved, "Coral_Rescue", AssetAddress, CancellationToken.None);
                var frames = 0;
                while (!operation.IsCompleted && frames++ < 1200) yield return null;
                Assert.That(operation.IsCompleted, Is.True, "Native Addressables operation did not complete; pass=" + pass + " frames=" + frames);
                if (operation.IsFaulted) Assert.Fail("Native Addressables pass " + pass + " failed: " + operation.Exception);
                Assert.That(operation.Result, Is.EqualTo(Expected), "Native TextAsset bytes differ from the signed source.");
                Assert.That(Addressables.ResourceLocators.Count(), Is.EqualTo(baselineLocatorCount), "Temporary catalog locator leaked after pass " + pass);
                Assert.That(Addressables.InternalIdTransformFunc, Is.Null,
                    "The signed catalog transport transform must be restored after pass " + pass);
            }
            Assert.That(transport.Calls, Is.EqualTo(files.Count), "Signed staging must read exactly the catalog and enumerated bundles.");

            // The genuine catalog is modified to point at a bundle absent from
            // the signed receipt, then the modified catalog itself is signed
            // and staged. Native locator resolution must reject its dependency
            // before Addressables can issue a request for the unlisted URL.
            var catalogText = Encoding.UTF8.GetString(catalogBytes);
            var bundleMarker = catalogText.IndexOf(".bundle", StringComparison.Ordinal);
            Assert.That(bundleMarker, Is.GreaterThan(0), "The built catalog must contain a bundle internal ID.");
            var quotedStart = catalogText.LastIndexOf('"', bundleMarker);
            var quotedEnd = catalogText.IndexOf('"', bundleMarker);
            Assert.That(quotedStart, Is.GreaterThanOrEqualTo(0));
            Assert.That(quotedEnd, Is.GreaterThan(quotedStart));
            var oldInternalId = catalogText.Substring(quotedStart + 1, quotedEnd - quotedStart - 1);
            Assert.That(catalogText.IndexOf(oldInternalId, quotedEnd + 1, StringComparison.Ordinal), Is.EqualTo(-1),
                "The adversarial fixture expects one unambiguous bundle dependency.");
            const string unsignedRemote = "http://127.0.0.1:9/m11-unsigned.bundle";
            var maliciousCatalogText = catalogText.Substring(0, quotedStart + 1) + unsignedRemote +
                catalogText.Substring(quotedEnd);
            var maliciousCatalog = Encoding.UTF8.GetBytes(maliciousCatalogText);
            var maliciousFiles = new Dictionary<string, byte[]>(StringComparer.Ordinal)
            {
                [CatalogPath] = maliciousCatalog
            };
            foreach (var path in bundlePaths) maliciousFiles.Add(path, files[path]);
            var maliciousEntries = new List<RemoteContentEntry>
            {
                new RemoteContentEntry(CatalogPath, RemoteContentKind.AddressablesCatalog,
                    maliciousCatalog.Length, RemoteContentSafety.Sha256(maliciousCatalog))
            };
            foreach (var path in bundlePaths)
            {
                var bytes = maliciousFiles[path];
                maliciousEntries.Add(new RemoteContentEntry(path,
                    RemoteContentKind.AddressablesBundle, bytes.Length,
                    RemoteContentSafety.Sha256(bytes)));
            }
            var maliciousManifest = new RemoteContentManifest("Coral_Rescue",
                RemotePlatform.Windows, RemoteChannel.Development,
                "m11-native-unlisted-bundle", "m11-playmode-key",
                new EngineVersion(1, 0, 0), new EngineVersion(1, 1, 0),
                1000, 9000, maliciousEntries);
            var maliciousSignature = rsa.SignData(maliciousManifest.CanonicalBytes(),
                HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            var maliciousTransport = new BuiltContentTransport(maliciousFiles);
            var maliciousStage = RemoteDownloadCoordinator.FetchStageAsync(
                maliciousManifest, maliciousSignature, new[] { key },
                new RemoteReadOrigin("https://signed.invalid/m11-native/"),
                maliciousTransport, maliciousManifest.GameId,
                maliciousManifest.Platform, maliciousManifest.Channel,
                new EngineVersion(1, 0, 0), 2000, CancellationToken.None);
            while (!maliciousStage.IsCompleted) yield return null;
            Assert.That(maliciousStage.IsFaulted, Is.False,
                "A correctly signed catalog with an unlisted dependency must reach native validation.");
            Assert.That(maliciousTransport.Calls, Is.EqualTo(maliciousEntries.Count));
            var maliciousApproved = RemoteVerifiedAddressablesPayload.Prepare(
                maliciousStage.Result, CatalogPath, bundlePaths,
                maliciousManifest.GameId, RemotePlatform.Windows);

            var previousOverride = Addressables.WebRequestOverride;
            var unsignedHttpRequests = 0;
            Action<UnityWebRequest> observer = request =>
            {
                if (Uri.TryCreate(request.url, UriKind.Absolute, out var requestUri) &&
                    (requestUri.Scheme == Uri.UriSchemeHttp ||
                     requestUri.Scheme == Uri.UriSchemeHttps))
                    unsignedHttpRequests++;
                previousOverride?.Invoke(request);
            };
            Addressables.WebRequestOverride = observer;
            try
            {
                var rejected = NeutralOfficialAddressablesConsumer.ReadSignedTextAssetAsync(
                    maliciousApproved, "Coral_Rescue", AssetAddress,
                    CancellationToken.None);
                var frames = 0;
                while (!rejected.IsCompleted && frames++ < 1200) yield return null;
                Assert.That(rejected.IsCompleted, Is.True,
                    "Unlisted bundle validation did not complete.");
                Assert.That(rejected.IsFaulted, Is.True,
                    "The native Addressables adapter accepted an unlisted bundle.");
                Assert.That(rejected.Exception.ToString(), Does.Contain("unsigned bundle"));
                Assert.That(unsignedHttpRequests, Is.EqualTo(0),
                    "No unsigned HTTP(S) request may be issued for an unlisted dependency.");
            }
            finally
            {
                if (ReferenceEquals(Addressables.WebRequestOverride, observer))
                    Addressables.WebRequestOverride = previousOverride;
            }
            Assert.That(Addressables.ResourceLocators.Count(), Is.EqualTo(baselineLocatorCount),
                "Rejected secondary catalog left a locator behind.");
            Assert.That(Addressables.ResourceLocators, Is.EquivalentTo(baselineLocators),
                "Rejected secondary catalog replaced or retained a locator.");
            Assert.That(Addressables.InternalIdTransformFunc, Is.Null,
                "Rejected secondary catalog left the global transform installed.");
            Debug.Log("M11_008_NATIVE_ADDRESSABLES_PLAYMODE=PASS target=Windows64 catalog=UnityBuild bundle=UnityBuild RSA=PASS stage=PASS loads=2 exactTextAssetBytes=PASS unlistedBundleRejected=PASS unsignedNetworkGET=0 locatorSetRestored=1 transformRestored=1");
            }
        }

    }
}
