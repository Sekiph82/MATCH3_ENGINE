using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Match3.App.Interfaces;
using Match3.Core.Structs;
using Match3.Engine.Runtime;
using Match3.Infrastructure;
using Match3.Infrastructure.SequenceDetectors;

namespace Match3.Engine.Demo
{
    // Neutral-demo 2D adapter, NOT a second Match3 solver. Evaluates each
    // possible swap against cloned SDK grid slots only. The pinned SDK's
    // GameBoardSolver may transition special state, so never give it the
    // active GameBoard for move finding or initial-spawn validation.
    public sealed class NeutralSdkSnapshotValidator
    {
        private readonly BoardTopology _topology;
        private readonly GameBoardSolver<DemoGridSlot> _solver =
            new GameBoardSolver<DemoGridSlot>(
                new ISequenceDetector<DemoGridSlot>[] {
                    new HorizontalLineDetector<DemoGridSlot>(),
                    new VerticalLineDetector<DemoGridSlot>()
                },
                new ISpecialItemDetector<DemoGridSlot>[0]);

        public NeutralSdkSnapshotValidator(BoardTopology topology)
        {
            _topology = topology ?? throw new ArgumentNullException(nameof(topology));
            if (topology.Layers != 1 ||
                topology.ActivePositions().Count != topology.Rows * topology.Columns)
                throw new NotSupportedException(
                    "Neutral demo SDK probe requires one fully active 2D layer.");
        }

        public bool HasInitialMatch(IReadOnlyDictionary<BoardPosition, string> contents)
        {
            var board = Clone(contents);
            var points = new GridPosition[_topology.Rows * _topology.Columns];
            var index = 0;
            for (var row = 0; row < _topology.Rows; row++)
                for (var column = 0; column < _topology.Columns; column++)
                    points[index++] = new GridPosition(row, column);
            return _solver.Solve(board, points).SolvedSequences.Count > 0;
        }

        // This is a read-only projection of the pinned SDK's detected
        // sequences, NOT a second matcher or rule owner. A fresh clone
        // prevents SDK special detectors from touching live board state.
        public IReadOnlyList<int> GetSdkRunLengths(
            IReadOnlyDictionary<BoardPosition, string> contents)
        {
            var board = Clone(contents);
            var cells = new List<GridPosition>();
            for (var row = 0; row < _topology.Rows; row++)
                for (var column = 0; column < _topology.Columns; column++)
                    cells.Add(new GridPosition(row, column));
            var result = _solver.Solve(board, cells.ToArray());
            var lengths = result.SolvedSequences.Select(x => x.SolvedGridSlots.Count)
                .OrderBy(x => x).ToArray();
            return Array.AsReadOnly(lengths);
        }

        public CandidateScanResult FindRealLegalMoves(
            IReadOnlyDictionary<BoardPosition, string> contents)
        {
            // Check every piece and ID before running expensive SDK calls.
            RequireSnapshot(contents);
            // A spawn template may deliberately begin EMPTY. Candidate cells
            // must be rebuilt from the validated proposed SDK snapshot, not
            // from the template's original occupants. Keep original locks.
            var snapshotTopology = new BoardTopology(
                _topology.Rows, _topology.Columns,
                locked: _topology.ActivePositions().Where(_topology.IsLocked),
                occupants: contents.Select(x => new BoardOccupant(x.Key, x.Value)));
            return CanonicalMoveCandidates.Find(snapshotTopology,
                candidate => HasMatchAfterSwap(contents, candidate));
        }

        private bool HasMatchAfterSwap(
            IReadOnlyDictionary<BoardPosition, string> contents, SwapCandidate candidate)
        {
            var cloned = Clone(contents);
            var a = cloned[candidate.Source.Row, candidate.Source.Column];
            var b = cloned[candidate.Target.Row, candidate.Target.Column];
            var item = a.Item;
            a.SetItem(b.Item);
            b.SetItem(item);
            return _solver.Solve(cloned,
                new GridPosition(candidate.Source.Row, candidate.Source.Column),
                new GridPosition(candidate.Target.Row, candidate.Target.Column))
                .SolvedSequences.Count > 0;
        }

        private SnapshotBoard Clone(IReadOnlyDictionary<BoardPosition, string> contents)
        {
            RequireSnapshot(contents);
            var array = new DemoGridSlot[_topology.Rows, _topology.Columns];
            for (var row = 0; row < _topology.Rows; row++)
                for (var column = 0; column < _topology.Columns; column++)
                {
                    var cell = new BoardPosition(row, column);
                    var slot = new DemoGridSlot(new GridPosition(row, column),
                        _topology.IsLocked(cell));
                    slot.SetItem(new DemoItem(contents[cell][0] - 'A'));
                    array[row, column] = slot;
                }
            return new SnapshotBoard(array);
        }

        private void RequireSnapshot(IReadOnlyDictionary<BoardPosition, string> contents)
        {
            if (contents == null) throw new ArgumentNullException(nameof(contents));
            if (contents.Count != _topology.Rows * _topology.Columns)
                throw new ArgumentException("Incomplete or extended SDK board snapshot.");
            for (var row = 0; row < _topology.Rows; row++)
                for (var column = 0; column < _topology.Columns; column++)
                {
                    var cell = new BoardPosition(row, column);
                    if (!contents.TryGetValue(cell, out var token) ||
                        token == null || token.Length != 1 ||
                        token[0] < 'A' || token[0] > 'D')
                        throw new ArgumentException("Invalid neutral demo SDK item ID.");
                }
        }

        private sealed class SnapshotBoard : IGameBoard<DemoGridSlot>
        {
            private readonly DemoGridSlot[,] _cells;

            public SnapshotBoard(DemoGridSlot[,] cells) { _cells = cells; }
            public int RowCount => _cells.GetLength(0);
            public int ColumnCount => _cells.GetLength(1);
            public DemoGridSlot this[GridPosition p] => _cells[p.RowIndex, p.ColumnIndex];
            public DemoGridSlot this[int row, int col] => _cells[row, col];
            public bool IsPositionOnGrid(GridPosition p) =>
                p.RowIndex >= 0 && p.RowIndex < RowCount &&
                p.ColumnIndex >= 0 && p.ColumnIndex < ColumnCount;
            public bool IsPositionOnBoard(GridPosition p) =>
                IsPositionOnGrid(p) && this[p].CanContainItem;
        }
    }
}
