using System;
using System.Collections.Generic;

namespace Match3.Engine.Runtime
{
    // Shared semantic projections. Counts, eligibility and deadline must be
    // supplied from VERIFIED existing Engine ledgers, never authored by UI.
    public enum EngagementKind
    {
        NextLevel = 0, WinStreak = 1, Journey = 2,
        Gift = 3, Collection = 4, WeeklyRecord = 5, CatchUp = 6
    }

    public sealed class VerifiedEngagementFact
    {
        public string GameId { get; }
        public string SourceReceiptId { get; }
        public EngagementKind Kind { get; }
        public bool Earned { get; }
        public bool Claimed { get; }
        public int VerifiedProgress { get; }
        public int VerifiedTarget { get; }
        public long? VerifiedExpiryUnixSeconds { get; }

        public VerifiedEngagementFact(string gameId, string sourceReceiptId,
            EngagementKind kind, bool earned, bool claimed,
            int verifiedProgress, int verifiedTarget,
            long? verifiedExpiryUnixSeconds = null)
        {
            if (!GameProfileCatalog.IsKnownId(gameId))
                throw new ArgumentException("Unknown game profile.", nameof(gameId));
            if (string.IsNullOrWhiteSpace(sourceReceiptId) ||
                sourceReceiptId.Length > 128)
                throw new ArgumentException(
                    "Fact must cite an authoritative source receipt.",
                    nameof(sourceReceiptId));
            if (!Enum.IsDefined(typeof(EngagementKind), kind))
                throw new ArgumentOutOfRangeException(nameof(kind));
            if (claimed && !earned)
                throw new InvalidOperationException(
                    "An unearned benefit cannot be reported claimed.");
            if (verifiedTarget < 1 || verifiedTarget > 1000000 ||
                verifiedProgress < 0 || verifiedProgress > verifiedTarget)
                throw new ArgumentOutOfRangeException(nameof(verifiedProgress));
            if (verifiedExpiryUnixSeconds.HasValue &&
                verifiedExpiryUnixSeconds.Value < 0)
                throw new ArgumentOutOfRangeException(
                    nameof(verifiedExpiryUnixSeconds));
            GameId = gameId;
            SourceReceiptId = sourceReceiptId;
            Kind = kind;
            Earned = earned;
            Claimed = claimed;
            VerifiedProgress = verifiedProgress;
            VerifiedTarget = verifiedTarget;
            VerifiedExpiryUnixSeconds = verifiedExpiryUnixSeconds;
        }
    }

    public sealed class EngagementPreview
    {
        public EngagementKind Kind { get; }
        public RewardPanelState State { get; }
        public string SourceReceiptId { get; }
        public int Progress { get; }
        public int Target { get; }
        public bool CanClaim { get; }
        public bool ShowCountdown { get; }
        public long RemainingSeconds { get; }

        internal EngagementPreview(VerifiedEngagementFact source,
            RewardPresentationPlan reward)
        {
            Kind = source.Kind;
            State = reward.State;
            SourceReceiptId = source.SourceReceiptId;
            Progress = source.VerifiedProgress;
            Target = source.VerifiedTarget;
            // Non-claim displays must never manufacture a new money button.
            CanClaim = (source.Kind == EngagementKind.Gift ||
                        source.Kind == EngagementKind.Collection ||
                        source.Kind == EngagementKind.CatchUp)
                        && reward.CanClaim;
            ShowCountdown = reward.ShowCountdown;
            RemainingSeconds = reward.RemainingSeconds;
        }
    }

    public static class HonestEngagementProjector
    {
        public static EngagementPreview Project(GameProfile profile,
            VerifiedEngagementFact fact, long nowUnixSeconds,
            bool sourceIsAuthoritativelyVerified)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            if (fact == null) throw new ArgumentNullException(nameof(fact));
            if (profile.Id != fact.GameId)
                throw new InvalidOperationException(
                    "Cross-game engagement display blocked.");
            if (!sourceIsAuthoritativelyVerified)
                throw new InvalidOperationException(
                    "Unverified engagement source cannot be displayed as earned.");
            if (fact.Kind == EngagementKind.CatchUp &&
                !profile.IsEnabled("catch_up",
                    EngineCompatibility.MinimumCapability))
                throw new InvalidOperationException(
                    "Catch-up display requires an approved feature gate.");
            // Prevent fake pressure: the only countdown comes from a verified
            // and bounded upstream fact and disappears on claim/expiry.
            var reward = HonestRewardProjection.Project(fact.Earned,
                fact.Claimed, nowUnixSeconds, fact.VerifiedExpiryUnixSeconds);
            return new EngagementPreview(fact, reward);
        }
    }
}
