using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;

namespace Match3.Engine.Runtime.Commerce
{
    public interface IGameplaySuspension
    {
        void Suspend();
        void Resume();
        bool IsSuspended { get; }
    }

    public sealed class NullGameplaySuspension : IGameplaySuspension
    {
        public bool IsSuspended { get; private set; }

        public void Suspend()
        {
            IsSuspended = true;
        }

        public void Resume()
        {
            IsSuspended = false;
        }
    }

    /// <summary>
    /// Eligibility-gated rewarded-ad flow that suspends gameplay while ads it requested are pending.
    /// MAIN-THREAD ONLY: not thread-safe. Call every member from the Unity main thread; SDK adapter callbacks
    /// that drive <see cref="RewardedAdLifecycle"/> or lead to <see cref="OnAdSessionEnded"/> must be marshalled
    /// to the main thread first.
    /// Gameplay is restored when an ad ends (<see cref="OnAdSessionEnded"/>), becomes ineligible before showing,
    /// the app is backgrounded before the SDK owns the screen (<see cref="OnApplicationPaused"/>), or a load/show
    /// start stalls past its timeout (<see cref="Tick"/>). Released lifecycles may later receive late SDK callbacks:
    /// adapters must ignore a callback whose lifecycle is no longer in the expected state.
    /// If <see cref="IGameplaySuspension.Resume"/> throws, the released lifecycle is already untracked and is not
    /// retried; the exception propagates to the caller.
    /// </summary>
    public sealed class AdShowCoordinator
    {
        public const long DefaultLoadTimeoutMs = 30000;
        public const long DefaultReadyTimeoutMs = 120000;

        private sealed class Tracked
        {
            public bool ShowInvoked;     // ShowRewardedAd was handed to the SDK; it may own the screen before NotifyShow.
            public bool Observed;        // Tick has seen ObservedState at least once.
            public RewardedAdState ObservedState;
            public long ObservedSinceMs; // monotonic time Tick first saw ObservedState
        }

        private readonly IAdMediationProvider _provider;
        private readonly IGameplaySuspension _suspension;
        private readonly long _loadTimeoutMs;
        private readonly long _readyTimeoutMs;
        // Lifecycles this coordinator suspended gameplay for. Gameplay is suspended when the first one is added
        // and resumed only when the last one is released (ENG-M14-006), so one ad ending never resumes another's.
        private readonly Dictionary<RewardedAdLifecycle, Tracked> _tracked = new Dictionary<RewardedAdLifecycle, Tracked>();

        private static readonly RewardedAdState[] TerminalStates =
        {
            RewardedAdState.Completed,
            RewardedAdState.Skipped,
            RewardedAdState.Failed
        };

        /// <summary>
        /// Game-facing factory (ENG-M14-002): binds the provider registered via
        /// <see cref="AdMediationProviderRegistry.Register"/> (register before creating) without ever exposing it,
        /// so Request/Show are reachable only through this coordinator's eligibility-gated methods.
        /// The timeouts apply only when the game drives <see cref="Tick"/>.
        /// </summary>
        public static AdShowCoordinator CreateFromRegistry(IGameplaySuspension suspension,
            long loadTimeoutMs = DefaultLoadTimeoutMs, long readyTimeoutMs = DefaultReadyTimeoutMs) =>
            new AdShowCoordinator(AdMediationProviderRegistry.Current, suspension, loadTimeoutMs, readyTimeoutMs);

        // Engine-internal: a public provider-taking constructor would require game code to hold the provider.
        internal AdShowCoordinator(IAdMediationProvider provider, IGameplaySuspension suspension,
            long loadTimeoutMs = DefaultLoadTimeoutMs, long readyTimeoutMs = DefaultReadyTimeoutMs)
        {
            _provider = provider ?? throw new ArgumentNullException(nameof(provider));
            _suspension = suspension ?? throw new ArgumentNullException(nameof(suspension));
            if (loadTimeoutMs <= 0) throw new ArgumentOutOfRangeException(nameof(loadTimeoutMs));
            if (readyTimeoutMs <= 0) throw new ArgumentOutOfRangeException(nameof(readyTimeoutMs));
            _loadTimeoutMs = loadTimeoutMs;
            _readyTimeoutMs = readyTimeoutMs;
        }

        /// <summary>
        /// Evaluates <see cref="AdEligibilityGate.Check(string, AdConsentStatus, bool)"/> itself for the
        /// placement's game; a caller-supplied eligibility result is never trusted.
        /// </summary>
        public bool TryShowRewardedAd(RewardPlacement placement, RewardedAdLifecycle lifecycle,
            AdConsentStatus consent, bool isKidProfile)
        {
            if (lifecycle == null) throw new ArgumentNullException(nameof(lifecycle));
            if (!AdEligibilityGate.Check(placement.GameId, consent, isKidProfile).IsEligible)
                return false;
            if (!placement.IsEnabled)
                return false;

            bool added = !_tracked.ContainsKey(lifecycle);
            if (added)
            {
                _tracked.Add(lifecycle, new Tracked());
                if (_tracked.Count == 1)
                {
                    try
                    {
                        _suspension.Suspend();
                    }
                    catch
                    {
                        _tracked.Remove(lifecycle); // never tracked without a successful suspend
                        throw;
                    }
                }
            }
            try
            {
                _provider.RequestRewardedAd(placement.PlacementId, lifecycle);
            }
            catch
            {
                // Release only what this call added: a repeat request for an ad that is already loaded or
                // on screen must not resume gameplay.
                if (added)
                    Release(lifecycle);
                throw;
            }
            if (!added)
                _tracked[lifecycle] = new Tracked(); // successful re-request (e.g. after Failed): fresh tracking
            return true;
        }

        /// <summary>
        /// Show step for an ad loaded via <see cref="TryShowRewardedAd(RewardPlacement, RewardedAdLifecycle, AdConsentStatus, bool)"/>.
        /// Re-runs <see cref="AdEligibilityGate.Check(string, AdConsentStatus, bool)"/> at show time with the CURRENT
        /// consent and kid flag (consent can be revoked between request and show) and re-checks the placement;
        /// only then calls <see cref="IAdMediationProvider.ShowRewardedAd"/>. When eligibility no longer holds the
        /// ad is not shown and this lifecycle's suspension is released (gameplay resumes once no other ad this
        /// coordinator suspended for is pending; ENG-M14-006) unless the SDK already owns the screen (Showing, or
        /// show already handed to the SDK), in which case it stays tracked and is released by
        /// <see cref="OnAdSessionEnded"/>. Returns false (no show) unless the lifecycle is Ready, was requested through
        /// this coordinator and has not been handed to the SDK yet. If the provider throws while showing, the lifecycle
        /// is marked Failed when it reached Showing, its suspension is released, and the exception is rethrown.
        /// A lifecycle left Ready after such a pre-show throw (or after a background/timeout release) is unusable:
        /// create a new <see cref="RewardedAdLifecycle"/> for the next request.
        /// </summary>
        public bool TryShowLoadedRewardedAd(RewardPlacement placement, RewardedAdLifecycle lifecycle,
            AdConsentStatus consent, bool isKidProfile)
        {
            if (lifecycle == null) throw new ArgumentNullException(nameof(lifecycle));
            _tracked.TryGetValue(lifecycle, out var tracked);

            if (!AdEligibilityGate.Check(placement.GameId, consent, isKidProfile).IsEligible || !placement.IsEnabled)
            {
                if (tracked != null && !SdkOwnsScreen(lifecycle, tracked))
                    Release(lifecycle);
                return false;
            }
            // Only show ads this coordinator requested (and therefore suspended gameplay for), once.
            if (lifecycle.State != RewardedAdState.Ready || tracked == null || tracked.ShowInvoked)
                return false;

            tracked.ShowInvoked = true;
            tracked.Observed = false; // show-start timeout counts from now
            try
            {
                _provider.ShowRewardedAd(placement.PlacementId, lifecycle);
            }
            catch
            {
                FailAndRelease(lifecycle, "ProviderShowException");
                throw;
            }
            return true;
        }

        [Obsolete("Caller-built eligibility is not trusted: always returns false. Use TryShowRewardedAd(placement, lifecycle, consent, isKidProfile).")]
        public bool TryShowRewardedAd(RewardPlacement placement, RewardedAdLifecycle lifecycle, AdEligibilityResult eligibility) =>
            false;

        /// <summary>
        /// Call when a lifecycle this coordinator requested reaches a terminal state. Releases that lifecycle's
        /// suspension; gameplay resumes only if this coordinator suspended it and no other tracked ad is pending.
        /// </summary>
        public void OnAdSessionEnded(RewardedAdLifecycle lifecycle)
        {
            if (lifecycle == null) throw new ArgumentNullException(nameof(lifecycle));
            var state = lifecycle.State;
            bool isTerminal = false;
            foreach (var t in TerminalStates)
                if (state == t) { isTerminal = true; break; }

            if (!isTerminal)
                throw new InvalidOperationException(
                    $"OnAdSessionEnded called with non-terminal lifecycle state: {state}");

            Release(lifecycle);
        }

        /// <summary>
        /// Background-interruption contract (ENG-M14-006): forward MonoBehaviour.OnApplicationPause(bool) here.
        /// On pause, every tracked ad still Loading (marked Failed) or Ready and not yet handed to the SDK (left Ready,
        /// unusable) is released, so gameplay is restored when the app returns to the foreground. Ads Showing or
        /// already handed to the SDK stay tracked: the SDK owns the screen (on Android a fullscreen ad itself pauses
        /// the app) and <see cref="OnAdSessionEnded"/> releases them. Unpause (false) is a no-op.
        /// Rethrows the first lifecycle state-handler exception after every eligible ad has been released.
        /// </summary>
        public void OnApplicationPaused(bool paused)
        {
            if (!paused)
                return;
            ReleaseWhere((lifecycle, tracked) =>
                !SdkOwnsScreen(lifecycle, tracked) &&
                (lifecycle.State == RewardedAdState.Loading || lifecycle.State == RewardedAdState.Ready),
                "AppBackgrounded");
        }

        /// <summary>
        /// Stuck-ad timeout driven by the caller's MONOTONIC clock in milliseconds (e.g. a Stopwatch or
        /// Time.realtimeSinceStartup; never wall-clock time). Call regularly, e.g. once per frame. A tracked ad that
        /// stays Loading for the load timeout is marked Failed and released; one that stays Ready (unshown, or handed
        /// to the SDK without reaching Showing) for the ready timeout is released (left Ready, unusable). Showing and
        /// terminal ads never time out. Each timer starts at the first Tick that observes the current state.
        /// Rethrows the first lifecycle state-handler exception after every timed-out ad has been released.
        /// </summary>
        public void Tick(long nowMs)
        {
            // ponytail: elapsed time is measured from the first Tick that sees a state, so accuracy is one Tick
            // interval; subscribe to OnStateChanged with a clock if sub-frame precision is ever needed.
            ReleaseWhere((lifecycle, tracked) =>
            {
                var state = lifecycle.State;
                if (!tracked.Observed || tracked.ObservedState != state)
                {
                    tracked.Observed = true;
                    tracked.ObservedState = state;
                    tracked.ObservedSinceMs = nowMs;
                    return false;
                }
                long elapsed = nowMs - tracked.ObservedSinceMs;
                if (state == RewardedAdState.Loading) return elapsed >= _loadTimeoutMs;
                if (state == RewardedAdState.Ready) return elapsed >= _readyTimeoutMs;
                return false;
            }, "Timeout");
        }

        private static bool SdkOwnsScreen(RewardedAdLifecycle lifecycle, Tracked tracked) =>
            lifecycle.State == RewardedAdState.Showing ||
            (tracked.ShowInvoked && lifecycle.State == RewardedAdState.Ready);

        private void ReleaseWhere(Func<RewardedAdLifecycle, Tracked, bool> shouldRelease, string reason)
        {
            List<RewardedAdLifecycle> doomed = null;
            foreach (var entry in _tracked)
                if (shouldRelease(entry.Key, entry.Value))
                    (doomed ??= new List<RewardedAdLifecycle>()).Add(entry.Key);
            if (doomed == null)
                return;

            ExceptionDispatchInfo first = null;
            foreach (var lifecycle in doomed)
            {
                try
                {
                    FailAndRelease(lifecycle, reason);
                }
                catch (Exception e)
                {
                    first ??= ExceptionDispatchInfo.Capture(e);
                }
            }
            first?.Throw();
        }

        // Marks the lifecycle Failed where the lifecycle API allows (Loading/Showing); a throwing OnStateChanged
        // handler can never skip the release.
        private void FailAndRelease(RewardedAdLifecycle lifecycle, string reason)
        {
            try
            {
                if (lifecycle.State == RewardedAdState.Loading || lifecycle.State == RewardedAdState.Showing)
                    lifecycle.NotifyFailed(reason);
            }
            finally
            {
                Release(lifecycle);
            }
        }

        // Resumes only what this coordinator suspended, and only when no other tracked ad is still pending.
        // Untracks before resuming, so a throwing Resume leaves the lifecycle untracked (not retried).
        private void Release(RewardedAdLifecycle lifecycle)
        {
            if (_tracked.Remove(lifecycle) && _tracked.Count == 0)
                _suspension.Resume();
        }
    }
}
