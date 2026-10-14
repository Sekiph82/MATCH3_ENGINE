using System.Collections;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Match3.Core.Structs;
using Match3.Engine.Runtime;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace Match3.Engine.Demo.Tests
{
    public sealed class DemoSdkAtomicInputBridgePlayModeTests
    {
        private static readonly BoardPosition From = new BoardPosition(1, 0);
        private static readonly BoardPosition To = new BoardPosition(2, 0);

        private static BoardTopology ReadSdkLayout(DemoGame game)
        {
            var rows = game.GetSnapshot().Split('/');
            var items = new List<BoardOccupant>();
            for (var r = 0; r < rows.Length; r++)
                for (var c = 0; c < rows[r].Length; c++)
                    if (rows[r][c] != '.')
                        items.Add(new BoardOccupant(new BoardPosition(r, c),
                            rows[r][c].ToString()));
            return new BoardTopology(rows.Length, rows[0].Length, occupants: items);
        }

        [UnityTest]
        public IEnumerator RapidDuplicateTapForwardsExactlyOneGenuineSdkMove()
        {
            var metrics = new DemoMetrics(20261013);
            var game = new DemoGame(metrics);
            game.InitializeAsync().Forget();
            yield return new UnityEngine.WaitUntil(() => metrics.InitialBoardSnapshot.Length > 0);
            var bridge = new DemoSdkAtomicInputBridge(game, metrics, ReadSdkLayout(game));

            var first = bridge.TryTapPairAsync(From, To);
            Assert.That(bridge.HasPending, Is.True);
            var duplicate = bridge.TryTapPairAsync(From, To).GetAwaiter().GetResult();
            Assert.That(duplicate.Accepted, Is.False);
            Assert.That(duplicate.Status, Is.EqualTo(InputReceiptStatus.NotReady));
            yield return new UnityEngine.WaitUntil(() => !bridge.HasPending);

            var receipt = first.GetAwaiter().GetResult();
            Assert.That(receipt.Accepted, Is.True);
            Assert.That(receipt.Sequence, Is.EqualTo(1L));
            Assert.That(metrics.MoveAccepted, Is.True);
            Assert.That(metrics.MovesRemaining, Is.EqualTo(1));
            Assert.That(metrics.Score, Is.EqualTo(130));
            Assert.That(metrics.GameFinished, Is.True);
            Assert.That(game.GetSnapshot(), Is.EqualTo("ABAB/CDBD/BBCC/DCBA"));
            Assert.That(metrics.Events, Has.Exactly(1).EqualTo("move-committed:1"));
        }

        [UnityTest]
        public IEnumerator DragAndTapBothForwardSameSdkWinningSwapWithoutRulesFork()
        {
            var firstMetrics = new DemoMetrics(20261013);
            var firstGame = new DemoGame(firstMetrics);
            firstGame.InitializeAsync().Forget();
            yield return new UnityEngine.WaitUntil(() => firstMetrics.InitialBoardSnapshot.Length > 0);
            var firstBridge = new DemoSdkAtomicInputBridge(firstGame, firstMetrics,
                ReadSdkLayout(firstGame));

            var drag = firstBridge.TryDragAsync(PointerDevice.Touch, From, To, 22f);
            yield return new UnityEngine.WaitUntil(() => !firstBridge.HasPending);
            Assert.That(drag.GetAwaiter().GetResult().Accepted, Is.True);

            var secondMetrics = new DemoMetrics(20261013);
            var secondGame = new DemoGame(secondMetrics);
            secondGame.InitializeAsync().Forget();
            yield return new UnityEngine.WaitUntil(() => secondMetrics.InitialBoardSnapshot.Length > 0);
            var secondBridge = new DemoSdkAtomicInputBridge(secondGame, secondMetrics,
                ReadSdkLayout(secondGame));

            var tap = secondBridge.TryTapPairAsync(From, To);
            yield return new UnityEngine.WaitUntil(() => !secondBridge.HasPending);
            Assert.That(tap.GetAwaiter().GetResult().Accepted, Is.True);
            Assert.That(firstGame.GetSnapshot(), Is.EqualTo(secondGame.GetSnapshot()));
            Assert.That(firstMetrics.Score, Is.EqualTo(secondMetrics.Score));
            Assert.That(firstMetrics.MovesRemaining, Is.EqualTo(secondMetrics.MovesRemaining));
            Assert.That(firstMetrics.GameFinished, Is.True);
            Assert.That(secondMetrics.GameFinished, Is.True);
        }

        [UnityTest]
        public IEnumerator ModalAndInvalidPointerDoNotInvokeOrSpendSdkSwap()
        {
            var metrics = new DemoMetrics(20261013);
            var game = new DemoGame(metrics);
            game.InitializeAsync().Forget();
            yield return new UnityEngine.WaitUntil(() => metrics.InitialBoardSnapshot.Length > 0);
            var bridge = new DemoSdkAtomicInputBridge(game, metrics, ReadSdkLayout(game));
            var initial = game.GetSnapshot();

            var modal = bridge.TryTapPairAsync(From, To, modalOpen: true).GetAwaiter().GetResult();
            var invalid = bridge.TryTapPairAsync(From, From).GetAwaiter().GetResult();
            var shortDrag = bridge.TryDragAsync(PointerDevice.Mouse, From, To, 1f)
                .GetAwaiter().GetResult();

            Assert.That(modal.Status, Is.EqualTo(InputReceiptStatus.ModalBlocked));
            Assert.That(invalid.Status, Is.EqualTo(InputReceiptStatus.Rejected));
            Assert.That(shortDrag.Status, Is.EqualTo(InputReceiptStatus.Rejected));
            Assert.That(game.GetSnapshot(), Is.EqualTo(initial));
            Assert.That(metrics.MovesRemaining, Is.EqualTo(2));
            Assert.That(metrics.Events, Does.Not.Contain("move-committed:1"));
            Assert.That(bridge.HasPending, Is.False);
        }
    }
}
