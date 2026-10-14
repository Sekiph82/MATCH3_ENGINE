using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Match3.Engine.Runtime
{
    public enum CascadePhase
    {
        Detect = 0,
        Damage = 1,
        Settle = 2,
        Refill = 3,
        Stable = 4
    }

    public readonly struct CascadePhaseEvent
    {
        public int Sequence { get; }
        public int Wave { get; }
        public CascadePhase Phase { get; }

        public CascadePhaseEvent(int sequence, int wave, CascadePhase phase)
        {
            if (sequence < 1) throw new ArgumentOutOfRangeException(nameof(sequence));
            if (wave < 0) throw new ArgumentOutOfRangeException(nameof(wave));
            if (!Enum.IsDefined(typeof(CascadePhase), phase))
                throw new ArgumentOutOfRangeException(nameof(phase));
            Sequence = sequence;
            Wave = wave;
            Phase = phase;
        }
    }

    public sealed class CascadeTimeline
    {
        private readonly ReadOnlyCollection<CascadePhaseEvent> _events;
        public IReadOnlyList<CascadePhaseEvent> Events => _events;
        public int ResolvedWaves { get; }
        public string LogicalHash { get; }

        internal CascadeTimeline(IEnumerable<CascadePhaseEvent> events, int waves)
        {
            _events = new ReadOnlyCollection<CascadePhaseEvent>(events.ToList());
            ResolvedWaves = waves;
            LogicalHash = Hash(_events);
        }

        private static string Hash(IEnumerable<CascadePhaseEvent> events)
        {
            var canonical = string.Join(";", events.Select(x =>
                x.Sequence + "|" + x.Wave + "|" + (int)x.Phase));
            using (var sha = SHA256.Create())
            {
                var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(canonical));
                var builder = new StringBuilder(bytes.Length * 2);
                foreach (var b in bytes) builder.Append(b.ToString("x2"));
                return builder.ToString();
            }
        }
    }

    public static class CascadeCoordinator
    {
        // The callback is authoritative SDK/integration truth: true means that
        // a solved wave exists. This coordinator never scans the board itself.
        public static CascadeTimeline Plan(Func<int, bool> authoritativeHasSolvedWave,
            int maximumResolvedWaves = 16)
        {
            if (authoritativeHasSolvedWave == null)
                throw new ArgumentNullException(nameof(authoritativeHasSolvedWave));
            if (maximumResolvedWaves < 1 || maximumResolvedWaves > 1024)
                throw new ArgumentOutOfRangeException(nameof(maximumResolvedWaves));

            var events = new List<CascadePhaseEvent>();
            var sequence = 1;
            var resolved = 0;
            for (var wave = 0; wave <= maximumResolvedWaves; wave++)
            {
                events.Add(new CascadePhaseEvent(sequence++, wave, CascadePhase.Detect));
                if (!authoritativeHasSolvedWave(wave))
                {
                    events.Add(new CascadePhaseEvent(sequence, wave, CascadePhase.Stable));
                    return new CascadeTimeline(events, resolved);
                }

                if (resolved >= maximumResolvedWaves)
                    throw new InvalidOperationException("Cascade wave budget exceeded.");

                events.Add(new CascadePhaseEvent(sequence++, wave, CascadePhase.Damage));
                events.Add(new CascadePhaseEvent(sequence++, wave, CascadePhase.Settle));
                events.Add(new CascadePhaseEvent(sequence++, wave, CascadePhase.Refill));
                resolved++;
            }
            throw new InvalidOperationException("Cascade planner did not converge.");
        }
    }

    public readonly struct PresentationFrame
    {
        public CascadePhaseEvent LogicalEvent { get; }
        public double DelaySeconds { get; }

        public PresentationFrame(CascadePhaseEvent logicalEvent, double delaySeconds)
        {
            LogicalEvent = logicalEvent;
            DelaySeconds = delaySeconds;
        }
    }

    public sealed class PresentationSchedule
    {
        public string LogicalHash { get; }
        public IReadOnlyList<PresentationFrame> Frames { get; }

        internal PresentationSchedule(string logicalHash,
            IEnumerable<PresentationFrame> frames)
        {
            LogicalHash = logicalHash;
            Frames = new ReadOnlyCollection<PresentationFrame>(frames.ToList());
        }
    }

    public static class TimelinePresentation
    {
        public static PresentationSchedule Project(CascadeTimeline timeline,
            double speedMultiplier = 1.0, bool skip = false)
        {
            if (timeline == null) throw new ArgumentNullException(nameof(timeline));
            if (Double.IsNaN(speedMultiplier) || Double.IsInfinity(speedMultiplier) ||
                speedMultiplier <= 0.0)
                throw new ArgumentOutOfRangeException(nameof(speedMultiplier));

            var frames = timeline.Events.Select(x => new PresentationFrame(
                x, skip ? 0.0 : BaseDelay(x.Phase) / speedMultiplier)).ToArray();
            return new PresentationSchedule(timeline.LogicalHash, frames);
        }

        private static double BaseDelay(CascadePhase phase)
        {
            switch (phase)
            {
                case CascadePhase.Damage: return 0.12;
                case CascadePhase.Settle: return 0.18;
                case CascadePhase.Refill: return 0.10;
                default: return 0.0;
            }
        }
    }

    public readonly struct SettleOperationToken : IEquatable<SettleOperationToken>
    {
        public int Generation { get; }
        public long Operation { get; }

        internal SettleOperationToken(int generation, long operation)
        {
            Generation = generation;
            Operation = operation;
        }

        public bool Equals(SettleOperationToken other) =>
            Generation == other.Generation && Operation == other.Operation;
        public override bool Equals(object obj) =>
            obj is SettleOperationToken && Equals((SettleOperationToken)obj);
        public override int GetHashCode() => Generation.GetHashCode() ^ Operation.GetHashCode();
    }

    public sealed class SettleCommitGate
    {
        private readonly HashSet<long> _pending = new HashSet<long>();
        private int _generation;
        private long _nextOperation;
        private bool _paused;

        public int Generation => _generation;
        public bool IsPaused => _paused;
        public int PendingCount => _pending.Count;

        public SettleOperationToken Begin()
        {
            if (_paused) throw new InvalidOperationException("Settle gate is paused.");
            checked { _nextOperation++; }
            _pending.Add(_nextOperation);
            return new SettleOperationToken(_generation, _nextOperation);
        }

        public bool TryCommit(SettleOperationToken token)
        {
            if (_paused || token.Generation != _generation)
                return false;
            return _pending.Remove(token.Operation);
        }

        public bool Pause()
        {
            if (_paused) return false;
            _paused = true;
            InvalidatePending();
            return true;
        }

        public bool Resume()
        {
            if (!_paused) return false;
            _paused = false;
            return true;
        }

        public void CancelPending()
        {
            InvalidatePending();
        }

        private void InvalidatePending()
        {
            checked { _generation++; }
            _pending.Clear();
        }
    }

    public readonly struct PlannerBenchmark
    {
        public int CellCount { get; }
        public int Operations { get; }
        public long ElapsedTicks { get; }
        public long AbsoluteManagedMemoryDelta { get; }

        public PlannerBenchmark(int cellCount, int operations,
            long elapsedTicks, long absoluteManagedMemoryDelta)
        {
            CellCount = cellCount;
            Operations = operations;
            ElapsedTicks = elapsedTicks;
            AbsoluteManagedMemoryDelta = absoluteManagedMemoryDelta;
        }
    }

    public static class GravityBenchmark
    {
        public static PlannerBenchmark MeasureCardinal(BoardTopology board,
            GravityDirection direction = GravityDirection.Down)
        {
            if (board == null) throw new ArgumentNullException(nameof(board));
            GC.Collect();
            var before = GC.GetTotalMemory(true);
            var watch = Stopwatch.StartNew();
            var plan = GravityPlanner.PlanCardinal(board, direction);
            watch.Stop();
            var after = GC.GetTotalMemory(false);
            return new PlannerBenchmark(
                checked(board.Rows * board.Columns * board.Layers),
                plan.Operations, watch.ElapsedTicks, Math.Abs(after - before));
        }
    }

    public sealed class SettlementPipelinePlan
    {
        public IReadOnlyDictionary<BoardPosition, string> Occupants { get; }
        public IReadOnlyList<string> Stages { get; }
        public int Operations { get; }

        internal SettlementPipelinePlan(IDictionary<BoardPosition, string> occupants,
            IEnumerable<string> stages, int operations)
        {
            Occupants = new ReadOnlyDictionary<BoardPosition, string>(
                new Dictionary<BoardPosition, string>(occupants));
            Stages = new ReadOnlyCollection<string>(stages.ToList());
            Operations = operations;
        }
    }

    public static class SettlementPipeline
    {
        // One bounded settle phase: conveyor -> portal -> gravity -> refill.
        // Match/damage happens before this call and remains SDK authority.
        public static SettlementPipelinePlan Plan(BoardTopology input,
            IReadOnlyList<ConveyorEdge> conveyors, PortalRegistry portals,
            GravityDirection direction, bool allowDiagonal,
            DiagonalTieBreak tieBreak, IReadOnlyList<WeightedContent> refill,
            SeededRandom rng)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));
            if (conveyors == null) throw new ArgumentNullException(nameof(conveyors));
            if (portals == null) throw new ArgumentNullException(nameof(portals));
            if (refill == null) throw new ArgumentNullException(nameof(refill));
            if (rng == null) throw new ArgumentNullException(nameof(rng));

            var stages = new List<string>();
            var operations = 0;

            var conveyor = ConveyorPlanner.Plan(input, input.Occupants, conveyors);
            operations += conveyor.Operations;
            stages.Add("conveyor");
            var currentTopology = Rebuild(input, conveyor.Occupants);

            var portal = PortalPlanner.Plan(currentTopology, conveyor.Occupants, portals);
            operations += portal.Operations;
            stages.Add("portal");
            currentTopology = Rebuild(input, portal.Occupants);

            GravityPlan gravity;
            if (allowDiagonal)
                gravity = GravityPlanner.PlanDiagonal(currentTopology,
                    direction, tieBreak, Math.Max(8, input.Rows + input.Columns + 4));
            else
                gravity = GravityPlanner.PlanCardinal(currentTopology, direction);
            operations += gravity.Operations;
            stages.Add("gravity");

            currentTopology = Rebuild(input, gravity.Occupants);
            var refillPlan = WeightedRefillPlanner.Plan(currentTopology,
                gravity.Occupants, refill, rng);
            operations += refillPlan.Operations;
            stages.Add("refill");

            return new SettlementPipelinePlan(
                new Dictionary<BoardPosition, string>(refillPlan.Occupants),
                stages, operations);
        }

        private static BoardTopology Rebuild(BoardTopology template,
            IReadOnlyDictionary<BoardPosition, string> occupants)
        {
            var holes = new List<BoardPosition>();
            var locked = new List<BoardPosition>();
            for (var layer = 0; layer < template.Layers; layer++)
                for (var row = 0; row < template.Rows; row++)
                    for (var col = 0; col < template.Columns; col++)
                    {
                        var p = new BoardPosition(row, col, layer);
                        if (template.IsHole(p)) holes.Add(p);
                        else if (template.IsLocked(p)) locked.Add(p);
                    }

            return new BoardTopology(template.Rows, template.Columns,
                template.Layers, holes, locked,
                occupants.Select(x => new BoardOccupant(x.Key, x.Value)));
        }
    }
}
