using System;
using Match3.Engine.Runtime;

namespace Match3.Engine.Demo
{
    // Intentionally read-only: this adapter observes the existing pinned
    // LibraStack Match3Game. It cannot replace its swap, match or refill rules.
    public sealed class DemoSdkReadAdapter : IReadOnlyMatch3Outcome
    {
        private readonly DemoGame _sdkGame;
        private readonly DemoMetrics _metrics;

        public DemoSdkReadAdapter(DemoGame sdkGame, DemoMetrics metrics)
        {
            _sdkGame = sdkGame ?? throw new ArgumentNullException(nameof(sdkGame));
            _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
        }

        public string BoardSnapshot => _sdkGame.GetSnapshot();
        public int MovesRemaining => _metrics.MovesRemaining;
        public bool GoalAchieved => _metrics.GoalAchieved;
        public bool GameFinished => _metrics.GameFinished;
        public int Score => _metrics.Score;
    }
}
