// Cross-factory wire M3P1. Only an already-real Engine PresentationEvent
// may enter this encoder. No images, Unity dependencies, invented matches,
// score, WIN, rewards or LevelFactory authority. The trailing SHA256 is an
// integrity check, NOT a signature/authentication of who produced the event.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Match3.Engine.Runtime
{
    // Host should feed actual Engine-produced PresentationEvents at their
    // original time and order; this collector never fabricates an event.
    public sealed class NeutralVisualReceiptCollector
    {
        private readonly string profile;
        private readonly List<PresentationEvent> observed =
            new List<PresentationEvent>();
        public int ObservedCount => observed.Count;

        public NeutralVisualReceiptCollector(string profileId)
        {
            if (!GameProfileCatalog.IsKnownId(profileId))
                throw new ArgumentException("unknown Engine profile");
            profile = profileId;
        }

        public void Observe(PresentationEvent realEvent)
        {
            if (realEvent == null)
                throw new ArgumentNullException(nameof(realEvent));
            if (observed.Count >= NeutralVisualReceiptCodec.MaxEvents ||
                realEvent.Sequence != observed.Count + 1L)
                throw new InvalidOperationException(
                    "Actual Engine events must be contiguous and bounded");
            observed.Add(realEvent);
        }

        public byte[] Export()
        {
            if (observed.Count == 0)
                throw new InvalidOperationException(
                    "Do not export fabricated/empty actual Engine events");
            return NeutralVisualReceiptCodec.Encode(profile, observed);
        }
    }

    public static class NeutralVisualReceiptCodec
    {
        public const string Schema = "M3P1";
        public const int MaxEvents = 2048;
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);

        private static string Text(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length > 256)
                throw new ArgumentException("Invalid event identifier/key");
            return Convert.ToBase64String(Utf8.GetBytes(value));
        }

        private static string Hex(byte[] bytes)
        {
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "")
                    .ToLowerInvariant();
        }

        // A returned M3P1 receipt is a faithful PROJECTION of supplied
        // engine-owned events, not evidence of settled replay or game release.
        public static byte[] Encode(string profileId,
            IEnumerable<PresentationEvent> actualEvents)
        {
            if (!GameProfileCatalog.IsKnownId(profileId))
                throw new ArgumentException("Unknown Engine game profile");
            if (actualEvents == null)
                throw new ArgumentNullException(nameof(actualEvents));
            var events = actualEvents.Take(MaxEvents + 1).ToArray();
            if (events.Length > MaxEvents || events.Any(e => e == null))
                throw new ArgumentException("Event count/null exceeds bound");
            var builder = new StringBuilder(Schema + "\n");
            builder.Append(profileId).Append('\n')
                .Append(events.Length.ToString(CultureInfo.InvariantCulture))
                .Append('\n');
            long seq = 0;
            foreach (var e in events)
            {
                if (e.Sequence != ++seq)
                    throw new InvalidOperationException(
                        "Only contiguous per-session Engine event sequences");
                builder.Append(e.Sequence.ToString(CultureInfo.InvariantCulture))
                    .Append('|').Append(Text(e.EventId))
                    .Append('|').Append(((int)e.Kind).ToString(CultureInfo.InvariantCulture))
                    .Append('|').Append(Text(e.PresentationKey))
                    .Append('|').Append(((int)e.Intensity).ToString(CultureInfo.InvariantCulture))
                    .Append('|');
                for (int i = 0; i < e.Positions.Count; ++i)
                {
                    if (i > 0) builder.Append(',');
                    var p = e.Positions[i];
                    builder.Append(p.Layer).Append(':').Append(p.Row)
                        .Append(':').Append(p.Column);
                }
                builder.Append('\n');
            }
            byte[] payload = Utf8.GetBytes(builder.ToString());
            builder.Append(Hex(payload)); // checksum covers trailing newline
            byte[] encoded = Utf8.GetBytes(builder.ToString());
            if (encoded.Length > 1024 * 1024)
                throw new ArgumentException("M3P1 receipt too large");
            return encoded;
        }

        public static IReadOnlyList<PresentationEvent> Decode(byte[] bytes,
            string expectedProfileId)
        {
            if (bytes == null || bytes.Length == 0 || bytes.Length > 1024 * 1024)
                throw new FormatException("Empty/oversized M3P1");
            if (!GameProfileCatalog.IsKnownId(expectedProfileId))
                throw new ArgumentException("Unknown expected profile");
            string all = Utf8.GetString(bytes);
            var lines = all.Split('\n');
            if (lines.Length < 4 || lines[0] != Schema ||
                lines[1] != expectedProfileId ||
                !int.TryParse(lines[2], NumberStyles.None, CultureInfo.InvariantCulture,
                    out int count) || count < 0 || count > MaxEvents ||
                lines.Length != count + 4)
                throw new FormatException("M3P1 schema/profile/count mismatch");
            var list = new List<PresentationEvent>();
            for (int i=0;i<count;i++)
            {
                var cols = lines[3+i].Split('|');
                if (cols.Length != 6 ||
                    !long.TryParse(cols[0], NumberStyles.None, CultureInfo.InvariantCulture,
                        out long sequence) ||
                    !int.TryParse(cols[2], NumberStyles.None, CultureInfo.InvariantCulture,
                        out int kind) ||
                    !int.TryParse(cols[4], NumberStyles.None, CultureInfo.InvariantCulture,
                        out int intensity))
                    throw new FormatException("Invalid M3P1 event fields");
                string eventId, key;
                try {
                    eventId = Utf8.GetString(Convert.FromBase64String(cols[1]));
                    key = Utf8.GetString(Convert.FromBase64String(cols[3]));
                } catch (Exception ex) when (ex is FormatException ||
                    ex is DecoderFallbackException) {
                    throw new FormatException("Invalid M3P1 UTF8 event", ex);
                }
                var positions = new List<BoardPosition>();
                foreach (var coordinate in cols[5].Split(','))
                {
                    var parts = coordinate.Split(':');
                    if (parts.Length != 3 ||
                        !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out int layer) ||
                        !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out int row) ||
                        !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out int col))
                        throw new FormatException("Invalid M3P1 position");
                    positions.Add(new BoardPosition(row, col, layer));
                }
                list.Add(new PresentationEvent(eventId, sequence,
                    (PresentationEventKind)kind, key, positions,
                    (PresentationIntensity)intensity));
            }
            if (!bytes.SequenceEqual(Encode(expectedProfileId, list)))
                throw new FormatException("M3P1 canonical event or SHA256 mismatch");
            return list.AsReadOnly();
        }
    }
}
