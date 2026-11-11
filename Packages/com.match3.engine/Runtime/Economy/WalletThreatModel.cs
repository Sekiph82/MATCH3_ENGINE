// Match3.Engine.Runtime.Economy — Wallet Threat Model
// stdlib only: System, System.Collections.Generic
using System;
using System.Collections.Generic;

namespace Match3.Engine.Runtime.Economy
{
    public enum ThreatVector
    {
        TamperedClient,
        OfflineReplay,
        ClockManipulation,
        ForgedPremiumCurrency,
        StolenReceipt,
        SharedDevice,
        TransactionConflict
    }

    public readonly struct ThreatAssessment
    {
        public ThreatVector Vector       { get; }
        public string       Severity     { get; }
        public bool         MitigationRequired { get; }
        public string       Description  { get; }

        private ThreatAssessment(ThreatVector vector, string severity, bool mitigationRequired, string description)
        {
            if (string.IsNullOrEmpty(severity))    throw new ArgumentException("Severity required.",    nameof(severity));
            if (string.IsNullOrEmpty(description)) throw new ArgumentException("Description required.", nameof(description));
            Vector             = vector;
            Severity           = severity;
            MitigationRequired = mitigationRequired;
            Description        = description;
        }

        public static ThreatAssessment Critical(ThreatVector vector, string description)
            => new ThreatAssessment(vector, "Critical", true, description);

        public static ThreatAssessment High(ThreatVector vector, string description)
            => new ThreatAssessment(vector, "High", true, description);
    }

    public readonly struct WalletThreatPolicy
    {
        public bool   ServerAuthoritative      { get; }
        public bool   RequireReceiptValidation { get; }
        public string PolicyDescription        { get; }

        private WalletThreatPolicy(bool serverAuthoritative, bool requireReceiptValidation, string policyDescription)
        {
            if (string.IsNullOrEmpty(policyDescription))
                throw new ArgumentException("PolicyDescription required.", nameof(policyDescription));
            ServerAuthoritative      = serverAuthoritative;
            RequireReceiptValidation = requireReceiptValidation;
            PolicyDescription        = policyDescription;
        }

        public static WalletThreatPolicy Hardened(string desc)
            => new WalletThreatPolicy(true, true, desc);

        public static readonly WalletThreatPolicy Default = Hardened(
            "Server-authoritative wallet with receipt validation; all threat vectors require server-side mitigation");
    }

    public static class WalletThreatGate
    {
        public static bool IsMitigationRequired(ThreatVector vector, WalletThreatPolicy policy)
            => true; // ponytail: all vectors unconditionally require mitigation per threat model contract

        public static (bool IsEligible, string Reason) CanBypassServerAuth(string gameId, WalletThreatPolicy policy)
        {
            // Guard 1 FIRST (CRITICAL): Wonder_Pop is never eligible for bypass
            if (string.Equals(gameId, "Wonder_Pop", StringComparison.Ordinal))
                return (false, "WonderPopBypassProhibited");

            // Guard 2: server-authoritative policy blocks bypass
            if (policy.ServerAuthoritative)
                return (false, "ServerAuthoritativeEnforced");

            return (true, "BypassAllowed");
        }

        public static IReadOnlyList<ThreatAssessment> GetKnownThreats()
            => new List<ThreatAssessment>
            {
                ThreatAssessment.Critical(ThreatVector.TamperedClient,         "Client-side state tampered to grant unearned currency or items"),
                ThreatAssessment.Critical(ThreatVector.OfflineReplay,          "Offline transactions replayed to duplicate rewards on reconnect"),
                ThreatAssessment.Critical(ThreatVector.ClockManipulation,      "Device clock altered to trigger time-gated rewards prematurely"),
                ThreatAssessment.Critical(ThreatVector.ForgedPremiumCurrency,  "Premium currency injected without valid payment receipt"),
                ThreatAssessment.Critical(ThreatVector.StolenReceipt,          "Valid purchase receipt replayed on a different account"),
                ThreatAssessment.High(    ThreatVector.SharedDevice,           "Shared-device session allows one player to consume another's wallet"),
                ThreatAssessment.High(    ThreatVector.TransactionConflict,    "Concurrent transactions produce inconsistent wallet balance")
            };
    }
}
