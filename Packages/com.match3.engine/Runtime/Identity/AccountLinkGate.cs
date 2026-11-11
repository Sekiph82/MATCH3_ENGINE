// ENG-M17-003 AccountLinkGate — guest-to-account linking contract
// stdlib only: System, System.Collections.Generic
using System;
using System.Collections.Generic;

namespace Match3.Engine.Runtime.Identity
{
    public enum LinkConflict
    {
        None,
        LocalAhead,
        RemoteAhead,
        Diverged
    }

    public readonly struct LinkRequest
    {
        public string GameId     { get; }
        public string GuestKey   { get; }
        public string AccountId  { get; }

        private LinkRequest(string gameId, string guestKey, string accountId)
        {
            GameId    = gameId;
            GuestKey  = guestKey;
            AccountId = accountId;
        }

        public static LinkRequest Create(string gameId, string guestKey, string accountId)
        {
            if (string.IsNullOrEmpty(gameId))    throw new ArgumentException("GameId must not be null or empty.",    nameof(gameId));
            if (string.IsNullOrEmpty(guestKey))  throw new ArgumentException("GuestKey must not be null or empty.",  nameof(guestKey));
            if (string.IsNullOrEmpty(accountId)) throw new ArgumentException("AccountId must not be null or empty.", nameof(accountId));
            return new LinkRequest(gameId, guestKey, accountId);
        }
    }

    public readonly struct AccountLinkPolicy
    {
        public bool   RequireUserWarning  { get; }
        public bool   AllowOverwrite      { get; }
        public string PolicyDescription   { get; }

        private AccountLinkPolicy(bool requireUserWarning, bool allowOverwrite, string policyDescription)
        {
            if (string.IsNullOrEmpty(policyDescription))
                throw new ArgumentException("PolicyDescription must not be null or empty.", nameof(policyDescription));
            RequireUserWarning = requireUserWarning;
            AllowOverwrite     = allowOverwrite;
            PolicyDescription  = policyDescription;
        }

        public static AccountLinkPolicy Safe(string desc) =>
            new AccountLinkPolicy(requireUserWarning: true, allowOverwrite: false, policyDescription: desc);

        public static AccountLinkPolicy Default =>
            Safe("Guest-to-account link requires user confirmation; silent overwrite prohibited");
    }

    public static class AccountLinkGate
    {
        private static AccountLinkPolicy Normalize(AccountLinkPolicy policy) =>
            policy.PolicyDescription == null ? AccountLinkPolicy.Default : policy;

        public readonly struct CanLinkResult
        {
            public bool   IsEligible { get; }
            public string Reason     { get; }
            internal CanLinkResult(bool isEligible, string reason) { IsEligible = isEligible; Reason = reason; }
        }

        public static CanLinkResult CanLink(string gameId, AccountLinkPolicy policy)
        {
            policy = Normalize(policy);

            // Guard 1 FIRST (CRITICAL): Wonder_Pop is never eligible for linking
            if (string.Equals(gameId, "Wonder_Pop", StringComparison.Ordinal))
                return new CanLinkResult(false, "WonderPopLinkProhibited");

            // Guard 2: silent overwrite (AllowOverwrite=true AND RequireUserWarning=false) is prohibited
            if (policy.AllowOverwrite && !policy.RequireUserWarning)
                return new CanLinkResult(false, "SilentOverwriteProhibited");

            return new CanLinkResult(true, string.Empty);
        }

        public static bool RequiresConflictResolution(LinkConflict conflict) =>
            conflict != LinkConflict.None;

        public static bool MustWarnUser(AccountLinkPolicy policy) =>
            Normalize(policy).RequireUserWarning;
    }

    public sealed class LinkAuditLog
    {
        private readonly List<(string gameId, bool succeeded, string reason)> _entries =
            new List<(string, bool, string)>();

        public int Count => _entries.Count;

        public void Record(LinkRequest request, bool succeeded, string reason)
        {
            if (string.IsNullOrEmpty(request.GameId))
                throw new ArgumentException("Request GameId must not be null or empty.", nameof(request));
            if (string.IsNullOrEmpty(reason))
                throw new ArgumentException("reason must not be null or empty.", nameof(reason));
            _entries.Add((request.GameId, succeeded, reason));
        }

        public void Clear() => _entries.Clear();

        public bool HasEntry(string gameId)
        {
            foreach (var e in _entries)
                if (string.Equals(e.gameId, gameId, StringComparison.Ordinal)) return true;
            return false;
        }
    }
}
