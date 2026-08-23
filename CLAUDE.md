# CLAUDE.md — Jobbliggaren coding conventions

@AGENTS.md

> **§-index — one shared namespace, each § in exactly one file.**
> `AGENTS.md`: §1 Identity · §1.6 Docs map · §2 Core principles · §3 C#/.NET ·
> §4 TS/Next.js · §5 Anti-patterns · §6 Commits/PR flow · §7 Testing · §8 DoD ·
> §10 Swedish UI · §12 When something looks wrong.
> This file: §1.5 Session protocol · §6.5 Parallel sessions · §9 Working with
> the driving session · §11 Tooling (budget valve, ADR 0135) · §13 Update process.
> A citation "CLAUDE.md §N" stays valid: it resolves here, then via this index.

## 1.5 Session protocol (mandatory)

**Start (mandatory roadmap-grounding — be tracker-driven, not prompt-driven):**
read `docs/current-work.md` **in full** + the `docs/steg-tracker.md` framåtplan
section + latest `docs/sessions/` log (the session-start hook's preview is not a
substitute for reading the files); verify HEAD via `git log --oneline -8`;
confirm the session-start hook ran. **Then confirm the session's task is the
right next step per the tracker before starting work — if the prompt diverges
from the tracker, flag it to Klas** rather than silently following either.
**Tracker and `mvp` answer different halves:** `steg-tracker.md` holds the strategic
sequence (what comes after what), the **`mvp` label holds the in-scope subset** (what is
on the path to real users at all — §6.5). Neither overrides the other; a task should
clear both, and where they disagree that is the thing to flag.
**During:** track multi-step work with TodoWrite; mark todos completed only
when verified; ask Klas before deviating from the planned step.
**After each STEG (not only session end):** sync `docs/current-work.md`,
`docs/steg-tracker.md`, and a session log — as separate logical commits **in
the same PR as the scope** (ADR 0065; never a docs-only PR) — and **proactively
anchor where we are in the roadmap and what the next step is per the tracker**
(don't wait for Klas to ask).
**Session end only:** generate the next-session start prompt per
`docs/runbooks/session-start-template.md` (4 sections, copy-paste block in
chat, never a repo file).
Details and formats: `docs/runbooks/session-protocol.md`.

## 6.5 Parallel sessions (autonomous multi-session flow)

Several Claude Code sessions (2–4, Max x20) run concurrently in isolated git
worktrees. The rules below keep parallel work collision-free; full playbook in
[`docs/runbooks/parallel-sessions.md`](docs/runbooks/parallel-sessions.md).

- **Worktree-per-task (NO exception — the stack-lane too).** Every session
  works in its own `c:/tmp` worktree off `origin/main`; **NEVER the shared main
  working copy.** Two sessions in one copy share one HEAD/index → either's
  `git checkout` silently reverts the other's working tree. **Session-start
  pre-flight, before any work:** `git worktree list` (see active sessions +
  their branches) → confirm the issue is not already claimed (`gh issue view
  <N>` + open PRs) → create + enter your worktree (**Path A, recommended:** the
  `EnterWorktree` tool; **or Path B:** raw worktree + docs-sync — commands in
  the playbook §3) → `cd` in →
  claim the issue visibly if it has one (`gh issue edit <N> --add-assignee
  @me`). **ABORT if launched in the main copy on a non-main branch** — another
  session owns it; never
  `git checkout` there. Own worktree = own index: rebase on `origin/main` before
  push, verify with `git show --stat HEAD`. The session-start hook surfaces the
  worktree list + a main-copy warning automatically.
- **Hotspot ownership.** Files many contexts touch (DI composition roots,
  `AppDbContext`, shared builders, `messages/{sv,en}.json`, `.sln` /
  `Directory.Packages.props`) are owned by ONE session at a time — coordinate
  via issue assignment; never edit a hotspot you do not own. Hotspot list in
  the playbook.
- **EF migrations = the most dangerous hotspot (single-owner).** Only ONE
  session creates or applies migrations at a time; migration order is serial.
  Other sessions wait for a merged migration before touching the schema.
- **Shared-Postgres rule.** Only ONE "stack-owner" session runs the local dev
  Postgres (port 5435) + Api/Worker (single-owner: the shared dev DB + port
  5435; the running stack bin-locks only its OWN worktree's `bin/`) — **from its
  own worktree** (Model 1), passing secrets via the env override in
  `docs/runbooks/local-dev-setup.md` §Fällor (NOT by copying
  `appsettings.Local.json`). Every other session runs code + unit +
  architecture + **Testcontainers** (ephemeral DB, parallel-safe) — never
  against the shared dev DB.
- **Local docs in worktrees.** Gitignored session state (`current-work.md`,
  `steg-tracker.md`, `sessions/`, local `reviews/`, **ADR 0005** and ADRs
  **0071+**) is absent from a fresh worktree. `.worktreeinclude` lists them; run
  `scripts/sync-worktree-docs.ps1 <worktree-path>` after creating a worktree.
  Secrets (`appsettings.Local.json`, `.env.local`) are NEVER synced into a
  worktree — the stack-owner injects them at runtime via env override
  (`ConnectionStrings__Postgres` from `.env`) so its worktree runs the real
  stack without committing or copying secrets.
  ⚠ **That sync is a MANUAL copy from the main copy**, so a pointer into those
  docs is dead for anyone who did not run it — a skipped step, a fresh clone,
  GitHub's web view, a sub-agent. §9.6 (3)'s operative bound stays in the spec
  on its own §13 ground, and its derivation history is tracked in
  `docs/spec-rationale.md` §9.6. ADR 0072 Decision 2 owns the other side and
  this does not reopen it.
- **Backlog = GitHub Issues** (`area:`/`hotspot:`/**`mvp`**/`P0`–`P3`/lane `BE`·`FE`·
  `BE+FE`/`wip`·`blocked` labels); `steg-tracker.md` is the strategic map.

  **`mvp` is the label you pick work from, and it is a second axis, not a fourth
  priority.** Klas-direktiv 2026-08-02: a couple of real test users on
  `jobbliggaren.se` **within a month of that date**. An issue earns `mvp` when **a real
  test user meets it, or it blocks going live** — that is the criterion, and **the second
  clause is doing real work**. Ties resolve toward labelling: a mis-labelled issue costs one backlog row,
  a mis-skipped user-facing defect ships.

  On the product side, *"a real test user meets it"* resolves to the **core features**
  Klas named: `/jobb` · `/ansokningar` · `/foretag` · the **smart watches** (industry +
  municipality) on the company page · `/cv/granska`. *(The CV **builder** is paused, so
  builder FEATURES are not MVP — but a builder-adjacent defect a user still meets is.)*

  **`P0`–`P3` grades severity and urgency; `mvp` says whether the item is in scope for
  reaching real users.** They are different questions and they cross; an ordinal
  scale cannot carry two orthogonal axes.

  **Read it as: `mvp` = in scope now; no `mvp` = skippable.** In scope is not the same
  as unblocked — `mvp` may coexist with `blocked`, and §9's hotspot/migration rules
  still gate pickup.

  **Claim-on-pickup:** the moment you start an issue, assign yourself + add
  `wip` so no other CC duplicates it (lighter coordination model, playbook §9 —
  soft lane affinity + claim signal, not hard per-CC ownership). A PR-babysitter
  runs via cloud `/schedule` on PR events (rebase + `automerge`); it **must never
  set `agents-done`** — that label is the owning session's review gate. It needs no
  rule about *when* to `update-branch`, and deliberately has none: a pure base merge
  does not disarm the gate (§6). It does **not** close issues — the owning session does
  (next bullet; playbook §8.1, unconditional since this change and gated on no
  babysitter running before it). Playbook §9 carries who said what, when. Side-track
  PRs you own are shepherded to green before new scope.
- **A pushed PR is not a merged PR, and a merged PR is not a closed issue.**
  Automerge does **not** rebase.
  Squash drops `Closes #N` → the issue keeps its `wip` claim. **Watch your own
  PRs to MERGE, then close out** (`gh issue close`, drop `wip`, unassign).
  Mechanics and all four `mergeStateStatus` states: playbook §8.1 — read it, it
  also carries the `gh pr update-branch` form (a local rebase + force-push is
  deny-listed and 422s). **Since #836 the symptom has a SECOND cause:** a PR with
  `automerge` but no `agents-done` is armed-but-gated **by design** and is not
  stuck — it is waiting for the mandatory agents, and the fix is to wait them in,
  not to rebase. Check which cause before acting; `update-branch` on the wrong
  diagnosis no longer costs a review round, but it does not unstick a PR that was
  never BEHIND. **And a THIRD cause since
  #836: the `arm` job itself failed** (head moved, `UNKNOWN` exhausted, or a real
  API error) — the PR carries both labels and was never armed. `label-automerge`
  is not a required check, so nothing surfaces it; read the job log before
  assuming either of the other two. A FOURTH: the `blocked` label = a §9.6
  STOPP to Klas, not a stuck PR.
  **The dead-REMOTE-branch half is mechanised since #725** —
  `.github/workflows/delete-merged-branches.yml` deletes remote branches whose PR
  has merged, daily or on `workflow_dispatch`. Do not re-file it, and do not read
  a surviving branch as a *new* defect before checking the sweep's last run.
  **Your LOCAL branches are still yours.**
- **Never reap a worktree you did not create — and never one whose PR has not
  merged.** The general case belongs to the SessionStart reaper: a PR usually
  merges *after* its session has ended, so "clean up when it merges" is not a
  same-session action (ADR 0094). But that reaper only ever touches trees
  carrying a close-stamped marker, and a tree made with a raw `git worktree add`
  has none. So it will not collect yours. The tree **you** made this session, whose PR **you** watched
  merge, you may remove yourself (rescue its gitignored docs first). Anyone
  else's: never, for any reason.
  **Liveness is the boolean the OWNER sets** (`.jbl-worktree.json` → `closed_at`),
  never an inference *you* make about someone else (ADR 0094): doubt resolves
  to skip, never to "probably fine".
  "I created it" is knowledge; "its lock looks stale" is a guess that yanks a
  live tree.
  And **land your `current-work.md` / `steg-tracker.md` edits in
  the main copy before you stop** — the rescue saves gitignored files the main
  copy does *not* have; it cannot save your edits to ones it already does.

*Derivations, incidents and dated measurements: `docs/spec-rationale.md` §6.5.*

## 9. Working with the driving session (CC or Codex)

**9.1 On any task:** read the relevant BUILD.md section → check existing
patterns (reuse, don't invent) → identify the layer → test-first for new
domain logic → implement minimally → `dotnet test` + lint → conventional
commit → push branch, `gh pr create`, set the `automerge` label → **run the
mandatory agents, wait in ALL of them, resolve every Blocker/Major** — batched, and
closed by scoped re-checks (§9.6; the procedure is the `jobbpilot-review-discipline`
skill) — **and only then set `agents-done`** (§6). The PR body is written twice, never
more: at creation (what changes and why), and ONE edit after the last verdict appending
the verdict table, every escalation verbatim and §9.6's named skips (§9.2).

**9.2 Boundaries.** The driving session (CC or Codex) writes code, tests,
migrations, CI config, docs; proposes refactorings; creates ADRs for its
architecture decisions. **The driving session MAY edit
`BUILD.md`/`CLAUDE.md`/`AGENTS.md`/`DESIGN.md` autonomously** via the normal feature-branch
→ PR → automerge flow (autonomous multi-session flow, 2026-06-25 — the prior
spec-edit pre-approval gate is lifted); Klas reviews the diff post-merge.
Mandatory spec-edit agents still apply (dotnet-architect + code-reviewer; plus
design-reviewer for `DESIGN.md` design-token changes). The driving session does
**not**: deploy
without Klas GO; add top-level dependencies without justification or libraries
outside BUILD.md §3.1 without discussion; violate §5 (a §5 anti-pattern is
never autonomous); start a new session phase without explicit Klas GO.

**Mandatory agent invocation** (before the STOPP report; skipping counts as a
discipline miss; reports go to `docs/reviews/<date>-<phase>-<agent>.md` — header +
findings + escalations verbatim, capped per the reviewing agent's charter's Output format; the
cap binds the session's transcription too. The PR body carries the verdict table,
escalations and §9.6's named skips, never a report; a report Klas must read on GitHub is
promoted with `git add -f`, the `.gitignore` exception):

| Agent | When |
|---|---|
| `senior-cto-advisor` | Multi-approach choices, finding triage (in-block vs follow-up PR vs issue). Routes a finding; never re-grades one — severity belongs to the agent that reported it (§9.6). Decision-maker — the driving session gives no own recommendation. Unambiguous CTO verdicts execute without extra Klas GO. |
| `security-auditor` | PII, auth, secrets, external integrations; **accepting a vulnerability rather than repairing it** — growing `pnpm.auditConfig.ignoreGhsas`, lowering `--audit-level`, or suppressing `NuGetAudit`/NU1901-NU1904 (ADR 0065 Amendment 2026-07-28 Beslut 4). Reducing exposure is not a trigger. Also every exposure-*increasing* change to the suppression surface itself: an `overrides` entry removed or its target lowered, a new override key **in open form**, a gated key becoming open, a removal from `ignoredBuiltDependencies`, and `pnpm/action-setup` raised **past 9** — that last is a migration, not a bump, since pnpm 11 reads none of this configuration, so every repair and the single acceptance go dead while the gate still reports clean. Full enumeration in her Triggers section, keyed to audit area 8. She is that area's **named consumer** of `.github/scripts/audit-suppression-guard.sh`: the blocking gate audits with the ignore list *applied* and so cannot see an accepted advisory that has begun reaching production. |
| `code-reviewer` + `dotnet-architect` | Larger changes (>5 files or architectural choices) |
| `dotnet-architect` (mandatory) | All Terraform/IaC scope (ADR 0036 precedent) |
| `db-migration-writer` | New migrations |
| `test-writer` | New domain types or handlers |

**The panel is runtime-agnostic by design:** Codex is intended to spawn the
same charters through `.codex/agents/` pointer stubs (set parity CI-guarded;
text home stays `.claude/agents/`; ADR 0135 Amendment 2) — §6 (AGENTS.md) owns
who attests. Extension-side discovery is unmeasured as of 2026-08-22 (delivery
condition V1); until it is read, this is a design, not a measurement.

**None of them can ask Klas anything.** `AskUserQuestion` is stripped from every
subagent — foreground and background alike, and **even when listed in a `tools:`
field** (code.claude.com/docs/en/sub-agents, read 2026-08-03). The one exception
is a **fork**, which "skips both filters and receives the main conversation's exact
tool pool" (same page, same reading); every agent in the table above is a custom
subagent, not a fork. So where a charter
says "escalate to Klas directly" — security-auditor's GDPR Blockers and her
area-8 Majors, code-reviewer's CLAUDE.md conflicts — what the agent can do is
**record** the escalation: in its report, and where §9.6 prescribes it (those
area-8 Majors), in a labelled issue, since `Bash` survives both filters and `gh`
with it. What no subagent can do is **prompt** Klas. Carrying it further is the
invoking session's duty, and an escalation the session paraphrases away has been
dropped, not delivered. Background is additionally the **default** for subagents
(v2.1.198+), and a background subagent keeps only a fixed built-in set (the
list, with its read-date: `docs/spec-rationale.md` §9.2) — with
everything else removed whether inherited or listed, so **the same definition
resolves to different tools in the foreground and the background**. `Agent` and
`ExitPlanMode` are the exceptions: they follow the first filter wherever the
subagent runs, and `Agent` drops only at the depth limit — so the charters'
Delegation sections are live, not dead text. That removal
"reports no error" unless it empties the list entirely: a charter section whose
tool never arrived comes back thin rather than failed, which is the shape to
suspect before believing a short report.
*Derivations and the dated tool list: `docs/spec-rationale.md` §9.2.*

**9.3 When unsure:** read first (repo, BUILD.md, existing patterns) → ask
concrete questions → never guess whether a feature should exist.

**9.4 Discovery and verification.** Unsure about file state or existing
patterns → discovery report ("read/map X, report Y, no changes") with raw
full-file output, no truncation. After `str_replace`/paste: prove file state
with grep/diff output. Long pastes (>20 lines): pre-flight the target + new
content, wait for GO. Verbatim text (ADR sections, doc content) is produced by
web-Claude; the CC session applies (a CC-specific pipeline). Missing source
text after compaction → STOPP and ask.

**9.5 Web search for external facts.** Present-tense questions about
external systems (deploy providers, .NET/Next.js versions, AI models/pricing,
Claude features, NuGet/npm status) → search before answering, never guess
from training data. Official docs > registries > blogs; verify dates; cite
URL + date in the STOPP report.

**9.6 Where a finding goes** (formerly §9.6/§9.7, which several ADRs still cite together)**.** Default is still **fix in-block** — quality > tempo,
and senior-cto-advisor decides when it is genuinely ambiguous. What changed
2026-08-02 is the alternative: **there is no TD register to raise anything into.**

**The list below answers "where does what is not fixed go" — never "what should
happen to a finding."** The default disposal is the fix itself; a destination is
for the remainder, and choosing one is the exception that needs a reason.
**A session leaves the backlog no larger than it found it** — issues filed ≤
issues closed or fixed, per session — **except for genuine defects in delivered
code, which are always filed**, and except for a filing a charter itself
mandates (security-auditor's area-8 Major): a charter-declared outcome is never
the session's to withhold, so the cap never blocks it. An ordinary Minor that
would breach the cap is fixed instead or carried as a named skip in the PR body
— the Minor bullet's issue route yields to the cap. The cap binds filing, never
closing: an issue closes only against Filing discipline's measurement duty,
never to buy filing room. **§12 gains no new class here** — an overrun cap is a
discipline miss, not a STOPP.

**Severity belongs to the agent that reported the finding, and §9.6 does not define it.**
Each mandatory agent grades in its own charter's scale — `code-reviewer` and
`design-reviewer` and `security-auditor` each define Blocker/Major/Minor for their own
domain, and `dotnet-architect` reports Kritiskt/Viktigt/Nice-to-have. **Do not re-grade
a finding against another agent's table**: a design-reviewer Blocker is a Blocker
because design-reviewer said so, not because it also fits code-reviewer's definition.
Three of the four already report in Blocker/Major/Minor verbatim; `dotnet-architect`
alone reports Kritiskt/Viktigt/Nice-to-have, which maps to Blocker/Major/Minor in that
order. (Praise is not a finding and routes nowhere.) Then:

- **Blocker or Major** → **in-block**, or a **follow-up PR** if it is a genuinely
  separate change-reason. Never an issue: §6 and §12 make an unresolved agent
  Blocker/Major merge-blocking, so filing one would convert a stop into a backlog row.
  **Three exceptions in this paragraph, and none of them is the session's to claim** — (1) and (2)
  are the reporting charter's to declare, and (3) is granted by Klas **and signed by
  `security-auditor`; neither of them alone grants it.**
  (1) A **Major** its own charter marks non-blocking because it
  grades **repo state the diff did not create** — `security-auditor` area 8, whose Major
  row escalates to Klas and lets the PR through — has neither an in-block home nor a
  change-reason of its own, so it is **filed as an issue with the escalation named in
  it**. That is the **Major row only**: the same charter does not unambiguously say it
  of an area-8 **Blocker**, whose classes are auth bypass, PII/secret exposure and RCE.
  **Where a charter is ambiguous about its own outcome, §9.6 does not pick a side** —
  escalate to Klas and let that charter's owner resolve it, because **exceptions (1) and (2) are the
  reporting charter's to declare**. (2) A security Major **without GDPR implication** may become a documented
  **accepted-risk ADR** — `security-auditor`'s own edge case, and **Klas owns that
  decision**.

  (3) **A GDPR-implicated security Major may become an accepted-risk ADR when the BOUND holds**
  (Klas-direktiv 2026-08-16). **The route is keyed on who bears the risk, never on the absence of a
  GDPR implication** — which is why (2) could not reach these cases. The deriving ADRs
  0132/0133 are gitignored, so this paragraph is written to stand alone.

  **The bound is ONE condition, measured, in three parts — all three required.** The only data
  subject whose Art. 5, Art. 12–22 or Chapter V position is affected is the **controller himself**,
  or there is none at all: **(i) no registered bearer** — every account is one the controller himself
  holds, measured against the account table; **(ii) no reached bearer** — every send has reached him,
  measured against the send log; **(iii) no reader** — the copy carrying the affected statement is
  not publicly readable, measured against the live surfaces. **(iii) is not an instance of (i)**, and
  writing it out is the whole point: publishing a false transparency statement about a live
  processing breaches Art. 5(1)(a)/12(1) with **no registered data subject at all**, so (i) can
  hold — vacuously — while (iii) fails.

  ⚠ **The three parts measure the criterion; they do not replace it — they are necessary, not
  sufficient.** They are the registers where a bearer has arisen so far — account, send, public
  reading — and **none of them measures CONTENT**. A bearer none of the three reaches fails the
  condition all the same: a referee named inside a CV the controller himself uploaded, or an enskild
  firma's `organization_number` in `job_ads`, which for a sole trader **is the holder's personnummer**
  (#841) — neither holds an account, received a send, or sits behind a public page.

  ⚠ **Read *affected* as strictly as the criterion writes it:** the bearer must be one **this finding
  reaches**. A content bearer it does not reach is not a bearer of this acceptance — otherwise the
  route is inert against any system that holds a third party's data at all, and neither delivered
  instance could have been signed. **Read the criterion first and the parts as its
  instruments.**

  **The measurement names its artefact, its date and its home.** It is recorded in the same ADR or
  CLAUDE.md update as the acceptance — never a PR body, never chat. **It expires**: a bearer-absence
  reading is never inherited from an earlier row, and is re-taken at the decision and at every lapse
  check.

  **Art. 24(1) is the ground, not a second condition.** Measures scale to *"the nature, scope,
  context and purposes of processing as well as the risks … for the rights and freedoms of natural
  persons"* — which is bearer-absence in the law's own vocabulary. It therefore **cannot
  independently gate anything and can never rescue a bound that failed**: Art. 24(1) scales
  *measures*, it does not scale away an obligation owed to a data subject (Art. 12(2) is absolute).
  **Cited alone it grants nothing**, and it is the arguable half beside three measurable ones —
  which is the direction a widening comes from.

  **What the route requires, and it is not less than what was already done twice:** Klas grants it ·
  **`security-auditor` signs it — an acceptance without her signature is not one** · it is recorded
  in an **ADR or a CLAUDE.md update, never a PR body** · it carries a **written lapse trigger with a
  single named home and a named human reader**, since nothing detects a lapse automatically · and it
  **withdraws the remedy only — never the finding, never its grade, never the record of what is
  unknown**. An accepted risk does not become measured by being accepted and must never be written as
  though it had. **The route lets a session propose an acceptance, never declare one.** The finding
  is then **resolved by acceptance** — which is **not** what §12 means by *unresolved*; its
  *0 Blocker / 0 Major* wording describes the ordinary case, not a signed acceptance — so the PR
  rides the normal flow. **Every other applicable §12 class must still clear independently**, and a §5 `Security:`
  class clears through Klas, not through this route.

  ⚠ **This is not a lowered bar, and reading it as one is the failure mode to avoid.**
  **Neither ground survives a widening.**
  Bearer-absence ends the moment there is a bearer — any of the three parts, or a bearer none of them
  reaches — and Art. 24(1) scales the measures **up** as the processing grows; together they license
  one acceptance, at one size, until the size changes. **An acceptance widened by a single
  non-controller data subject is an acceptance of a third party's rights, which this route does not
  grant and `security-auditor` does not sign.**

  **The lapse fires on ANY trigger the acceptance names, and one sentence is not a trigger set.**
  *"The first personal data reaching the processor that is not the controller's"* is the **ground**,
  not the operative form. The operative set is the acceptance's own, enumerated in one
  home and counted nowhere else. The delivered pair carries four — and one of them, **the copy
  becoming publicly readable, fires with no personal data reaching any processor at all.**

  **A GDPR Blocker is never in any of these three categories**, and neither is a Major whose bound
  cannot be measured. **(2) and (3) are granted by Klas — (3) also signed by `security-auditor` —
  and recorded in an ADR or a CLAUDE.md update, never by the session in a PR body. (1) is not a Klas
  grant at all** — per its own text above it is
  filed as an issue with the escalation named in it, and the PR proceeds.
- **The finding does not hold** — its premise is false or revoked → say so plainly, with
  the measurement. Neither a fix nor an issue. This is a real outcome, not a way out.
- **Minor / nice-to-have** → a **GitHub issue**, and a line in a PR
  body is not disposal because it has no reader. The reason is **visibility between
  parallel lanes**, not issue inflation, so an issue no other lane would need to see may be
  skipped — but the skip is **named in the PR body**, one line, with what makes it
  invisible to a peer lane. An unnamed skip is not an exception; it is an omission.
  **Label it as you file it** — `area:`, a `P0`–`P3`, a lane, and **`mvp` if a real
  test user meets it or it blocks going live** (§6.5). An unlabelled
  issue is filed into the same invisibility the TD register was retired for.
- When it is genuinely ambiguous, **senior-cto-advisor decides**.
- **Never** re-create `docs/tech-debt.md`, a `TD-NNN` identifier, or a
  Severity × Fas matrix. If the register looks like it is missing something, it is not
  — read [#1172](https://github.com/klasolsson81/jobbliggaren/issues/1172).

**Filing discipline.** An issue asserting that live code does something **measures it
first**, and records **what adjudicated it and on what date** — a claim with no date
cannot be told from a claim that has decayed. A parked or
deferred item makes **no** truth claim and needs no measurement — but it must then be
written as scheduling ("not MVP scope, not verified"), never as fact ("still applies",
"no longer relevant").

**Closing a Blocker/Major — the scoped re-check.** **A fix that closes a finding deletes
text or changes code — it never adds a claim-sentence.** A finding only closable by added
prose is closed by deleting the sentence that carried the claim. A fix landing after an
agent's verdict goes back to **the agent that issued it** — only the issuer can say its
own finding is closed — except a finding that closes **mechanically**: the flagged
sentence is deleted, the old string greps to zero and the closing diff for that file
adds zero lines (no `+` lines beyond the header), recorded as one row in the verdict
table, no re-check owed. A fresh reviewer re-reviews the whole PR. The re-check is
**report-only** and scoped to the **fix delta**; it grades that delta only — **no phrasing
findings, and no new findings on lines the delta did not touch, except a finding the
re-checking charter itself grades as a Blocker or defines repo-wide rather than
per-diff**, which is always reported. That carve-out is not optional: an unconditional gag
would silence a GDPR or a11y veto the charter holds, and §9.6 does not overrule a
charter's own exceptions. **The cycle is capped: one batched review round, then at most
ONE scoped re-check per issuing agent.** The cap counts finding-closing re-checks;
re-verifying a moved head with no open findings is not a round. A new-in-delta
Blocker/Major raised by that re-check is fixed by deletion (closed mechanically — a
deletion cannot introduce a claim) or by a code-only change closed by re-running the
finding's own measurement, each recorded in the verdict table.
A finding that survives the cap and genuinely needs new prose → **STOPP to Klas**, and
the session sets the `blocked` label: §6 and
§12 keep it merge-blocking, so the choice is delete, fix in code, or stop — never explain.
Nothing is fixed in-block *during* a re-check: each in-block fix invalidates the check
just run. Verify HEAD is unchanged immediately before setting `agents-done`. **Charters
and the skill carry pointers here, never restatements** (#1173). Batching, the delta
command, the report-only prompt, the verdict-table format and the label checklist are the
`jobbpilot-review-discipline` skill's, and **§12 gains no new class here.**

*Derivations, incidents and dated measurements: `docs/spec-rationale.md` §9.6.*

## 11. Tooling

- Pre-commit (Husky + a hand-rolled `git diff --cached` filter, not
  lint-staged): staged `*.cs`/`*.csproj`/`*.props`/`*.targets`/`*.sln`/
  `global.json` → `dotnet format --verify-no-changes` + Domain/Application/
  Architecture unit tests; staged `web/jobbliggaren-web/` files → `pnpm lint`
  (ESLint, no `--fix`) + `pnpm tsc --noEmit`. No Prettier; `json`/`md`/`yaml`
  not auto-formatted.
- `.editorconfig` + committed `.vscode/` settings/extensions.
- Dev env: Docker Compose (`postgres`, `redis`, `seq`) — MEL logs to console
  **and to Seq**: `AddJobbliggarenLogging` (shared by Api + Worker) attaches the
  Seq provider **only when `Seq:ServerUrl` is set**; the Hetzner residual is the
  *production* Seq, not the wiring. Everything runs locally: `LocalDataKeyProvider`
  (AES-256-GCM) for field encryption, and mail via `AddEmailSender`'s
  `Email:Provider` switch — **three** `IEmailSender` impls, not one:
  `ConsoleEmailSender` (Development/Test **only**; it logs the recipient address
  and the whole body, confirmation and activation links included — **but only for a
  recipient at a domain RFC 2606/6761 reserve**, i.e. one that cannot be a real
  mailbox. Every other recipient gets a kind-only `Warning` and no body at all, so a
  real address cannot put its activation link into dev's Seq (#1208). The rule lives at
  `WriteEmail`, the one choke point every send method funnels through, as a set fixed in code
  and never an `IOptions` value — anything settable at runtime can be widened to the domains
  it excludes.
  `ConsoleEmailSenderReservedRecipientTests` pins both arms, the `IEmailSender` arity,
  and the set's exact membership. The sink is loopback-bound **and
  admin-authenticated** on top of that (#1198). **The guard reaches the email-body path
  and nothing else** — every other line written to the same sink is outside it, and
  that residual is unmeasured),
  `NullEmailSender` (what `Provider=Console` falls back to outside Dev/Test),
  and `ScalewayEmailSender` (`Provider=Scaleway` — Scaleway Transactional Email in
  `fr-par` over the **HTTPS API, never SMTP**; fail-loud without
  `Email:Scaleway:Region` **and** both `Email:Scaleway:SecretKey`/`ProjectId`, #183).
  **The count is still three because each provider REPLACED the last.** `Resend` and
  `Ses` both now throw like any other unknown value, and `AddEmailSenderGateTests`
  pins that. **There is no .NET SDK and no package** — the arm is `HttpClient` +
  `System.Text.Json`, so `NoAmazonReferenceTests` went back to a total ban.
  What actually prevented
  double delivery was never the provider key but the claim-then-send spine (plus
  `StrandedMatchReaperJob`) and `ICooldownGate` (ADR 0103); the residual
  transport retry is closed by the arm registering **no resilience handler at
  all**. Frontend `.env.local`; backend
  `appsettings.Development.json` + gitignored `appsettings.Local.json`.
- `ReverseProxyOptions`/`ReverseProxy:HttpsEnabled` (renamed from `AlbOptions`/`Alb:`
  2026-08-04) is **live** — it co-gates `UseHsts` and `UseHttpsRedirection` with the
  environment in `Api/Program.cs`, and gates the fail-loud HSTS config validation.
  `UseHttpsRedirectionGateTests` pins both middleware gates in both polarities and
  `ReverseProxyOptionsTests` pins the section key; the validation gate itself is still
  unpinned. **The retired `Alb` key has no transitional fallback** — measured empty
  consumer set. That absence is a **documented intent, not an executable guarantee**:
  the pin covers the constant, not the composition root, so a fallback bind added in
  `Program.cs` would still pass. A fourth `WebApplicationFactory` would pin it properly
  and was declined deliberately
  ([#1190](https://github.com/klasolsson81/jobbliggaren/issues/1190)).
  **`HttpsEnabled` must stay `false` under Option B, and that is a decision, not an
  unfinished flip:** Next reaches the API over plain internal HTTP, so `true` would 307
  every internal call and break the app, while `UseHsts` stays inert either way because
  the API's responses are consumed by a Next route handler and never reach a browser.
  Browser-visible HSTS is owed **outside ASP.NET**, on **both** response paths — the
  Caddyfile in #196 for the 401 that never reaches Next, and `buildSecurityHeaders` for
  the Next path (ADR 0050 Amendment 2026-08-04 §5, gate M-5a). Never by flipping this flag.
  ADR 0066 destroyed the *deployed* AWS dev stack and deliberately
  **preserved** `infra/terraform/`, which still carries the **old `Alb__HttpsEnabled`**
  injection — deliberately, as a **record of what ran**, not as live config. Do not
  "repair" it toward the current key. Retirement — and restoration — is a cutover ADR, never a cleanup sweep
  (BUILD.md §15); [#196](https://github.com/klasolsson81/jobbliggaren/issues/196)
  owns the deploy stack and the `ForwardedHeaders:KnownNetworks` CIDR, which A1
  deliberately left empty — no compose file in the repo declares a network, so the
  fail-loud gate stays armed rather than holding a guess.
- **Dev-boot config contract.** A new fail-fast option (a `ValidateOnStart` secret,
  usually in the Infrastructure DI both hosts share) that a fresh dev-stack boot needs —
  a required key, secret, or pepper the API/Worker refuses to start without — MUST be added to
  `src/Jobbliggaren.Api/appsettings.Local.json.example` (the required-keys SSOT) **and**
  `docs/runbooks/local-dev-setup.md` (§2.4 + §7) in the **same PR** as the option, by the
  introducing CC. A gitignored `appsettings.Local.json` predates the new key, so an
  out-of-sync template fails the next stack-owner's boot one crash at a time.

*Derivations, incidents and dated measurements: `docs/spec-rationale.md` §11.*

## 13. Update process

This file — and `AGENTS.md`, the shared core — changes when a new anti-pattern,
standard, or CC boundary is needed, under the same process and the same
mandatory agents; a § moving between the two files updates the §-index in the
same PR. CC may propose **and apply** changes autonomously via PR + automerge
(§9.2's mandatory spec-edit agents); Klas may also propose.
Never silently — always via a visible PR diff, which Klas reviews (post-merge
under automerge). Rules land here; derivations, incident history and dated
measurements land in `docs/spec-rationale.md` (§-keyed, same PR). A spec edit
that adds a derivation paragraph to this file is the regrowth that split
exists to prevent.

---

**End of CLAUDE.md.** Shared core in [`AGENTS.md`](./AGENTS.md), main spec in [`BUILD.md`](./BUILD.md), design in
[`DESIGN.md`](./DESIGN.md).
