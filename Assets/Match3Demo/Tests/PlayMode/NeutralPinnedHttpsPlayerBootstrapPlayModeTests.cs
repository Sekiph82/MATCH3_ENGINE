using System;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Match3.Engine.Demo.Tests
{
    public sealed class NeutralPinnedHttpsPlayerBootstrapPlayModeTests
    {
        [Test]
        public void M11_013_DefaultHttpsPlayerIsDormantAndDoesNotHijackStandaloneDemo()
        {
            var go = new GameObject("unsigned-standalone-dormant");
            try
            {
                var game = go.AddComponent<NeutralMatch3Demo>();
                go.AddComponent<NeutralPatchBootstrapView>();
                var host = go.AddComponent<NeutralPatchSessionHost>();
                var player = go.AddComponent<NeutralPinnedHttpsPlayerBootstrap>();

                Assert.That(game.IsSignedContentRequired, Is.False);
                Assert.That(player.CanRetryDiscovery, Is.False);
                Assert.That(player.IsChecking, Is.False);
                Assert.That(host.HasConfiguredCandidate, Is.False);
                Assert.That(host.AttemptCount, Is.Zero);
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }

        [UnityTest]
        public IEnumerator M11_013_UnconfiguredOptInHttpsSceneFailsClosedWithoutNetwork()
        {
            var go = new GameObject("opt-in-first-party-no-credentials");
            go.SetActive(false);
            try
            {
                var game = go.AddComponent<NeutralMatch3Demo>();
                var view = go.AddComponent<NeutralPatchBootstrapView>();
                var host = go.AddComponent<NeutralPatchSessionHost>();
                var player = go.AddComponent<NeutralPinnedHttpsPlayerBootstrap>();
                var flag = typeof(NeutralPinnedHttpsPlayerBootstrap).GetField(
                    "enableSignedUpdates", BindingFlags.Instance |
                    BindingFlags.NonPublic);
                Assert.That(flag, Is.Not.Null);
                flag.SetValue(player, true);
                go.SetActive(true);
                yield return null;
                Assert.That(game.IsSignedContentRequired, Is.True);
                Assert.That(game.IsBoardInputAllowed, Is.False);
                Assert.That(view.CanEnterGame, Is.False);
                Assert.That(host.AttemptCount, Is.Zero);
                Assert.That(host.HasConfiguredCandidate, Is.False);
                Assert.That(player.CanRetryDiscovery, Is.True);
                Assert.That(player.RequestNewSignedDiscovery(), Is.True);
                Assert.That(host.AttemptCount, Is.Zero,
                    "Missing pinned public trust must never start HTTPS.");
                Assert.That(view.CanEnterGame, Is.False);
                Assert.That(game.IsBoardInputAllowed, Is.False);
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }
    }
}
