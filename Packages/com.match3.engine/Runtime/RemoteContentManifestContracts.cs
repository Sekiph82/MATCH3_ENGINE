using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Match3.Engine.Runtime
{
    // Source and tests are data-only. No network write credentials or title imagery.
    public enum RemotePlatform { Android = 0, Ios = 1, Windows = 2 }
    public enum RemoteChannel { Development = 0, FamilyTest = 1, Release = 2 }
    public enum RemoteContentKind
    {
        Json = 0, LevelsCsv = 1, AddressablesCatalog = 2, AddressablesBundle = 3
    }

    public static class RemoteContentSafety
    {
        public const long FileByteLimit = 16L * 1024 * 1024;
        public const long PackageByteLimit = 64L * 1024 * 1024;
        public static bool IsSafeToken(string input, int maxLength = 96)
        {
            return !string.IsNullOrEmpty(input) && input.Length <= maxLength &&
                input.All(c => (c >= 'a' && c <= 'z') ||
                    (c >= 'A' && c <= 'Z') ||
                    (c >= '0' && c <= '9') || c == '.' || c == '_' || c == '-');
        }

        public static bool IsSafePath(string path)
        {
            if (string.IsNullOrEmpty(path) || path.Length > 180 ||
                path.StartsWith("/", StringComparison.Ordinal) ||
                path.EndsWith("/", StringComparison.Ordinal) ||
                path.Contains("\\") || path.Contains(":") ||
                path.Contains("%") || path.Contains("//"))
                return false;
            var segments = path.Split('/');
            return segments.All(s => s != "." && s != ".." &&
                IsSafeToken(s, 96) && !s.EndsWith(".", StringComparison.Ordinal));
        }

        public static bool AllowedKindPath(RemoteContentKind kind, string path)
        {
            if (!Enum.IsDefined(typeof(RemoteContentKind), kind) ||
                !IsSafePath(path))
                return false;
            var extension = path.Substring(path.LastIndexOf('.') + 1);
            switch (kind)
            {
                case RemoteContentKind.Json:
                case RemoteContentKind.AddressablesCatalog:
                    return extension == "json";
                case RemoteContentKind.LevelsCsv:
                    return extension == "csv";
                case RemoteContentKind.AddressablesBundle:
                    return extension == "bundle";
                default: return false;
            }
        }

        public static bool IsSha256Hex(string hash)
        {
            return hash != null && hash.Length == 64 &&
                hash.All(c => c >= '0' && c <= '9' || c >= 'a' && c <= 'f');
        }

        public static string Sha256(byte[] bytes)
        {
            if (bytes == null) throw new ArgumentNullException(nameof(bytes));
            using (var sha = SHA256.Create())
            {
                var hash = sha.ComputeHash(bytes);
                var sb = new StringBuilder(64);
                foreach (var b in hash)
                    sb.Append(b.ToString("x2", CultureInfo.InvariantCulture));
                return sb.ToString();
            }
        }

        public static bool Matches(byte[] bytes, long length, string hash)
        {
            return bytes != null && bytes.LongLength == length &&
                IsSha256Hex(hash) &&
                StringComparer.Ordinal.Equals(Sha256(bytes), hash);
        }

        public static void RequireNonExecutable(byte[] bytes,
            RemoteContentKind kind)
        {
            if (bytes == null || bytes.Length == 0)
                throw new ArgumentException("Empty remote payload.");
            // This is an early file-type firewall, not a substitute for
            // a trusted Unity bundle loader and signed import graph.
            if (bytes.Length >= 2 && bytes[0] == 0x4d && bytes[1] == 0x5a ||
                bytes.Length >= 4 && bytes[0] == 0x7f &&
                    bytes[1] == 0x45 && bytes[2] == 0x4c && bytes[3] == 0x46 ||
                bytes.Length >= 2 && bytes[0] == 0x23 && bytes[1] == 0x21)
                throw new InvalidOperationException("Executable remote content refused.");
            if (kind == RemoteContentKind.Json ||
                kind == RemoteContentKind.AddressablesCatalog)
            {
                var first = bytes.FirstOrDefault(b =>
                    b != 9 && b != 10 && b != 13 && b != 32);
                if (first != 0x7b && first != 0x5b)
                    throw new InvalidOperationException("Expected JSON payload.");
            }
        }
    }

    public sealed class RemoteContentEntry
    {
        public string Path { get; }
        public RemoteContentKind Kind { get; }
        public long Length { get; }
        public string Sha256 { get; }

        public RemoteContentEntry(string path, RemoteContentKind kind,
            long length, string sha256)
        {
            if (!RemoteContentSafety.AllowedKindPath(kind, path))
                throw new ArgumentException("Unsafe remote content path/type.", nameof(path));
            if (length < 1 || length > RemoteContentSafety.FileByteLimit)
                throw new ArgumentOutOfRangeException(nameof(length));
            if (!RemoteContentSafety.IsSha256Hex(sha256))
                throw new ArgumentException("SHA-256 lowercase hex required.", nameof(sha256));
            Path = path;
            Kind = kind;
            Length = length;
            Sha256 = sha256;
        }
    }

    public sealed class RemoteContentManifest
    {
        private readonly ReadOnlyCollection<RemoteContentEntry> _entries;
        public int Schema { get; }
        public string GameId { get; }
        public RemotePlatform Platform { get; }
        public RemoteChannel Channel { get; }
        public string ReleaseId { get; }
        public string KeyId { get; }
        public EngineVersion MinEngine { get; }
        public EngineVersion MaxEngine { get; }
        public long IssuedAt { get; }
        public long ExpiresAt { get; }
        public IReadOnlyList<RemoteContentEntry> Entries => _entries;

        public RemoteContentManifest(string gameId, RemotePlatform platform,
            RemoteChannel channel, string releaseId, string keyId,
            EngineVersion minEngine, EngineVersion maxEngine,
            long issuedAt, long expiresAt, IEnumerable<RemoteContentEntry> entries,
            int schema = 1)
        {
            if (schema != 1)
                throw new NotSupportedException("Only signed manifest schema 1 supported.");
            if (!GameProfileCatalog.IsKnownId(gameId))
                throw new ArgumentException("Unknown title profile.", nameof(gameId));
            if (!Enum.IsDefined(typeof(RemotePlatform), platform) ||
                !Enum.IsDefined(typeof(RemoteChannel), channel))
                throw new ArgumentOutOfRangeException(nameof(platform));
            if (!RemoteContentSafety.IsSafeToken(releaseId) ||
                !RemoteContentSafety.IsSafeToken(keyId))
                throw new ArgumentException("Unsafe release/key identity.");
            if (minEngine.Major != EngineCompatibility.Api.Major ||
                maxEngine.Major != EngineCompatibility.Api.Major ||
                Compare(minEngine, maxEngine) > 0)
                throw new NotSupportedException("Invalid engine compatibility bounds.");
            if (issuedAt < 0 || expiresAt <= issuedAt ||
                expiresAt - issuedAt > 366L * 24 * 3600)
                throw new ArgumentException("Signed manifest time bounds invalid.");
            if (entries == null) throw new ArgumentNullException(nameof(entries));
            var list = entries.Take(257).ToArray();
            if (list.Length < 1 || list.Length > 256 ||
                list.Any(x => x == null) ||
                list.Select(x => x.Path)
                    .Distinct(StringComparer.OrdinalIgnoreCase).Count() != list.Length)
                throw new ArgumentException("Empty/duplicate/oversized signed entries.");
            long total = 0;
            foreach (var item in list)
            {
                total = checked(total + item.Length);
                if (total > RemoteContentSafety.PackageByteLimit)
                    throw new ArgumentException("Signed package exceeds byte budget.");
            }
            Schema = schema;
            GameId = gameId;
            Platform = platform;
            Channel = channel;
            ReleaseId = releaseId;
            KeyId = keyId;
            MinEngine = minEngine;
            MaxEngine = maxEngine;
            IssuedAt = issuedAt;
            ExpiresAt = expiresAt;
            _entries = new ReadOnlyCollection<RemoteContentEntry>(
                list.OrderBy(e => e.Path, StringComparer.Ordinal).ToList());
        }

        public static int Compare(EngineVersion a, EngineVersion b)
        {
            var major = a.Major.CompareTo(b.Major);
            if (major != 0) return major;
            var minor = a.Minor.CompareTo(b.Minor);
            return minor != 0 ? minor : a.Patch.CompareTo(b.Patch);
        }

        public byte[] CanonicalBytes()
        {
            var lines = new List<string> {
                "MATCH3-CONTENT-V1", Schema.ToString(CultureInfo.InvariantCulture),
                GameId, ((int)Platform).ToString(CultureInfo.InvariantCulture),
                ((int)Channel).ToString(CultureInfo.InvariantCulture),
                ReleaseId, KeyId, MinEngine.ToString(), MaxEngine.ToString(),
                IssuedAt.ToString(CultureInfo.InvariantCulture),
                ExpiresAt.ToString(CultureInfo.InvariantCulture),
                Entries.Count.ToString(CultureInfo.InvariantCulture)
            };
            foreach (var entry in _entries)
            {
                lines.Add(entry.Path);
                lines.Add(((int)entry.Kind).ToString(CultureInfo.InvariantCulture));
                lines.Add(entry.Length.ToString(CultureInfo.InvariantCulture));
                lines.Add(entry.Sha256);
            }
            return Encoding.UTF8.GetBytes(string.Join("\n", lines) + "\n");
        }

        public void RequireCompatible(string expectedGameId,
            RemotePlatform platform, RemoteChannel channel,
            EngineVersion runtimeVersion, long unixNowSeconds)
        {
            if (expectedGameId != GameId || platform != Platform ||
                channel != Channel ||
                Compare(runtimeVersion, MinEngine) < 0 ||
                Compare(runtimeVersion, MaxEngine) > 0 ||
                unixNowSeconds < IssuedAt || unixNowSeconds >= ExpiresAt)
                throw new InvalidOperationException(
                    "Manifest game/platform/channel/version/time incompatible.");
        }

        public string PartitionKey =>
            GameId + "/" + ((int)Platform) + "/" + ((int)Channel);
    }

    public sealed class RemotePublicSigningKey
    {
        private readonly byte[] _modulus;
        private readonly byte[] _exponent;
        public string KeyId { get; }
        public long NotBefore { get; }
        public long NotAfter { get; }
        public bool Revoked { get; }

        public RemotePublicSigningKey(string keyId, byte[] modulus,
            byte[] exponent, long notBefore, long notAfter,
            bool revoked = false)
        {
            if (!RemoteContentSafety.IsSafeToken(keyId) ||
                modulus == null || modulus.Length < 256 ||
                exponent == null || exponent.Length < 3 || exponent.Length > 4 ||
                notBefore < 0 || notAfter <= notBefore)
                throw new ArgumentException("Invalid pinned RSA public key parameters.");
            KeyId = keyId;
            _modulus = (byte[])modulus.Clone();
            _exponent = (byte[])exponent.Clone();
            NotBefore = notBefore;
            NotAfter = notAfter;
            Revoked = revoked;
        }

        public RSAParameters Parameters => new RSAParameters
        {
            Modulus = (byte[])_modulus.Clone(),
            Exponent = (byte[])_exponent.Clone()
        };
    }

    public static class RemoteManifestVerifier
    {
        public static void Verify(RemoteContentManifest manifest,
            byte[] signature, IEnumerable<RemotePublicSigningKey> trustedKeys,
            string expectedGameId, RemotePlatform platform,
            RemoteChannel channel, EngineVersion runtimeVersion,
            long nowUnixSeconds)
        {
            if (manifest == null) throw new ArgumentNullException(nameof(manifest));
            if (signature == null || signature.Length < 256)
                throw new InvalidOperationException("Signature missing or truncated.");
            if (trustedKeys == null)
                throw new ArgumentNullException(nameof(trustedKeys));
            var keys = trustedKeys.Take(17).ToArray();
            if (keys.Length > 16 || keys.Any(k => k == null) ||
                keys.Select(k => k.KeyId).Distinct(StringComparer.Ordinal).Count() != keys.Length)
                throw new ArgumentException("Invalid pinned trust-key registry.");
            manifest.RequireCompatible(expectedGameId, platform, channel,
                runtimeVersion, nowUnixSeconds);
            var key = keys.SingleOrDefault(k => k.KeyId == manifest.KeyId);
            if (key == null || key.Revoked ||
                nowUnixSeconds < key.NotBefore || nowUnixSeconds >= key.NotAfter)
                throw new InvalidOperationException("Signing key missing, expired or revoked.");
            try
            {
                using (var rsa = RSA.Create())
                {
                    rsa.ImportParameters(key.Parameters);
                    if (!rsa.VerifyData(manifest.CanonicalBytes(), signature,
                        HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1))
                        throw new InvalidOperationException(
                            "Manifest RSA-SHA256 verification failed.");
                }
            }
            catch (CryptographicException ex)
            {
                throw new InvalidOperationException(
                    "Unverifiable signed manifest or unsupported public key.", ex);
            }
        }
    }
}
