using System;
using System.Collections.Generic;

namespace Match3.Engine.Runtime
{
    /// <summary>
    /// Classifies the coordinates of an already detected SDK match. This class never
    /// scans a board, removes pieces, awards rewards, or resolves cascades.
    /// </summary>
    public static class SdkMatchGeometry
    {
        public enum Shape { Invalid, Three, FourHorizontal, FourVertical, FiveOrMore, L, T }

        public static Shape Classify(IReadOnlyList<(int x, int y)> sdkMatchedCells)
        {
            if (sdkMatchedCells == null || sdkMatchedCells.Count < 3)
                return Shape.Invalid;

            var cells = new HashSet<(int x, int y)>();
            foreach (var cell in sdkMatchedCells)
                if (!cells.Add(cell)) return Shape.Invalid;

            var horizontal = new Dictionary<int, HashSet<int>>();
            var vertical = new Dictionary<int, HashSet<int>>();
            foreach (var cell in cells)
            {
                if (!horizontal.TryGetValue(cell.y, out var xs))
                    horizontal[cell.y] = xs = new HashSet<int>();
                xs.Add(cell.x);
                if (!vertical.TryGetValue(cell.x, out var ys))
                    vertical[cell.x] = ys = new HashSet<int>();
                ys.Add(cell.y);
            }

            var hRuns = Runs(horizontal);
            var vRuns = Runs(vertical);
            if (hRuns.Count == 0 && vRuns.Count == 0) return Shape.Invalid;

            foreach (var run in hRuns)
                if (run.Count >= 5) return Shape.FiveOrMore;
            foreach (var run in vRuns)
                if (run.Count >= 5) return Shape.FiveOrMore;

            foreach (var h in hRuns)
            foreach (var v in vRuns)
            {
                int ix = v.Start;
                int iy = h.Start;
                if (ix < h.Start || ix >= h.Start + h.Count ||
                    iy < v.Start || iy >= v.Start + v.Count) continue;
                bool hMiddle = ix > h.Start && ix < h.Start + h.Count - 1;
                bool vMiddle = iy > v.Start && iy < v.Start + v.Count - 1;
                return hMiddle || vMiddle ? Shape.T : Shape.L;
            }

            if (hRuns.Count == 1 && vRuns.Count == 0 && cells.Count == hRuns[0].Count)
                return hRuns[0].Count == 4 ? Shape.FourHorizontal : Shape.Three;
            if (vRuns.Count == 1 && hRuns.Count == 0 && cells.Count == vRuns[0].Count)
                return vRuns[0].Count == 4 ? Shape.FourVertical : Shape.Three;
            return Shape.Invalid;
        }

        private readonly struct Run
        {
            public readonly int Start;
            public readonly int Count;
            public Run(int start, int count) { Start = start; Count = count; }
        }

        private static List<Run> Runs(Dictionary<int, HashSet<int>> lines)
        {
            var result = new List<Run>();
            foreach (var line in lines)
            {
                var positions = new List<int>(line.Value);
                positions.Sort();
                int start = 0;
                while (start < positions.Count)
                {
                    int end = start + 1;
                    while (end < positions.Count && positions[end] == positions[end - 1] + 1)
                        end++;
                    if (end - start >= 3) result.Add(new Run(positions[start], end - start));
                    start = end;
                }
            }
            return result;
        }
    }
}
