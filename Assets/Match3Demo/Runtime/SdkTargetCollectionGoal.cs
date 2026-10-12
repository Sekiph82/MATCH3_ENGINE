using System;
using System.Collections.Generic;
using Match3.App;
using Match3.Core.Interfaces;

namespace Match3.Engine.Demo
{
    /// <summary>Counts distinct movable SDK-cleared slots that contain the configured target item.</summary>
    public sealed class SdkTargetCollectionGoal<TGridSlot> : LevelGoal<TGridSlot>
        where TGridSlot : IGridSlot
    {
        private readonly Func<TGridSlot, bool> _isTargetItem;

        public SdkTargetCollectionGoal(int targetCount, Func<TGridSlot, bool> isTargetItem)
        {
            if (targetCount <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(targetCount), "The target count must be positive.");
            }

            _isTargetItem = isTargetItem ?? throw new ArgumentNullException(nameof(isTargetItem));
            TargetCount = targetCount;
        }

        public int TargetCount { get; }
        public int CollectedCount { get; private set; }

        public override void OnSequencesSolved(SolvedData<TGridSlot> solvedData)
        {
            if (IsAchieved)
            {
                return;
            }

            if (solvedData == null)
            {
                throw new ArgumentNullException(nameof(solvedData));
            }

            var uniqueSlots = new HashSet<TGridSlot>();
            foreach (var slot in solvedData.GetSolvedGridSlots(true))
            {
                if (slot != null && slot.HasItem && uniqueSlots.Add(slot) && _isTargetItem(slot))
                {
                    CollectedCount++;
                    if (CollectedCount == TargetCount)
                    {
                        MarkAchieved();
                        return;
                    }
                }
            }
        }
    }
}
