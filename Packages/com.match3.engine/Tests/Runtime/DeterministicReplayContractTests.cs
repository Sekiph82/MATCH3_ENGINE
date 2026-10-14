using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Match3.Engine.Runtime;
using NUnit.Framework;

namespace Match3.Engine.Tests
{
    public sealed class DeterministicReplayContractTests
    {
        private static readonly string Config = ReplayCodec.BoardHash("profile:Coral_Rescue;board:4x4;rules:v1");

        private static ReplayTrace Example()
        {
            return new ReplayTrace("Coral_Rescue", Config, 20261014u,
                new[] {
                    new ReplayStep(1, new BoardPosition(1, 0), new BoardPosition(2, 0),
                        ReplayCodec.BoardHash("AA/BB")),
                    new ReplayStep(2, new BoardPosition(0, 1), new BoardPosition(0, 2),
                        ReplayCodec.BoardHash("AB/BA"))
                });
        }

        [Test]
        public void CanonicalReplayIsByteIdenticalAcrossIndependentEncodes()
        {
            var a = ReplayCodec.Encode(Example());
            var b = ReplayCodec.Encode(Example());
            CollectionAssert.AreEqual(a, b);
            var decoded = ReplayCodec.Decode(a, "Coral_Rescue", Config);
            Assert.That(decoded.Seed, Is.EqualTo(20261014u));
            Assert.That(decoded.Steps.Count, Is.EqualTo(2));
            Assert.That(decoded.Steps[1].Number, Is.EqualTo(2));
            CollectionAssert.AreEqual(a, ReplayCodec.Encode(decoded));
            Assert.That(((IList<ReplayStep>)decoded.Steps).IsReadOnly, Is.True);
        }

        [Test]
        public void ReplayReceiptsVerifyEveryActualAuthoritativeSnapshot()
        {
            var trace = Example();
            Assert.DoesNotThrow(() => ReplayCodec.VerifySdkSettledSnapshots(trace,
                new[] { "AA/BB", "AB/BA" }));
            Assert.Throws<InvalidOperationException>(() =>
                ReplayCodec.VerifySdkSettledSnapshots(trace, new[] { "AA/BB", "AA/BB" }));
            Assert.Throws<InvalidOperationException>(() =>
                ReplayCodec.VerifySdkSettledSnapshots(trace, new[] { "AA/BB" }));
        }

        [Test]
        public void ForgedPayloadChecksumAndTrailingBytesAreRejected()
        {
            var genuine = ReplayCodec.Encode(Example());
            var tampered = (byte[])genuine.Clone();
            tampered[0] = (byte)'X';
            Assert.Throws<FormatException>(() =>
                ReplayCodec.Decode(tampered, "Coral_Rescue", Config));
            tampered = (byte[])genuine.Clone();
            var text = Encoding.UTF8.GetString(tampered);
            var i = text.IndexOf("AA", StringComparison.Ordinal);
            if (i < 0) i = text.IndexOf("20261014", StringComparison.Ordinal);
            Assert.That(i, Is.GreaterThan(0));
            tampered[i] = tampered[i] == (byte)'1' ? (byte)'2' : (byte)'1';
            Assert.Throws<FormatException>(() =>
                ReplayCodec.Decode(tampered, "Coral_Rescue", Config));
            Assert.Throws<FormatException>(() =>
                ReplayCodec.Decode(genuine.Concat(new byte[] { 10 }).ToArray(),
                    "Coral_Rescue", Config));
        }

        [Test]
        public void ReplayRejectsWrongGameAndDifferentLevelConfiguration()
        {
            var data = ReplayCodec.Encode(Example());
            Assert.Throws<InvalidOperationException>(() =>
                ReplayCodec.Decode(data, "Wonder_Pop", Config));
            Assert.Throws<InvalidOperationException>(() =>
                ReplayCodec.Decode(data, "Coral_Rescue", ReplayCodec.BoardHash("different-level")));
            Assert.Throws<ArgumentException>(() =>
                new ReplayTrace("unknown", Config, 1u, new ReplayStep[0]));
            Assert.Throws<ArgumentException>(() =>
                ReplayCodec.Decode(data, "unknown", Config));
        }

        [Test]
        public void ReplayRejectsMissingDuplicateOrNonOrthogonalActionOrder()
        {
            var hash = ReplayCodec.BoardHash("snapshot");
            var valid = new ReplayStep(1, new BoardPosition(0, 0), new BoardPosition(0, 1), hash);
            var wrongOrder = new ReplayStep(1, new BoardPosition(0, 1), new BoardPosition(1, 1), hash);
            Assert.Throws<ArgumentException>(() =>
                new ReplayTrace("Coral_Rescue", Config, 1u, new[] { valid, wrongOrder }));
            Assert.Throws<ArgumentException>(() =>
                new ReplayStep(1, new BoardPosition(0, 0), new BoardPosition(1, 1), hash));
            Assert.Throws<ArgumentException>(() =>
                new ReplayStep(1, new BoardPosition(0, 0, 0), new BoardPosition(0, 1, 1), hash));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new ReplayStep(0, new BoardPosition(0, 0), new BoardPosition(0, 1), hash));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new ReplayTrace("Coral_Rescue", Config, 0u, new ReplayStep[0]));
        }

        [Test]
        public void FutureSchemaUnknownDigestAndOversizedLogsFailClosed()
        {
            var bytes = ReplayCodec.Encode(Example());
            var data = Encoding.UTF8.GetString(bytes);
            Assert.Throws<FormatException>(() =>
                ReplayCodec.Decode(Encoding.UTF8.GetBytes(data.Replace("M3R1", "M3R2")),
                    "Coral_Rescue", Config));
            Assert.Throws<ArgumentException>(() =>
                ReplayCodec.RequireDigest(new string('F', 64)));
            Assert.Throws<ArgumentException>(() =>
                ReplayCodec.RequireDigest("x"));
            Assert.Throws<FormatException>(() =>
                ReplayCodec.Decode(new byte[ReplayCodec.MaximumBytes + 1],
                    "Coral_Rescue", Config));
            Assert.Throws<ArgumentNullException>(() => ReplayCodec.Encode(null));
        }

        [Test]
        public void ReplayHashChangesOnSnapshotChangeAndCannotBeMutable()
        {
            Assert.That(ReplayCodec.BoardHash("A/B"), Is.Not.EqualTo(ReplayCodec.BoardHash("A/C")));
            var trace = Example();
            Assert.Throws<NotSupportedException>(() =>
                ((IList<ReplayStep>)trace.Steps).Clear());
            Assert.Throws<ArgumentNullException>(() =>
                ReplayCodec.VerifySdkSettledSnapshots(trace, null));
        }
    }
}
