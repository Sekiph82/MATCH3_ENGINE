using System;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using Match3.Engine.Runtime;
using UnityEngine;
using UnityEngine.Networking;

namespace Match3.Engine.Demo
{
    // Isolated, development-build-only SIGNED starter demonstration.
    // Its private signing key is not in the repository or the player. Only a
    // frozen public QA key is embedded, never a production trust root.
    // The Editor QA builder provisions the matching genuine RSA-signed bytes.
    // Shipping/non-development players cannot trust or execute this QA path.
    public sealed class NeutralSignedQaBootstrap : MonoBehaviour
    {
        private NeutralMatch3Demo _demo;
        private NeutralPatchBootstrapView _view;
        private CancellationTokenSource _lifetime;

        private const string MobilePublicModulus =
            "ysPGhg7MRH/SmXixYumJ9cC2/pEfM1s57RF++5HWyz1JKUH/ckK7pwQFSzios/D1cufCMwF/BLuAK74FI4/lPpjDLRMEUFcMxUkw8c76MW+7xymbzedruleyoCyuSr/zai/MgQxVLzpLiHBV4we2GpLmcFIC5OsfVmM7lOY3uwdWCFHIAki1DuCgTNOyHsNbbk3xTjybnC+XmyyiWwKRytU0WQu6c4mt5LGXrAVR2iQmUFXHBz/q7oxFdMAutI/05aPzSZlINsy9NxnaXl/uZKzlvQeQRdoh7k3D8sHIzExnVZi+grtHFUn0+dRnr0dr0YuXW6bbTyKvrAETM+cPpQ==";

        public bool IsFinished { get; private set; }
        public bool HasAuthenticatedStarter { get; private set; }
        public bool RestoredLastKnownGoodFromDisk { get; private set; }
        public int FirebaseScenario { get; private set; }

        private void Awake()
        {
            _demo = GetComponent<NeutralMatch3Demo>();
            _view = GetComponent<NeutralPatchBootstrapView>();
            if (_demo == null || _view == null)
                throw new InvalidOperationException(
                    "QA signed bootstrap requires demo and patch-view siblings.");
            // Awake precedes gameplay Start and OnGUI: the board cannot flash
            // visible or accept input before signed content is authenticated.
            _demo.RequireSignedPrelaunch(_view);
        }

        private async void Start()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            _lifetime = new CancellationTokenSource();
            try
            {
                if (!TryGetPlatform(Application.platform, out var platform))
                    throw new InvalidOperationException(
                        "Signed QA starter requires a supported native player.");

                var mobile = platform == RemotePlatform.Android ||
                    platform == RemotePlatform.Ios;
                var androidGameLoop = platform == RemotePlatform.Android;
                FirebaseScenario = androidGameLoop ? ReadFirebaseScenario() : 0;
                if (androidGameLoop && FirebaseScenario != 1 && FirebaseScenario != 2)
                    throw new InvalidOperationException(
                        "Firebase Game Loop must provide scenario 1 or 2.");
                var publicModulus = Convert.FromBase64String(mobile
                    ? MobilePublicModulus
                    : "zoodN2wo20EQxljrIekNMICz96ZUP60ns3ShOygfHdBo4O5k7AcerQx8+Cf3UrCz52k+s+C9RuxlsyU9Y+AlXIJUYJ5CfI3IIpvb7LDHWzgUEu9CXIIrQ9ZX3y4/5D3GMF1CeHZOj9If2rOS41HpVXPf/OdtQxlccGdVdGi9LEw5p5yb39Zt2zA6OFlHzlk7fNB/w27bPVVCinYfk+kipmKHJ93G6dykWqcrh2X4dFarqpcZe3UCFaEAVhA06NiTFm79L1ESeDTJORE2pB7gmgFPY8p0vfexiZwvkYD9BgfGlWZYeA8k7TFxwog1MzLNwZbIEeR5rQ+G5mzkOtN4TQ==");
                var key = new RemotePublicSigningKey(
                    mobile ? "m11-mobile-qa" : "m11-qa-public", publicModulus,
                    new byte[] { 0x01, 0x00, 0x01 },
                    mobile ? 1792329183L : 1792022400L,
                    mobile ? 1823868783L : 1823472000L);
                var engine = new EngineVersion(1, 0, 0);
                var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                var packaged = await NeutralUnityStreamingStarterLoader.LoadAsync(
                    "Coral_Rescue", platform,
                    RemoteChannel.Development, engine, new[] { key }, now,
                    _lifetime.Token);
                // Persist only under this QA app's Unity-owned private root.
                // The folder is a new, isolated QA partition; existing app
                // data is never reset, replaced or cleaned.
                var durableRoot = Path.Combine(Application.persistentDataPath,
                    "m11-offline-lkg");
                Directory.CreateDirectory(durableRoot);
                var durable = new RemoteDurableActivationHost(durableRoot,
                    "Coral_Rescue", platform,
                    RemoteChannel.Development);
                var packagedBytes = await ReadPackagedBytesAsync(packaged,
                    _lifetime.Token);
                var starter = new RemotePackagedStarter(
                    packaged.Manifest.CanonicalBytes(), packaged.Signature,
                    packagedBytes);
                var before = durable.Restore(new[] { key }, engine, now);
                RestoredLastKnownGoodFromDisk = before.Active != null;
                if (before.Active == null)
                {
                    await durable.PrepareVerifiedAsync(packaged, packagedBytes,
                        _lifetime.Token);
                    var activated = await durable.ExchangeActiveAsync(
                        "Coral_Rescue/2/0", "", packaged.SignedEnvelopeDigest,
                        packaged.Manifest.Sequence, _lifetime.Token);
                    if (!activated)
                        throw new InvalidOperationException(
                            "Verified packaged starter was not durably activated.");
                }
                var restored = durable.Restore(new[] { key }, engine, now);
                if (mobile && FirebaseScenario == 2 &&
                    !RestoredLastKnownGoodFromDisk)
                    throw new InvalidOperationException(
                        "Firebase restart scenario did not restore persisted signed state.");
                var offline = RemoteOfflineBootstrap.Select(restored,
                    starter, new[] { key }, engine, now);
                NeutralSignedStarterPrelaunch.BindVerifiedOffline(_view,
                    offline, new[] { key }, "Coral_Rescue",
                    platform, RemoteChannel.Development,
                    engine, now);
                HasAuthenticatedStarter = _view.CanEnterGame;
                if (!HasAuthenticatedStarter)
                    throw new InvalidOperationException(
                        "Signed starter did not open the gameplay gate.");
                await UniTask.WaitUntil(() => _demo.IsPlayable &&
                    _demo.VisibleBoardCellCount == 16);
                Debug.Log("M11_006_PLAYER_SIGNED_OFFLINE_STARTUP=PASS source=" +
                    (RestoredLastKnownGoodFromDisk
                        ? "persisted-last-known-good" : "packaged-signed-starter-committed") +
                    " signature=verified bytes=verified title=Coral_Rescue platform=" + platform + " " +
                    "channel=Development CanEnterGame=" + _view.CanEnterGame +
                    " boardCells=" + _demo.VisibleBoardCellCount +
                    " durableSequence=" + offline.Manifest.Sequence +
                    " rewards=disabled savedProgressMutation=none");
                if (mobile && FirebaseScenario == 1)
                {
                    var identity = CurrentAndroidProcessIdentity();
                    File.WriteAllText(Path.Combine(durableRoot,
                        "firebase-first-process.txt"), identity,
                        new System.Text.UTF8Encoding(false));
                    Debug.Log("M11_006_PLAYER_FIRST_PROCESS=PASS identity=" + identity);
                }
                else if (mobile && FirebaseScenario == 2)
                {
                    var marker = Path.Combine(durableRoot,
                        "firebase-first-process.txt");
                    var previous = File.Exists(marker)
                        ? File.ReadAllText(marker).Trim() : string.Empty;
                    var current = CurrentAndroidProcessIdentity();
                    if (string.IsNullOrWhiteSpace(previous) || previous == current)
                        throw new InvalidOperationException(
                            "Firebase Game Loop did not create a distinct process restart.");
                    Debug.Log("M11_006_PLAYER_PERSISTED_LKG_RESTART=PASS " +
                        "previousProcess=" + previous + " currentProcess=" + current +
                        " lkgRestored=true signature=verified savedProgressMutation=none rewards=disabled");
                }
                if (mobile && FirebaseScenario > 0)
                    await CompleteFirebaseScenarioAsync(platform, FirebaseScenario);
            }
            catch (OperationCanceledException)
            {
                // A cancelled scene cannot authorize any gameplay.
                HasAuthenticatedStarter = false;
                if (Application.platform == RuntimePlatform.Android)
                {
                    Debug.LogError("M11_006_PLAYER_SIGNED_OFFLINE_STARTUP=FAIL cause=cancelled");
                    Debug.LogError("M11_FIREBASE_SCENARIO=FAIL cause=cancelled");
                    Application.Quit(2);
                }
            }
            catch (Exception error)
            {
                // No local paths, signatures or exception internals in UI.
                HasAuthenticatedStarter = false;
                if (_view != null && !_view.HasVerifiedSession)
                    _view.ShowUnavailable("content-unavailable");
                if (Application.platform == RuntimePlatform.Android)
                {
                    Debug.LogError("M11_006_PLAYER_SIGNED_OFFLINE_STARTUP=FAIL cause=" +
                        error.GetType().Name);
                    if (FirebaseScenario == 2)
                        Debug.LogError("M11_006_PLAYER_PERSISTED_LKG_RESTART=FAIL cause=" +
                            error.GetType().Name);
                    Debug.LogError("M11_FIREBASE_SCENARIO=FAIL cause=bootstrap");
                    Application.Quit(2);
                }
            }
            finally
            {
                IsFinished = true;
            }
#else
            // An accidentally referenced QA scene in an actual release must
            // fail closed, not embed a test trust root or unlock the board.
            _view.ShowUnavailable("no-signed-starter");
            IsFinished = true;
#endif
        }

        private async Task CompleteFirebaseScenarioAsync(RemotePlatform platform,
            int scenario)
        {
            var addressables = GetComponent<OfficialAddressables.NeutralMobileAddressablesPlayerProbe>();
            var https = GetComponent<NeutralM11HttpsQaPlayerProbe>();
            var deadline = Time.realtimeSinceStartup + 150f;
            while (Time.realtimeSinceStartup < deadline &&
                (addressables == null || !addressables.IsFinished ||
                 https == null || !https.IsFinished))
            {
                await Task.Yield();
                _lifetime.Token.ThrowIfCancellationRequested();
            }
            var passed = addressables != null && addressables.IsFinished &&
                addressables.Passed && https != null && https.IsFinished && https.Passed;
            if (!passed)
            {
                Debug.LogError("M11_FIREBASE_SCENARIO=FAIL scenario=" + scenario +
                    " addressables=" + (addressables != null && addressables.Passed) +
                    " https=" + (https != null && https.Passed) +
                    " timeout=" + (Time.realtimeSinceStartup >= deadline));
                Application.Quit(2);
                return;
            }
            Debug.Log("M11_FIREBASE_SCENARIO=PASS scenario=" + scenario +
                " addressablesNative=true liveTlsRsaPositiveNegative=true");
            Application.Quit(0);
        }

        private static int ReadFirebaseScenario()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
            using (var intent = activity.Call<AndroidJavaObject>("getIntent"))
                return intent.Call<int>("getIntExtra", "scenario", 0);
#else
            return 0;
#endif
        }

        private static string CurrentAndroidProcessIdentity()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            using (var process = new AndroidJavaClass("android.os.Process"))
            {
                var pid = process.CallStatic<int>("myPid");
                var stat = File.ReadAllText("/proc/self/stat");
                var close = stat.LastIndexOf(')');
                if (close < 0 || close + 2 >= stat.Length)
                    throw new InvalidOperationException("Android process identity is unavailable.");
                var fields = stat.Substring(close + 2).Split(
                    new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                // /proc/<pid>/stat field 22 is process start time; after the
                // parenthesized comm field, start time is token index 19.
                if (fields.Length <= 19)
                    throw new InvalidOperationException("Android process start time is unavailable.");
                long startTicks;
                if (!long.TryParse(fields[19], NumberStyles.None,
                    CultureInfo.InvariantCulture, out startTicks))
                    throw new InvalidOperationException("Android process start time is invalid.");
                return pid.ToString(CultureInfo.InvariantCulture) + ":" +
                    startTicks.ToString(CultureInfo.InvariantCulture);
            }
#else
            return "unsupported-platform";
#endif
        }

        private static async Task<IReadOnlyDictionary<string, byte[]>>
            ReadPackagedBytesAsync(RemoteVerifiedRelease release,
                CancellationToken cancellation)
        {
            var root = Application.streamingAssetsPath.TrimEnd('/', '\\') +
                "/match3/" + release.Manifest.GameId + "/" +
                (int)release.Manifest.Platform + "/" +
                (int)release.Manifest.Channel + "/";
            var bytes = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            foreach (var entry in release.Manifest.Entries)
            {
                cancellation.ThrowIfCancellationRequested();
                if (!RemoteContentSafety.IsSafePath(entry.Path))
                    throw new InvalidOperationException(
                        "Unsafe signed starter path for durable activation.");
                var url = root + string.Join("/", entry.Path.Split('/')
                    .Select(Uri.EscapeDataString));
                byte[] content;
                using (var request = UnityWebRequest.Get(url))
                {
                    request.timeout = 15;
                    var operation = request.SendWebRequest();
                    while (!operation.isDone)
                    {
                        cancellation.ThrowIfCancellationRequested();
                        await Task.Yield();
                    }
                    cancellation.ThrowIfCancellationRequested();
                    if (request.result != UnityWebRequest.Result.Success)
                        throw new InvalidOperationException(
                            "Packaged starter unavailable for durable activation.");
                    content = request.downloadHandler.data;
                }
                if (!RemoteContentSafety.Matches(content, entry.Length,
                    entry.Sha256))
                    throw new InvalidOperationException(
                        "Packaged starter changed before durable activation.");
                bytes.Add(entry.Path, content);
            }
            return bytes;
        }

        private static bool TryGetPlatform(RuntimePlatform runtime,
            out RemotePlatform platform)
        {
            switch (runtime)
            {
                case RuntimePlatform.WindowsEditor:
                case RuntimePlatform.WindowsPlayer:
                    platform = RemotePlatform.Windows;
                    return true;
                case RuntimePlatform.Android:
                    platform = RemotePlatform.Android;
                    return true;
                case RuntimePlatform.IPhonePlayer:
                    platform = RemotePlatform.Ios;
                    return true;
                default:
                    platform = default;
                    return false;
            }
        }

        private void OnDestroy()
        {
            if (_lifetime == null) return;
            _lifetime.Cancel();
            _lifetime.Dispose();
            _lifetime = null;
        }
    }
}
