# ENG-M00-010 clean-room behavior contracts

This document records original, narrow behavior rules informed by high-level descriptions in the two M009 `REFERENCE_ONLY` sources. The implementations and fixtures below were written independently for this repository. No reference source code, assets, scene, prefab, level, or test was copied or adapted.

## Reference boundary

- [Rimaethon/Gem-Match3 at `fc17afbd324cd19337f7bc8c62e14407ac1fae87`](https://github.com/Rimaethon/Gem-Match3/tree/fc17afbd324cd19337f7bc8c62e14407ac1fae87) — the M009 rights inventory records CC BY-NC-ND 4.0 and classifies the repository as reference-only. The high-level idea used here is progress for clearing a chosen item type toward an objective.
- [thefcan/unity-match3 at `60ad2e2ed9ebbed7d646f41ae9861832d3799e40`](https://github.com/thefcan/unity-match3/tree/60ad2e2ed9ebbed7d646f41ae9861832d3799e40) — the M009 rights inventory found no explicit source reuse license and classifies the repository as reference-only. The high-level idea used here is a rising score multiplier across cascade waves.

These references receive idea attribution only. No protected expression, names, artwork, layout, authored level, test, or source implementation is included. Rights classification details and immutable license-file blob IDs remain in [`THIRD_PARTY_PROVENANCE.md`](THIRD_PARTY_PROVENANCE.md).

## Contract A: target-item collection goal

Input is the SDK's `SolvedData<TGridSlot>` for one resolved match wave. Count each distinct slot at most once, and only when it is movable, still contains an item, and passes the caller's target predicate. Keep progress monotonic and capped at the positive target count. Raise the SDK goal-achieved event exactly once when progress reaches the target. Non-target, empty, immovable, duplicate, or later-wave observations after completion do not add progress.

`SdkTargetCollectionGoal<TGridSlot>` is a thin adapter over the pinned SDK's `LevelGoal<TGridSlot>` and solved-slot enumeration. It leaves sequence detection, swap validation, and rollback to the SDK.

## Contract B: cascade score

For an accepted move, number match waves from zero. Award 10 points per distinct cleared item multiplied by `min(waveIndex + 1, 4)`. Wave zero uses multiplier 1; each following cascade raises it by one until the cap of 4. A zero-item wave awards zero. Negative item counts or wave indices are invalid. A rejected swap does not create a solved wave and receives no award. The wave index starts again at zero on the next move.

`DemoSettleJob` applies this rule to the distinct slots returned by SDK `SolvedData` for each SDK solver result. The SDK solver remains the only match detector.

## Independent fixtures and planned scope

`CleanRoomBehaviorContractTests` creates its own small SDK-compatible slot fixtures and solved sequences; it does not import or mirror tests from either reference repository. It verifies objective progress, overlap de-duplication, ignored inputs, one-time completion, multiplier growth, cap behavior, zero awards, and invalid inputs. A PlayMode assertion verifies the adapter behavior through the neutral demo's real SDK match/refill/cascade path.

Blockers and generators, auto-shuffle and dead-board handling, boosters, authored levels, and campaign systems remain planned for their owning milestones. M00 implements only the two narrow contracts above.
