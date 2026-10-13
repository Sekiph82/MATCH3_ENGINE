# ENG-M01 reusable Unity 6.6 architecture

Master spec: https://docs.google.com/document/d/1Trf1URFaDwkWM8T9s4LUFce5e4NRyCqgOKqQfrcNAGM/edit
Frozen audit: https://docs.google.com/document/d/1IJv-hq0BEtfWZqFW49I7dlLt4JUHpcmcVZ8GnMa-wnA/edit
Single status authority: root TASKS.md. No duplicate tracker.

## Components
- Existing UPM com.match3.engine package version 0.1.0 and pure C# Match3.Engine.Runtime assembly have no UnityEngine, UnityEditor, theme or GameObject dependency. EngineCompatibility.Api v1.0.0 is a separate contract version.
- Match3-SDK at 771e62908056c2c5fa69937a52ca52ab589f19ea remains sole swap, match, refill, rollback and scoring-board authority. UniTask is pinned at 2e993ff18f28c931602a07292df0b0804eebef99. Preserve MIT notices.
- GameProfile has typed immutable ID, neutral theme binding, age/content policy, schema v1 and a copied feature list. Five internal IDs: Coral_Rescue, Garage_Legends, Dino_Islands, The_Fractured_Realms, Wonder_Pop. These are placeholders with optional gameplay/commerce features OFF until owner approval.
- FeatureCatalog IDs: timed, endless, collection, match_2x2, targeted_missile, conveyor, portal, countdown_bomb, egg, daily, weekly, catch_up, achievements. Unsupported version, unknown ID, duplicate or timed+endless conflict rejected.
- PluginRegistry: versioned neutral domain factories of kinds obstacle/goal/special/booster, usable only with enabled capability. IDs unique across plugin kinds.
- GameplayEventJournal: immutable ordered event snapshots; trusted internal publisher, observer-only subscribers; provider failures isolated. Terminal win/loss closes the journal.
- EngineStateMachine: internal guarded Boot->Loading->Ready->Swapping->Resolving->Ready/Won/Lost, pause/resume and cancel generation fencing. UI, analytics, ads, haptics and tutorials cannot mutate its state.
- Only adapters outside pure core know Unity rendering and SDK objects. Do not implement a competing match resolver or import source/media from restricted reference repositories.

## Rights, build and audit policy
- Source provenance: LibraStack and UniTask MIT at immutable SHA; no Rimaethon CC BY-NC-ND, unlicensed thefcan/Daltonbr, restricted Unity Companion or ambiguously licensed UltraGem assets/code copies. See THIRD_PARTY_PROVENANCE.md and THIRD_PARTY_NOTICES.md.
- Real Unity 6000.6.5f1 GitHub-hosted CI, original SDK pointer-swap, EditMode and PlayMode XML positive and negative fixtures, deliberate failed NUnit assertion nonzero, fresh UPM import and final actual Windows64 build with forbidden-fixture scanner are required. Do not infer PASS from source-only commits.
- Not demonstrated: local Windows workspace synchronization, Windows executable launch, Android/iOS builds or device certification. These cannot be claimed from hosted GitHub CI.
