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
Recommended run mode: {High | Max (xhigh) | Ultra (ultracode)} + {Plan-first | Auto} — {one-line why, calibrated to task size; see the Run-mode rubric below}.

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

Concrete and verifiable. Includes the PR deliverable (ADR 0065) and the close-out — a
PR that merges but leaves its issue open is not done (CLAUDE.md §6.5).

```
## Expected end state
- {Deliverable verified}
- PR open against `main`, `ci` green, automerge label set, agent reports inline
- Docs-sync committed in the same PR
- Close-out: PR watched to MERGE (`BEHIND` → `gh pr update-branch`; automerge does
  NOT rebase); issue closed + `wip` dropped (squash drops `Closes #N`) — playbook §8.1.
  Worktree/branch reap belongs to the SessionStart reaper (ADR 0094), never to you
- {Task-specific Klas-STOPP flags, only if any — e.g. spec-edit, deploy}
```

---

## Run-mode rubric (quality first — round UP when unsure)

State a per-task **Recommended run mode** in section 1: an effort/orchestration tier
plus Plan-vs-Auto, calibrated to the task's size and risk.

Effort / orchestration:
- **High** — small, well-scoped, low-ambiguity, single-file or mechanical (a one-line fix + test, a copy/config tweak).
- **Max (xhigh)** — the default STEG: multi-file or non-trivial logic a single strong agent handles well.
- **Ultra (ultracode)** — big / broad / high-stakes: multi-subsystem work, audits/reviews, multi-approach design or research, migrations/sweeps, or anything security/PII/architecture-critical where breadth + adversarial verification cuts risk. **A big task requires Ultra.**

Plan vs Auto:
- **Plan-first** — large or ambiguous scope, architectural choices, or an unclear approach: design the plan, get GO, then execute (pairs with Ultra for big work).
- **Auto** — well-scoped and the approach is already CTO-bound/clear in the prompt, or the work is read-only/low-risk: execute autonomously.

Bias: quality > tempo (§9.6). When unsure, round up (Max over High; Ultra + Plan for anything big, risky, or security/PII/architecture-touching). Never under-power a big task. Ultra costs roughly 3–5x the tokens of a single Max agent (the extra buys breadth + verification, not parallelism) — worth it for big/high-stakes work, wasteful for narrow single-file tasks.

---

## Delivery rules

- Always a fenced copy-paste block in chat, never a repo file
- Self-contained for a `/clear` session, but lean — trust the auto-loaded context
- Real values, never placeholders: verified HEAD SHA, dates, file paths
- Recommend a run mode (effort tier + Plan/Auto) per the Run-mode rubric — quality first; a big/broad/high-stakes task gets Ultra (+ Plan when the approach is not obvious)
- If CC or Klas discovers a start prompt missed something critical, update THIS
  template in the same session

## Version history

- **2026-07-02:** Added the per-task "Recommended run mode" line (section 1) + the
  Run-mode rubric (High / Max / Ultra + Plan / Auto, calibrated to task size, quality
  first). Klas directive — deliberate, per-task mode selection; a big task requires Ultra.
- **2026-06-12:** Rewritten from 12 sections to 4 (CC cold review, Klas-approved
  plan). Removed: mandatory-reads list, memory list, discipline section,
  prohibitions section, pending-operative section — all duplicated auto-loaded
  context (CLAUDE.md §1.5/§9.2, MEMORY.md, ADR 0065). Saves ~2–3k tokens per
  session start and removes drift risk. Language switched to English per the
  same session's language decision.
- **2026-06-10:** AWS cleanup per ADR 0066 (superseded by 2026-06-12 rewrite).
- **2026-05-25:** PR-flow update per ADR 0065 (superseded by 2026-06-12 rewrite).
- **2026-05-13:** Created after Klas directive to standardize start prompts.
