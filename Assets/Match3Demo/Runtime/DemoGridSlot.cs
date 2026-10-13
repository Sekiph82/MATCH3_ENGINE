using Match3.Core.Interfaces;
using Match3.Core.Structs;

namespace Match3.Engine.Demo
{
    public sealed class DemoGridSlot : IGridSlot
    {
        private readonly DemoTileState _state;

        public DemoGridSlot(GridPosition gridPosition, bool isLocked = false)
        {
            GridPosition = gridPosition;
            _state = new DemoTileState(isLocked);
        }

        public int ItemId => Item?.ItemId ?? -1;
        public bool HasItem => Item != null;
        public bool IsMovable => HasItem && _state.CanContainItem && !_state.IsLocked;
        public bool CanContainItem => _state.CanContainItem;
        public IGridSlotState State => _state;
        public GridPosition GridPosition { get; }
        public DemoItem Item { get; private set; }

        public void SetItem(DemoItem item)
        {
            Item = item;
        }

        public void Clear()
        {
            Item = null;
        }
    }

    internal sealed class DemoTileState : IGridSlotState
    {
        public DemoTileState(bool isLocked)
        {
            IsLocked = isLocked;
        }

        public int GroupId => 0;
        public bool IsLocked { get; }
        public bool CanContainItem => true;
    }
}
