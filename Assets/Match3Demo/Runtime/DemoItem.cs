namespace Match3.Engine.Demo
{
    public sealed class DemoItem
    {
        public DemoItem(int itemId)
        {
            ItemId = itemId;
        }

        public int ItemId { get; }

        public char Label => (char)('A' + ItemId);
    }
}
