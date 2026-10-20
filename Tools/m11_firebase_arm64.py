#!/usr/bin/env python3
"""Run one explicitly authorized ARM64 Firebase Test Lab Game Loop matrix.

This does not claim acceptance. A 0 exit from gcloud means the matrix ran,
NOT that Unity's M11 signed-content assertions passed. Review original device
logs and evidence before changing TASKS.md. No emulator or GitHub CI usage.
"""
import argparse
import json
import pathlib
import re
import shutil
import subprocess
import sys
import zipfile


UNITY_LIBS = ("libunity.so", "libil2cpp.so")
ACTION = "com.google.intent.action.TEST_LOOP"
# Integrated from upstream staging commit 20f012e9b6dc64e863ea42e8720ac465bc6c804a.
REQUIRED_MARKERS = (
    ("M11_006_PLAYER_SIGNED_OFFLINE_STARTUP=PASS", 2),
    ("M11_006_PLAYER_FIRST_PROCESS=PASS", 1),
    ("M11_006_PLAYER_PERSISTED_LKG_RESTART=PASS", 1),
    ("M11_008_PLAYER_ADDRESSABLES_NATIVE_LOAD=PASS", 2),
    ("M11_013_PLAYER_HTTPS_ACTIVATION=PASS", 2),
    ("M11_013_PLAYER_HTTPS_BAD_SIGNATURE=PASS", 2),
    ("M11_013_PLAYER_RETRY_CANCELLATION=PASS", 2),
    ("M11_FIREBASE_SCENARIO=PASS scenario=1", 1),
    ("M11_FIREBASE_SCENARIO=PASS scenario=2", 1),
)


def verify_game_activity_theme(xmltree):
    """Require an explicit theme on the Unity GameActivity in compiled APK output."""
    lines = xmltree.splitlines()
    for index, line in enumerate(lines):
        node = re.match(r"^(\s*)E:\s+activity\b", line)
        if not node:
            continue
        prefix = len(node.group(1))
        block = [line]
        for following in lines[index + 1:]:
            child = re.match(r"^(\s*)E:\s+", following)
            if child and len(child.group(1)) <= prefix:
                break
            block.append(following)
        content = "\n".join(block)
        if "UnityPlayerGameActivity" not in content:
            continue
        return bool(re.search(r"A:\s+android:theme\(", content))
    return False
def verify_apk(apk, aapt):
    problems = []
    if not apk.is_file():
        return ["APK missing: " + str(apk)]
    try:
        with zipfile.ZipFile(apk) as archive:
            files = set(archive.namelist())
            for lib in UNITY_LIBS:
                path = "lib/arm64-v8a/" + lib
                if path not in files:
                    problems.append("Missing ARM64 Unity native library: " + path)
    except (OSError, zipfile.BadZipFile) as exc:
        return ["Cannot read APK: " + str(exc)]
    try:
        cmd = subprocess.run(
            [aapt, "dump", "xmltree", str(apk), "AndroidManifest.xml"],
            capture_output=True, text=True, timeout=25, check=False
        )
        if cmd.returncode != 0:
            problems.append("AAPT could not read compiled AndroidManifest.xml")
        elif ACTION not in cmd.stdout:
            problems.append("Missing Firebase Game Loop action in packaged manifest")
        elif "application/javascript" not in cmd.stdout:
            problems.append("Missing Game Loop intent-filter MIME in packaged manifest")
        if cmd.returncode == 0 and not verify_game_activity_theme(cmd.stdout):
            problems.append("UnityPlayerGameActivity lacks an explicit compiled AppCompat theme")
    except (FileNotFoundError, subprocess.TimeoutExpired):
        problems.append("Android SDK aapt missing or timed out")
    return problems


def inspect_device_log(logfile):
    """Original device log only, strict one-per-marker and explicit fail rejection."""
    lines = logfile.read_text(encoding="utf-8", errors="replace").splitlines()
    passed = {}
    for marker, expected_count in REQUIRED_MARKERS:
        count = sum(1 for line in lines if marker in line)
        passed[marker] = {"observed": count, "minimum": expected_count,
                          "satisfied": count >= expected_count}
    failed = [line.strip() for line in lines if
              re.search(r"\bM11_(006|008|013)_PLAYER_[A-Z0-9_]+=FAIL\b", line)]
    return {
        "markers": passed,
        "explicit_failures": failed,
        "pass": all(item["satisfied"] for item in passed.values()) and not failed,
        "scope": "ANDROID_PHYSICAL_DEVICE_LOG_ONLY",
    }


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--apk", type=pathlib.Path)
    parser.add_argument("--aapt", default="aapt")
    parser.add_argument("--project")
    parser.add_argument("--device-model")
    parser.add_argument("--android-version")
    parser.add_argument("--scenario-numbers", default="1,2",
                        help="Game Loop first-launch and persisted-restart scenarios")
    parser.add_argument("--run", action="store_true", help="execute ONE physical FTL matrix")
    parser.add_argument("--spark-confirmed", action="store_true",
                        help="owner verified free Firebase Spark project, no billing")
    parser.add_argument("--device-log", type=pathlib.Path,
                        help="independently audit an original downloaded device log")
    args = parser.parse_args()
    if args.device_log:
        if not args.device_log.is_file():
            parser.error("Original device log does not exist")
        verdict = inspect_device_log(args.device_log)
        print(json.dumps(verdict, indent=2, sort_keys=True))
        return 0 if verdict["pass"] else 3

    if args.apk is None:
        parser.error("--apk is required unless --device-log is used")
    problems = verify_apk(args.apk, args.aapt)
    if problems:
        print(json.dumps({"ready": False, "problems": problems}, indent=2))
        return 2
    print("ARM64 libraries and Game Loop manifest: static checks PASS; no Player evidence")
    if not args.run:
        print("DRY RUN: no Firebase tests were executed")
        return 0
    if not args.spark_confirmed:
        parser.error("Refusing paid/unknown plan: --spark-confirmed required")
    if not (args.project and args.device_model and args.android_version):
        parser.error("--project, --device-model, --android-version required")
    if not re.fullmatch(r"[a-z][a-z0-9-]{4,40}", args.project):
        parser.error("Invalid project identifier")
    if not re.fullmatch(r"[A-Za-z0-9_-]{1,64}", args.device_model):
        parser.error("Invalid device identifier")
    if not re.fullmatch(r"[A-Za-z0-9_.-]{1,24}", args.android_version):
        parser.error("Invalid Android version")
    scenarios = args.scenario_numbers.split(",")
    if len(scenarios) < 2 or len(set(scenarios)) != len(scenarios) or any(
            not re.fullmatch(r"[1-9][0-9]{0,3}", value) for value in scenarios):
        parser.error("At least two distinct positive scenarios are required for process-restart evidence")
    gcloud = shutil.which("gcloud")
    if not gcloud:
        parser.error("Google Cloud CLI not installed")
    billing = subprocess.run(
        [gcloud, "billing", "projects", "describe", args.project,
         "--format=json"],
        capture_output=True, text=True, timeout=35, check=False
    )
    if billing.returncode != 0:
        parser.error("Cannot verify project billing state; refusing to spend a test")
    try:
        billing_state = json.loads(billing.stdout)
    except ValueError:
        parser.error("Unparseable project billing state; refusing to spend a test")
    if billing_state.get("billingEnabled") is True or billing_state.get("billingAccountName"):
        parser.error("Project has Cloud Billing enabled; refusing any potentially paid test")
    inventory = subprocess.run(
        [gcloud, "--project=" + args.project, "firebase", "test", "android",
         "models", "describe", args.device_model, "--format=json"],
        capture_output=True, text=True, timeout=35, check=False
    )
    if inventory.returncode != 0:
        parser.error("Cannot verify Firebase device model; refusing to spend a test")
    try:
        device = json.loads(inventory.stdout)
    except ValueError:
        parser.error("Unparseable Firebase device metadata; refusing test")
    if str(device.get("form", "")).upper() != "PHYSICAL":
        parser.error("Selected model is NOT a confirmed physical ARM64 device")
    abis = device.get("supportedAbis", [])
    if "arm64-v8a" not in abis:
        parser.error("Selected physical device does not declare ARM64 compatibility")
    versions = device.get("supportedVersionIds", [])
    if args.android_version not in versions:
        parser.error("Selected Android version is not supported on physical model")
    command = [
        gcloud, "--project=" + args.project, "firebase", "test",
        "android", "run", "--type=game-loop", "--app=" + str(args.apk),
        "--device=model=" + args.device_model + ",version=" +
        args.android_version + ",locale=en,orientation=portrait",
        "--scenario-numbers=" + ",".join(scenarios), "--timeout=8m",
        "--client-details=matrixLabel=M11_ARM64_REAL_PLAYER",
    ]
    print("ONE physical Firebase Test Lab Game Loop run. No automatic retry.")
    result = subprocess.run(command, check=False)
    print("M11_FIREBASE_MATRIX_EXIT=" + str(result.returncode))
    print("Matrix exit code does not independently prove M11 markers.")
    return result.returncode


if __name__ == "__main__":
    sys.exit(main())
