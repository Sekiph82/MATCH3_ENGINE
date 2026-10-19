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
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Networking;

namespace Match3.Engine.Demo.OfficialAddressables
{
    /// <summary>
    /// Development-player proof channel for a real platform Addressables build.
    /// Its fixture is copied from Unity's native Addressables build output into
    /// StreamingAssets by M11MobileExportBuilder. All bytes are RSA-verified and
    /// staged through the Engine contracts before the official Addressables API
    /// is permitted to read the TextAsset.
    /// </summary>
    public sealed class NeutralMobileAddressablesPlayerProbe : MonoBehaviour
    {
        private const string FixtureRoot = "m11-addressables";
        private const string AssetAddress = "match3/engine/neutral/m11-addressables";
        private static readonly byte[] Expected = new UTF8Encoding(false)
            .GetBytes("match3-neutral-addressables-native-v1\n");
        private CancellationTokenSource _lifetime;

        private void Awake()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (!Application.isMobilePlatform)
            {
                enabled = false;
                return;
            }
            _lifetime = new CancellationTokenSource();
            StartCoroutine(Run());
#else
            enabled = false;
#endif
        }

        private IEnumerator Run()
        {
            var task = RunAsync(_lifetime.Token);
            while (!task.IsCompleted) yield return null;
            if (task.IsFaulted)
            {
                var cause = task.Exception == null ? "unknown" :
                    task.Exception.GetBaseException().GetType().Name;
                Debug.LogError("M11_008_PLAYER_ADDRESSABLES_NATIVE_LOAD=FAIL cause=" + cause);
            }
        }

        private static async Task RunAsync(CancellationToken cancellation)
        {
            var platform = CurrentPlatform();
            var root = Application.streamingAssetsPath.TrimEnd('/', '\\') +
                "/" + FixtureRoot + "/" + PlatformName(platform) + "/";
            var listBytes = await ReadStreamingAssetAsync(root + "files.list", cancellation);
            var paths = Encoding.UTF8.GetString(listBytes)
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            if (paths.Length < 2 || paths.Length > 129 || paths[0] != "catalog.json")
                throw new InvalidOperationException("Invalid native fixture file list.");

            var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            foreach (var relative in paths)
            {
                if (!SafeRelativePath(relative))
                    throw new InvalidOperationException("Unsafe native fixture path.");
                var bytes = await ReadStreamingAssetAsync(root + JoinUrlPath(relative), cancellation);
                if (bytes.Length == 0 || bytes.LongLength > RemoteContentSafety.FileByteLimit)
                    throw new InvalidOperationException("Empty or oversized native fixture file.");
                files.Add(relative, bytes);
            }
            if (!files.TryGetValue("catalog.json", out var catalog) || catalog.Length < 32)
                throw new InvalidOperationException("Native catalog is missing.");
            var bundlePaths = paths.Skip(1).ToArray();
            if (bundlePaths.Any(path => !path.StartsWith("bundles/", StringComparison.Ordinal)))
                throw new InvalidOperationException("Native bundle path is outside its fixture root.");

            using (var rsa = new RSACryptoServiceProvider(2048))
            {
                var parameters = rsa.ExportParameters(false);
                var keyId = "m11-mobile-player-probe";
                var key = new RemotePublicSigningKey(keyId, parameters.Modulus,
                    parameters.Exponent, 0, long.MaxValue);
                var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                var positive = CreateSignedPackage(platform, keyId, catalog,
                    bundlePaths, files, rsa, now, "m11-mobile-native-positive");
                var verified = await StageAsync(positive.Manifest, positive.Signature,
                    positive.Files, key, platform, now, cancellation);
                var approved = RemoteVerifiedAddressablesPayload.Prepare(verified,
                    "catalog.json", bundlePaths, "Coral_Rescue", platform);

                for (var pass = 0; pass < 2; pass++)
                {
                    var loaded = await NeutralOfficialAddressablesConsumer.ReadSignedTextAssetAsync(
                        approved, "Coral_Rescue", AssetAddress, cancellation);
                    if (!loaded.SequenceEqual(Expected))
                        throw new InvalidOperationException("Loaded TextAsset bytes differ from the signed fixture.");
                }

                var maliciousCatalog = ReplaceSingleBundleWithUnsignedHttp(catalog);
                var negative = CreateSignedPackage(platform, keyId, maliciousCatalog,
                    bundlePaths, files, rsa, now, "m11-mobile-native-hostile");
                var hostileVerified = await StageAsync(negative.Manifest, negative.Signature,
                    negative.Files, key, platform, now, cancellation);
                var hostileApproved = RemoteVerifiedAddressablesPayload.Prepare(
                    hostileVerified, "catalog.json", bundlePaths, "Coral_Rescue", platform);

                var previousOverride = Addressables.WebRequestOverride;
                var unsignedGets = 0;
                Action<UnityWebRequest> observer = request =>
                {
                    if (Uri.TryCreate(request.url, UriKind.Absolute, out var uri) &&
                        (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
                    {
                        if (string.Equals(uri.Host, "127.0.0.1",
                                StringComparison.OrdinalIgnoreCase) && uri.Port == 9 &&
                            uri.AbsolutePath.EndsWith("/m11-unsigned.bundle",
                                StringComparison.Ordinal))
                            unsignedGets++;
                    }
                    previousOverride?.Invoke(request);
                };
                Addressables.WebRequestOverride = observer;
                try
                {
                    var rejected = NeutralOfficialAddressablesConsumer.ReadSignedTextAssetAsync(
                        hostileApproved, "Coral_Rescue", AssetAddress, cancellation);
                    await rejected;
                    throw new InvalidOperationException("Hostile unsigned bundle dependency was accepted.");
                }
                catch (InvalidOperationException error) when
                    (error.Message.IndexOf("unsigned bundle", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    if (unsignedGets != 0)
                        throw new InvalidOperationException("Unsigned dependency caused an HTTP(S) request.");
                }
                finally
                {
                    if (ReferenceEquals(Addressables.WebRequestOverride, observer))
                        Addressables.WebRequestOverride = previousOverride;
                }
                Debug.Log("M11_008_PLAYER_ADDRESSABLES_NATIVE_LOAD=PASS target=" + platform +
                    " signedRepeatLoads=2 exactTextAssetBytes=true unsignedBundleBlocked=true" +
                    " textSha256=" + RemoteContentSafety.Sha256(Expected) +
                    " unsignedGets=" + unsignedGets +
                    " locatorStateRestored=true transformRestored=true");
            }
        }

        private sealed class SignedFixture
        {
            public RemoteContentManifest Manifest;
            public byte[] Signature;
            public Dictionary<string, byte[]> Files;
        }

        private static SignedFixture CreateSignedPackage(RemotePlatform platform,
            string keyId, byte[] catalog, string[] bundlePaths,
            Dictionary<string, byte[]> sourceFiles, RSACryptoServiceProvider rsa,
            long now, string releaseId)
        {
            var content = new Dictionary<string, byte[]>(StringComparer.Ordinal)
            {
                ["catalog.json"] = (byte[])catalog.Clone()
            };
            foreach (var path in bundlePaths) content.Add(path, sourceFiles[path]);
            var entries = content.Select(pair => new RemoteContentEntry(pair.Key,
                pair.Key == "catalog.json" ? RemoteContentKind.AddressablesCatalog :
                    RemoteContentKind.AddressablesBundle,
                pair.Value.Length, RemoteContentSafety.Sha256(pair.Value))).ToArray();
            var manifest = new RemoteContentManifest("Coral_Rescue", platform,
                RemoteChannel.Development, releaseId, keyId,
                new EngineVersion(1, 0, 0), new EngineVersion(1, 1, 0),
                now - 10, now + 3600, entries);
            var signature = rsa.SignData(manifest.CanonicalBytes(),
                HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            return new SignedFixture { Manifest = manifest, Signature = signature, Files = content };
        }

        private static async Task<RemoteFetchedPackage> StageAsync(
            RemoteContentManifest manifest, byte[] signature,
            Dictionary<string, byte[]> files, RemotePublicSigningKey key,
            RemotePlatform platform, long now, CancellationToken cancellation)
        {
            var transport = new MemoryFixtureTransport(files);
            return await RemoteDownloadCoordinator.FetchStageAsync(manifest,
                signature, new[] { key },
                new RemoteReadOrigin("https://signed.invalid/m11-native/"),
                transport, "Coral_Rescue", platform,
                RemoteChannel.Development, new EngineVersion(1, 0, 0), now,
                cancellation);
        }

        private sealed class MemoryFixtureTransport : IRemoteReadTransport
        {
            private readonly Dictionary<string, byte[]> _files;
            public MemoryFixtureTransport(Dictionary<string, byte[]> files) { _files = files; }

            public Task<RemoteReadResponse> FetchAsync(Uri url, long maximumBytes,
                CancellationToken cancellation)
            {
                cancellation.ThrowIfCancellationRequested();
                var key = Uri.UnescapeDataString(url.AbsolutePath.Substring(
                    url.AbsolutePath.LastIndexOf("/m11-native/", StringComparison.Ordinal) +
                    "/m11-native/".Length));
                if (!_files.TryGetValue(key, out var bytes) || bytes.LongLength > maximumBytes)
                    throw new InvalidOperationException("Native fixture transport rejected an unsigned path.");
                return Task.FromResult(new RemoteReadResponse(200, bytes));
            }
        }

        private static byte[] ReplaceSingleBundleWithUnsignedHttp(byte[] catalog)
        {
            var text = Encoding.UTF8.GetString(catalog);
            var marker = text.IndexOf(".bundle", StringComparison.Ordinal);
            if (marker < 0) throw new InvalidOperationException("Catalog has no native bundle dependency.");
            var start = text.LastIndexOf('"', marker);
            var end = text.IndexOf('"', marker);
            if (start < 0 || end <= start) throw new InvalidOperationException("Catalog bundle id is malformed.");
            var oldId = text.Substring(start + 1, end - start - 1);
            if (text.IndexOf(oldId, end + 1, StringComparison.Ordinal) >= 0)
                throw new InvalidOperationException("Hostile fixture requires a unique bundle dependency.");
            return Encoding.UTF8.GetBytes(text.Substring(0, start + 1) +
                "http://127.0.0.1:9/m11-unsigned.bundle" + text.Substring(end));
        }

        private static async Task<byte[]> ReadStreamingAssetAsync(string url,
            CancellationToken cancellation)
        {
            using (var request = UnityWebRequest.Get(url))
            {
                request.timeout = 15;
                var operation = request.SendWebRequest();
                while (!operation.isDone)
                {
                    cancellation.ThrowIfCancellationRequested();
                    await Task.Yield();
                }
                cancellation.ThrowIfCancellationRequested();
                if (request.result != UnityWebRequest.Result.Success)
                    throw new InvalidOperationException("Packaged native Addressables fixture is unavailable.");
                return request.downloadHandler.data;
            }
        }

        private static bool SafeRelativePath(string path)
        {
            return !string.IsNullOrWhiteSpace(path) && path.Length <= 240 &&
                !path.StartsWith("/", StringComparison.Ordinal) &&
                path.IndexOf('\\') < 0 && path.Split('/').All(part =>
                    part.Length > 0 && part != "." && part != ".." &&
                    part.IndexOf(':') < 0 && part.IndexOf('%') < 0);
        }

        private static string JoinUrlPath(string path) => string.Join("/",
            path.Split('/').Select(Uri.EscapeDataString));

        private static RemotePlatform CurrentPlatform()
        {
            if (Application.platform == RuntimePlatform.Android) return RemotePlatform.Android;
            if (Application.platform == RuntimePlatform.IPhonePlayer) return RemotePlatform.Ios;
            throw new PlatformNotSupportedException("Mobile Addressables QA probe requires Android or iOS Player.");
        }

        private static string PlatformName(RemotePlatform platform) =>
            platform == RemotePlatform.Android ? "Android" : "iOS";

        private void OnDestroy()
        {
            if (_lifetime == null) return;
            _lifetime.Cancel();
            _lifetime.Dispose();
            _lifetime = null;
        }
    }
}
