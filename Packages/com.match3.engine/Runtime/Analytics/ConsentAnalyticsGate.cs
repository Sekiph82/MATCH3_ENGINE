// ENG-M18-003 ConsentAnalyticsGate stdlib contract
// stdlib only: System, System.Collections.Generic
using System;
using System.Collections.Generic;

namespace Match3.Engine.Runtime.Analytics
{
    public enum ConsentBasis
    {
        NoneRequired,
        ExplicitConsent,
        LegitimateInterest,
        ChildSafe
    }

    public readonly struct ConsentRecord
    {
        public string GameId { get; }
        public ConsentBasis Basis { get; }
        public bool IsRevoked { get; }

        // IsAnalyticsAllowed: not revoked and there is a lawful basis (NoneRequired = no analytics allowed)
        public bool IsAnalyticsAllowed => !IsRevoked && Basis != ConsentBasis.NoneRequired;

        private ConsentRecord(string gameId, ConsentBasis basis, bool isRevoked)
        {
            GameId = gameId;
            Basis = basis;
            IsRevoked = isRevoked;
        }

        public static ConsentRecord Create(string gameId, ConsentBasis basis)
        {
            if (string.IsNullOrEmpty(gameId))
                throw new ArgumentException("GameId must not be null or empty.", nameof(gameId));
            return new ConsentRecord(gameId, basis, false);
        }

        public static ConsentRecord Revoke(ConsentRecord record)
        {
            return new ConsentRecord(record.GameId, record.Basis, true);
        }
    }

    public readonly struct ConsentPolicy
    {
        public bool RequireExplicitConsent { get; }
        public bool AllowChildSafeAnalytics { get; }
        public string PolicyDescription { get; }

        private ConsentPolicy(bool requireExplicitConsent, bool allowChildSafeAnalytics, string policyDescription)
        {
            RequireExplicitConsent = requireExplicitConsent;
            AllowChildSafeAnalytics = allowChildSafeAnalytics;
            PolicyDescription = policyDescription;
        }

        public static ConsentPolicy Safe(string desc)
        {
            if (string.IsNullOrEmpty(desc))
                throw new ArgumentException("PolicyDescription must not be null or empty.", nameof(desc));
            return new ConsentPolicy(true, false, desc);
        }

        public static readonly ConsentPolicy Default =
            Safe("No analytics until explicit consent; child profiles blocked");
    }

    public static class ConsentAnalyticsGate
    {
        public static bool CanCollectAnalytics(string gameId, ConsentRecord record, ConsentPolicy policy,
            out string ineligibilityReason)
        {
            // Guard 1 (FIRST): Wonder_Pop is structurally prohibited (Ordinal)
            if (string.Equals(gameId, "Wonder_Pop", StringComparison.Ordinal))
            {
                ineligibilityReason = "WonderPopAnalyticsProhibited";
                return false;
            }

            // Guard 2: revoked consent
            if (record.IsRevoked)
            {
                ineligibilityReason = "ConsentRevoked";
                return false;
            }

            // Guard 3: policy requires explicit consent but basis is not ExplicitConsent
            if (policy.RequireExplicitConsent && record.Basis != ConsentBasis.ExplicitConsent)
            {
                ineligibilityReason = "ExplicitConsentRequired";
                return false;
            }

            // Guard 4: policy disallows ChildSafe analytics and basis is ChildSafe
            if (!policy.AllowChildSafeAnalytics && record.Basis == ConsentBasis.ChildSafe)
            {
                ineligibilityReason = "ChildSafeAnalyticsDisabled";
                return false;
            }

            ineligibilityReason = string.Empty;
            return true;
        }

        public static bool RequiresExplicitConsent(ConsentPolicy policy)
            => policy.RequireExplicitConsent;

        public static bool IsConsentActive(ConsentRecord record)
            => record.IsAnalyticsAllowed;
    }

    public sealed class ConsentAuditLog
    {
        private readonly List<string> _entries = new List<string>();

        public int Count => _entries.Count;

        public void Record(string gameId, string action)
        {
            if (string.IsNullOrEmpty(gameId))
                throw new ArgumentException("gameId must not be null or empty.", nameof(gameId));
            if (string.IsNullOrEmpty(action))
                throw new ArgumentException("action must not be null or empty.", nameof(action));
            _entries.Add($"{gameId}:{action}");
        }

        public bool HasEntry(string gameId)
        {
            if (string.IsNullOrEmpty(gameId))
                return false;
            foreach (var entry in _entries)
            {
                if (entry.StartsWith(gameId + ":", StringComparison.Ordinal))
                    return true;
            }
            return false;
        }

        public void Clear() => _entries.Clear();
    }
}
