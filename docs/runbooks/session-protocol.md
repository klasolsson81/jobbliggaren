# Session protocol — detailed mechanics

CLAUDE.md §1.5 owns the session obligations; §6.5 owns worktree isolation and
§9 owns review. This runbook maps them to the driving runtime.

## Session start

1. Read `docs/current-work.md` in full, the current forward-plan section of
   `docs/steg-tracker.md`, and the latest relevant session log. Exclude
   `precompact-*`, archived snapshots and reaper logs when selecting a session log.
   Search older records when the task refers to their decisions or unresolved work.
2. Run `git log --oneline -8`, `git status --short` and `git worktree list`.
   Reconcile the actual checkout with the recorded status; do not reset a checkout
   to make it match. For file changes, enter an isolated worktree per §6.5 first.
3. Sync local context per §6.5. Check the target before and after copying:
   preserve tracked destination files and existing local edits. If the installed
   sync script would overwrite them, copy only missing local documents within
   `.worktreeinclude`, retaining its secret-file exclusions.
4. Confirm the task against the tracker and the GitHub `mvp` scope (§6.5).
   An explicit Klas request for tooling or maintenance supplies its own scope;
   record that reason without inventing a new product roadmap step.
5. In Claude Code, verify the configured SessionStart hook ran. In Codex, perform
   the checks below with available tools; do not claim a Claude hook ran.

Missing local context is not an empty backlog. Recover it from the main checkout
when available; otherwise report exactly what is missing before relying on it.

## Runtime mapping

`.claude/settings.json` configures Claude Code, not Codex. Its permissions and
hooks are not Codex enforcement. Codex's own permissions still apply.

| Claude mechanism | Codex execution |
|---|---|
| SessionStart diagnostics | Inspect Git state as above. For stack or test work, check `docker info`, `docker compose ps`, required local-file presence (never print secrets), and installed frontend versions against the package pins. Do not start or restart another session's stack. |
| TodoWrite | Use the available plan/task tool, or maintain a written checklist. Mark work complete only after verification. |
| PostToolUse formatting/type checks | Run the applicable §11 commands after the edit batch and before commit. Inspect any formatter changes. |
| PostTodo review reminder | Invoke the mandatory §9.2 panel explicitly; do not rely on a reminder hook. |
| PreCompact snapshot | Keep current-work and the session log current after each completed step, so compaction does not depend on an unavailable hook. |
| SessionEnd / worktree reaper | Land local session-state edits per §6.5 and report the worktree/PR status. Do not invoke the Claude lifecycle hooks or infer that another worktree is abandoned. |
| Claude tool names in charters | Use available tools for the same permitted action. Read/Grep/Glob mean read-only inspection; a shell transport does not grant permission to mutate. |

Git's Husky hooks and GitHub CI are shared gates. Verify
`git config --get core.hooksPath` and the hook installation in the checkout
used for the commit; do not bypass gates when adapting the runtime.
For shell guards on Windows, use Git Bash with its Unix tools on PATH, e.g.:

```powershell
& 'C:/Program Files/Git/bin/bash.exe' -c 'export PATH=/usr/bin:/bin:$PATH; bash .github/scripts/agents-md-budget-guard.sh'
```

## During and after each completed step

Keep a verified checklist. Before changing the authorized scope, follow §9.2.
After each completed step, not only at session end:

1. Update current-work with the actual status, active worktree, outstanding
   blockers/decisions and next step.
2. Update the tracker only when strategic order or phase status changes.
3. Write `docs/sessions/YYYY-MM-DD-HHMM-<slug>.md` with YAML fields
   `session`, `datum`, `slug`, `status`, `commits`, followed by outcomes,
   decisions, verification and handoff.
4. Tracked documentation changes belong in the scope PR. Local session state
   stays gitignored and is landed in the main copy per §6.5, with a conflict
   check before writing. Do not force-add private state to satisfy docs-sync.

Report artifacts are English; chat to Klas is Swedish (AGENTS.md §1).
Use `docs/runbooks/session-start-template.md` for the end-of-session handoff.

## Bounded current state and lossless history

Keep current-work around 10–15 KB. It holds current work and unresolved
obligations; session logs own detailed delivery narratives. Move older complete
blocks to `docs/current-work-archive.md`, preserving their full content.
The tracker holds strategic order and current decisions; move historical
delivery narratives to `docs/steg-tracker-archive.md`.

Before compacting either file, save a dated byte-for-byte snapshot under
`docs/sessions/archive/` and verify its SHA-256 against the source. Preserve
existing archives. Verify the source has not changed before replacing it.
Keep links to the archived material in the active files. Never mark uncertain
or unresolved work complete merely to meet the size target; retain its status
and source pointer. The backlog remains GitHub Issues.

Archives are read on demand. Do not make a full historical archive part of
every session's startup read.