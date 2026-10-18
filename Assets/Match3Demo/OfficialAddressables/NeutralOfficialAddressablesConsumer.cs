using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Match3.Engine.Runtime;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.AddressableAssets.ResourceLocators;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceLocations;
using UnityEngine.ResourceManagement.ResourceProviders;

namespace Match3.Engine.Demo.OfficialAddressables
{
    // Optional typed adapter, compiled ONLY when official com.unity.addressables
    // is installed. No UPM install, implicit network request or bypass of the
    // existing signed Engine verifier. The caller must supply a FULLY staged
    // RemoteVerifiedAddressablesPayload, never raw catalog bytes or a URL.
    //
    // The secondary catalog is loaded from app-private, verified local files.
    // Its bundle references are redirected to the EXACT approved staged bytes.
    // A temporary locator is removed after the load, so it cannot later
    // resolve a network address without our signed-content firewall.
    public static class NeutralOfficialAddressablesConsumer
    {
        private static readonly SemaphoreSlim OwnOperations = new SemaphoreSlim(1, 1);

        public static async Task<byte[]> ReadSignedTextAssetAsync(
            RemoteVerifiedAddressablesPayload approved,
            string expectedGameId, string assetAddress,
            CancellationToken cancellation)
        {
            if (approved == null) throw new ArgumentNullException(nameof(approved));
            if (!GameProfileCatalog.IsKnownId(expectedGameId) ||
                !string.Equals(approved.GameId, expectedGameId, StringComparison.Ordinal))
                throw new InvalidOperationException("Addressables title mismatch.");
            if (approved.Platform != CurrentPlatform())
                throw new InvalidOperationException("Addressables signed platform mismatch.");
            if (string.IsNullOrWhiteSpace(assetAddress) ||
                assetAddress.Length > 240 ||
                !RemoteContentSafety.IsSafePath(assetAddress))
                throw new ArgumentException("Unsafe Addressables TextAsset address.");
            if (!IsDigest(approved.ReleaseDigest))
                throw new InvalidOperationException("Invalid authenticated release digest.");
            cancellation.ThrowIfCancellationRequested();

            // Addressables exposes a global InternalIdTransformFunc; refuse
            // interfering with another title's transform or active load.
            await OwnOperations.WaitAsync(cancellation);
            try
            {
                var previousTransform = Addressables.InternalIdTransformFunc;
                if (previousTransform != null)
                    throw new InvalidOperationException(
                        "Another Addressables transport policy is already active.");
                var verifiedBundles = new Dictionary<string, string>(
                    StringComparer.OrdinalIgnoreCase);
                var releaseDir = PrivateReleaseDirectory(approved);
                var catalogFile = Materialize(releaseDir, approved.CatalogPath,
                    approved.ReadVerifiedBytes(approved.CatalogPath));
                foreach (var path in approved.BundlePaths)
                {
                    cancellation.ThrowIfCancellationRequested();
                    var leaf = path.Split('/').Last();
                    if (!verifiedBundles.TryAdd(leaf, Materialize(
                        releaseDir, path, approved.ReadVerifiedBytes(path))))
                        throw new InvalidOperationException(
                            "Ambiguous signed bundle names in Addressables catalog.");
                }

                // This hook is active BEFORE catalog load and is removed only
                // after EVERY asynchronous native operation has finished.
                // Unknown bundle locations never get a network request.
                Func<IResourceLocation, string> firewall = location =>
                {
                    if (location == null)
                        throw new InvalidOperationException("Unknown Addressables location.");
                    var id = location.InternalId ?? string.Empty;
                    if (IsBundleLocation(location))
                    {
                        var leaf = BundleLeafName(id);
                        if (!verifiedBundles.TryGetValue(leaf, out var signedFile))
                            throw new InvalidOperationException(
                                "Addressables bundle is not in the signed receipt.");
                        return new Uri(signedFile).AbsoluteUri;
                    }
                    if (id.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                        id.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException(
                            "Addressables may not fetch an unsigned remote location.");
                    return id;
                };
                Addressables.InternalIdTransformFunc = firewall;
                AsyncOperationHandle<IResourceLocator> catalog = default;
                AsyncOperationHandle<IResourceLocator> initialization = default;
                AsyncOperationHandle<TextAsset> asset = default;
                var catalogStarted = false;
                var initializationStarted = false;
                var assetStarted = false;
                IResourceLocator addedLocator = null;
                List<IResourceLocator> locatorsBeforeCatalog = null;
                var unexpectedTransform = false;
                try
                {
                    // Keep the handle alive while this Task yields back to the
                    // Unity synchronization context. The default overload
                    // auto-releases before an async continuation can inspect it.
                    initialization = Addressables.InitializeAsync(false);
                    initializationStarted = true;
                    while (!initialization.IsDone)
                    {
                        cancellation.ThrowIfCancellationRequested();
                        await Task.Yield();
                    }
                    if (initialization.Status != AsyncOperationStatus.Succeeded ||
                        initialization.Result == null)
                        throw new InvalidOperationException(
                            "Official Addressables initialization failed.");
                    // Addressables 2.x also appends a DynamicResourceLocator
                    // while loading a secondary catalog. Keep a snapshot so
                    // both it and the returned catalog locator can be removed
                    // after the authenticated asset load.
                    locatorsBeforeCatalog = Addressables.ResourceLocators.ToList();
                    catalog = Addressables.LoadContentCatalogAsync(
                        new Uri(catalogFile).AbsoluteUri, false);
                    catalogStarted = true;
                    while (!catalog.IsDone)
                    {
                        cancellation.ThrowIfCancellationRequested();
                        await Task.Yield();
                    }
                    if (catalog.Status != AsyncOperationStatus.Succeeded ||
                        catalog.Result == null)
                        throw new InvalidOperationException(
                            "Official Addressables rejected the verified catalog.");
                    addedLocator = catalog.Result;
                    cancellation.ThrowIfCancellationRequested();
                    if (!addedLocator.Locate(assetAddress, typeof(TextAsset),
                        out IList<IResourceLocation> locations) ||
                        locations == null || locations.Count != 1)
                        throw new InvalidOperationException(
                            "Signed catalog has no unique neutral TextAsset.");
                    var seen = new HashSet<IResourceLocation>();
                    var signedBundleDependencies = new HashSet<string>(
                        StringComparer.OrdinalIgnoreCase);
                    ValidateLocation(locations[0], verifiedBundles,
                        signedBundleDependencies, seen);
                    if (signedBundleDependencies.Count == 0)
                        throw new InvalidOperationException(
                            "TextAsset was not backed by a signed native bundle.");
                    asset = Addressables.LoadAssetAsync<TextAsset>(locations[0]);
                    assetStarted = true;
                    while (!asset.IsDone)
                    {
                        cancellation.ThrowIfCancellationRequested();
                        await Task.Yield();
                    }
                    cancellation.ThrowIfCancellationRequested();
                    if (asset.Status != AsyncOperationStatus.Succeeded ||
                        asset.Result == null || asset.Result.bytes == null ||
                        asset.Result.bytes.Length == 0 ||
                        asset.Result.bytes.LongLength > RemoteContentSafety.FileByteLimit)
                        throw new InvalidOperationException(
                            "Official Addressables failed to load signed TextAsset.");
                    return (byte[])asset.Result.bytes.Clone();
                }
                finally
                {
                    var unexpectedLocator = false;
                    // Removing this locator prevents later unsupervised loads
                    // against its original embedded catalog CDN locations.
                    if (assetStarted && asset.IsValid()) Addressables.Release(asset);
                    if (addedLocator != null)
                        Addressables.RemoveResourceLocator(addedLocator);
                    if (locatorsBeforeCatalog != null)
                    {
                        foreach (var locator in Addressables.ResourceLocators.ToList())
                        {
                            if (locatorsBeforeCatalog.Any(existing =>
                                ReferenceEquals(existing, locator)) ||
                                ReferenceEquals(addedLocator, locator))
                                continue;
                            // The only additional locator created by the
                            // Addressables secondary catalog operation is its
                            // generic dynamic lookup locator. Refuse to leave
                            // it behind or remove unrelated locators.
                            if (!string.Equals(locator.GetType().FullName,
                                "UnityEngine.AddressableAssets.DynamicResourceLocator",
                                StringComparison.Ordinal))
                            {
                                unexpectedLocator = true;
                                continue;
                            }
                            Addressables.RemoveResourceLocator(locator);
                        }
                    }
                    if (catalogStarted && catalog.IsValid())
                        Addressables.Release(catalog);
                    if (initializationStarted && initialization.IsValid())
                        Addressables.Release(initialization);
                    if (ReferenceEquals(Addressables.InternalIdTransformFunc, firewall))
                        Addressables.InternalIdTransformFunc = previousTransform;
                    else if (!ReferenceEquals(Addressables.InternalIdTransformFunc,
                        previousTransform))
                        unexpectedTransform = true;
                    if (unexpectedLocator || unexpectedTransform)
                        throw new InvalidOperationException(
                            "Addressables global state changed during signed catalog load.");
                }
            }
            finally { OwnOperations.Release(); }
        }

        private static void ValidateLocation(IResourceLocation location,
            Dictionary<string, string> approvedBundles, HashSet<string> used,
            HashSet<IResourceLocation> seen)
        {
            if (location == null || seen.Count > 256)
                throw new InvalidOperationException("Unsafe catalog dependency graph.");
            if (!seen.Add(location)) return; // Shared dependencies are normal.
            var id = location.InternalId ?? string.Empty;
            if (IsBundleLocation(location))
            {
                var name = BundleLeafName(id);
                if (!approvedBundles.ContainsKey(name))
                    throw new InvalidOperationException(
                        "Catalog references an unsigned bundle.");
                used.Add(name);
            }
            else if (id.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
                     id.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    "Catalog references unsigned remote data.");
            if (location.Dependencies != null)
                foreach (var dependency in location.Dependencies)
                    ValidateLocation(dependency, approvedBundles, used, seen);
        }

        private static bool IsBundleLocation(IResourceLocation location)
        {
            return location.ResourceType == typeof(IAssetBundleResource) ||
                (location.ProviderId ?? string.Empty).IndexOf(
                    "AssetBundleProvider", StringComparison.Ordinal) >= 0;
        }

        private static string BundleLeafName(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
                throw new InvalidOperationException("Empty bundle location.");
            var stripped = id.Split('?', '#')[0].Replace('\\', '/');
            var leaf = stripped.Substring(stripped.LastIndexOf('/') + 1);
            if (leaf.Length == 0 || leaf == "." || leaf == ".." ||
                leaf.IndexOf('%') >= 0 || leaf.IndexOf(':') >= 0)
                throw new InvalidOperationException("Unsafe bundle location.");
            return leaf;
        }

        private static string PrivateReleaseDirectory(
            RemoteVerifiedAddressablesPayload approved)
        {
            var app = Application.persistentDataPath;
            if (string.IsNullOrWhiteSpace(app) ||
                !Path.IsPathRooted(app) || !Directory.Exists(app))
                throw new InvalidOperationException(
                    "Application-private persistence is unavailable.");
            // Keep the authenticated envelope digest as the collision-resistant
            // release identity while avoiding Windows MAX_PATH when Addressables
            // bundle paths are materialized beneath the private cache.
            return Path.Combine(app, "m3a", approved.ReleaseDigest);
        }

        private static string Materialize(
            string releaseDir, string signedPath, byte[] signedBytes)
        {
            if (!RemoteContentSafety.IsSafePath(signedPath) ||
                signedBytes == null || signedBytes.Length < 1 ||
                signedBytes.LongLength > RemoteContentSafety.FileByteLimit)
                throw new InvalidOperationException("Invalid signed staging entry.");
            var fullRoot = Path.GetFullPath(releaseDir) +
                Path.DirectorySeparatorChar;
            var file = Path.GetFullPath(Path.Combine(releaseDir,
                signedPath.Replace('/', Path.DirectorySeparatorChar)));
            if (!file.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    "Signed staging must remain inside the private release.");
            Directory.CreateDirectory(Path.GetDirectoryName(file));
            if (File.Exists(file))
            {
                if (!File.ReadAllBytes(file).SequenceEqual(signedBytes))
                    throw new InvalidOperationException(
                        "Existing signed Addressables cache was altered.");
            }
            else
            {
                // CreateNew refuses replacement of pre-existing owner data.
                using (var stream = new FileStream(file, FileMode.CreateNew,
                    FileAccess.Write, FileShare.None))
                {
                    stream.Write(signedBytes, 0, signedBytes.Length);
                    stream.Flush(true);
                }
            }
            return file;
        }

        private static bool IsDigest(string s) =>
            s != null && s.Length == 64 && s.All(c =>
                (c >= '0' && c <= '9') ||
                (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F'));

        private static RemotePlatform CurrentPlatform()
        {
            switch (Application.platform)
            {
                case RuntimePlatform.WindowsEditor:
                case RuntimePlatform.WindowsPlayer: return RemotePlatform.Windows;
                case RuntimePlatform.Android: return RemotePlatform.Android;
                case RuntimePlatform.IPhonePlayer: return RemotePlatform.Ios;
                default: throw new PlatformNotSupportedException(
                    "Unsupported official Addressables player platform.");
            }
        }
    }
}
