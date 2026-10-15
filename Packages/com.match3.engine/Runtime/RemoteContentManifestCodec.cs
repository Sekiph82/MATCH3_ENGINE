using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Match3.Engine.Runtime
{
    // Only one canonical, bounded wire format is accepted for RSA signing.
    // The format cannot contain ambiguous separators because every token and
    // path is restricted to ASCII punctuation-safe values.
    public static class RemoteManifestCodec
    {
        public const int MaximumCanonicalBytes = 128 * 1024;

        public static RemoteContentManifest ParseCanonical(byte[] encoded)
        {
            if (encoded == null) throw new ArgumentNullException(nameof(encoded));
            if (encoded.Length < 24 || encoded.Length > MaximumCanonicalBytes)
                throw new InvalidOperationException("Invalid signed manifest byte length.");
            string source;
            try
            {
                source = new UTF8Encoding(false, true).GetString(encoded);
            }
            catch (DecoderFallbackException ex)
            {
                throw new InvalidOperationException("Invalid canonical manifest UTF-8.", ex);
            }
            if (source.Length == 0 || source[source.Length - 1] != '\n')
                throw new InvalidOperationException("Canonical manifest must end in newline.");
            var rows = source.Split('\n');
            if (rows[rows.Length - 1] != "")
                throw new InvalidOperationException("Invalid canonical terminator.");
            var data = rows.Take(rows.Length - 1).ToArray();
            if (data.Length < 14 || data[0] != "MATCH3-CONTENT-V1")
                throw new InvalidOperationException("Invalid manifest magic/fields.");
            var i = 1;
            var schema = Integer(data[i++]);
            var game = data[i++];
            var platform = (RemotePlatform)Integer(data[i++]);
            var channel = (RemoteChannel)Integer(data[i++]);
            var release = data[i++];
            var sequence = PositiveLong(data[i++]);
            var key = data[i++];
            var min = Version(data[i++]);
            var max = Version(data[i++]);
            var issued = NonnegativeLong(data[i++]);
            var expires = NonnegativeLong(data[i++]);
            var entryCount = Integer(data[i++]);
            if (entryCount < 1 || entryCount > 256 ||
                data.Length - i < entryCount * 4 + 1)
                throw new InvalidOperationException("Invalid declared manifest entry count.");
            var entries = new List<RemoteContentEntry>();
            for (var j = 0; j < entryCount; j++)
            {
                var path = data[i++];
                var kind = (RemoteContentKind)Integer(data[i++]);
                var size = PositiveLong(data[i++]);
                var digest = data[i++];
                entries.Add(new RemoteContentEntry(path, kind, size, digest));
            }
            var metaCount = Integer(data[i++]);
            if (metaCount < 0 || metaCount > 128 ||
                data.Length != i + metaCount * 5)
                throw new InvalidOperationException(
                    "Malformed signed metadata declaration count.");
            var meta = new List<SignedRemoteMetaCommand>();
            for (var j = 0; j < metaCount; j++)
            {
                var metaGame = data[i++];
                var receipt = data[i++];
                var kind = (RemoteMetaKind)Integer(data[i++]);
                var id = data[i++];
                var flag = data[i++];
                if (flag != "0" && flag != "1")
                    throw new InvalidOperationException("Invalid canonical metadata bit.");
                meta.Add(new SignedRemoteMetaCommand(metaGame, receipt,
                    kind, id, flag == "1"));
            }
            var manifest = new RemoteContentManifest(game, platform,
                channel, release, key, min, max, issued, expires,
                entries, schema, sequence, meta);
            var rebuilt = manifest.CanonicalBytes();
            if (rebuilt.Length != encoded.Length ||
                !rebuilt.SequenceEqual(encoded))
                throw new InvalidOperationException(
                    "Manifest is not in canonical signed byte form.");
            return manifest;
        }

        public static RemoteContentManifest ParseVerified(byte[] encoded,
            byte[] signature, IEnumerable<RemotePublicSigningKey> trusted,
            string gameId, RemotePlatform platform, RemoteChannel channel,
            EngineVersion engine, long nowUnixSeconds)
        {
            var manifest = ParseCanonical(encoded);
            RemoteManifestVerifier.Verify(manifest, signature, trusted,
                gameId, platform, channel, engine, nowUnixSeconds);
            return manifest;
        }

        private static int Integer(string raw)
        {
            int n;
            if (string.IsNullOrEmpty(raw) ||
                !int.TryParse(raw, NumberStyles.None,
                    CultureInfo.InvariantCulture, out n) || n < 0)
                throw new InvalidOperationException("Noncanonical integer.");
            return n;
        }

        private static long NonnegativeLong(string raw)
        {
            long n;
            if (string.IsNullOrEmpty(raw) ||
                !long.TryParse(raw, NumberStyles.None,
                    CultureInfo.InvariantCulture, out n) || n < 0)
                throw new InvalidOperationException("Noncanonical time/sequence.");
            return n;
        }

        private static long PositiveLong(string raw)
        {
            var result = NonnegativeLong(raw);
            if (result == 0)
                throw new InvalidOperationException("Expected positive signed integer.");
            return result;
        }

        private static EngineVersion Version(string source)
        {
            var parts = source.Split('.');
            if (parts.Length != 3)
                throw new InvalidOperationException("Invalid engine semver.");
            return new EngineVersion(Integer(parts[0]),
                Integer(parts[1]), Integer(parts[2]));
        }
    }
}
