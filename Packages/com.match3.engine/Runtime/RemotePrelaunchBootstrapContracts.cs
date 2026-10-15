using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Match3.Engine.Runtime
{
    public sealed class RemotePrelaunchResult
    {
        public RemotePatchFlow Flow { get; }
        public RemoteVerifiedRelease PlayableContent { get; }
        public bool CanPlay => PlayableContent != null && Flow.Snapshot().CanPlay;
        public bool NewReleaseActivated { get; }
        public bool FellBackOffline { get; }

        internal RemotePrelaunchResult(RemotePatchFlow flow,
            RemoteVerifiedRelease content, bool activated, bool offline)
        {
            Flow = flow;
            PlayableContent = content;
            NewReleaseActivated = activated;
            FellBackOffline = offline;
        }
    }

    // A real (but platform-neutral) prelaunch orchestrator: signed network
    // reads, verified receipt progress, durable commit acknowledgement, safe
    // no-update and previously verified offline fallback. It cannot award
    // currency, edit saves, change gameplay, or download executable code.
    public static class RemotePrelaunchBootstrap
    {
        public static async Task<RemotePrelaunchResult> ExecuteAsync(
            RemoteContentManifest candidate, byte[] candidateSignature,
            IEnumerable<RemotePublicSigningKey> trustedKeys,
            RemoteReadOrigin origin, IRemoteReadTransport transport,
            RemoteReleaseState previousVerifiedState,
            RemotePackagedStarter packagedStarter,
            IRemoteActivationHost host, EngineVersion engine,
            long nowUnixSeconds, CancellationToken cancellation,
            Action<RemotePatchUiSnapshot> progress = null)
        {
            if (candidate == null) throw new ArgumentNullException(nameof(candidate));
            if (previousVerifiedState == null)
                throw new ArgumentNullException(nameof(previousVerifiedState));
            if (trustedKeys == null) throw new ArgumentNullException(nameof(trustedKeys));
            if (origin == null) throw new ArgumentNullException(nameof(origin));
            if (transport == null) throw new ArgumentNullException(nameof(transport));
            if (host == null) throw new ArgumentNullException(nameof(host));
            cancellation.ThrowIfCancellationRequested();
            var keys = trustedKeys.Take(17).ToArray();
            if (keys.Length > 16 || keys.Any(x => x == null))
                throw new ArgumentException("Invalid pinned public-key registry.");
            // Authenticate candidate before any network requests or callbacks.
            var flow = new RemotePatchFlow(candidate, candidateSignature,
                keys, previousVerifiedState.GameId,
                previousVerifiedState.Platform,
                previousVerifiedState.Channel, engine, nowUnixSeconds);
            progress?.Invoke(flow.Snapshot());
            try
            {
                var data = new Dictionary<string, byte[]>(StringComparer.Ordinal);
                foreach (var entry in candidate.Entries)
                {
                    cancellation.ThrowIfCancellationRequested();
                    var receipt = await RemoteDownloadCoordinator.FetchVerifiedAsync(
                        candidate, candidateSignature, keys,
                        origin, transport, entry,
                        previousVerifiedState.GameId,
                        previousVerifiedState.Platform,
                        previousVerifiedState.Channel,
                        engine, nowUnixSeconds, cancellation)
                        .ConfigureAwait(false);
                    data.Add(entry.Path, receipt.Bytes);
                    flow.RecordVerified(receipt);
                    progress?.Invoke(flow.Snapshot());
                }
                var signed = RemoteContentStager.VerifyComplete(candidate,
                    candidateSignature, keys, previousVerifiedState.GameId,
                    previousVerifiedState.Platform,
                    previousVerifiedState.Channel, engine, nowUnixSeconds, data);
                var fetched = new RemoteFetchedPackage(signed, data);
                var commit = await RemoteActivationCoordinator.CommitVerifiedAsync(
                    previousVerifiedState, fetched,
                    new RemoteCommitAdapter(host, candidate.PartitionKey),
                    cancellation).ConfigureAwait(false);
                if (!commit.Result.Active.SignedEnvelopeDigest.Equals(
                    signed.SignedEnvelopeDigest, StringComparison.Ordinal))
                    throw new InvalidOperationException(
                        "Host did not acknowledge the expected signed release.");
                if (commit.Result.Active == previousVerifiedState.Active)
                {
                    flow.RecordCurrentInstalled(previousVerifiedState.Active,
                        keys, engine, nowUnixSeconds);
                    progress?.Invoke(flow.Snapshot());
                    return new RemotePrelaunchResult(
                        flow, previousVerifiedState.Active, false, false);
                }
                // Create a receipt using the same authenticated release the
                // host committed; never infer success from byte progress.
                var activated = new RemoteActivationResult(commit.Result, true);
                flow.RecordActivation(activated);
                progress?.Invoke(flow.Snapshot());
                return new RemotePrelaunchResult(flow, signed, true, false);
            }
            catch (OperationCanceledException)
            {
                cancellation.ThrowIfCancellationRequested();
                throw;
            }
            catch (Exception ex) when (ex is InvalidOperationException ||
                ex is IOException || ex is HttpRequestException)
            {
                cancellation.ThrowIfCancellationRequested();
                try
                {
                    var verifiedFallback = RemoteOfflineBootstrap.Select(
                        previousVerifiedState, packagedStarter,
                        keys, engine, nowUnixSeconds);
                    flow.RecordOfflineFallback(verifiedFallback, keys,
                        engine, nowUnixSeconds);
                    progress?.Invoke(flow.Snapshot());
                    return new RemotePrelaunchResult(
                        flow, verifiedFallback, false, true);
                }
                catch (Exception fallbackFailure) when (
                    fallbackFailure is InvalidOperationException ||
                    fallbackFailure is ArgumentException)
                {
                    flow.RecordFailure();
                    progress?.Invoke(flow.Snapshot());
                    return new RemotePrelaunchResult(flow, null, false, false);
                }
            }
        }

        // Reuse the Engine host's actual atomic acknowledgement and do not
        // replace it with an optimistic in-memory pointer mutation.
        private sealed class RemoteCommitAdapter : IRemoteAtomicCommitBackend
        {
            private readonly IRemoteActivationHost _host;
            private readonly string _partition;
            public RemoteCommitAdapter(IRemoteActivationHost host, string partition)
            {
                _host = host;
                _partition = partition;
            }

            public Task PrepareAsync(RemoteVerifiedRelease release,
                IReadOnlyDictionary<string, byte[]> data,
                CancellationToken cancellation) =>
                _host.PrepareVerifiedAsync(release, data, cancellation);

            public async Task CommitPointerAsync(RemoteVerifiedRelease expectedOld,
                RemoteVerifiedRelease requested, long highWater,
                CancellationToken cancellation)
            {
                var oldDigest = expectedOld == null ? "" :
                    expectedOld.SignedEnvelopeDigest;
                var ok = await _host.ExchangeActiveAsync(_partition,
                    oldDigest, requested.SignedEnvelopeDigest, highWater,
                    cancellation).ConfigureAwait(false);
                if (!ok)
                    throw new InvalidOperationException(
                        "Storage host refused atomic signed pointer commit.");
            }
        }
    }
}
