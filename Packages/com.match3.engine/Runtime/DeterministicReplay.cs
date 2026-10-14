using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Match3.Engine.Runtime
{
    // Strict canonical replay log. The hash is an INTEGRITY CHECK, not
    // authentication: remote rewards must independently validate a replay.
    public readonly struct ReplayStep
    {
        public int Number { get; }
        public BoardPosition From { get; }
        public BoardPosition To { get; }
        public string BoardHash { get; }

        public ReplayStep(int number, BoardPosition from, BoardPosition to, string boardHash)
        {
            if (number < 1) throw new ArgumentOutOfRangeException(nameof(number));
            if (from.Equals(to) || from.Layer != to.Layer ||
                Math.Abs(from.Row - to.Row) + Math.Abs(from.Column - to.Column) != 1)
                throw new ArgumentException("Replay swap must be orthogonally adjacent on one layer.");
            BoardHash = ReplayCodec.RequireDigest(boardHash);
            Number = number;
            From = from;
            To = to;
        }
    }

    public sealed class ReplayTrace
    {
        public const int CurrentSchema = 1;
        public const int MaxSteps = 10000;

        public string ProfileId { get; }
        public string ConfigurationHash { get; }
        public uint Seed { get; }
        public IReadOnlyList<ReplayStep> Steps { get; }

        public ReplayTrace(string profileId, string configurationHash, uint seed,
            IEnumerable<ReplayStep> steps)
        {
            if (!GameProfileCatalog.IsKnownId(profileId))
                throw new ArgumentException("Replay profile is not an approved game.", nameof(profileId));
            if (seed == 0) throw new ArgumentOutOfRangeException(nameof(seed));
            if (steps == null) throw new ArgumentNullException(nameof(steps));
            var data = steps.ToArray();
            if (data.Length > MaxSteps)
                throw new ArgumentOutOfRangeException(nameof(steps), "Replay is too large.");
            for (var i = 0; i < data.Length; i++)
                if (data[i].Number != i + 1)
                    throw new ArgumentException("Replay steps must be ordered and contiguous.");
            ProfileId = profileId;
            ConfigurationHash = ReplayCodec.RequireDigest(configurationHash);
            Seed = seed;
            Steps = new ReadOnlyCollection<ReplayStep>(data);
        }
    }

    public static class ReplayCodec
    {
        private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);
        public const int MaximumBytes = 1048576;

        public static string BoardHash(string boardSnapshot)
        {
            if (boardSnapshot == null) throw new ArgumentNullException(nameof(boardSnapshot));
            return Hash(StrictUtf8.GetBytes(boardSnapshot));
        }

        public static byte[] Encode(ReplayTrace trace)
        {
            if (trace == null) throw new ArgumentNullException(nameof(trace));
            var b = new StringBuilder();
            b.Append("M3R1\n").Append(trace.ProfileId).Append('\n')
                .Append(trace.ConfigurationHash).Append('\n')
                .Append(trace.Seed.ToString(CultureInfo.InvariantCulture)).Append('\n')
                .Append(trace.Steps.Count.ToString(CultureInfo.InvariantCulture)).Append('\n');
            foreach (var a in trace.Steps)
            {
                b.Append(a.Number).Append('|')
                    .Append(a.From.Layer).Append('|').Append(a.From.Row).Append('|')
                    .Append(a.From.Column).Append('|').Append(a.To.Layer).Append('|')
                    .Append(a.To.Row).Append('|').Append(a.To.Column).Append('|')
                    .Append(a.BoardHash).Append('\n');
            }
            var prefix = StrictUtf8.GetBytes(b.ToString());
            b.Append(Hash(prefix));
            var bytes = StrictUtf8.GetBytes(b.ToString());
            if (bytes.Length > MaximumBytes)
                throw new ArgumentOutOfRangeException(nameof(trace), "Replay exceeds size budget.");
            return bytes;
        }

        public static ReplayTrace Decode(byte[] data, string requiredProfileId,
            string requiredConfigurationHash)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (data.Length == 0 || data.Length > MaximumBytes)
                throw new FormatException("Replay empty or too large.");
            if (!GameProfileCatalog.IsKnownId(requiredProfileId))
                throw new ArgumentException("Unknown target game profile.", nameof(requiredProfileId));
            var expectedConfigurationHash = RequireDigest(requiredConfigurationHash);
            var text = StrictUtf8.GetString(data);
            var lines = text.Split('\n');
            if (lines.Length < 6 || lines[0] != "M3R1")
                throw new FormatException("Unsupported replay schema/version.");
            if (lines[1] != requiredProfileId || lines[2] != expectedConfigurationHash)
                throw new InvalidOperationException("Replay profile/configuration does not match.");
            if (!uint.TryParse(lines[3], NumberStyles.None, CultureInfo.InvariantCulture, out var seed) ||
                seed == 0 || !int.TryParse(lines[4], NumberStyles.None,
                    CultureInfo.InvariantCulture, out var count) ||
                count < 0 || count > ReplayTrace.MaxSteps || lines.Length != count + 6)
                throw new FormatException("Invalid replay count or seed.");
            var steps = new List<ReplayStep>();
            for (var i = 0; i < count; i++)
            {
                var fields = lines[i + 5].Split('|');
                if (fields.Length != 8)
                    throw new FormatException("Invalid replay action field count.");
                var numbers = new int[7];
                for (var j = 0; j < 7; j++)
                    if (!int.TryParse(fields[j], NumberStyles.None,
                        CultureInfo.InvariantCulture, out numbers[j]))
                        throw new FormatException("Invalid replay position.");
                steps.Add(new ReplayStep(numbers[0],
                    new BoardPosition(numbers[2], numbers[3], numbers[1]),
                    new BoardPosition(numbers[5], numbers[6], numbers[4]),
                    fields[7]));
            }
            var decoded = new ReplayTrace(lines[1], lines[2], seed, steps);
            var canonical = Encode(decoded);
            if (!data.SequenceEqual(canonical))
                throw new FormatException("Replay checksum, ordering or canonical form mismatch.");
            return decoded;
        }

        // Compare authoritative SDK settled snapshots at every step,
        // not merely the final score. This method never simulates a swap.
        public static void VerifySdkSettledSnapshots(ReplayTrace trace,
            IReadOnlyList<string> authoritativeSettledSnapshots)
        {
            if (trace == null) throw new ArgumentNullException(nameof(trace));
            if (authoritativeSettledSnapshots == null)
                throw new ArgumentNullException(nameof(authoritativeSettledSnapshots));
            if (trace.Steps.Count != authoritativeSettledSnapshots.Count)
                throw new InvalidOperationException("Replay step count and SDK receipts differ.");
            for (var i = 0; i < trace.Steps.Count; i++)
                if (!StringComparer.Ordinal.Equals(trace.Steps[i].BoardHash,
                    BoardHash(authoritativeSettledSnapshots[i])))
                    throw new InvalidOperationException("Replay SDK board hash divergence at step " + (i + 1));
        }

        public static string RequireDigest(string value)
        {
            if (value == null || value.Length != 64 ||
                value.Any(c => !((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'))))
                throw new ArgumentException("Expected canonical lowercase SHA256 hex digest.");
            return value;
        }

        private static string Hash(byte[] bytes)
        {
            using (var sha = SHA256.Create())
            {
                var hash = sha.ComputeHash(bytes);
                var b = new StringBuilder(64);
                foreach (var digit in hash) b.Append(digit.ToString("x2"));
                return b.ToString();
            }
        }
    }
}
