using System;
using System.Collections.Generic;

namespace Match3.Engine.Runtime.Commerce
{
    public readonly struct AdFrequencyPolicy
    {
        public int MaxImpressionsPerSession { get; }
        public int MinIntervalSeconds { get; }
        public int AppOpenGraceSeconds { get; }

        public AdFrequencyPolicy(int maxImpressionsPerSession, int minIntervalSeconds, int appOpenGraceSeconds)
        {
            if (maxImpressionsPerSession < 1)
                throw new ArgumentOutOfRangeException(nameof(maxImpressionsPerSession), "Must be at least 1.");
            if (minIntervalSeconds < 0)
                throw new ArgumentOutOfRangeException(nameof(minIntervalSeconds), "Must be >= 0.");
            if (appOpenGraceSeconds < 0)
                throw new ArgumentOutOfRangeException(nameof(appOpenGraceSeconds), "Must be >= 0.");

            MaxImpressionsPerSession = maxImpressionsPerSession;
            MinIntervalSeconds = minIntervalSeconds;
            AppOpenGraceSeconds = appOpenGraceSeconds;
        }

        public static AdFrequencyPolicy Default { get; } =
            new AdFrequencyPolicy(5, 120, 30);
    }

    public sealed class AdSessionTracker
    {
        private readonly AdFrequencyPolicy _policy;

        public int ImpressionsThisSession { get; private set; }
        public DateTime? LastImpressionUtc { get; private set; }

        public AdSessionTracker(AdFrequencyPolicy policy)
        {
            _policy = policy;
        }

        public void RecordImpression(DateTime utc)
        {
            ImpressionsThisSession++;
            LastImpressionUtc = utc;
        }

        public void ResetSession()
        {
            ImpressionsThisSession = 0;
            LastImpressionUtc = null;
        }

        public bool IsAdAllowed(DateTime nowUtc, DateTime sessionStartUtc)
        {
            if (ImpressionsThisSession >= _policy.MaxImpressionsPerSession)
                return false;
            if ((nowUtc - sessionStartUtc).TotalSeconds < _policy.AppOpenGraceSeconds)
                return false;
            if (LastImpressionUtc != null &&
                (nowUtc - LastImpressionUtc.Value).TotalSeconds < _policy.MinIntervalSeconds)
                return false;
            return true;
        }
    }

    public sealed class AdPlacementBlocklist
    {
        private readonly HashSet<string> _blocked = new HashSet<string>(StringComparer.Ordinal);

        public void Block(string placementId)
        {
            if (string.IsNullOrEmpty(placementId))
                throw new ArgumentException("placementId must not be null or empty.", nameof(placementId));
            _blocked.Add(placementId);
        }

        public bool IsBlocked(string placementId) =>
            placementId != null && _blocked.Contains(placementId);
    }

    public static class AdFrequencyGate
    {
        public static AdEligibilityResult Check(
            AdSessionTracker tracker,
            string placementId,
            AdPlacementBlocklist blocklist,
            DateTime nowUtc,
            DateTime sessionStartUtc)
        {
            if (blocklist.IsBlocked(placementId))
                return AdEligibilityResult.Ineligible("PlacementBlocked");
            if (!tracker.IsAdAllowed(nowUtc, sessionStartUtc))
                return AdEligibilityResult.Ineligible("FrequencyCapReached");
            return AdEligibilityResult.Eligible();
        }
    }
}
