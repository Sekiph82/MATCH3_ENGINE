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

        // IsAnalyticsAllowed: not revoked and there is a lawful analytics basis.
        // NoneRequired carries no basis and ChildSafe is never an analytics basis.
        public bool IsAnalyticsAllowed =>
            !IsRevoked && Basis != ConsentBasis.NoneRequired && Basis != ConsentBasis.ChildSafe;

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
        [Obsolete("Ignored: ChildSafe-basis analytics are always blocked, under every policy.")]
        public bool AllowChildSafeAnalytics => false;
        public string PolicyDescription { get; }

        private ConsentPolicy(bool requireExplicitConsent, string policyDescription)
        {
            RequireExplicitConsent = requireExplicitConsent;
            PolicyDescription = policyDescription;
        }

        public static ConsentPolicy Safe(string desc)
        {
            if (string.IsNullOrEmpty(desc))
                throw new ArgumentException("PolicyDescription must not be null or empty.", nameof(desc));
            return new ConsentPolicy(true, desc);
        }

        public static readonly ConsentPolicy Default =
            Safe("No analytics until explicit consent; child profiles blocked");
    }

    public static class ConsentAnalyticsGate
    {
        public static bool CanCollectAnalytics(string gameId, ConsentRecord record, ConsentPolicy policy,
            out string ineligibilityReason)
        {
            if (policy.PolicyDescription == null) policy = ConsentPolicy.Default;

            if (string.IsNullOrEmpty(gameId))
            {
                ineligibilityReason = "GameIdMissing";
                return false;
            }
            if (!GameProfileCatalog.IsKnownId(gameId))
            {
                ineligibilityReason = "UnknownGame";
                return false;
            }

            if (!string.Equals(gameId, record.GameId, StringComparison.Ordinal))
            {
                ineligibilityReason = "GameIdMismatch";
                return false;
            }

            // Wonder_Pop is structurally prohibited (Ordinal)
            if (string.Equals(gameId, "Wonder_Pop", StringComparison.Ordinal))
            {
                ineligibilityReason = "WonderPopAnalyticsProhibited";
                return false;
            }

            // Unconditional: revoked consent
            if (record.IsRevoked)
            {
                ineligibilityReason = "ConsentRevoked";
                return false;
            }

            // Unconditional: child-safe basis is never an analytics basis, under any policy
            if (record.Basis == ConsentBasis.ChildSafe)
            {
                ineligibilityReason = "ChildSafeAnalyticsDisabled";
                return false;
            }

            // Unconditional: NoneRequired carries no lawful basis (!IsAnalyticsAllowed).
            // Policy may only add restriction: RequireExplicitConsent also blocks LegitimateInterest.
            if (!record.IsAnalyticsAllowed ||
                (policy.RequireExplicitConsent && record.Basis != ConsentBasis.ExplicitConsent))
            {
                ineligibilityReason = "ExplicitConsentRequired";
                return false;
            }

            ineligibilityReason = string.Empty;
            return true;
        }

        public static bool RequiresExplicitConsent(ConsentPolicy policy)
            => (policy.PolicyDescription == null ? ConsentPolicy.Default : policy).RequireExplicitConsent;

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
