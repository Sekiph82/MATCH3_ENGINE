using System.Collections;
using Cysharp.Threading.Tasks;
using Match3.Core.Structs;
using Match3.Engine.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Match3.Engine.Demo.Tests
{
    public sealed class DemoSdkReplayPlayModeTests
    {
        [UnityTest]
        public IEnumerator ActualSdkMoveRoundTripsThroughCanonicalReplayAndReproducesWin()
        {
            var configHash = ReplayCodec.BoardHash("demo:4x4;level:1;version:1");
            var firstMetrics = new DemoMetrics(20261013);
            var first = new DemoGame(firstMetrics);
            first.InitializeAsync().Forget();
            yield return new WaitUntil(() => firstMetrics.InitialBoardSnapshot.Length > 0);
            var started = first.GetSnapshot();
            first.TrySwapAsync(new GridPosition(1, 0), new GridPosition(2, 0)).Forget();
            yield return new WaitUntil(() => !first.IsBusy);

            Assert.That(firstMetrics.MoveAccepted, Is.True);
            var trace = new ReplayTrace("Coral_Rescue", configHash, 20261013u, new[]
            {
                new ReplayStep(1, new BoardPosition(1, 0), new BoardPosition(2, 0),
                    ReplayCodec.BoardHash(first.GetSnapshot()))
            });
            var originalBytes = ReplayCodec.Encode(trace);
            var restored = ReplayCodec.Decode(originalBytes, "Coral_Rescue", configHash);

            // Crucially, ReplayCodec does NOT run a second resolver. Replay
            // applies exactly the recorded input through the original SDK.
            var secondMetrics = new DemoMetrics((int)restored.Seed);
            var second = new DemoGame(secondMetrics);
            second.InitializeAsync().Forget();
            yield return new WaitUntil(() => secondMetrics.InitialBoardSnapshot.Length > 0);
            Assert.That(second.GetSnapshot(), Is.EqualTo(started));

            var action = restored.Steps[0];
            second.TrySwapAsync(new GridPosition(action.From.Row, action.From.Column),
                new GridPosition(action.To.Row, action.To.Column)).Forget();
            yield return new WaitUntil(() => !second.IsBusy);

            ReplayCodec.VerifySdkSettledSnapshots(restored, new[] { second.GetSnapshot() });
            Assert.That(second.GetSnapshot(), Is.EqualTo(first.GetSnapshot()));
            Assert.That(secondMetrics.Score, Is.EqualTo(firstMetrics.Score));
            Assert.That(secondMetrics.MovesRemaining, Is.EqualTo(firstMetrics.MovesRemaining));
            Assert.That(secondMetrics.CascadeCount, Is.EqualTo(firstMetrics.CascadeCount));
            Assert.That(secondMetrics.GoalAchieved, Is.True);
            Assert.That(secondMetrics.GameFinished, Is.True);
            CollectionAssert.AreEqual(originalBytes, ReplayCodec.Encode(restored));
        }

        [UnityTest]
        public IEnumerator WrongSdkBoardOrGameConfigCannotSatisfyReplayReceipt()
        {
            var configHash = ReplayCodec.BoardHash("demo:4x4;level:1;version:1");
            var metrics = new DemoMetrics(20261013);
            var game = new DemoGame(metrics);
            game.InitializeAsync().Forget();
            yield return new WaitUntil(() => metrics.InitialBoardSnapshot.Length > 0);
            game.TrySwapAsync(new GridPosition(1, 0), new GridPosition(2, 0)).Forget();
            yield return new WaitUntil(() => !game.IsBusy);

            var trace = new ReplayTrace("Coral_Rescue", configHash, 20261013u, new[]
            {
                new ReplayStep(1, new BoardPosition(1, 0), new BoardPosition(2, 0),
                    ReplayCodec.BoardHash(game.GetSnapshot()))
            });
            var bytes = ReplayCodec.Encode(trace);
            Assert.Throws<System.InvalidOperationException>(() =>
                ReplayCodec.Decode(bytes, "Coral_Rescue", ReplayCodec.BoardHash("changed rules")));
            Assert.Throws<System.InvalidOperationException>(() =>
                ReplayCodec.Decode(bytes, "Dino_Islands", configHash));
            Assert.Throws<System.InvalidOperationException>(() =>
                ReplayCodec.VerifySdkSettledSnapshots(trace,
                    new[] { metrics.InitialBoardSnapshot }));
            // Rejected replay does not alter the genuine settled SDK board.
            Assert.That(metrics.MovesRemaining, Is.EqualTo(1));
            Assert.That(metrics.GameFinished, Is.True);
        }
    }
}
