---
name: jobbpilot-review-discipline
description: >
  Procedure for closing out a JobbPilot review cycle once a mandatory agent has
  already ruled: batching the findings, writing fixes that create no new
  reviewable claims, isolating the fix delta, and closing Blocker/Major findings
  via scoped report-only re-checks by the agent that issued them. Use after a
  verdict exists — not during the first review round; CLAUDE.md §9.6 governs
  what happens there. Triggers on: re-review, re-check,
  omgranskning, omkontroll, granskningsrunda, review round, scoped re-check,
  report-only, inga editeringar, agents-done, new-in-delta, fix efter review,
  batcha fixar, isolera deltat.
---

# JobbPilot Review Discipline

> **This skill is the *how*. The norms live elsewhere and win over this file:**
> - Where a finding goes, and the re-check norm → `CLAUDE.md` §9.6
> - Label semantics (`automerge` = intent, `agents-done` = permission) → `CLAUDE.md` §6
> - Severity → the reporting agent's own charter. Never re-graded here.
> - Comment discipline (author side) → `CLAUDE.md` §5 `Comments:`
>
> If this file disagrees with §9.6, §9.6 is right.
>
> **Scope:** this is the *closing* half of a review cycle — it applies once an
> agent has ruled. In a first round, §9.6 governs what happens; this skill does not apply.

---

## Why this exists

Measured, not asserted — across PRs #1412–#1422: 72,7 % of round-≥2 findings sat
in prose an earlier fix added, and **no substantive deletion-only or code-only
fix delta was ever submitted for re-review**: the remedy this skill now mandates
had never been tried. The 2026-08-04/05 rules did not hold because this skill's
Step 2 permitted reformulation — a reformulation is a new claim. Numbers and
regeneration: `docs/spec-rationale.md` §9.6.

The mechanism is arithmetic, not bad luck: **every fix that carries prose adds
new reviewable claims, so round N's explanations become round N+1's findings.**
The reviewers are right every round — the artefact is too talkative.

The counter-measurement (2026-08-09, PRs #1249/#1254): a scoped re-check
returned **0 blocking findings in under three minutes**, against full rounds of
twenty minutes that generated fresh sentences to defend. One reviewer even
withdrew its own finding — "the finding does not hold" is a real outcome (§9.6).

---

## Step 1 — Batch before you fix

| ✅ Ja | ❌ Nej |
|---|---|
| Wait in **every** mandatory agent, then fix all reports as one batch | Fix agent 1's findings while agent 2 is still reviewing |
| One Major = one `TodoWrite` row, ticked only after re-run | One row for "fix review findings" |
| **Commit the batch before you tick the fix todos** | Tick a code todo with a dirty tree |

That last row is mechanical, not stylistic: `post-todo-review.sh` asks for a full
`code-reviewer` when a completed todo matches its keyword list **and** the tree
carries uncommitted `.cs/.ts/.tsx/.razor/.cshtml`. Mid-batch that is a fresh
verdict against a moving tree — a round you created yourself. Commit first and
the hook exits silently.

---

## Step 2 — Write the fix so it creates no new claims

| ✅ Ja | ❌ Nej |
|---|---|
| Fix reasoning in the **commit message** — it is not reviewed as code | A comment above the fix explaining why it is correct |
| Publish the **command that regenerates** a number | Publish a live number in a tracked file (§5 `Comments:`) |
| Run the fix's comments against §5 `Comments:` before you commit | Prose restating the next line, or re-arguing an ADR |
| Measure the fix **the way the defect was measured**, before push | Report a fix as landed because the edit succeeded |
| Close a prose finding by **deleting the claim-sentence** — mechanical closure per §9.6's three conditions | Reformulating the sentence — a reformulation is a new claim |
| Fix delta adds **zero claim-sentences** — read the diff's added lines before commit | An added sentence explaining why the fix is right |

Deletion is the only fix form that cannot introduce a new finding.

Three of #1206's rounds existed only because a fix was reported as landed with
no counter-check — one of them a PR body that was empty.

---

## Step 3 — Isolate the delta

```bash
git fetch origin main --quiet
git log --no-merges --oneline <verdict-sha>..HEAD --not origin/main
git show --stat <fix-sha>                            # file scope per commit
```

`--not origin/main` is the load-bearing part, and it is the only form that errs
in neither direction (measured 2026-08-09 on this repo's history):

| Form | Base merge pulls in a sibling's commits | You merged your own topic branch in |
|---|---|---|
| `git diff A..B` | ❌ admits them — `bd0df72b..ac329eb1` reported **69 files** including a parallel lane's SES work | — |
| `git log --no-merges A..B` | ❌ still admits them (it drops the *merge commit*, not what the merge brought) — 3 commits on that same range | ✅ |
| `--first-parent` | ✅ 1 commit | ❌ **hides your own unreviewed work** — `97022d25` (6 files) vanished from `feat/foretag-sok-live-commit` |
| `--not origin/main` | ✅ 1 commit | ✅ `97022d25` retained |

The `--first-parent` row is the dangerous one: it fails by *under*-inclusion, so
the reviewer never sees code that is merging. Fetch first — the exclusion is only
as current as the local `origin/main` ref.

---

## Step 4 — Scoped re-check, one per issuing agent

Send it to **the agent that issued the verdict** — `dotnet-architect` for its own
Kritiskt/Viktigt, `code-reviewer` for its own Majors (§9.6). **At most ONE scoped
re-check per issuing agent per PR** — overflow routing is §9.6's: deletion/code-only
fix closed mechanically, or STOPP to Klas.

Prompt template — the report-only clause is load-bearing:

```
REPORT-ONLY re-check — inga editeringar. You issued <N> findings on PR #<PR>;
your verdict was against commit <sha>. Fixes have landed. Scope: ONLY these
commits: <git log --no-merges --oneline <sha>..HEAD --not origin/main output>.

Per finding: is it closed by this delta — yes/no, verified with the same
measurement that established it? Raise no phrasing findings, and no new
findings on lines this delta did not touch — EXCEPT anything your own charter
grades as a Blocker, or any class your charter defines repo-wide rather than
per-diff. Those you always report, wherever you see them. A defect the delta
itself introduces is reported and marked "new in delta".

Report findings only, within your own charter's Output-format cap; no Q&A
sections, no methodology narration.

Do not edit any file. An edit is a content push, and a content push strips
agents-done (CLAUDE.md §6) — it tears down the gate you were invoked to close.
```

What the re-check raises is routed by §9.6, including its cap, its rule for a
new-in-delta Blocker/Major and its rule on in-block fixes during a re-check.

The PR-body verdict table — appended in ONE body edit, escalations verbatim below it:

```
| Agent | Verdict | B/M/m | HEAD granskad | Rapport |
|---|---|---|---|---|
```

---

## Step 5 — agents-done checklist

`agents-done` asserts that the mandatory agents answered **the diff that is
merging**. Before setting it:

1. Every mandatory agent (§9.2) has reported, or closed its findings via its own
   scoped re-check — **including any new-in-delta finding that re-check raised** —
   or the finding closed mechanically per §9.6's three conditions, or via §9.6's
   capped overflow route.
2. The PR-body verdict table + verbatim escalations are appended, in ONE edit.
3. No unresolved Blocker/Major, and no §12 merge-blocking condition.
4. `git log --oneline -1` still matches the SHA the **last re-check** answered —
   check it immediately before setting the label, not before the final fixes.
5. After the label, confirm it actually armed:
   `gh pr view <N> --json autoMergeRequest,mergeStateStatus`. The arm job re-reads
   the head and **no-ops with a `::notice::` if it moved since the label was set**
   (`label-automerge.yml`) — a green skip nothing surfaces, and `label-automerge`
   is not a required check. Head moved? Remove and re-add `agents-done` so the
   event re-fires against the current SHA.
6. Push nothing afterwards. `BEHIND` may be cleared with `gh pr update-branch` at
   any point — there is deliberately **no ordering rule** against the label
   (`docs/runbooks/parallel-sessions.md` §8.1), because a pure base merge does not
   disarm the gate. It does move the head, so step 5 applies afterwards.

---

## When this skill is not enough

- **Where a finding goes**, and which outcomes exist → `CLAUDE.md` §9.6
- **A finding's severity, or whether an exception applies** → the issuing
  agent's charter in `.claude/agents/`. Never re-grade against another table.
- **Ambiguous routing, or a finding whose disposal is not obvious** →
  `senior-cto-advisor`
- **Label arming, `mergeStateStatus`, stuck PRs** → `CLAUDE.md` §6 and
  `docs/runbooks/parallel-sessions.md` §8.1
- **Worktree, hotspot and migration rules** → `CLAUDE.md` §6.5
