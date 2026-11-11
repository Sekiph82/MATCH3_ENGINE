using System;
using System.Collections.Generic;
using Match3.Engine.Runtime.Analytics;

namespace Match3.Engine.Runtime.Identity
{
    public readonly struct GuestIdentity
    {
        public string GameId { get; }
        public string LocalKey { get; }
        public bool IsRestricted { get; }

        public GuestIdentity(string gameId, string localKey, bool isRestricted)
        {
            if (string.IsNullOrEmpty(gameId)) throw new ArgumentException("GameId must not be null or empty.", nameof(gameId));
            if (string.IsNullOrEmpty(localKey)) throw new ArgumentException("LocalKey must not be null or empty.", nameof(localKey));
            GameId = gameId;
            LocalKey = localKey;
            IsRestricted = isRestricted;
        }

        public bool IsEmpty => string.IsNullOrEmpty(LocalKey);

        public static GuestIdentity Create(string gameId) =>
            new GuestIdentity(gameId, Guid.NewGuid().ToString("N"), false);

        public static GuestIdentity CreateChild(string gameId) =>
            new GuestIdentity(gameId, Guid.NewGuid().ToString("N"), true);
    }

    public readonly struct IdentityPolicy
    {
        public bool RequireExplicitSignup { get; }
        public bool CollectionAllowed { get; }
        public string PolicyDescription { get; }

        public IdentityPolicy(bool requireExplicitSignup, bool collectionAllowed, string policyDescription)
        {
            if (string.IsNullOrEmpty(policyDescription)) throw new ArgumentException("PolicyDescription must not be null or empty.", nameof(policyDescription));
            RequireExplicitSignup = requireExplicitSignup;
            CollectionAllowed = collectionAllowed;
            PolicyDescription = policyDescription;
        }

        public static IdentityPolicy GuestFirst(string desc) =>
            new IdentityPolicy(false, false, desc);

        public static readonly IdentityPolicy Default =
            GuestFirst("Play without signup; guest identity only; data collection disabled until consent");
    }

    public readonly struct CollectionEligibilityResult
    {
        public bool IsEligible { get; }
        public string Reason { get; }

        public CollectionEligibilityResult(bool isEligible, string reason)
        {
            IsEligible = isEligible;
            Reason = reason;
        }
    }

    public static class AnonymousIdentityGate
    {
        /// <summary>
        /// Data collection for a guest identity. Child/unknown identities are never collected from and
        /// explicit analytics consent (ConsentAnalyticsGate under ConsentPolicy.Default) is required
        /// unconditionally; the policy can only add restriction.
        /// </summary>
        public static CollectionEligibilityResult CanCollectData(
            string gameId, GuestIdentity identity, ConsentRecord consent, IdentityPolicy policy)
        {
            if (policy.PolicyDescription == null) policy = IdentityPolicy.Default;

            if (string.IsNullOrEmpty(gameId))
                return new CollectionEligibilityResult(false, "GameIdMissing");
            if (!GameProfileCatalog.IsKnownId(gameId))
                return new CollectionEligibilityResult(false, "UnknownGame");

            if (!string.Equals(gameId, identity.GameId, StringComparison.Ordinal) ||
                !string.Equals(gameId, consent.GameId, StringComparison.Ordinal))
                return new CollectionEligibilityResult(false, "GameIdMismatch");

            if (string.Equals(gameId, "Wonder_Pop", StringComparison.Ordinal))
                return new CollectionEligibilityResult(false, "WonderPopCollectionProhibited");

            // Unconditional: child-restricted or unknown (empty) identities are never collected from
            if (identity.IsEmpty || identity.IsRestricted)
                return new CollectionEligibilityResult(false, "ChildCollectionProhibited");

            // Unconditional: explicit analytics consent is required under every policy
            if (!ConsentAnalyticsGate.CanCollectAnalytics(gameId, consent, ConsentPolicy.Default, out _))
                return new CollectionEligibilityResult(false, "ConsentRequired");

            if (!policy.CollectionAllowed)
                return new CollectionEligibilityResult(false, "CollectionNotAllowed");

            return new CollectionEligibilityResult(true, "Eligible");
        }

        /// <summary>Identity and consent unknown: fails closed with "ConsentRequired".</summary>
        [Obsolete("Identity and consent unknown: always ineligible (ConsentRequired). Use the overload taking GuestIdentity and ConsentRecord.")]
        public static CollectionEligibilityResult CanCollectData(string gameId, IdentityPolicy policy)
        {
            if (string.IsNullOrEmpty(gameId))
                return new CollectionEligibilityResult(false, "GameIdMissing");
            if (!GameProfileCatalog.IsKnownId(gameId))
                return new CollectionEligibilityResult(false, "UnknownGame");
            if (string.Equals(gameId, "Wonder_Pop", StringComparison.Ordinal))
                return new CollectionEligibilityResult(false, "WonderPopCollectionProhibited");

            return new CollectionEligibilityResult(false, "ConsentRequired");
        }

        public static bool RequiresGuestMode(IdentityPolicy policy) =>
            !policy.RequireExplicitSignup;

        public static bool IsChildRestricted(GuestIdentity identity) =>
            identity.IsRestricted;
    }

    public sealed class GuestIdentityStore
    {
        private readonly Dictionary<string, GuestIdentity> _store = new Dictionary<string, GuestIdentity>();

        public void Store(GuestIdentity identity) =>
            _store[identity.GameId] = identity;

        public bool TryGet(string gameId, out GuestIdentity identity) =>
            _store.TryGetValue(gameId, out identity);

        public void Clear() => _store.Clear();

        public int Count => _store.Count;
    }
}
