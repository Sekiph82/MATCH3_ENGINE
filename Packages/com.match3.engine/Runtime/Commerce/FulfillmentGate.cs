using System;
using System.Collections.Generic;

namespace Match3.Engine.Runtime.Commerce
{
    public readonly struct FulfillmentRecord
    {
        public string OrderId { get; }
        public string GameId { get; }
        public bool IsAcknowledged { get; }
        public bool IsGranted { get; }

        public FulfillmentRecord(string orderId, string gameId, bool isGranted, bool isAcknowledged)
        {
            if (string.IsNullOrEmpty(orderId))
                throw new ArgumentException("OrderId must not be null or empty.", nameof(orderId));
            if (string.IsNullOrEmpty(gameId))
                throw new ArgumentException("GameId must not be null or empty.", nameof(gameId));
            OrderId = orderId;
            GameId = gameId;
            IsGranted = isGranted;
            IsAcknowledged = isAcknowledged;
        }

        public static FulfillmentRecord Pending(string orderId, string gameId) =>
            new FulfillmentRecord(orderId, gameId, isGranted: false, isAcknowledged: false);

        public static FulfillmentRecord Granted(string orderId, string gameId) =>
            new FulfillmentRecord(orderId, gameId, isGranted: true, isAcknowledged: false);

        public static FulfillmentRecord Acknowledged(string orderId, string gameId) =>
            new FulfillmentRecord(orderId, gameId, isGranted: true, isAcknowledged: true);
    }

    public static class FulfillmentGate
    {
        public static bool CanGrantReward(string gameId, FulfillmentRecord record)
        {
            if (!string.Equals(gameId, record.GameId, StringComparison.Ordinal))
                return false;

            if (string.Equals(gameId, "Wonder_Pop", StringComparison.Ordinal))
                return false;
            if (record.IsGranted)
                return false;
            if (record.IsAcknowledged)
                return false;
            return true;
        }

        public static bool ShouldAcknowledge(FulfillmentRecord record) =>
            record.IsGranted && !record.IsAcknowledged;

        public static bool IsAlreadyFulfilled(FulfillmentRecord record) =>
            record.IsGranted && record.IsAcknowledged;

        public static void AssertGrantedBeforeAcknowledge(FulfillmentRecord record)
        {
            if (!record.IsGranted)
                throw new InvalidOperationException("Cannot acknowledge before granting fulfillment");
        }
    }

    public sealed class FulfillmentLedger
    {
        private readonly Dictionary<string, FulfillmentRecord> _records = new Dictionary<string, FulfillmentRecord>();

        public int Count => _records.Count;

        public bool TryGetRecord(string orderId, out FulfillmentRecord record)
        {
            if (string.IsNullOrEmpty(orderId))
                throw new ArgumentException("OrderId must not be null or empty.", nameof(orderId));
            return _records.TryGetValue(orderId, out record);
        }

        public void RecordPending(FulfillmentRecord record)
        {
            if (_records.TryGetValue(record.OrderId, out var existing) && existing.IsGranted)
                throw new ArgumentException($"Order '{record.OrderId}' already has a granted record and cannot be re-added as pending.");
            _records[record.OrderId] = record;
        }

        public void RecordGranted(FulfillmentRecord record)
        {
            _records[record.OrderId] = record;
        }

        public void RecordAcknowledged(FulfillmentRecord record)
        {
            _records[record.OrderId] = record;
        }

        public void Clear()
        {
            _records.Clear();
        }
    }
}
