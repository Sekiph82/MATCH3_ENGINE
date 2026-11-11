using System;
using NUnit.Framework;
using Match3.Engine.Runtime.Identity;

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

        [Test]
        public void CanCollectData_WonderPop_AlwaysIneligible_EvenIfPolicyAllows()
        {
            var permissivePolicy = new IdentityPolicy(false, true, "allow");
            var result = AnonymousIdentityGate.CanCollectData("Wonder_Pop", permissivePolicy);
            Assert.IsFalse(result.IsEligible);
            Assert.AreEqual("WonderPopCollectionProhibited", result.Reason);
        }

        [Test]
        public void CanCollectData_WonderPop_CaseSensitive_OtherCaseIsNotBlocked()
        {
            var permissivePolicy = new IdentityPolicy(false, true, "allow");
            // "wonder_pop" (lowercase) is NOT Wonder_Pop — ordinal check
            var result = AnonymousIdentityGate.CanCollectData("wonder_pop", permissivePolicy);
            Assert.IsTrue(result.IsEligible);
        }

        [Test]
        public void CanCollectData_PolicyNotAllowed_Ineligible()
        {
            var result = AnonymousIdentityGate.CanCollectData("OtherGame", IdentityPolicy.Default);
            Assert.IsFalse(result.IsEligible);
            Assert.AreEqual("CollectionNotAllowed", result.Reason);
        }

        [Test]
        public void CanCollectData_Eligible_WhenPolicyAllowsNonWonderPop()
        {
            var policy = new IdentityPolicy(false, true, "allow");
            var result = AnonymousIdentityGate.CanCollectData("OtherGame", policy);
            Assert.IsTrue(result.IsEligible);
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
    }
}
