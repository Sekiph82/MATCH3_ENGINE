using System;
using System.Collections.Generic;

namespace Match3.Engine.Runtime.Privacy
{
    public enum CmpDisclosureKind
    {
        GdprTcf,
        UmpGoogle,
        CcpaUs,
        CoppaChild,
        AppleAtt,
        CustomRegional
    }

    public readonly struct VendorDisclosureRecord
    {
        public string GameId { get; }
        public CmpDisclosureKind DisclosureKind { get; }
        public bool HasUserConsent { get; }
        public bool IsRegionRequired { get; }

        private VendorDisclosureRecord(string gameId, CmpDisclosureKind disclosureKind, bool hasUserConsent, bool isRegionRequired)
        {
            GameId = gameId;
            DisclosureKind = disclosureKind;
            HasUserConsent = hasUserConsent;
            IsRegionRequired = isRegionRequired;
        }

        public static VendorDisclosureRecord Create(string gameId, CmpDisclosureKind disclosureKind, bool hasUserConsent, bool isRegionRequired)
        {
            if (string.IsNullOrEmpty(gameId))
                throw new ArgumentException("gameId cannot be null or empty.", nameof(gameId));
            return new VendorDisclosureRecord(gameId, disclosureKind, hasUserConsent, isRegionRequired);
        }

        public bool IsDisclosureComplete => HasUserConsent || !IsRegionRequired;
        public bool RequiresDisclosure => IsRegionRequired;
    }

    public readonly struct CmpAdPolicy
    {
        public bool AllowAdServing { get; }
        public bool RequireVendorConsent { get; }
        public string PolicyDescription { get; }

        private CmpAdPolicy(bool allowAdServing, bool requireVendorConsent, string policyDescription)
        {
            AllowAdServing = allowAdServing;
            RequireVendorConsent = requireVendorConsent;
            PolicyDescription = policyDescription;
        }

        public static CmpAdPolicy Safe(string desc)
        {
            if (string.IsNullOrEmpty(desc))
                throw new ArgumentException("PolicyDescription cannot be null or empty.", nameof(desc));
            return new CmpAdPolicy(false, true, desc);
        }

        public static readonly CmpAdPolicy Default = Safe("Default CMP policy - no ad serving, require vendor consent");
    }

    public static class CmpAdVendorDisclosureGate
    {
        public static bool CanServeAds(string gameId, VendorDisclosureRecord record, CmpAdPolicy policy, out string reason)
        {
            if (policy.PolicyDescription == null) policy = CmpAdPolicy.Default;

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

            if (string.Equals(gameId, "Wonder_Pop", StringComparison.Ordinal))
            {
                reason = "WonderPopAdsProhibited";
                return false;
            }

            if (!policy.AllowAdServing)
            {
                reason = "AdServingDisabled";
                return false;
            }

            if (policy.RequireVendorConsent && !record.HasUserConsent)
            {
                reason = "VendorConsentRequired";
                return false;
            }

            reason = string.Empty;
            return true;
        }

        public static bool IsDisclosureComplete(VendorDisclosureRecord record) => record.IsDisclosureComplete;
        public static bool RequiresDisclosure(VendorDisclosureRecord record) => record.RequiresDisclosure;
    }

    public sealed class VendorDisclosureLog
    {
        private readonly List<string> _entries = new List<string>();

        public void Record(string gameId, string disclosureKind)
        {
            if (string.IsNullOrEmpty(gameId))
                throw new ArgumentException("gameId cannot be null or empty.", nameof(gameId));
            if (string.IsNullOrEmpty(disclosureKind))
                throw new ArgumentException("disclosureKind cannot be null or empty.", nameof(disclosureKind));
            _entries.Add(gameId + ":" + disclosureKind);
        }

        public int Count => _entries.Count;
        public void Clear() => _entries.Clear();
        public bool HasEntry(string gameId) => _entries.Exists(e => e.StartsWith(gameId + ":", StringComparison.Ordinal));
    }
}
