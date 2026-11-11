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
            var record = RemoteConfigRecord.Create("TestGame", RemoteConfigScope.Anonymous, true, false);
            Assert.AreEqual("TestGame", record.GameId);
            Assert.AreEqual(RemoteConfigScope.Anonymous, record.Scope);
            Assert.IsTrue(record.HasUserConsent);
            Assert.IsFalse(record.ContainsUserIdentifier);
        }

        [Test]
        public void RemoteConfigRecord_IsConsentSafe_NoIdentifier_True()
        {
            var record = RemoteConfigRecord.Create("TestGame", RemoteConfigScope.Anonymous, false, false);
            Assert.IsTrue(record.IsConsentSafe);
        }

        [Test]
        public void RemoteConfigRecord_IsConsentSafe_IdentifierWithConsent_True()
        {
            var record = RemoteConfigRecord.Create("TestGame", RemoteConfigScope.ConsentedUser, true, true);
            Assert.IsTrue(record.IsConsentSafe);
        }

        [Test]
        public void RemoteConfigRecord_IsConsentSafe_IdentifierNoConsent_False()
        {
            var record = RemoteConfigRecord.Create("TestGame", RemoteConfigScope.ConsentedUser, false, true);
            Assert.IsFalse(record.IsConsentSafe);
        }

        [Test]
        public void RemoteConfigRecord_IsAnonymous_Anonymous_True()
        {
            var record = RemoteConfigRecord.Create("TestGame", RemoteConfigScope.Anonymous, false, false);
            Assert.IsTrue(record.IsAnonymous);
        }

        [Test]
        public void RemoteConfigRecord_IsAnonymous_AggregateOnly_True()
        {
            var record = RemoteConfigRecord.Create("TestGame", RemoteConfigScope.AggregateOnly, false, false);
            Assert.IsTrue(record.IsAnonymous);
        }

        [Test]
        public void RemoteConfigRecord_IsAnonymous_ConsentedUser_False()
        {
            var record = RemoteConfigRecord.Create("TestGame", RemoteConfigScope.ConsentedUser, true, false);
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
        public void CanFetchRemoteConfig_WonderPop_Lowercase_NotBlocked()
        {
            var record = RemoteConfigRecord.Create("wonder_pop", RemoteConfigScope.Anonymous, false, true);
            var policy = ConsentSafeRemoteConfigPolicy.Default;
            // Ordinal mismatch: "wonder_pop" != "Wonder_Pop"
            // But ContainsUserIdentifier=true, HasUserConsent=false => Guard 2 fires
            var result = ConsentSafeRemoteConfigGate.CanFetchRemoteConfig("wonder_pop", record, policy, out string reason);
            Assert.IsFalse(result);
            Assert.AreNotEqual("WonderPopUserIdentifierProhibited", reason);
        }

        [Test]
        public void CanFetchRemoteConfig_UserIdentifierNoConsent_Blocked()
        {
            var record = RemoteConfigRecord.Create("OtherGame", RemoteConfigScope.PlatformDefault, false, true);
            var policy = ConsentSafeRemoteConfigPolicy.Default;
            var result = ConsentSafeRemoteConfigGate.CanFetchRemoteConfig("OtherGame", record, policy, out string reason);
            Assert.IsFalse(result);
            Assert.AreEqual("UserIdentifierRequiresConsent", reason);
        }

        [Test]
        public void CanFetchRemoteConfig_UserIdentifierWithConsent_Eligible()
        {
            var record = RemoteConfigRecord.Create("OtherGame", RemoteConfigScope.ConsentedUser, true, true);
            var policy = ConsentSafeRemoteConfigPolicy.Default;
            var result = ConsentSafeRemoteConfigGate.CanFetchRemoteConfig("OtherGame", record, policy, out string reason);
            Assert.IsTrue(result);
            Assert.AreEqual(string.Empty, reason);
        }

        [Test]
        public void CanFetchRemoteConfig_ConsentedScopeNoConsent_Blocked()
        {
            var record = RemoteConfigRecord.Create("OtherGame", RemoteConfigScope.ConsentedUser, false, false);
            var policy = ConsentSafeRemoteConfigPolicy.Default;
            var result = ConsentSafeRemoteConfigGate.CanFetchRemoteConfig("OtherGame", record, policy, out string reason);
            Assert.IsFalse(result);
            Assert.AreEqual("ConsentedScopeRequiresConsent", reason);
        }

        [Test]
        public void CanFetchRemoteConfig_ConsentedScopeWithConsent_Eligible()
        {
            var record = RemoteConfigRecord.Create("OtherGame", RemoteConfigScope.ConsentedUser, true, false);
            var policy = ConsentSafeRemoteConfigPolicy.Default;
            var result = ConsentSafeRemoteConfigGate.CanFetchRemoteConfig("OtherGame", record, policy, out string reason);
            Assert.IsTrue(result);
            Assert.AreEqual(string.Empty, reason);
        }

        [Test]
        public void CanFetchRemoteConfig_AnonymousScope_SafePolicy_Eligible()
        {
            var record = RemoteConfigRecord.Create("OtherGame", RemoteConfigScope.Anonymous, false, false);
            var policy = ConsentSafeRemoteConfigPolicy.Default;
            var result = ConsentSafeRemoteConfigGate.CanFetchRemoteConfig("OtherGame", record, policy, out string reason);
            Assert.IsTrue(result);
            Assert.AreEqual(string.Empty, reason);
        }

        [Test]
        public void CanFetchRemoteConfig_EmergencyKillSwitch_Eligible()
        {
            var record = RemoteConfigRecord.Create("OtherGame", RemoteConfigScope.EmergencyKillSwitch, false, false);
            var policy = ConsentSafeRemoteConfigPolicy.Default;
            var result = ConsentSafeRemoteConfigGate.CanFetchRemoteConfig("OtherGame", record, policy, out string reason);
            Assert.IsTrue(result);
            Assert.AreEqual(string.Empty, reason);
        }

        // Helper method tests

        [Test]
        public void IsConsentSafe_Helper_NoIdentifier_True()
        {
            var record = RemoteConfigRecord.Create("TestGame", RemoteConfigScope.Anonymous, false, false);
            Assert.IsTrue(ConsentSafeRemoteConfigGate.IsConsentSafe(record));
        }

        [Test]
        public void IsConsentSafe_Helper_IdentifierNoConsent_False()
        {
            var record = RemoteConfigRecord.Create("TestGame", RemoteConfigScope.ConsentedUser, false, true);
            Assert.IsFalse(ConsentSafeRemoteConfigGate.IsConsentSafe(record));
        }

        [Test]
        public void IsAnonymous_Helper_AggregateOnly_True()
        {
            var record = RemoteConfigRecord.Create("TestGame", RemoteConfigScope.AggregateOnly, false, false);
            Assert.IsTrue(ConsentSafeRemoteConfigGate.IsAnonymous(record));
        }

        [Test]
        public void IsAnonymous_Helper_ConsentedUser_False()
        {
            var record = RemoteConfigRecord.Create("TestGame", RemoteConfigScope.ConsentedUser, true, false);
            Assert.IsFalse(ConsentSafeRemoteConfigGate.IsAnonymous(record));
        }

        // RemoteConfigLog tests

        [Test]
        public void RemoteConfigLog_Record_And_Count()
        {
            var log = new RemoteConfigLog();
            Assert.AreEqual(0, log.Count);
            log.Record("TestGame", "Anonymous");
            log.Record("TestGame", "AggregateOnly");
            Assert.AreEqual(2, log.Count);
        }

        [Test]
        public void RemoteConfigLog_HasEntry_True()
        {
            var log = new RemoteConfigLog();
            log.Record("TestGame", "Anonymous");
            Assert.IsTrue(log.HasEntry("TestGame"));
            Assert.IsFalse(log.HasEntry("OtherGame"));
        }

        [Test]
        public void RemoteConfigLog_Clear_ResetsCount()
        {
            var log = new RemoteConfigLog();
            log.Record("TestGame", "Anonymous");
            log.Record("TestGame", "AggregateOnly");
            log.Clear();
            Assert.AreEqual(0, log.Count);
        }
    }
}
