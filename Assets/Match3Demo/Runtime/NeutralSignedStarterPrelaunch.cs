using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Match3.Engine.Runtime;

namespace Match3.Engine.Demo
{
    // Optional player integration. A packaged starter or durable last-known-
    // good release is allowed to unlock play ONLY after public-key/byte checks.
    // No CI, cloud keys, reward, store, or arbitrary remote code dependency.
    public static class NeutralSignedStarterPrelaunch
    {
        public static void BindVerifiedOffline(
            NeutralPatchBootstrapView view,
            RemoteVerifiedRelease verifiedRelease,
            IEnumerable<RemotePublicSigningKey> trustedPublicKeys,
            string gameId, RemotePlatform platform, RemoteChannel channel,
            EngineVersion engine, long nowUnixSeconds)
        {
            if (view == null) throw new ArgumentNullException(nameof(view));
            if (verifiedRelease == null)
                throw new ArgumentNullException(nameof(verifiedRelease));
            if (trustedPublicKeys == null)
                throw new ArgumentNullException(nameof(trustedPublicKeys));
            if (view.HasVerifiedSession)
                throw new InvalidOperationException(
                    "Cannot silently replace an existing signed prelaunch session.");
            var keys = trustedPublicKeys.Take(17).ToArray();
            if (keys.Length == 0 || keys.Length > 16 ||
                keys.Any(k => k == null))
                throw new ArgumentException("Invalid bounded public trust set.");

            // Re-authenticate the exact signed release, including title/OS/
            // channel, engine version and key validity at this moment.
            var flow = new RemotePatchFlow(verifiedRelease.Manifest,
                verifiedRelease.Signature, keys, gameId, platform, channel,
                engine, nowUnixSeconds);
            flow.RecordOfflineFallback(verifiedRelease, keys,
                engine, nowUnixSeconds);
            view.Bind(flow);
            if (!view.CanEnterGame ||
                flow.Snapshot().Stage != RemotePatchStage.Offline)
                throw new InvalidOperationException(
                    "Signed offline content never reached a playable stage.");
        }

        public static async Task<bool> TryBindAppPackagedStarterAsync(
            NeutralPatchBootstrapView view,
            string gameId, RemotePlatform platform, RemoteChannel channel,
            EngineVersion engine,
            IEnumerable<RemotePublicSigningKey> trustedPublicKeys,
            long nowUnixSeconds, CancellationToken cancellation)
        {
            if (view == null) throw new ArgumentNullException(nameof(view));
            if (trustedPublicKeys == null)
                throw new ArgumentNullException(nameof(trustedPublicKeys));
            if (view.HasVerifiedSession)
                throw new InvalidOperationException(
                    "A signed prelaunch session was already bound.");
            var keys = trustedPublicKeys.Take(17).ToArray();
            if (keys.Length == 0 || keys.Length > 16 ||
                keys.Any(k => k == null))
                throw new ArgumentException("Invalid bounded public trust set.");
            cancellation.ThrowIfCancellationRequested();
            try
            {
                // Uses only Unity's own StreamingAssets root, and verifies
                // signature BEFORE reading any signed payload paths.
                var release = await NeutralUnityStreamingStarterLoader.LoadAsync(
                    gameId, platform, channel, engine, keys,
                    nowUnixSeconds, cancellation);
                cancellation.ThrowIfCancellationRequested();
                BindVerifiedOffline(view, release, keys, gameId,
                    platform, channel, engine, nowUnixSeconds);
                return true;
            }
            catch (InvalidOperationException)
            {
                // Do not leak exception text, sensitive file paths or URLs.
                // No unsigned or missing packaged fallback may unlock a CTA.
                if (!view.HasVerifiedSession)
                    view.ShowUnavailable("no-signed-starter");
                return false;
            }
        }
    }
}
