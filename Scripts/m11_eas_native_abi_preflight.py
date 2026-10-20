#!/usr/bin/env python3
"""Offline preflight: require compatible native libs in Unity EAS QA artifact."""
import argparse
import json
import pathlib
import zipfile

p=argparse.ArgumentParser()
p.add_argument("--apk",required=True)
p.add_argument("--pbxproj",required=True)
p.add_argument("--android-abi",default="x86_64")
p.add_argument("--ios-abi",default="arm64")
a=p.parse_args()
with zipfile.ZipFile(a.apk) as z:
    names=set(z.namelist())
lib="lib/"+a.android_abi+"/"
checks={
  "android_unity":lib+"libunity.so" in names,
  "android_il2cpp":lib+"libil2cpp.so" in names,
}
txt=pathlib.Path(a.pbxproj).read_text()
checks["ios_simulator"]="SUPPORTED_PLATFORMS = iphonesimulator;" in txt
checks["ios_architecture"]="ARCHS = "+a.ios_abi+";" in txt
checks["ios_framework"]="UnityFramework" in txt
print(json.dumps(checks,indent=2))
raise SystemExit(0 if all(checks.values()) else 2)
