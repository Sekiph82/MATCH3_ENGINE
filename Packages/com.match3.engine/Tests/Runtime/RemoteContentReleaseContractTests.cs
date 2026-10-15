using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Match3.Engine.Runtime;
using NUnit.Framework;

namespace Match3.Engine.Tests
{
    public sealed class RemoteContentReleaseContractTests
    {
        private static readonly byte[] Payload = Encoding.UTF8.GetBytes("{\"level\":1}");
        private static RemoteContentManifest Manifest(long sequence, string release,
            string game = "Coral_Rescue") =>
            new RemoteContentManifest(game, RemotePlatform.Android,
                RemoteChannel.FamilyTest, release, "key-v1",
                new EngineVersion(1, 0, 0), new EngineVersion(1, 2, 0),
                1000, 9000, new[] {
                    new RemoteContentEntry("levels/first.json",
                        RemoteContentKind.Json, Payload.Length,
                        RemoteContentSafety.Sha256(Payload))
                }, sequence: sequence);

        private static RemoteVerifiedRelease Stage(RSA signer,
            long sequence, string release, string game = "Coral_Rescue")
        {
            var manifest = Manifest(sequence, release, game);
            var publicKey = signer.ExportParameters(false);
            var trust = new RemotePublicSigningKey("key-v1",
                publicKey.Modulus, publicKey.Exponent, 0, 10000);
            var sig = signer.SignData(manifest.CanonicalBytes(),
                HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            return RemoteContentStager.VerifyComplete(manifest, sig,
                new[] { trust }, game, RemotePlatform.Android,
                RemoteChannel.FamilyTest, new EngineVersion(1, 0, 0), 2000,
                new Dictionary<string, byte[]> {
                    { "levels/first.json", Payload }
                });
        }

        [Test]
        public void M11_004_IncrementalCacheRequiresExactBytesAndVerifiedDigest()
        {
            var entry = Manifest(1, "v1").Entries[0];
            Assert.That(RemoteContentStager.CanSkipDownload(entry, Payload),
                Is.True);
            Assert.That(RemoteContentStager.CanSkipDownload(entry, null),
                Is.False);
            Assert.That(RemoteContentStager.CanSkipDownload(
                entry, Encoding.UTF8.GetBytes("{\"level\":2}")), Is.False);
            Assert.That(RemoteContentStager.CanSkipDownload(
                entry, new byte[0]), Is.False);
            Assert.That(RemoteContentSafety.Matches(Payload,
                entry.Length, new string('0', 64)), Is.False);
        }

        [Test]
        public void M11_005_SignedCompleteStageRefusesCorruptionMissingAndExtras()
        {
            using (var rsa = new RSACryptoServiceProvider(2048))
            {
                var good = Stage(rsa, 1, "v1");
                Assert.That(good.Manifest.ReleaseId, Is.EqualTo("v1"));
                Assert.That(good.FileDigests.Count, Is.EqualTo(1));
                var manifest = Manifest(1, "v1");
                var p = rsa.ExportParameters(false);
                var trust = new RemotePublicSigningKey("key-v1",
                    p.Modulus, p.Exponent, 0, 10000);
                var sig = rsa.SignData(manifest.CanonicalBytes(),
                    HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                Assert.Throws<InvalidOperationException>(() =>
                    RemoteContentStager.VerifyComplete(manifest, sig,
                        new[] { trust }, manifest.GameId, manifest.Platform,
                        manifest.Channel, new EngineVersion(1, 0, 0), 2000,
                        new Dictionary<string, byte[]>()));
                Assert.Throws<InvalidOperationException>(() =>
                    RemoteContentStager.VerifyComplete(manifest, sig,
                        new[] { trust }, manifest.GameId, manifest.Platform,
                        manifest.Channel, new EngineVersion(1, 0, 0), 2000,
                        new Dictionary<string, byte[]> {
                            { "levels/first.json", new byte[Payload.Length] }
                        }));
                Assert.Throws<InvalidOperationException>(() =>
                    RemoteContentStager.VerifyComplete(manifest, sig,
                        new[] { trust }, manifest.GameId, manifest.Platform,
                        manifest.Channel, new EngineVersion(1, 0, 0), 2000,
                        new Dictionary<string, byte[]> {
                            { "levels/first.json", Payload },
                            { "levels/evil.dll", new byte[] {0x4d, 0x5a} }
                        }));
                Assert.Throws<InvalidOperationException>(() =>
                    RemoteContentStager.VerifyComplete(manifest,
                        new byte[sig.Length],
                        new[] { trust }, manifest.GameId, manifest.Platform,
                        manifest.Channel, new EngineVersion(1, 0, 0), 2000,
                        new Dictionary<string, byte[]> {
                            { "levels/first.json", Payload }
                        }));
            }
        }

        [Test]
        public void M11_006_VerifiedOfflineSelectionHasFailClosedEmptyState()
        {
            using (var rsa = new RSACryptoServiceProvider(2048))
            {
                var starter = Stage(rsa, 1, "starter");
                var state = new RemoteReleaseState("Coral_Rescue",
                    RemotePlatform.Android, RemoteChannel.FamilyTest);
                Assert.That(RemoteReleasePlanner.SelectOffline(state, starter),
                    Is.SameAs(starter));
                Assert.Throws<InvalidOperationException>(() =>
                    RemoteReleasePlanner.SelectOffline(state, null));
                Assert.Throws<InvalidOperationException>(() =>
                    RemoteReleasePlanner.SelectOffline(state,
                        Stage(rsa, 1, "foreign", "Wonder_Pop")));
            }
        }

        [Test]
        public void M11_007_SignedSequencePreventsDowngradeAndRecoverKeepsHighWater()
        {
            using (var rsa = new RSACryptoServiceProvider(2048))
            {
                var state = new RemoteReleaseState("Coral_Rescue",
                    RemotePlatform.Android, RemoteChannel.FamilyTest);
                var first = Stage(rsa, 10, "v10");
                var plan = RemoteReleasePlanner.Promote(state, first);
                Assert.That(plan.Applied, Is.True);
                Assert.That(plan.Result.HighestSequence, Is.EqualTo(10));
                Assert.That(RemoteReleasePlanner.Promote(
                    plan.Result, first).Applied, Is.False);
                var current = Stage(rsa, 11, "v11");
                var raised = RemoteReleasePlanner.Promote(plan.Result, current);
                Assert.That(raised.Result.PreviousValid, Is.SameAs(first));
                Assert.Throws<InvalidOperationException>(() =>
                    RemoteReleasePlanner.Promote(raised.Result,
                        Stage(rsa, 9, "v9")));
                Assert.Throws<InvalidOperationException>(() =>
                    RemoteReleasePlanner.Promote(raised.Result,
                        Stage(rsa, 11, "same-sequence-new-payload")));
                Assert.Throws<InvalidOperationException>(() =>
                    RemoteReleasePlanner.RecoverFromInvalidActive(
                        raised.Result, "incorrect-release"));
                var rollback = RemoteReleasePlanner.RecoverFromInvalidActive(
                    raised.Result, "v11");
                Assert.That(rollback.Result.Active, Is.SameAs(first));
                Assert.That(rollback.Result.HighestSequence, Is.EqualTo(11),
                    "Rollback must not lower the anti-downgrade high-water.");
                Assert.Throws<InvalidOperationException>(() =>
                    RemoteReleasePlanner.Promote(rollback.Result, current));
                var corrected = RemoteReleasePlanner.Promote(rollback.Result,
                    Stage(rsa, 12, "v12"));
                Assert.That(corrected.Applied, Is.True);
            }
        }

        [Test]
        public void M11_013_PatchStatesShowOnlyVerifiedBoundedProgress()
        {
            var download = new RemotePatchDisplay(
                RemotePatchDisplayKind.Downloading, 20, 100, false);
            Assert.That(download.CompletedPercent, Is.EqualTo(20));
            Assert.That(download.MayRetry, Is.False);
            Assert.That(download.TextKey, Is.EqualTo("patch.downloading"));
            var failed = new RemotePatchDisplay(
                RemotePatchDisplayKind.Failed, 0, 100, true);
            Assert.That(failed.MayRetry, Is.True);
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new RemotePatchDisplay(RemotePatchDisplayKind.Downloading,
                    101, 100, false));
            Assert.Throws<ArgumentException>(() =>
                new RemotePatchDisplay(RemotePatchDisplayKind.Downloading,
                    0, 0, false));
            var restored = new RemotePatchDisplay(
                RemotePatchDisplayKind.Restored, 0, 0, false);
            Assert.That(restored.MayRetry, Is.False);
        }

        [Test]
        public void M11_014_UnverifiedRemoteMetadataAndChildOfferAreBlocked()
        {
            var wonder = new GameProfile("Wonder_Pop", "neutral.wonder",
                ProfileAgeRule.General, ProfileContentChannel.Development);
            var eventCommand = new SignedRemoteMetaCommand(
                "Wonder_Pop", "signed-source", RemoteMetaKind.Event,
                "season.1", true);
            using (var signer = new RSACryptoServiceProvider(2048))
            {
                var m = new RemoteContentManifest("Wonder_Pop",
                    RemotePlatform.Android, RemoteChannel.FamilyTest,
                    "signed-meta", "key-v1", new EngineVersion(1, 0, 0),
                    new EngineVersion(1, 2, 0), 1000, 9000,
                    Manifest(1, "x").Entries, metaCommands: new [] {
                        eventCommand, new SignedRemoteMetaCommand(
                            "Wonder_Pop", "signed-offer", RemoteMetaKind.Offer,
                            "offer.1", true)
                    });
                var key = signer.ExportParameters(false);
                var pinned = new RemotePublicSigningKey("key-v1",
                    key.Modulus, key.Exponent, 0, 10000);
                var signature = signer.SignData(m.CanonicalBytes(),
                    HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                var verified = RemoteContentStager.VerifyComplete(m, signature,
                    new [] { pinned }, "Wonder_Pop", RemotePlatform.Android,
                    RemoteChannel.FamilyTest, new EngineVersion(1, 0, 0), 2000,
                    new Dictionary<string, byte[]> {
                        { "levels/first.json", Payload }
                    });
                Assert.Throws<InvalidOperationException>(() =>
                    RemoteMetaPolicy.Allow(wonder, eventCommand,
                        null, true, true));
                Assert.That(RemoteMetaPolicy.Allow(wonder, eventCommand,
                    verified, false, false), Is.False);
                var offer = new SignedRemoteMetaCommand(
                    "Wonder_Pop", "signed-offer", RemoteMetaKind.Offer,
                    "offer.1", true);
                Assert.That(RemoteMetaPolicy.Allow(wonder, offer,
                    verified, true, true), Is.False);
                var forged = new SignedRemoteMetaCommand(
                    "Wonder_Pop", "forged", RemoteMetaKind.Offer,
                    "offer.1", true);
                Assert.Throws<InvalidOperationException>(() =>
                    RemoteMetaPolicy.Allow(wonder, forged,
                        verified, true, true));
                var adult = new GameProfile("Coral_Rescue", "neutral.coral",
                    ProfileAgeRule.General, ProfileContentChannel.Development);
                Assert.Throws<InvalidOperationException>(() =>
                    RemoteMetaPolicy.Allow(adult, offer,
                        verified, true, true));
                var altered = new SignedRemoteMetaCommand(
                    "Wonder_Pop", "signed-offer", RemoteMetaKind.Offer,
                    "offer.1", false);
                Assert.Throws<InvalidOperationException>(() =>
                    RemoteMetaPolicy.Allow(wonder, altered,
                        verified, true, true));
            }
        }
    }
}
