using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Match3.Engine.Runtime
{
    // The optional Unity Addressables adapter must receive bytes only from a
    // complete RSA-authenticated, SHA-verified staging receipt. A raw catalog
    // path or untrusted CDN URL never authorizes a bundle to be loaded.
    // This type does not import the Addressables package into the Engine.
    public sealed class RemoteVerifiedAddressablesPayload
    {
        private readonly Dictionary<string, byte[]> _content;
        private readonly ReadOnlyCollection<string> _bundlePaths;

        public string GameId { get; }
        public RemotePlatform Platform { get; }
        public string ReleaseDigest { get; }
        public string CatalogPath { get; }
        public IReadOnlyList<string> BundlePaths => _bundlePaths;

        private RemoteVerifiedAddressablesPayload(RemoteFetchedPackage package,
            RemoteAddressablesPlan plan, IReadOnlyList<string> bundlePaths,
            Dictionary<string, byte[]> bytes)
        {
            GameId = plan.GameId;
            Platform = plan.Platform;
            CatalogPath = plan.CatalogPath;
            ReleaseDigest = package.Verified.SignedEnvelopeDigest;
            _bundlePaths = new ReadOnlyCollection<string>(
                bundlePaths.ToArray());
            _content = bytes;
        }

        public static RemoteVerifiedAddressablesPayload Prepare(
            RemoteFetchedPackage verifiedPackage,
            string catalogPath, IEnumerable<string> bundlePaths,
            string expectedGame, RemotePlatform expectedPlatform)
        {
            if (verifiedPackage == null)
                throw new ArgumentNullException(nameof(verifiedPackage));
            if (bundlePaths == null)
                throw new ArgumentNullException(nameof(bundlePaths));

            // The planner rejects cross-title, cross-platform, missing and
            // incorrectly typed catalog or bundle references.
            var requested = bundlePaths.Take(129).ToArray();
            if (requested.Length == 0 || requested.Length > 128)
                throw new ArgumentException("One to 128 signed bundles required.");
            var plan = RemoteAddressablesPlanner.Resolve(
                verifiedPackage.Verified, catalogPath, requested,
                expectedGame, expectedPlatform);

            var manifest = verifiedPackage.Verified.Manifest;
            var content = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            foreach (var path in new[] { plan.CatalogPath }.Concat(plan.BundlePaths))
            {
                var entry = manifest.Entries.Single(x => x.Path == path);
                var bytes = verifiedPackage.GetVerifiedBytes(path);
                // Rehash immediately before exposure, not just at download.
                if (!RemoteContentSafety.Matches(bytes, entry.Length,
                    entry.Sha256))
                    throw new InvalidOperationException(
                        "Addressables content changed after signed staging.");
                RemoteContentSafety.RequireNonExecutable(bytes, entry.Kind);
                content.Add(path, bytes);
            }
            return new RemoteVerifiedAddressablesPayload(
                verifiedPackage, plan, requested, content);
        }

        // Defensive copy: an Addressables consumer cannot corrupt its
        // subsequent read or mutate an already-approved payload.
        public byte[] ReadVerifiedBytes(string signedPath)
        {
            byte[] content;
            if (signedPath == null || !_content.TryGetValue(signedPath, out content))
                throw new InvalidOperationException(
                    "Unsigned Addressables path cannot be read.");
            return (byte[])content.Clone();
        }
    }
}
