using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Match3.Engine.Runtime
{
    public enum ObstacleLayerKind
    {
        Underlay = 0,
        Overlay = 1,
        Piece = 2,
        Cell = 3
    }

    public enum ObstacleDamageKind
    {
        Normal = 0,
        Line = 1,
        Area = 2,
        Color = 3,
        Targeted = 4
    }

    public enum LineInteraction
    {
        PassThrough = 0,
        StopAfterDamage = 1,
        AbsorbWithoutDamage = 2
    }

    public enum TurnMutationKind
    {
        PlayerMove = 0,
        Cascade = 1,
        Booster = 2,
        Shuffle = 3,
        Preview = 4,
        ReplayPresentation = 5
    }

    public enum CountdownZeroRule
    {
        Lose = 0,
        Detonate = 1
    }

    public enum CountdownOutcome
    {
        None = 0,
        GoalWinPrecedence = 1,
        Loss = 2,
        Detonate = 3
    }

    public sealed class ObstacleDefinition
    {
        public string Id { get; }
        public int MaxDurability { get; }
        public bool ImmobilizesPiece { get; }
        public bool Spreads { get; }
        public LineInteraction LineInteraction { get; }
        public bool AreaDamageAllowed { get; }
        public string GoalKey { get; }
        public string PresentationKey { get; }

        public ObstacleDefinition(string id, int maxDurability,
            bool immobilizesPiece = false, bool spreads = false,
            LineInteraction lineInteraction = LineInteraction.PassThrough,
            bool areaDamageAllowed = true, string goalKey = null,
            string presentationKey = null)
        {
            if (string.IsNullOrWhiteSpace(id))
                throw new ArgumentException("Obstacle ID required.", nameof(id));
            if (maxDurability < 1 || maxDurability > 1024)
                throw new ArgumentOutOfRangeException(nameof(maxDurability));
            if (!Enum.IsDefined(typeof(LineInteraction), lineInteraction))
                throw new ArgumentOutOfRangeException(nameof(lineInteraction));
            if (goalKey != null && string.IsNullOrWhiteSpace(goalKey))
                throw new ArgumentException("Goal key cannot be blank.", nameof(goalKey));
            if (presentationKey != null && string.IsNullOrWhiteSpace(presentationKey))
                throw new ArgumentException("Presentation key cannot be blank.", nameof(presentationKey));

            Id = id;
            MaxDurability = maxDurability;
            ImmobilizesPiece = immobilizesPiece;
            Spreads = spreads;
            LineInteraction = lineInteraction;
            AreaDamageAllowed = areaDamageAllowed;
            GoalKey = goalKey;
            PresentationKey = presentationKey;
        }
    }

    public sealed class ObstacleState
    {
        public string InstanceId { get; }
        public string DefinitionId { get; }
        public BoardPosition Position { get; }
        public ObstacleLayerKind Layer { get; }
        public int Durability { get; }

        public ObstacleState(string instanceId, string definitionId,
            BoardPosition position, ObstacleLayerKind layer, int durability)
        {
            if (string.IsNullOrWhiteSpace(instanceId))
                throw new ArgumentException("Obstacle instance ID required.", nameof(instanceId));
            if (string.IsNullOrWhiteSpace(definitionId))
                throw new ArgumentException("Obstacle definition ID required.", nameof(definitionId));
            if (!Enum.IsDefined(typeof(ObstacleLayerKind), layer))
                throw new ArgumentOutOfRangeException(nameof(layer));
            if (durability < 1 || durability > 1024)
                throw new ArgumentOutOfRangeException(nameof(durability));
            InstanceId = instanceId;
            DefinitionId = definitionId;
            Position = position;
            Layer = layer;
            Durability = durability;
        }

        internal ObstacleState WithDurability(int durability)
        {
            return new ObstacleState(InstanceId, DefinitionId, Position, Layer, durability);
        }
    }

    public sealed class ObstacleSnapshot
    {
        private readonly ReadOnlyCollection<ObstacleState> _states;
        private readonly ReadOnlyDictionary<string, ObstacleState> _byId;

        public int Rows { get; }
        public int Columns { get; }
        public int Layers { get; }
        public IReadOnlyList<ObstacleState> States => _states;
        public IReadOnlyDictionary<string, ObstacleState> ByInstanceId => _byId;

        public ObstacleSnapshot(int rows, int columns,
            IEnumerable<ObstacleState> states, int layers = 1)
        {
            if (rows < 1 || columns < 1 || layers < 1 ||
                rows > 1024 || columns > 1024 || layers > 16)
                throw new ArgumentOutOfRangeException(nameof(rows));
            if (states == null) throw new ArgumentNullException(nameof(states));
            Rows = rows;
            Columns = columns;
            Layers = layers;

            var list = states.ToList();
            if (list.Any(x => x == null))
                throw new ArgumentException("Null obstacle state.", nameof(states));
            if (list.Any(x => !Contains(x.Position)))
                throw new ArgumentOutOfRangeException(nameof(states), "Obstacle outside board.");
            if (list.GroupBy(x => x.InstanceId, StringComparer.Ordinal).Any(x => x.Count() > 1))
                throw new ArgumentException("Duplicate obstacle instance ID.", nameof(states));
            if (list.GroupBy(x => Tuple.Create(x.Position, x.Layer)).Any(x => x.Count() > 1))
                throw new ArgumentException("Duplicate obstacle layer at one board position.",
                    nameof(states));

            var ordered = list.OrderBy(x => x.Position.Layer)
                .ThenBy(x => x.Position.Row)
                .ThenBy(x => x.Position.Column)
                .ThenBy(x => (int)x.Layer)
                .ThenBy(x => x.InstanceId, StringComparer.Ordinal).ToList();
            _states = new ReadOnlyCollection<ObstacleState>(ordered);
            _byId = new ReadOnlyDictionary<string, ObstacleState>(
                ordered.ToDictionary(x => x.InstanceId, x => x, StringComparer.Ordinal));
        }

        public bool Contains(BoardPosition p) =>
            p.Row >= 0 && p.Row < Rows &&
            p.Column >= 0 && p.Column < Columns &&
            p.Layer >= 0 && p.Layer < Layers;

        public IReadOnlyList<ObstacleState> At(BoardPosition p)
        {
            return new ReadOnlyCollection<ObstacleState>(
                _states.Where(x => x.Position.Equals(p))
                    .OrderBy(x => (int)x.Layer).ToList());
        }
    }

    public static class ObstacleDefinitions
    {
        public static IReadOnlyDictionary<string, ObstacleDefinition> Validate(
            IEnumerable<ObstacleDefinition> definitions)
        {
            if (definitions == null) throw new ArgumentNullException(nameof(definitions));
            var list = definitions.ToList();
            if (list.Any(x => x == null))
                throw new ArgumentException("Null obstacle definition.", nameof(definitions));
            if (list.GroupBy(x => x.Id, StringComparer.Ordinal).Any(x => x.Count() > 1))
                throw new ArgumentException("Duplicate obstacle definition ID.", nameof(definitions));
            return new ReadOnlyDictionary<string, ObstacleDefinition>(
                list.ToDictionary(x => x.Id, x => x, StringComparer.Ordinal));
        }

        internal static ObstacleDefinition Require(
            IReadOnlyDictionary<string, ObstacleDefinition> definitions,
            ObstacleState state)
        {
            ObstacleDefinition definition;
            if (definitions == null ||
                !definitions.TryGetValue(state.DefinitionId, out definition))
                throw new InvalidOperationException(
                    "Missing obstacle definition: " + state.DefinitionId);
            if (state.Durability > definition.MaxDurability)
                throw new InvalidOperationException(
                    "Obstacle durability exceeds definition maximum.");
            return definition;
        }
    }

    public readonly struct ObstacleHitRequest
    {
        public BoardPosition Position { get; }
        public ObstacleDamageKind Kind { get; }
        public string PhaseId { get; }
        public int Amount { get; }

        public ObstacleHitRequest(BoardPosition position,
            ObstacleDamageKind kind, string phaseId, int amount = 1)
        {
            if (!Enum.IsDefined(typeof(ObstacleDamageKind), kind))
                throw new ArgumentOutOfRangeException(nameof(kind));
            if (string.IsNullOrWhiteSpace(phaseId))
                throw new ArgumentException("Damage phase ID required.", nameof(phaseId));
            if (amount < 1 || amount > 1024)
                throw new ArgumentOutOfRangeException(nameof(amount));
            Position = position;
            Kind = kind;
            PhaseId = phaseId;
            Amount = amount;
        }
    }

    public readonly struct ObstacleDamageReceipt
    {
        public string InstanceId { get; }
        public string DefinitionId { get; }
        public BoardPosition Position { get; }
        public string PhaseId { get; }
        public ObstacleDamageKind Kind { get; }
        public int Before { get; }
        public int After { get; }
        public bool Removed => After == 0;

        public ObstacleDamageReceipt(string instanceId, string definitionId,
            BoardPosition position, string phaseId, ObstacleDamageKind kind,
            int before, int after)
        {
            InstanceId = instanceId;
            DefinitionId = definitionId;
            Position = position;
            PhaseId = phaseId;
            Kind = kind;
            Before = before;
            After = after;
        }
    }

    public readonly struct GoalProgressReceipt
    {
        public string ReceiptId { get; }
        public string GoalKey { get; }
        public string InstanceId { get; }
        public int Delta { get; }

        public GoalProgressReceipt(string receiptId, string goalKey,
            string instanceId, int delta)
        {
            if (string.IsNullOrWhiteSpace(receiptId) ||
                string.IsNullOrWhiteSpace(goalKey) ||
                string.IsNullOrWhiteSpace(instanceId))
                throw new ArgumentException("Goal receipt fields required.");
            if (delta != 1) throw new ArgumentOutOfRangeException(nameof(delta));
            ReceiptId = receiptId;
            GoalKey = goalKey;
            InstanceId = instanceId;
            Delta = delta;
        }
    }

    public sealed class ObstacleDamagePlan
    {
        public ObstacleSnapshot Result { get; }
        public IReadOnlyList<ObstacleDamageReceipt> DamageReceipts { get; }
        public IReadOnlyList<GoalProgressReceipt> GoalReceipts { get; }

        internal ObstacleDamagePlan(ObstacleSnapshot result,
            IEnumerable<ObstacleDamageReceipt> damage,
            IEnumerable<GoalProgressReceipt> goals)
        {
            Result = result;
            DamageReceipts = new ReadOnlyCollection<ObstacleDamageReceipt>(damage.ToList());
            GoalReceipts = new ReadOnlyCollection<GoalProgressReceipt>(goals.ToList());
        }
    }

    public static class ObstacleDamagePlanner
    {
        public static ObstacleDamagePlan Plan(ObstacleSnapshot snapshot,
            IEnumerable<ObstacleDefinition> definitions,
            IEnumerable<ObstacleHitRequest> hits)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            if (hits == null) throw new ArgumentNullException(nameof(hits));
            var defs = ObstacleDefinitions.Validate(definitions);
            var current = snapshot.States.ToDictionary(
                x => x.InstanceId, x => x, StringComparer.Ordinal);
            foreach (var state in snapshot.States)
                ObstacleDefinitions.Require(defs, state);

            var receipts = new List<ObstacleDamageReceipt>();
            var goals = new List<GoalProgressReceipt>();
            var seen = new HashSet<string>(StringComparer.Ordinal);

            foreach (var hit in hits)
            {
                if (!snapshot.Contains(hit.Position))
                    throw new ArgumentOutOfRangeException(nameof(hits),
                        "Damage request outside obstacle board.");
                var candidates = current.Values
                    .Where(x => x.Position.Equals(hit.Position))
                    .OrderByDescending(x => (int)x.Layer)
                    .ThenBy(x => x.InstanceId, StringComparer.Ordinal).ToArray();

                foreach (var state in candidates)
                {
                    var definition = ObstacleDefinitions.Require(defs, state);
                    if (hit.Kind == ObstacleDamageKind.Area &&
                        !definition.AreaDamageAllowed)
                        continue;
                    if (hit.Kind == ObstacleDamageKind.Line &&
                        definition.LineInteraction == LineInteraction.AbsorbWithoutDamage)
                        continue;

                    var dedupKey = state.InstanceId + "|" + hit.PhaseId;
                    if (!seen.Add(dedupKey)) continue;

                    var after = Math.Max(0, state.Durability - hit.Amount);
                    receipts.Add(new ObstacleDamageReceipt(
                        state.InstanceId, state.DefinitionId, state.Position,
                        hit.PhaseId, hit.Kind, state.Durability, after));

                    if (after == 0)
                    {
                        current.Remove(state.InstanceId);
                        if (!string.IsNullOrEmpty(definition.GoalKey))
                            goals.Add(new GoalProgressReceipt(
                                hit.PhaseId + "|" + state.InstanceId,
                                definition.GoalKey, state.InstanceId, 1));
                    }
                    else
                    {
                        current[state.InstanceId] = state.WithDurability(after);
                    }
                }
            }

            var result = new ObstacleSnapshot(snapshot.Rows, snapshot.Columns,
                current.Values, snapshot.Layers);
            return new ObstacleDamagePlan(result, receipts, goals);
        }

        public static bool IsPieceImmobilized(ObstacleSnapshot snapshot,
            IEnumerable<ObstacleDefinition> definitions, BoardPosition position)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            var defs = ObstacleDefinitions.Validate(definitions);
            return snapshot.At(position).Any(x =>
                ObstacleDefinitions.Require(defs, x).ImmobilizesPiece);
        }
    }

    public sealed class BeamPlan
    {
        public IReadOnlyList<ObstacleHitRequest> Hits { get; }
        public bool Stopped { get; }
        public BoardPosition? StopPosition { get; }

        internal BeamPlan(IEnumerable<ObstacleHitRequest> hits,
            bool stopped, BoardPosition? stopPosition)
        {
            Hits = new ReadOnlyCollection<ObstacleHitRequest>(hits.ToList());
            Stopped = stopped;
            StopPosition = stopPosition;
        }
    }

    public static class ObstacleBeamPlanner
    {
        public static BeamPlan PlanLine(ObstacleSnapshot snapshot,
            IEnumerable<ObstacleDefinition> definitions,
            IEnumerable<BoardPosition> orderedPath, string phaseId)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            if (orderedPath == null) throw new ArgumentNullException(nameof(orderedPath));
            if (string.IsNullOrWhiteSpace(phaseId))
                throw new ArgumentException("Phase ID required.", nameof(phaseId));
            var defs = ObstacleDefinitions.Validate(definitions);
            var hits = new List<ObstacleHitRequest>();

            foreach (var p in orderedPath)
            {
                if (!snapshot.Contains(p))
                    throw new ArgumentOutOfRangeException(nameof(orderedPath));
                var obstacles = snapshot.At(p);
                var stop = false;
                foreach (var state in obstacles)
                {
                    var definition = ObstacleDefinitions.Require(defs, state);
                    if (definition.LineInteraction != LineInteraction.AbsorbWithoutDamage)
                        hits.Add(new ObstacleHitRequest(
                            p, ObstacleDamageKind.Line, phaseId));
                    if (definition.LineInteraction == LineInteraction.StopAfterDamage ||
                        definition.LineInteraction == LineInteraction.AbsorbWithoutDamage)
                        stop = true;
                }
                if (stop) return new BeamPlan(hits, true, p);
            }
            return new BeamPlan(hits, false, null);
        }
    }

    public sealed class TurnReceipt
    {
        public string TurnId { get; }
        public TurnMutationKind Kind { get; }
        public bool Committed { get; }

        public TurnReceipt(string turnId, TurnMutationKind kind, bool committed)
        {
            if (string.IsNullOrWhiteSpace(turnId))
                throw new ArgumentException("Turn ID required.", nameof(turnId));
            if (!Enum.IsDefined(typeof(TurnMutationKind), kind))
                throw new ArgumentOutOfRangeException(nameof(kind));
            TurnId = turnId;
            Kind = kind;
            Committed = committed;
        }

        public bool IsLegitimatePlayerTurn =>
            Committed && Kind == TurnMutationKind.PlayerMove;
    }

    public sealed class SpreadPlan
    {
        public ObstacleSnapshot Result { get; }
        public IReadOnlyList<ObstacleState> Spawned { get; }
        public IReadOnlyList<string> ProcessedTurnIds { get; }
        public int CandidateCount { get; }

        internal SpreadPlan(ObstacleSnapshot result,
            IEnumerable<ObstacleState> spawned, int candidates,
            IEnumerable<string> processedTurnIds)
        {
            Result = result;
            Spawned = new ReadOnlyCollection<ObstacleState>(spawned.ToList());
            ProcessedTurnIds = new ReadOnlyCollection<string>(
                processedTurnIds.OrderBy(x => x, StringComparer.Ordinal).ToList());
            CandidateCount = candidates;
        }
    }

    public static class ObstacleSpreadPlanner
    {
        public static SpreadPlan Plan(ObstacleSnapshot snapshot,
            IEnumerable<ObstacleDefinition> definitions, TurnReceipt turn,
            uint seed, int maximumNewCells = 1, int maximumTotalObstacles = 4096,
            IEnumerable<string> processedTurnIds = null)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            if (turn == null) throw new ArgumentNullException(nameof(turn));
            if (seed == 0) throw new ArgumentOutOfRangeException(nameof(seed));
            if (maximumNewCells < 0 || maximumNewCells > 256)
                throw new ArgumentOutOfRangeException(nameof(maximumNewCells));
            if (maximumTotalObstacles < 1 || maximumTotalObstacles > 65536)
                throw new ArgumentOutOfRangeException(nameof(maximumTotalObstacles));
            var defs = ObstacleDefinitions.Validate(definitions);
            foreach (var state in snapshot.States)
                ObstacleDefinitions.Require(defs, state);
            var processed = new HashSet<string>(
                processedTurnIds ?? Enumerable.Empty<string>(), StringComparer.Ordinal);

            if (!turn.IsLegitimatePlayerTurn || processed.Contains(turn.TurnId) ||
                maximumNewCells == 0 || snapshot.States.Count >= maximumTotalObstacles)
                return new SpreadPlan(snapshot, new ObstacleState[0], 0, processed);

            processed.Add(turn.TurnId);

            var sources = snapshot.States.Where(x =>
                ObstacleDefinitions.Require(defs, x).Spreads)
                .OrderBy(x => x.InstanceId, StringComparer.Ordinal).ToArray();
            var occupied = new HashSet<string>(snapshot.States.Select(x =>
                CellLayerKey(x.Position, x.Layer)), StringComparer.Ordinal);
            var candidates = new List<Tuple<ObstacleState, BoardPosition>>();

            foreach (var source in sources)
            {
                foreach (var p in Orthogonal(snapshot, source.Position))
                {
                    var key = CellLayerKey(p, source.Layer);
                    if (!occupied.Contains(key))
                        candidates.Add(Tuple.Create(source, p));
                }
            }

            candidates = candidates.GroupBy(x =>
                    x.Item1.DefinitionId + "|" + CellLayerKey(x.Item2, x.Item1.Layer),
                    StringComparer.Ordinal)
                .Select(x => x.OrderBy(y => y.Item1.InstanceId,
                    StringComparer.Ordinal).First())
                .OrderBy(x => x.Item2.Layer)
                .ThenBy(x => x.Item2.Row)
                .ThenBy(x => x.Item2.Column)
                .ThenBy(x => x.Item1.InstanceId, StringComparer.Ordinal).ToList();

            var rng = new SeededRandom(seed);
            var spawned = new List<ObstacleState>();
            var available = new List<Tuple<ObstacleState, BoardPosition>>(candidates);
            while (available.Count > 0 && spawned.Count < maximumNewCells &&
                snapshot.States.Count + spawned.Count < maximumTotalObstacles)
            {
                var index = rng.NextIndex(available.Count);
                var choice = available[index];
                available.RemoveAt(index);
                var def = ObstacleDefinitions.Require(defs, choice.Item1);
                var id = "spread:" + turn.TurnId + ":" +
                    choice.Item2.ToString() + ":" + choice.Item1.DefinitionId;
                var next = new ObstacleState(id, choice.Item1.DefinitionId,
                    choice.Item2, choice.Item1.Layer, def.MaxDurability);
                var key = CellLayerKey(next.Position, next.Layer);
                if (occupied.Add(key))
                    spawned.Add(next);
                available.RemoveAll(x =>
                    CellLayerKey(x.Item2, x.Item1.Layer) == key);
            }

            return new SpreadPlan(
                new ObstacleSnapshot(snapshot.Rows, snapshot.Columns,
                    snapshot.States.Concat(spawned), snapshot.Layers),
                spawned, candidates.Count, processed);
        }

        private static IEnumerable<BoardPosition> Orthogonal(
            ObstacleSnapshot snapshot, BoardPosition p)
        {
            if (p.Row > 0) yield return new BoardPosition(p.Row - 1, p.Column, p.Layer);
            if (p.Row + 1 < snapshot.Rows)
                yield return new BoardPosition(p.Row + 1, p.Column, p.Layer);
            if (p.Column > 0) yield return new BoardPosition(p.Row, p.Column - 1, p.Layer);
            if (p.Column + 1 < snapshot.Columns)
                yield return new BoardPosition(p.Row, p.Column + 1, p.Layer);
        }

        private static string CellLayerKey(BoardPosition p, ObstacleLayerKind layer) =>
            p.ToString() + "|" + (int)layer;
    }

    public sealed class CountdownState
    {
        public string InstanceId { get; }
        public BoardPosition Position { get; }
        public int FuseRemaining { get; }
        public CountdownZeroRule ZeroRule { get; }

        public CountdownState(string instanceId, BoardPosition position,
            int fuseRemaining, CountdownZeroRule zeroRule)
        {
            if (string.IsNullOrWhiteSpace(instanceId))
                throw new ArgumentException("Countdown instance ID required.", nameof(instanceId));
            if (fuseRemaining < 0 || fuseRemaining > 999)
                throw new ArgumentOutOfRangeException(nameof(fuseRemaining));
            if (!Enum.IsDefined(typeof(CountdownZeroRule), zeroRule))
                throw new ArgumentOutOfRangeException(nameof(zeroRule));
            InstanceId = instanceId;
            Position = position;
            FuseRemaining = fuseRemaining;
            ZeroRule = zeroRule;
        }

        internal CountdownState WithFuse(int fuse) =>
            new CountdownState(InstanceId, Position, fuse, ZeroRule);
    }

    public sealed class CountdownPlan
    {
        public IReadOnlyList<CountdownState> States { get; }
        public IReadOnlyList<string> ProcessedTurnIds { get; }
        public CountdownOutcome Outcome { get; }

        internal CountdownPlan(IEnumerable<CountdownState> states,
            IEnumerable<string> processed, CountdownOutcome outcome)
        {
            States = new ReadOnlyCollection<CountdownState>(states.ToList());
            ProcessedTurnIds = new ReadOnlyCollection<string>(
                processed.OrderBy(x => x, StringComparer.Ordinal).ToList());
            Outcome = outcome;
        }
    }

    public static class CountdownPlanner
    {
        public static CountdownPlan Plan(IEnumerable<CountdownState> states,
            TurnReceipt turn, IEnumerable<string> processedTurnIds,
            bool goalsAchievedAfterResolution)
        {
            if (states == null) throw new ArgumentNullException(nameof(states));
            if (turn == null) throw new ArgumentNullException(nameof(turn));
            var list = states.ToList();
            if (list.Any(x => x == null))
                throw new ArgumentException("Null countdown state.", nameof(states));
            if (list.GroupBy(x => x.InstanceId, StringComparer.Ordinal)
                .Any(x => x.Count() > 1))
                throw new ArgumentException("Duplicate countdown instance ID.", nameof(states));
            var processed = new HashSet<string>(
                processedTurnIds ?? Enumerable.Empty<string>(), StringComparer.Ordinal);

            if (!turn.IsLegitimatePlayerTurn || processed.Contains(turn.TurnId))
                return new CountdownPlan(list, processed, CountdownOutcome.None);

            processed.Add(turn.TurnId);
            var next = list.Select(x => x.FuseRemaining > 0
                ? x.WithFuse(x.FuseRemaining - 1)
                : x).ToList();
            var zero = next.Where(x => x.FuseRemaining == 0).ToArray();
            if (zero.Length == 0)
                return new CountdownPlan(next, processed, CountdownOutcome.None);
            if (goalsAchievedAfterResolution)
                return new CountdownPlan(next, processed,
                    CountdownOutcome.GoalWinPrecedence);
            var outcome = zero.Any(x => x.ZeroRule == CountdownZeroRule.Lose)
                ? CountdownOutcome.Loss
                : CountdownOutcome.Detonate;
            return new CountdownPlan(next, processed, outcome);
        }
    }

    public readonly struct EggOutput
    {
        public string ContentId { get; }
        public int Weight { get; }

        public EggOutput(string contentId, int weight)
        {
            if (string.IsNullOrWhiteSpace(contentId))
                throw new ArgumentException("Egg output ID required.", nameof(contentId));
            if (weight < 1 || weight > 1000000)
                throw new ArgumentOutOfRangeException(nameof(weight));
            ContentId = contentId;
            Weight = weight;
        }
    }

    public sealed class MysteryEggState
    {
        public string InstanceId { get; }
        public int Stage { get; }
        public int FinalStage { get; }

        public MysteryEggState(string instanceId, int stage, int finalStage)
        {
            if (string.IsNullOrWhiteSpace(instanceId))
                throw new ArgumentException("Egg instance ID required.", nameof(instanceId));
            if (finalStage < 1 || finalStage > 64 ||
                stage < 0 || stage > finalStage)
                throw new ArgumentOutOfRangeException(nameof(stage));
            InstanceId = instanceId;
            Stage = stage;
            FinalStage = finalStage;
        }
    }

    public readonly struct EggResolution
    {
        public int NewStage { get; }
        public bool Hatched { get; }
        public string OutputContentId { get; }

        public EggResolution(int newStage, bool hatched, string output)
        {
            NewStage = newStage;
            Hatched = hatched;
            OutputContentId = output;
        }
    }

    public static class MysteryEggPlanner
    {
        public static EggResolution Advance(MysteryEggState egg,
            IReadOnlyList<EggOutput> outputs, uint seed)
        {
            if (egg == null) throw new ArgumentNullException(nameof(egg));
            if (outputs == null || outputs.Count == 0)
                throw new ArgumentException("Egg output catalog required.", nameof(outputs));
            if (seed == 0) throw new ArgumentOutOfRangeException(nameof(seed));
            long total = 0;
            foreach (var output in outputs) total += output.Weight;
            if (total < 1 || total > Int32.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(outputs));

            // A final-stage egg is already resolved. Replaying the same
            // logical state must never hatch or award output a second time.
            if (egg.Stage >= egg.FinalStage)
                return new EggResolution(egg.Stage, false, null);

            var nextStage = Math.Min(egg.FinalStage, egg.Stage + 1);
            if (nextStage < egg.FinalStage)
                return new EggResolution(nextStage, false, null);

            var rng = new SeededRandom(seed);
            var roll = rng.NextIndex((int)total);
            var cumulative = 0;
            foreach (var output in outputs)
            {
                cumulative += output.Weight;
                if (roll < cumulative)
                    return new EggResolution(nextStage, true, output.ContentId);
            }
            throw new InvalidOperationException("Egg output selection failed.");
        }
    }
}
