using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Match3.Engine.Runtime
{
    // Evidence preparation only. Neither PNG header checks nor a content digest
    // constitute visual approval, real-device execution, or an M09-014 PASS.
    public sealed class PlayerSurfaceArtworkProof
    {
        private static readonly byte[] PngSignature =
            { 137, 80, 78, 71, 13, 10, 26, 10 };

        public string AssetKey { get; }
        public string RelativePath { get; }
        public string Sha256 { get; }
        public int Width { get; }
        public int Height { get; }

        public PlayerSurfaceArtworkProof(
            string assetKey, string relativePath, byte[] originalPng)
        {
            if (string.IsNullOrWhiteSpace(assetKey) ||
                assetKey.Length > 256)
                throw new ArgumentException(
                    "Bounded artwork asset key required.", nameof(assetKey));
            if (string.IsNullOrWhiteSpace(relativePath) ||
                relativePath.Length > 512 ||
                relativePath.IndexOf('\\') >= 0 ||
                !relativePath.StartsWith("Assets/", StringComparison.Ordinal) ||
                !relativePath.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
                relativePath.Split('/').Any(part =>
                    part.Length == 0 || part == "." || part == ".."))
                throw new ArgumentException(
                    "Artwork source must be a safe relative Assets PNG path.",
                    nameof(relativePath));

            if (originalPng == null || originalPng.Length < 45 ||
                originalPng.Length > 8 * 1024 * 1024 ||
                !PngSignature.SequenceEqual(originalPng.Take(8)) ||
                Read32(originalPng, 8) != 13 ||
                originalPng[12] != (byte)'I' ||
                originalPng[13] != (byte)'H' ||
                originalPng[14] != (byte)'D' ||
                originalPng[15] != (byte)'R' ||
                Read32(originalPng, originalPng.Length - 12) != 0 ||
                Encoding.ASCII.GetString(
                    originalPng, originalPng.Length - 8, 4) != "IEND")
                throw new ArgumentException(
                    "Original PNG file envelope is invalid or exceeds 8 MiB.",
                    nameof(originalPng));

            var width = Read32(originalPng, 16);
            var height = Read32(originalPng, 20);
            if (width < 1 || height < 1 ||
                width > 16384 || height > 16384)
                throw new ArgumentException(
                    "Artwork dimensions are invalid.", nameof(originalPng));

            byte[] hash;
            using (var sha = SHA256.Create())
                hash = sha.ComputeHash(originalPng);

            AssetKey = assetKey;
            RelativePath = relativePath;
            Width = width;
            Height = height;
            Sha256 = string.Concat(hash.Select(
                b => b.ToString("x2", CultureInfo.InvariantCulture)));
        }

        private static long Read32(byte[] bytes, int offset)
        {
            return ((long)bytes[offset] << 24) |
                ((long)bytes[offset + 1] << 16) |
                ((long)bytes[offset + 2] << 8) | bytes[offset + 3];
        }
    }

    public sealed class PlayerSurfaceArtworkReviewReport
    {
        private readonly ReadOnlyCollection<string> _missingKeys;
        private readonly ReadOnlyCollection<PlayerSurfaceArtworkProof> _proofs;

        public int RequiredMasterCount { get; }
        public int VerifiedMasterCount => _proofs.Count;
        public IReadOnlyList<string> MissingMasterKeys => _missingKeys;
        public IReadOnlyList<PlayerSurfaceArtworkProof> Proofs => _proofs;
        public bool ReadyForHumanVisualReview => _missingKeys.Count == 0;

        internal PlayerSurfaceArtworkReviewReport(
            int required, IEnumerable<string> missing,
            IEnumerable<PlayerSurfaceArtworkProof> proofs)
        {
            RequiredMasterCount = required;
            _missingKeys = new ReadOnlyCollection<string>(missing.ToList());
            _proofs = new ReadOnlyCollection<PlayerSurfaceArtworkProof>(
                proofs.ToList());
        }
    }

    public static class PlayerSurfaceArtworkReview
    {
        public static PlayerSurfaceArtworkReviewReport Inspect(
            PlayerSurfaceMatrix matrix,
            IEnumerable<PlayerSurfaceArtworkProof> originalFiles)
        {
            if (matrix == null)
                throw new ArgumentNullException(nameof(matrix));
            if (originalFiles == null)
                throw new ArgumentNullException(nameof(originalFiles));

            var requiredBindings = matrix.Bindings.Values.ToArray();
            // The shared neutral engine may reuse one master across states,
            // but distinct game themes may never silently share its identity.
            if (requiredBindings.GroupBy(x => x.MasterAssetKey,
                    StringComparer.Ordinal).Any(group =>
                    group.Select(x => x.GameId)
                        .Distinct(StringComparer.Ordinal).Count() > 1))
                throw new InvalidOperationException(
                    "Different games share one artwork master key.");

            var required = new HashSet<string>(
                requiredBindings.Select(x => x.MasterAssetKey),
                StringComparer.Ordinal);
            var proofs = originalFiles.Take(2049).ToArray();
            if (proofs.Length > 2048 || proofs.Any(x => x == null))
                throw new ArgumentException(
                    "Artwork proof list exceeds bound or contains null.",
                    nameof(originalFiles));

            var provided = new Dictionary<string, PlayerSurfaceArtworkProof>(
                StringComparer.Ordinal);
            foreach (var proof in proofs)
            {
                if (!required.Contains(proof.AssetKey))
                    throw new ArgumentException(
                        "Artwork proof does not bind to a required theme master.",
                        nameof(originalFiles));
                if (provided.ContainsKey(proof.AssetKey))
                    throw new ArgumentException(
                        "Duplicate artwork proof for master key.",
                        nameof(originalFiles));
                provided.Add(proof.AssetKey, proof);
            }

            return new PlayerSurfaceArtworkReviewReport(
                required.Count,
                required.Where(key => !provided.ContainsKey(key))
                    .OrderBy(x => x, StringComparer.Ordinal),
                provided.Values.OrderBy(x => x.AssetKey, StringComparer.Ordinal));
        }
    }
}
