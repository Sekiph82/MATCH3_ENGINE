using Match3.App.Interfaces;
using Match3.Core.Structs;

namespace Match3.Engine.Demo
{
    internal sealed class DemoBoardDataProvider : IGameBoardDataProvider<DemoGridSlot>
    {
        public DemoGridSlot[,] GetGameBoardSlots(int level)
        {
            if (level != DemoScenario.Level)
            {
                throw new System.ArgumentOutOfRangeException(nameof(level));
            }

            var slots = new DemoGridSlot[DemoScenario.Rows, DemoScenario.Columns];
            for (var row = 0; row < DemoScenario.Rows; row++)
            {
                for (var column = 0; column < DemoScenario.Columns; column++)
                {
                    slots[row, column] = new DemoGridSlot(new GridPosition(row, column));
                }
            }

            return slots;
        }
    }

    internal static class DemoScenario
    {
        public const int Seed = 20261013;
        public const int Level = 0;
        public const int Rows = 4;
        public const int Columns = 4;
        public const int StartingMoves = 2;
        public const int GoalItemCount = 3;

        // Row-major deterministic fixture. Swapping (1,0) with (2,0) makes the bottom row a triple.
        public static readonly int[,] StartingItems =
        {
            { 2, 3, 2, 1 },
            { 0, 1, 2, 3 },
            { 1, 0, 0, 2 },
            { 3, 2, 1, 0 }
        };
    }
}
