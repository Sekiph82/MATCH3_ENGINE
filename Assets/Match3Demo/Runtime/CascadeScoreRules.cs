using System;

namespace Match3.Engine.Demo
{
    /// <summary>Original demo scoring contract for SDK match waves within one accepted move.</summary>
    public static class CascadeScoreRules
    {
        public const int PointsPerClearedItem = 10;
        public const int MaximumMultiplier = 4;

        public static int GetMultiplier(int cascadeIndex)
        {
            if (cascadeIndex < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(cascadeIndex));
            }

            return Math.Min(cascadeIndex + 1, MaximumMultiplier);
        }

        public static int GetAward(int clearedItemCount, int cascadeIndex)
        {
            if (clearedItemCount < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(clearedItemCount));
            }

            return clearedItemCount * PointsPerClearedItem * GetMultiplier(cascadeIndex);
        }
    }
}
