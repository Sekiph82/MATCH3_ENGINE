// ENG-M18-003 ConsentAnalyticsGate tests
using System;
using NUnit.Framework;
using Match3.Engine.Runtime.Analytics;

namespace Match3.Engine.Tests.Editor
{
    [TestFixture]
    public class ConsentAnalyticsGateTests
    {
        // ---- ConsentRecord validation ----

        [Test]
        public void ConsentRecord_NullGameId_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() => ConsentRecord.Create(null, ConsentBasis.ExplicitConsent));
        }

        [Test]
        public void ConsentRecord_EmptyGameId_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() => ConsentRecord.Create("", ConsentBasis.ExplicitConsent));
        }

        // ---- ConsentRecord Create / Revoke factories ----

        [Test]
        public void ConsentRecord_Create_IsNotRevoked()
        {
            var r = ConsentRecord.Create("GameA", ConsentBasis.ExplicitConsent);
            Assert.IsFalse(r.IsRevoked);
        }

        [Test]
        public void ConsentRecord_Create_StoresBasisAndGameId()
        {
            var r = ConsentRecord.Create("GameA", ConsentBasis.LegitimateInterest);
            Assert.AreEqual("GameA", r.GameId);
            Assert.AreEqual(ConsentBasis.LegitimateInterest, r.Basis);
        }

        [Test]
        public void ConsentRecord_Revoke_SetsIsRevoked()
        {
            var r = ConsentRecord.Create("GameA", ConsentBasis.ExplicitConsent);
            var revoked = ConsentRecord.Revoke(r);
            Assert.IsTrue(revoked.IsRevoked);
        }

        [Test]
        public void ConsentRecord_Revoke_PreservesBasisAndGameId()
        {
            var r = ConsentRecord.Create("GameA", ConsentBasis.LegitimateInterest);
            var revoked = ConsentRecord.Revoke(r);
            Assert.AreEqual("GameA", revoked.GameId);
            Assert.AreEqual(ConsentBasis.LegitimateInterest, revoked.Basis);
        }

        // ---- IsAnalyticsAllowed ----

        [Test]
        public void IsAnalyticsAllowed_ExplicitConsent_NotRevoked_IsTrue()
        {
            var r = ConsentRecord.Create("GameA", ConsentBasis.ExplicitConsent);
            Assert.IsTrue(r.IsAnalyticsAllowed);
        }

        [Test]
        public void IsAnalyticsAllowed_LegitimateInterest_NotRevoked_IsTrue()
        {
            var r = ConsentRecord.Create("GameA", ConsentBasis.LegitimateInterest);
            Assert.IsTrue(r.IsAnalyticsAllowed);
        }

        [Test]
        public void IsAnalyticsAllowed_ChildSafe_NotRevoked_IsTrue()
        {
            var r = ConsentRecord.Create("GameA", ConsentBasis.ChildSafe);
            Assert.IsTrue(r.IsAnalyticsAllowed);
        }

        [Test]
        public void IsAnalyticsAllowed_NoneRequired_IsFalse()
        {
            var r = ConsentRecord.Create("GameA", ConsentBasis.NoneRequired);
            Assert.IsFalse(r.IsAnalyticsAllowed);
        }

        [Test]
        public void IsAnalyticsAllowed_ExplicitConsent_Revoked_IsFalse()
        {
            var r = ConsentRecord.Revoke(ConsentRecord.Create("GameA", ConsentBasis.ExplicitConsent));
            Assert.IsFalse(r.IsAnalyticsAllowed);
        }

        // ---- ConsentPolicy validation ----

        [Test]
        public void ConsentPolicy_Safe_NullDesc_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() => ConsentPolicy.Safe(null));
        }

        [Test]
        public void ConsentPolicy_Safe_EmptyDesc_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() => ConsentPolicy.Safe(""));
        }

        // ---- ConsentPolicy Safe / Default ----

        [Test]
        public void ConsentPolicy_Safe_SetsRequireExplicitConsentTrue()
        {
            var p = ConsentPolicy.Safe("desc");
            Assert.IsTrue(p.RequireExplicitConsent);
        }

        [Test]
        public void ConsentPolicy_Default_IsCorrect()
        {
            var d = ConsentPolicy.Default;
            Assert.IsTrue(d.RequireExplicitConsent);
            Assert.IsFalse(string.IsNullOrEmpty(d.PolicyDescription));
        }

        // ---- Guard 1: Wonder_Pop blocked (Ordinal) ----

        [Test]
        public void CanCollect_WonderPop_ExactCase_IsBlocked()
        {
            var record = ConsentRecord.Create("Wonder_Pop", ConsentBasis.ExplicitConsent);
            var policy = ConsentPolicy.Safe("desc");
            var result = ConsentAnalyticsGate.CanCollectAnalytics("Wonder_Pop", record, policy, out var reason);
            Assert.IsFalse(result);
            Assert.AreEqual("WonderPopAnalyticsProhibited", reason);
        }

        [Test]
        public void CanCollect_WonderPopLowercase_IsNotBlocked_OrdinalCheck()
        {
            var record = ConsentRecord.Create("wonder_pop", ConsentBasis.ExplicitConsent);
            var policy = ConsentPolicy.Safe("desc");
            // Ordinal: lowercase "wonder_pop" is not the Wonder_Pop guard; ExplicitConsent → eligible
            var result = ConsentAnalyticsGate.CanCollectAnalytics("wonder_pop", record, policy, out var reason);
            Assert.IsTrue(result);
        }

        // ---- Guard 2: ConsentRevoked ----

        [Test]
        public void CanCollect_RevokedConsent_IsBlocked()
        {
            var record = ConsentRecord.Revoke(ConsentRecord.Create("GameA", ConsentBasis.ExplicitConsent));
            var policy = ConsentPolicy.Safe("desc");
            var result = ConsentAnalyticsGate.CanCollectAnalytics("GameA", record, policy, out var reason);
            Assert.IsFalse(result);
            Assert.AreEqual("ConsentRevoked", reason);
        }

        // ---- Guard 3: ExplicitConsentRequired ----

        [Test]
        public void CanCollect_RequireExplicit_LegitimateInterest_IsBlocked()
        {
            var record = ConsentRecord.Create("GameA", ConsentBasis.LegitimateInterest);
            var policy = ConsentPolicy.Safe("desc"); // RequireExplicitConsent=true
            var result = ConsentAnalyticsGate.CanCollectAnalytics("GameA", record, policy, out var reason);
            Assert.IsFalse(result);
            Assert.AreEqual("ExplicitConsentRequired", reason);
        }

        [Test]
        public void CanCollect_RequireExplicit_ExplicitConsent_Passes()
        {
            var record = ConsentRecord.Create("GameA", ConsentBasis.ExplicitConsent);
            var policy = ConsentPolicy.Safe("desc");
            var result = ConsentAnalyticsGate.CanCollectAnalytics("GameA", record, policy, out _);
            Assert.IsTrue(result);
        }

        // ---- Guard 4: ChildSafeAnalyticsDisabled ----

        private static readonly ConsentPolicy[] AllConstructiblePolicies =
            { ConsentPolicy.Safe("desc"), ConsentPolicy.Default, default(ConsentPolicy) };

        [Test]
        public void CanCollect_ChildSafe_BlockedUnderEveryConstructiblePolicy()
        {
            var record = ConsentRecord.Create("GameA", ConsentBasis.ChildSafe);
            foreach (var policy in AllConstructiblePolicies)
            {
                Assert.IsFalse(ConsentAnalyticsGate.CanCollectAnalytics("GameA", record, policy, out var reason));
                Assert.AreEqual("ChildSafeAnalyticsDisabled", reason);
            }
        }

        [Test]
        public void CanCollect_NoneRequired_BlockedUnderEveryConstructiblePolicy()
        {
            var record = ConsentRecord.Create("GameA", ConsentBasis.NoneRequired);
            foreach (var policy in AllConstructiblePolicies)
            {
                Assert.IsFalse(ConsentAnalyticsGate.CanCollectAnalytics("GameA", record, policy, out var reason));
                Assert.AreEqual("ExplicitConsentRequired", reason);
            }
        }

        [Test]
        public void CanCollect_NoneRequired_UnderDefault_IsBlocked()
        {
            var record = ConsentRecord.Create("GameA", ConsentBasis.NoneRequired);
            Assert.IsFalse(ConsentAnalyticsGate.CanCollectAnalytics("GameA", record, ConsentPolicy.Default, out var reason));
            Assert.AreEqual("ExplicitConsentRequired", reason);
        }

        [Test]
        public void CanCollect_DefaultStructPolicy_FailsClosed()
        {
            // default(ConsentPolicy) is normalized to ConsentPolicy.Default: explicit consent still required.
            var record = ConsentRecord.Create("GameA", ConsentBasis.LegitimateInterest);
            Assert.IsFalse(ConsentAnalyticsGate.CanCollectAnalytics("GameA", record, ConsentPolicy.Default, out var expected));
            Assert.IsFalse(ConsentAnalyticsGate.CanCollectAnalytics("GameA", record, default(ConsentPolicy), out var reason));
            Assert.AreEqual(expected, reason);
            Assert.AreEqual("ExplicitConsentRequired", reason);
            Assert.IsTrue(ConsentAnalyticsGate.RequiresExplicitConsent(default(ConsentPolicy)));
        }

        [Test]
        public void CanCollect_GameIdMismatch_Blocked()
        {
            var record = ConsentRecord.Create("Wonder_Pop", ConsentBasis.ExplicitConsent);
            Assert.IsFalse(ConsentAnalyticsGate.CanCollectAnalytics("Coral_Rescue", record, ConsentPolicy.Default, out var reason));
            Assert.AreEqual("GameIdMismatch", reason);
        }

        [Test]
        public void CanCollect_WonderPopParam_RecordDiffers_GameIdMismatch()
        {
            var record = ConsentRecord.Create("Coral_Rescue", ConsentBasis.ExplicitConsent);
            Assert.IsFalse(ConsentAnalyticsGate.CanCollectAnalytics("Wonder_Pop", record, ConsentPolicy.Default, out var reason));
            Assert.AreEqual("GameIdMismatch", reason);
        }

        // ---- RequiresExplicitConsent and IsConsentActive helpers ----

        [Test]
        public void RequiresExplicitConsent_SafePolicy_ReturnsTrue()
        {
            var policy = ConsentPolicy.Safe("desc");
            Assert.IsTrue(ConsentAnalyticsGate.RequiresExplicitConsent(policy));
        }

        [Test]
        public void IsConsentActive_ActiveRecord_ReturnsTrue()
        {
            var record = ConsentRecord.Create("GameA", ConsentBasis.ExplicitConsent);
            Assert.IsTrue(ConsentAnalyticsGate.IsConsentActive(record));
        }

        [Test]
        public void IsConsentActive_RevokedRecord_ReturnsFalse()
        {
            var record = ConsentRecord.Revoke(ConsentRecord.Create("GameA", ConsentBasis.ExplicitConsent));
            Assert.IsFalse(ConsentAnalyticsGate.IsConsentActive(record));
        }

        [Test]
        public void IsConsentActive_NoneRequired_ReturnsFalse()
        {
            var record = ConsentRecord.Create("GameA", ConsentBasis.NoneRequired);
            Assert.IsFalse(ConsentAnalyticsGate.IsConsentActive(record));
        }

        // ---- ConsentAuditLog ----

        [Test]
        public void ConsentAuditLog_Record_NullGameId_ThrowsArgumentException()
        {
            var log = new ConsentAuditLog();
            Assert.Throws<ArgumentException>(() => log.Record(null, "action"));
        }

        [Test]
        public void ConsentAuditLog_Record_EmptyGameId_ThrowsArgumentException()
        {
            var log = new ConsentAuditLog();
            Assert.Throws<ArgumentException>(() => log.Record("", "action"));
        }

        [Test]
        public void ConsentAuditLog_Record_NullAction_ThrowsArgumentException()
        {
            var log = new ConsentAuditLog();
            Assert.Throws<ArgumentException>(() => log.Record("GameA", null));
        }

        [Test]
        public void ConsentAuditLog_Record_EmptyAction_ThrowsArgumentException()
        {
            var log = new ConsentAuditLog();
            Assert.Throws<ArgumentException>(() => log.Record("GameA", ""));
        }

        [Test]
        public void ConsentAuditLog_Record_IncreasesCount()
        {
            var log = new ConsentAuditLog();
            log.Record("GameA", "granted");
            log.Record("GameB", "revoked");
            Assert.AreEqual(2, log.Count);
        }

        [Test]
        public void ConsentAuditLog_HasEntry_ReturnsTrueForKnownGameId()
        {
            var log = new ConsentAuditLog();
            log.Record("GameA", "granted");
            Assert.IsTrue(log.HasEntry("GameA"));
        }

        [Test]
        public void ConsentAuditLog_HasEntry_ReturnsFalseForUnknownGameId()
        {
            var log = new ConsentAuditLog();
            log.Record("GameA", "granted");
            Assert.IsFalse(log.HasEntry("GameB"));
        }

        [Test]
        public void ConsentAuditLog_Clear_ResetsCountToZero()
        {
            var log = new ConsentAuditLog();
            log.Record("GameA", "granted");
            log.Clear();
            Assert.AreEqual(0, log.Count);
        }

        [Test]
        public void ConsentAuditLog_HasEntry_AfterClear_ReturnsFalse()
        {
            var log = new ConsentAuditLog();
            log.Record("GameA", "granted");
            log.Clear();
            Assert.IsFalse(log.HasEntry("GameA"));
        }
    }
}
