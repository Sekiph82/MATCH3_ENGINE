#!/usr/bin/env bash
set -uo pipefail

APP=${1:?usage: eas-m11-ios.sh <unity-simulator.app>}
BUNDLE=com.match3engine.engm11.qa
OUT=${M11_EVIDENCE_DIR:-M11Evidence/iOS}
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

[[ -d "$APP" && "$APP" == *.app ]] || { fail "simulator .app missing: $APP"; exit 2; }
ditto -c -k --sequesterRsrc --keepParent "$APP" "$OUT/unity-simulator-app.zip"
shasum -a 256 "$OUT/unity-simulator-app.zip" | tee "$OUT/app.sha256"
command -v xcrun >/dev/null || { fail 'xcrun is unavailable'; exit 2; }
mapfile -t devices < <(xcrun simctl list devices booted | sed -nE 's/.*\(([0-9A-Fa-f-]{36})\) \(Booted\).*/\1/p')
if [[ ${#devices[@]} -ne 1 ]]; then
  fail "expected exactly one booted iOS simulator, found ${#devices[@]}"
  xcrun simctl list devices booted | tee "$OUT/booted-devices.txt"
  exit 2
fi
device=${devices[0]}
xcrun simctl getenv "$device" SIMULATOR_MODEL_IDENTIFIER | tee "$OUT/device.txt"
xcrun simctl install "$device" "$APP" 2>&1 | tee "$OUT/install.txt" || { fail 'simulator app install failed'; exit 2; }
xcrun simctl spawn "$device" log erase --all 2>&1 | tee "$OUT/log-erase.txt"
xcrun simctl launch "$device" "$BUNDLE" | tee "$OUT/first-launch.txt" || { fail 'first simulator app launch failed'; exit 2; }
for _ in $(seq 1 120); do
  launch_log=$(xcrun simctl spawn "$device" log show --last 2m --style compact --predicate 'process == "Unity-iPhone"' 2>/dev/null)
  if grep -Fq 'M11_006_PLAYER_SIGNED_OFFLINE_STARTUP=PASS' <<<"$launch_log" &&
     grep -Fq 'M11_008_PLAYER_ADDRESSABLES_NATIVE_LOAD=PASS' <<<"$launch_log" &&
     grep -Fq 'M11_013_PLAYER_HTTPS_QA=' <<<"$launch_log" &&
     grep -Fq 'M11_013_PLAYER_RETRY_CANCELLATION=PASS' <<<"$launch_log"; then break; fi
  sleep 1
done
xcrun simctl spawn "$device" log show --last 5m --style compact --predicate 'process == "Unity-iPhone"' > "$OUT/first-launch.log.txt" 2>&1
xcrun simctl io "$device" screenshot "$OUT/first-launch.png" >/dev/null 2>&1 || fail 'first-launch screenshot unavailable'
first="$OUT/first-launch.log.txt"
require_marker "$first" 'M11_006_PLAYER_SIGNED_OFFLINE_STARTUP=PASS' '006 first signed app launch'
require_marker "$first" 'signature=verified bytes=verified' '006 RSA and exact packaged bytes'
require_marker "$first" 'boardCells=16' '006 playable board after trust gate'
require_marker "$first" 'rewards=disabled savedProgressMutation=none' '006 rewards and progress unchanged'

xcrun simctl terminate "$device" "$BUNDLE" 2>&1 | tee "$OUT/terminate.txt" || true
# The simulator API has no supported per-device network-off switch. Refuse to
# label a relaunch offline unless the runner supplies a measured network block.
if [[ "${M11_IOS_OFFLINE_PROOF:-}" != "confirmed" ]]; then
  fail 'iOS simulator network isolation was not established; offline relaunch cannot be claimed'
else
  xcrun simctl spawn "$device" log erase --all 2>&1 | tee "$OUT/relaunch-log-erase.txt"
  xcrun simctl launch "$device" "$BUNDLE" | tee "$OUT/offline-relaunch.txt" || fail 'offline relaunch failed'
  for _ in $(seq 1 90); do
    xcrun simctl spawn "$device" log show --last 2m --style compact --predicate 'process == "Unity-iPhone"' 2>/dev/null | grep -Fq 'source=persisted-last-known-good' && break
    sleep 1
  done
  xcrun simctl spawn "$device" log show --last 5m --style compact --predicate 'process == "Unity-iPhone"' > "$OUT/offline-relaunch.log.txt" 2>&1
  xcrun simctl io "$device" screenshot "$OUT/offline-relaunch.png" >/dev/null 2>&1 || fail 'offline relaunch screenshot unavailable'
  second="$OUT/offline-relaunch.log.txt"
  require_marker "$second" 'M11_006_PLAYER_SIGNED_OFFLINE_STARTUP=PASS' '006 process relaunch'
  require_marker "$second" 'source=persisted-last-known-good' '006 durable app-private LKG restored after process restart'
  require_marker "$second" 'boardCells=16' '006 board playable after LKG restore'
fi

require_marker "$first" 'M11_008_PLAYER_ADDRESSABLES_NATIVE_LOAD=PASS' '008 signed native Addressables load'
require_marker "$first" 'signedRepeatLoads=2 exactTextAssetBytes=true unsignedBundleBlocked=true unsignedGets=0' '008 repeated signed load and unsigned firewall'
require_marker "$first" 'M11_013_PLAYER_HTTPS_POSITIVE=PASS tls=validated rsa=verified' '013 validated TLS and signed manifest'
require_marker "$first" 'M11_013_PLAYER_HTTPS_BAD_SIGNATURE=PASS' '013 modified RSA signature rejected over verified TLS'
require_marker "$first" 'M11_013_PLAYER_RETRY_CANCELLATION=PASS callerCancelled=true pendingReadCancelled=true' '013 bounded caller cancellation'
require_marker "$first" 'M11_013_PLAYER_HTTPS_QA=PASS positive=verified negative=modified_signature_rejected' '013 positive and negative HTTPS terminal result'

printf 'assertion_failures=%s\n' "$failures" | tee "$OUT/runner.summary.txt"
(( failures == 0 )) || exit 1
printf 'All required iOS runtime assertions were observed.\n'
