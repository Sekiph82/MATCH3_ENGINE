using System.Collections;
using System.Linq;
using Cysharp.Threading.Tasks;
using Match3.Core.Structs;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Match3.Engine.Demo.Tests
{
    public sealed class NeutralMatch3DemoPlayModeTests
    {
        [UnityTest]
        public IEnumerator InvalidAdjacentSwapRollsBackWithoutSpendingAMove()
        {
            yield return LoadDemoScene();
            var demo = Object.FindAnyObjectByType<NeutralMatch3Demo>();
            Assert.That(demo, Is.Not.Null);

            var before = demo.BoardSnapshot;
            var movesBefore = demo.Metrics.MovesRemaining;
            demo.TrySwapAsync(new GridPosition(0, 0), new GridPosition(0, 1)).Forget();
            yield return new WaitUntil(() => !demo.IsBusy);

            Assert.That(demo.BoardSnapshot, Is.EqualTo(before));
            Assert.That(demo.Metrics.MovesRemaining, Is.EqualTo(movesBefore));
            Assert.That(demo.Metrics.Events, Does.Contain("swap-rolled-back"));
            Assert.That(demo.Metrics.GameFinished, Is.False);
        }

        [UnityTest]
        public IEnumerator SeededAdjacentSwapClearsRefillsCascadesAndWins()
        {
            yield return LoadDemoScene();
            var demo = Object.FindAnyObjectByType<NeutralMatch3Demo>();
            Assert.That(demo, Is.Not.Null);
            Assert.That(demo.Metrics.Seed, Is.EqualTo(20261013));
            Assert.That(demo.BoardSnapshot, Is.EqualTo("CDCB/ABCD/BAAC/DCBA"));
            Assert.That(demo.Metrics.InitialBoardHash, Is.EqualTo("6a8d250a2a588ab46987df9b5d72871d2b5ddb512d47a510a3e9fa1a3a26fd54"));

            demo.TrySwapAsync(new GridPosition(1, 0), new GridPosition(2, 0)).Forget();
            yield return new WaitUntil(() => !demo.IsBusy);

            Assert.That(demo.Metrics.MoveAccepted, Is.True);
            Assert.That(demo.Metrics.InitialMatchedItemCount, Is.EqualTo(3));
            Assert.That(demo.Metrics.CascadeCount, Is.EqualTo(1));
            Assert.That(demo.Metrics.GoalAchieved, Is.True);
            Assert.That(demo.Metrics.GameFinished, Is.True);
            Assert.That(demo.Metrics.MovesRemaining, Is.EqualTo(1));
            Assert.That(demo.Metrics.Score, Is.EqualTo(130));
            Assert.That(demo.BoardSnapshot, Is.EqualTo("ABAB/CDBD/BBCC/DCBA"));
            Assert.That(demo.Metrics.FinalBoardHash, Is.EqualTo("e1cba6fb1b18f0a4bd3c060ed84e56f75b081c7873c62abf8077d5a7bbe3fcf6"));
            Assert.That(demo.Metrics.Events, Does.Contain("move-committed:1"));
            Assert.That(demo.Metrics.Events.Last(), Is.EqualTo("game-finished"));
            Assert.That(demo.Metrics.SettledSnapshots.Count, Is.EqualTo(2));
            Assert.That(demo.Metrics.SettledSnapshots[0], Is.EqualTo("CCCB/CDCD/BBCC/DCBA"));
            Assert.That(demo.Metrics.SettledSnapshots[1], Is.EqualTo("ABAB/CDBD/BBCC/DCBA"));
        }

        private static IEnumerator LoadDemoScene()
        {
            var load = SceneManager.LoadSceneAsync("NeutralMatch3Demo", LoadSceneMode.Single);
            Assert.That(load, Is.Not.Null, "The neutral demo scene must be in EditorBuildSettings.");
            while (!load.isDone)
            {
                yield return null;
            }

            var ready = Object.FindAnyObjectByType<NeutralMatch3Demo>();
            Assert.That(ready, Is.Not.Null);
            yield return new WaitUntil(() => ready.IsReady);
        }
    }
}
