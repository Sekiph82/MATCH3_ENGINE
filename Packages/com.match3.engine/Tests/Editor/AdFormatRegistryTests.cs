using System;
using System.Collections.Generic;
using NUnit.Framework;
using Match3.Engine.Runtime.Commerce;

namespace Match3.Engine.Tests.Editor
{
    [TestFixture]
    public class AdFormatRegistryTests
    {
        // AdFormatConfig tests

        [Test]
        public void AdFormatConfig_WonderPop_AlwaysDisabled_EvenWhenEnabledFactory()
        {
            var cfg = AdFormatConfig.Enabled(AdFormatKind.Interstitial, "Wonder_Pop");
            Assert.IsFalse(cfg.IsEnabled);
        }

        [Test]
        public void AdFormatConfig_NonWonderPop_EnabledFactory_IsEnabled()
        {
            var cfg = AdFormatConfig.Enabled(AdFormatKind.Interstitial, "Coral_Rescue");
            Assert.IsTrue(cfg.IsEnabled);
        }

        [Test]
        public void AdFormatConfig_EmptyGameId_Throws()
        {
            Assert.Throws<ArgumentException>(() => AdFormatConfig.Enabled(AdFormatKind.Banner, ""));
        }

        // AdFormatRegistry tests

        [Test]
        public void AdFormatRegistry_IsEnabled_UnregisteredFormat_ReturnsFalse()
        {
            var reg = new AdFormatRegistry();
            Assert.IsFalse(reg.IsEnabled("Coral_Rescue", AdFormatKind.Banner));
        }

        [Test]
        public void AdFormatRegistry_Register_Overwrites()
        {
            var reg = new AdFormatRegistry();
            reg.Register(AdFormatConfig.Enabled(AdFormatKind.Interstitial, "Dino_Islands"));
            reg.Register(AdFormatConfig.Disabled(AdFormatKind.Interstitial, "Dino_Islands"));
            Assert.IsFalse(reg.IsEnabled("Dino_Islands", AdFormatKind.Interstitial));
        }

        [Test]
        public void AdFormatRegistry_IsEnabled_RegisteredEnabled_ReturnsTrue()
        {
            var reg = new AdFormatRegistry();
            reg.Register(AdFormatConfig.Enabled(AdFormatKind.Banner, "Garage_Legends"));
            Assert.IsTrue(reg.IsEnabled("Garage_Legends", AdFormatKind.Banner));
        }

        [Test]
        public void AdFormatRegistry_IsEnabled_NonCatalogOrMissingGame_ReturnsFalse()
        {
            var reg = new AdFormatRegistry();
            foreach (var gameId in new[] { "Unknown_Game", "garage_legends", "GARAGE_LEGENDS", " Garage_Legends" })
            {
                reg.Register(AdFormatConfig.Enabled(AdFormatKind.Banner, gameId));
                Assert.IsFalse(reg.IsEnabled(gameId, AdFormatKind.Banner), gameId);
            }
            Assert.IsFalse(reg.IsEnabled(null, AdFormatKind.Banner));
            Assert.IsFalse(reg.IsEnabled(string.Empty, AdFormatKind.Banner));
        }

        [Test]
        public void AdFormatRegistry_IsEnabled_RegisteredDisabled_ReturnsFalse()
        {
            var reg = new AdFormatRegistry();
            reg.Register(AdFormatConfig.Disabled(AdFormatKind.Banner, "Garage_Legends"));
            Assert.IsFalse(reg.IsEnabled("Garage_Legends", AdFormatKind.Banner));
        }

        // AdNaturalBreakPolicy tests

        [Test]
        public void AdNaturalBreakPolicy_AllowedBreak_ReturnsTrue()
        {
            var policy = new AdNaturalBreakPolicy(new List<string> { "LevelComplete", "MainMenu" });
            Assert.IsTrue(policy.IsNaturalBreak("LevelComplete"));
        }

        [Test]
        public void AdNaturalBreakPolicy_UnknownBreak_ReturnsFalse()
        {
            var policy = new AdNaturalBreakPolicy(new List<string> { "LevelComplete" });
            Assert.IsFalse(policy.IsNaturalBreak("MidBoard"));
        }

        [Test]
        public void AdNaturalBreakPolicy_NullBreakPoint_ReturnsFalse()
        {
            var policy = new AdNaturalBreakPolicy(new List<string> { "LevelComplete" });
            Assert.IsFalse(policy.IsNaturalBreak(null));
        }

        [Test]
        public void AdNaturalBreakPolicy_NullList_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => new AdNaturalBreakPolicy(null));
        }

        [Test]
        public void AdNaturalBreakPolicy_AllBreaksBlocked_AlwaysFalse()
        {
            var policy = AdNaturalBreakPolicy.AllBreaksBlocked;
            Assert.IsFalse(policy.IsNaturalBreak("LevelComplete"));
            Assert.IsFalse(policy.IsNaturalBreak("MainMenu"));
            Assert.IsFalse(policy.IsNaturalBreak(""));
        }
    }
}
