#!/usr/bin/env bash
set -uo pipefail

APK=${1:?usage: eas-m11-android.sh <unity-debug.apk>}
PACKAGE=com.match3engine.engm11.qa
OUT=${M11_EVIDENCE_DIR:-M11Evidence/Android}
mkdir -p "$OUT"
exec > >(tee "$OUT/runner.stdout.log") 2> >(tee "$OUT/runner.stderr.log" >&2)

failures=0
fail() { printf 'FAIL %s\n' "$*" | tee -a "$OUT/runner.assertions.txt"; failures=$((failures+1)); }
require_marker() {
  local file=$1 marker=$2 label=$3
  if grep -Fq "$marker" "$file"; then
    printf 'OBSERVED %s\n' "$label" | tee -a "$OUT/runner.assertions.txt"
  else
    fail "missing runtime marker: $label"
  fi
}

[[ -f "$APK" ]] || { fail "APK missing: $APK"; exit 2; }
sha256sum "$APK" | tee "$OUT/apk.sha256"
command -v adb >/dev/null || { fail 'adb is unavailable'; exit 2; }

mapfile -t emulators < <(adb devices | awk '$1 ~ /^emulator-/ && $2 == "device" {print $1}')
if [[ ${#emulators[@]} -ne 1 ]]; then
  fail "expected exactly one booted Android emulator, found ${#emulators[@]}"
  adb devices -l | tee "$OUT/adb-devices.txt"
  exit 2
fi
serial=${emulators[0]}
abi=$(adb -s "$serial" shell getprop ro.product.cpu.abi | tr -d '\r')
printf 'serial=%s\nabi=%s\n' "$serial" "$abi" | tee "$OUT/device.txt"
[[ "$abi" == arm64-v8a ]] || { fail "Unity 6000.6.5f1 APK requires ARM64 emulator; got $abi"; exit 2; }
if adb -s "$serial" shell pm list packages "$PACKAGE" | grep -Fxq "package:$PACKAGE"; then
  fail 'expected a clean hosted emulator with no existing QA app or private profile'
  exit 2
fi

adb -s "$serial" install --no-streaming "$APK" 2>&1 | tee "$OUT/install.txt" || { fail 'APK install failed'; exit 2; }
adb -s "$serial" logcat -c
adb -s "$serial" shell monkey -p "$PACKAGE" 1 | tee "$OUT/first-launch.txt"
for _ in $(seq 1 120); do
  launch_log=$(adb -s "$serial" logcat -d -s Unity:V)
  if grep -Fq 'M11_006_PLAYER_SIGNED_OFFLINE_STARTUP=PASS' <<<"$launch_log" &&
     grep -Fq 'M11_008_PLAYER_ADDRESSABLES_NATIVE_LOAD=PASS' <<<"$launch_log" &&
     grep -Fq 'M11_013_PLAYER_HTTPS_QA=' <<<"$launch_log" &&
     grep -Fq 'M11_013_PLAYER_RETRY_CANCELLATION=PASS' <<<"$launch_log"; then break; fi
  sleep 1
done
adb -s "$serial" logcat -v epoch -d > "$OUT/first-launch.logcat.txt"
adb -s "$serial" shell screencap -p /sdcard/m11-first-launch.png
adb -s "$serial" pull /sdcard/m11-first-launch.png "$OUT/first-launch.png" >/dev/null 2>&1 || fail 'first-launch screenshot unavailable'
first="$OUT/first-launch.logcat.txt"
require_marker "$first" 'M11_006_PLAYER_SIGNED_OFFLINE_STARTUP=PASS' '006 first signed app launch'
require_marker "$first" 'signature=verified bytes=verified' '006 RSA and exact packaged bytes'
require_marker "$first" 'boardCells=16' '006 playable board after trust gate'
require_marker "$first" 'rewards=disabled savedProgressMutation=none' '006 rewards and progress unchanged'

adb -s "$serial" shell am force-stop "$PACKAGE"
adb -s "$serial" shell cmd connectivity airplane-mode enable 2>&1 | tee "$OUT/offline-control.txt"
airplane=$(adb -s "$serial" shell settings get global airplane_mode_on | tr -d '\r')
printf 'airplane_mode_on=%s\n' "$airplane" | tee -a "$OUT/offline-control.txt"
[[ "$airplane" == 1 ]] || fail 'could not prove emulator network is disabled'
adb -s "$serial" logcat -c
adb -s "$serial" shell monkey -p "$PACKAGE" 1 | tee "$OUT/offline-relaunch.txt"
for _ in $(seq 1 90); do
  adb -s "$serial" logcat -d -s Unity:V | grep -Fq 'source=persisted-last-known-good' && break
  sleep 1
done
adb -s "$serial" logcat -v epoch -d > "$OUT/offline-relaunch.logcat.txt"
adb -s "$serial" shell screencap -p /sdcard/m11-offline-relaunch.png
adb -s "$serial" pull /sdcard/m11-offline-relaunch.png "$OUT/offline-relaunch.png" >/dev/null 2>&1 || fail 'offline relaunch screenshot unavailable'
second="$OUT/offline-relaunch.logcat.txt"
require_marker "$second" 'M11_006_PLAYER_SIGNED_OFFLINE_STARTUP=PASS' '006 offline process relaunch'
require_marker "$second" 'source=persisted-last-known-good' '006 durable app-private LKG restored after process restart'
require_marker "$second" 'boardCells=16' '006 board playable after offline LKG restore'

require_marker "$first" 'M11_008_PLAYER_ADDRESSABLES_NATIVE_LOAD=PASS' '008 signed native Addressables load'
require_marker "$first" 'signedRepeatLoads=2 exactTextAssetBytes=true unsignedBundleBlocked=true unsignedGets=0' '008 repeated signed load and unsigned firewall'
require_marker "$first" 'M11_013_PLAYER_HTTPS_POSITIVE=PASS tls=validated rsa=verified' '013 validated TLS and signed manifest'
require_marker "$first" 'M11_013_PLAYER_HTTPS_BAD_SIGNATURE=PASS' '013 modified RSA signature rejected over verified TLS'
require_marker "$first" 'M11_013_PLAYER_RETRY_CANCELLATION=PASS callerCancelled=true pendingReadCancelled=true' '013 bounded caller cancellation'
require_marker "$first" 'M11_013_PLAYER_HTTPS_QA=PASS positive=verified negative=modified_signature_rejected' '013 positive and negative HTTPS terminal result'

printf 'assertion_failures=%s\n' "$failures" | tee "$OUT/runner.summary.txt"
(( failures == 0 )) || exit 1
printf 'All required Android runtime assertions were observed.\n'
