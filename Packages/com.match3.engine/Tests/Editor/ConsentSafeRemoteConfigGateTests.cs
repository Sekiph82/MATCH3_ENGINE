using System;
using NUnit.Framework;
using Match3.Engine.Runtime.Privacy;

namespace Match3.Engine.Tests.Editor
{
    [TestFixture]
    public class ConsentSafeRemoteConfigGateTests
    {
        // RemoteConfigRecord tests

        [Test]
        public void RemoteConfigRecord_Create_NullGameId_Throws()
        {
            Assert.Throws<ArgumentException>(() =>
                RemoteConfigRecord.Create(null, RemoteConfigScope.Anonymous, false, false));
        }

        [Test]
        public void RemoteConfigRecord_Create_EmptyGameId_Throws()
        {
            Assert.Throws<ArgumentException>(() =>
                RemoteConfigRecord.Create("", RemoteConfigScope.Anonymous, false, false));
        }

        [Test]
        public void RemoteConfigRecord_Create_Valid_Succeeds()
        {
            var record = RemoteConfigRecord.Create("Coral_Rescue", RemoteConfigScope.Anonymous, true, false);
            Assert.AreEqual("Coral_Rescue", record.GameId);
            Assert.AreEqual(RemoteConfigScope.Anonymous, record.Scope);
            Assert.IsTrue(record.HasUserConsent);
            Assert.IsFalse(record.ContainsUserIdentifier);
        }

        [Test]
        public void RemoteConfigRecord_IsConsentSafe_NoIdentifier_True()
        {
            var record = RemoteConfigRecord.Create("Coral_Rescue", RemoteConfigScope.Anonymous, false, false);
            Assert.IsTrue(record.IsConsentSafe);
        }

        [Test]
        public void RemoteConfigRecord_IsConsentSafe_IdentifierWithConsent_True()
        {
            var record = RemoteConfigRecord.Create("Coral_Rescue", RemoteConfigScope.ConsentedUser, true, true);
            Assert.IsTrue(record.IsConsentSafe);
        }

        [Test]
        public void RemoteConfigRecord_IsConsentSafe_IdentifierNoConsent_False()
        {
            var record = RemoteConfigRecord.Create("Coral_Rescue", RemoteConfigScope.ConsentedUser, false, true);
            Assert.IsFalse(record.IsConsentSafe);
        }

        [Test]
        public void RemoteConfigRecord_IsAnonymous_Anonymous_True()
        {
            var record = RemoteConfigRecord.Create("Coral_Rescue", RemoteConfigScope.Anonymous, false, false);
            Assert.IsTrue(record.IsAnonymous);
        }

        [Test]
        public void RemoteConfigRecord_IsAnonymous_AggregateOnly_True()
        {
            var record = RemoteConfigRecord.Create("Coral_Rescue", RemoteConfigScope.AggregateOnly, false, false);
            Assert.IsTrue(record.IsAnonymous);
        }

        [Test]
        public void RemoteConfigRecord_IsAnonymous_ConsentedUser_False()
        {
            var record = RemoteConfigRecord.Create("Coral_Rescue", RemoteConfigScope.ConsentedUser, true, false);
            Assert.IsFalse(record.IsAnonymous);
        }

        // ConsentSafeRemoteConfigPolicy tests

        [Test]
        public void ConsentSafeRemoteConfigPolicy_Safe_Values_Correct()
        {
            var policy = ConsentSafeRemoteConfigPolicy.Safe("Test policy");
            Assert.IsTrue(policy.RequireConsentForUserConfig);
            Assert.IsTrue(policy.BlockUserIdentifiersWithoutConsent);
            Assert.AreEqual("Test policy", policy.PolicyDescription);
        }

        [Test]
        public void ConsentSafeRemoteConfigPolicy_Default_NotNull()
        {
            var policy = ConsentSafeRemoteConfigPolicy.Default;
            Assert.IsNotNull(policy.PolicyDescription);
            Assert.IsTrue(policy.RequireConsentForUserConfig);
            Assert.IsTrue(policy.BlockUserIdentifiersWithoutConsent);
        }

        [Test]
        public void ConsentSafeRemoteConfigPolicy_NullDescription_Throws()
        {
            Assert.Throws<ArgumentException>(() => ConsentSafeRemoteConfigPolicy.Safe(null));
        }

        // CanFetchRemoteConfig tests

        [Test]
        public void CanFetchRemoteConfig_WonderPop_WithUserIdentifier_Blocked()
        {
            var record = RemoteConfigRecord.Create("Wonder_Pop", RemoteConfigScope.ConsentedUser, true, true);
            var policy = ConsentSafeRemoteConfigPolicy.Default;
            var result = ConsentSafeRemoteConfigGate.CanFetchRemoteConfig("Wonder_Pop", record, policy, out string reason);
            Assert.IsFalse(result);
            Assert.AreEqual("WonderPopUserIdentifierProhibited", reason);
        }

        [Test]
        public void CanFetchRemoteConfig_WonderPop_NoUserIdentifier_NotBlocked()
        {
            var record = RemoteConfigRecord.Create("Wonder_Pop", RemoteConfigScope.Anonymous, false, false);
            var policy = ConsentSafeRemoteConfigPolicy.Default;
            var result = ConsentSafeRemoteConfigGate.CanFetchRemoteConfig("Wonder_Pop", record, policy, out string reason);
            Assert.IsTrue(result);
            Assert.AreEqual(string.Empty, reason);
        }

        [Test]
        public void CanFetchRemoteConfig_WonderPop_Lowercase_UnknownGame()
        {
            var record = RemoteConfigRecord.Create("wonder_pop", RemoteConfigScope.Anonymous, false, true);
            var policy = ConsentSafeRemoteConfigPolicy.Default;
            var result = ConsentSafeRemoteConfigGate.CanFetchRemoteConfig("wonder_pop", record, policy, out string reason);
            Assert.IsFalse(result);
            Assert.AreNotEqual("WonderPopUserIdentifierProhibited", reason);
            // Ordinal catalog lookup: the case variant is not a catalog ID, so it fails closed with UnknownGame.
            Assert.AreEqual("UnknownGame", reason);
        }

        [Test]
        public void CanFetchRemoteConfig_UserIdentifierNoConsent_Blocked()
        {
            var record = RemoteConfigRecord.Create("Garage_Legends", RemoteConfigScope.PlatformDefault, false, true);
            var policy = ConsentSafeRemoteConfigPolicy.Default;
            var result = ConsentSafeRemoteConfigGate.CanFetchRemoteConfig("Garage_Legends", record, policy, out string reason);
            Assert.IsFalse(result);
            Assert.AreEqual("UserIdentifierRequiresConsent", reason);
        }

        [Test]
        public void CanFetchRemoteConfig_UserIdentifierWithConsent_Eligible()
        {
            var record = RemoteConfigRecord.Create("Garage_Legends", RemoteConfigScope.ConsentedUser, true, true);
            var policy = ConsentSafeRemoteConfigPolicy.Default;
            var result = ConsentSafeRemoteConfigGate.CanFetchRemoteConfig("Garage_Legends", record, policy, out string reason);
            Assert.IsTrue(result);
            Assert.AreEqual(string.Empty, reason);
        }

        [Test]
        public void CanFetchRemoteConfig_ConsentedScopeNoConsent_Blocked()
        {
            var record = RemoteConfigRecord.Create("Garage_Legends", RemoteConfigScope.ConsentedUser, false, false);
            var policy = ConsentSafeRemoteConfigPolicy.Default;
            var result = ConsentSafeRemoteConfigGate.CanFetchRemoteConfig("Garage_Legends", record, policy, out string reason);
            Assert.IsFalse(result);
            Assert.AreEqual("ConsentedScopeRequiresConsent", reason);
        }

        [Test]
        public void CanFetchRemoteConfig_ConsentedScopeWithConsent_Eligible()
        {
            var record = RemoteConfigRecord.Create("Garage_Legends", RemoteConfigScope.ConsentedUser, true, false);
            var policy = ConsentSafeRemoteConfigPolicy.Default;
            var result = ConsentSafeRemoteConfigGate.CanFetchRemoteConfig("Garage_Legends", record, policy, out string reason);
            Assert.IsTrue(result);
            Assert.AreEqual(string.Empty, reason);
        }

        [Test]
        public void CanFetchRemoteConfig_AnonymousScope_SafePolicy_Eligible()
        {
            var record = RemoteConfigRecord.Create("Garage_Legends", RemoteConfigScope.Anonymous, false, false);
            var policy = ConsentSafeRemoteConfigPolicy.Default;
            var result = ConsentSafeRemoteConfigGate.CanFetchRemoteConfig("Garage_Legends", record, policy, out string reason);
            Assert.IsTrue(result);
            Assert.AreEqual(string.Empty, reason);
        }

        [Test]
        public void CanFetchRemoteConfig_EmergencyKillSwitch_Eligible()
        {
            var record = RemoteConfigRecord.Create("Garage_Legends", RemoteConfigScope.EmergencyKillSwitch, false, false);
            var policy = ConsentSafeRemoteConfigPolicy.Default;
            var result = ConsentSafeRemoteConfigGate.CanFetchRemoteConfig("Garage_Legends", record, policy, out string reason);
            Assert.IsTrue(result);
            Assert.AreEqual(string.Empty, reason);
        }

        // Helper method tests

        [Test]
        public void IsConsentSafe_Helper_NoIdentifier_True()
        {
            var record = RemoteConfigRecord.Create("Coral_Rescue", RemoteConfigScope.Anonymous, false, false);
            Assert.IsTrue(ConsentSafeRemoteConfigGate.IsConsentSafe(record));
        }

        [Test]
        public void IsConsentSafe_Helper_IdentifierNoConsent_False()
        {
            var record = RemoteConfigRecord.Create("Coral_Rescue", RemoteConfigScope.ConsentedUser, false, true);
            Assert.IsFalse(ConsentSafeRemoteConfigGate.IsConsentSafe(record));
        }

        [Test]
        public void IsAnonymous_Helper_AggregateOnly_True()
        {
            var record = RemoteConfigRecord.Create("Coral_Rescue", RemoteConfigScope.AggregateOnly, false, false);
            Assert.IsTrue(ConsentSafeRemoteConfigGate.IsAnonymous(record));
        }

        [Test]
        public void IsAnonymous_Helper_ConsentedUser_False()
        {
            var record = RemoteConfigRecord.Create("Coral_Rescue", RemoteConfigScope.ConsentedUser, true, false);
            Assert.IsFalse(ConsentSafeRemoteConfigGate.IsAnonymous(record));
        }

        // RemoteConfigLog tests

        [Test]
        public void RemoteConfigLog_Record_And_Count()
        {
            var log = new RemoteConfigLog();
            Assert.AreEqual(0, log.Count);
            log.Record("Coral_Rescue", "Anonymous");
            log.Record("Coral_Rescue", "AggregateOnly");
            Assert.AreEqual(2, log.Count);
        }

        [Test]
        public void RemoteConfigLog_HasEntry_True()
        {
            var log = new RemoteConfigLog();
            log.Record("Coral_Rescue", "Anonymous");
            Assert.IsTrue(log.HasEntry("Coral_Rescue"));
            Assert.IsFalse(log.HasEntry("Garage_Legends"));
        }

        [Test]
        public void RemoteConfigLog_Clear_ResetsCount()
        {
            var log = new RemoteConfigLog();
            log.Record("Coral_Rescue", "Anonymous");
            log.Record("Coral_Rescue", "AggregateOnly");
            log.Clear();
            Assert.AreEqual(0, log.Count);
        }

        // QA remediation: fail-closed default policy, GameId mismatch guard, log argument validation
        [Test]
        public void CanFetchRemoteConfig_DefaultStructPolicy_FailsClosed()
        {
            var record = RemoteConfigRecord.Create("Coral_Rescue", RemoteConfigScope.Anonymous, false, true);
            var expected = ConsentSafeRemoteConfigGate.CanFetchRemoteConfig("Coral_Rescue", record, ConsentSafeRemoteConfigPolicy.Default, out var expectedReason);
            var result = ConsentSafeRemoteConfigGate.CanFetchRemoteConfig("Coral_Rescue", record, default(ConsentSafeRemoteConfigPolicy), out var reason);
            Assert.IsFalse(expected);
            Assert.IsFalse(result);
            Assert.AreEqual("UserIdentifierRequiresConsent", expectedReason);
            Assert.AreEqual(expectedReason, reason);
        }

        [Test]
        public void CanFetchRemoteConfig_GameIdMismatch_Blocked()
        {
            var record = RemoteConfigRecord.Create("Wonder_Pop", RemoteConfigScope.Anonymous, false, true);
            var result = ConsentSafeRemoteConfigGate.CanFetchRemoteConfig("Coral_Rescue", record, ConsentSafeRemoteConfigPolicy.Default, out var reason);
            Assert.IsFalse(result);
            Assert.AreEqual("GameIdMismatch", reason);
        }

        [Test]
        public void RemoteConfigLog_Record_NullOrEmptySecondArg_Throws()
        {
            var log = new RemoteConfigLog();
            Assert.Throws<System.ArgumentException>(() => log.Record("Coral_Rescue", null));
            Assert.Throws<System.ArgumentException>(() => log.Record("Coral_Rescue", string.Empty));
            Assert.AreEqual(0, log.Count);
        }

        [Test]
        public void RemoteConfigLog_HasEntry_IsOrdinal()
        {
            var log = new RemoteConfigLog();
            log.Record("Wonder_Pop", "evt");
            Assert.IsTrue(log.HasEntry("Wonder_Pop"));
            Assert.IsFalse(log.HasEntry("wonder_pop"));
            // Soft hyphen (U+00AD) is ignorable under culture-sensitive comparison; Ordinal must not match it.
            log.Record("Coral\u00AD_Rescue", "evt");
            Assert.IsTrue(log.HasEntry("Coral\u00AD_Rescue"));
            Assert.IsFalse(log.HasEntry("Coral_Rescue"));
        }


        [Test]
        public void NullOrEmptyGameId_DefaultRecord_GameIdMissing()
        {
            foreach (var gameId in new[] { null, string.Empty })
            {
                Assert.IsFalse(ConsentSafeRemoteConfigGate.CanFetchRemoteConfig(gameId, default(RemoteConfigRecord), default(ConsentSafeRemoteConfigPolicy), out var reason));
                Assert.AreEqual("GameIdMissing", reason);
            }
        }

        [Test]
        public void UnknownOrCaseVariantGameId_DefaultRecord_UnknownGame()
        {
            foreach (var gameId in new[] { "Unknown_Game", "coral_rescue", "CORAL_RESCUE", " Coral_Rescue", "Coral_Rescue ", "wonder_pop", "WONDER_POP" })
            {
                Assert.IsFalse(ConsentSafeRemoteConfigGate.CanFetchRemoteConfig(gameId, default(RemoteConfigRecord), default(ConsentSafeRemoteConfigPolicy), out var reason));
                Assert.AreEqual("UnknownGame", reason);
            }
        }
    }
}
