using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Match3.Engine.Runtime
{
    // Neutral Unity Addressables binding contract. The installed title-specific
    // Unity consumer later resolves the signed catalog/bundle names. No images
    // or Addressables binary packages are embedded into the Engine assembly.
    public sealed class RemoteAddressablesPlan
    {
        private readonly ReadOnlyCollection<string> _bundlePaths;
        public string CatalogPath { get; }
        public IReadOnlyList<string> BundlePaths => _bundlePaths;
        public string GameId { get; }
        public RemotePlatform Platform { get; }
        public string ReleaseId { get; }

        internal RemoteAddressablesPlan(RemoteContentManifest manifest,
            string catalogPath, IEnumerable<string> bundlePaths)
        {
            CatalogPath = catalogPath;
            _bundlePaths = new ReadOnlyCollection<string>(bundlePaths.ToList());
            GameId = manifest.GameId;
            Platform = manifest.Platform;
            ReleaseId = manifest.ReleaseId;
        }
    }

    public static class RemoteAddressablesPlanner
    {
        public static RemoteAddressablesPlan Resolve(RemoteVerifiedRelease release,
            string catalogPath, IEnumerable<string> requestedBundlePaths,
            string expectedGameId, RemotePlatform expectedPlatform)
        {
            if (release == null) throw new ArgumentNullException(nameof(release));
            if (requestedBundlePaths == null)
                throw new ArgumentNullException(nameof(requestedBundlePaths));
            var m = release.Manifest;
            if (m.GameId != expectedGameId || m.Platform != expectedPlatform)
                throw new InvalidOperationException("Addressables title/platform mismatch.");
            if (!RemoteContentSafety.IsSafePath(catalogPath) ||
                !m.Entries.Any(x => x.Path == catalogPath &&
                    x.Kind == RemoteContentKind.AddressablesCatalog))
                throw new InvalidOperationException(
                    "Unsigned or invalid Addressables catalog binding.");
            var requested = requestedBundlePaths.Take(129).ToArray();
            if (requested.Length > 128 || requested.Any(x =>
                !RemoteContentSafety.IsSafePath(x)) ||
                requested.Distinct(StringComparer.Ordinal).Count() != requested.Length)
                throw new ArgumentException("Invalid Addressables bundle request.");
            foreach (var path in requested)
                if (!m.Entries.Any(x => x.Path == path &&
                    x.Kind == RemoteContentKind.AddressablesBundle))
                    throw new InvalidOperationException(
                        "Unapproved Addressables bundle reference.");
            return new RemoteAddressablesPlan(m, catalogPath, requested);
        }
    }
}
