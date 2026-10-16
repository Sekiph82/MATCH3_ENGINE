using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Match3.Engine.Runtime;

namespace Match3.Engine.Demo
{
    // Signed update discovery for a FIRST-PARTY pinned HTTPS base URL.
    // The manifest and detached signature are retrieved as untrusted bytes.
    // Neither can provide a URL, redirect, signing key or trust authority.
    // Only an application-owned immutable public trust set may authorize
    // the candidate returned to the signed prelaunch host.
    public static class NeutralPinnedHttpsManifestReader
    {
        public const string ManifestPath = "manifest.canonical";
        public const string SignaturePath = "manifest.sig";
        public const int MinimumSignatureBytes = 256;
        public const int MaximumSignatureBytes = 1024;

        public static async Task<RemoteContentManifest> FetchVerifiedAsync(
            RemoteReadOrigin pinnedOrigin, IRemoteReadTransport transport,
            IEnumerable<RemotePublicSigningKey> pinnedPublicKeys,
            string gameId, RemotePlatform platform, RemoteChannel channel,
            EngineVersion engine, long nowUnixSeconds,
            Action<byte[]> onVerifiedSignature,
            CancellationToken cancellation)
        {
            if (pinnedOrigin == null) throw new ArgumentNullException(nameof(pinnedOrigin));
            if (transport == null) throw new ArgumentNullException(nameof(transport));
            if (pinnedPublicKeys == null)
                throw new ArgumentNullException(nameof(pinnedPublicKeys));
            if (onVerifiedSignature == null)
                throw new ArgumentNullException(nameof(onVerifiedSignature));
            var keys = pinnedPublicKeys.Take(17).ToArray();
            if (keys.Length == 0 || keys.Length > 16 || keys.Any(x => x == null))
                throw new ArgumentException("A bounded first-party public trust set is required.");

            cancellation.ThrowIfCancellationRequested();
            // Date validity may only be tightened by the actual current
            // clock. An untrusted caller must never extend key validity by
            // providing an old timestamp.
            var verifiedAt = Math.Max(nowUnixSeconds,
                DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            var manifest = await transport.FetchAsync(
                pinnedOrigin.ForPath(ManifestPath),
                RemoteManifestCodec.MaximumCanonicalBytes,
                cancellation).ConfigureAwait(false);
            cancellation.ThrowIfCancellationRequested();
            if (manifest == null || manifest.HttpStatus != 200)
                throw new InvalidOperationException("Signed candidate manifest unavailable.");
            var canonical = manifest.Bytes;
            if (canonical.Length < 24 ||
                canonical.Length > RemoteManifestCodec.MaximumCanonicalBytes)
                throw new InvalidOperationException("Invalid signed manifest byte limit.");

            // The signature is an independent fixed path. Its bytes NEVER
            // come from an unsigned manifest field or redirected origin.
            var signature = await transport.FetchAsync(
                pinnedOrigin.ForPath(SignaturePath), MaximumSignatureBytes,
                cancellation).ConfigureAwait(false);
            cancellation.ThrowIfCancellationRequested();
            if (signature == null || signature.HttpStatus != 200)
                throw new InvalidOperationException("Detached signature unavailable.");
            var detached = signature.Bytes;
            if (detached.Length < MinimumSignatureBytes ||
                detached.Length > MaximumSignatureBytes)
                throw new InvalidOperationException("Invalid detached signature size.");

            // No remote content/payload requests occur until the exact
            // canonical manifest has passed signature, game, platform,
            // channel, engine compatibility and clock validity checks.
            var verified = RemoteManifestCodec.ParseVerified(
                canonical, detached, keys, gameId, platform, channel,
                engine, verifiedAt);
            // Do not leak the signature until validation finishes. Caller
            // takes ownership of this fresh clone; no raw HTTP body reuse.
            onVerifiedSignature((byte[])detached.Clone());
            return verified;
        }
    }
}
