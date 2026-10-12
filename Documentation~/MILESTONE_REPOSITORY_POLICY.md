# Milestone repository and evidence boundary

GitHub `main` is the source-of-truth branch for runtime code, tests, Unity project settings, package manifests, licenses and product-facing technical documentation. Before a child task, fetch `origin/main`, confirm the local branch is synchronized, inspect working-tree changes, and preserve owner work. After a child, commit only reviewed source, tests, configuration, licenses and product documentation; push to `main`, fetch, and verify that `HEAD` matches `origin/main`.

The root `TASKS.md` is a protected owner and auditor handoff. Codex must not change it. Only ChatGPT updates its audit checkboxes and baton after independent review.

Drive is authoritative for implementation prompts, frozen audit criteria, Codex logs, audit results, handoff indexes and run evidence. Keep these files in the project Drive folders `02_CODEX_LOGS`, `04_AUDIT_RESULTS` and `05_EVIDENCE`. Do not commit those Drive artifacts or copies of them to GitHub. The repository policy validator rejects coordination directory paths and task artifact filenames, rejects any dirty or committed root `TASKS.md`, requires the `main` branch synchronized with `origin/main`, and checks that the pinned reuse-rights records and notices still validate.

Run the positive/negative policy fixtures with `pwsh -NoProfile -File Tests/Policy/MilestoneRepositoryPolicy.Tests.ps1`. Run the repository check on a synchronized checkout with `pwsh -NoProfile -File Tools/ValidateMilestoneRepositoryPolicy.ps1`. The project CI adapter also runs the policy fixtures, reuse-rights validator, and repository check. A local green result is implementation evidence; only the independent ChatGPT audit can determine task acceptance.

## Reuse and licensing

Preserve the reuse-first decision in `THIRD_PARTY_PROVENANCE.md`. Source can be reused only where an explicit license permits the intended modification and distribution, with exact pins and full required notices. Reference-only and unresolved-license projects contribute only independently written behavior specifications within their authorized task scope. Unity sample terms do not become a general commercial reuse grant. `THIRD_PARTY_NOTICES.md`, pinned manifest/lock entries and `ValidateThirdPartyProvenance.ps1` are part of this boundary.

## Evidence limits

Every log names the source commit, exact executed commands, Unity version, raw test counts, return codes and Drive evidence links. Redacted outputs identify the raw artifact hash and state which personal or local paths were masked. Missing hosted-runner, mobile package, signing or device evidence remains explicitly pending; it is never inferred from a local scanner or desktop player build.
