using System;

namespace Match3.Engine.Runtime
{
    // Pure idle scheduler. Legal candidates are computed externally from
    // isolated SDK snapshot copies, never from a mutable live game board.
    public sealed class IdleHintPolicy
    {
        private readonly int _idleMilliseconds;
        private readonly SeededRandom _tieBreaker;
        private long _lastActionAt;
        private bool _started;
        private bool _shownSinceAction;

        public IdleHintPolicy(int idleMilliseconds, uint seed)
        {
            if (idleMilliseconds < 1000 || idleMilliseconds > 600000)
                throw new ArgumentOutOfRangeException(nameof(idleMilliseconds));
            _idleMilliseconds = idleMilliseconds;
            _tieBreaker = new SeededRandom(seed);
        }

        public void RecordAction(long monotonicMilliseconds)
        {
            if (monotonicMilliseconds < 0 ||
                (_started && monotonicMilliseconds < _lastActionAt))
                throw new ArgumentOutOfRangeException(nameof(monotonicMilliseconds),
                    "Hint clock must be monotonic.");
            _lastActionAt = monotonicMilliseconds;
            _started = true;
            _shownSinceAction = false;
        }

        public bool TrySelect(long monotonicMilliseconds,
            EnginePhase phase, bool accessibilityEnabled,
            bool tutorialVisible, bool modalVisible, bool adVisible,
            bool animationRunning, bool lowPowerAllowed,
            CandidateScanResult sdkCandidates,
            out SwapCandidate hint)
        {
            hint = default(SwapCandidate);
            if (!_started || monotonicMilliseconds < _lastActionAt)
                return false;
            if (phase != EnginePhase.Ready || !accessibilityEnabled ||
                tutorialVisible || modalVisible || adVisible ||
                animationRunning || !lowPowerAllowed || _shownSinceAction ||
                sdkCandidates == null || !sdkCandidates.HasMoves)
                return false;
            if (monotonicMilliseconds - _lastActionAt < _idleMilliseconds)
                return false;
            hint = sdkCandidates.Accepted[_tieBreaker.NextIndex(sdkCandidates.Accepted.Count)];
            _shownSinceAction = true;
            return true;
        }
    }
}
