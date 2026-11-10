# ENG-M14-002: LevelPlay Mediation Policy

## Preferred mediation path

Unity LevelPlay (IronSource mediation layer) is the **preferred** rewarded-ad
mediation path for all Match3 titles that carry ads. The deprecated Unity Ads
Legacy direct integration (`com.unity.ads` Advertisement API) **must not** be
used as the default path in new code. Any remaining Legacy call sites must be
migrated to route through `IAdMediationProvider` / `AdMediationProviderRegistry`.

## SDK installation gate

Actual installation of the LevelPlay/IronSource SDK adapter packages
(`com.unity.ads`, IronSource Unity adapter, mediation network adapters) requires:

1. **Explicit owner approval** prior to any UPM manifest change.
2. **Sandbox validation** per task `ENG-M14-013` — including Unity 6.x
   compatibility check, adapter version pinning, and a confirmed build passing
   without ad-network network calls in offline/editor mode.

No SDK package may be added to `Packages/manifest.json` or
`Packages/packages-lock.json` without that gate passing.

## Wonder Pop exclusion

Wonder Pop (`Sekiph82/Wonder_Pop`) **must not** use any ad mediation SDK.
The game's `IAdsService` implementation must remain the null stub, and the
`AdMediationProviderRegistry` must never be called with a live provider from
Wonder Pop code paths. Enforcement is via the game's profile flag checked before
any `IAdsService` call site.

## Adapter assembly isolation

Any real SDK adapter (LevelPlay, AdMob, AppLovin, etc.) must be shipped in a
**separate Assembly Definition** that:

- Is excluded from the core `com.match3.engine` package asmdef.
- Declares an explicit `asmdef` reference to `Match3.Engine.Runtime` only.
- Is placed outside `Packages/com.match3.engine/Runtime/`.
- Is never auto-referenced.

This keeps the core engine free of SDK dependencies and ensures tree-shaking
removes the adapter for builds where ads are disabled.
