# Codex Task Automation

This file configures sequential, cost-aware Codex work for this repository. It adds no task ledger and does not authorize product work by itself.

## Authority and task start

- Follow all existing repository instructions, the repository-declared canonical task source (normally root `TASKS.md`), and any active task prompt or handoff. More specific owner decisions and project gates take precedence.
- Before implementation, read the exact current task, its acceptance criteria, required prior audits/contracts, and any mandatory project-control files. If a required prompt, audit rubric, or authority file is unavailable or contradictory, stop and report the specific blocker.
- Start only the explicitly named current task when its state, required actor, prerequisites, and owner authorization permit Codex work. Never infer work from the first unchecked row, invent task scope, or cross a milestone gate.
- Continue through multiple tasks only when an existing owner-approved master prompt or canonical task state authorizes the exact ordered batch. Stop at the batch boundary, independent-audit handoff, owner decision, or blocker.
- Do not edit task status, audit results, or handoff state when this repository assigns those duties to an owner or independent auditor. When Codex is authorized to update state, update only the declared canonical files and required control-plane mirrors; never create a competing ledger.

## Sequential and cost-aware execution

- Use one implementation worker at a time. Add a read-only reviewer only when the repository workflow allows it and it does not replace the named independent auditor; do not spawn a large agent team for routine tasks.
- Run only task-required focused checks during implementation. Run the complete milestone acceptance suite once at its explicit closure, unless the active project criteria require a stricter check.
- Prefer the existing checkout when it is safe. If project rules allow a task-owned worktree, keep it isolated and remove only that exact worktree and branch after authorized publication. Preserve owner changes, caches, evidence, and unrelated temporary files.
- Avoid unnecessary dependency installs, repeated full suites, duplicate clones, workflow dispatches, paid services, and storage-heavy artifacts. Honor all project-specific CI, network, licensing, native-device, and release constraints.

## Completion and stop conditions

- Report exact files, commands, results, skipped checks, commits, and remaining owner/audit gates. Builder evidence is not independent acceptance.
- Publish only through the branch, review, or mainline procedure authorized by this repository's task source and owner instructions.
- Stop for required human approval, protected owner data, unavailable native/device access, unapproved external costs, or unresolved policy conflicts.