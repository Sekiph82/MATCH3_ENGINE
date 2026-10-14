using System;
using Cysharp.Threading.Tasks;
using Match3.Core.Structs;
using Match3.Engine.Runtime;

namespace Match3.Engine.Demo
{
    // Unity demo integration proof only. Authority stays with the ORIGINAL
    // SDK's SwapItemsAsync and invalid-swap rollback, never with this bridge.
    public sealed class DemoSdkAtomicInputBridge
    {
        private readonly DemoGame _sdkGame;
        private readonly DemoMetrics _metrics;
        private readonly BoardTopology _topology;
        private readonly AtomicInputGate _gate = new AtomicInputGate();

        public DemoSdkAtomicInputBridge(DemoGame sdkGame, DemoMetrics metrics, BoardTopology topology)
        {
            _sdkGame = sdkGame ?? throw new ArgumentNullException(nameof(sdkGame));
            _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
            _topology = topology ?? throw new ArgumentNullException(nameof(topology));
        }

        public bool HasPending => _gate.HasPending;

        public UniTask<InputReceipt> TryTapPairAsync(BoardPosition source,
            BoardPosition target, bool modalOpen = false)
        {
            InputIntent intent;
            if (!PointerIntentNormalizer.TryFromTapPair(_topology, source, target, out intent))
                return UniTask.FromResult(new InputReceipt(0, InputReceiptStatus.Rejected));
            return DispatchAsync(intent, modalOpen);
        }

        public UniTask<InputReceipt> TryDragAsync(PointerDevice device,
            BoardPosition source, BoardPosition target, float dragDistancePixels,
            bool modalOpen = false)
        {
            InputIntent intent;
            if (!PointerIntentNormalizer.TryFromDrag(_topology, device, source,
                target, dragDistancePixels, out intent))
                return UniTask.FromResult(new InputReceipt(0, InputReceiptStatus.Rejected));
            return DispatchAsync(intent, modalOpen);
        }

        private async UniTask<InputReceipt> DispatchAsync(InputIntent intent, bool modalOpen)
        {
            // A pending or resolving SDK board never receives a second swap.
            // A terminal result or noninitialized SDK board cannot receive one.
            var phase = _sdkGame.IsBusy || string.IsNullOrEmpty(_metrics.InitialBoardSnapshot)
                || _metrics.GameFinished ? EnginePhase.Resolving : EnginePhase.Ready;
            var receipt = _gate.TryBegin(intent, phase, modalOpen);
            if (!receipt.Accepted) return receipt;

            try
            {
                await _sdkGame.TrySwapAsync(
                    new GridPosition(intent.Source.Row, intent.Source.Column),
                    new GridPosition(intent.Target.Row, intent.Target.Column));
                // "Accepted" means input was forwarded ONCE, not that the
                // SDK validated a match. Move truth stays in DemoMetrics.
                return receipt;
            }
            finally
            {
                _gate.Complete(receipt.Sequence);
            }
        }
    }
}
