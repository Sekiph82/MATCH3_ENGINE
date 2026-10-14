using System;

namespace Match3.Engine.Runtime
{
    public enum PointerDevice { Mouse = 0, Touch = 1 }
    public enum InputIntentKind { Swap = 0, Hint = 1, BoosterTarget = 2 }
    public enum InputReceiptStatus
    {
        Accepted = 0, Busy = 1, NotReady = 2,
        ModalBlocked = 3, Rejected = 4
    }

    // Device identity deliberately never enters the authority envelope.
    public readonly struct InputIntent : IEquatable<InputIntent>
    {
        public InputIntentKind Kind { get; }
        public BoardPosition Source { get; }
        public BoardPosition Target { get; }
        public bool IsConstructed { get; }

        public InputIntent(InputIntentKind kind, BoardPosition source, BoardPosition target)
        {
            if (!Enum.IsDefined(typeof(InputIntentKind), kind))
                throw new ArgumentOutOfRangeException(nameof(kind));
            Kind = kind;
            Source = source;
            Target = target;
            IsConstructed = true;
        }

        public bool Equals(InputIntent other) =>
            Kind == other.Kind && Source.Equals(other.Source) && Target.Equals(other.Target)
            && IsConstructed == other.IsConstructed;
        public override bool Equals(object value) => value is InputIntent other && Equals(other);
        public override int GetHashCode()
        {
            unchecked { return ((int)Kind * 397 ^ Source.GetHashCode()) * 397 ^ Target.GetHashCode(); }
        }
    }

    // Unity UI adapters translate their already-hit-tested grid cells into
    // this pure gesture envelope. Actual match validation remains with SDK.
    public static class PointerIntentNormalizer
    {
        public static bool TryFromDrag(BoardTopology board, PointerDevice device,
            BoardPosition from, BoardPosition to, float deltaPixels,
            out InputIntent intent, float minimumDragPixels = 12f)
        {
            intent = default(InputIntent);
            if (!Enum.IsDefined(typeof(PointerDevice), device))
                throw new ArgumentOutOfRangeException(nameof(device));
            if (board == null) throw new ArgumentNullException(nameof(board));
            if (float.IsNaN(minimumDragPixels) || float.IsInfinity(minimumDragPixels) ||
                minimumDragPixels < 0f)
                throw new ArgumentOutOfRangeException(nameof(minimumDragPixels));
            if (float.IsNaN(deltaPixels) || float.IsInfinity(deltaPixels) ||
                deltaPixels < minimumDragPixels || !board.IsSwapCandidate(from, to))
                return false;
            intent = new InputIntent(InputIntentKind.Swap, from, to);
            return true;
        }

        // A two-tap select uses precisely the same SDK swap envelope as drag.
        public static bool TryFromTapPair(BoardTopology board,
            BoardPosition first, BoardPosition second, out InputIntent intent)
        {
            if (board == null) throw new ArgumentNullException(nameof(board));
            intent = default(InputIntent);
            if (!board.IsSwapCandidate(first, second)) return false;
            intent = new InputIntent(InputIntentKind.Swap, first, second);
            return true;
        }
    }

    public readonly struct InputReceipt
    {
        public long Sequence { get; }
        public InputReceiptStatus Status { get; }
        public bool Accepted => Status == InputReceiptStatus.Accepted;
        public InputReceipt(long sequence, InputReceiptStatus status)
        {
            Sequence = sequence;
            Status = status;
        }
    }

    // Single pending action, no board/inventory/economy access or local UI
    // callbacks allowed to change the accepted action after it is issued.
    public sealed class AtomicInputGate
    {
        private long _nextSequence;
        private long _pending;
        public bool HasPending => _pending != 0;

        public InputReceipt TryBegin(InputIntent intent, EnginePhase phase, bool modalOpen)
        {
            if (!intent.IsConstructed)
                return new InputReceipt(0, InputReceiptStatus.Rejected);
            if (!Enum.IsDefined(typeof(EnginePhase), phase))
                throw new ArgumentOutOfRangeException(nameof(phase));
            if (modalOpen)
                return new InputReceipt(0, InputReceiptStatus.ModalBlocked);
            if (phase != EnginePhase.Ready)
                return new InputReceipt(0, InputReceiptStatus.NotReady);
            if (HasPending)
                return new InputReceipt(0, InputReceiptStatus.Busy);
            var token = checked(++_nextSequence);
            _pending = token;
            return new InputReceipt(token, InputReceiptStatus.Accepted);
        }

        public bool Complete(long sequence)
        {
            if (sequence < 1 || sequence != _pending) return false;
            _pending = 0;
            return true;
        }

        public void CancelPending()
        {
            // Invalidates previous pending token. The monotonically
            // increasing token is never reused after cancellation.
            _pending = 0;
        }
    }
}
