using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using Match3.App;
using Match3.App.Interfaces;
using Match3.Core.Interfaces;
using Match3.Core.Structs;
using Match3.Infrastructure.Extensions;

namespace Match3.Engine.Demo
{
    internal sealed class DemoFillStrategy : IBoardFillStrategy<DemoGridSlot>
    {
        private readonly IGameBoardSolver<DemoGridSlot> _solver;
        private readonly DemoMetrics _metrics;
        private readonly SeededItemSource _itemSource;

        public DemoFillStrategy(IGameBoardSolver<DemoGridSlot> solver, DemoMetrics metrics, int seed)
        {
            _solver = solver;
            _metrics = metrics;
            _itemSource = new SeededItemSource(seed);
        }

        public string Name => "Seeded neutral demo fill";

        public IEnumerable<IJob> GetFillJobs(IGameBoard<DemoGridSlot> gameBoard)
        {
            for (var row = 0; row < gameBoard.RowCount; row++)
            {
                for (var column = 0; column < gameBoard.ColumnCount; column++)
                {
                    gameBoard[row, column].SetItem(new DemoItem(DemoScenario.StartingItems[row, column]));
                }
            }

            return Array.Empty<IJob>();
        }

        public IEnumerable<IJob> GetSolveJobs(IGameBoard<DemoGridSlot> gameBoard, SolvedData<DemoGridSlot> solvedData)
        {
            var matchedCount = solvedData.GetUniqueSolvedGridSlots(true).Count();
            _metrics.MoveResolved(matchedCount);
            return new IJob[] { new DemoSettleJob(gameBoard, solvedData, _solver, _itemSource, _metrics) };
        }
    }

    internal sealed class DemoSettleJob : Job
    {
        private const int MaximumCascadeWaves = 8;
        private readonly IGameBoard<DemoGridSlot> _gameBoard;
        private readonly IGameBoardSolver<DemoGridSlot> _solver;
        private readonly SeededItemSource _itemSource;
        private readonly DemoMetrics _metrics;
        private SolvedData<DemoGridSlot> _solvedData;

        public DemoSettleJob(IGameBoard<DemoGridSlot> gameBoard, SolvedData<DemoGridSlot> solvedData,
            IGameBoardSolver<DemoGridSlot> solver, SeededItemSource itemSource, DemoMetrics metrics) : base(0)
        {
            _gameBoard = gameBoard;
            _solvedData = solvedData;
            _solver = solver;
            _itemSource = itemSource;
            _metrics = metrics;
        }

        public override UniTask ExecuteAsync(CancellationToken cancellationToken = default)
        {
            var wave = 0;
            while (_solvedData.SolvedSequences.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var solvedSlots = _solvedData.GetUniqueSolvedGridSlots(true).ToArray();
                if (solvedSlots.Length == 0)
                {
                    break;
                }

                _metrics.WaveCleared(wave, solvedSlots.Length);
                _metrics.AwardWave(wave, solvedSlots.Length);
                foreach (var slot in solvedSlots)
                {
                    slot.Clear();
                }

                ApplyGravityAndRefill();
                _metrics.Settled(DemoBoardSnapshot.Create(_gameBoard));

                var nextWave = _solver.Solve(_gameBoard, GetAllPositions());
                if (nextWave.SolvedSequences.Count == 0)
                {
                    break;
                }

                wave++;
                if (wave > MaximumCascadeWaves)
                {
                    throw new InvalidOperationException("The deterministic demo exceeded its cascade safety limit.");
                }

                _solvedData = nextWave;
            }

            return UniTask.CompletedTask;
        }

        private void ApplyGravityAndRefill()
        {
            for (var column = 0; column < _gameBoard.ColumnCount; column++)
            {
                var items = new List<DemoItem>(_gameBoard.RowCount);
                for (var row = _gameBoard.RowCount - 1; row >= 0; row--)
                {
                    var slot = _gameBoard[row, column];
                    if (slot.HasItem)
                    {
                        items.Add(slot.Item);
                    }
                }

                for (var row = 0; row < _gameBoard.RowCount; row++)
                {
                    _gameBoard[row, column].Clear();
                }

                var nextRow = _gameBoard.RowCount - 1;
                foreach (var item in items)
                {
                    _gameBoard[nextRow--, column].SetItem(item);
                }
            }

            for (var row = 0; row < _gameBoard.RowCount; row++)
            {
                for (var column = 0; column < _gameBoard.ColumnCount; column++)
                {
                    var slot = _gameBoard[row, column];
                    if (!slot.HasItem)
                    {
                        slot.SetItem(new DemoItem(_itemSource.NextItemId()));
                    }
                }
            }
        }

        private GridPosition[] GetAllPositions()
        {
            var positions = new List<GridPosition>(_gameBoard.RowCount * _gameBoard.ColumnCount);
            for (var row = 0; row < _gameBoard.RowCount; row++)
            {
                for (var column = 0; column < _gameBoard.ColumnCount; column++)
                {
                    positions.Add(new GridPosition(row, column));
                }
            }

            return positions.ToArray();
        }
    }

    internal sealed class SeededItemSource
    {
        private readonly System.Random _random;
        private readonly int[] _cascadeSetup = { 2, 2, 2, 0, 1, 0, 1, 2 };
        private int _index;

        public SeededItemSource(int seed)
        {
            _random = new System.Random(seed);
        }

        public int NextItemId()
        {
            if (_index < _cascadeSetup.Length)
            {
                return _cascadeSetup[_index++];
            }

            _index++;
            return _random.Next(0, 4);
        }
    }

    internal static class DemoBoardSnapshot
    {
        public static string Create(IGameBoard<DemoGridSlot> gameBoard)
        {
            var result = new System.Text.StringBuilder();
            for (var row = 0; row < gameBoard.RowCount; row++)
            {
                if (row > 0)
                {
                    result.Append('/');
                }

                for (var column = 0; column < gameBoard.ColumnCount; column++)
                {
                    var slot = gameBoard[row, column];
                    result.Append(slot.HasItem ? (char)('A' + slot.ItemId) : '.');
                }
            }

            return result.ToString();
        }
    }
}
