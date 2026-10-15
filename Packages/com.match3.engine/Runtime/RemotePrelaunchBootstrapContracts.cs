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
            // The installed state is already verified by the trusted host.
            // When the candidate signed envelope is identical, do not
            // re-download unchanged bytes or touch atomic storage.
            if (previousVerifiedState.Active != null &&
                StringComparer.Ordinal.Equals(
                    previousVerifiedState.Active.SignedEnvelopeDigest,
                    RemoteContentSafety.Sha256(
                        candidate.CanonicalBytes().Concat(candidateSignature)
                            .ToArray())))
            {
                flow.RecordCurrentInstalled(previousVerifiedState.Active,
                    keys, engine, nowUnixSeconds);
                progress?.Invoke(flow.Snapshot());
                return new RemotePrelaunchResult(flow,
                    previousVerifiedState.Active, false, false);
            }
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
                var activation = await RemoteActivationCoordinator.ApplyAsync(
                    previousVerifiedState, fetched, host,
                    cancellation).ConfigureAwait(false);
                if (activation.State.Active == null ||
                    !StringComparer.Ordinal.Equals(
                        activation.State.Active.SignedEnvelopeDigest,
                        signed.SignedEnvelopeDigest))
                    throw new InvalidOperationException(
                        "Host did not acknowledge expected signed release.");
                if (!activation.Activated)
                {
                    flow.RecordCurrentInstalled(previousVerifiedState.Active,
                        keys, engine, nowUnixSeconds);
                    progress?.Invoke(flow.Snapshot());
                    return new RemotePrelaunchResult(flow,
                        previousVerifiedState.Active, false, false);
                }
                // Never mark ready from 100% progress. The verified host
                // must explicitly confirm the actual atomic pointer exchange.
                flow.RecordActivation(activation);
                progress?.Invoke(flow.Snapshot());
                return new RemotePrelaunchResult(flow, signed, true, false);
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
                // A user's requested cancellation must not auto-retry or
                // silently change the current signed offline state.
                throw;
            }
            catch (Exception ex) when (ex is InvalidOperationException ||
                ex is IOException || ex is HttpRequestException ||
                ex is OperationCanceledException)
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

    }
}
