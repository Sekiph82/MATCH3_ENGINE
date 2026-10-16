using System;
using System.Reflection;
using System.Security.Cryptography;
using Match3.Engine.Runtime;
using NUnit.Framework;
using UnityEngine;

namespace Match3.Engine.Demo.Tests
{
    // Pure, read-only preflight. No HTTP request, release write, build
    // artifact or private signing key may leave this test method.
    public sealed class NeutralPinnedHttpsConfigurationEditModeTests
    {
        private const BindingFlags PrivateInstance =
            BindingFlags.Instance | BindingFlags.NonPublic;

        private static void Set(NeutralPinnedHttpsPlayerBootstrap player,
            string fieldName, object value)
        {
            var field = typeof(NeutralPinnedHttpsPlayerBootstrap)
                .GetField(fieldName, PrivateInstance);
            Assert.That(field, Is.Not.Null, "Pinned field " + fieldName);
            field.SetValue(player, value);
        }

        private static NeutralPinnedHttpsPlayerBootstrap Configure(
            GameObject root, byte[] modulus, string prefix,
            long keyStart, long keyEnd)
        {
            var player = root.AddComponent<NeutralPinnedHttpsPlayerBootstrap>();
            Set(player, "enableSignedUpdates", true);
            Set(player, "gameId", "Coral_Rescue");
            Set(player, "channel", RemoteChannel.FamilyTest);
            Set(player, "approvedHttpsPrefix", prefix);
            Set(player, "trustedPublicKeyId", "m11-r23-public");
            Set(player, "trustedRsaModulusBase64",
                Convert.ToBase64String(modulus));
            Set(player, "keyValidFromUnixSeconds", keyStart);
            Set(player, "keyValidUntilUnixSeconds", keyEnd);
            return player;
        }

        [Test]
        public void M11_013_ConfigAcceptsImportablePinnedPublicKeyWithoutNetwork()
        {
            var root = new GameObject("valid-public-key-preflight");
            root.SetActive(false);
            using (var signer = new RSACryptoServiceProvider(2048))
            {
                try
                {
                    var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                    var player = Configure(root,
                        signer.ExportParameters(false).Modulus,
                        "https://signed.qa.invalid/first-party/", now - 60, now + 3600);
                    Assert.DoesNotThrow(() =>
                        player.RequireValidPinnedConfiguration(
                            RemotePlatform.Windows, now));
                    Assert.That(player.SignedUpdatesEnabled, Is.True);
                    Assert.That(player.IsChecking, Is.False);
                    Assert.That(player.HasVerifiedPlay, Is.False);
                }
                finally { UnityEngine.Object.DestroyImmediate(root); }
            }
        }

        [Test]
        public void M11_013_ConfigRejectsExpiredTrustBeforeHttpsOrPlayerBuild()
        {
            var root = new GameObject("expired-public-key-preflight");
            root.SetActive(false);
            using (var signer = new RSACryptoServiceProvider(2048))
            {
                try
                {
                    var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                    var player = Configure(root,
                        signer.ExportParameters(false).Modulus,
                        "https://signed.qa.invalid/first-party/",
                        now - 7200, now - 3600);
                    Assert.Throws<InvalidOperationException>(() =>
                        player.RequireValidPinnedConfiguration(
                            RemotePlatform.Windows, now));
                    Assert.That(player.IsChecking, Is.False);
                }
                finally { UnityEngine.Object.DestroyImmediate(root); }
            }
        }

        [Test]
        public void M11_013_ConfigRejectsExpiredTrustEvenWithStaleCallerClock()
        {
            var root = new GameObject("stale-clock-public-trust");
            root.SetActive(false);
            using (var signer = new RSACryptoServiceProvider(2048))
            {
                try
                {
                    var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                    var player = Configure(root,
                        signer.ExportParameters(false).Modulus,
                        "https://signed.qa.invalid/first-party/",
                        now - 7200, now - 3600);
                    Assert.Throws<InvalidOperationException>(() =>
                        player.RequireValidPinnedConfiguration(
                            RemotePlatform.Windows, now - 86400));
                    Assert.That(player.IsChecking, Is.False);
                }
                finally { UnityEngine.Object.DestroyImmediate(root); }
            }
        }

        [Test]
        public void M11_013_ConfigRejectsUntrustedHttpOriginAndMalformedRsaKey()
        {
            var root = new GameObject("bad-origin-preflight");
            root.SetActive(false);
            using (var signer = new RSACryptoServiceProvider(2048))
            {
                try
                {
                    var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                    var player = Configure(root,
                        signer.ExportParameters(false).Modulus,
                        "http://signed.qa.invalid/first-party/",
                        now - 60, now + 3600);
                    Assert.Throws<ArgumentException>(() =>
                        player.RequireValidPinnedConfiguration(
                            RemotePlatform.Windows, now));
                    Set(player, "approvedHttpsPrefix",
                        "https://signed.qa.invalid/first-party/");
                    Set(player, "trustedRsaModulusBase64", "bad-base64!!!");
                    Assert.Throws<InvalidOperationException>(() =>
                        player.RequireValidPinnedConfiguration(
                            RemotePlatform.Windows, now));
                    Set(player, "trustedRsaModulusBase64",
                        new string('A', 2049));
                    Assert.Throws<InvalidOperationException>(() =>
                        player.RequireValidPinnedConfiguration(
                            RemotePlatform.Windows, now));
                }
                finally { UnityEngine.Object.DestroyImmediate(root); }
            }
        }

        [Test]
        public void M11_013_ConfigCannotValidateSignedUpdatesWhenOptInIsDisabled()
        {
            var root = new GameObject("default-off-public-trust");
            root.SetActive(false);
            try
            {
                var player = root.AddComponent<NeutralPinnedHttpsPlayerBootstrap>();
                Assert.That(player.SignedUpdatesEnabled, Is.False);
                Assert.Throws<InvalidOperationException>(() =>
                    player.RequireValidPinnedConfiguration(
                        RemotePlatform.Windows,
                        DateTimeOffset.UtcNow.ToUnixTimeSeconds()));
                Assert.That(player.HasVerifiedPlay, Is.False);
                Assert.That(player.IsChecking, Is.False);
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }
    }
}
