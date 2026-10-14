using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;
using Match3.Engine.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Match3.Engine.Demo.Tests
{
    public sealed class NeutralSdkSnapshotValidatorPlayModeTests
    {
        private static BoardPosition P(int r, int c) => new BoardPosition(r, c);

        private static BoardTopology FromOriginalSdkBoard(DemoGame game)
        {
            var rows = game.GetSnapshot().Split('/');
            var cells = new List<BoardOccupant>();
            for (var row = 0; row < rows.Length; row++)
                for (var col = 0; col < rows[row].Length; col++)
                    cells.Add(new BoardOccupant(P(row, col), rows[row][col].ToString()));
            return new BoardTopology(rows.Length, rows[0].Length, occupants: cells);
        }

        [UnityTest]
        public IEnumerator RealSdkCloneMoveFinderAcceptsWinningPairAndNeverMutatesGame()
        {
            var metrics = new DemoMetrics(20261013);
            var game = new DemoGame(metrics);
            game.InitializeAsync().Forget();
            yield return new WaitUntil(() => metrics.InitialBoardSnapshot.Length > 0);
            var board = FromOriginalSdkBoard(game);
            var probe = new NeutralSdkSnapshotValidator(board);
            var before = game.GetSnapshot();
            Assert.That(probe.HasInitialMatch(board.Occupants), Is.False);

            var found = probe.FindRealLegalMoves(board.Occupants);
            Assert.That(found.HasMoves, Is.True);
            Assert.That(found.Accepted, Does.Contain(new SwapCandidate(P(1, 0), P(2, 0))));
            Assert.That(found.Accepted, Does.Not.Contain(new SwapCandidate(P(0, 0), P(0, 1))));
            Assert.That(found.Accepted.Distinct().Count(), Is.EqualTo(found.Accepted.Count));

            Assert.That(game.GetSnapshot(), Is.EqualTo(before));
            Assert.That(metrics.MovesRemaining, Is.EqualTo(2));
            Assert.That(metrics.Score, Is.EqualTo(0));
            Assert.That(metrics.GameFinished, Is.False);
        }

        [UnityTest]
        public IEnumerator RealSdkCloneDetectsPrematchAndRejectsMalformedProposal()
        {
            var metrics = new DemoMetrics(20261013);
            var game = new DemoGame(metrics);
            game.InitializeAsync().Forget();
            yield return new WaitUntil(() => metrics.InitialBoardSnapshot.Length > 0);
            var board = FromOriginalSdkBoard(game);
            var probe = new NeutralSdkSnapshotValidator(board);

            var illegal = new Dictionary<BoardPosition, string>(board.Occupants);
            illegal[P(0, 0)] = "A";
            illegal[P(0, 1)] = "A";
            illegal[P(0, 2)] = "A";
            Assert.That(probe.HasInitialMatch(illegal), Is.True);
            Assert.That(probe.HasInitialMatch(board.Occupants), Is.False);
            var invalid = new Dictionary<BoardPosition, string>(board.Occupants);
            invalid.Remove(P(0, 0));
            Assert.Throws<ArgumentException>(() => probe.FindRealLegalMoves(invalid));
            invalid = new Dictionary<BoardPosition, string>(board.Occupants);
            invalid[P(0, 0)] = "INVALID";
            Assert.Throws<ArgumentException>(() => probe.HasInitialMatch(invalid));
            Assert.That(game.GetSnapshot(), Is.EqualTo(metrics.InitialBoardSnapshot));
            Assert.That(metrics.MovesRemaining, Is.EqualTo(2));
        }

        [UnityTest]
        public IEnumerator SeededShufflePlansSdkPlayableLayoutWithoutSpendingARealMove()
        {
            var metrics = new DemoMetrics(20261013);
            var game = new DemoGame(metrics);
            game.InitializeAsync().Forget();
            yield return new WaitUntil(() => metrics.InitialBoardSnapshot.Length > 0);
            var board = FromOriginalSdkBoard(game);
            var probe = new NeutralSdkSnapshotValidator(board);

            Func<IReadOnlyDictionary<BoardPosition, string>, bool> sdkValid =
                candidate => !probe.HasInitialMatch(candidate) &&
                    probe.FindRealLegalMoves(candidate).HasMoves;
            var first = DeterministicBoardPlanning.TryShuffle(
                board, new SeededRandom(982u), sdkValid, 64);
            var second = DeterministicBoardPlanning.TryShuffle(
                board, new SeededRandom(982u), sdkValid, 64);
            Assert.That(first.Succeeded, Is.True);
            Assert.That(second.Succeeded, Is.True);
            Assert.That(first.Attempts, Is.EqualTo(second.Attempts));
            foreach (var p in board.ActivePositions())
                Assert.That(first.Occupants[p], Is.EqualTo(second.Occupants[p]));
            Assert.That(probe.HasInitialMatch(first.Occupants), Is.False);
            Assert.That(probe.FindRealLegalMoves(first.Occupants).HasMoves, Is.True);
            Assert.That(game.GetSnapshot(), Is.EqualTo(metrics.InitialBoardSnapshot));
            Assert.That(metrics.MovesRemaining, Is.EqualTo(2));
            Assert.That(metrics.Score, Is.EqualTo(0));
        }

        [UnityTest]
        public IEnumerator SeededInitialSpawnUsesRealSdkSolverOnEveryCandidate()
        {
            var metrics = new DemoMetrics(20261013);
            var game = new DemoGame(metrics);
            game.InitializeAsync().Forget();
            yield return new WaitUntil(() => metrics.InitialBoardSnapshot.Length > 0);
            var empty = new BoardTopology(4, 4);
            var probe = new NeutralSdkSnapshotValidator(empty);

            Func<IReadOnlyDictionary<BoardPosition, string>, bool> sdkValid =
                candidate => !probe.HasInitialMatch(candidate) &&
                    probe.FindRealLegalMoves(candidate).HasMoves;
            var result = DeterministicBoardPlanning.TrySpawn(
                empty, new[] { "A", "B", "C", "D" }, new SeededRandom(55u),
                sdkValid, 128);
            Assert.That(result.Succeeded, Is.True);
            Assert.That(result.Occupants.Count, Is.EqualTo(16));
            Assert.That(probe.HasInitialMatch(result.Occupants), Is.False);
            Assert.That(probe.FindRealLegalMoves(result.Occupants).HasMoves, Is.True);
            Assert.That(game.GetSnapshot(), Is.EqualTo(metrics.InitialBoardSnapshot));
            Assert.That(metrics.MovesRemaining, Is.EqualTo(2));
        }
    }
}
