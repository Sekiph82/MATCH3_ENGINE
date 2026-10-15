using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Match3.Engine.Runtime;
using NUnit.Framework;

namespace Match3.Engine.Tests
{
    public sealed class RemoteManifestCodecAndLevelTests
    {
        private static readonly byte[] Payload = Encoding.UTF8.GetBytes(
            "{\"level\":1}");

        private static RemoteContentManifest Manifest(string game, int disabled)
        {
            return new RemoteContentManifest(
                game, RemotePlatform.Android, RemoteChannel.FamilyTest,
                "v001", "public-key", new EngineVersion(1, 0, 0),
                new EngineVersion(1, 2, 0), 1000, 5000,
                new[] { new RemoteContentEntry("levels/chapter.json",
                    RemoteContentKind.Json, Payload.Length,
                    RemoteContentSafety.Sha256(Payload)) },
                sequence: 1, metaCommands: new[] {
                    new SignedRemoteMetaCommand(game, "disable." + disabled,
                        RemoteMetaKind.DisabledLevel, "level." + disabled, false)
                });
        }

        private static RemotePublicSigningKey Trust(RSA signer)
        {
            var k = signer.ExportParameters(false);
            return new RemotePublicSigningKey(
                "public-key", k.Modulus, k.Exponent, 0, 10000);
        }

        private static byte[] Sign(RSA signer, RemoteContentManifest m) =>
            signer.SignData(m.CanonicalBytes(), HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1);

        [Test]
        public void M11_001_CanonicalWireRoundTripRejectsNoncanonicalOrMalformedInput()
        {
            var manifest = Manifest("Coral_Rescue", 11);
            var wire = manifest.CanonicalBytes();
            var reread = RemoteManifestCodec.ParseCanonical(wire);
            Assert.That(reread.GameId, Is.EqualTo(manifest.GameId));
            Assert.That(reread.Sequence, Is.EqualTo(1));
            Assert.That(reread.Entries[0].Path, Is.EqualTo("levels/chapter.json"));
            Assert.That(reread.MetaCommands[0].NeutralId, Is.EqualTo("level.11"));
            Assert.That(reread.CanonicalBytes(), Is.EqualTo(wire));
            Assert.Throws<InvalidOperationException>(() =>
                RemoteManifestCodec.ParseCanonical(
                    Encoding.UTF8.GetBytes(
                        Encoding.UTF8.GetString(wire).Replace(
                            "MATCH3-CONTENT-V1\n1\n", "MATCH3-CONTENT-V1\n01\n"))));
            Assert.Throws<InvalidOperationException>(() =>
                RemoteManifestCodec.ParseCanonical(
                    wire.Take(wire.Length - 1).ToArray()));
            Assert.Throws<InvalidOperationException>(() =>
                RemoteManifestCodec.ParseCanonical(
                    Encoding.UTF8.GetBytes(
                        Encoding.UTF8.GetString(wire) + "extra\n")));
            Assert.Throws<InvalidOperationException>(() =>
                RemoteManifestCodec.ParseCanonical(new byte[] { 0xc0, 0xaf }));
        }

        [Test]
        public void M11_002_VerifiedWireRejectsTamperingAndWrongGame()
        {
            using (var rsa = RSA.Create())
            {
                rsa.KeySize = 2048;
                var m = Manifest("Coral_Rescue", 11);
                var sig = Sign(rsa, m);
                var key = Trust(rsa);
                var approved = RemoteManifestCodec.ParseVerified(
                    m.CanonicalBytes(), sig, new[] { key },
                    m.GameId, m.Platform, m.Channel,
                    new EngineVersion(1, 0, 0), 2000);
                Assert.That(approved.ReleaseId, Is.EqualTo("v001"));
                Assert.Throws<InvalidOperationException>(() =>
                    RemoteManifestCodec.ParseVerified(
                        Manifest("Coral_Rescue", 12).CanonicalBytes(),
                        sig, new[] { key }, m.GameId, m.Platform,
                        m.Channel, new EngineVersion(1, 0, 0), 2000));
                Assert.Throws<InvalidOperationException>(() =>
                    RemoteManifestCodec.ParseVerified(
                        m.CanonicalBytes(), sig, new[] { key },
                        "Wonder_Pop", m.Platform, m.Channel,
                        new EngineVersion(1, 0, 0), 2000));
            }
        }

        [Test]
        public void M11_012_DisabledNeighborProjectionRequiresSignedVerifiedProfile()
        {
            using (var rsa = RSA.Create())
            {
                rsa.KeySize = 2048;
                foreach (var game in GameProfileCatalog.Ids)
                {
                    var m = Manifest(game, 2);
                    var verified = RemoteContentStager.VerifyComplete(
                        m, Sign(rsa, m), new[] { Trust(rsa) }, game,
                        m.Platform, m.Channel, new EngineVersion(1, 0, 0),
                        2000, new Dictionary<string, byte[]> {
                            { "levels/chapter.json", Payload }
                        });
                    var profile = new GameProfile(game, "neutral." + game,
                        ProfileAgeRule.General,
                        ProfileContentChannel.Development);
                    var blocked = RemoteDisabledLevelPlanner.Evaluate(
                        profile, verified, 2, new[] { 1, 3, 4 });
                    Assert.That(blocked.Disabled, Is.True);
                    Assert.That(blocked.ReasonKey,
                        Is.EqualTo("level.remote_disabled"));
                    Assert.That(blocked.CanGrantReward, Is.False);
                    Assert.That(blocked.CanForgeCompletedState, Is.False);
                    var neighbor = RemoteDisabledLevelPlanner.Evaluate(
                        profile, verified, 1, new[] { 2, 3 });
                    Assert.That(neighbor.PlayableNeighborIds,
                        Is.EqualTo(new[] { 3 }),
                        "Signed disabled neighbors cannot be suggested.");
                    Assert.Throws<InvalidOperationException>(() =>
                        RemoteDisabledLevelPlanner.Evaluate(
                            new GameProfile(
                                game == "Wonder_Pop" ? "Coral_Rescue" : "Wonder_Pop",
                                "neutral", ProfileAgeRule.General,
                                ProfileContentChannel.Development),
                            verified, 2, new[] { 3 }));
                    Assert.Throws<ArgumentException>(() =>
                        RemoteDisabledLevelPlanner.Evaluate(
                            profile, verified, 2, new[] { 1, 1 }));
                }
            }
        }

        [Test]
        public void M11_012_NewSignedManifestCanReenableWithoutForgingRewards()
        {
            using (var rsa = RSA.Create())
            {
                rsa.KeySize = 2048;
                var m = new RemoteContentManifest(
                    "Coral_Rescue", RemotePlatform.Android,
                    RemoteChannel.FamilyTest, "v2", "public-key",
                    new EngineVersion(1, 0, 0),
                    new EngineVersion(1, 2, 0), 1000, 5000,
                    Manifest("Coral_Rescue", 2).Entries, sequence: 2,
                    metaCommands: new[] {
                        new SignedRemoteMetaCommand("Coral_Rescue",
                            "reenable.2", RemoteMetaKind.DisabledLevel,
                            "level.2", true)
                    });
                var approved = RemoteContentStager.VerifyComplete(
                    m, Sign(rsa, m), new[] { Trust(rsa) }, m.GameId,
                    m.Platform, m.Channel, new EngineVersion(1, 0, 0),
                    2000, new Dictionary<string, byte[]> {
                        { "levels/chapter.json", Payload }
                    });
                var p = new GameProfile(m.GameId, "neutral",
                    ProfileAgeRule.General, ProfileContentChannel.Development);
                var allowed = RemoteDisabledLevelPlanner.Evaluate(
                    p, approved, 2, new[] { 3 });
                Assert.That(allowed.Disabled, Is.False);
                Assert.That(allowed.CanGrantReward, Is.False);
            }
        }
    }
}
