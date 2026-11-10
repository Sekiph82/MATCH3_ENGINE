using System;
using System.Collections.Generic;
using System.Linq;

namespace Match3.Engine.Runtime.Commerce
{
    public enum RewardPlacementKind
    {
        ExtraMoves,
        BonusCoins,
        OneBooster,
        ExtraHeart,
        PostWinBonus
    }

    public readonly struct RewardPlacement
    {
        public string PlacementId { get; }
        public RewardPlacementKind Kind { get; }
        public string GameId { get; }
        public int RewardAmount { get; }
        public bool IsEnabled { get; }

        public RewardPlacement(string placementId, RewardPlacementKind kind, string gameId, int rewardAmount, bool isEnabled)
        {
            if (string.IsNullOrEmpty(placementId))
                throw new ArgumentException("PlacementId must not be null or empty.", nameof(placementId));
            if (string.IsNullOrEmpty(gameId))
                throw new ArgumentException("GameId must not be null or empty.", nameof(gameId));
            if (rewardAmount <= 0)
                throw new ArgumentException("RewardAmount must be positive.", nameof(rewardAmount));

            PlacementId = placementId;
            Kind = kind;
            GameId = gameId;
            RewardAmount = rewardAmount;
            // ponytail: Wonder_Pop check is a string compare — fine for a catalog that never hot-reloads
            IsEnabled = isEnabled && !string.Equals(gameId, "Wonder_Pop", StringComparison.Ordinal);
        }
    }

    public sealed class RewardPlacementCatalog
    {
        public IReadOnlyList<RewardPlacement> Placements { get; }

        public RewardPlacementCatalog(IEnumerable<RewardPlacement> placements)
        {
            if (placements == null)
                throw new ArgumentNullException(nameof(placements));

            var list = placements.ToList();
            var ids = new HashSet<string>();
            foreach (var p in list)
            {
                if (!ids.Add(p.PlacementId))
                    throw new ArgumentException($"Duplicate PlacementId: '{p.PlacementId}'.", nameof(placements));
            }

            Placements = list.AsReadOnly();
        }

        public RewardPlacement? FindPlacement(string placementId)
        {
            foreach (var p in Placements)
                if (p.PlacementId == placementId)
                    return p;
            return null;
        }

        public IReadOnlyList<RewardPlacement> GetEnabledPlacementsForGame(string gameId)
        {
            var result = new List<RewardPlacement>();
            foreach (var p in Placements)
                if (p.IsEnabled && p.GameId == gameId)
                    result.Add(p);
            return result.AsReadOnly();
        }
    }
}
