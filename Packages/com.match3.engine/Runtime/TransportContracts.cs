using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Match3.Engine.Runtime
{
    public readonly struct ConveyorEdge
    {
        public BoardPosition Source { get; }
        public BoardPosition Target { get; }
        public int Priority { get; }

        public ConveyorEdge(BoardPosition source, BoardPosition target, int priority = 0)
        {
            if (source.Equals(target))
                throw new ArgumentException("Conveyor edge must move to another cell.");
            if (source.Layer != target.Layer)
                throw new ArgumentException("Conveyor edge cannot cross layers.");
            if (priority < 0) throw new ArgumentOutOfRangeException(nameof(priority));
            Source = source;
            Target = target;
            Priority = priority;
        }
    }

    public sealed class TransportPlan
    {
        public IReadOnlyDictionary<BoardPosition, string> Occupants { get; }
        public IReadOnlyList<BoardDisplacement> Moves { get; }
        public int Operations { get; }

        internal TransportPlan(IDictionary<BoardPosition, string> occupants,
            IEnumerable<BoardDisplacement> moves, int operations)
        {
            Occupants = new ReadOnlyDictionary<BoardPosition, string>(
                new Dictionary<BoardPosition, string>(occupants));
            Moves = new ReadOnlyCollection<BoardDisplacement>(moves.ToList());
            Operations = operations;
        }
    }

    public static class ConveyorPlanner
    {
        public static TransportPlan Plan(BoardTopology topology,
            IReadOnlyDictionary<BoardPosition, string> occupants,
            IReadOnlyList<ConveyorEdge> edges, int maximumEdges = 4096)
        {
            if (topology == null) throw new ArgumentNullException(nameof(topology));
            if (occupants == null) throw new ArgumentNullException(nameof(occupants));
            if (edges == null) throw new ArgumentNullException(nameof(edges));
            if (maximumEdges < 1 || maximumEdges > 65536)
                throw new ArgumentOutOfRangeException(nameof(maximumEdges));
            if (edges.Count > maximumEdges)
                throw new InvalidOperationException("Conveyor edge budget exceeded.");

            ValidateOccupants(topology, occupants);
            var bySource = new Dictionary<BoardPosition, ConveyorEdge>();
            foreach (var edge in edges)
            {
                RequireActiveUnlocked(topology, edge.Source, nameof(edges));
                RequireActiveUnlocked(topology, edge.Target, nameof(edges));
                if (bySource.ContainsKey(edge.Source))
                    throw new ArgumentException("A conveyor source may have only one outgoing edge.",
                        nameof(edges));
                bySource.Add(edge.Source, edge);
            }
            RequireAcyclic(bySource);

            var candidates = edges
                .Where(e => occupants.ContainsKey(e.Source))
                .GroupBy(e => e.Target)
                .Select(g => g.OrderBy(e => e.Priority)
                    .ThenBy(e => e.Source.Layer)
                    .ThenBy(e => e.Source.Row)
                    .ThenBy(e => e.Source.Column).First())
                .ToDictionary(e => e.Source, e => e);

            var memo = new Dictionary<BoardPosition, bool>();
            var accepted = new List<ConveyorEdge>();
            foreach (var edge in candidates.Values.OrderBy(e => e.Priority)
                .ThenBy(e => e.Source.Layer).ThenBy(e => e.Source.Row)
                .ThenBy(e => e.Source.Column))
            {
                if (CanMove(edge, occupants, candidates, memo))
                    accepted.Add(edge);
            }

            var result = new Dictionary<BoardPosition, string>(occupants);
            foreach (var edge in accepted)
                result.Remove(edge.Source);
            var moves = new List<BoardDisplacement>();
            foreach (var edge in accepted.OrderBy(e => e.Priority)
                .ThenBy(e => e.Source.Layer).ThenBy(e => e.Source.Row)
                .ThenBy(e => e.Source.Column))
            {
                var content = occupants[edge.Source];
                if (result.ContainsKey(edge.Target))
                    throw new InvalidOperationException("Conveyor collision survived arbitration.");
                result.Add(edge.Target, content);
                moves.Add(new BoardDisplacement(edge.Source, edge.Target, content));
            }

            return new TransportPlan(result, moves, edges.Count + occupants.Count);
        }

        private static bool CanMove(ConveyorEdge edge,
            IReadOnlyDictionary<BoardPosition, string> occupants,
            IDictionary<BoardPosition, ConveyorEdge> selected,
            IDictionary<BoardPosition, bool> memo)
        {
            bool known;
            if (memo.TryGetValue(edge.Source, out known)) return known;
            if (!occupants.ContainsKey(edge.Target))
            {
                memo[edge.Source] = true;
                return true;
            }

            ConveyorEdge next;
            if (!selected.TryGetValue(edge.Target, out next))
            {
                memo[edge.Source] = false;
                return false;
            }
            var can = CanMove(next, occupants, selected, memo);
            memo[edge.Source] = can;
            return can;
        }

        private static void RequireAcyclic(IDictionary<BoardPosition, ConveyorEdge> edges)
        {
            var state = new Dictionary<BoardPosition, int>();
            foreach (var source in edges.Keys)
                Visit(source, edges, state);
        }

        private static void Visit(BoardPosition p,
            IDictionary<BoardPosition, ConveyorEdge> edges,
            IDictionary<BoardPosition, int> state)
        {
            int s;
            if (state.TryGetValue(p, out s))
            {
                if (s == 1) throw new ArgumentException("Conveyor cycle is not allowed.");
                if (s == 2) return;
            }
            state[p] = 1;
            ConveyorEdge edge;
            if (edges.TryGetValue(p, out edge) && edges.ContainsKey(edge.Target))
                Visit(edge.Target, edges, state);
            state[p] = 2;
        }

        private static void ValidateOccupants(BoardTopology topology,
            IReadOnlyDictionary<BoardPosition, string> occupants)
        {
            foreach (var pair in occupants)
            {
                if (!topology.Contains(pair.Key) || topology.IsHole(pair.Key) ||
                    string.IsNullOrWhiteSpace(pair.Value))
                    throw new ArgumentException("Invalid transport occupant.", nameof(occupants));
            }
        }

        private static void RequireActiveUnlocked(BoardTopology topology,
            BoardPosition p, string parameter)
        {
            if (!topology.Contains(p) || topology.IsHole(p) || topology.IsLocked(p))
                throw new ArgumentException("Transport edge targets inactive or locked cell.", parameter);
        }
    }

    public static class ConveyorSchema
    {
        public const int Version = 1;

        public static string Encode(IEnumerable<ConveyorEdge> edges)
        {
            if (edges == null) throw new ArgumentNullException(nameof(edges));
            var array = edges.OrderBy(e => e.Source.Layer).ThenBy(e => e.Source.Row)
                .ThenBy(e => e.Source.Column).ThenBy(e => e.Target.Row)
                .ThenBy(e => e.Target.Column).ThenBy(e => e.Priority).ToArray();
            return "v1|" + string.Join(";", array.Select(e =>
                Pos(e.Source) + ">" + Pos(e.Target) + "@" + e.Priority));
        }

        public static IReadOnlyList<ConveyorEdge> Decode(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || !value.StartsWith("v1|", StringComparison.Ordinal))
                throw new FormatException("Unsupported conveyor schema.");
            var body = value.Substring(3);
            if (body.Length == 0) return Array.AsReadOnly(new ConveyorEdge[0]);
            var result = new List<ConveyorEdge>();
            foreach (var token in body.Split(';'))
            {
                var at = token.LastIndexOf('@');
                var arrow = token.IndexOf('>');
                if (at < 0 || arrow < 0 || arrow > at)
                    throw new FormatException("Malformed conveyor edge.");
                int priority;
                if (!Int32.TryParse(token.Substring(at + 1), out priority))
                    throw new FormatException("Malformed conveyor priority.");
                result.Add(new ConveyorEdge(ParsePos(token.Substring(0, arrow)),
                    ParsePos(token.Substring(arrow + 1, at - arrow - 1)), priority));
            }
            return new ReadOnlyCollection<ConveyorEdge>(result);
        }

        internal static string Pos(BoardPosition p) =>
            p.Layer + "," + p.Row + "," + p.Column;

        internal static BoardPosition ParsePos(string value)
        {
            var parts = value.Split(',');
            int layer, row, col;
            if (parts.Length != 3 || !Int32.TryParse(parts[0], out layer) ||
                !Int32.TryParse(parts[1], out row) || !Int32.TryParse(parts[2], out col) ||
                layer < 0 || row < 0 || col < 0)
                throw new FormatException("Malformed board position.");
            return new BoardPosition(row, col, layer);
        }
    }

    public sealed class PortalPair
    {
        public string Id { get; }
        public BoardPosition First { get; }
        public BoardPosition Second { get; }

        public PortalPair(string id, BoardPosition first, BoardPosition second)
        {
            if (string.IsNullOrWhiteSpace(id))
                throw new ArgumentException("Portal ID required.", nameof(id));
            if (first.Equals(second))
                throw new ArgumentException("Portal endpoints must differ.");
            if (first.Layer != second.Layer)
                throw new ArgumentException("Portal pair cannot cross layers.");
            Id = id;
            First = first;
            Second = second;
        }

        public BoardPosition Other(BoardPosition p)
        {
            if (p.Equals(First)) return Second;
            if (p.Equals(Second)) return First;
            throw new ArgumentException("Position is not a portal endpoint.", nameof(p));
        }
    }

    public sealed class PortalRegistry
    {
        private readonly ReadOnlyCollection<PortalPair> _pairs;
        private readonly ReadOnlyDictionary<BoardPosition, PortalPair> _byPosition;

        public IReadOnlyList<PortalPair> Pairs => _pairs;

        public PortalRegistry(IEnumerable<PortalPair> pairs)
        {
            if (pairs == null) throw new ArgumentNullException(nameof(pairs));
            var array = pairs.ToArray();
            if (array.Any(x => x == null)) throw new ArgumentException("Null portal pair.");
            if (array.GroupBy(x => x.Id, StringComparer.Ordinal).Any(x => x.Count() != 1))
                throw new ArgumentException("Duplicate portal ID.");

            var map = new Dictionary<BoardPosition, PortalPair>();
            foreach (var pair in array)
            {
                if (map.ContainsKey(pair.First) || map.ContainsKey(pair.Second))
                    throw new ArgumentException("Portal endpoint may belong to only one pair.");
                map.Add(pair.First, pair);
                map.Add(pair.Second, pair);
            }
            _pairs = new ReadOnlyCollection<PortalPair>(array
                .OrderBy(x => x.Id, StringComparer.Ordinal).ToList());
            _byPosition = new ReadOnlyDictionary<BoardPosition, PortalPair>(map);
        }

        public bool TryGet(BoardPosition p, out PortalPair pair) =>
            _byPosition.TryGetValue(p, out pair);
    }

    public static class PortalPlanner
    {
        public static TransportPlan Plan(BoardTopology topology,
            IReadOnlyDictionary<BoardPosition, string> occupants,
            PortalRegistry registry)
        {
            if (topology == null) throw new ArgumentNullException(nameof(topology));
            if (occupants == null) throw new ArgumentNullException(nameof(occupants));
            if (registry == null) throw new ArgumentNullException(nameof(registry));

            var result = new Dictionary<BoardPosition, string>(occupants);
            var moves = new List<BoardDisplacement>();
            var operations = 0;
            foreach (var pair in registry.Pairs)
            {
                operations++;
                RequirePortalCell(topology, pair.First);
                RequirePortalCell(topology, pair.Second);
                string first, second;
                var hasFirst = occupants.TryGetValue(pair.First, out first);
                var hasSecond = occupants.TryGetValue(pair.Second, out second);
                if (hasFirst == hasSecond) continue;

                var source = hasFirst ? pair.First : pair.Second;
                var target = hasFirst ? pair.Second : pair.First;
                var content = hasFirst ? first : second;
                result.Remove(source);
                if (result.ContainsKey(target))
                    throw new InvalidOperationException("Portal target became occupied.");
                result.Add(target, content);
                moves.Add(new BoardDisplacement(source, target, content));
            }
            return new TransportPlan(result, moves, operations);
        }

        private static void RequirePortalCell(BoardTopology topology, BoardPosition p)
        {
            if (!topology.Contains(p) || topology.IsHole(p) || topology.IsLocked(p))
                throw new ArgumentException("Portal endpoint must be active and unlocked.");
        }
    }

    public static class PortalSchema
    {
        public const int Version = 1;

        public static string Encode(PortalRegistry registry)
        {
            if (registry == null) throw new ArgumentNullException(nameof(registry));
            return "v1|" + string.Join(";", registry.Pairs.Select(p =>
                EscapeId(p.Id) + "=" + ConveyorSchema.Pos(p.First) + "<>" +
                ConveyorSchema.Pos(p.Second)));
        }

        public static PortalRegistry Decode(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || !value.StartsWith("v1|", StringComparison.Ordinal))
                throw new FormatException("Unsupported portal schema.");
            var body = value.Substring(3);
            if (body.Length == 0) return new PortalRegistry(new PortalPair[0]);
            var pairs = new List<PortalPair>();
            foreach (var token in body.Split(';'))
            {
                var eq = token.IndexOf('=');
                var pair = token.IndexOf("<>", StringComparison.Ordinal);
                if (eq < 1 || pair < 0 || pair < eq)
                    throw new FormatException("Malformed portal pair.");
                var id = UnescapeId(token.Substring(0, eq));
                pairs.Add(new PortalPair(id,
                    ConveyorSchema.ParsePos(token.Substring(eq + 1, pair - eq - 1)),
                    ConveyorSchema.ParsePos(token.Substring(pair + 2))));
            }
            return new PortalRegistry(pairs);
        }

        private static string EscapeId(string id)
        {
            if (id.IndexOfAny(new[] { ';', '=', '<', '>', '%' }) >= 0)
                throw new ArgumentException("Portal ID contains reserved schema characters.");
            return id;
        }

        private static string UnescapeId(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
                throw new FormatException("Portal ID required.");
            return id;
        }
    }
}
