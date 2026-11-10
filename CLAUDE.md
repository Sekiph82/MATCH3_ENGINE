# MATCH3_ENGINE | Claude Code project ownership

**Owner handover effective 2026-11-10. Claude Code is the sole execution and orchestration agent for the entire remaining project.** The authoritative task ledger is root [TASKS.md](TASKS.md). The top `OWNER HANDOVER` entry in that file overrides previous **actor/assignment** rules referring to Codex or ChatGPT. It does not override previously approved game rules, acceptance tests, prerequisites, financial restrictions or owner-only choices.

## Startup contract (ALWAYS FIRST)
1. Use the owner's canonical Windows checkout: `C:\Users\sekip\Desktop\GAMES\MATCH3 GAMES\MATCH3_ENGINE`. Before changes inspect `git status --short`, branches, worktrees, ignored/untracked/modified content, `git rev-parse HEAD`, `origin/main` and GitHub `main`; fetch safely.
2. Preserve all owner-local state, including ProjectSettings, ServerData, ios-v12, M11Evidence, local evidence, any locally advanced M13 work and concurrent sessions. **Never run `git clean`, destructive `reset`, forced update, uncontrolled cleanup, or auto-overwrite uncommitted edits.** If remote moved, reconcile via safe explicit file-level review, no loss. Keep one main worktree; no unnecessary branches/clones.
3. Read current `TASKS.md` and this file; inspect current `.github/workflows/` triggers, official source and relevant Drive criteria. Identify the next **READY** `CLAUDE_OWNER` unchecked task, its exact dependencies, tests and audit rubric. Prefer first unresolved milestone, but you may work on genuinely independent source preparation when a native/owner gate is BLOCKED.
4. Independently recount unchecked/checked ENG tasks before any report. Handover snapshot: **153 checked + 179 open = 332 tasks**. Counts are not a claim that all 179 are executable immediately.

## Own the entire remaining delivery
Claude performs task selection, design, source edits, bug fixes, unit/PlayMode/actual Player tests where possible, build configuration, reproducible original evidence, security review, Drive logging, safe GitHub publication, and `TASKS.md` lifecycle updates. **Do not hand work to Codex or wait for a ChatGPT executor.**

Run an ongoing task loop within your **active session**:
- SELECT next legitimately READY open ENG task from root `TASKS.md`, preserving owner-approved priority and milestones.
- IMPLEMENT actual source/test changes; inspect existing modules and licenses rather than reinvent. Avoid unrelated projects.
- TEST real positive and negative cases locally in Unity 6000.6.5f1 (Windows), or on an independently available authorized genuine platform. Never fabricate device/network/gateway evidence or convert source compilation to Player acceptance.
- AUDIT with a **separate independent Claude reviewer agent or isolated review session** using `.claude/agents/match3-independent-auditor.md`. Builder must not self-sign independent PASS. Freeze criteria before evaluating, collect original logs/SHAs and record FAIL/PARTIAL/NOT_RUN truthfully.
- DOCUMENT in the existing Drive folders; update **only root `TASKS.md`** status/check boxes after actual reviewer-backed acceptance. On audit FAIL, create a bounded remediation cycle and retest. No second task ledger and no premature milestone closure.
- PUBLISH checked source safely to `main`, confirm SHA parity and absence of workflows; then select the next task without asking the owner for another generic prompt. Hand off only on authentic owner authorization/security/device/cost blockers.
- If blocked, record the exact reason and keep it OPEN; work on safe preparation or another independent READY task. Revisit blockers later. Never pretend a blocked task passed or break prerequisite gates.

**Milestone gate:** ENG-M11 is currently 11/14, with `ENG-M11-006 Offline fallback`, `ENG-M11-008 Addressables adapter`, `ENG-M11-013 User-visible patch flow` OPEN. M12 acceptance is GATED. The rest of M12–M24 contains unchecked tasks, but no false dependency waiver is granted.

**Owner-only product policy:** For M13-005 and later child-targeted policies, do not invent approved ages, countries, ad/SKU/telemetry/monetization choices. Keep fail-closed defaults and prepare an explicit decision table. Do not pause unrelated safe engineering waiting for those business decisions. Release, paid services and legally sensitive choices still need real owner approval.

## Unity / test / security requirements
- Unity `6000.6.5f1`, Windows 11. Engine must remain game-neutral with five isolated game profiles; no unauthorized cross-game brands/assets or unapproved SDKs.
- No Android physical phone or Mac is available at the owner's local setup; **no Android emulator**. Don't claim cloud/native access exists merely because a script references it.
- Retain official signed content rules, RSA/SHA/TLS and fail-closed remote loading; no remote executable hotpatches. Keep test assemblies, fixtures, diagnostics, screenshots and audit evidence out of release Player builds.
- Local Editor tests, static validation and genuine Windows builds are allowed if they protect owner files and other agent sessions. Rerun targeted suites after relevant changes; full expensive suite only when contract requires it. Cite source exact SHA, commands, outputs, Unity version, exit codes and real screenshots where applicable.
- Owner-approved V21 Firebase/EAS one-time slots were consumed. **New Firebase, EAS, GitHub Actions, CI, remote device/cloud workloads, paid resources, upgrades or redeploys are NOT authorized.** Obtain fresh narrowly scoped owner permission and verify actual account plan/quota/zero spend before any exception. No Windows network changes or disruption of other running AI agents.
- Never silently weaken security, age policy or acceptance assertions to obtain a green result.

## GitHub publication and permissions
- `main` is canonical. Before publishing inspect protected owner-local paths, staged diff, secrets, active repo workflows and branch parity. Do not stage unknown data.
- **No GitHub Actions usage.** Never dispatch or rerun any workflow or edit workflows to disable checks. For a source commit that could match `push` or `pull_request` workflows, use a commit message containing `[skip ci]` and independently verify current workflow triggers and zero runs for the exact commit. If zero-CI publication cannot be assured, keep tested changes locally and clearly report the blocker rather than triggering CI.
- Only non-force fast-forward main publication; no history rewrites, no credential changes, no changing repo visibility or collaborators without separate approval.
- Shared `.claude/settings.json` grants common local editing/Git/test permissions. It cannot install the Claude GitHub App, authenticate GitHub/Drive, bypass managed permissions or create paid-provider entitlements. Claude should use the owner's already authorized Git credentials; any missing sign-in or external GitHub App installation must be reported accurately.

## Evidence and review locations (existing structure, no renaming)
- Drive root: https://drive.google.com/drive/folders/1P4IYrnb56uQup6mqT3gC5iyI5vGX0Yti
- `01_PROMPTS`: https://drive.google.com/drive/folders/1x9INdvex6l7b8UVsurZhYBZ2aHpRr9lt
- `02_CODEX_LOGS` (historical folder name, **new documents named `CLAUDE_...`**): https://drive.google.com/drive/folders/1Hgn5a43tW3Bu8zpXuQmA--yUu-3pEUQk
- `03_AUDIT_CRITERIA`: https://drive.google.com/drive/folders/11a6Fwv7XHPtk0wqGtQ9SFO05OTsXE6U8
- `04_AUDIT_RESULTS`: https://drive.google.com/drive/folders/17qK_cImcLofgR5HBZ6vn2ygKnY3p56dz
- `05_EVIDENCE`: https://drive.google.com/drive/folders/13IKhwUZpBNMgvmYE0jGB8n5O_8K61vMD
- `06_RELEASES`: https://drive.google.com/drive/folders/1xqXt2qaF8sSJQrH1PiKyjg9agDFa_fpz
- Prompts/logs/audit criteria/results/evidence belong in Drive, NOT GitHub. `TASKS.md` is the only project progress tracker in GitHub. Do not claim a Drive document was written unless you have actually used a writable connection and verified it. Missing Drive access blocks **audit closure**, not necessarily safe local source work.

## Reporting and handoffs
For each real remediation or newly started task, deliver a compact handoff containing **task IDs, exact names, and the Google Drive full-prompt URL** when a prompt is stored. Provide exact starting/final SHAs, changed paths, actual executed tests, blocked external gates, independent-audit status and next task. No repeating the same question, no lengthy prompt pasted into chat and no claims of asynchronous background progress.
