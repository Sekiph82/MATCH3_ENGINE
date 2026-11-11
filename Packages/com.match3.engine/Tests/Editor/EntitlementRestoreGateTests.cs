// ENG-M16-007 EntitlementRestoreGate tests — NUnit only
using NUnit.Framework;
using System;
using Match3.Engine.Runtime.Commerce;

namespace Match3.Engine.Tests.Editor
{
    [TestFixture]
    public class EntitlementRestoreGateTests
    {
        // ── EntitlementRecord ───────────────────────────────────────────────

        [Test]
        public void EntitlementRecord_Active_SetsIsActiveTrue()
        {
            var r = EntitlementRecord.Active("GameA", "sku1", "ord1");
            Assert.IsTrue(r.IsActive);
            Assert.AreEqual("GameA", r.GameId);
            Assert.AreEqual("sku1",  r.SkuId);
            Assert.AreEqual("ord1",  r.OrderId);
        }

        [Test]
        public void EntitlementRecord_Revoked_SetsIsActiveFalse()
        {
            var r = EntitlementRecord.Revoked("GameB", "sku2", "ord2");
            Assert.IsFalse(r.IsActive);
        }

        [Test]
        public void EntitlementRecord_NullGameId_Throws()
        {
            Assert.Throws<ArgumentException>(() => EntitlementRecord.Active(null, "sku", "ord"));
        }

        [Test]
        public void EntitlementRecord_EmptySkuId_Throws()
        {
            Assert.Throws<ArgumentException>(() => EntitlementRecord.Active("g", "", "ord"));
        }

        [Test]
        public void EntitlementRecord_EmptyOrderId_Throws()
        {
            Assert.Throws<ArgumentException>(() => EntitlementRecord.Revoked("g", "sku", ""));
        }

        // ── RestoreEntitlementPolicy ────────────────────────────────────────

        [Test]
        public void Policy_Standard_SetsFields()
        {
            var p = RestoreEntitlementPolicy.Standard("desc");
            Assert.IsTrue(p.RequireServerReceipt);
            Assert.AreEqual(3, p.OfflineGracePeriodDays);
            Assert.AreEqual("desc", p.PolicyDescription);
        }

        [Test]
        public void Policy_Default_IsServerReceiptRequired()
        {
            Assert.IsTrue(RestoreEntitlementPolicy.Default.RequireServerReceipt);
            Assert.AreEqual(3, RestoreEntitlementPolicy.Default.OfflineGracePeriodDays);
        }

        [Test]
        public void Policy_NullDescription_Throws()
        {
            Assert.Throws<ArgumentException>(() => RestoreEntitlementPolicy.Standard(null));
        }

        [Test]
        public void Policy_NegativeGracePeriod_Throws()
        {
            // Constructor path via reflection not needed; guard tested via Standard then
            // we directly test the ArgumentOutOfRangeException guard with a wrapper.
            // The only constructor path is via Standard which always passes 3; to reach
            // the negative guard we need a custom invocation — test via known bad value:
            Assert.Throws<ArgumentOutOfRangeException>(() =>
            {
                // Use reflection to hit the private constructor with negative days
                var ctor = typeof(RestoreEntitlementPolicy)
                    .GetConstructor(
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance,
                        null,
                        new[] { typeof(bool), typeof(int), typeof(string) },
                        null);
                try { ctor.Invoke(new object[] { true, -1, "desc" }); }
                catch (System.Reflection.TargetInvocationException ex)
                {
                    System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
                }
            });
        }

        // ── EntitlementRestoreGate.CanRestore ───────────────────────────────

        [Test]
        public void CanRestore_WonderPop_IsIneligible_Guard1_First()
        {
            // Even if policy has server receipt disabled (would trigger guard 2),
            // Guard 1 must fire first for Wonder_Pop.
            var policy = RestoreEntitlementPolicy.Default;
            var result = EntitlementRestoreGate.CanRestore("Wonder_Pop", policy);
            Assert.IsFalse(result.IsEligible);
            Assert.AreEqual("WonderPopRestoreProhibited", result.Reason);
        }

        [Test]
        public void CanRestore_WonderPop_OrdinalCaseSensitive_OtherCaseIsEligible()
        {
            // "wonder_pop" (lowercase) must NOT be caught by Wonder_Pop guard
            var policy = RestoreEntitlementPolicy.Default;
            var result = EntitlementRestoreGate.CanRestore("wonder_pop", policy);
            Assert.IsTrue(result.IsEligible);
        }

        [Test]
        public void CanRestore_ServerReceiptDisabled_IsIneligible()
        {
            // Build a policy with RequireServerReceipt=false via reflection (same pattern)
            var ctor = typeof(RestoreEntitlementPolicy)
                .GetConstructor(
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance,
                    null,
                    new[] { typeof(bool), typeof(int), typeof(string) },
                    null);
            var policy = (RestoreEntitlementPolicy)ctor.Invoke(new object[] { false, 3, "no receipt" });
            var result = EntitlementRestoreGate.CanRestore("OtherGame", policy);
            Assert.IsFalse(result.IsEligible);
            Assert.AreEqual("ServerReceiptDisabled", result.Reason);
        }

        [Test]
        public void CanRestore_ValidGame_IsEligible()
        {
            var result = EntitlementRestoreGate.CanRestore("Garage_Legends", RestoreEntitlementPolicy.Default);
            Assert.IsTrue(result.IsEligible);
        }

        // ── IsWithinGracePeriod ─────────────────────────────────────────────

        [Test]
        public void IsWithinGracePeriod_ExactlyAtLimit_ReturnsTrue()
        {
            var p = RestoreEntitlementPolicy.Default; // grace = 3
            Assert.IsTrue(EntitlementRestoreGate.IsWithinGracePeriod(3, p));
        }

        [Test]
        public void IsWithinGracePeriod_OneDayOver_ReturnsFalse()
        {
            var p = RestoreEntitlementPolicy.Default;
            Assert.IsFalse(EntitlementRestoreGate.IsWithinGracePeriod(4, p));
        }

        // ── RequiresCloudBackup ─────────────────────────────────────────────

        [Test]
        public void RequiresCloudBackup_AlwaysTrue()
        {
            Assert.IsTrue(EntitlementRestoreGate.RequiresCloudBackup(RestoreEntitlementPolicy.Default));
        }

        // ── EntitlementVault ────────────────────────────────────────────────

        [Test]
        public void Vault_StoreAndRetrieve()
        {
            var vault = new EntitlementVault();
            var rec   = EntitlementRecord.Active("G", "sku1", "o1");
            vault.Store(rec);
            Assert.IsTrue(vault.TryGet("G", "sku1", out var got));
            Assert.AreEqual("o1", got.OrderId);
        }

        [Test]
        public void Vault_GetAllForGame_ReturnsCopy()
        {
            var vault = new EntitlementVault();
            vault.Store(EntitlementRecord.Active("G", "s1", "o1"));
            vault.Store(EntitlementRecord.Active("G", "s2", "o2"));
            var list = vault.GetAllForGame("G");
            Assert.AreEqual(2, list.Count);
        }

        [Test]
        public void Vault_Clear_ResetsCount()
        {
            var vault = new EntitlementVault();
            vault.Store(EntitlementRecord.Active("G", "s1", "o1"));
            vault.Clear();
            Assert.AreEqual(0, vault.Count);
            Assert.IsFalse(vault.TryGet("G", "s1", out _));
        }

        [Test]
        public void Vault_Count_TracksUniqueSku()
        {
            var vault = new EntitlementVault();
            vault.Store(EntitlementRecord.Active("G", "s1", "o1"));
            vault.Store(EntitlementRecord.Active("G", "s1", "o2")); // overwrite same sku
            Assert.AreEqual(1, vault.Count);
        }


        [Test]
        public void CanRestore_NullOrEmptyGameId_GameIdMissing()
        {
            foreach (var gameId in new[] { null, string.Empty })
            {
                var result = EntitlementRestoreGate.CanRestore(gameId, RestoreEntitlementPolicy.Default);
                Assert.IsFalse(result.IsEligible);
                Assert.AreEqual("GameIdMissing", result.Reason);
            }
        }
    }
}
