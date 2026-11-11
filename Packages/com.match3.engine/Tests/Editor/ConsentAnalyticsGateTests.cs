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
        public void ConsentPolicy_Safe_SetsAllowChildSafeAnalyticsFalse()
        {
            var p = ConsentPolicy.Safe("desc");
            Assert.IsFalse(p.AllowChildSafeAnalytics);
        }

        [Test]
        public void ConsentPolicy_Default_IsCorrect()
        {
            var d = ConsentPolicy.Default;
            Assert.IsTrue(d.RequireExplicitConsent);
            Assert.IsFalse(d.AllowChildSafeAnalytics);
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
            // lowercase "wonder_pop" must NOT trigger Guard 1 (Ordinal comparison)
            // it will fail on Guard 3 (RequireExplicitConsent + ExplicitConsent == ExplicitConsent => passes)
            // actually ExplicitConsent satisfies RequireExplicitConsent → eligible
            var permissivePolicy = new ConsentPolicy(); // default struct: RequireExplicitConsent=false
            // Use a policy that doesn't block on other guards to verify Guard 1 doesn't fire
            // ConsentPolicy.Safe requires explicit consent; "wonder_pop" with ExplicitConsent → eligible
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

        [Test]
        public void CanCollect_ChildSafe_PolicyDisallows_IsBlocked()
        {
            // Need a policy that passes Guard 3 but blocks Guard 4
            // RequireExplicitConsent=false, AllowChildSafeAnalytics=false
            // ConsentPolicy Safe sets RequireExplicitConsent=true, so ChildSafe would fail Guard 3 first.
            // We need to construct a scenario: use a game-agnostic record with ChildSafe basis
            // and a policy that does NOT require explicit consent but disallows ChildSafe.
            // ConsentPolicy.Safe always sets RequireExplicitConsent=true which blocks Guard 3 for non-ExplicitConsent.
            // So for Guard 4 to fire, we need RequireExplicitConsent=false.
            // ConsentPolicy struct has defaults: RequireExplicitConsent=false, AllowChildSafeAnalytics=false
            // But ConsentPolicy is a readonly struct; use default(ConsentPolicy) for RequireExplicit=false.
            // However PolicyDescription would be null — the struct doesn't validate in constructor for default.
            // This is acceptable because only Safe() validates. Use Safe with allowChildSafe manually.
            // Actually there's no public factory that creates RequireExplicit=false, AllowChildSafe=false.
            // Per spec we only have Safe() which is RequireExplicit=true, AllowChildSafe=false.
            // With Safe() + ChildSafe basis: Guard 3 fires first (ExplicitConsentRequired), not Guard 4.
            // Guard 4 is only reachable when RequireExplicitConsent=false.
            // We'll use default(ConsentPolicy) which gives RequireExplicit=false, AllowChildSafe=false.
            var record = ConsentRecord.Create("GameA", ConsentBasis.ChildSafe);
            var policy = default(ConsentPolicy); // RequireExplicitConsent=false, AllowChildSafeAnalytics=false
            var result = ConsentAnalyticsGate.CanCollectAnalytics("GameA", record, policy, out var reason);
            Assert.IsFalse(result);
            Assert.AreEqual("ChildSafeAnalyticsDisabled", reason);
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
