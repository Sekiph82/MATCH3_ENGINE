using System;
using System.Collections.Generic;
using System.Linq;
using Match3.Engine.Runtime;
using NUnit.Framework;

namespace Match3.Engine.Tests
{
    public sealed class CanonicalMoveCandidateTests
    {
        private static BoardPosition P(int r, int c, int layer = 0) =>
            new BoardPosition(r, c, layer);

        private static BoardTopology FilledTwoByTwo() =>
            new BoardTopology(2, 2, occupants: new[]
            {
                new BoardOccupant(P(0, 0), "A"),
                new BoardOccupant(P(0, 1), "B"),
                new BoardOccupant(P(1, 0), "C"),
                new BoardOccupant(P(1, 1), "D")
            });

        [Test]
        public void CanonicalEdgesEnumerateExactlyOnceInStableRightThenDownOrder()
        {
            var checks = new List<SwapCandidate>();
            var r = CanonicalMoveCandidates.Find(FilledTwoByTwo(),
                candidate => { checks.Add(candidate); return true; });
            Assert.That(r.HasMoves, Is.True);
            Assert.That(r.CandidatesChecked, Is.EqualTo(4));
            Assert.That(checks.Count, Is.EqualTo(4));
            Assert.That(r.Accepted.Count, Is.EqualTo(4));
            Assert.That(checks.Distinct().Count(), Is.EqualTo(4));
            Assert.That(checks[0], Is.EqualTo(new SwapCandidate(P(0, 0), P(0, 1))));
            Assert.That(checks[1], Is.EqualTo(new SwapCandidate(P(0, 0), P(1, 0))));
            Assert.That(checks[2], Is.EqualTo(new SwapCandidate(P(0, 1), P(1, 1))));
            Assert.That(checks[3], Is.EqualTo(new SwapCandidate(P(1, 0), P(1, 1))));
            Assert.Throws<NotSupportedException>(() =>
                ((IList<SwapCandidate>)r.Accepted).Add(checks[0]));
        }

        [Test]
        public void ResolverIsSoleAuthorityToAcceptCandidate()
        {
            var desired = new SwapCandidate(P(0, 0), P(1, 0));
            var checkedCount = 0;
            var result = CanonicalMoveCandidates.Find(FilledTwoByTwo(), x =>
            {
                checkedCount++;
                return x.Equals(desired);
            });
            Assert.That(checkedCount, Is.EqualTo(4));
            Assert.That(result.CandidatesChecked, Is.EqualTo(4));
            Assert.That(result.Accepted.Count, Is.EqualTo(1));
            Assert.That(result.Accepted[0], Is.EqualTo(desired));

            // Even a fully occupied board MUST report no legal match when
            // its actual upstream SDK validator finds none.
            var noMoves = CanonicalMoveCandidates.Find(FilledTwoByTwo(), x => false);
            Assert.That(noMoves.HasMoves, Is.False);
            Assert.That(noMoves.Accepted, Is.Empty);
        }

        [Test]
        public void HolesLocksAndEmptyCellsAreNotPresentedToSdkValidator()
        {
            var board = new BoardTopology(3, 3,
                holes: new[] { P(1, 1) },
                locked: new[] { P(2, 0) },
                occupants: new[]
                {
                    new BoardOccupant(P(0, 0), "A"),
                    new BoardOccupant(P(0, 1), "B"),
                    new BoardOccupant(P(1, 0), "C"),
                    new BoardOccupant(P(2, 0), "D")
                });
            var result = CanonicalMoveCandidates.Find(board, x =>
            {
                Assert.That(board.IsSwapCandidate(x.Source, x.Target), Is.True);
                return true;
            });
            Assert.That(result.CandidatesChecked, Is.EqualTo(2));
            Assert.That(result.Accepted.Count, Is.EqualTo(2));
            Assert.That(result.Accepted.All(x => !board.IsLocked(x.Target)), Is.True);
        }

        [Test]
        public void CandidateBudgetFailsClosedWithoutPartialSuccess()
        {
            var calls = 0;
            Assert.Throws<InvalidOperationException>(() =>
                CanonicalMoveCandidates.Find(FilledTwoByTwo(), x =>
                {
                    calls++;
                    return true;
                }, maximumCandidates: 2));
            Assert.That(calls, Is.EqualTo(2));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                CanonicalMoveCandidates.Find(FilledTwoByTwo(), x => true, 0));
        }

        [Test]
        public void AbsentOrBrokenSdkValidatorNeverInventsMoves()
        {
            Assert.Throws<ArgumentNullException>(() =>
                CanonicalMoveCandidates.Find(null, x => true));
            Assert.Throws<ArgumentNullException>(() =>
                CanonicalMoveCandidates.Find(FilledTwoByTwo(), null));
            Assert.Throws<InvalidOperationException>(() =>
                CanonicalMoveCandidates.Find(FilledTwoByTwo(), x =>
                    { throw new InvalidOperationException("SDK not available"); }));
        }

        [Test]
        public void DifferentLayersNeverCrossMatchOrDuplicateAnEdge()
        {
            var a = new List<BoardOccupant>();
            for (var layer = 0; layer < 2; layer++)
                for (var row = 0; row < 2; row++)
                    for (var column = 0; column < 2; column++)
                        a.Add(new BoardOccupant(P(row, column, layer), "X"));
            var board = new BoardTopology(2, 2, 2, occupants: a);
            var result = CanonicalMoveCandidates.Find(board, x => true);
            Assert.That(result.CandidatesChecked, Is.EqualTo(8));
            Assert.That(result.Accepted.Count, Is.EqualTo(8));
            Assert.That(result.Accepted.All(x =>
                x.Source.Layer == x.Target.Layer), Is.True);
        }
    }
}
