using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Match3.Engine.Runtime
{
    // A concrete platform host is responsible for private storage durability.
    // This interface never grants the Engine permission to change unrelated files.
    public interface IRemoteActivationHost
    {
        Task PrepareVerifiedAsync(RemoteVerifiedRelease candidate,
            IReadOnlyDictionary<string, byte[]> payload,
            CancellationToken cancellation);

        Task<bool> ExchangeActiveAsync(string partition,
            string expectedEnvelopeDigest, string replacementEnvelopeDigest,
            long highestSequence, CancellationToken cancellation);
    }

    public sealed class RemoteActivationResult
    {
        public RemoteReleaseState State { get; }
        public bool Activated { get; }
        internal RemoteActivationResult(RemoteReleaseState state, bool activated)
        {
            State = state;
            Activated = activated;
        }
    }

    public static class RemoteActivationCoordinator
    {
        // The caller may publish only completely validated signed bytes.
        // A failed host stage/exchange never advances in-memory release state.
        public static async Task<RemoteActivationResult> ApplyAsync(
            RemoteReleaseState original, RemoteFetchedPackage candidate,
            IRemoteActivationHost host, CancellationToken cancellation)
        {
            if (original == null) throw new ArgumentNullException(nameof(original));
            if (candidate == null) throw new ArgumentNullException(nameof(candidate));
            if (host == null) throw new ArgumentNullException(nameof(host));
            cancellation.ThrowIfCancellationRequested();
            var plan = RemoteReleasePlanner.Promote(original, candidate.Verified);
            if (!plan.Applied) return new RemoteActivationResult(original, false);
            var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            foreach (var item in candidate.Verified.Manifest.Entries)
            {
                var bytes = candidate.GetVerifiedBytes(item.Path);
                if (!RemoteContentSafety.Matches(bytes, item.Length, item.Sha256))
                    throw new InvalidOperationException("Staged download bytes changed.");
                files.Add(item.Path, bytes);
            }
            await host.PrepareVerifiedAsync(candidate.Verified, files,
                cancellation).ConfigureAwait(false);
            cancellation.ThrowIfCancellationRequested();
            var expected = original.Active == null ? "" :
                original.Active.SignedEnvelopeDigest;
            var committed = await host.ExchangeActiveAsync(
                candidate.Verified.Manifest.PartitionKey, expected,
                candidate.Verified.SignedEnvelopeDigest,
                plan.Result.HighestSequence, cancellation)
                .ConfigureAwait(false);
            if (!committed)
                throw new InvalidOperationException(
                    "Atomic release exchange was not confirmed.");
            return new RemoteActivationResult(plan.Result, true);
        }
    }
}
