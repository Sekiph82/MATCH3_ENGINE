using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Match3.Engine.Runtime;
using UnityEngine;
using UnityEngine.Networking;

namespace Match3.Engine.Demo
{
    // Real Unity streaming-assets adapter. The ONLY read root is Unity's
    // application-owned StreamingAssets; this does not fetch arbitrary URLs,
    // execute code, grant rewards, embed credentials or import game graphics.
    // Supports Android jar:file:// and Windows/iOS local streaming assets.
    public static class NeutralUnityStreamingStarterLoader
    {
        public static async Task<RemoteVerifiedRelease> LoadAsync(
            string gameId, RemotePlatform platform,
            RemoteChannel channel, EngineVersion engine,
            IEnumerable<RemotePublicSigningKey> pinnedKeys,
            long unixNowSeconds, CancellationToken cancellation)
        {
            if (!GameProfileCatalog.IsKnownId(gameId) ||
                !Enum.IsDefined(typeof(RemotePlatform), platform) ||
                !Enum.IsDefined(typeof(RemoteChannel), channel))
                throw new ArgumentException("Invalid app-owned starter partition.");
            if (pinnedKeys == null) throw new ArgumentNullException(nameof(pinnedKeys));
            var keySet = pinnedKeys.Take(17).ToArray();
            if (keySet.Length == 0 || keySet.Length > 16 ||
                keySet.Any(x => x == null))
                throw new ArgumentException("Invalid pinned public key set.");
            cancellation.ThrowIfCancellationRequested();
            var basePath = "match3/" + gameId + "/" +
                (int)platform + "/" + (int)channel + "/";
            var canonical = await ReadAsync(basePath + "manifest.canonical",
                RemoteManifestCodec.MaximumCanonicalBytes, cancellation);
            var signature = await ReadAsync(basePath + "manifest.sig",
                1024, cancellation);
            // Never read attacker-declared file paths until signature,
            // target title/OS/channel/version and time window are validated.
            var manifest = RemoteManifestCodec.ParseVerified(
                canonical, signature, keySet, gameId, platform,
                channel, engine, unixNowSeconds);
            var data = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            foreach (var entry in manifest.Entries)
            {
                cancellation.ThrowIfCancellationRequested();
                var bytes = await ReadAsync(basePath + entry.Path,
                    entry.Length, cancellation);
                if (!RemoteContentSafety.Matches(bytes,
                    entry.Length, entry.Sha256))
                    throw new InvalidOperationException(
                        "Signed packaged starter digest or size mismatch.");
                RemoteContentSafety.RequireNonExecutable(bytes, entry.Kind);
                data.Add(entry.Path, bytes);
            }
            // Same strict Engine verifier as network and offline staging.
            return new RemotePackagedStarter(canonical, signature, data)
                .VerifyForOffline(keySet, gameId, platform, channel,
                    engine, unixNowSeconds);
        }

        private static async Task<byte[]> ReadAsync(string path,
            long byteLimit, CancellationToken cancellation)
        {
            if (!RemoteContentSafety.IsSafePath(path) ||
                byteLimit < 1 || byteLimit > RemoteContentSafety.FileByteLimit)
                throw new ArgumentException("Invalid trusted packaged path/budget.");
            var root = Application.streamingAssetsPath;
            if (string.IsNullOrWhiteSpace(root))
                throw new InvalidOperationException("Unity streaming assets root missing.");
            var prefix = root.Contains("://") || root.StartsWith("jar:file:",
                StringComparison.OrdinalIgnoreCase) ? root.TrimEnd('/') + "/" :
                new Uri(Path.GetFullPath(root).TrimEnd(
                    Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar).AbsoluteUri;
            var url = prefix + string.Join("/",
                path.Split('/').Select(Uri.EscapeDataString));
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
                    throw new InvalidOperationException(
                        "Packaged starter missing/unavailable.");
                var bytes = request.downloadHandler.data;
                if (bytes == null || bytes.Length == 0 ||
                    bytes.LongLength > byteLimit)
                    throw new InvalidOperationException(
                        "Packaged starter exceeds signed byte budget.");
                return bytes;
            }
        }
    }
}
