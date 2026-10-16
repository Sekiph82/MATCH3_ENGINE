using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Match3.Engine.Runtime;
using UnityEngine;

namespace Match3.Engine.Demo
{
    // Native Unity consumer for an already RSA-verified and SHA-staged bundle.
    // It does NOT treat a raw Addressables catalog, public URL, or arbitrary
    // CDN content as trusted. Only neutral TextAsset bytes are exposed here.
    // The official Addressables address graph remains a separate M11-008 gate.
    public static class NeutralVerifiedBundleTextConsumer
    {
        public static async Task<byte[]> ReadSignedTextAssetAsync(
            RemoteVerifiedAddressablesPayload approved,
            string expectedGameId, string signedBundlePath, string assetName,
            CancellationToken cancellation)
        {
            if (approved == null)
                throw new ArgumentNullException(nameof(approved));
            if (!GameProfileCatalog.IsKnownId(expectedGameId) ||
                approved.GameId != expectedGameId)
                throw new InvalidOperationException("Signed bundle game mismatch.");
            if (!RemoteContentSafety.IsSafePath(signedBundlePath) ||
                !approved.BundlePaths.Contains(signedBundlePath,
                    StringComparer.Ordinal))
                throw new InvalidOperationException(
                    "Bundle not listed in the complete signed staging receipt.");
            if (!RemoteContentSafety.IsSafePath(assetName))
                throw new ArgumentException("Invalid neutral asset address.",
                    nameof(assetName));
            cancellation.ThrowIfCancellationRequested();
            if (approved.Platform != CurrentPlatform())
                throw new InvalidOperationException(
                    "Cannot load a bundle signed for a different Unity player platform.");

            // Defensive copy is provided by the verified payload API;
            // its signature, content type, digest and source partition were
            // authenticated before this opt-in Unity adapter was invoked.
            var bytes = approved.ReadVerifiedBytes(signedBundlePath);
#if MATCH3_NATIVE_ASSETBUNDLE
            AssetBundle bundle = null;
            try
            {
                var request = AssetBundle.LoadFromMemoryAsync(bytes);
                if (request == null)
                    throw new InvalidOperationException(
                        "Native AssetBundle creation was unavailable.");
                // Finish this native operation before cancellation cleanup,
                // so any successfully allocated bundle can be unloaded.
                while (!request.isDone) await Task.Yield();
                bundle = request.assetBundle;
                cancellation.ThrowIfCancellationRequested();
                if (bundle == null)
                    throw new InvalidOperationException(
                        "Signed bytes were not a loadable bundle for this platform.");
                var assetRequest = bundle.LoadAssetAsync<TextAsset>(assetName);
                while (!assetRequest.isDone) await Task.Yield();
                cancellation.ThrowIfCancellationRequested();
                var text = assetRequest.asset as TextAsset;
                if (text == null || text.bytes == null ||
                    text.bytes.Length == 0 ||
                    text.bytes.LongLength > RemoteContentSafety.FileByteLimit)
                    throw new InvalidOperationException(
                        "Expected signed neutral TextAsset missing or over budget.");
                return (byte[])text.bytes.Clone();
            }
            finally
            {
                if (bundle != null) bundle.Unload(true);
            }
#else
            // The signed bytes are valid, but the Unity native bundle module
            // was not included in this build. The only safe outcome is to
            // refuse load, never reinterpret a fake bundle as usable data.
            throw new PlatformNotSupportedException(
                "Native signed AssetBundle consumer requires the Unity AssetBundle module.");
#endif
        }

        private static RemotePlatform CurrentPlatform()
        {
            switch (Application.platform)
            {
                case RuntimePlatform.WindowsEditor:
                case RuntimePlatform.WindowsPlayer:
                    return RemotePlatform.Windows;
                case RuntimePlatform.Android:
                    return RemotePlatform.Android;
                case RuntimePlatform.IPhonePlayer:
                    return RemotePlatform.Ios;
                default:
                    throw new PlatformNotSupportedException(
                        "Signed bundles require a supported real player OS.");
            }
        }
    }
}
