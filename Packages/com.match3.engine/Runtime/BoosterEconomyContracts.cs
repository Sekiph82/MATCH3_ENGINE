using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Match3.Engine.Runtime
{
    public enum BoosterKind
    {
        Hammer = 0,
        HorizontalLine = 1,
        VerticalLine = 2,
        Area = 3,
        Color = 4,
        Shuffle = 5,
        Swap = 6,
        TargetedMissile = 7
    }

    public enum BoosterTransactionState
    {
        Reserved = 0,
        Committed = 1,
        Refunded = 2,
        Granted = 3
    }

    public sealed class BoosterTransaction
    {
        public string SourceId { get; }
        public BoosterKind Kind { get; }
        public int Amount { get; }
        public BoosterTransactionState State { get; }

        public BoosterTransaction(string sourceId, BoosterKind kind, int amount,
            BoosterTransactionState state)
        {
            if (string.IsNullOrWhiteSpace(sourceId))
                throw new ArgumentException("Booster transaction source ID required.",
                    nameof(sourceId));
            if (!Enum.IsDefined(typeof(BoosterKind), kind))
                throw new ArgumentOutOfRangeException(nameof(kind));
            if (amount < 1 || amount > 1000000)
                throw new ArgumentOutOfRangeException(nameof(amount));
            if (!Enum.IsDefined(typeof(BoosterTransactionState), state))
                throw new ArgumentOutOfRangeException(nameof(state));
            SourceId = sourceId;
            Kind = kind;
            Amount = amount;
            State = state;
        }

        internal BoosterTransaction WithState(BoosterTransactionState state) =>
            new BoosterTransaction(SourceId, Kind, Amount, state);
    }

    public sealed class BoosterEconomySnapshot
    {
        private readonly ReadOnlyDictionary<BoosterKind, int> _balances;
        private readonly ReadOnlyDictionary<string, BoosterTransaction> _transactions;

        public IReadOnlyDictionary<BoosterKind, int> Balances => _balances;
        public IReadOnlyDictionary<string, BoosterTransaction> Transactions => _transactions;

        public BoosterEconomySnapshot(
            IEnumerable<KeyValuePair<BoosterKind, int>> balances,
            IEnumerable<BoosterTransaction> transactions = null)
        {
            if (balances == null) throw new ArgumentNullException(nameof(balances));
            var balanceMap = new Dictionary<BoosterKind, int>();
            foreach (var pair in balances)
            {
                if (!Enum.IsDefined(typeof(BoosterKind), pair.Key))
                    throw new ArgumentOutOfRangeException(nameof(balances));
                if (pair.Value < 0 || pair.Value > 1000000)
                    throw new ArgumentOutOfRangeException(nameof(balances));
                if (balanceMap.ContainsKey(pair.Key))
                    throw new ArgumentException("Duplicate booster balance.", nameof(balances));
                balanceMap.Add(pair.Key, pair.Value);
            }

            var txMap = new Dictionary<string, BoosterTransaction>(StringComparer.Ordinal);
            if (transactions != null)
            {
                foreach (var tx in transactions)
                {
                    if (tx == null)
                        throw new ArgumentException("Null booster transaction.",
                            nameof(transactions));
                    if (txMap.ContainsKey(tx.SourceId))
                        throw new ArgumentException("Duplicate booster transaction source.",
                            nameof(transactions));
                    txMap.Add(tx.SourceId, tx);
                }
            }

            _balances = new ReadOnlyDictionary<BoosterKind, int>(balanceMap);
            _transactions = new ReadOnlyDictionary<string, BoosterTransaction>(txMap);
        }

        public int Balance(BoosterKind kind)
        {
            int value;
            return _balances.TryGetValue(kind, out value) ? value : 0;
        }

        public int Available(BoosterKind kind)
        {
            var held = _transactions.Values
                .Where(x => x.Kind == kind &&
                    x.State == BoosterTransactionState.Reserved)
                .Sum(x => x.Amount);
            return Math.Max(0, Balance(kind) - held);
        }
    }

    public readonly struct BoosterEconomyReceipt
    {
        public string SourceId { get; }
        public BoosterKind Kind { get; }
        public BoosterTransactionState State { get; }
        public bool Applied { get; }
        public int BalanceBefore { get; }
        public int BalanceAfter { get; }

        public BoosterEconomyReceipt(string sourceId, BoosterKind kind,
            BoosterTransactionState state, bool applied,
            int balanceBefore, int balanceAfter)
        {
            SourceId = sourceId;
            Kind = kind;
            State = state;
            Applied = applied;
            BalanceBefore = balanceBefore;
            BalanceAfter = balanceAfter;
        }
    }

    public sealed class BoosterEconomyPlan
    {
        public BoosterEconomySnapshot Result { get; }
        public BoosterEconomyReceipt Receipt { get; }

        internal BoosterEconomyPlan(BoosterEconomySnapshot result,
            BoosterEconomyReceipt receipt)
        {
            Result = result ?? throw new ArgumentNullException(nameof(result));
            Receipt = receipt;
        }
    }

    public static class BoosterEconomyPlanner
    {
        public static BoosterEconomyPlan Reserve(BoosterEconomySnapshot snapshot,
            BoosterKind kind, string sourceId)
        {
            RequireSnapshot(snapshot);
            RequireSource(sourceId);
            BoosterTransaction existing;
            if (snapshot.Transactions.TryGetValue(sourceId, out existing))
            {
                RequireSameKind(existing, kind);
                return Noop(snapshot, existing);
            }
            if (snapshot.Available(kind) < 1)
                throw new InvalidOperationException("Insufficient booster inventory.");

            var tx = new BoosterTransaction(sourceId, kind, 1,
                BoosterTransactionState.Reserved);
            return WithTransaction(snapshot, tx, false, 0);
        }

        public static BoosterEconomyPlan Confirm(BoosterEconomySnapshot snapshot,
            string sourceId)
        {
            RequireSnapshot(snapshot);
            var existing = RequireTransaction(snapshot, sourceId);
            if (existing.State == BoosterTransactionState.Committed)
                return Noop(snapshot, existing);
            if (existing.State != BoosterTransactionState.Reserved)
                throw new InvalidOperationException(
                    "Only a reserved booster transaction may be confirmed.");
            var before = snapshot.Balance(existing.Kind);
            if (before < existing.Amount)
                throw new InvalidOperationException("Reserved inventory is no longer available.");
            return WithTransaction(snapshot,
                existing.WithState(BoosterTransactionState.Committed),
                true, -existing.Amount);
        }

        public static BoosterEconomyPlan Refund(BoosterEconomySnapshot snapshot,
            string sourceId)
        {
            RequireSnapshot(snapshot);
            var existing = RequireTransaction(snapshot, sourceId);
            if (existing.State == BoosterTransactionState.Refunded)
                return Noop(snapshot, existing);
            if (existing.State == BoosterTransactionState.Granted)
                throw new InvalidOperationException("A grant cannot be refunded as a spend.");

            var restore = existing.State == BoosterTransactionState.Committed
                ? existing.Amount
                : 0;
            return WithTransaction(snapshot,
                existing.WithState(BoosterTransactionState.Refunded),
                true, restore);
        }

        public static BoosterEconomyPlan Grant(BoosterEconomySnapshot snapshot,
            BoosterKind kind, string sourceId, int amount = 1)
        {
            RequireSnapshot(snapshot);
            RequireSource(sourceId);
            if (amount < 1 || amount > 1000000)
                throw new ArgumentOutOfRangeException(nameof(amount));

            BoosterTransaction existing;
            if (snapshot.Transactions.TryGetValue(sourceId, out existing))
            {
                if (existing.Kind != kind || existing.Amount != amount ||
                    existing.State != BoosterTransactionState.Granted)
                    throw new InvalidOperationException(
                        "Transaction source ID is already bound to a different operation.");
                return Noop(snapshot, existing);
            }

            var before = snapshot.Balance(kind);
            if ((long)before + amount > 1000000)
                throw new InvalidOperationException("Booster inventory limit exceeded.");
            var tx = new BoosterTransaction(sourceId, kind, amount,
                BoosterTransactionState.Granted);
            return WithTransaction(snapshot, tx, true, amount);
        }

        private static BoosterEconomyPlan WithTransaction(
            BoosterEconomySnapshot snapshot, BoosterTransaction transaction,
            bool changeBalance, int balanceDelta)
        {
            var balances = snapshot.Balances.ToDictionary(x => x.Key, x => x.Value);
            var before = snapshot.Balance(transaction.Kind);
            var after = before;
            if (changeBalance)
            {
                after = checked(before + balanceDelta);
                if (after < 0 || after > 1000000)
                    throw new InvalidOperationException("Invalid booster balance.");
                balances[transaction.Kind] = after;
            }
            else if (!balances.ContainsKey(transaction.Kind))
            {
                balances[transaction.Kind] = before;
            }

            var transactions = snapshot.Transactions.Values
                .Where(x => x.SourceId != transaction.SourceId)
                .Concat(new[] { transaction }).ToArray();
            var result = new BoosterEconomySnapshot(balances, transactions);
            return new BoosterEconomyPlan(result, new BoosterEconomyReceipt(
                transaction.SourceId, transaction.Kind, transaction.State,
                true, before, after));
        }

        private static BoosterEconomyPlan Noop(BoosterEconomySnapshot snapshot,
            BoosterTransaction transaction)
        {
            var balance = snapshot.Balance(transaction.Kind);
            return new BoosterEconomyPlan(snapshot, new BoosterEconomyReceipt(
                transaction.SourceId, transaction.Kind, transaction.State,
                false, balance, balance));
        }

        private static void RequireSnapshot(BoosterEconomySnapshot snapshot)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
        }

        private static void RequireSource(string sourceId)
        {
            if (string.IsNullOrWhiteSpace(sourceId))
                throw new ArgumentException("Transaction source ID required.",
                    nameof(sourceId));
        }

        private static BoosterTransaction RequireTransaction(
            BoosterEconomySnapshot snapshot, string sourceId)
        {
            RequireSource(sourceId);
            BoosterTransaction transaction;
            if (!snapshot.Transactions.TryGetValue(sourceId, out transaction))
                throw new InvalidOperationException("Unknown booster transaction.");
            return transaction;
        }

        private static void RequireSameKind(BoosterTransaction transaction,
            BoosterKind kind)
        {
            if (transaction.Kind != kind)
                throw new InvalidOperationException(
                    "Transaction source ID is already bound to another booster kind.");
        }
    }

    public enum BoosterAcquisitionSource
    {
        Owned = 0,
        FreeReward = 1,
        RewardedAd = 2,
        Paid = 3
    }

    public sealed class BoosterProfilePolicy
    {
        public bool IsChildProfile { get; }
        public bool AllowRewardedAcquisition { get; }
        public bool AllowPaidAcquisition { get; }
        public bool RequireExplicitConfirmation { get; }

        public BoosterProfilePolicy(bool isChildProfile,
            bool allowRewardedAcquisition, bool allowPaidAcquisition,
            bool requireExplicitConfirmation = true)
        {
            IsChildProfile = isChildProfile;
            AllowRewardedAcquisition = allowRewardedAcquisition;
            AllowPaidAcquisition = allowPaidAcquisition;
            RequireExplicitConfirmation = requireExplicitConfirmation;
        }

        public static BoosterProfilePolicy ChildSafe() =>
            new BoosterProfilePolicy(true, false, false, true);
    }

    public static class BoosterSafetyPolicy
    {
        public static void ValidateUse(BoosterProfilePolicy policy,
            BoosterAcquisitionSource source, bool explicitConfirmation,
            bool silentSpend)
        {
            if (policy == null) throw new ArgumentNullException(nameof(policy));
            if (!Enum.IsDefined(typeof(BoosterAcquisitionSource), source))
                throw new ArgumentOutOfRangeException(nameof(source));
            if (silentSpend)
                throw new InvalidOperationException("Silent booster spending is forbidden.");
            if (policy.RequireExplicitConfirmation && !explicitConfirmation)
                throw new InvalidOperationException("Explicit booster confirmation required.");
            if (source == BoosterAcquisitionSource.Paid &&
                (!policy.AllowPaidAcquisition || policy.IsChildProfile))
                throw new InvalidOperationException(
                    "Paid booster acquisition is disabled for this profile.");
            if (source == BoosterAcquisitionSource.RewardedAd &&
                !policy.AllowRewardedAcquisition)
                throw new InvalidOperationException(
                    "Rewarded booster acquisition is disabled for this profile.");
        }
    }

    public readonly struct BoosterActionToken : IEquatable<BoosterActionToken>
    {
        public int Generation { get; }
        public string ActionId { get; }

        internal BoosterActionToken(int generation, string actionId)
        {
            Generation = generation;
            ActionId = actionId;
        }

        public bool Equals(BoosterActionToken other) =>
            Generation == other.Generation &&
            string.Equals(ActionId, other.ActionId, StringComparison.Ordinal);
        public override bool Equals(object obj) =>
            obj is BoosterActionToken && Equals((BoosterActionToken)obj);
        public override int GetHashCode() =>
            (Generation * 397) ^ (ActionId == null ? 0 : ActionId.GetHashCode());
    }

    public sealed class BoosterActionGate
    {
        private readonly HashSet<string> _pending =
            new HashSet<string>(StringComparer.Ordinal);
        private int _generation;
        private bool _paused;

        public int Generation => _generation;
        public bool IsPaused => _paused;
        public int PendingCount => _pending.Count;

        public BoosterActionToken Begin(string actionId)
        {
            if (_paused) throw new InvalidOperationException("Booster action gate is paused.");
            if (string.IsNullOrWhiteSpace(actionId))
                throw new ArgumentException("Action ID required.", nameof(actionId));
            if (!_pending.Add(actionId))
                throw new InvalidOperationException("Duplicate pending booster action.");
            return new BoosterActionToken(_generation, actionId);
        }

        public bool TryCommit(BoosterActionToken token)
        {
            if (_paused || token.Generation != _generation ||
                string.IsNullOrEmpty(token.ActionId))
                return false;
            return _pending.Remove(token.ActionId);
        }

        public bool Cancel(BoosterActionToken token)
        {
            if (token.Generation != _generation ||
                string.IsNullOrEmpty(token.ActionId))
                return false;
            return _pending.Remove(token.ActionId);
        }

        public void Interrupt()
        {
            checked { _generation++; }
            _pending.Clear();
        }

        public bool Pause()
        {
            if (_paused) return false;
            _paused = true;
            Interrupt();
            return true;
        }

        public bool Resume()
        {
            if (!_paused) return false;
            _paused = false;
            return true;
        }
    }

    public static class BoosterRecoveryPlanner
    {
        public static BoosterEconomyPlan RecoverReservation(
            BoosterEconomySnapshot economy, string sourceId)
        {
            return BoosterEconomyPlanner.Refund(economy, sourceId);
        }
    }

    public enum ScorePowerMode
    {
        StandardCampaign = 0,
        Timed = 1,
        Endless = 2
    }

    public sealed class ScorePowerSnapshot
    {
        private readonly ReadOnlyCollection<string> _processedEventIds;

        public ScorePowerMode Mode { get; }
        public int Meter { get; }
        public int MaximumMeter { get; }
        public IReadOnlyList<string> ProcessedEventIds => _processedEventIds;

        public ScorePowerSnapshot(ScorePowerMode mode, int meter,
            int maximumMeter, IEnumerable<string> processedEventIds = null)
        {
            if (!Enum.IsDefined(typeof(ScorePowerMode), mode))
                throw new ArgumentOutOfRangeException(nameof(mode));
            if (maximumMeter < 1 || maximumMeter > 1000000 ||
                meter < 0 || meter > maximumMeter)
                throw new ArgumentOutOfRangeException(nameof(meter));
            Mode = mode;
            Meter = meter;
            MaximumMeter = maximumMeter;
            var ids = (processedEventIds ?? Enumerable.Empty<string>()).ToList();
            if (ids.Any(string.IsNullOrWhiteSpace) ||
                ids.Distinct(StringComparer.Ordinal).Count() != ids.Count)
                throw new ArgumentException("Invalid score-power event IDs.",
                    nameof(processedEventIds));
            if (ids.Count > 100000)
                throw new ArgumentException("Score-power event history limit exceeded.",
                    nameof(processedEventIds));
            _processedEventIds = new ReadOnlyCollection<string>(
                ids.OrderBy(x => x, StringComparer.Ordinal).ToList());
        }
    }

    public sealed class ScorePowerPlan
    {
        public ScorePowerSnapshot Result { get; }
        public bool Applied { get; }
        public int Delta { get; }

        internal ScorePowerPlan(ScorePowerSnapshot result, bool applied, int delta)
        {
            Result = result;
            Applied = applied;
            Delta = delta;
        }
    }

    public static class ScorePowerPlanner
    {
        public static ScorePowerPlan Earn(ScorePowerSnapshot snapshot,
            string eventId, int scoreDelta, int scorePerMeterPoint = 100)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            RequireEventId(eventId);
            if (scoreDelta < 0 || scoreDelta > 100000000)
                throw new ArgumentOutOfRangeException(nameof(scoreDelta));
            if (scorePerMeterPoint < 1 || scorePerMeterPoint > 1000000)
                throw new ArgumentOutOfRangeException(nameof(scorePerMeterPoint));

            if (snapshot.Mode == ScorePowerMode.StandardCampaign)
                return new ScorePowerPlan(snapshot, false, 0);

            var key = "earn:" + eventId;
            if (snapshot.ProcessedEventIds.Contains(key))
                return new ScorePowerPlan(snapshot, false, 0);

            var earned = scoreDelta / scorePerMeterPoint;
            var next = Math.Min(snapshot.MaximumMeter,
                checked(snapshot.Meter + earned));
            return NewPlan(snapshot, key, next, true, next - snapshot.Meter);
        }

        public static ScorePowerPlan Consume(ScorePowerSnapshot snapshot,
            string actionId, int cost)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            RequireEventId(actionId);
            if (cost < 1 || cost > snapshot.MaximumMeter)
                throw new ArgumentOutOfRangeException(nameof(cost));
            if (snapshot.Mode == ScorePowerMode.StandardCampaign)
                throw new InvalidOperationException(
                    "Score-to-power is disabled in standard campaign.");

            var key = "consume:" + actionId;
            if (snapshot.ProcessedEventIds.Contains(key))
                return new ScorePowerPlan(snapshot, false, 0);
            if (snapshot.Meter < cost)
                throw new InvalidOperationException("Insufficient score-power meter.");

            return NewPlan(snapshot, key, snapshot.Meter - cost,
                true, -cost);
        }

        private static ScorePowerPlan NewPlan(ScorePowerSnapshot snapshot,
            string eventKey, int meter, bool applied, int delta)
        {
            var ids = snapshot.ProcessedEventIds.Concat(new[] { eventKey });
            return new ScorePowerPlan(new ScorePowerSnapshot(
                snapshot.Mode, meter, snapshot.MaximumMeter, ids), applied, delta);
        }

        private static void RequireEventId(string eventId)
        {
            if (string.IsNullOrWhiteSpace(eventId))
                throw new ArgumentException("Score-power event ID required.",
                    nameof(eventId));
        }
    }
}
