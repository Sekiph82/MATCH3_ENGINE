using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

namespace Match3.Engine.Demo
{
    public sealed class DemoMetrics
    {
        private readonly List<string> _events = new List<string>();
        private readonly List<string> _settledSnapshots = new List<string>();

        public int Seed => DemoScenario.Seed;
        public int MovesRemaining { get; private set; } = DemoScenario.StartingMoves;
        public bool MoveAccepted { get; private set; }
        public bool GoalAchieved { get; private set; }
        public bool GameFinished { get; private set; }
        public int InitialMatchedItemCount { get; private set; }
        public int CascadeCount { get; private set; }
        public string InitialBoardSnapshot { get; private set; } = string.Empty;
        public string FinalBoardSnapshot { get; private set; } = string.Empty;
        public string InitialBoardHash => Hash(InitialBoardSnapshot);
        public string FinalBoardHash => Hash(FinalBoardSnapshot);
        public IReadOnlyList<string> Events => _events;
        public IReadOnlyList<string> SettledSnapshots => _settledSnapshots;

        internal void BoardReady(string snapshot)
        {
            InitialBoardSnapshot = snapshot;
            FinalBoardSnapshot = snapshot;
            _events.Add("board-ready:" + snapshot);
        }

        internal void BeginMoveAttempt()
        {
            MoveAccepted = false;
        }

        internal void MoveResolved(int matchedItemCount)
        {
            MoveAccepted = true;
            InitialMatchedItemCount = matchedItemCount;
            _events.Add("move-accepted:" + matchedItemCount);
        }

        internal void WaveCleared(int wave, int itemCount)
        {
            if (wave > 0)
            {
                CascadeCount++;
                _events.Add("cascade:" + wave + ":" + itemCount);
            }
            else
            {
                _events.Add("match:" + itemCount);
            }
        }

        internal void Settled(string snapshot)
        {
            FinalBoardSnapshot = snapshot;
            _settledSnapshots.Add(snapshot);
            _events.Add("settled:" + snapshot);
        }

        internal void MoveCommitted()
        {
            MovesRemaining--;
            _events.Add("move-committed:" + MovesRemaining);
        }

        internal void MoveRolledBack()
        {
            _events.Add("swap-rolled-back");
        }

        internal void GoalReached()
        {
            GoalAchieved = true;
            _events.Add("goal-achieved");
        }

        internal void GameWasFinished()
        {
            GameFinished = true;
            _events.Add("game-finished");
        }

        private static string Hash(string value)
        {
            using (var sha = SHA256.Create())
            {
                var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(value));
                var result = new StringBuilder(bytes.Length * 2);
                foreach (var item in bytes)
                {
                    result.Append(item.ToString("x2"));
                }

                return result.ToString();
            }
        }
    }
}
