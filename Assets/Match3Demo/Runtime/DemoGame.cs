using System;
using System.Linq;
using Cysharp.Threading.Tasks;
using Match3.App;
using Match3.App.Interfaces;
using Match3.Core.Structs;
using Match3.Infrastructure;
using Match3.Infrastructure.SequenceDetectors;

namespace Match3.Engine.Demo
{
    public sealed class DemoGame : Match3Game<DemoGridSlot>
    {
        private readonly DemoMetrics _metrics;
        private readonly DemoFillStrategy _fillStrategy;

        public DemoGame(DemoMetrics metrics) : this(metrics, new DemoGameServices(metrics))
        {
        }

        private DemoGame(DemoMetrics metrics, DemoGameServices services) : base(services.Config)
        {
            _metrics = metrics;
            _fillStrategy = services.FillStrategy;
            SetGameBoardFillStrategy(_fillStrategy);
        }

        public bool IsBusy { get; private set; }

        public async UniTask InitializeAsync()
        {
            InitGameLevel(DemoScenario.Level);
            LevelGoalAchieved += OnLevelGoalAchieved;
            Finished += OnFinished;
            await StartAsync();
            _metrics.BoardReady(GetSnapshot());
        }

        public async UniTask<bool> TrySwapAsync(GridPosition first, GridPosition second)
        {
            if (IsBusy || _metrics.GameFinished || _metrics.MovesRemaining <= 0 || !AreAdjacent(first, second))
            {
                return false;
            }

            if (!GameBoard.IsPositionOnBoard(first) || !GameBoard.IsPositionOnBoard(second) ||
                !GameBoard[first].IsMovable || !GameBoard[second].IsMovable)
            {
                return false;
            }

            var before = GetSnapshot();
            _metrics.BeginMoveAttempt();
            IsBusy = true;
            try
            {
                await SwapItemsAsync(first, second);
                if (_metrics.MoveAccepted)
                {
                    _metrics.MoveCommitted();
                }
                else
                {
                    var after = GetSnapshot();
                    if (after != before)
                    {
                        throw new InvalidOperationException("The SDK did not restore an invalid swap to its original board.");
                    }

                    _metrics.MoveRolledBack();
                }

                return _metrics.MoveAccepted;
            }
            finally
            {
                IsBusy = false;
            }
        }

        public string GetSnapshot()
        {
            var result = new System.Text.StringBuilder();
            for (var row = 0; row < GameBoard.RowCount; row++)
            {
                if (row > 0)
                {
                    result.Append('/');
                }

                for (var column = 0; column < GameBoard.ColumnCount; column++)
                {
                    result.Append(GameBoard[row, column].HasItem ? (char)('A' + GameBoard[row, column].ItemId) : '.');
                }
            }

            return result.ToString();
        }

        public int GetItemId(int row, int column) => GameBoard[row, column].ItemId;

        protected override void OnGameStarted()
        {
        }

        protected override void OnGameStopped()
        {
        }

        private static bool AreAdjacent(GridPosition first, GridPosition second)
        {
            var rowDistance = Math.Abs(first.RowIndex - second.RowIndex);
            var columnDistance = Math.Abs(first.ColumnIndex - second.ColumnIndex);
            return rowDistance + columnDistance == 1;
        }

        private void OnLevelGoalAchieved(object sender, LevelGoal<DemoGridSlot> goal)
        {
            _metrics.GoalReached();
        }

        private void OnFinished(object sender, EventArgs args)
        {
            _metrics.GameWasFinished();
        }

        private sealed class DemoGameServices
        {
            public DemoGameServices(DemoMetrics metrics)
            {
                var solver = new GameBoardSolver<DemoGridSlot>(
                    new ISequenceDetector<DemoGridSlot>[]
                    {
                        new HorizontalLineDetector<DemoGridSlot>(),
                        new VerticalLineDetector<DemoGridSlot>()
                    },
                    new ISpecialItemDetector<DemoGridSlot>[0]);
                FillStrategy = new DemoFillStrategy(solver, metrics, metrics.Seed,
                    metrics.Seed == DemoScenario.Seed);
                Config = new GameConfig<DemoGridSlot>
                {
                    GameBoardSolver = solver,
                    GameBoardDataProvider = new DemoBoardDataProvider(),
                    ItemSwapper = new DemoItemSwapper(),
                    LevelGoalsProvider = new DemoLevelGoalsProvider(),
                    SolvedSequencesConsumers = new ISolvedSequencesConsumer<DemoGridSlot>[0]
                };
            }

            public GameConfig<DemoGridSlot> Config { get; }
            public DemoFillStrategy FillStrategy { get; }
        }
    }

    internal sealed class DemoItemSwapper : IItemSwapper<DemoGridSlot>
    {
        public async UniTask SwapItemsAsync(DemoGridSlot first, DemoGridSlot second,
            System.Threading.CancellationToken cancellationToken = default)
        {
            await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken);
            var firstItem = first.Item;
            first.SetItem(second.Item);
            second.SetItem(firstItem);
        }
    }

    internal sealed class DemoLevelGoalsProvider : ILevelGoalsProvider<DemoGridSlot>
    {
        public LevelGoal<DemoGridSlot>[] GetLevelGoals(int level, IGameBoard<DemoGridSlot> gameBoard)
        {
            return new LevelGoal<DemoGridSlot>[]
            {
                new SdkTargetCollectionGoal<DemoGridSlot>(DemoScenario.GoalItemCount,
                    slot => slot.HasItem && slot.ItemId == DemoScenario.TargetItemId)
            };
        }
    }
}
