using System;
using System.Threading;
using System.Threading.Tasks;
using Match3.Engine.Runtime;
using UnityEngine;

namespace Match3.Engine.Demo
{
    public enum NeutralM11HttpsQaRunState
    {
        Waiting,
        Failed,
        Passed
    }

    public static class NeutralM11HttpsQaRunStateEvaluator
    {
        public static NeutralM11HttpsQaRunState Evaluate(
            bool negativeFinished, bool negativePassed,
            bool positivePassed, bool deadlineExpired)
        {
            if (deadlineExpired) return NeutralM11HttpsQaRunState.Failed;
            if (!negativeFinished) return NeutralM11HttpsQaRunState.Waiting;
            if (!negativePassed) return NeutralM11HttpsQaRunState.Failed;
            return positivePassed
                ? NeutralM11HttpsQaRunState.Passed
                : NeutralM11HttpsQaRunState.Waiting;
        }
    }

    // Development-only live HTTPS negative-path probe used by the isolated
    // ENG-M11 QA Player. It contains only a public trust anchor and URL.
    [DefaultExecutionOrder(-10990)]
    [DisallowMultipleComponent]
    public sealed class NeutralM11HttpsQaPlayerProbe : MonoBehaviour
    {
        [SerializeField] private string negativeHttpsPrefix;
        [SerializeField] private string publicKeyId;
        [SerializeField] private string publicRsaModulusBase64;
        [SerializeField] private long keyValidFromUnixSeconds;
        [SerializeField] private long keyValidUntilUnixSeconds;
        private NeutralPinnedHttpsPlayerBootstrap _bootstrap;
        private bool _negativeFinished;
        private bool _negativePassed;
        private float _deadline;
        private bool _terminal;
        private string _negativeFailure = "probe_not_completed";
        private CancellationTokenSource _negativeLifetime;

        private sealed class CancellationAwarePendingTransport : IRemoteReadTransport
        {
            public bool ObservedCancellation { get; private set; }

            public Task<RemoteReadResponse> FetchAsync(Uri url,
                long maximumBytes, CancellationToken cancellation)
            {
                var pending = new TaskCompletionSource<RemoteReadResponse>();
                cancellation.Register(() =>
                {
                    ObservedCancellation = true;
                    pending.TrySetCanceled();
                });
                return pending.Task;
            }
        }

        private const int AttemptTimeoutSeconds = 30;
        private const int OverallTimeoutSeconds = 75;

        private void Awake()
        {
            _bootstrap = GetComponent<NeutralPinnedHttpsPlayerBootstrap>();
        }

        private async void Start()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            _deadline = Time.realtimeSinceStartup + OverallTimeoutSeconds;
            Debug.Log("M11_013_PLAYER_HTTPS_QA=START attemptTimeoutSeconds=" +
                AttemptTimeoutSeconds + " overallTimeoutSeconds=" +
                OverallTimeoutSeconds);
            var platform = CurrentRemotePlatform();
            _negativeLifetime = new CancellationTokenSource();
            try
            {
                await VerifyCallerCancellationAsync();
                var key = new RemotePublicSigningKey(publicKeyId,
                    Convert.FromBase64String(publicRsaModulusBase64),
                    new byte[] { 0x01, 0x00, 0x01 },
                    keyValidFromUnixSeconds, keyValidUntilUnixSeconds);
                var prefix = negativeHttpsPrefix.Replace("{platform}",
                    PlatformPath(platform));
                var verification = NeutralPinnedHttpsManifestReader.FetchVerifiedAsync(
                    new RemoteReadOrigin(prefix),
                    new NeutralTimeoutRemoteReadTransport(
                        new RemoteHttpsReadTransport(),
                        TimeSpan.FromSeconds(AttemptTimeoutSeconds)),
                    new[] { key },
                    "Coral_Rescue", platform,
                    RemoteChannel.Development, new EngineVersion(1, 0, 0),
                    DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                    _negativeLifetime.Token);
                if (!await CompletesWithinAsync(verification,
                    TimeSpan.FromSeconds(AttemptTimeoutSeconds)))
                {
                    _negativeFailure = "per_attempt_timeout";
                    _negativeLifetime.Cancel();
                    Debug.LogError("M11_013_PLAYER_HTTPS_BAD_SIGNATURE=FAIL " +
                        "reason=per_attempt_timeout seconds=" + AttemptTimeoutSeconds);
                    return;
                }
                await verification;
                Debug.LogError("M11_013_PLAYER_HTTPS_BAD_SIGNATURE=FAIL " +
                    "negative notice was accepted");
                _negativeFailure = "bad_signature_accepted";
            }
            catch (InvalidOperationException ex)
            {
                if (ex.Message == "Manifest RSA-SHA256 verification failed.")
                {
                    _negativePassed = true;
                    _negativeFailure = string.Empty;
                    Debug.Log("M11_013_PLAYER_HTTPS_BAD_SIGNATURE=PASS " +
                        "platform=" + platform + " rsa=reject tls=system_validated");
                }
                else
                {
                    _negativeFailure = "validation_failure";
                    Debug.LogError("M11_013_PLAYER_HTTPS_BAD_SIGNATURE=FAIL " +
                        "unexpected_validation_failure=" + ex.GetType().Name);
                }
            }
            catch (OperationCanceledException)
            {
                _negativeFailure = "request_cancelled_or_timed_out";
                Debug.LogError("M11_013_PLAYER_HTTPS_BAD_SIGNATURE=FAIL " +
                    "reason=" + _negativeFailure);
            }
            catch (Exception ex)
            {
                _negativeFailure = "transport_or_probe_failure";
                Debug.LogError("M11_013_PLAYER_HTTPS_BAD_SIGNATURE=FAIL " +
                    "transport_or_probe_failure=" + ex.GetType().Name);
            }
            finally
            {
                _negativeFinished = true;
            }
#endif
        }

        public static async System.Threading.Tasks.Task<bool> CompletesWithinAsync(
            System.Threading.Tasks.Task operation, TimeSpan timeout)
        {
            if (operation == null) throw new ArgumentNullException(nameof(operation));
            if (timeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(timeout));
            using (var delayLifetime = new CancellationTokenSource())
            {
                var delay = System.Threading.Tasks.Task.Delay(timeout,
                    delayLifetime.Token);
                var completed = await System.Threading.Tasks.Task.WhenAny(
                    operation, delay).ConfigureAwait(false);
                if (completed == operation)
                {
                    delayLifetime.Cancel();
                    return true;
                }
                return false;
            }
        }

        private void Update()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (_terminal) return;
            var deadlineExpired = _deadline > 0f &&
                Time.realtimeSinceStartup >= _deadline;
            var positivePassed = _bootstrap != null &&
                _bootstrap.HasVerifiedHttpsActivation;
            var state = NeutralM11HttpsQaRunStateEvaluator.Evaluate(
                _negativeFinished, _negativePassed, positivePassed,
                deadlineExpired);
            if (state == NeutralM11HttpsQaRunState.Waiting)
                return;

            _terminal = true;
            if (state == NeutralM11HttpsQaRunState.Failed)
            {
                if (!_negativeFinished)
                {
                    _negativeFailure = "overall_watchdog_timeout";
                    _negativeLifetime?.Cancel();
                    Debug.LogError("M11_013_PLAYER_HTTPS_BAD_SIGNATURE=FAIL " +
                        "reason=overall_watchdog_timeout seconds=" + OverallTimeoutSeconds);
                }
                if (!positivePassed)
                    Debug.LogError("M11_013_PLAYER_HTTPS_POSITIVE=NOT_VERIFIED " +
                        "reason=network_or_timeout");
                Debug.LogError("M11_013_PLAYER_HTTPS_QA=FAIL negative=" +
                    (_negativePassed ? "rejected" : _negativeFailure) +
                    " positive=" + (positivePassed ? "verified" : "not_verified"));
                if (!Application.isMobilePlatform) Application.Quit(2);
                enabled = false;
                return;
            }

            Debug.Log("M11_013_PLAYER_HTTPS_POSITIVE=PASS " +
                "tls=validated rsa=verified player=development");
            Debug.Log("M11_013_PLAYER_HTTPS_QA=PASS " +
                "positive=verified negative=modified_signature_rejected");
            if (!Application.isMobilePlatform) Application.Quit(0);
            enabled = false;
#endif
        }

        private void OnDestroy()
        {
            _negativeLifetime?.Cancel();
            _negativeLifetime?.Dispose();
            _negativeLifetime = null;
        }

        private static RemotePlatform CurrentRemotePlatform()
        {
            if (Application.platform == RuntimePlatform.Android) return RemotePlatform.Android;
            if (Application.platform == RuntimePlatform.IPhonePlayer) return RemotePlatform.Ios;
            if (Application.platform == RuntimePlatform.WindowsPlayer ||
                Application.platform == RuntimePlatform.WindowsEditor) return RemotePlatform.Windows;
            throw new PlatformNotSupportedException("Unsupported M11 HTTPS QA platform.");
        }

        private static string PlatformPath(RemotePlatform platform)
        {
            if (platform == RemotePlatform.Android) return "android";
            if (platform == RemotePlatform.Ios) return "ios";
            return "windows";
        }

        private static async Task VerifyCallerCancellationAsync()
        {
            var pending = new CancellationAwarePendingTransport();
            var bounded = new NeutralTimeoutRemoteReadTransport(pending,
                TimeSpan.FromSeconds(10));
            using (var caller = new CancellationTokenSource())
            {
                caller.CancelAfter(TimeSpan.FromMilliseconds(50));
                try
                {
                    await bounded.FetchAsync(new Uri("https://signed.invalid/cancel"),
                        1024, caller.Token);
                }
                catch (OperationCanceledException) when
                    (caller.IsCancellationRequested && pending.ObservedCancellation)
                {
                    Debug.Log("M11_013_PLAYER_RETRY_CANCELLATION=PASS " +
                        "callerCancelled=true pendingReadCancelled=true timeoutDistinct=true");
                    return;
                }
            }
            throw new InvalidOperationException(
                "Caller cancellation was converted to a timeout or ignored.");
        }
    }
}
