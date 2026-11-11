using System;
using System.Collections.Generic;
using Match3.Engine.Runtime.LiveOps;

namespace Match3.Engine.Runtime.Economy
{
    /// <summary>
    /// ENG-M16-011 integration contract for ENG-M08 <see cref="UnifiedRewardPlanner"/> grants (M08 code unchanged).
    /// A planner <see cref="RewardGrantRequest"/> becomes a <see cref="RewardLedgerRequest"/> with
    /// source = <see cref="MapSource"/>(SourceKind), sourceInstanceId = SourceId and amounts = mapped positive deltas.
    /// The same event therefore derives the same <see cref="RewardLedgerKey"/> whether it arrives through this adapter
    /// or a direct ledger grant, and is credited once. Fails closed: unmapped source kinds, unmapped resources and
    /// debits are rejected; windowed sources (Daily) need expiresUtc and a server-anchored anchor.
    /// Not yet migrated (follow-up): Gift, Collection, Event, Replay, Recovery, Task, Achievement kinds and
    /// non-currency resources such as specific booster ids.
    /// </summary>
    public static class UnifiedRewardLedgerAdapter
    {
        public const string UnmappedRewardSource = "UnmappedRewardSource";
        public const string UnmappedResource = "UnmappedResource";
        public const string DebitNotSupported = "DebitNotSupported";

        private static readonly Dictionary<string, string> M08ResourceAliases = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["coins"] = nameof(CurrencyType.Coins),
            ["hearts"] = nameof(CurrencyType.Hearts)
        };

        public static string MapSource(RewardSourceKind kind)
        {
            switch (kind)
            {
                case RewardSourceKind.FirstClear: return RewardSources.FirstClear;
                case RewardSourceKind.Streak: return RewardSources.Streak;
                case RewardSourceKind.Chest: return RewardSources.Chest;
                case RewardSourceKind.Pack: return RewardSources.EarnedPack;
                case RewardSourceKind.Purchase: return RewardSources.Purchase;
                case RewardSourceKind.RewardedVerified: return RewardSources.AdServerSideVerified;
                case RewardSourceKind.Daily: return RewardSources.Daily;
                default: return null;
            }
        }

        public static RewardLedgerGrantResult TryGrant(RewardLedger ledger, RewardGrantRequest m08Request,
            TrustedUtcAnchor anchor, DateTime? expiresUtc = null)
        {
            if (ledger == null) throw new ArgumentNullException(nameof(ledger));
            if (m08Request == null) throw new ArgumentNullException(nameof(m08Request));
            var source = MapSource(m08Request.SourceKind);
            if (source == null) return RewardLedgerGrantResult.Rejected(UnmappedRewardSource);
            var amounts = new Dictionary<string, long>(StringComparer.Ordinal);
            foreach (var d in m08Request.Deltas)
            {
                if (d.Delta < 0) return RewardLedgerGrantResult.Rejected(DebitNotSupported);
                var asset = M08ResourceAliases.TryGetValue(d.ResourceId, out var a) ? a
                    : RewardLedger.IsKnownAsset(d.ResourceId) ? d.ResourceId : null;
                if (asset == null || amounts.ContainsKey(asset)) return RewardLedgerGrantResult.Rejected(UnmappedResource);
                amounts[asset] = d.Delta;
            }
            return ledger.TryGrant(new RewardLedgerRequest(m08Request.GameId, ledger.PlayerId, source, m08Request.SourceId,
                amounts, anchor.UtcNow, expiresUtc), anchor);
        }
    }
}
