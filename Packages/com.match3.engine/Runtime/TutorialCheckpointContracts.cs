using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Match3.Engine.Runtime
{
    // Durable, corruption-detecting neutral tutorial checkpoint.
    // Digest is not a MAC: trusted game saves and reward ledgers stay separate.
    public static class FtueCheckpointCodec
    {
        private const string Magic = "M10FTUE";
        private const string Schema = "1";
        private const int MaximumBytes = 256 * 1024;

        public static SaveEnvelope Write(FtueSnapshot snapshot)
        {
            if (snapshot == null)
                throw new ArgumentNullException(nameof(snapshot));
            var chunks = new List<string> {
                Magic, Schema, snapshot.GameId,
                snapshot.Version.ToString(CultureInfo.InvariantCulture),
                snapshot.Receipts.Count.ToString(CultureInfo.InvariantCulture)
            };
            foreach (var receipt in snapshot.Receipts)
            {
                chunks.Add(B64(receipt.Id));
                chunks.Add(B64(receipt.StepId));
                chunks.Add(((int)receipt.Kind).ToString(CultureInfo.InvariantCulture));
            }
            var raw = string.Join("|", chunks);
            var payload = raw + "|" + Sha(raw);
            if (Encoding.UTF8.GetByteCount(payload) > MaximumBytes)
                throw new InvalidOperationException("Checkpoint exceeds budget.");
            return new SaveEnvelope(1, payload);
        }

        public static FtueSnapshot Read(SaveEnvelope envelope, string expectedGameId)
        {
            if (envelope == null) throw new ArgumentNullException(nameof(envelope));
            if (!GameProfileCatalog.IsKnownId(expectedGameId))
                throw new ArgumentException("Unknown expected game.", nameof(expectedGameId));
            if (envelope.SchemaVersion != 1 ||
                Encoding.UTF8.GetByteCount(envelope.Payload) > MaximumBytes)
                throw new InvalidOperationException("Unsupported or oversized FTUE checkpoint.");
            var parts = envelope.Payload.Split('|');
            int version, count;
            if (parts.Length < 6 || parts[0] != Magic || parts[1] != Schema ||
                parts[2] != expectedGameId ||
                !int.TryParse(parts[3], NumberStyles.None,
                    CultureInfo.InvariantCulture, out version) ||
                !int.TryParse(parts[4], NumberStyles.None,
                    CultureInfo.InvariantCulture, out count) ||
                version < 1 || count < 0 || count > 512 ||
                parts.Length != 6 + count * 3)
                throw new InvalidOperationException("Invalid FTUE checkpoint envelope.");
            var raw = string.Join("|", parts.Take(parts.Length - 1));
            if (!StringComparer.Ordinal.Equals(
                parts[parts.Length - 1], Sha(raw)))
                throw new InvalidOperationException("FTUE checkpoint integrity mismatch.");
            var entries = new List<FtueReceipt>();
            for (var i = 0; i < count; i++)
            {
                int kind;
                if (!int.TryParse(parts[7 + 3 * i], NumberStyles.None,
                    CultureInfo.InvariantCulture, out kind) ||
                    !Enum.IsDefined(typeof(FtueReceiptKind), kind))
                    throw new InvalidOperationException("Invalid FTUE checkpoint receipt.");
                try
                {
                    entries.Add(new FtueReceipt(
                        Unb64(parts[5 + 3 * i]),
                        Unb64(parts[6 + 3 * i]),
                        (FtueReceiptKind)kind));
                }
                catch (FormatException ex)
                {
                    throw new InvalidOperationException(
                        "Invalid checkpoint UTF-8/base64 receipt.", ex);
                }
            }
            return new FtueSnapshot(expectedGameId, version, entries);
        }

        public static FtueSnapshot Restore(FtueCatalog catalog,
            SaveEnvelope envelope)
        {
            if (catalog == null) throw new ArgumentNullException(nameof(catalog));
            var snapshot = Read(envelope, catalog.GameId);
            if (snapshot.Version != catalog.Version ||
                snapshot.Receipts.Any(r => !catalog.Steps.Any(s =>
                    StringComparer.Ordinal.Equals(s.Id, r.StepId))))
                throw new InvalidOperationException(
                    "Checkpoint version or step schema does not match catalog.");
            return snapshot;
        }

        private static string B64(string text) =>
            Convert.ToBase64String(Encoding.UTF8.GetBytes(text));

        private static string Unb64(string value)
        {
            var bytes = Convert.FromBase64String(value);
            var text = new UTF8Encoding(false, true).GetString(bytes);
            if (!B64(text).Equals(value, StringComparison.Ordinal))
                throw new FormatException("Noncanonical checkpoint encoding.");
            return text;
        }

        private static string Sha(string value)
        {
            using (var sha = SHA256.Create())
            {
                var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(value));
                var builder = new StringBuilder(64);
                foreach (var b in bytes)
                    builder.Append(b.ToString("x2", CultureInfo.InvariantCulture));
                return builder.ToString();
            }
        }
    }
}
