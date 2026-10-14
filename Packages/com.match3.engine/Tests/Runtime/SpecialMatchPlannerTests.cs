using System;
using System.Linq;
using Match3.Engine.Runtime;
using NUnit.Framework;

namespace Match3.Engine.Tests
{
    public sealed class SpecialMatchPlannerTests
    {
        private static BoardPosition P(int r, int c) => new BoardPosition(r, c);

        private static MatchRunProjection H(string id, int row, params int[] columns) =>
            new MatchRunProjection(MatchRunAxis.Horizontal, id,
                columns.Select(c => P(row, c)));

        private static MatchRunProjection V(string id, int column, params int[] rows) =>
            new MatchRunProjection(MatchRunAxis.Vertical, id,
                rows.Select(r => P(r, column)));

        [Test]
        public void ThreeMatchDeduplicatesSdkRunsAndOrdersEventsStably()
        {
            var a = H("A", 2, 1, 2, 3);
            var duplicate = H("A", 2, 1, 2, 3);
            var b = V("B", 4, 0, 1, 2);

            var first = SpecialMatchPlanner.Plan(new[] { b, duplicate, a });
            var second = SpecialMatchPlanner.Plan(new[] { a, b, duplicate });

            Assert.That(first.Count, Is.EqualTo(2));
            Assert.That(first.All(x => x.Shape == MatchShapeKind.Three), Is.True);
            Assert.That(first.All(x => x.Special == MatchSpecialKind.None), Is.True);
            Assert.That(first.Sum(x => x.RewardCellCount), Is.EqualTo(6));
            Assert.That(first.Select(x => x.ContentId).ToArray(),
                Is.EqualTo(second.Select(x => x.ContentId).ToArray()));
            Assert.That(first.Select(x => x.Anchor.ToString()).ToArray(),
                Is.EqualTo(second.Select(x => x.Anchor.ToString()).ToArray()));
        }

        [Test]
        public void StraightFourUsesSdkOrientationAndPreferredAnchorDeterministically()
        {
            var preferred = P(1, 3);
            var horizontal = SpecialMatchPlanner.Plan(
                new[] { H("A", 1, 1, 2, 3, 4) },
                new[] { preferred, P(9, 9) }).Single();
            Assert.That(horizontal.Shape, Is.EqualTo(MatchShapeKind.StraightFour));
            Assert.That(horizontal.Special, Is.EqualTo(MatchSpecialKind.HorizontalLine));
            Assert.That(horizontal.Anchor, Is.EqualTo(preferred));
            Assert.That(horizontal.RewardCellCount, Is.EqualTo(4));

            var vertical = SpecialMatchPlanner.Plan(
                new[] { V("B", 2, 0, 1, 2, 3) }).Single();
            Assert.That(vertical.Special, Is.EqualTo(MatchSpecialKind.VerticalLine));
            Assert.That(vertical.Anchor, Is.EqualTo(P(0, 2)));
        }

        [Test]
        public void FivePlusHasColorPriorityAndDuplicateProjectionCannotDuplicateReward()
        {
            var five = H("C", 3, 0, 1, 2, 3, 4);
            var duplicate = H("C", 3, 0, 1, 2, 3, 4);
            var plan = SpecialMatchPlanner.Plan(
                new[] { duplicate, five },
                new[] { P(3, 2) }).Single();

            Assert.That(plan.Shape, Is.EqualTo(MatchShapeKind.StraightFivePlus));
            Assert.That(plan.Special, Is.EqualTo(MatchSpecialKind.Color));
            Assert.That(plan.Anchor, Is.EqualTo(P(3, 2)));
            Assert.That(plan.RewardCellCount, Is.EqualTo(5));
            Assert.That(plan.MatchedCells.Distinct().Count(), Is.EqualTo(5));

            var six = H("D", 4, 0, 1, 2, 3, 4, 5);
            var sixPlan = SpecialMatchPlanner.Plan(new[] { six }).Single();
            Assert.That(sixPlan.Shape, Is.EqualTo(MatchShapeKind.StraightFivePlus));
            Assert.That(sixPlan.Special, Is.EqualTo(MatchSpecialKind.Color));
            Assert.That(sixPlan.RewardCellCount, Is.EqualTo(6));

            var fiveWithFourConflict = SpecialMatchPlanner.Plan(new[]
            {
                H("E", 6, 0, 1, 2, 3, 4),
                V("E", 2, 5, 6, 7, 8)
            }, new[] { P(6, 2) }).Single();
            Assert.That(fiveWithFourConflict.Shape, Is.EqualTo(MatchShapeKind.StraightFivePlus));
            Assert.That(fiveWithFourConflict.Special, Is.EqualTo(MatchSpecialKind.Color));
            Assert.That(fiveWithFourConflict.Anchor, Is.EqualTo(P(6, 2)));
            Assert.That(fiveWithFourConflict.RewardCellCount, Is.EqualTo(8));
        }

        [Test]
        public void LAndTIntersectionsCreateOneAreaPlanAndCountSharedCellOnce()
        {
            var l = SpecialMatchPlanner.Plan(new[]
            {
                H("A", 2, 2, 3, 4),
                V("A", 2, 2, 3, 4)
            }).Single();
            Assert.That(l.Shape, Is.EqualTo(MatchShapeKind.L));
            Assert.That(l.Special, Is.EqualTo(MatchSpecialKind.Area));
            Assert.That(l.Anchor, Is.EqualTo(P(2, 2)));
            Assert.That(l.RewardCellCount, Is.EqualTo(5));

            var t = SpecialMatchPlanner.Plan(new[]
            {
                H("B", 2, 1, 2, 3),
                V("B", 2, 2, 3, 4)
            }).Single();
            Assert.That(t.Shape, Is.EqualTo(MatchShapeKind.T));
            Assert.That(t.Special, Is.EqualTo(MatchSpecialKind.Area));
            Assert.That(t.Anchor, Is.EqualTo(P(2, 2)));
            Assert.That(t.RewardCellCount, Is.EqualTo(5));

            var disjointSameContent = SpecialMatchPlanner.Plan(new[]
            {
                H("C", 8, 0, 1, 2),
                H("C", 10, 0, 1, 2)
            });
            Assert.That(disjointSameContent.Count, Is.EqualTo(2));
            Assert.That(disjointSameContent.All(x => x.Shape == MatchShapeKind.Three), Is.True);
        }

        [Test]
        public void MalformedOrSubthresholdSdkProjectionFailsClosed()
        {
            Assert.Throws<ArgumentException>(() =>
                new MatchRunProjection(MatchRunAxis.Horizontal, "A",
                    new[] { P(0, 0), P(0, 1) }));
            Assert.Throws<ArgumentException>(() =>
                new MatchRunProjection(MatchRunAxis.Horizontal, "A",
                    new[] { P(0, 0), P(0, 1), P(0, 3) }));
            Assert.Throws<ArgumentException>(() =>
                new MatchRunProjection(MatchRunAxis.Vertical, "A",
                    new[] { P(0, 0), P(1, 0), P(1, 0) }));
            Assert.Throws<ArgumentException>(() =>
                SpecialMatchPlanner.Plan(new MatchRunProjection[] { null }));
        }
    }
}
