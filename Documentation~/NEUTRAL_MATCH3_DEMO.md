# Neutral Match-3 Engine Demo

Open `Scenes/NeutralMatch3Demo.unity` in Unity 6.6 and enter Play mode. Click two edge-sharing tiles to try a move. Invalid moves restore the original board; the fixed seed makes the included match, cascade, and win path repeatable.

## Implementation notes

- The demo delegates swaps, swap rollback, sequence detection, solved data, and the goal lifecycle to the pinned LibraStack Match3-SDK dependency. Its refill strategy only clears solved slots, applies gravity, supplies seeded pieces, and asks the same SDK solver to check the settled board for cascades.
- `Tests/PlayMode/NeutralMatch3DemoPlayModeTests.cs` covers an invalid adjacent swap and a fixed-seed three-match that refills, cascades once, and reaches the goal.
- Board snapshots use row-major rows separated by `/`; item IDs `0` through `3` display as `A` through `D`. SHA-256 hashes cover the UTF-8 snapshot text with no trailing newline.
- The sample uses Unity IMGUI and generated colors only. It does not include product branding, downloaded art, analytics, network calls, or persistence.
- The pinned Match3-SDK and UniTask package metadata both declare MIT licenses. Keep their upstream license notices with any redistribution of those dependencies.
- The scene and sample are compatibility evidence for the pinned SDK in Unity Editor 6000.6.5f1. They do not establish player-build, mobile-device, or distribution acceptance.
