# Session protocol — detailed mechanics

Extracted from CLAUDE.md §1.5 (2026-06-12). CLAUDE.md keeps the mandatory core;
this runbook holds the detailed formats and rationale.

## Session start

1. Read `docs/current-work.md` (status from previous session).
2. Read the latest file in `docs/sessions/` for recent context.
3. `git log --oneline -8` — verify HEAD matches current-work.md.
4. If hooks should be active: verify `bash .claude/hooks/session-start.sh`
   produces output.

## During the session

- Track multi-step work with TodoWrite; mark todos completed only when
  verified working (post-todo-review hook triggers code-reviewer on completed
  code todos).
- Pause and ask Klas before deviating from the planned step.

## After each STEG completion (and at session end)

Sync docs at STEG completion, not only session end — otherwise pushed state
lies about reality if context is lost mid-session:

1. Update `docs/current-work.md`: status header (current + next step),
   delivered-this-session block, commit table, pending list.
2. Update `docs/steg-tracker.md` if a STEG changed status.
3. Create a session log in `docs/sessions/YYYY-MM-DD-HHMM-<slug>.md`:
   - YAML frontmatter: `session`, `datum`, `slug`, `status`, `commits`
   - Body: goals, what was completed per step, decisions and detours (not
     what commit messages already say), next session
   - Medium detail; sessions 3–4 in `docs/sessions/` are reference templates
4. Commit docs updates as **separate logical commits** in **the same PR as the
   scope** (ADR 0065 — never a separate docs-only PR).

## At session end only

Generate the start prompt for the next session per
`docs/runbooks/session-start-template.md` (4 sections):

- Delivered as a copy-paste block in chat — **never** a repo file
- Self-contained for a `/clear` session but lean — CLAUDE.md, MEMORY.md and
  the SessionStart hook load automatically; do not duplicate them
- Real values (verified HEAD SHA, dates, paths), never placeholders
- If a start prompt missed something critical, update the template in the
  same session

## Current-work trimming

When `docs/current-work.md` grows past ~10–15 KB, move older delivered-session
blocks to `docs/current-work-archive.md` (full body, reverse chronology, new
archivals at the top under the header). The SessionStart hook injects an
excerpt of current-work.md every session — its size is a per-session token
cost.
