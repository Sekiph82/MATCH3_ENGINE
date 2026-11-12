using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Match3.Engine.Runtime.Tests")]
// Editor tests assert registry state via the engine-internal Ad*Registry.Current (ENG-M14-002/003).
[assembly: InternalsVisibleTo("Match3.Engine.Editor.Tests")]

namespace Match3.Engine.Runtime
{
    internal static class AssemblyMarker
    {
    }
}
