# MATCH3 ENGINE | ENG-M11 V22 | CODEX HANDOFF

**Task authority:** [Root TASKS.md](../../TASKS.md). **Status:** M11 11/14, M12 GATED. **Source starting SHA:** 8aef0a52569cf9189910ae05c245ccd05eacd456.

## V22-000 | FIRST: local Windows / GitHub main source synchronization
Canonical directory: `C:\Users\sekip\Desktop\GAMES\MATCH3 GAMES\MATCH3_ENGINE`. First read TASKS.md and inspect GitHub main, origin/main, local HEAD, branches, worktrees and tracked/untracked/ignored changes. Preserve all five owner-local V03 editor/catalog files and V21 generated artifacts. Fetch and safely fast-forward canonical local main to the latest GitHub main, without reset/clean/force or overwriting changes. Reconcile conflicts safely. Verify and record HEAD == origin/main == GitHub main BEFORE remediation. After safe publication, repeat sync and SHA verification.

## ENG-M11-008 | Addressables adapter
Fix actual V21 Android `Addressables may not fetch an unsigned remote location` during `Addressables.InitializeAsync(false)`. Diagnose original stack and V21 catalog update suppression at commit 185a6da. Keep signed-only firewall, native catalogs and bundle byte checks. Test twice loading expected TextAsset, unsigned dependency rejection before GET, cancellation and no leaked global locators. Never weaken signature/TLS validation.

## ENG-M11-006 | Offline fallback
Fix genuine signed packaged startup and app-private persisted last-known-good restart failures on Android. Verify offline boot and different-process restart, authenticated content, playable 16-cell neutral board and zero save/progress/reward mutation. Add fault-injection and safe local tests.

## ENG-M11-013 | User-visible patch flow
Diagnose Android `M11_013_PLAYER_HTTPS_BAD_SIGNATURE=FAIL transport_or_probe_failure=ArgumentException` and HTTPS positive NOT_VERIFIED. Inspect real exception source, pinned RSA, URL/platform routing and signed transport. Preserve TLS/security enforcement. Test positive and negative signature, Retry, cancellation, progress/offline UI and saved-state integrity. Never claim simulated HTTPS as genuine Player proof.

## V22-iOS | ENG-M11-006/008/013 native packaging remediation
V21 EAS failed before test on missing macOS IL2CPP/Bee deploy_arm64 executables. Verify real local EAS archive contents, full .easignore rules, Xcode references and modes; preserve Bash 3.2 fix cadf2b7. Do not claim iOS runtime PASS.

## Execution boundaries and delivery
Run feasible local Unity 6000.6.5f1 EditMode/PlayMode and development Player tests. Safely fix ordinary failures and continue. All V21 Firebase and Expo one-time runs were consumed: **ZERO further Firebase, EAS, GitHub Actions or paid tests without fresh explicit approval**. Do not delete ios-v12, ServerData/iOS, M11Evidence, or other protected owner data; no Windows network changes or interruptions to other agents. Check workflow push-path triggers before any commit; do not push changes that trigger CI. No new branches/worktrees, destructive git operations or false acceptance.

Log tested code/SHAs, test counts and evidence in existing [05_EVIDENCE](https://drive.google.com/drive/folders/13IKhwUZpBNMgvmYE0jGB8n5O_8K61vMD), and one V22 execution log in 02_CODEX_LOGS. Source history and errors: [V21 final log](https://docs.google.com/document/d/1RFRBPjz26A6kCIIjkdb-y7WEMlT8kfGwcDE51wInuH4/edit). Maintain 006/008/013 OPEN and M12 GATED until actual independently audited mobile Player evidence. Complete all locally actionable code, tests and safe source sync now; report remaining blockers honestly.
