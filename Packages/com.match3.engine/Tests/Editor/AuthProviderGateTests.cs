using System;
using NUnit.Framework;
using Match3.Engine.Runtime.Identity;

namespace Match3.Engine.Tests.Editor
{
    [TestFixture]
    public class AuthProviderGateTests
    {
        // --- AuthToken ---

        [Test]
        public void AuthToken_Empty_IsNotValid()
        {
            var token = AuthToken.Empty;
            Assert.IsFalse(token.IsValid);
        }

        [Test]
        public void AuthToken_FromPlatform_Valid_ReturnsValidToken()
        {
            var token = AuthToken.FromPlatform(AuthProvider.Apple, "tok123", "Coral_Rescue");
            Assert.IsTrue(token.IsValid);
            Assert.AreEqual(AuthProvider.Apple, token.Provider);
            Assert.AreEqual("tok123", token.TokenValue);
            Assert.AreEqual("Coral_Rescue", token.GameId);
        }

        [Test]
        public void AuthToken_FromPlatform_NoneProvider_Throws()
        {
            Assert.Throws<ArgumentException>(() =>
                AuthToken.FromPlatform(AuthProvider.None, "tok", "Coral_Rescue"));
        }

        [Test]
        public void AuthToken_FromPlatform_EmptyTokenValue_Throws()
        {
            Assert.Throws<ArgumentException>(() =>
                AuthToken.FromPlatform(AuthProvider.Google, "", "Coral_Rescue"));
        }

        [Test]
        public void AuthToken_FromPlatform_EmptyGameId_Throws()
        {
            Assert.Throws<ArgumentException>(() =>
                AuthToken.FromPlatform(AuthProvider.Google, "tok", ""));
        }

        // --- AuthProviderPolicy ---

        [Test]
        public void AuthProviderPolicy_Safe_AllowsLoginButDoesNotExposeToken()
        {
            var policy = AuthProviderPolicy.Safe("desc");
            Assert.IsTrue(policy.AllowPlatformLogin);
            Assert.IsFalse(policy.ExposeTokenToGameLogic);
        }

        [Test]
        public void AuthProviderPolicy_Default_IsNotExposingToken()
        {
            var policy = AuthProviderPolicy.Default;
            Assert.IsFalse(policy.ExposeTokenToGameLogic);
        }

        [Test]
        public void AuthProviderPolicy_NullDescription_Throws()
        {
            Assert.Throws<ArgumentException>(() =>
                new AuthProviderPolicy(true, false, null));
        }

        // --- AuthProviderGate.CanUsePlatformLogin ---

        [Test]
        public void CanUsePlatformLogin_WonderPop_IneligibleFirst_RegarlessOfPolicy()
        {
            // Guard 1 must fire even when policy allows login and provider is valid
            var policy = AuthProviderPolicy.Default;
            var result = AuthProviderGate.CanUsePlatformLogin("Wonder_Pop", AuthProvider.Apple, policy);
            Assert.IsFalse(result.IsEligible);
            Assert.AreEqual("WonderPopAuthProhibited", result.Reason);
        }

        [Test]
        public void CanUsePlatformLogin_WonderPop_CaseSensitive_OtherCaseIsUnknownGame()
        {
            // Ordinal catalog lookup: a case variant of Wonder_Pop is not a catalog ID, so it fails closed with UnknownGame (never eligible, never the Wonder_Pop reason).
            var policy = AuthProviderPolicy.Default;
            var result = AuthProviderGate.CanUsePlatformLogin("wonder_pop", AuthProvider.Apple, policy);
            Assert.IsFalse(result.IsEligible);
            Assert.AreEqual("UnknownGame", result.Reason);
        }

        [Test]
        public void CanUsePlatformLogin_PolicyDisabled_IneligibleWithReason()
        {
            var policy = new AuthProviderPolicy(false, false, "disabled");
            var result = AuthProviderGate.CanUsePlatformLogin("Coral_Rescue", AuthProvider.Apple, policy);
            Assert.IsFalse(result.IsEligible);
            Assert.AreEqual("PlatformLoginDisabled", result.Reason);
        }

        [Test]
        public void CanUsePlatformLogin_NoProvider_IneligibleWithReason()
        {
            var policy = AuthProviderPolicy.Default;
            var result = AuthProviderGate.CanUsePlatformLogin("Coral_Rescue", AuthProvider.None, policy);
            Assert.IsFalse(result.IsEligible);
            Assert.AreEqual("NoProviderSelected", result.Reason);
        }

        [Test]
        public void CanUsePlatformLogin_GoogleProvider_Eligible()
        {
            var policy = AuthProviderPolicy.Default;
            var result = AuthProviderGate.CanUsePlatformLogin("Coral_Rescue", AuthProvider.Google, policy);
            Assert.IsTrue(result.IsEligible);
        }

        [Test]
        public void IsTokenSafeForGameLogic_AlwaysFalse()
        {
            Assert.IsFalse(AuthProviderGate.IsTokenSafeForGameLogic(AuthProviderPolicy.Default));
        }

        // --- AuthSessionCache ---

        [Test]
        public void AuthSessionCache_StoreAndRetrieve()
        {
            var cache = new AuthSessionCache();
            var token = AuthToken.FromPlatform(AuthProvider.Apple, "tok", "Garage_Legends");
            cache.Store(token);
            Assert.IsTrue(cache.TryGet("Garage_Legends", out var retrieved));
            Assert.AreEqual("tok", retrieved.TokenValue);
        }

        [Test]
        public void AuthSessionCache_Invalidate_RemovesEntry()
        {
            var cache = new AuthSessionCache();
            var token = AuthToken.FromPlatform(AuthProvider.Google, "tok2", "Dino_Islands");
            cache.Store(token);
            cache.Invalidate("Dino_Islands");
            Assert.IsFalse(cache.TryGet("Dino_Islands", out _));
        }

        [Test]
        public void AuthSessionCache_Clear_EmptiesAll()
        {
            var cache = new AuthSessionCache();
            cache.Store(AuthToken.FromPlatform(AuthProvider.Apple, "t1", "Coral_Rescue"));
            cache.Store(AuthToken.FromPlatform(AuthProvider.Google, "t2", "Garage_Legends"));
            cache.Clear();
            Assert.AreEqual(0, cache.Count);
        }

        [Test]
        public void AuthSessionCache_Count_ReflectsStoredEntries()
        {
            var cache = new AuthSessionCache();
            Assert.AreEqual(0, cache.Count);
            cache.Store(AuthToken.FromPlatform(AuthProvider.Apple, "t1", "Coral_Rescue"));
            Assert.AreEqual(1, cache.Count);
            cache.Store(AuthToken.FromPlatform(AuthProvider.Google, "t2", "Coral_Rescue")); // overwrite same gameId
            Assert.AreEqual(1, cache.Count);
        }


        [Test]
        public void CanUsePlatformLogin_NullOrEmptyGameId_GameIdMissing()
        {
            foreach (var gameId in new[] { null, string.Empty })
            {
                var result = AuthProviderGate.CanUsePlatformLogin(gameId, AuthProvider.Apple, AuthProviderPolicy.Default);
                Assert.IsFalse(result.IsEligible);
                Assert.AreEqual("GameIdMissing", result.Reason);
            }
        }

        [Test]
        public void CanUsePlatformLogin_UnknownOrCaseVariantGameId_UnknownGame()
        {
            foreach (var gameId in new[] { "Unknown_Game", "coral_rescue", "CORAL_RESCUE", " Coral_Rescue", "Coral_Rescue ", "wonder_pop", "WONDER_POP" })
            {
                var result = AuthProviderGate.CanUsePlatformLogin(gameId, AuthProvider.Apple, AuthProviderPolicy.Default);
                Assert.IsFalse(result.IsEligible);
                Assert.AreEqual("UnknownGame", result.Reason);
            }
        }
    }
}
