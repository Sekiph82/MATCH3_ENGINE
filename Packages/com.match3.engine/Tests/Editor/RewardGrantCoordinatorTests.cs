using System;
using NUnit.Framework;
using Match3.Engine.Runtime.Commerce;

namespace Match3.Engine.Tests.Editor
{
    public class RewardGrantCoordinatorTests
    {
        private static RewardedAdLifecycle CompletedLifecycle()
        {
            var lc = new RewardedAdLifecycle();
            lc.RequestLoad(); lc.NotifyReady(); lc.NotifyShow(); lc.NotifyCompleted();
            return lc;
        }

        private static RewardedAdLifecycle SkippedLifecycle()
        {
            var lc = new RewardedAdLifecycle();
            lc.RequestLoad(); lc.NotifyReady(); lc.NotifyShow(); lc.NotifySkipped();
            return lc;
        }

        private static RewardedAdLifecycle FailedLifecycle()
        {
            var lc = new RewardedAdLifecycle();
            lc.RequestLoad(); lc.NotifyFailed("test");
            return lc;
        }

        private static RewardTransaction MakeTx(string id = "tx1") =>
            new RewardTransaction(id, "placement1", "game1");

        [Test]
        public void Transaction_NullId_Throws()
        {
            Assert.Throws<ArgumentException>(() =>
                new RewardTransaction(null, "p", "g"));
        }

        [Test]
        public void Transaction_EmptyPlacementId_Throws()
        {
            Assert.Throws<ArgumentException>(() =>
                new RewardTransaction("tx", "", "g"));
        }

        [Test]
        public void Transaction_EmptyGameId_Throws()
        {
            Assert.Throws<ArgumentException>(() =>
                new RewardTransaction("tx", "p", ""));
        }

        [Test]
        public void Ledger_FirstRecord_ReturnsTrue()
        {
            var ledger = new InMemoryRewardLedger();
            Assert.IsTrue(ledger.TryRecord(MakeTx()));
        }

        [Test]
        public void Ledger_DuplicateRecord_ReturnsFalse()
        {
            var ledger = new InMemoryRewardLedger();
            ledger.TryRecord(MakeTx("dup"));
            Assert.IsFalse(ledger.TryRecord(MakeTx("dup")));
        }

        [Test]
        public void Ledger_Find_ReturnsNullForUnknown()
        {
            var ledger = new InMemoryRewardLedger();
            Assert.IsNull(ledger.Find("nope"));
        }

        [Test]
        public void Coordinator_NullLedger_Throws()
        {
            Assert.Throws<ArgumentNullException>(() =>
                new RewardGrantCoordinator(null));
        }

        [Test]
        public void TryGrant_CompletedLifecycle_ReturnsGranted()
        {
            var coord = new RewardGrantCoordinator(new InMemoryRewardLedger());
            var status = coord.TryGrant(MakeTx(), CompletedLifecycle());
            Assert.AreEqual(RewardGrantStatus.Granted, status);
        }

        [Test]
        public void TryGrant_SkippedLifecycle_ReturnsSkipped()
        {
            var coord = new RewardGrantCoordinator(new InMemoryRewardLedger());
            var status = coord.TryGrant(MakeTx(), SkippedLifecycle());
            Assert.AreEqual(RewardGrantStatus.Skipped, status);
        }

        [Test]
        public void TryGrant_FailedLifecycle_ReturnsSkipped()
        {
            var coord = new RewardGrantCoordinator(new InMemoryRewardLedger());
            var status = coord.TryGrant(MakeTx(), FailedLifecycle());
            Assert.AreEqual(RewardGrantStatus.Skipped, status);
        }

        [Test]
        public void TryGrant_Duplicate_ReturnsDuplicate()
        {
            var ledger = new InMemoryRewardLedger();
            var coord = new RewardGrantCoordinator(ledger);
            var tx = MakeTx("dup-tx");
            coord.TryGrant(tx, CompletedLifecycle());
            var status = coord.TryGrant(tx, CompletedLifecycle());
            Assert.AreEqual(RewardGrantStatus.Duplicate, status);
        }

        [Test]
        public void CreateTransaction_GeneratesUniqueIds()
        {
            var coord = new RewardGrantCoordinator(new InMemoryRewardLedger());
            var t1 = coord.CreateTransaction("p", "g");
            var t2 = coord.CreateTransaction("p", "g");
            Assert.AreNotEqual(t1.TransactionId, t2.TransactionId);
        }

        [Test]
        public void MarkGranted_SetsStatusAndUtc()
        {
            var tx = MakeTx();
            var utc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var granted = tx.MarkGranted(utc);
            Assert.AreEqual(RewardGrantStatus.Granted, granted.Status);
            Assert.AreEqual(utc, granted.GrantedAtUtc);
        }

        [Test]
        public void Ledger_Find_ReturnsRecordedTransaction()
        {
            var ledger = new InMemoryRewardLedger();
            var tx = MakeTx("find-me");
            ledger.TryRecord(tx);
            var found = ledger.Find("find-me");
            Assert.IsNotNull(found);
            Assert.AreEqual("find-me", found.Value.TransactionId);
        }
    }
}
