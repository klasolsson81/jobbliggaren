# Session-start template

Structural guide for start prompts for new Claude Code sessions in JobbPilot.

**How it is used:** At session end, CC generates a start prompt for the next
session following this structure and delivers it as a copy-paste block in chat —
**never** as a new file in the repo.

**Design principle (rewritten 2026-06-12, CC cold review):** CLAUDE.md, the
MEMORY.md index, and the SessionStart hook (current-work excerpt) load
**automatically** in every session. The start prompt must therefore carry only
what those sources cannot: the task handoff. Do NOT duplicate discipline rules,
prohibitions, agent-invocation lists, or memory listings — they are already in
context, and duplication costs tokens twice and creates drift when the copies
diverge.

---

## Structure — four sections, ~15–25 lines total

### 1. Task + pre-flight

```
Hej. Klas-prompt: {phase/scope name} — {one-line delivery goal}.

Pre-flight (CLAUDE.md §6.5, Modell 1 — worktree-first, ALDRIG huvudkopian):
`git fetch origin`; `git worktree list` (se aktiva sessioner + deras branchar);
bekräfta att issuet inte redan är taget (`gh issue view {N}` + öppna PRs);
skapa + gå in i din EGNA worktree: `git worktree add c:/tmp/jbl-{slug}
origin/main -b {type}/{slug}` → `pwsh scripts/sync-worktree-docs.ps1
c:/tmp/jbl-{slug}` → `cd` in; claima issuet om det finns ett (`gh issue edit
{N} --add-assignee @me`); verifiera
HEAD = `{expected-sha}` (`{latest commit message snippet}`); Docker stack uppe.
ABORT om huvudkopians HEAD är en icke-main-branch — en annan session äger den.
```

### 2. Scope (the unique payload)

Numbered, concrete deliverables. Name files to create/change when known.
Reference the specific ADRs/BUILD.md sections THIS task needs (not generic
"read CLAUDE.md" — that loads automatically).

```
## Scope: {phase name}

Per {ADR reference}:
1. {Deliverable 1 — file pointer if known}
2. {Deliverable 2}

Task-specific reads: {only docs this task actually needs, e.g. ADR 00XX, TD-NN}
```

### 3. Discovery / web-search targets (when external facts are involved)

Per CLAUDE.md §9.5 — list what to verify and why, or state explicitly:
"No external discovery needed — task is purely internal."

```
## Discovery
- {What}: {why} — search "{concrete query}"
```

### 4. Expected end state

Concrete and verifiable. Includes the PR deliverable (ADR 0065).

```
## Expected end state
- {Deliverable verified}
- PR open against `main`, `ci` green, automerge label set, agent reports inline
- Docs-sync committed in the same PR
- {Task-specific Klas-STOPP flags, only if any — e.g. spec-edit, deploy}
```

---

## Delivery rules

- Always a fenced copy-paste block in chat, never a repo file
- Self-contained for a `/clear` session, but lean — trust the auto-loaded context
- Real values, never placeholders: verified HEAD SHA, dates, file paths
- If CC or Klas discovers a start prompt missed something critical, update THIS
  template in the same session

## Version history

- **2026-06-12:** Rewritten from 12 sections to 4 (CC cold review, Klas-approved
  plan). Removed: mandatory-reads list, memory list, discipline section,
  prohibitions section, pending-operative section — all duplicated auto-loaded
  context (CLAUDE.md §1.5/§9.2, MEMORY.md, ADR 0065). Saves ~2–3k tokens per
  session start and removes drift risk. Language switched to English per the
  same session's language decision.
- **2026-06-10:** AWS cleanup per ADR 0066 (superseded by 2026-06-12 rewrite).
- **2026-05-25:** PR-flow update per ADR 0065 (superseded by 2026-06-12 rewrite).
- **2026-05-13:** Created after Klas directive to standardize start prompts.
