using System;
using System.Collections.Generic;
using Match3.App;
using Match3.Core.Interfaces;
using Match3.Core.Structs;
using NUnit.Framework;

namespace Match3.Engine.Demo.Tests
{
    public sealed class CleanRoomBehaviorContractTests
    {
        [Test]
        public void TargetCollectionCountsDistinctMovableMatchingItemsAndCompletesOnce()
        {
            var firstTarget = new FixtureSlot(0, true, true);
            var secondTarget = new FixtureSlot(0, true, true);
            var wrongItem = new FixtureSlot(1, true, true);
            var lockedTarget = new FixtureSlot(0, true, false);
            var goal = new SdkTargetCollectionGoal<FixtureSlot>(3, slot => slot.ItemId == 0);
            var achievedEvents = 0;
            goal.Achieved += (_, _) => achievedEvents++;

            goal.OnSequencesSolved(Solved(firstTarget, secondTarget, wrongItem, lockedTarget,
                firstTarget));

            Assert.That(goal.CollectedCount, Is.EqualTo(2));
            Assert.That(goal.IsAchieved, Is.False);

            goal.OnSequencesSolved(Solved(new FixtureSlot(0, true, true)));
            goal.OnSequencesSolved(Solved(new FixtureSlot(0, true, true)));

            Assert.That(goal.CollectedCount, Is.EqualTo(3));
            Assert.That(goal.IsAchieved, Is.True);
            Assert.That(achievedEvents, Is.EqualTo(1));
        }

        [Test]
        public void TargetCollectionRejectsInvalidConfigurationAndIgnoresNonmatches()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new SdkTargetCollectionGoal<FixtureSlot>(0, slot => slot.ItemId == 0));
            Assert.Throws<ArgumentNullException>(() =>
                new SdkTargetCollectionGoal<FixtureSlot>(1, null));

            var goal = new SdkTargetCollectionGoal<FixtureSlot>(2, slot => slot.ItemId == 0);
            goal.OnSequencesSolved(Solved(
                new FixtureSlot(1, true, true),
                new FixtureSlot(0, false, true),
                new FixtureSlot(0, true, false)));

            Assert.That(goal.CollectedCount, Is.Zero);
            Assert.That(goal.IsAchieved, Is.False);
        }

        [Test]
        public void CascadeScoreRisesByWaveAndStopsAtTheConfiguredCap()
        {
            Assert.That(CascadeScoreRules.GetAward(3, 0), Is.EqualTo(30));
            Assert.That(CascadeScoreRules.GetAward(3, 1), Is.EqualTo(60));
            Assert.That(CascadeScoreRules.GetAward(3, 5), Is.EqualTo(120));
            Assert.That(CascadeScoreRules.GetAward(0, 1), Is.Zero);
        }

        [Test]
        public void CascadeScoreRejectsNegativeInputs()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => CascadeScoreRules.GetMultiplier(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => CascadeScoreRules.GetAward(-1, 0));
        }

        private static SolvedData<FixtureSlot> Solved(params FixtureSlot[] slots)
        {
            return new SolvedData<FixtureSlot>(new[]
            {
                new ItemSequence<FixtureSlot>(typeof(CleanRoomBehaviorContractTests), slots)
            }, Array.Empty<FixtureSlot>());
        }

        private sealed class FixtureSlot : IGridSlot
        {
            private readonly FixtureSlotState _state;

            public FixtureSlot(int itemId, bool hasItem, bool movable)
            {
                ItemId = itemId;
                HasItem = hasItem;
                _state = new FixtureSlotState(movable);
            }

            public int ItemId { get; }
            public bool HasItem { get; }
            public bool IsMovable => HasItem && _state.CanContainItem && !_state.IsLocked;
            public bool CanContainItem => _state.CanContainItem;
            public IGridSlotState State => _state;
            public GridPosition GridPosition => new GridPosition(0, 0);
        }

        private sealed class FixtureSlotState : IGridSlotState
        {
            public FixtureSlotState(bool movable)
            {
                IsLocked = !movable;
            }

            public int GroupId => 0;
            public bool IsLocked { get; }
            public bool CanContainItem => true;
        }
    }
}
