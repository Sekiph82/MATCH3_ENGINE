using System;
using System.Collections.Generic;

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
        public static CollectionEligibilityResult CanCollectData(string gameId, IdentityPolicy policy)
        {
            // Guard 1 FIRST (CRITICAL): Wonder_Pop is never eligible for data collection
            if (string.Equals(gameId, "Wonder_Pop", StringComparison.Ordinal))
                return new CollectionEligibilityResult(false, "WonderPopCollectionProhibited");

            // Guard 2: policy must allow collection
            if (!policy.CollectionAllowed)
                return new CollectionEligibilityResult(false, "CollectionNotAllowed");

            return new CollectionEligibilityResult(true, "Eligible");
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
