---
name: jobbpilot-review-discipline
description: >
  Procedure for JobbPilot's PR review cycle: batching agent findings, writing
  fixes that create no new reviewable claims, isolating the fix delta, and
  closing Blocker/Major findings via scoped report-only re-checks by the agent
  that issued them. Use when a mandatory agent has reported findings, when a fix
  lands after an agent's verdict, when preparing to set the agents-done label,
  or at PR creation. Triggers on: review, re-review, re-check, omgranskning,
  granskningsrunda, review round, finding, fynd, Blocker, Major, verdict, dom,
  agents-done, automerge, gh pr create, PR body, delta, git log --no-merges,
  report-only, inga editeringar, batcha fixar, fix commit.
---

# JobbPilot Review Discipline

> **This skill is the *how*. The norms live elsewhere and win over this file:**
> - Where a finding goes, and the re-check norm → `CLAUDE.md` §9.6
> - Label semantics (`automerge` = intent, `agents-done` = permission) → `CLAUDE.md` §6
> - Severity → the reporting agent's own charter. Never re-graded here.
> - Comment discipline (author side) → `CLAUDE.md` §5 `Comments:`
>
> If this file disagrees with §9.6, §9.6 is right.

---

## Why this exists

Measured, not asserted. **PR #1206 took 11 review rounds and zero of its ~16
findings were code defects.** PR #1220/#1221 carried ~11 real defects against
~20 findings that were only sentences. A guard file shipped in that batch was
70 % comment; the PR added 564 comment lines against 405 code lines.

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

That last row is mechanical, not stylistic: `post-todo-review.sh` invokes a full
`code-reviewer` when a completed todo looks like code work **and** the worktree
is dirty. Mid-batch that produces a fresh verdict against a moving tree — a new
round you created yourself. A clean tree exits the hook silently.

---

## Step 2 — Write the fix so it creates no new claims

| ✅ Ja | ❌ Nej |
|---|---|
| Fix reasoning in the **commit message** — it is not reviewed as code | A comment above the fix explaining why it is correct |
| Publish the **command that regenerates** a number | Publish the number in a tracked file — it decays within 1–2 commits |
| Comment only where the code cannot show the thing itself (§5) | Prose restating the next line, or re-arguing an ADR |
| Measure the fix **the way the defect was measured**, before push | Report a fix as landed because the edit succeeded |

Three of #1206's rounds existed only because a fix was reported as landed with
no counter-check — one of them a PR body that was empty.

---

## Step 3 — Isolate the delta

```bash
git log --no-merges --oneline <verdict-sha>..HEAD   # your fix commits, only yours
git show --stat <fix-sha>                            # file scope per commit
```

❌ **Never a two-dot diff for review scope.** It spans the base merge:
`bd0df72b..ac329eb1` reported **69 files** and pulled a parallel lane's SES work
into the review scope. See `reference_two_dot_diff_range_spans_the_base_merge`.

---

## Step 4 — Scoped re-check, one per issuing agent

Send it to **the agent that issued the verdict** — `dotnet-architect` for its own
Kritiskt/Viktigt, `code-reviewer` for its own Majors. Only the issuer can say
whether its finding is closed; a fresh reviewer re-reviews the whole PR.

Prompt template — the report-only clause is load-bearing:

```
REPORT-ONLY re-check — inga editeringar. You issued <N> findings on PR #<PR>;
your verdict was against commit <sha>. Fixes have landed. Scope: ONLY these
commits: <git log --no-merges --oneline output>.

Per finding: is it closed by this delta — yes/no, verified with the same
measurement that established it? Raise no new findings on unchanged lines and
no phrasing findings. A NEW defect the delta itself introduces is reported,
marked "new in delta".

Do not edit any file. An edit is a content push, and a content push strips
agents-done (CLAUDE.md §6) — it tears down the gate you were invoked to close.
```

Non-blocking findings the re-check does raise are routed by
`senior-cto-advisor` per §9.6 and are **not fixed in-block** — every in-block fix
invalidates the check just run.

---

## Step 5 — agents-done checklist

`agents-done` asserts that the mandatory agents answered **the diff that is
merging**. Before setting it:

1. Every mandatory agent (§9.2) has reported, or closed its findings via its own
   scoped re-check.
2. No unresolved Blocker/Major, and no §12 merge-blocking condition.
3. `git log --oneline -1` matches the SHA the verdicts answered — verify
   **immediately** before setting the label, not before the last round of fixes.
4. `gh pr view <N> --json mergeStateStatus` — `BEHIND` is fixed with
   `gh pr update-branch` **before** the label, never after (a pure base merge
   does not disarm the gate; a content push does).
5. Push nothing afterwards.

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
