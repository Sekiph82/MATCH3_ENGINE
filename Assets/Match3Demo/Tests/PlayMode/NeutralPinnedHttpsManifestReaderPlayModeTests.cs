using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Match3.Engine.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Match3.Engine.Demo.Tests
{
    // HTTPS discovery and RSA validation tests use an INJECTED, finite,
    // in-memory transport. No external network request is made by this file.
    public sealed class NeutralPinnedHttpsManifestReaderPlayModeTests
    {
        private const string Game = "Coral_Rescue";
        private const RemotePlatform Platform = RemotePlatform.Windows;
        private const RemoteChannel Channel = RemoteChannel.Development;
        private static readonly EngineVersion Engine = new EngineVersion(1, 0, 0);

        private sealed class ReceiptTransport : IRemoteReadTransport
        {
            private readonly Dictionary<string, byte[]> _receipts =
                new Dictionary<string, byte[]>(StringComparer.Ordinal);
            public readonly List<string> RequestedPaths = new List<string>();
            public bool RejectSignature;
            public ReceiptTransport(byte[] manifest, byte[] signature)
            {
                _receipts.Add(NeutralPinnedHttpsManifestReader.ManifestPath,
                    (byte[])manifest.Clone());
                _receipts.Add(NeutralPinnedHttpsManifestReader.SignaturePath,
                    (byte[])signature.Clone());
            }

            public Task<RemoteReadResponse> FetchAsync(
                Uri uri, long maxBytes, CancellationToken token)
            {
                token.ThrowIfCancellationRequested();
                Assert.That(uri.Scheme, Is.EqualTo("https"));
                Assert.That(uri.Host, Is.EqualTo("signed.qa.invalid"));
                Assert.That(uri.AbsolutePath, Does.StartWith("/pinned/"));
                var path = uri.AbsolutePath.Substring("/pinned/".Length);
                RequestedPaths.Add(path);
                byte[] bytes;
                if (!_receipts.TryGetValue(path, out bytes))
                    throw new InvalidOperationException(
                        "Payload fetch must not happen before signature verification.");
                Assert.That(bytes.LongLength, Is.LessThanOrEqualTo(maxBytes));
                return Task.FromResult(new RemoteReadResponse(
                    RejectSignature && path == NeutralPinnedHttpsManifestReader.SignaturePath
                        ? 503 : 200, (byte[])bytes.Clone()));
            }
        }

        private static RemoteContentManifest SignedManifest(long now)
        {
            var bytes = Encoding.UTF8.GetBytes("{\"neutral\":true}");
            return new RemoteContentManifest(Game, Platform, Channel,
                "https-qa", "public-test", Engine, new EngineVersion(1, 1, 0),
                now - 60, now + 3600,
                new[] { new RemoteContentEntry("levels/neutral.json",
                    RemoteContentKind.Json, bytes.Length,
                    RemoteContentSafety.Sha256(bytes)) });
        }

        private static RemotePublicSigningKey PublicKey(
            RSACryptoServiceProvider signer, long now)
        {
            var rsa = signer.ExportParameters(false);
            return new RemotePublicSigningKey("public-test",
                rsa.Modulus, rsa.Exponent, now - 60, now + 3600);
        }

        private static byte[] Sign(
            RSACryptoServiceProvider signer, RemoteContentManifest manifest)
        {
            return signer.SignData(manifest.CanonicalBytes(),
                HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        }

        private static IEnumerator WaitFor(Task task)
        {
            var deadline = Time.realtimeSinceStartup + 30f;
            while (!task.IsCompleted)
            {
                if (Time.realtimeSinceStartup >= deadline)
                    Assert.Fail("Signed discovery did not finish within 30 seconds.");
                yield return null;
            }
        }

        [UnityTest]
        public IEnumerator M11_013_PinnedHttpsCandidateVerifiesBeforeDisclosingSignature()
        {
            using (var signer = new RSACryptoServiceProvider(2048))
            {
                var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                var manifest = SignedManifest(now);
                var signature = Sign(signer, manifest);
                var transport = new ReceiptTransport(manifest.CanonicalBytes(),
                    signature);
                var task = NeutralPinnedHttpsManifestReader.FetchVerifiedAsync(
                    new RemoteReadOrigin("https://signed.qa.invalid/pinned/"),
                    transport, new[] { PublicKey(signer, now) },
                    Game, Platform, Channel, Engine, now, CancellationToken.None);
                yield return WaitFor(task);
                Assert.That(task.IsFaulted, Is.False);
                Assert.That(task.Result.Manifest.ReleaseId,
                    Is.EqualTo(manifest.ReleaseId));
                Assert.That(task.Result.Signature, Is.EqualTo(signature));
                Assert.That(transport.RequestedPaths,
                    Is.EqualTo(new[] { "manifest.canonical", "manifest.sig" }));
                var cloned = task.Result.Signature;
                cloned[0] ^= 0x80;
                Assert.That(task.Result.Signature, Is.EqualTo(signature),
                    "Caller cannot mutate a verified signature receipt.");
            }
        }

        [UnityTest]
        public IEnumerator M11_013_PinnedHttpsTamperedManifestNeverReachesPayload()
        {
            using (var signer = new RSACryptoServiceProvider(2048))
            {
                var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                var manifest = SignedManifest(now);
                var mutated = manifest.CanonicalBytes();
                var signature = Sign(signer, manifest);
                // Mutate a valid canonical token, not the detached signature.
                var encoded = Encoding.UTF8.GetString(mutated)
                    .Replace("https-qa", "https-qb");
                var transport = new ReceiptTransport(
                    Encoding.UTF8.GetBytes(encoded), signature);
                var task = NeutralPinnedHttpsManifestReader.FetchVerifiedAsync(
                    new RemoteReadOrigin("https://signed.qa.invalid/pinned/"),
                    transport, new[] { PublicKey(signer, now) },
                    Game, Platform, Channel, Engine, now, CancellationToken.None);
                yield return WaitFor(task);
                Assert.That(task.IsFaulted, Is.True);
                Assert.That(task.Exception, Is.Not.Null);
                Assert.That(transport.RequestedPaths,
                    Is.EqualTo(new[] { "manifest.canonical", "manifest.sig" }));
            }
        }

        [UnityTest]
        public IEnumerator M11_013_PinnedHttpsRejectsUnsignedWrongTitleAndExpiredClock()
        {
            using (var signer = new RSACryptoServiceProvider(2048))
            {
                var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                var manifest = SignedManifest(now);
                var signature = Sign(signer, manifest);
                var origin = new RemoteReadOrigin(
                    "https://signed.qa.invalid/pinned/");
                var wrongGame = new ReceiptTransport(manifest.CanonicalBytes(),
                    signature);
                var badGame = NeutralPinnedHttpsManifestReader.FetchVerifiedAsync(
                    origin, wrongGame, new[] { PublicKey(signer, now) },
                    "Garage_Legends", Platform, Channel, Engine, now,
                    CancellationToken.None);
                yield return WaitFor(badGame);
                Assert.That(badGame.IsFaulted, Is.True);
                Assert.That(wrongGame.RequestedPaths.Count, Is.EqualTo(2));

                var stale = SignedManifest(now - 86400);
                var staleTransport = new ReceiptTransport(
                    stale.CanonicalBytes(), Sign(signer, stale));
                var expired = NeutralPinnedHttpsManifestReader.FetchVerifiedAsync(
                    origin, staleTransport,
                    new[] { PublicKey(signer, now - 86400) },
                    Game, Platform, Channel, Engine, now - 86400,
                    CancellationToken.None);
                yield return WaitFor(expired);
                Assert.That(expired.IsFaulted, Is.True,
                    "Caller-provided stale time must not extend trust.");
                Assert.That(staleTransport.RequestedPaths.Count, Is.EqualTo(2));
            }
        }

        [UnityTest]
        public IEnumerator M11_013_PinnedHttpsRejectsHttpErrorBeforeCandidateRelease()
        {
            using (var signer = new RSACryptoServiceProvider(2048))
            {
                var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                var manifest = SignedManifest(now);
                var transport = new ReceiptTransport(
                    manifest.CanonicalBytes(), Sign(signer, manifest))
                {
                    RejectSignature = true
                };
                var task = NeutralPinnedHttpsManifestReader.FetchVerifiedAsync(
                    new RemoteReadOrigin("https://signed.qa.invalid/pinned/"),
                    transport, new[] { PublicKey(signer, now) },
                    Game, Platform, Channel, Engine, now, CancellationToken.None);
                yield return WaitFor(task);
                Assert.That(task.IsFaulted, Is.True);
                Assert.That(transport.RequestedPaths.Count, Is.EqualTo(2));
            }
        }
    }
}
