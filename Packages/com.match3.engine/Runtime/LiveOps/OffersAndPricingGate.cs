using System;
using System.Collections.Generic;

namespace Match3.Engine.Runtime.LiveOps
{
    public enum OfferType
    {
        StarterBundle,
        BonusBoosterEvent,
        ContentAvailability,
        ShopPromotion,
        SeasonalOffer,
        LimitedTimeBundle
    }

    public readonly struct OfferRecord
    {
        public string GameId { get; }
        public OfferType Type { get; }
        public bool IsOwnerApproved { get; }
        public bool IsRealMoneyOffer { get; }
        public bool HasStoreAuthorization { get; }

        private OfferRecord(string gameId, OfferType type, bool isOwnerApproved, bool isRealMoneyOffer, bool hasStoreAuthorization)
        {
            GameId = gameId;
            Type = type;
            IsOwnerApproved = isOwnerApproved;
            IsRealMoneyOffer = isRealMoneyOffer;
            HasStoreAuthorization = hasStoreAuthorization;
        }

        public static OfferRecord Create(string gameId, OfferType type, bool isOwnerApproved, bool isRealMoneyOffer, bool hasStoreAuthorization)
        {
            if (string.IsNullOrEmpty(gameId))
                throw new ArgumentException("gameId must not be null or empty.", nameof(gameId));
            return new OfferRecord(gameId, type, isOwnerApproved, isRealMoneyOffer, hasStoreAuthorization);
        }

        public bool RequiresStoreAuth => IsRealMoneyOffer && !HasStoreAuthorization;

        public bool IsEligibleForPresentation => IsOwnerApproved && (!IsRealMoneyOffer || HasStoreAuthorization);
    }

    // Owner approval and store authorization for real-money offers are unconditional core invariants,
    // so the policy carries no relaxing flags; default(OfferPolicy) therefore cannot fail open.
    public readonly struct OfferPolicy
    {
        public string PolicyDescription { get; }

        public OfferPolicy(string policyDescription)
        {
            if (string.IsNullOrEmpty(policyDescription))
                throw new ArgumentException("PolicyDescription must not be null or empty.", nameof(policyDescription));
            PolicyDescription = policyDescription;
        }

        public static OfferPolicy Safe(string desc) => new OfferPolicy(desc);

        public static readonly OfferPolicy Default = Safe("Default offer policy - require owner approval, block unapproved real-money offers");
    }

    public static class OffersAndPricingGate
    {
        public static bool CanPresentOffer(string gameId, OfferRecord record, OfferPolicy policy, out string reason)
        {
            if (string.IsNullOrEmpty(gameId))
            {
                reason = "GameIdMissing";
                return false;
            }
            if (!GameProfileCatalog.IsKnownId(gameId))
            {
                reason = "UnknownGame";
                return false;
            }

            if (!string.Equals(gameId, record.GameId, StringComparison.Ordinal))
            {
                reason = "GameIdMismatch";
                return false;
            }

            // Guard 1: Wonder_Pop real-money without store auth (Ordinal, structurally first)
            if (string.Equals(gameId, "Wonder_Pop", StringComparison.Ordinal) && record.IsRealMoneyOffer && !record.HasStoreAuthorization)
            {
                reason = "WonderPopRealMoneyRequiresStoreAuth";
                return false;
            }

            // Guard 2 (unconditional): real-money offers require store authorization
            if (record.IsRealMoneyOffer && !record.HasStoreAuthorization)
            {
                reason = "RealMoneyOfferRequiresStoreAuth";
                return false;
            }

            // Guard 3 (unconditional): every offer requires owner approval
            if (!record.IsOwnerApproved)
            {
                reason = "OfferRequiresOwnerApproval";
                return false;
            }

            reason = string.Empty;
            return true;
        }

        public static bool RequiresStoreAuth(OfferRecord record) => record.RequiresStoreAuth;

        public static bool IsEligibleForPresentation(OfferRecord record) => record.IsEligibleForPresentation;
    }

    public sealed class OfferLog
    {
        private readonly List<string> _entries = new List<string>();

        public void Record(string gameId, string offerType)
        {
            if (string.IsNullOrEmpty(gameId))
                throw new ArgumentException("gameId must not be null or empty.", nameof(gameId));
            if (string.IsNullOrEmpty(offerType))
                throw new ArgumentException("offerType must not be null or empty.", nameof(offerType));
            _entries.Add(gameId + ":" + offerType);
        }

        public int Count => _entries.Count;

        public void Clear() => _entries.Clear();

        public bool HasEntry(string gameId) => _entries.Exists(e => e.StartsWith(gameId + ":", StringComparison.Ordinal));
    }
}
