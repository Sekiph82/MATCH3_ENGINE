using System;
using System.Collections.Generic;

namespace Match3.Engine.Runtime.Commerce
{
    // ENG-M13-010: Five-title commercial architecture audit.
    // Wonder Pop must never receive real ads/IAP/analytics.
    // Other four titles may receive real services only via the authorized interfaces.
    public static class CommerceArchitectureAudit
    {
        private const string WonderPopId = "Wonder_Pop";

        public static AuditResult RunAudit(
            string gameId,
            IAdsService ads,
            IIapService iap,
            IAnalyticsSink analytics)
        {
            if (string.IsNullOrWhiteSpace(gameId))
                throw new ArgumentException("gameId is required.", nameof(gameId));

            if (!GameProfileCatalog.IsKnownId(gameId))
                throw new ArgumentException("Unknown game identifier: " + gameId, nameof(gameId));

            if (StringComparer.Ordinal.Equals(gameId, WonderPopId))
            {
                var findings = new List<string>();
                if (ads != null)
                    findings.Add("Wonder Pop must not receive an IAdsService (ads must be null/disabled).");
                if (iap != null)
                    findings.Add("Wonder Pop must not receive an IIapService (iap must be null/disabled).");
                if (analytics != null)
                    findings.Add("Wonder Pop must not receive an IAnalyticsSink (analytics must be null/disabled).");
                return findings.Count == 0 ? AuditResult.Ok() : AuditResult.Fail(findings.ToArray());
            }

            // For the other four titles services may be real, but only when they
            // implement the authorized interfaces — which is structurally guaranteed
            // by the type system; no additional runtime check needed here.
            // ponytail: structural type check is sufficient; add consent/gate
            // verification here if per-title policy tightens.
            return AuditResult.Ok();
        }

        public static bool IsWonderPopExcluded(IAdsService ads, IIapService iap)
        {
            return ads == null && iap == null;
        }
    }

    public sealed class AuditResult
    {
        public bool Passed { get; }
        public string[] Findings { get; }

        private AuditResult(bool passed, string[] findings)
        {
            Passed = passed;
            Findings = findings;
        }

        public static AuditResult Ok() => new AuditResult(true, null);

        public static AuditResult Fail(string[] findings)
        {
            if (findings == null || findings.Length == 0)
                throw new ArgumentException("Fail requires at least one finding.", nameof(findings));
            return new AuditResult(false, findings);
        }
    }
}
