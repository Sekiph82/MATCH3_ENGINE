using System;
using System.Collections.Generic;
using System.Linq;
using Match3.Engine.Runtime;
using NUnit.Framework;

namespace Match3.Engine.Tests
{
    public sealed class BoardTopologyAndRandomTests
    {
        private static BoardPosition Cell(int row, int col, int layer = 0)
        {
            return new BoardPosition(row, col, layer);
        }

        [Test]
        public void LayeredTopologyIsImmutableAndEnumeratedInStableOrder()
        {
            var cells = new[] {
                new BoardOccupant(Cell(0, 0), "A"),
                new BoardOccupant(Cell(1, 0), "B"),
                new BoardOccupant(Cell(0, 0, 1), "C")
            };
            var board = new BoardTopology(2, 2, 2,
                holes: new[] { Cell(0, 1) },
                locked: new[] { Cell(1, 1, 1) },
                occupants: cells);
            Assert.That(board.Rows, Is.EqualTo(2));
            Assert.That(board.Columns, Is.EqualTo(2));
            Assert.That(board.Layers, Is.EqualTo(2));
            Assert.That(board.ActivePositions().Count, Is.EqualTo(7));
            Assert.That(board.IsHole(Cell(0, 1)), Is.True);
            Assert.That(board.IsHole(Cell(0, 1, 1)), Is.False);
            Assert.That(board.IsLocked(Cell(1, 1, 1)), Is.True);
            Assert.That(board.IsLocked(Cell(1, 1)), Is.False);
            Assert.That(board.Occupants[Cell(0, 0, 1)], Is.EqualTo("C"));
            Assert.That(board.ActivePositions().First(), Is.EqualTo(Cell(0, 0)));
            var mutable = (IDictionary<BoardPosition, string>)board.Occupants;
            Assert.Throws<NotSupportedException>(() => mutable.Add(Cell(1, 1), "D"));
        }

        [Test]
        public void TopologyCandidateOnlyChecksOrthogonalNonblockedOccupiedCells()
        {
            var board = new BoardTopology(3, 3, 2,
                holes: new[] { Cell(1, 1) },
                locked: new[] { Cell(2, 0) },
                occupants: new[] {
                    new BoardOccupant(Cell(0, 0), "A"),
                    new BoardOccupant(Cell(0, 1), "B"),
                    new BoardOccupant(Cell(1, 0), "C"),
                    new BoardOccupant(Cell(2, 0), "D"),
                    new BoardOccupant(Cell(0, 0, 1), "E")
                });
            Assert.That(board.IsSwapCandidate(Cell(0, 0), Cell(0, 1)), Is.True);
            Assert.That(board.IsSwapCandidate(Cell(0, 0), Cell(1, 0)), Is.True);
            Assert.That(board.IsSwapCandidate(Cell(0, 0), Cell(0, 0)), Is.False);
            Assert.That(board.IsSwapCandidate(Cell(0, 1), Cell(1, 0)), Is.False);
            Assert.That(board.IsSwapCandidate(Cell(0, 0), Cell(0, 0, 1)), Is.False);
            Assert.That(board.IsSwapCandidate(Cell(1, 0), Cell(2, 0)), Is.False);
            Assert.That(board.IsSwapCandidate(Cell(0, 1), Cell(1, 1)), Is.False);
            Assert.That(board.IsSwapCandidate(Cell(0, 2), Cell(0, 1)), Is.False);
        }

        [Test]
        public void DuplicateOccupancyAndHoleLocksRejectMalformedBoard()
        {
            Assert.Throws<ArgumentException>(() => new BoardTopology(2, 2,
                occupants: new[] {
                    new BoardOccupant(Cell(0, 0), "A"),
                    new BoardOccupant(Cell(0, 0), "B")
                }));
            Assert.Throws<ArgumentException>(() => new BoardTopology(2, 2,
                holes: new[] { Cell(0, 0) },
                occupants: new[] { new BoardOccupant(Cell(0, 0), "A") }));
            Assert.Throws<ArgumentException>(() => new BoardTopology(2, 2,
                holes: new[] { Cell(0, 0) }, locked: new[] { Cell(0, 0) }));
            Assert.Throws<ArgumentException>(() => new BoardTopology(2, 2,
                holes: new[] { Cell(0, 0), Cell(0, 0) }));
        }

        [Test]
        public void InvalidCoordinatesLayersAndSizeFailClosed()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new BoardPosition(-1, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new BoardTopology(0, 2));
            Assert.Throws<ArgumentOutOfRangeException>(() => new BoardTopology(1025, 2));
            Assert.Throws<ArgumentOutOfRangeException>(() => new BoardTopology(2, 2, 17));
            Assert.Throws<ArgumentOutOfRangeException>(() => new BoardTopology(2, 2,
                occupants: new[] { new BoardOccupant(Cell(2, 0), "A") }));
            Assert.Throws<ArgumentOutOfRangeException>(() => new BoardTopology(2, 2,
                locked: new[] { Cell(1, 1, 1) }));
            Assert.Throws<ArgumentException>(() => new BoardOccupant(Cell(0, 0), ""));
        }

        [Test]
        public void SeededRandomProducesIdenticalByteSequenceAcrossFreshRuns()
        {
            var a = new SeededRandom(20261014u);
            var b = new SeededRandom(20261014u);
            for (var i = 0; i < 100; i++)
                Assert.That(a.NextUInt32(), Is.EqualTo(b.NextUInt32()));
            Assert.That(a.State, Is.EqualTo(b.State));
            Assert.That(a.State, Is.Not.EqualTo(0u));
        }

        [Test]
        public void DifferentSeedProducesDifferentSequenceAndAllIndicesBounded()
        {
            var a = new SeededRandom(10u);
            var b = new SeededRandom(11u);
            Assert.That(a.NextUInt32(), Is.Not.EqualTo(b.NextUInt32()));
            for (var i = 0; i < 256; i++)
            {
                var index = a.NextIndex(9);
                Assert.That(index, Is.InRange(0, 8));
            }
            Assert.That(new SeededRandom(1u).NextIndex(1), Is.EqualTo(0));
        }

        [Test]
        public void RNGRejectsZeroSeedAndInvalidBoundWithoutHiddenFallback()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new SeededRandom(0u));
            var rng = new SeededRandom(1u);
            Assert.Throws<ArgumentOutOfRangeException>(() => rng.NextIndex(0));
            Assert.Throws<ArgumentOutOfRangeException>(() => rng.NextIndex(-5));
            Assert.That(rng.State, Is.EqualTo(1u));
        }
    }
}
