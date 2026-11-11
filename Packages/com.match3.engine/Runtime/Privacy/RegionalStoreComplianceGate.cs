using System;
using System.Collections.Generic;

namespace Match3.Engine.Runtime.Privacy
{
    public enum StoreRegion
    {
        GlobalDefault,
        EuropeanUnion,
        UnitedStates,
        China,
        SouthKorea,
        Brazil
    }

    public readonly struct ComplianceRecord
    {
        public string GameId { get; }
        public StoreRegion Region { get; }
        public bool HasPolicyDeclaration { get; }
        public bool RequiresLegalReview { get; }

        private ComplianceRecord(string gameId, StoreRegion region, bool hasPolicyDeclaration, bool requiresLegalReview)
        {
            GameId = gameId;
            Region = region;
            HasPolicyDeclaration = hasPolicyDeclaration;
            RequiresLegalReview = requiresLegalReview;
        }

        public static ComplianceRecord Create(string gameId, StoreRegion region, bool hasPolicyDeclaration, bool requiresLegalReview)
        {
            if (string.IsNullOrEmpty(gameId))
                throw new ArgumentException("gameId must not be null or empty.", nameof(gameId));
            return new ComplianceRecord(gameId, region, hasPolicyDeclaration, requiresLegalReview);
        }

        public bool IsCompliant => HasPolicyDeclaration && !RequiresLegalReview;
        public bool NeedsDeclaration => !HasPolicyDeclaration;
    }

    public readonly struct RegionalCompliancePolicy
    {
        public bool RequirePolicyDeclaration { get; }
        public bool BlockWithoutDeclaration { get; }
        public string PolicyDescription { get; }

        private RegionalCompliancePolicy(bool requirePolicyDeclaration, bool blockWithoutDeclaration, string policyDescription)
        {
            if (string.IsNullOrEmpty(policyDescription))
                throw new ArgumentException("PolicyDescription must not be null or empty.", nameof(policyDescription));
            RequirePolicyDeclaration = requirePolicyDeclaration;
            BlockWithoutDeclaration = blockWithoutDeclaration;
            PolicyDescription = policyDescription;
        }

        public static RegionalCompliancePolicy Safe(string desc)
        {
            return new RegionalCompliancePolicy(true, true, desc);
        }

        public static readonly RegionalCompliancePolicy Default =
            Safe("Default regional compliance policy - require policy declaration, block without declaration");
    }

    public static class RegionalStoreComplianceGate
    {
        public static bool CanActivateForRegion(string gameId, ComplianceRecord record, RegionalCompliancePolicy policy, out string reason)
        {
            if (string.Equals(gameId, "Wonder_Pop", StringComparison.Ordinal) &&
                record.Region == StoreRegion.China &&
                !record.HasPolicyDeclaration)
            {
                reason = "WonderPopChinaDeclarationRequired";
                return false;
            }

            if (policy.BlockWithoutDeclaration && record.NeedsDeclaration)
            {
                reason = "PolicyDeclarationMissing";
                return false;
            }

            if (policy.RequirePolicyDeclaration && record.RequiresLegalReview && !record.HasPolicyDeclaration)
            {
                reason = "LegalReviewPending";
                return false;
            }

            reason = string.Empty;
            return true;
        }

        public static bool IsCompliant(ComplianceRecord record) => record.IsCompliant;

        public static bool NeedsDeclaration(ComplianceRecord record) => record.NeedsDeclaration;
    }

    public sealed class ComplianceLog
    {
        private readonly List<string> _entries = new List<string>();

        public void Record(string gameId, string regionName)
        {
            if (string.IsNullOrEmpty(gameId))
                throw new ArgumentException("gameId must not be null or empty.", nameof(gameId));
            _entries.Add(gameId + ":" + regionName);
        }

        public int Count => _entries.Count;

        public void Clear() => _entries.Clear();

        public bool HasEntry(string gameId) => _entries.Exists(e => e.StartsWith(gameId + ":"));
    }
}
