using System;
using System.Collections.Generic;
using NUnit.Framework;
using Match3.Engine.Runtime.Commerce;

namespace Match3.Engine.Tests.Editor
{
    [TestFixture]
    public class RewardPlacementCatalogTests
    {
        [Test]
        public void WonderPop_Placement_IsAlwaysDisabled()
        {
            var p = new RewardPlacement("wp_extra_moves", RewardPlacementKind.ExtraMoves, "Wonder_Pop", 5, isEnabled: true);
            Assert.IsFalse(p.IsEnabled, "Wonder_Pop placement must always be disabled regardless of isEnabled arg");
        }

        [Test]
        public void NonWonderPop_Placement_CanBeEnabled()
        {
            var p = new RewardPlacement("cr_extra_moves", RewardPlacementKind.ExtraMoves, "Coral_Rescue", 5, isEnabled: true);
            Assert.IsTrue(p.IsEnabled);
        }

        [Test]
        public void Placement_RewardAmount_MustBePositive()
        {
            Assert.Throws<ArgumentException>(() =>
                new RewardPlacement("cr_zero", RewardPlacementKind.BonusCoins, "Coral_Rescue", 0, true));
            Assert.Throws<ArgumentException>(() =>
                new RewardPlacement("cr_neg", RewardPlacementKind.BonusCoins, "Coral_Rescue", -1, true));
        }

        [Test]
        public void Placement_PlacementId_MustNotBeEmpty()
        {
            Assert.Throws<ArgumentException>(() =>
                new RewardPlacement("", RewardPlacementKind.ExtraMoves, "Coral_Rescue", 5, true));
            Assert.Throws<ArgumentException>(() =>
                new RewardPlacement(null, RewardPlacementKind.ExtraMoves, "Coral_Rescue", 5, true));
        }

        [Test]
        public void Catalog_FindPlacement_ReturnsCorrectEntry()
        {
            var p = new RewardPlacement("cr_booster", RewardPlacementKind.OneBooster, "Coral_Rescue", 1, true);
            var catalog = new RewardPlacementCatalog(new[] { p });
            var found = catalog.FindPlacement("cr_booster");
            Assert.IsTrue(found.HasValue);
            Assert.AreEqual(RewardPlacementKind.OneBooster, found.Value.Kind);
            Assert.AreEqual(1, found.Value.RewardAmount);
        }

        [Test]
        public void Catalog_FindPlacement_UnknownId_ReturnsNull()
        {
            var p = new RewardPlacement("cr_booster", RewardPlacementKind.OneBooster, "Coral_Rescue", 1, true);
            var catalog = new RewardPlacementCatalog(new[] { p });
            Assert.IsNull(catalog.FindPlacement("does_not_exist"));
        }

        [Test]
        public void Catalog_GetEnabledForGame_FiltersCorrectly()
        {
            var placements = new[]
            {
                new RewardPlacement("cr_moves", RewardPlacementKind.ExtraMoves, "Coral_Rescue", 5, true),
                new RewardPlacement("cr_coins", RewardPlacementKind.BonusCoins, "Coral_Rescue", 100, false),
                new RewardPlacement("di_moves", RewardPlacementKind.ExtraMoves, "Dino_Islands", 5, true),
            };
            var catalog = new RewardPlacementCatalog(placements);
            IReadOnlyList<RewardPlacement> enabled = catalog.GetEnabledPlacementsForGame("Coral_Rescue");
            Assert.AreEqual(1, enabled.Count);
            Assert.AreEqual("cr_moves", enabled[0].PlacementId);
        }

        [Test]
        public void Catalog_DuplicatePlacementId_Throws()
        {
            var placements = new[]
            {
                new RewardPlacement("dup_id", RewardPlacementKind.ExtraHeart, "Coral_Rescue", 1, true),
                new RewardPlacement("dup_id", RewardPlacementKind.PostWinBonus, "Dino_Islands", 50, true),
            };
            Assert.Throws<ArgumentException>(() => new RewardPlacementCatalog(placements));
        }

        [Test]
        public void Catalog_NullInput_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => new RewardPlacementCatalog(null));
        }
    }
}
