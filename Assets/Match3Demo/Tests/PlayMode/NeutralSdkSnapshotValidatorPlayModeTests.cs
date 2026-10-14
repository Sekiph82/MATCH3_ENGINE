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

        private static BoardTopology FromRows(params string[] rows)
        {
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
            Assert.That(found.Accepted.Contains(new SwapCandidate(P(0, 0), P(0, 1))), Is.False);
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

        [Test]
        public void FullyLockedOriginalSdkSnapshotReturnsTypedNoMoveWithoutLiveMutation()
        {
            var occupants = new List<BoardOccupant>();
            var locks = new List<BoardPosition>();
            for (var row = 0; row < 3; row++)
                for (var col = 0; col < 3; col++)
                {
                    var cell = P(row, col);
                    locks.Add(cell);
                    occupants.Add(new BoardOccupant(cell,
                        ((char)('A' + (row + col) % 3)).ToString()));
                }

            var board = new BoardTopology(3, 3, locked: locks, occupants: occupants);
            var probe = new NeutralSdkSnapshotValidator(board);
            var snapshot = SafeRecoveryBoundary.HashBoard(board.Occupants);
            var noMoves = probe.FindRealLegalMoves(board.Occupants);
            Assert.That(noMoves.HasMoves, Is.False);
            Assert.That(noMoves.CandidatesChecked, Is.EqualTo(0));
            Assert.That(noMoves.Accepted, Is.Empty);
            Assert.That(SafeRecoveryBoundary.HashBoard(board.Occupants),
                Is.EqualTo(snapshot));
        }


        [Test]
        public void PinnedSdkProjectionFeedsThreeFourFivePlansWithoutRivalMatcher()
        {
            var h3 = FromRows("AAAB", "BCDA", "CDAB", "DABC");
            var h3Probe = new NeutralSdkSnapshotValidator(h3);
            var h3Runs = h3Probe.GetSdkMatchRuns(h3.Occupants);
            Assert.That(h3Runs.Count, Is.EqualTo(1));
            Assert.That(h3Runs[0].Axis, Is.EqualTo(MatchRunAxis.Horizontal));
            Assert.That(h3Runs[0].Count, Is.EqualTo(3));
            var h3Plan = SpecialMatchPlanner.Plan(h3Runs).Single();
            Assert.That(h3Plan.Shape, Is.EqualTo(MatchShapeKind.Three));
            Assert.That(h3Plan.Special, Is.EqualTo(MatchSpecialKind.None));
            Assert.That(h3Plan.RewardCellCount, Is.EqualTo(3));

            var v3 = FromRows("ABCD", "ACDB", "ADBC", "BACD");
            var v3Runs = new NeutralSdkSnapshotValidator(v3).GetSdkMatchRuns(v3.Occupants);
            Assert.That(v3Runs.Count, Is.EqualTo(1));
            Assert.That(v3Runs[0].Axis, Is.EqualTo(MatchRunAxis.Vertical));
            Assert.That(v3Runs[0].Count, Is.EqualTo(3));

            var h4 = FromRows("AAAA", "BCDB", "CDBC", "DBCD");
            var h4Runs = new NeutralSdkSnapshotValidator(h4).GetSdkMatchRuns(h4.Occupants);
            var h4Plan = SpecialMatchPlanner.Plan(h4Runs, new[] { P(0, 2) }).Single();
            Assert.That(h4Plan.Shape, Is.EqualTo(MatchShapeKind.StraightFour));
            Assert.That(h4Plan.Special, Is.EqualTo(MatchSpecialKind.HorizontalLine));
            Assert.That(h4Plan.Anchor, Is.EqualTo(P(0, 2)));
            Assert.That(h4Plan.RewardCellCount, Is.EqualTo(4));

            var v4 = FromRows("ABCD", "ACDB", "ADBC", "ABCD");
            var v4Runs = new NeutralSdkSnapshotValidator(v4).GetSdkMatchRuns(v4.Occupants);
            Assert.That(v4Runs.Count, Is.EqualTo(1));
            var v4Plan = SpecialMatchPlanner.Plan(v4Runs, new[] { P(2, 0) }).Single();
            Assert.That(v4Plan.Shape, Is.EqualTo(MatchShapeKind.StraightFour));
            Assert.That(v4Plan.Special, Is.EqualTo(MatchSpecialKind.VerticalLine));
            Assert.That(v4Plan.Anchor, Is.EqualTo(P(2, 0)));
            Assert.That(v4Plan.RewardCellCount, Is.EqualTo(4));

            var h5 = FromRows("AAAAA", "BCDAB", "CDABC", "DABCD", "ABCDD");
            var h5Probe = new NeutralSdkSnapshotValidator(h5);
            var firstRuns = h5Probe.GetSdkMatchRuns(h5.Occupants);
            var secondRuns = h5Probe.GetSdkMatchRuns(h5.Occupants);
            Assert.That(firstRuns.Select(x => x.Count).ToArray(),
                Is.EqualTo(secondRuns.Select(x => x.Count).ToArray()));
            var h5Plan = SpecialMatchPlanner.Plan(firstRuns, new[] { P(0, 3) }).Single();
            Assert.That(h5Plan.Shape, Is.EqualTo(MatchShapeKind.StraightFivePlus));
            Assert.That(h5Plan.Special, Is.EqualTo(MatchSpecialKind.Color));
            Assert.That(h5Plan.Anchor, Is.EqualTo(P(0, 3)));
            Assert.That(h5Plan.RewardCellCount, Is.EqualTo(5));

            var h6 = FromRows(
                "AAAAAA",
                "BCDABC",
                "CDABCD",
                "DABCDA",
                "ABCDAB",
                "BCDABC");
            var h6Runs = new NeutralSdkSnapshotValidator(h6).GetSdkMatchRuns(h6.Occupants);
            Assert.That(h6Runs.Count, Is.EqualTo(1));
            var h6Plan = SpecialMatchPlanner.Plan(h6Runs, new[] { P(0, 4) }).Single();
            Assert.That(h6Plan.Shape, Is.EqualTo(MatchShapeKind.StraightFivePlus));
            Assert.That(h6Plan.Special, Is.EqualTo(MatchSpecialKind.Color));
            Assert.That(h6Plan.Anchor, Is.EqualTo(P(0, 4)));
            Assert.That(h6Plan.RewardCellCount, Is.EqualTo(6));
        }

        [Test]
        public void PinnedSdkCrossProjectionCreatesSingleLOrTPlanWithoutDoubleReward()
        {
            var lBoard = FromRows(
                "BCDAB",
                "CDBCD",
                "BCAAA",
                "DAACD",
                "BCADB");
            var lRuns = new NeutralSdkSnapshotValidator(lBoard)
                .GetSdkMatchRuns(lBoard.Occupants);
            Assert.That(lRuns.Count, Is.EqualTo(2));
            Assert.That(lRuns.Select(x => x.Axis).Distinct().Count(), Is.EqualTo(2));
            var l = SpecialMatchPlanner.Plan(lRuns).Single();
            Assert.That(l.Shape, Is.EqualTo(MatchShapeKind.L));
            Assert.That(l.Special, Is.EqualTo(MatchSpecialKind.Area));
            Assert.That(l.Anchor, Is.EqualTo(P(2, 2)));
            Assert.That(l.RewardCellCount, Is.EqualTo(5));
            Assert.That(l.MatchedCells.Distinct().Count(), Is.EqualTo(5));

            var tBoard = FromRows(
                "BCDAB",
                "CDBCD",
                "BAAAB",
                "DAACD",
                "BCADB");
            var tRuns = new NeutralSdkSnapshotValidator(tBoard)
                .GetSdkMatchRuns(tBoard.Occupants);
            Assert.That(tRuns.Count, Is.EqualTo(2));
            var t = SpecialMatchPlanner.Plan(tRuns).Single();
            Assert.That(t.Shape, Is.EqualTo(MatchShapeKind.T));
            Assert.That(t.Special, Is.EqualTo(MatchSpecialKind.Area));
            Assert.That(t.Anchor, Is.EqualTo(P(2, 2)));
            Assert.That(t.RewardCellCount, Is.EqualTo(5));
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
