using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Match3.Engine.Runtime;

internal static class Program
{
    private static readonly EngineVersion Engine = new EngineVersion(1, 0, 0);
    private const long Now = 2000;

    private sealed class MemorySource : IRemoteReadTransport
    {
        private readonly byte[] _bytes;
        public MemorySource(byte[] bytes) { _bytes = bytes; }

        public Task<RemoteReadResponse> FetchAsync(
            Uri uri, long maximumBytes, CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();
            if (_bytes.LongLength > maximumBytes)
                throw new InvalidOperationException("Source exceeds signed budget");
            return Task.FromResult(new RemoteReadResponse(200, _bytes));
        }
    }

    private static void Require(bool condition, string label)
    {
        if (!condition) throw new InvalidOperationException("FAIL: " + label);
    }

    private static RemotePublicSigningKey Trust(RSA signer)
    {
        var k = signer.ExportParameters(false);
        return new RemotePublicSigningKey("qa-public-1",
            k.Modulus, k.Exponent, 0, 10000);
    }

    private static async Task<RemoteFetchedPackage> SignedPackage(
        RSA signer, RemotePublicSigningKey trust, long sequence)
    {
        var bytes = Encoding.UTF8.GetBytes(
            "{\"version\":" + sequence + ",\"neutral\":true}");
        var manifest = new RemoteContentManifest(
            "Coral_Rescue", RemotePlatform.Windows,
            RemoteChannel.FamilyTest, "native-qa-v" + sequence,
            "qa-public-1", Engine, new EngineVersion(1, 1, 0),
            1000, 9000, new[] {
                new RemoteContentEntry("levels/neutral.json",
                    RemoteContentKind.Json, bytes.Length,
                    RemoteContentSafety.Sha256(bytes))
            }, sequence: sequence);
        var sig = signer.SignData(manifest.CanonicalBytes(),
            HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return await RemoteDownloadCoordinator.FetchStageAsync(
            manifest, sig, new[] { trust },
            new RemoteReadOrigin("https://qa.example.com/"),
            new MemorySource(bytes), manifest.GameId, manifest.Platform,
            manifest.Channel, Engine, Now, CancellationToken.None,
            maxParallel: 1);
    }

    private static RemoteDurableActivationHost Host(string path) =>
        new RemoteDurableActivationHost(path, "Coral_Rescue",
            RemotePlatform.Windows, RemoteChannel.FamilyTest);

    private static async Task Run(string root)
    {
        using (var signer = RSA.Create(2048))
        {
            var pinned = Trust(signer);
            var first = await SignedPackage(signer, pinned, 1);
            var state = new RemoteReleaseState("Coral_Rescue",
                RemotePlatform.Windows, RemoteChannel.FamilyTest);
            var host = Host(root);
            var committed1 = await RemoteActivationCoordinator.ApplyAsync(
                state, first, host, CancellationToken.None);
            Require(committed1.Activated, "signed first Windows commit acknowledged");
            Require(committed1.State.HighestSequence == 1,
                "first Windows high-water");
            var restarted1 = Host(root).Restore(new[] { pinned }, Engine, Now);
            Require(restarted1.Active != null &&
                restarted1.Active.Manifest.Sequence == 1,
                "first signed Windows release restart");
            Console.WriteLine("M11_WIN64_SIGNED_DURABLE_RESTART=PASS");

            var second = await SignedPackage(signer, pinned, 2);
            var committed2 = await RemoteActivationCoordinator.ApplyAsync(
                restarted1, second, Host(root), CancellationToken.None);
            Require(committed2.Activated, "second Windows pointer exchange");
            Require(committed2.State.HighestSequence == 2,
                "second high-water persisted");
            var restarted2 = Host(root).Restore(new[] { pinned }, Engine, Now);
            Require(restarted2.Active.Manifest.Sequence == 2,
                "second actual signed release loaded on Windows");
            Require(restarted2.PreviousValid != null &&
                restarted2.PreviousValid.Manifest.Sequence == 1,
                "previous signed release still present");
            Console.WriteLine("M11_WIN64_ATOMIC_POINTER_SWAP=PASS");

            // Corrupt ONLY the file created inside the test-owned root.
            var release2 = Directory.GetFiles(root, "neutral.json",
                SearchOption.AllDirectories).Single(x =>
                    x.Contains("release-2-", StringComparison.Ordinal));
            File.WriteAllBytes(release2,
                Encoding.UTF8.GetBytes("{\"version\":999,\"neutral\":true}"));
            var rolledBack = Host(root).Restore(new[] { pinned }, Engine, Now);
            Require(rolledBack.Active.Manifest.Sequence == 1,
                "Windows fallback re-verified previous signed release");
            Require(rolledBack.HighestSequence == 2,
                "Windows crash-recovery preserved anti-rollback sequence");
            try
            {
                RemoteReleasePlanner.Promote(rolledBack, second.Verified);
                throw new InvalidOperationException(
                    "FAIL: stale sequence accepted after Windows recovery");
            }
            catch (InvalidOperationException ex)
            {
                if (ex.Message.StartsWith("FAIL:", StringComparison.Ordinal))
                    throw;
            }
            Console.WriteLine("M11_WIN64_TAMPER_ROLLBACK_HIGHEST_SEQUENCE=PASS");

            // Losing the active pointer is NOT permission to clear anti-
            // downgrade protection while previous-pointer history remains.
            var active = Directory.GetFiles(root, "active.pointer",
                SearchOption.AllDirectories).Single();
            File.Delete(active);
            var denied = false;
            try { Host(root).Restore(new[] { pinned }, Engine, Now); }
            catch (InvalidOperationException) { denied = true; }
            Require(denied, "missing active pointer cannot reset signed ledger");
            Console.WriteLine("M11_WIN64_POINTER_LOSS_FAIL_CLOSED=PASS");
        }
    }

    private static async Task<int> Main(string[] args)
    {
        // This executable is only invoked on a disposable GitHub runner.
        // Never touch a user's desktop, app data, repository or arbitrary path.
        if (args.Length != 0)
            throw new ArgumentException("Native QA takes no filesystem arguments.");
        var root = Path.Combine(Path.GetTempPath(),
            "match3-native-win64-qa-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            await Run(root);
            return 0;
        }
        finally
        {
            // Only the unique isolated directory created above is owned by
            // this test; no other directory can be supplied for deletion.
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }
}
