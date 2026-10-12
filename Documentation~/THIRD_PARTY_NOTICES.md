# Third-party notices

## LibraStack Match3-SDK

Source: [LibraStack/Match3-SDK](https://github.com/LibraStack/Match3-SDK/tree/771e62908056c2c5fa69937a52ca52ab589f19ea), repository URL `https://github.com/LibraStack/Match3-SDK.git`, tag `v0.1.3` resolves to commit `771e62908056c2c5fa69937a52ca52ab589f19ea`.

Package: `com.chebanovdd.match3sdk` version `0.1.3`, imported from `src/Match3.UnityPackage/Assets/Plugins/Match3`. The package contains Core, App, and Infrastructure C# source assemblies. The direct package URL is pinned to the full commit in `Packages/manifest.json`. Preserve this notice with the SDK source and review upstream license changes before updating the commit.

Upstream LICENSE file Git blob SHA-1: `204c8f1b8226e38c428c31c716bcd109810f73b6`.

```text
MIT License

Copyright (c) 2022 Dima

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

## UniTask

Source: [Cysharp/UniTask](https://github.com/Cysharp/UniTask/tree/2e993ff18f28c931602a07292df0b0804eebef99), repository URL `https://github.com/Cysharp/UniTask.git`, release tag `2.5.11` resolves to commit `2e993ff18f28c931602a07292df0b0804eebef99`.

Package: `com.cysharp.unitask` version `2.5.11`, imported from `src/UniTask/Assets/Plugins/UniTask`. UniTask is a declared dependency of Match3-SDK. Pinning it directly prevents the SDK's unpinned transitive URL from selecting an unreviewed moving revision.

Upstream LICENSE file Git blob SHA-1: `e44b59b10deacda8e7d1a81555429cdb3f7d12c8`. Keep this notice with the package and review upstream license changes before updating the commit.

```text
The MIT License (MIT)

Copyright (c) 2019 Yoshifumi Kawai / Cysharp, Inc.

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

## Update procedure

For either package, inspect the upstream release/tag and its resolved commit, package manifest, source tree, and `LICENSE` before changing the pinned URL. Review the full source diff for dependency and API changes. If the license or bundled file terms differ, stop and update this notice and the provenance matrix before adoption. Update both `Packages/manifest.json` and `Packages/packages-lock.json` together, run the dependency and provenance policy checks, then run Unity package resolution. Never advance a package from a floating branch URL.

## Explicit exclusions

The SDK sample contains bundled DOTween DLLs and other sample art/media. These are not included in this project's dependency manifest. Their individual license and redistribution terms have not been approved for this project. No other match-3 source, scenes, levels, sprites, audio, or tutorial assets are copied by ENG-M00.
