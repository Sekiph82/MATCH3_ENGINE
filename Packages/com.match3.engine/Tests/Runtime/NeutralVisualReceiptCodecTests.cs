using System;
using System.Linq;
using System.Text;
using Match3.Engine.Runtime;
using NUnit.Framework;

namespace Match3.Engine.Tests
{
    public sealed class NeutralVisualReceiptCodecTests
    {
        private static PresentationEvent Event(long sequence,
            PresentationEventKind kind = PresentationEventKind.Clear)
        {
            return new PresentationEvent(
                "evt-" + sequence, sequence, kind, "board.content.猫",
                new [] { new BoardPosition(0, 1, 0) },
                PresentationIntensity.Medium);
        }

        [Test]
        public void RealEnginePresentationEventRoundTripsUnicodePositionsAndKind()
        {
            byte[] wire = NeutralVisualReceiptCodec.Encode("Coral_Rescue",
                new[]{Event(1, PresentationEventKind.Clear),
                      Event(2, PresentationEventKind.Refill)});
            var decoded = NeutralVisualReceiptCodec.Decode(wire, "Coral_Rescue");
            Assert.That(decoded.Count, Is.EqualTo(2));
            Assert.That(decoded[0].Kind, Is.EqualTo(PresentationEventKind.Clear));
            Assert.That(decoded[0].PresentationKey, Is.EqualTo("board.content.猫"));
            Assert.That(decoded[0].Positions[0].Column, Is.EqualTo(1));
            Assert.That(decoded[1].Kind, Is.EqualTo(PresentationEventKind.Refill));
            Assert.That(Encoding.UTF8.GetString(wire), Does.StartWith("M3P1\n"));
        }

        [Test]
        public void ChecksumChangesOrExtraLinesMustReject()
        {
            byte[] wire = NeutralVisualReceiptCodec.Encode("Coral_Rescue",
                new[]{Event(1)});
            wire[wire.Length - 1] =
                (byte)(wire[wire.Length-1] == 'a' ? 'b' : 'a');
            Assert.Throws<FormatException>(() =>
                NeutralVisualReceiptCodec.Decode(wire, "Coral_Rescue"));
            var extra = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(
                NeutralVisualReceiptCodec.Encode("Coral_Rescue", new[]{Event(1)})) +
                "\nextra");
            Assert.Throws<FormatException>(() =>
                NeutralVisualReceiptCodec.Decode(extra, "Coral_Rescue"));
        }

        [Test]
        public void CannotCrossTitlesOrClaimOutOfOrderEngineSequence()
        {
            var bytes = NeutralVisualReceiptCodec.Encode("Coral_Rescue",
                new[]{Event(1)});
            Assert.Throws<FormatException>(() =>
                NeutralVisualReceiptCodec.Decode(bytes, "Wonder_Pop"));
            Assert.Throws<InvalidOperationException>(() =>
                NeutralVisualReceiptCodec.Encode("Coral_Rescue",
                    new[]{Event(2)}));
        }

        [Test]
        public void SevenActualEnginePresentationKindsRemainDistinct()
        {
            foreach (PresentationEventKind kind in
                Enum.GetValues(typeof(PresentationEventKind)))
            {
                var one = NeutralVisualReceiptCodec.Decode(
                    NeutralVisualReceiptCodec.Encode("Dino_Islands",
                        new[]{Event(1, kind)}), "Dino_Islands");
                Assert.That(one[0].Kind, Is.EqualTo(kind));
            }
        }

        [Test]
        public void HostCanCollectOnlyActualNonemptyContiguousEvents()
        {
            var recorder = new NeutralVisualReceiptCollector("Coral_Rescue");
            Assert.Throws<InvalidOperationException>(() => recorder.Export());
            recorder.Observe(Event(1));
            Assert.Throws<InvalidOperationException>(() =>
                recorder.Observe(Event(3)));
            recorder.Observe(Event(2, PresentationEventKind.Refill));
            var recovered = NeutralVisualReceiptCodec.Decode(
                recorder.Export(), "Coral_Rescue");
            Assert.That(recovered.Count, Is.EqualTo(2));
            Assert.That(recovered[1].Kind, Is.EqualTo(PresentationEventKind.Refill));
        }

        [Test]
        public void CodecContainsNoScoreOrOwnerRewardAuthority()
        {
            var bytes = NeutralVisualReceiptCodec.Encode("Coral_Rescue",
                new[]{Event(1)});
            var text = Encoding.UTF8.GetString(bytes);
            Assert.That(text, Does.Not.Contain("release_allowed"));
            Assert.That(text, Does.Not.Contain("score"));
            Assert.That(text, Does.Not.Contain("WIN"));
            Assert.That(text, Does.Not.Contain("reward"));
        }
    }
}
