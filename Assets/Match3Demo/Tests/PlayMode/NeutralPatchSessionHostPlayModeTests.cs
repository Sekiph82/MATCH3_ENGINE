using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
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
    // Tests use an explicitly SYNTHETIC injectable transport. These verify
    // actual signed Engine orchestration + genuine durable Windows storage,
    // not internet reachability or an official Addressables runtime.
    public sealed class NeutralPatchSessionHostPlayModeTests
    {
        private const string GameId = "Coral_Rescue";
        private const RemotePlatform Platform = RemotePlatform.Windows;
        private const RemoteChannel Channel = RemoteChannel.FamilyTest;
        private static readonly EngineVersion Engine = new EngineVersion(1, 0, 0);

        private sealed class ScriptedTransport : IRemoteReadTransport
        {
            private readonly byte[] _bytes;
            public bool Failing;
            public int Calls;
            public long ReturnedBytes;

            public ScriptedTransport(byte[] bytes, bool failing)
            {
                _bytes = (byte[])bytes.Clone();
                Failing = failing;
            }

            public Task<RemoteReadResponse> FetchAsync(
                Uri uri, long maxBytes, CancellationToken cancellation)
            {
                cancellation.ThrowIfCancellationRequested();
                Calls++;
                if (Failing)
                    throw new HttpRequestException("Synthetic scripted failure");
                if (_bytes.LongLength > maxBytes)
                    throw new InvalidOperationException("Signed budget exceeded");
                ReturnedBytes += _bytes.Length;
                return Task.FromResult(new RemoteReadResponse(
                    200, (byte[])_bytes.Clone()));
            }
        }

        private static byte[] Payload(int revision) => Encoding.UTF8.GetBytes(
            "{\"neutral\":true,\"revision\":" + revision + "}");

        private static RemoteContentManifest Manifest(
            byte[] bytes, int sequence, long now)
        {
            return new RemoteContentManifest(GameId, Platform, Channel,
                "m11-host-qa-" + sequence, "ephemeral-host",
                Engine, new EngineVersion(1, 1, 0), now - 60, now + 3600,
                new[] { new RemoteContentEntry("levels/start.json",
                    RemoteContentKind.Json, bytes.Length,
                    RemoteContentSafety.Sha256(bytes)) }, sequence: sequence);
        }

        private static byte[] Sign(
            RSACryptoServiceProvider rsa, RemoteContentManifest manifest)
        {
            return rsa.SignData(manifest.CanonicalBytes(),
                HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        }

        private static RemotePublicSigningKey PublicKey(
            RSACryptoServiceProvider rsa, long now)
        {
            var p = rsa.ExportParameters(false);
            return new RemotePublicSigningKey("ephemeral-host",
                p.Modulus, p.Exponent, now - 60, now + 3600);
        }

        private static GameObject NewHost(
            out NeutralMatch3Demo game, out NeutralPatchBootstrapView view,
            out NeutralPatchSessionHost host)
        {
            var root = new GameObject("signed-patch-host-integration");
            game = root.AddComponent<NeutralMatch3Demo>();
            view = root.AddComponent<NeutralPatchBootstrapView>();
            host = root.AddComponent<NeutralPatchSessionHost>();
            return root;
        }

        private static string NewRetainedTestRoot()
        {
            // Isolated durable test evidence is preserved, not automatically
            // removed or written to any existing owner's saved-game root.
            var path = Path.Combine(Application.persistentDataPath,
                "m11-host-r21-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return path;
        }

        [UnityTest]
        public IEnumerator M11_013_RealHostRetryRefetchesThenRestartsWithoutPrematureGameAccess()
        {
            using (var signer = new RSACryptoServiceProvider(2048))
            {
                var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                var payload = Payload(1);
                var manifest = Manifest(payload, 1, now);
                var key = PublicKey(signer, now);
                var signature = Sign(signer, manifest);
                var diskPath = NewRetainedTestRoot();
                var durable = new RemoteDurableActivationHost(
                    diskPath, GameId, Platform, Channel);
                var installed = durable.Restore(new[] { key }, Engine, now);
                var scripted = new ScriptedTransport(payload, true);
                var root = NewHost(out var game, out var view, out var host);
                try
                {
                    var first = host.BeginPrelaunchAsync(manifest, signature,
                        new[] { key },
                        new RemoteReadOrigin("https://synthetic.invalid/qa/"),
                        scripted, installed, null, durable, Engine, now);
                    Assert.That(game.IsSignedContentRequired, Is.True);
                    Assert.That(game.IsBoardInputAllowed, Is.False);
                    yield return new WaitUntil(() => first.IsCompleted);
                    Assert.That(first.IsFaulted, Is.False);
                    Assert.That(first.Result, Is.Not.Null);
                    Assert.That(first.Result.CanPlay, Is.False);
                    Assert.That(first.Result.Flow.Snapshot().Stage,
                        Is.EqualTo(RemotePatchStage.Failed));
                    Assert.That(view.CanEnterGame, Is.False);
                    Assert.That(view.Snapshot().CanPlay, Is.False);
                    Assert.That(view.CanRequestHostRetry, Is.True);
                    var beforeRetry = scripted.Calls;
                    Assert.That(beforeRetry, Is.GreaterThan(0));

                    scripted.Failing = false;
                    Assert.That(view.TryRequestHostRetry(), Is.True);
                    yield return new WaitUntil(() =>
                        !host.IsRunning && host.LastResult != null);
                    Assert.That(host.AttemptCount, Is.EqualTo(2));
                    Assert.That(scripted.Calls, Is.GreaterThan(beforeRetry),
                        "Retry must do a fresh verified-byte network fetch.");
                    Assert.That(scripted.ReturnedBytes,
                        Is.EqualTo(payload.Length));
                    Assert.That(host.LastResult.NewReleaseActivated, Is.True);
                    Assert.That(host.LastResult.Flow.Snapshot().Stage,
                        Is.EqualTo(RemotePatchStage.Ready));
                    Assert.That(view.CanEnterGame, Is.True);
                    Assert.That(game.IsBoardInputAllowed, Is.True);
                    Assert.That(view.HostRetryPending, Is.False);
                    Assert.That(view.TryRequestHostRetry(), Is.False);

                    var restarted = new RemoteDurableActivationHost(
                        diskPath, GameId, Platform, Channel);
                    var restored = restarted.Restore(
                        new[] { key }, Engine, now);
                    Assert.That(restored.Active, Is.Not.Null);
                    Assert.That(restored.Active.Manifest.Sequence, Is.EqualTo(1));
                    Assert.That(restored.Active.SignedEnvelopeDigest,
                        Is.EqualTo(host.LastResult.PlayableContent.SignedEnvelopeDigest));

                    // Identical signed release is a true NoUpdate: no bytes
                    // are downloaded on a new app/host instance.
                    var noUpdateTransport = new ScriptedTransport(payload, false);
                    var secondRoot = NewHost(
                        out _, out var secondView, out var secondHost);
                    try
                    {
                        var noUpdate = secondHost.BeginPrelaunchAsync(
                            manifest, signature, new[] { key },
                            new RemoteReadOrigin("https://synthetic.invalid/qa/"),
                            noUpdateTransport, restored, null, restarted,
                            Engine, now);
                        yield return new WaitUntil(() => noUpdate.IsCompleted);
                        Assert.That(noUpdate.IsFaulted, Is.False);
                        Assert.That(noUpdate.Result.CanPlay, Is.True);
                        Assert.That(noUpdate.Result.Flow.Snapshot().Stage,
                            Is.EqualTo(RemotePatchStage.NoUpdate));
                        Assert.That(noUpdateTransport.Calls, Is.Zero);
                        Assert.That(secondView.CanEnterGame, Is.True);
                    }
                    finally { UnityEngine.Object.DestroyImmediate(secondRoot); }

                    TestContext.WriteLine("R21_TRANSPORT=SYNTHETIC_NOT_INTERNET");
                    TestContext.WriteLine("R21_RETRY_FRESH_FETCH=" + scripted.Calls);
                    TestContext.WriteLine("R21_PRIVATE_DURABLE_ROOT=" + diskPath);
                }
                finally { UnityEngine.Object.DestroyImmediate(root); }
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator M11_013_HostFallsBackToPreviouslySignedOfflineReleaseWithoutSavingProgress()
        {
            using (var signer = new RSACryptoServiceProvider(2048))
            {
                var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                var v1Bytes = Payload(1);
                var v2Bytes = Payload(2);
                var v1 = Manifest(v1Bytes, 1, now);
                var v2 = Manifest(v2Bytes, 2, now);
                var key = PublicKey(signer, now);
                var storage = NewRetainedTestRoot();
                var durable = new RemoteDurableActivationHost(
                    storage, GameId, Platform, Channel);
                var initial = durable.Restore(new[] { key }, Engine, now);
                var firstRoot = NewHost(out _, out _, out var firstHost);
                try
                {
                    var committed = firstHost.BeginPrelaunchAsync(
                        v1, Sign(signer, v1), new[] { key },
                        new RemoteReadOrigin("https://synthetic.invalid/qa/"),
                        new ScriptedTransport(v1Bytes, false), initial,
                        null, durable, Engine, now);
                    yield return new WaitUntil(() => committed.IsCompleted);
                    Assert.That(committed.Result.NewReleaseActivated, Is.True);
                }
                finally { UnityEngine.Object.DestroyImmediate(firstRoot); }

                var restored = new RemoteDurableActivationHost(
                    storage, GameId, Platform, Channel)
                    .Restore(new[] { key }, Engine, now);
                var failedNetwork = new ScriptedTransport(v2Bytes, true);
                var secondRoot = NewHost(out var game,
                    out var view, out var offlineHost);
                try
                {
                    var offline = offlineHost.BeginPrelaunchAsync(
                        v2, Sign(signer, v2), new[] { key },
                        new RemoteReadOrigin("https://synthetic.invalid/qa/"),
                        failedNetwork, restored, null, durable, Engine, now);
                    yield return new WaitUntil(() => offline.IsCompleted);
                    Assert.That(offline.IsFaulted, Is.False);
                    Assert.That(offline.Result.FellBackOffline, Is.True);
                    Assert.That(offline.Result.CanPlay, Is.True);
                    Assert.That(offline.Result.PlayableContent.Manifest.Sequence,
                        Is.EqualTo(1));
                    Assert.That(offline.Result.Flow.Snapshot().Stage,
                        Is.EqualTo(RemotePatchStage.Offline));
                    Assert.That(game.IsBoardInputAllowed, Is.True);
                    Assert.That(view.CanEnterGame, Is.True);
                    Assert.That(view.CanRequestHostRetry, Is.False);
                    Assert.That(view.Snapshot().CanClaimReward, Is.False);
                    Assert.That(view.Snapshot().CanPurchase, Is.False);
                    var again = new RemoteDurableActivationHost(
                        storage, GameId, Platform, Channel)
                        .Restore(new[] { key }, Engine, now);
                    Assert.That(again.Active.Manifest.Sequence, Is.EqualTo(1),
                        "Failed network must not corrupt installed signed state.");
                }
                finally { UnityEngine.Object.DestroyImmediate(secondRoot); }
            }
        }

        [Test]
        public void M11_013_HostRequiresSameGameAndCannotUseUnknownUnsignedRelease()
        {
            var root = NewHost(out var game, out var view, out var host);
            try
            {
                var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                using (var signer = new RSACryptoServiceProvider(2048))
                {
                    var bytes = Payload(1);
                    var manifest = Manifest(bytes, 1, now);
                    var keys = new[] { PublicKey(signer, now) };
                    var wrongPartition = new RemoteReleaseState(
                        "Garage_Legends", Platform, Channel);
                    var transport = new ScriptedTransport(bytes, false);
                    var installed = new RemoteReleaseState(
                        GameId, Platform, Channel);
                    var activation = new RemoteDurableActivationHost(
                        NewRetainedTestRoot(), GameId, Platform, Channel);
                    Assert.Throws<InvalidOperationException>(() =>
                        host.BeginPrelaunchAsync(manifest, Sign(signer, manifest),
                            keys, new RemoteReadOrigin("https://synthetic.invalid/qa/"),
                            transport, wrongPartition, null,
                            activation, Engine, now));
                    Assert.That(transport.Calls, Is.Zero);
                    Assert.That(host.AttemptCount, Is.Zero);
                    Assert.That(view.CanEnterGame, Is.False);
                    // Previously failed parameter validation cannot install
                    // the scene gate or configure a real retry.
                    Assert.That(game.IsSignedContentRequired, Is.False);
                    Assert.That(view.CanRequestHostRetry, Is.False);
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }
    }
}
