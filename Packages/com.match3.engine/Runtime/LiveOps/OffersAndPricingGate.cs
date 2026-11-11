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

    public readonly struct OfferPolicy
    {
        public bool RequireOwnerApproval { get; }
        public bool BlockUnapprovedRealMoney { get; }
        public string PolicyDescription { get; }

        public OfferPolicy(bool requireOwnerApproval, bool blockUnapprovedRealMoney, string policyDescription)
        {
            if (string.IsNullOrEmpty(policyDescription))
                throw new ArgumentException("PolicyDescription must not be null or empty.", nameof(policyDescription));
            RequireOwnerApproval = requireOwnerApproval;
            BlockUnapprovedRealMoney = blockUnapprovedRealMoney;
            PolicyDescription = policyDescription;
        }

        public static OfferPolicy Safe(string desc) => new OfferPolicy(true, true, desc);

        public static readonly OfferPolicy Default = Safe("Default offer policy - require owner approval, block unapproved real-money offers");
    }

    public static class OffersAndPricingGate
    {
        public static bool CanPresentOffer(string gameId, OfferRecord record, OfferPolicy policy, out string reason)
        {
            if (policy.PolicyDescription == null) policy = OfferPolicy.Default;

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

            // Guard 2: policy blocks unapproved real-money offers
            if (policy.BlockUnapprovedRealMoney && record.IsRealMoneyOffer && !record.HasStoreAuthorization)
            {
                reason = "RealMoneyOfferRequiresStoreAuth";
                return false;
            }

            // Guard 3: policy requires owner approval
            if (policy.RequireOwnerApproval && !record.IsOwnerApproved)
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
