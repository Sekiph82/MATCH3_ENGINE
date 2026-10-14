using System;
using Match3.Engine.Runtime;
using NUnit.Framework;

namespace Match3.Engine.Tests
{
    public sealed class NormalizedInputContractTests
    {
        private static BoardPosition A => new BoardPosition(0, 0);
        private static BoardPosition B => new BoardPosition(0, 1);
        private static BoardPosition C => new BoardPosition(1, 0);

        private static BoardTopology Board()
        {
            return new BoardTopology(2, 2, occupants: new[]
            {
                new BoardOccupant(A, "A"), new BoardOccupant(B, "B"),
                new BoardOccupant(C, "C")
            });
        }

        [Test]
        public void TouchAndMouseDragNormalizeToExactlySameSwap()
        {
            InputIntent mouse, touch;
            Assert.That(PointerIntentNormalizer.TryFromDrag(Board(),
                PointerDevice.Mouse, A, B, 27f, out mouse), Is.True);
            Assert.That(PointerIntentNormalizer.TryFromDrag(Board(),
                PointerDevice.Touch, A, B, 27f, out touch), Is.True);
            Assert.That(touch.Equals(mouse), Is.True);
            Assert.That(touch.Kind, Is.EqualTo(InputIntentKind.Swap));
            Assert.That(touch.Source, Is.EqualTo(A));
            Assert.That(touch.Target, Is.EqualTo(B));
        }

        [Test]
        public void TapPairAndDragProduceIdenticalSdkInputEnvelope()
        {
            InputIntent tap, drag;
            Assert.That(PointerIntentNormalizer.TryFromTapPair(Board(), A, C, out tap), Is.True);
            Assert.That(PointerIntentNormalizer.TryFromDrag(Board(),
                PointerDevice.Touch, A, C, 21f, out drag), Is.True);
            Assert.That(tap.Equals(drag), Is.True);
        }

        [Test]
        public void ShortOrNonfiniteDragsRejectWithoutCreatingAnIntent()
        {
            foreach (var value in new[] { 0f, 11.99f, -5f, float.NaN,
                float.PositiveInfinity, float.NegativeInfinity })
            {
                InputIntent intent;
                Assert.That(PointerIntentNormalizer.TryFromDrag(Board(),
                    PointerDevice.Touch, A, B, value, out intent), Is.False);
                Assert.That(intent.IsConstructed, Is.False);
            }
            InputIntent atThreshold;
            Assert.That(PointerIntentNormalizer.TryFromDrag(Board(),
                PointerDevice.Touch, A, B, 12f, out atThreshold), Is.True);
        }

        [Test]
        public void IllegalTopologyAndInvalidPointerDeviceRejectSafely()
        {
            var board = new BoardTopology(2, 2,
                holes: new[] { new BoardPosition(1, 1) },
                locked: new[] { C },
                occupants: new[] { new BoardOccupant(A, "A"),
                    new BoardOccupant(B, "B"), new BoardOccupant(C, "C") });
            InputIntent intent;
            Assert.That(PointerIntentNormalizer.TryFromTapPair(board, A, C, out intent), Is.False);
            Assert.That(PointerIntentNormalizer.TryFromTapPair(board, A, A, out intent), Is.False);
            Assert.That(PointerIntentNormalizer.TryFromTapPair(board, B, C, out intent), Is.False);
            Assert.That(PointerIntentNormalizer.TryFromDrag(board,
                PointerDevice.Mouse, A, C, 50f, out intent), Is.False);
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                PointerIntentNormalizer.TryFromDrag(board, (PointerDevice)99,
                    A, B, 50f, out intent));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                PointerIntentNormalizer.TryFromDrag(board, PointerDevice.Mouse,
                    A, B, 50f, out intent, -1f));
        }

        [Test]
        public void AtomicGateAcceptsOnlyOnePendingIntentAndRejectsDuplicates()
        {
            InputIntent intent;
            Assert.That(PointerIntentNormalizer.TryFromTapPair(Board(), A, B, out intent), Is.True);
            var gate = new AtomicInputGate();
            var accepted = gate.TryBegin(intent, EnginePhase.Ready, false);
            Assert.That(accepted.Accepted, Is.True);
            Assert.That(accepted.Sequence, Is.EqualTo(1L));
            Assert.That(gate.HasPending, Is.True);
            var duplicate = gate.TryBegin(intent, EnginePhase.Ready, false);
            Assert.That(duplicate.Accepted, Is.False);
            Assert.That(duplicate.Status, Is.EqualTo(InputReceiptStatus.Busy));
            Assert.That(duplicate.Sequence, Is.EqualTo(0L));
            Assert.That(gate.Complete(2L), Is.False);
            Assert.That(gate.Complete(accepted.Sequence), Is.True);
            Assert.That(gate.Complete(accepted.Sequence), Is.False);
            Assert.That(gate.HasPending, Is.False);
            Assert.That(gate.TryBegin(intent, EnginePhase.Ready, false).Sequence, Is.EqualTo(2L));
        }

        [Test]
        public void ModalAndResolvingAndUnconstructedInputsNeverSpendOrAccept()
        {
            var gate = new AtomicInputGate();
            InputIntent intent;
            PointerIntentNormalizer.TryFromTapPair(Board(), A, B, out intent);
            Assert.That(gate.TryBegin(intent, EnginePhase.Ready, true).Status,
                Is.EqualTo(InputReceiptStatus.ModalBlocked));
            Assert.That(gate.TryBegin(intent, EnginePhase.Resolving, false).Status,
                Is.EqualTo(InputReceiptStatus.NotReady));
            Assert.That(gate.TryBegin(intent, EnginePhase.Paused, false).Status,
                Is.EqualTo(InputReceiptStatus.NotReady));
            Assert.That(gate.TryBegin(default(InputIntent), EnginePhase.Ready, false).Status,
                Is.EqualTo(InputReceiptStatus.Rejected));
            Assert.That(gate.HasPending, Is.False);
            Assert.That(gate.TryBegin(intent, EnginePhase.Ready, false).Sequence,
                Is.EqualTo(1L));
        }

        [Test]
        public void CancelledPointerCannotCompleteReusedOrStaleSequence()
        {
            InputIntent intent;
            PointerIntentNormalizer.TryFromTapPair(Board(), A, B, out intent);
            var gate = new AtomicInputGate();
            var old = gate.TryBegin(intent, EnginePhase.Ready, false);
            gate.CancelPending();
            Assert.That(gate.HasPending, Is.False);
            Assert.That(gate.Complete(old.Sequence), Is.False);
            var next = gate.TryBegin(intent, EnginePhase.Ready, false);
            Assert.That(next.Sequence, Is.GreaterThan(old.Sequence));
            Assert.That(gate.Complete(old.Sequence), Is.False);
            Assert.That(gate.Complete(next.Sequence), Is.True);
        }

        [Test]
        public void HintAndBoosterTargetUseSameNoEconomyMutationGate()
        {
            var gate = new AtomicInputGate();
            var hint = new InputIntent(InputIntentKind.Hint, A, B);
            var booster = new InputIntent(InputIntentKind.BoosterTarget, A, C);
            var first = gate.TryBegin(hint, EnginePhase.Ready, false);
            Assert.That(first.Accepted, Is.True);
            Assert.That(gate.TryBegin(booster, EnginePhase.Ready, false).Accepted, Is.False);
            Assert.That(gate.Complete(first.Sequence), Is.True);
            Assert.That(gate.TryBegin(booster, EnginePhase.Ready, false).Accepted, Is.True);
            // Gate contains no SDK board or wallet to mutate or spend charges.
            Assert.That(typeof(AtomicInputGate).GetFields(
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Instance).Length, Is.EqualTo(2));
        }
    }
}
