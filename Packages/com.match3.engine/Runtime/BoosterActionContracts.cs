using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Match3.Engine.Runtime
{
    public sealed class PrelevelBoosterPlacementPlan
    {
        public BoosterKind Kind { get; }
        public BoardPosition Position { get; }
        public SpecialActivationKind SpecialKind { get; }
        public int CandidateCount { get; }

        internal PrelevelBoosterPlacementPlan(BoosterKind kind,
            BoardPosition position, SpecialActivationKind specialKind,
            int candidateCount)
        {
            Kind = kind;
            Position = position;
            SpecialKind = specialKind;
            CandidateCount = candidateCount;
        }
    }

    public static class PrelevelBoosterPlanner
    {
        public static PrelevelBoosterPlacementPlan Plan(BoardTopology topology,
            BoosterKind kind, uint seed, int maximumCandidates = 4096)
        {
            if (topology == null) throw new ArgumentNullException(nameof(topology));
            if (seed == 0) throw new ArgumentOutOfRangeException(nameof(seed));
            if (maximumCandidates < 1 || maximumCandidates > 65536)
                throw new ArgumentOutOfRangeException(nameof(maximumCandidates));

            var specialKind = ToSpecialKind(kind);
            var candidates = new List<BoardPosition>();
            foreach (var pair in topology.Occupants)
            {
                var p = pair.Key;
                if (topology.IsLocked(p) || topology.IsHole(p))
                    continue;
                candidates.Add(p);
                if (candidates.Count > maximumCandidates)
                    break;
            }
            candidates = candidates
                .OrderBy(p => p.Layer)
                .ThenBy(p => p.Row)
                .ThenBy(p => p.Column)
                .ToList();

            if (candidates.Count > maximumCandidates)
                throw new InvalidOperationException(
                    "Prelevel booster candidate budget exceeded.");
            if (candidates.Count == 0)
                throw new InvalidOperationException(
                    "No valid prelevel booster placement exists.");

            var rng = new SeededRandom(seed);
            var selected = candidates[rng.NextIndex(candidates.Count)];
            return new PrelevelBoosterPlacementPlan(
                kind, selected, specialKind, candidates.Count);
        }

        private static SpecialActivationKind ToSpecialKind(BoosterKind kind)
        {
            switch (kind)
            {
                case BoosterKind.HorizontalLine:
                    return SpecialActivationKind.HorizontalLine;
                case BoosterKind.VerticalLine:
                    return SpecialActivationKind.VerticalLine;
                case BoosterKind.Area:
                    return SpecialActivationKind.Area;
                case BoosterKind.Color:
                    return SpecialActivationKind.Color;
                case BoosterKind.TargetedMissile:
                    return SpecialActivationKind.Targeted;
                default:
                    throw new ArgumentException(
                        "Booster kind cannot be placed as a prelevel special.",
                        nameof(kind));
            }
        }
    }

    public sealed class BoosterActionRequest
    {
        public string ActionId { get; }
        public BoosterKind Kind { get; }
        public BoardPosition Target { get; }
        public BoardPosition? SecondaryTarget { get; }
        public int Radius { get; }
        public string TargetColor { get; }
        public uint Seed { get; }

        public BoosterActionRequest(string actionId, BoosterKind kind,
            BoardPosition target, BoardPosition? secondaryTarget = null,
            int radius = 1, string targetColor = null, uint seed = 1u)
        {
            if (string.IsNullOrWhiteSpace(actionId))
                throw new ArgumentException("Booster action ID required.",
                    nameof(actionId));
            if (!Enum.IsDefined(typeof(BoosterKind), kind))
                throw new ArgumentOutOfRangeException(nameof(kind));
            if (radius < 1 || radius > 32)
                throw new ArgumentOutOfRangeException(nameof(radius));
            if (seed == 0) throw new ArgumentOutOfRangeException(nameof(seed));
            ActionId = actionId;
            Kind = kind;
            Target = target;
            SecondaryTarget = secondaryTarget;
            Radius = radius;
            TargetColor = targetColor;
            Seed = seed;
        }
    }

    public sealed class BoosterEffectPlan
    {
        private readonly ReadOnlyCollection<BoardPosition> _hitPositions;

        public BoosterKind Kind { get; }
        public IReadOnlyList<BoardPosition> HitPositions => _hitPositions;
        public SpecialEffectPlan CanonicalSpecialPlan { get; }
        public string LogicalHash { get; }

        internal BoosterEffectPlan(BoosterKind kind,
            IEnumerable<BoardPosition> hitPositions,
            SpecialEffectPlan canonicalSpecialPlan, string logicalHash)
        {
            Kind = kind;
            _hitPositions = new ReadOnlyCollection<BoardPosition>(
                hitPositions.OrderBy(x => x.Layer)
                    .ThenBy(x => x.Row)
                    .ThenBy(x => x.Column)
                    .Distinct().ToList());
            CanonicalSpecialPlan = canonicalSpecialPlan;
            if (string.IsNullOrWhiteSpace(logicalHash))
                throw new ArgumentException("Booster logical hash required.",
                    nameof(logicalHash));
            LogicalHash = logicalHash;
        }
    }

    public static class BoosterEffectPlanner
    {
        public static BoosterEffectPlan Plan(SpecialBoardSnapshot board,
            BoosterActionRequest request)
        {
            if (board == null) throw new ArgumentNullException(nameof(board));
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (!board.Contains(request.Target))
                throw new ArgumentOutOfRangeException(nameof(request));
            SpecialCellState targetCell;
            if (!board.TryGet(request.Target, out targetCell) ||
                targetCell.Immune || targetCell.Locked)
                throw new InvalidOperationException(
                    "Booster requires an eligible target cell.");

            switch (request.Kind)
            {
                case BoosterKind.Hammer:
                    return PlanHammer(board, request);
                case BoosterKind.HorizontalLine:
                case BoosterKind.VerticalLine:
                case BoosterKind.Area:
                case BoosterKind.Color:
                case BoosterKind.TargetedMissile:
                    return PlanCanonicalSpecial(board, request);
                default:
                    throw new ArgumentException(
                        "Booster kind requires a board-transform planner.",
                        nameof(request));
            }
        }

        private static BoosterEffectPlan PlanHammer(SpecialBoardSnapshot board,
            BoosterActionRequest request)
        {
            SpecialCellState cell;
            if (!board.TryGet(request.Target, out cell) ||
                cell.Immune || cell.Locked)
                throw new InvalidOperationException(
                    "Hammer requires an eligible target cell.");

            var hash = Sha256("hammer|" + request.Target);
            return new BoosterEffectPlan(BoosterKind.Hammer,
                new[] { request.Target }, null, hash);
        }

        private static BoosterEffectPlan PlanCanonicalSpecial(
            SpecialBoardSnapshot board, BoosterActionRequest request)
        {
            var activation = new SpecialActivation(
                ToSpecialKind(request.Kind), request.Target,
                request.Radius, request.TargetColor,
                request.SecondaryTarget);
            if (request.SecondaryTarget.HasValue &&
                !board.Contains(request.SecondaryTarget.Value))
                throw new ArgumentOutOfRangeException(nameof(request));

            var canonical = SpecialEffectPlanner.Resolve(
                board, activation, null, request.Seed);
            var hash = SpecialGoldenHasher.HashEffectPlan(canonical);
            return new BoosterEffectPlan(request.Kind,
                canonical.Hits.Select(x => x.Position),
                canonical, hash);
        }

        private static SpecialActivationKind ToSpecialKind(BoosterKind kind)
        {
            switch (kind)
            {
                case BoosterKind.HorizontalLine:
                    return SpecialActivationKind.HorizontalLine;
                case BoosterKind.VerticalLine:
                    return SpecialActivationKind.VerticalLine;
                case BoosterKind.Area:
                    return SpecialActivationKind.Area;
                case BoosterKind.Color:
                    return SpecialActivationKind.Color;
                case BoosterKind.TargetedMissile:
                    return SpecialActivationKind.Targeted;
                default:
                    throw new ArgumentOutOfRangeException(nameof(kind));
            }
        }

        private static string Sha256(string value)
        {
            using (var sha = SHA256.Create())
            {
                var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(value));
                var builder = new StringBuilder(bytes.Length * 2);
                foreach (var b in bytes) builder.Append(b.ToString("x2"));
                return builder.ToString();
            }
        }
    }

    public sealed class BoosterBoardTransformPlan
    {
        private readonly ReadOnlyDictionary<BoardPosition, string> _occupants;
        private readonly ReadOnlyCollection<BoardPosition> _changed;

        public BoosterKind Kind { get; }
        public IReadOnlyDictionary<BoardPosition, string> Occupants => _occupants;
        public IReadOnlyList<BoardPosition> ChangedPositions => _changed;
        public string LogicalHash { get; }

        internal BoosterBoardTransformPlan(BoosterKind kind,
            IDictionary<BoardPosition, string> occupants,
            IEnumerable<BoardPosition> changed, string logicalHash)
        {
            Kind = kind;
            _occupants = new ReadOnlyDictionary<BoardPosition, string>(
                new Dictionary<BoardPosition, string>(occupants));
            _changed = new ReadOnlyCollection<BoardPosition>(
                changed.OrderBy(x => x.Layer)
                    .ThenBy(x => x.Row)
                    .ThenBy(x => x.Column).Distinct().ToList());
            LogicalHash = logicalHash;
        }
    }

    public static class BoosterBoardTransformPlanner
    {
        public static BoosterBoardTransformPlan Plan(BoardTopology topology,
            BoosterActionRequest request, int maximumMovableCells = 4096)
        {
            if (topology == null) throw new ArgumentNullException(nameof(topology));
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (maximumMovableCells < 2 || maximumMovableCells > 65536)
                throw new ArgumentOutOfRangeException(nameof(maximumMovableCells));

            switch (request.Kind)
            {
                case BoosterKind.Swap:
                    return PlanSwap(topology, request.Target,
                        RequireSecondary(request));
                case BoosterKind.Shuffle:
                    return PlanShuffle(topology, request.Seed,
                        maximumMovableCells);
                default:
                    throw new ArgumentException(
                        "Booster kind is not a board transform.", nameof(request));
            }
        }

        private static BoardPosition RequireSecondary(BoosterActionRequest request)
        {
            if (!request.SecondaryTarget.HasValue)
                throw new ArgumentException(
                    "Swap booster requires a secondary target.", nameof(request));
            return request.SecondaryTarget.Value;
        }

        private static BoosterBoardTransformPlan PlanSwap(BoardTopology topology,
            BoardPosition first, BoardPosition second)
        {
            if (!topology.IsSwapCandidate(first, second))
                throw new InvalidOperationException("Illegal booster swap.");

            var result = topology.Occupants.ToDictionary(x => x.Key, x => x.Value);
            var a = result[first];
            var b = result[second];
            result[first] = b;
            result[second] = a;
            return new BoosterBoardTransformPlan(
                BoosterKind.Swap, result, new[] { first, second },
                HashOccupants(result));
        }

        private static BoosterBoardTransformPlan PlanShuffle(BoardTopology topology,
            uint seed, int maximumMovableCells)
        {
            var positions = new List<BoardPosition>();
            foreach (var pair in topology.Occupants)
            {
                var p = pair.Key;
                if (topology.IsLocked(p) || topology.IsHole(p))
                    continue;
                positions.Add(p);
                if (positions.Count > maximumMovableCells)
                    break;
            }
            positions = positions
                .OrderBy(x => x.Layer)
                .ThenBy(x => x.Row)
                .ThenBy(x => x.Column)
                .ToList();
            if (positions.Count > maximumMovableCells)
                throw new InvalidOperationException("Shuffle cell budget exceeded.");
            if (positions.Count < 2)
                throw new InvalidOperationException("Not enough movable cells to shuffle.");

            var original = positions.Select(x => topology.Occupants[x]).ToList();
            if (original.Distinct(StringComparer.Ordinal).Count() < 2)
                throw new InvalidOperationException(
                    "Shuffle requires at least two distinct movable contents.");

            var shuffled = original.ToList();
            var rng = new SeededRandom(seed);
            for (var i = shuffled.Count - 1; i > 0; i--)
            {
                var j = rng.NextIndex(i + 1);
                var temp = shuffled[i];
                shuffled[i] = shuffled[j];
                shuffled[j] = temp;
            }

            if (shuffled.SequenceEqual(original))
            {
                var first = shuffled[0];
                shuffled.RemoveAt(0);
                shuffled.Add(first);
            }

            var result = topology.Occupants.ToDictionary(x => x.Key, x => x.Value);
            var changed = new List<BoardPosition>();
            for (var i = 0; i < positions.Count; i++)
            {
                result[positions[i]] = shuffled[i];
                if (!string.Equals(original[i], shuffled[i], StringComparison.Ordinal))
                    changed.Add(positions[i]);
            }

            return new BoosterBoardTransformPlan(
                BoosterKind.Shuffle, result, changed, HashOccupants(result));
        }

        private static string HashOccupants(
            IReadOnlyDictionary<BoardPosition, string> occupants)
        {
            var canonical = string.Join(";", occupants
                .OrderBy(x => x.Key.Layer)
                .ThenBy(x => x.Key.Row)
                .ThenBy(x => x.Key.Column)
                .Select(x => x.Key + "=" + x.Value));
            using (var sha = SHA256.Create())
            {
                var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(canonical));
                var builder = new StringBuilder(bytes.Length * 2);
                foreach (var b in bytes) builder.Append(b.ToString("x2"));
                return builder.ToString();
            }
        }
    }

    public sealed class PreparedBoosterAction
    {
        public BoosterActionRequest Request { get; }
        public BoosterEconomySnapshot ReservedEconomy { get; }
        public BoosterEffectPlan EffectPlan { get; }
        public BoosterBoardTransformPlan TransformPlan { get; }

        internal PreparedBoosterAction(BoosterActionRequest request,
            BoosterEconomySnapshot reservedEconomy,
            BoosterEffectPlan effectPlan,
            BoosterBoardTransformPlan transformPlan)
        {
            Request = request ?? throw new ArgumentNullException(nameof(request));
            ReservedEconomy = reservedEconomy ??
                throw new ArgumentNullException(nameof(reservedEconomy));
            EffectPlan = effectPlan;
            TransformPlan = transformPlan;
            if ((effectPlan == null) == (transformPlan == null))
                throw new ArgumentException(
                    "Prepared booster action must contain exactly one logical plan.");
        }
    }

    public static class BoosterActionLifecycle
    {
        public static PreparedBoosterAction PrepareEffect(
            BoosterEconomySnapshot economy,
            BoosterProfilePolicy policy,
            BoosterAcquisitionSource acquisitionSource,
            bool explicitConfirmation,
            SpecialBoardSnapshot board,
            BoosterActionRequest request)
        {
            if (economy == null) throw new ArgumentNullException(nameof(economy));
            BoosterSafetyPolicy.ValidateUse(
                policy, acquisitionSource, explicitConfirmation, false);

            // Logical validation happens before a reservation is returned.
            // All planners are pure, so a thrown validation leaves the caller's
            // original economy snapshot untouched.
            var effect = BoosterEffectPlanner.Plan(board, request);
            var reserved = BoosterEconomyPlanner.Reserve(
                economy, request.Kind, request.ActionId);
            return new PreparedBoosterAction(
                request, reserved.Result, effect, null);
        }

        public static PreparedBoosterAction PrepareTransform(
            BoosterEconomySnapshot economy,
            BoosterProfilePolicy policy,
            BoosterAcquisitionSource acquisitionSource,
            bool explicitConfirmation,
            BoardTopology board,
            BoosterActionRequest request)
        {
            if (economy == null) throw new ArgumentNullException(nameof(economy));
            BoosterSafetyPolicy.ValidateUse(
                policy, acquisitionSource, explicitConfirmation, false);

            var transform = BoosterBoardTransformPlanner.Plan(board, request);
            var reserved = BoosterEconomyPlanner.Reserve(
                economy, request.Kind, request.ActionId);
            return new PreparedBoosterAction(
                request, reserved.Result, null, transform);
        }

        public static BoosterEconomyPlan Confirm(PreparedBoosterAction prepared,
            BoosterEconomySnapshot currentEconomy)
        {
            if (prepared == null) throw new ArgumentNullException(nameof(prepared));
            if (currentEconomy == null)
                throw new ArgumentNullException(nameof(currentEconomy));
            BoosterTransaction current;
            if (!currentEconomy.Transactions.TryGetValue(
                prepared.Request.ActionId, out current) ||
                current.Kind != prepared.Request.Kind)
                throw new InvalidOperationException(
                    "Current economy does not contain the prepared reservation.");
            return BoosterEconomyPlanner.Confirm(
                currentEconomy, prepared.Request.ActionId);
        }

        public static BoosterEconomyPlan Cancel(PreparedBoosterAction prepared,
            BoosterEconomySnapshot currentEconomy)
        {
            if (prepared == null) throw new ArgumentNullException(nameof(prepared));
            if (currentEconomy == null)
                throw new ArgumentNullException(nameof(currentEconomy));
            BoosterTransaction current;
            if (!currentEconomy.Transactions.TryGetValue(
                prepared.Request.ActionId, out current) ||
                current.Kind != prepared.Request.Kind)
                throw new InvalidOperationException(
                    "Current economy does not contain the prepared reservation.");
            return BoosterEconomyPlanner.Refund(
                currentEconomy, prepared.Request.ActionId);
        }
    }

    public enum BoosterPresentationPhase
    {
        Preview = 0,
        Confirmed = 1,
        Cancelled = 2
    }

    public readonly struct BoosterPresentationEvent
    {
        public int Sequence { get; }
        public string ActionId { get; }
        public BoosterPresentationPhase Phase { get; }
        public string PresentationKey { get; }

        public BoosterPresentationEvent(int sequence, string actionId,
            BoosterPresentationPhase phase, string presentationKey)
        {
            if (sequence < 1) throw new ArgumentOutOfRangeException(nameof(sequence));
            if (string.IsNullOrWhiteSpace(actionId))
                throw new ArgumentException("Presentation action ID required.",
                    nameof(actionId));
            if (!Enum.IsDefined(typeof(BoosterPresentationPhase), phase))
                throw new ArgumentOutOfRangeException(nameof(phase));
            if (string.IsNullOrWhiteSpace(presentationKey))
                throw new ArgumentException("Presentation key required.",
                    nameof(presentationKey));
            Sequence = sequence;
            ActionId = actionId;
            Phase = phase;
            PresentationKey = presentationKey;
        }
    }

    public sealed class BoosterPresentationSchedule
    {
        private readonly ReadOnlyCollection<BoosterPresentationEvent> _events;

        public string LogicalHash { get; }
        public IReadOnlyList<BoosterPresentationEvent> Events => _events;

        internal BoosterPresentationSchedule(string logicalHash,
            IEnumerable<BoosterPresentationEvent> events)
        {
            LogicalHash = logicalHash;
            _events = new ReadOnlyCollection<BoosterPresentationEvent>(
                events.ToList());
        }
    }

    public static class BoosterPresentationPlanner
    {
        public static BoosterPresentationSchedule Project(
            BoosterEffectPlan logicalPlan, string actionId,
            BoosterPresentationPhase phase, bool skipPresentation = false)
        {
            if (logicalPlan == null) throw new ArgumentNullException(nameof(logicalPlan));
            if (string.IsNullOrWhiteSpace(actionId))
                throw new ArgumentException("Action ID required.", nameof(actionId));
            if (!Enum.IsDefined(typeof(BoosterPresentationPhase), phase))
                throw new ArgumentOutOfRangeException(nameof(phase));

            if (skipPresentation)
                return new BoosterPresentationSchedule(
                    logicalPlan.LogicalHash,
                    new BoosterPresentationEvent[0]);

            var key = "booster." +
                logicalPlan.Kind.ToString().ToLowerInvariant() + "." +
                phase.ToString().ToLowerInvariant();
            return new BoosterPresentationSchedule(
                logicalPlan.LogicalHash,
                new[]
                {
                    new BoosterPresentationEvent(1, actionId, phase, key)
                });
        }
    }
}
