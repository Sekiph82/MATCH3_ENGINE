using System;
using Match3.Engine.Runtime;
using NUnit.Framework;

namespace Match3.Engine.Tests
{
    public sealed class TutorialCheckpointContractTests
    {
        [Test]
        public void M10_006_RestoredMidLevelStepDoesNotDuplicatePresentation()
        {
            var catalog = FtueCatalog.ThreeLevel("Coral_Rescue");
            var started = FtuePlanner.Apply(catalog,
                new FtueSnapshot(catalog.GameId, catalog.Version),
                "intro", "show-1", FtueReceiptKind.Presented, 0);
            var completed = FtuePlanner.Apply(catalog, started.Result,
                "intro", "done-1", FtueReceiptKind.Completed, 0);
            var ready = FtuePlanner.Apply(catalog, completed.Result,
                "swap", "show-2", FtueReceiptKind.Presented, 1);
            Assert.That(ready.Applied, Is.True);
            var persistent = FtueCheckpointCodec.Write(ready.Result);
            var restored = FtueCheckpointCodec.Restore(catalog, persistent);
            Assert.That(restored.Presented, Does.Contain("swap"));
            Assert.That(restored.Completed, Does.Not.Contain("swap"));
            Assert.That(FtuePlanner.Apply(catalog, restored,
                "swap", "show-2", FtueReceiptKind.Presented, 1).Applied,
                Is.False);
            Assert.That(FtuePlanner.Apply(catalog, restored,
                "swap", "show-another", FtueReceiptKind.Presented, 1).Applied,
                Is.False);
            var finish = FtuePlanner.Apply(catalog, restored,
                "swap", "done-2", FtueReceiptKind.Completed, 1);
            Assert.That(finish.Applied, Is.True);
            var reread = FtueCheckpointCodec.Restore(
                catalog, FtueCheckpointCodec.Write(finish.Result));
            Assert.That(reread.Completed, Does.Contain("swap"));
            Assert.That(reread.Receipts.Count, Is.EqualTo(4));
        }

        [Test]
        public void M10_006_CorruptedCrossProfileOrForeignSchemaFailClosed()
        {
            var catalog = FtueCatalog.ThreeLevel("Coral_Rescue");
            var saved = FtueCheckpointCodec.Write(
                new FtueSnapshot(catalog.GameId, catalog.Version));
            Assert.Throws<InvalidOperationException>(() =>
                FtueCheckpointCodec.Restore(
                    FtueCatalog.ThreeLevel("Wonder_Pop"), saved));
            Assert.Throws<InvalidOperationException>(() =>
                FtueCheckpointCodec.Read(new SaveEnvelope(2, saved.Payload),
                    catalog.GameId));
            var broken = saved.Payload.Substring(0, saved.Payload.Length - 1) + "z";
            Assert.Throws<InvalidOperationException>(() =>
                FtueCheckpointCodec.Restore(
                    catalog, new SaveEnvelope(1, broken)));
            var future = FtueCheckpointCodec.Write(
                new FtueSnapshot(catalog.GameId, 2));
            Assert.Throws<InvalidOperationException>(() =>
                FtueCheckpointCodec.Restore(catalog, future));
            Assert.Throws<ArgumentException>(() =>
                new FtueSnapshot(catalog.GameId, 1, new [] {
                    new FtueReceipt("x", "intro", FtueReceiptKind.Presented),
                    new FtueReceipt("y", "intro", FtueReceiptKind.Presented)
                }));
            // A self-consistent checksum is not proof of a valid event history.
            var forgedFutureStep = new FtueSnapshot(catalog.GameId, 1, new [] {
                new FtueReceipt("synthetic.special", "special",
                    FtueReceiptKind.Presented)
            });
            Assert.Throws<InvalidOperationException>(() =>
                FtueCheckpointCodec.Restore(catalog,
                    FtueCheckpointCodec.Write(forgedFutureStep)));
        }
    }
}
