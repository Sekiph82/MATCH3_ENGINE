using System;
using NUnit.Framework;
using Match3.Engine.Runtime.Identity;
using Match3.Engine.Runtime.Analytics;

namespace Match3.Engine.Tests.Editor
{
    public sealed class AnonymousIdentityGateTests
    {
        // --- GuestIdentity ---

        [Test]
        public void GuestIdentity_NullGameId_Throws()
        {
            Assert.Throws<ArgumentException>(() => new GuestIdentity(null, "key", false));
        }

        [Test]
        public void GuestIdentity_EmptyLocalKey_Throws()
        {
            Assert.Throws<ArgumentException>(() => new GuestIdentity("game", string.Empty, false));
        }

        [Test]
        public void GuestIdentity_Create_IsNotRestricted()
        {
            var id = GuestIdentity.Create("MyGame");
            Assert.IsFalse(id.IsRestricted);
            Assert.AreEqual("MyGame", id.GameId);
            Assert.IsFalse(string.IsNullOrEmpty(id.LocalKey));
        }

        [Test]
        public void GuestIdentity_CreateChild_IsRestricted()
        {
            var id = GuestIdentity.CreateChild("MyGame");
            Assert.IsTrue(id.IsRestricted);
        }

        [Test]
        public void GuestIdentity_IsEmpty_FalseWhenKeySet()
        {
            var id = GuestIdentity.Create("g");
            Assert.IsFalse(id.IsEmpty);
        }

        // --- IdentityPolicy ---

        [Test]
        public void IdentityPolicy_NullDescription_Throws()
        {
            Assert.Throws<ArgumentException>(() => new IdentityPolicy(false, false, null));
        }

        [Test]
        public void IdentityPolicy_EmptyDescription_Throws()
        {
            Assert.Throws<ArgumentException>(() => new IdentityPolicy(false, true, string.Empty));
        }

        [Test]
        public void IdentityPolicy_GuestFirst_CollectionFalse()
        {
            var p = IdentityPolicy.GuestFirst("desc");
            Assert.IsFalse(p.CollectionAllowed);
            Assert.IsFalse(p.RequireExplicitSignup);
        }

        [Test]
        public void IdentityPolicy_Default_MatchesSpec()
        {
            var p = IdentityPolicy.Default;
            Assert.IsFalse(p.RequireExplicitSignup);
            Assert.IsFalse(p.CollectionAllowed);
            Assert.IsFalse(string.IsNullOrEmpty(p.PolicyDescription));
        }

        // --- AnonymousIdentityGate.CanCollectData ---

        private static readonly IdentityPolicy Permissive = new IdentityPolicy(false, true, "allow");

        private static ConsentRecord Consent(string gameId) =>
            ConsentRecord.Create(gameId, ConsentBasis.ExplicitConsent);

        private static readonly IdentityPolicy[] AllConstructiblePolicies =
            { IdentityPolicy.Default, IdentityPolicy.GuestFirst("g"), new IdentityPolicy(true, true, "allow"), Permissive, default(IdentityPolicy) };

        [Test]
        public void CanCollectData_WonderPop_AlwaysIneligible_EvenIfPolicyAllows()
        {
            var result = AnonymousIdentityGate.CanCollectData("Wonder_Pop", GuestIdentity.Create("Wonder_Pop"), Consent("Wonder_Pop"), Permissive);
            Assert.IsFalse(result.IsEligible);
            Assert.AreEqual("WonderPopCollectionProhibited", result.Reason);
        }

        [Test]
        public void CanCollectData_WonderPop_CaseSensitive_OtherCaseIsNotBlocked()
        {
            // "wonder_pop" (lowercase) is NOT Wonder_Pop — ordinal check
            var result = AnonymousIdentityGate.CanCollectData("wonder_pop", GuestIdentity.Create("wonder_pop"), Consent("wonder_pop"), Permissive);
            Assert.IsTrue(result.IsEligible);
        }

        [Test]
        public void CanCollectData_PolicyNotAllowed_Ineligible()
        {
            var result = AnonymousIdentityGate.CanCollectData("OtherGame", GuestIdentity.Create("OtherGame"), Consent("OtherGame"), IdentityPolicy.Default);
            Assert.IsFalse(result.IsEligible);
            Assert.AreEqual("CollectionNotAllowed", result.Reason);
        }

        [Test]
        public void CanCollectData_AdultWithExplicitConsent_PermissivePolicy_Eligible()
        {
            var result = AnonymousIdentityGate.CanCollectData("OtherGame", GuestIdentity.Create("OtherGame"), Consent("OtherGame"), Permissive);
            Assert.IsTrue(result.IsEligible);
            Assert.AreEqual("Eligible", result.Reason);
        }

        [Test]
        public void CanCollectData_ChildIdentity_IneligibleUnderEveryPolicy()
        {
            var child = GuestIdentity.CreateChild("OtherGame");
            foreach (var policy in AllConstructiblePolicies)
            {
                var result = AnonymousIdentityGate.CanCollectData("OtherGame", child, Consent("OtherGame"), policy);
                Assert.IsFalse(result.IsEligible);
                Assert.AreEqual("ChildCollectionProhibited", result.Reason);
            }
        }

        [Test]
        public void CanCollectData_NoOrRevokedConsent_IneligibleUnderPermissivePolicy()
        {
            var adult = GuestIdentity.Create("OtherGame");
            var noConsent = new[]
            {
                ConsentRecord.Create("OtherGame", ConsentBasis.NoneRequired),
                ConsentRecord.Create("OtherGame", ConsentBasis.ChildSafe),
                ConsentRecord.Create("OtherGame", ConsentBasis.LegitimateInterest),
                ConsentRecord.Revoke(Consent("OtherGame")),
            };
            foreach (var consent in noConsent)
            {
                var result = AnonymousIdentityGate.CanCollectData("OtherGame", adult, consent, Permissive);
                Assert.IsFalse(result.IsEligible);
                Assert.AreEqual("ConsentRequired", result.Reason);
            }
        }

        [Test]
        public void CanCollectData_DefaultGuestIdentity_FailsClosed()
        {
            // default(GuestIdentity) has GameId=null -> GameIdMismatch for any real game; with a matching
            // GameId but empty key it is treated as unknown/child.
            var r1 = AnonymousIdentityGate.CanCollectData("OtherGame", default(GuestIdentity), Consent("OtherGame"), Permissive);
            Assert.IsFalse(r1.IsEligible);
            Assert.AreEqual("GameIdMismatch", r1.Reason);
            Assert.IsTrue(default(GuestIdentity).IsEmpty);
        }

        [Test]
        public void CanCollectData_DefaultStructPolicy_FailsClosed()
        {
            var result = AnonymousIdentityGate.CanCollectData("OtherGame", GuestIdentity.Create("OtherGame"), Consent("OtherGame"), default(IdentityPolicy));
            Assert.IsFalse(result.IsEligible);
            Assert.AreEqual("CollectionNotAllowed", result.Reason);
        }

        [Test]
        public void CanCollectData_GameIdMismatch_Blocked()
        {
            var r1 = AnonymousIdentityGate.CanCollectData("Coral_Rescue", GuestIdentity.Create("Wonder_Pop"), Consent("Coral_Rescue"), Permissive);
            Assert.IsFalse(r1.IsEligible);
            Assert.AreEqual("GameIdMismatch", r1.Reason);
            var r2 = AnonymousIdentityGate.CanCollectData("Coral_Rescue", GuestIdentity.Create("Coral_Rescue"), Consent("Wonder_Pop"), Permissive);
            Assert.IsFalse(r2.IsEligible);
            Assert.AreEqual("GameIdMismatch", r2.Reason);
        }

        [Test]
        public void CanCollectData_WonderPopParam_RecordDiffers_GameIdMismatch()
        {
            var r1 = AnonymousIdentityGate.CanCollectData("Wonder_Pop", GuestIdentity.Create("Coral_Rescue"), Consent("Wonder_Pop"), Permissive);
            Assert.AreEqual("GameIdMismatch", r1.Reason);
            var r2 = AnonymousIdentityGate.CanCollectData("Wonder_Pop", GuestIdentity.Create("Wonder_Pop"), Consent("Coral_Rescue"), Permissive);
            Assert.AreEqual("GameIdMismatch", r2.Reason);
        }

        [Test]
        public void CanCollectData_ObsoleteOverload_FailsClosed()
        {
#pragma warning disable 0618
            var r = AnonymousIdentityGate.CanCollectData("OtherGame", Permissive);
            var wp = AnonymousIdentityGate.CanCollectData("Wonder_Pop", Permissive);
#pragma warning restore 0618
            Assert.IsFalse(r.IsEligible);
            Assert.AreEqual("ConsentRequired", r.Reason);
            Assert.IsFalse(wp.IsEligible);
            Assert.AreEqual("WonderPopCollectionProhibited", wp.Reason);
        }

        // --- RequiresGuestMode / IsChildRestricted ---

        [Test]
        public void RequiresGuestMode_TrueWhenNoExplicitSignup()
        {
            Assert.IsTrue(AnonymousIdentityGate.RequiresGuestMode(IdentityPolicy.Default));
        }

        [Test]
        public void RequiresGuestMode_FalseWhenSignupRequired()
        {
            var policy = new IdentityPolicy(true, false, "signup required");
            Assert.IsFalse(AnonymousIdentityGate.RequiresGuestMode(policy));
        }

        [Test]
        public void IsChildRestricted_TrueForChildIdentity()
        {
            var child = GuestIdentity.CreateChild("g");
            Assert.IsTrue(AnonymousIdentityGate.IsChildRestricted(child));
        }

        [Test]
        public void IsChildRestricted_FalseForNormalIdentity()
        {
            var normal = GuestIdentity.Create("g");
            Assert.IsFalse(AnonymousIdentityGate.IsChildRestricted(normal));
        }

        // --- GuestIdentityStore ---

        [Test]
        public void Store_And_TryGet_RoundTrip()
        {
            var store = new GuestIdentityStore();
            var id = GuestIdentity.Create("g1");
            store.Store(id);
            Assert.IsTrue(store.TryGet("g1", out var retrieved));
            Assert.AreEqual(id.LocalKey, retrieved.LocalKey);
        }

        [Test]
        public void Store_Clear_ResetsCount()
        {
            var store = new GuestIdentityStore();
            store.Store(GuestIdentity.Create("a"));
            store.Store(GuestIdentity.Create("b"));
            store.Clear();
            Assert.AreEqual(0, store.Count);
        }

        [Test]
        public void Store_TryGet_MissingKey_ReturnsFalse()
        {
            var store = new GuestIdentityStore();
            Assert.IsFalse(store.TryGet("missing", out _));
        }


        [Test]
        public void NullOrEmptyGameId_DefaultRecord_GameIdMissing()
        {
            foreach (var gameId in new[] { null, string.Empty })
            {
                var result = AnonymousIdentityGate.CanCollectData(gameId, default(GuestIdentity), default(ConsentRecord), default(IdentityPolicy));
                Assert.IsFalse(result.IsEligible);
                Assert.AreEqual("GameIdMissing", result.Reason);
            }
        }
    }
}
