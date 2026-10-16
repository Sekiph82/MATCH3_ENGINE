using System;
using System.Collections;
using System.Reflection;
using System.Security.Cryptography;
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
        public IEnumerator M11_013_ExpiredPinnedTrustDoesNotOfferUnusableUpdateRetry()
        {
            var go = new GameObject("expired-first-party-public-trust");
            go.SetActive(false);
            using (var signer = new RSACryptoServiceProvider(2048))
            {
                try
                {
                    var game = go.AddComponent<NeutralMatch3Demo>();
                    var view = go.AddComponent<NeutralPatchBootstrapView>();
                    var host = go.AddComponent<NeutralPatchSessionHost>();
                    var player = go.AddComponent<NeutralPinnedHttpsPlayerBootstrap>();
                    var flags = BindingFlags.NonPublic | BindingFlags.Instance;
                    var type = typeof(NeutralPinnedHttpsPlayerBootstrap);
                    var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                    type.GetField("enableSignedUpdates", flags).SetValue(player, true);
                    type.GetField("approvedHttpsPrefix", flags).SetValue(
                        player, "https://signed.qa.invalid/pinned/");
                    type.GetField("trustedPublicKeyId", flags).SetValue(
                        player, "expired-public");
                    type.GetField("trustedRsaModulusBase64", flags).SetValue(
                        player, Convert.ToBase64String(
                            signer.ExportParameters(false).Modulus));
                    type.GetField("keyValidFromUnixSeconds", flags).SetValue(
                        player, now - 86400 * 3L);
                    type.GetField("keyValidUntilUnixSeconds", flags).SetValue(
                        player, now - 86400 * 2L);
                    go.SetActive(true);
                    yield return null;
                    Assert.That(game.IsSignedContentRequired, Is.True);
                    Assert.That(game.IsBoardInputAllowed, Is.False);
                    Assert.That(view.CanEnterGame, Is.False);
                    Assert.That(host.AttemptCount, Is.Zero);
                    Assert.That(player.CanRetryDiscovery, Is.False);
                    Assert.That(player.RequestNewSignedDiscovery(), Is.False);
                    Assert.That(player.PlayerStatus,
                        Is.EqualTo("Signed update is not configured"));
                }
                finally { UnityEngine.Object.DestroyImmediate(go); }
            }
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
                Assert.That(player.CanRetryDiscovery, Is.False,
                    "An absent RSA trust anchor is not a retryable network error.");
                Assert.That(player.PlayerStatus,
                    Is.EqualTo("Signed update is not configured"));
                Assert.That(player.RequestNewSignedDiscovery(), Is.False);
                Assert.That(host.AttemptCount, Is.Zero,
                    "Missing pinned public trust must never start HTTPS.");
                Assert.That(view.CanEnterGame, Is.False);
                Assert.That(game.IsBoardInputAllowed, Is.False);
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }
    }
}
