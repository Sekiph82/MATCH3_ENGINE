// ENG-M18-007 AttributionAdapterGate stdlib contract
// stdlib only: System, System.Collections.Generic
using System;
using System.Collections.Generic;

namespace Match3.Engine.Runtime.Analytics
{
    public enum AttributionChannel
    {
        Organic,
        PaidSearch,
        PaidSocial,
        CrossPromo
    }

    public readonly struct AttributionToken
    {
        public string GameId { get; }
        public AttributionChannel Channel { get; }
        public bool TrackingAllowed { get; }

        private AttributionToken(string gameId, AttributionChannel channel, bool trackingAllowed)
        {
            GameId = gameId;
            Channel = channel;
            TrackingAllowed = trackingAllowed;
        }

        public static AttributionToken Create(string gameId, AttributionChannel channel, bool trackingAllowed)
        {
            if (string.IsNullOrEmpty(gameId))
                throw new ArgumentException("GameId must not be null or empty.", nameof(gameId));
            return new AttributionToken(gameId, channel, trackingAllowed);
        }

        public bool IsOrganic => Channel == AttributionChannel.Organic;
        public bool IsPaidChannel => Channel != AttributionChannel.Organic;
    }

    public readonly struct AttributionPolicy
    {
        public bool AllowCrossAppTracking { get; }
        public bool RequireExplicitConsent { get; }
        public string PolicyDescription { get; }

        private AttributionPolicy(bool allowCrossApp, bool requireConsent, string description)
        {
            AllowCrossAppTracking = allowCrossApp;
            RequireExplicitConsent = requireConsent;
            PolicyDescription = description;
        }

        public static AttributionPolicy Safe(string desc)
        {
            if (string.IsNullOrEmpty(desc))
                throw new ArgumentException("PolicyDescription must not be null or empty.", nameof(desc));
            return new AttributionPolicy(false, true, desc);
        }

        public static readonly AttributionPolicy Default =
            Safe("Cross-app tracking disabled; explicit consent required");
    }

    public static class AttributionAdapterGate
    {
        public static (bool eligible, string reason) CanRecordAttribution(
            string gameId, AttributionToken token, AttributionPolicy policy)
        {
            // Guard 1 (FIRST): Wonder_Pop is always ineligible (Ordinal)
            if (string.Equals(gameId, "Wonder_Pop", StringComparison.Ordinal))
                return (false, "WonderPopAttributionProhibited");

            // Guard 2: tracking not allowed on the token
            if (!token.TrackingAllowed)
                return (false, "TrackingNotAllowed");

            // Guard 3: paid channel blocked by cross-app tracking policy
            if (token.IsPaidChannel && !policy.AllowCrossAppTracking)
                return (false, "CrossAppTrackingDisabled");

            return (true, string.Empty);
        }

        public static bool IsOrganicInstall(AttributionToken token) => token.IsOrganic;

        public static bool RequiresConsent(AttributionPolicy policy) => policy.RequireExplicitConsent;
    }

    public sealed class AttributionLog
    {
        private readonly List<(string gameId, AttributionChannel channel)> _entries =
            new List<(string, AttributionChannel)>();

        public int Count => _entries.Count;

        public void Record(string gameId, AttributionChannel channel)
        {
            if (string.IsNullOrEmpty(gameId))
                throw new ArgumentException("GameId must not be null or empty.", nameof(gameId));
            _entries.Add((gameId, channel));
        }

        public bool HasEntry(string gameId) =>
            _entries.Exists(e => string.Equals(e.gameId, gameId, StringComparison.Ordinal));

        public void Clear() => _entries.Clear();
    }
}
