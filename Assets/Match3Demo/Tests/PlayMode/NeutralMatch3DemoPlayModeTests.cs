using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Cysharp.Threading.Tasks;
using Match3.Core.Structs;
using Match3.Engine.Runtime;
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
            // The shipped neutral scene includes a dormant signed patch
            // viewer. No fake session should block the current demo board.
            var patchView = Object.FindAnyObjectByType<NeutralPatchBootstrapView>();
            Assert.That(patchView, Is.Not.Null);
            Assert.That(patchView.HasVerifiedSession, Is.False);
            Assert.That(patchView.CanEnterGame, Is.False);

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
        public IEnumerator LockedAdjacentSwapLeavesBoardAndMovesUntouched()
        {
            // The exact same move that wins on the unlocked board must be
            // rejected when the source cell is locked. No rules fork.
            var metrics = new DemoMetrics(20261013);
            var game = new DemoGame(metrics, new GridPosition(1, 0));
            game.InitializeAsync().Forget();
            yield return new WaitUntil(() => metrics.InitialBoardSnapshot.Length > 0);

            var before = game.GetSnapshot();
            var movesBefore = metrics.MovesRemaining;
            Assert.That(before, Is.EqualTo("CDCB/ABCD/BAAC/DCBA"));
            Assert.That(game.IsBusy, Is.False);

            game.TrySwapAsync(new GridPosition(1, 0), new GridPosition(2, 0)).Forget();
            yield return null;

            Assert.That(game.IsBusy, Is.False);
            Assert.That(game.GetSnapshot(), Is.EqualTo(before));
            Assert.That(metrics.MovesRemaining, Is.EqualTo(movesBefore));
            Assert.That(metrics.MoveAccepted, Is.False);
            Assert.That(metrics.GameFinished, Is.False);
            Assert.That(metrics.Events, Does.Not.Contain("move-committed:1"));
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

        [UnityTest]
        public IEnumerator ReadOnlyAdapterObservesRealSdkSwapAndCannotEditBoard()
        {
            var metrics = new DemoMetrics(20261013);
            var game = new DemoGame(metrics);
            game.InitializeAsync().Forget();
            yield return new WaitUntil(() => metrics.InitialBoardSnapshot.Length > 0);

            IReadOnlyMatch3Outcome reader = new DemoSdkReadAdapter(game, metrics);
            Assert.That(reader.BoardSnapshot, Is.EqualTo(game.GetSnapshot()));
            Assert.That(reader.MovesRemaining, Is.EqualTo(2));
            Assert.That(typeof(IReadOnlyMatch3Outcome).GetProperties().All(p => !p.CanWrite), Is.True);
            Assert.That(typeof(IReadOnlyMatch3Outcome).GetMethods()
                .All(m => m.IsSpecialName && m.Name.StartsWith("get_")), Is.True);

            game.TrySwapAsync(new GridPosition(1, 0), new GridPosition(2, 0)).Forget();
            yield return new WaitUntil(() => !game.IsBusy);

            Assert.That(metrics.MoveAccepted, Is.True);
            Assert.That(reader.BoardSnapshot, Is.EqualTo(game.GetSnapshot()));
            Assert.That(reader.BoardSnapshot, Is.Not.EqualTo(metrics.InitialBoardSnapshot));
            Assert.That(reader.MovesRemaining, Is.EqualTo(1));
            Assert.That(reader.GoalAchieved, Is.True);
            Assert.That(reader.GameFinished, Is.True);
            Assert.That(reader.Score, Is.EqualTo(130));
            Assert.Throws<System.ArgumentNullException>(() => new DemoSdkReadAdapter(null, metrics));
            Assert.Throws<System.ArgumentNullException>(() => new DemoSdkReadAdapter(game, null));
        }

        [UnityTest]
        public IEnumerator AlternateSeedRunsTheUncannedRefillPath()
        {
            const int alternateSeed = 20261014;
            var metrics = new DemoMetrics(alternateSeed);
            var game = new DemoGame(metrics);
            game.InitializeAsync().Forget();
            yield return new WaitUntil(() => metrics.InitialBoardSnapshot.Length > 0);

            Assert.That(metrics.Seed, Is.EqualTo(alternateSeed));
            Assert.That(game.GetSnapshot(), Is.EqualTo("CDCB/ABCD/BAAC/DCBA"));

            game.TrySwapAsync(new GridPosition(1, 0), new GridPosition(2, 0)).Forget();
            yield return new WaitUntil(() => !game.IsBusy);

            Assert.That(metrics.MoveAccepted, Is.True);
            Assert.That(metrics.InitialMatchedItemCount, Is.EqualTo(3));
            Assert.That(metrics.GoalAchieved, Is.True);
            Assert.That(metrics.SettledSnapshots, Is.Not.Empty);
            Assert.That(metrics.Events, Does.Contain("score-wave:0:3:30:total-30"));
        }

        [UnityTest]
        public IEnumerator SameSlotSwapIsRejectedWithoutChangingBoardOrMoves()
        {
            yield return LoadDemoScene();
            var demo = Object.FindAnyObjectByType<NeutralMatch3Demo>();
            Assert.That(demo, Is.Not.Null);

            var before = demo.BoardSnapshot;
            var movesBefore = demo.Metrics.MovesRemaining;
            demo.TrySwapAsync(new GridPosition(0, 0), new GridPosition(0, 0)).Forget();
            yield return null;

            Assert.That(demo.BoardSnapshot, Is.EqualTo(before));
            Assert.That(demo.Metrics.MovesRemaining, Is.EqualTo(movesBefore));
            Assert.That(demo.Metrics.MoveAccepted, Is.False);
        }

        [UnityTest]
        public IEnumerator M11_013_VisibleReadyAndBoardSwapsRequireVerifiedSession()
        {
            yield return LoadDemoScene();
            var demo = Object.FindAnyObjectByType<NeutralMatch3Demo>();
            var view = Object.FindAnyObjectByType<NeutralPatchBootstrapView>();
            Assert.That(demo, Is.Not.Null);
            Assert.That(view, Is.Not.Null);
            Assert.That(demo.IsReady, Is.True);
            Assert.That(demo.IsPlayable, Is.True);

            demo.RequireSignedPrelaunch(view);
            Assert.That(demo.IsSignedContentRequired, Is.True);
            Assert.That(demo.IsPlayable, Is.False);
            Assert.That(demo.VisibleBoardCellCount, Is.Zero);
            Assert.That(demo.IsBoardInputAllowed, Is.False);
            Assert.That(demo.VisibleGameplayStatus, Does.StartWith("Locked:"));
            Assert.That(demo.VisibleGameplayStatus, Does.Not.Contain("Ready"));
            var before = demo.BoardSnapshot;
            var movesBefore = demo.Metrics.MovesRemaining;

            var blocked = demo.TrySwapAsync(new GridPosition(1, 0),
                new GridPosition(2, 0)).AsTask();
            while (!blocked.IsCompleted)
                yield return null;
            Assert.That(blocked.IsFaulted, Is.False);
            Assert.That(blocked.Result, Is.False);
            Assert.That(demo.BoardSnapshot, Is.EqualTo(before));
            Assert.That(demo.Metrics.MovesRemaining, Is.EqualTo(movesBefore));

            // Only actual signed staged level bytes may unlock this demo.
            // RSA private material lives for this test only, never in a player.
            var bytes = Encoding.UTF8.GetBytes("{\"neutral\":\"test\"}");
            using (var rsa = new RSACryptoServiceProvider(2048))
            {
                var manifest = new RemoteContentManifest(
                    "Coral_Rescue", RemotePlatform.Windows,
                    RemoteChannel.FamilyTest, "visible-demo-qa",
                    "ephemeral-public", new EngineVersion(1, 0, 0),
                    new EngineVersion(1, 1, 0), 1000, 9000,
                    new[] { new RemoteContentEntry(
                        "levels/neutral.json", RemoteContentKind.Json,
                        bytes.Length, RemoteContentSafety.Sha256(bytes)) });
                var parameters = rsa.ExportParameters(false);
                var key = new RemotePublicSigningKey(
                    "ephemeral-public", parameters.Modulus,
                    parameters.Exponent, 0, 10000);
                var signature = rsa.SignData(manifest.CanonicalBytes(),
                    HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                var verified = RemoteContentStager.VerifyComplete(
                    manifest, signature, new[] { key },
                    manifest.GameId, manifest.Platform, manifest.Channel,
                    new EngineVersion(1, 0, 0), 2000,
                    new Dictionary<string, byte[]> {
                        { "levels/neutral.json", bytes }
                    });
                NeutralSignedStarterPrelaunch.BindVerifiedOffline(
                    view, verified, new[] { key }, manifest.GameId,
                    manifest.Platform, manifest.Channel,
                    new EngineVersion(1, 0, 0), 2000);
                Assert.That(view.CanEnterGame, Is.True);
                Assert.That(demo.IsPlayable, Is.True);
                Assert.That(demo.VisibleBoardCellCount, Is.EqualTo(16));
                Assert.That(demo.VisibleGameplayStatus, Is.EqualTo("Ready"));
            }

            var accepted = demo.TrySwapAsync(new GridPosition(1, 0),
                new GridPosition(2, 0)).AsTask();
            while (!accepted.IsCompleted)
                yield return null;
            Assert.That(accepted.IsFaulted, Is.False);
            Assert.That(accepted.Result, Is.True);
            Assert.That(demo.Metrics.GameFinished, Is.True);
            Assert.That(demo.Metrics.MovesRemaining, Is.EqualTo(1));
            Assert.That(demo.VisibleGameplayStatus, Does.StartWith("WIN"));
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
