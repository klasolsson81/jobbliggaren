# CLAUDE.md — Jobbliggaren coding conventions

> Instruction file for Claude Code — read on every invocation before writing
> code. Main spec: [`BUILD.md`](./BUILD.md) · Design: [`DESIGN.md`](./DESIGN.md)

## 1. Identity

Jobbliggaren is a Swedish job-application manager built as a **civic utility** —
think 1177 or Digg in tone, never Linear or Vercel. When unsure, choose what
feels *serious and trustworthy* over fun or trendy.

**Product owner:** Klas Olsson, .NET/fullstack student (NBI/Handelsakademin).
High quality bar, direct Swedish, no AI clichés. Write every commit as if it
must survive a Mastercard-level code review.

**Language policy (2026-06-12):** code identifiers in English; UI copy in
Swedish (`messages/sv.json`); new docs, ADRs, session logs, reviews, commit
messages, and comments in **English**; chat replies to Klas in **Swedish**.
Existing Swedish docs are not mass-translated.

## 1.5 Session protocol (mandatory)

**Start (mandatory roadmap-grounding — be tracker-driven, not prompt-driven):**
read `docs/current-work.md` **in full** + the `docs/steg-tracker.md` framåtplan
section + latest `docs/sessions/` log (the session-start hook's preview is not a
substitute for reading the files); verify HEAD via `git log --oneline -8`;
confirm the session-start hook ran. **Then confirm the session's task is the
right next step per the tracker before starting work — if the prompt diverges
from the tracker, flag it to Klas** rather than silently following either.
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

## 1.6 Docs map

| Location | Purpose |
|---|---|
| `docs/current-work.md` (+`-archive.md`) | Session-state source of truth (+ archived blocks) |
| `docs/sessions/` | Per-session logs |
| `docs/decisions/` (+`README.md` index) | ADRs — create via `/new-adr` (adr-keeper); next number from the index |
| `docs/runbooks/` | Operational procedures |
| `docs/research/` (+`issues/`) | Findings, planning, open questions |
| `docs/reviews/` | Agent review reports |
| `docs/tech-debt.md` (+`-archive.md`) | Active TDs (Severity × Fas) / closed TDs — mechanics in the `jobbpilot-td-lifecycle` skill |

Top-level `BUILD.md`/`CLAUDE.md`/`DESIGN.md` may be edited autonomously via the
normal feature-branch → PR → automerge flow (§9.2/§6); Klas reviews the diff
post-merge. Mandatory spec-edit agents apply (dotnet-architect + code-reviewer;
design-reviewer for DESIGN.md design-token changes). Agents place new docs per
this map; when unsure, ask.

## 2. Core principles

**2.1 Clean Architecture is non-negotiable.** Domain depends on nothing —
not Mediator, not EF Core. Application depends on Domain and defines every
interface Infrastructure implements. Infrastructure implements them (EF Core,
external clients). Api/Worker compose DI only. If you are importing
`Microsoft.EntityFrameworkCore` in Domain or Application — stop.

**2.2 DDD.** Aggregates protect invariants in constructors/methods, not
handlers. No public setters (private set + EF mappings where forced). Changes
raise domain events. Aggregates reference each other via strongly-typed IDs
only. State transitions go through explicit methods with preconditions.

**2.3 CQRS via Mediator.SourceGenerator.** Commands return `Result<T>`;
queries return DTOs (never domain objects past the Application boundary).
Pipeline order: Logging → Validation → Authorization → UnitOfWork. One handler
does one thing — compose complex flows from several commands.

**2.4 Testable first.** Aggregates testable without a database; handlers with
fake DbContext + NSubstitute. If it needs ASP.NET to test, the design is wrong.

**2.5 Performance has a written verdict.** Static query hygiene (§3.6) is the
floor; ADR 0045 budgets (hot-path latency, Core Web Vitals, Worker memory)
are the runtime verdict. Regressing against budget requires a STOPP
justification or a fix — same discipline as lowered coverage. Fitness
functions stay observe-only until an explicit Klas ratchet.
`LoggingBehavior` already measures latency — unexplained regression with the
signal available is a discipline miss.

## 3. C# / .NET standards

- **Style:** C# 14 where it helps (primary constructors, collection
  expressions, `field`); nullable reference types on solution-wide;
  file-scoped namespaces; `global using` per project; `dotnet format`
  pre-commit + CI.
- **Naming:** aggregates = singular nouns (`Application`, not `Applications`);
  `<Verb><Noun>Command(/Query)Handler`; `SubmitApplicationCommand` order;
  `I`-prefixed interfaces; `_camelCase` private fields; `Async` suffix always;
  tests `<ClassUnderTest>_<Scenario>_<Expected>`.
- **Immutability:** value objects = `record struct`/`readonly record class`;
  DTOs = `record class`; entities = `class` with private setters; exposed
  collections = `IReadOnlyList<T>`/`IReadOnlyCollection<T>`, never `List<T>`.
- **Errors — two coexisting idioms:** expected failures → `Result<TSuccess,
  TError>` carrying a `DomainError`; unexpected → exceptions. (1) *Result
  idiom:* `DomainError.Kind` (`ErrorKind`) is the discriminator the central Api
  mapper `DomainError.ToProblemResult()` translates to a status — Validation→400,
  NotFound→404, Conflict→409, Gone→410 (exhaustive switch, `_`→500); one place,
  never per-endpoint `Code`-string matching (§5). Construct `DomainError` only via
  its factories (`NotFound`/`Validation`/`Conflict`/`Gone` — the kind is stamped
  there; a raw `new DomainError(...)` defaults to Validation/400 and is
  architecture-test-forbidden). (2) *Exception idiom:* `DomainException` → 400,
  `NotFoundException` → 404 via middleware. A genuinely authentication-only status
  (401) the kind-union does not model stays endpoint-local, not an `ErrorKind`.
  Never `throw new Exception(...)` — always a specific subclass.
- **Async:** `CancellationToken` propagated end-to-end. Never `.Result` or
  `.Wait()`. `Task.Run` only for CPU-bound work. No `ConfigureAwait(false)`
  needed inside ASP.NET Core.
- **3.6 Queries:** `IAppDbContext` directly in handlers — no repository layer.
  `ISpecification<T>` only when the same filter is used in 3+ places.
  `.AsNoTracking()` default for reads. `Include()` only when needed.
  Pagination via `.Skip().Take()` + separate count query.

## 4. TypeScript / Next.js standards

- `strict: true`, no exceptions; `any` is **forbidden** — `unknown` + guards.
  ESLint via Husky (no Prettier on web). Functional components + hooks only.
- Files: components `PascalCase.tsx` (one export); hooks `useCamelCase.ts`;
  types in `types.ts` per folder; tests co-located (`Button.test.tsx`).
- Data: Server Components by default; `"use client"` only where interactivity
  requires it; TanStack Query for client mutations/polling; React Hook Form +
  Zod for forms — never loose `useState` for large forms.
- Naming: routes = Swedish nouns (`/ansokningar`, `/jobb`); components =
  English PascalCase; UI copy Swedish, code English.

## 5. Anti-patterns (never)

**Backend:** repository pattern over EF Core · AutoMapper across the Domain
boundary (map explicitly) · `DateTime.Now/UtcNow` (inject `IDateTimeProvider`)
· magic strings (use constants/enums/SmartEnums) · generic `*Service` names
(name by what the class does) · primitive obsession (make value objects) ·
stateful static helpers · `dynamic` · catch-all try/catch without action ·
logging sensitive data in plaintext (CV content, parsed CV text, OAuth
tokens) · hardcoded config (use `IOptions<T>` + gitignored
`appsettings.Local.json` locally / managed secrets in ops) · sync I/O in the
request pipeline · unpaginated list fetches · `SELECT *` via EF (project to
DTOs).

**Frontend:** `any` · global state where server state suffices · `useEffect`
for data fetching · `console.log` in production · emoji in UI copy ·
exclamation marks (civic tone) · gradients/drop shadows > `shadow-sm`/glow/
glassmorphism — **sole exception:** the hero plate's dark-green gradient
(`--jp-hero-gradient`, scoped per ADR 0068) · radius > 6px except pills/badges
· `localStorage` for sensitive data · hardcoded UI strings (use `next-intl` +
`messages/sv.json`) · direct DOM manipulation.

**CV & matching engines (deterministic, no AI/LLM — ADR 0071):** any
LLM/AI inference call in the product (no `IAiProvider`, no Anthropic/BYOK/credit
system — ADR 0051 superseded) · hardcoded rubric thresholds, cliché lists, or
action-verb lists in C# (versioned data/config per the knowledge bank, not
inline strings) · a CV verdict without cited textual evidence (every
PASS/WARN/FAIL cites the CV span; reduced-precision criteria are marked "not
assessed v1", never mis-reported) · applying a CV change without an explicit
propose-and-approve diff (a rule engine never rewrites silently) · synthesising
prose the user did not write (determinism diagnoses and structures, never
invents qualifications) · personnummer echoed to logs or surfaced un-flagged
(the personnummer guard is highest-priority) · a match score as an opaque number
(matched/missing keywords are always surfaced — explainable by design) · SSYK
derivation without user confirmation (taxonomy lookup + confirm, ADR 0040).

**Security:** secrets in committed `appsettings.json` or plaintext env —
gitignored `appsettings.Local.json` locally, managed secrets store in ops;
PII via DEK envelope (`IDataKeyProvider`, ADR 0066/0049) · JWT in
localStorage · CORS `*` or broad credentials · raw SQL via concatenation
(parameterize) · impersonation without an audit event · `User.Identity.Name`
for authorization (use policies via `[Authorize(Policy = ...)]`).

## 6. Commits, branches, PR flow

- `main` is protected; **all changes via feature branch + PR** (ADR 0065,
  `enforce_admins: true` — Klas included). Branch: `<type>/<short-slug>`.
  Linear history (squash/rebase — no merge commits). Deploy via tags on main
  (`v*-dev` → dev, `v*-rc*` → staging, `v*` → prod, manual approval).
- **Conventional Commits:** `<type>(<scope>): <description>` — types feat/fix/
  docs/refactor/test/chore/perf/build/ci; scopes e.g. applications, resumes,
  ai, infra, web; imperative; English (language policy §1).
- **Review gates (ADR 0065):** plan design in chat → STOPP discipline at
  transitions → agent invocation (§9.2) with reports in the PR body → CI gate
  (`ci` aggregate green; observe-only jobs don't block) → pre-commit gates
  (`dotnet format`, web ESLint + `tsc`) + pre-push gitleaks secret scan.
- **Automerge (ADR 0065 Amendment 2026-06-07; autonomous flow 2026-06-25):** CC
  creates PRs and pushes without asking, and sets the `automerge` label on its
  own PRs (`gh pr edit <nr> --add-label automerge`); merge on green `ci`; Klas
  reviews the diff **post-merge**. Spec-edits to BUILD/CLAUDE/DESIGN no longer
  require pre-approval (§9.2) — they ride the same flow. Exception (STOPP
  instead): an unresolved agent Blocker/Major, **or any §12 merge-blocking
  condition** (a §5 anti-pattern, Clean-Architecture boundary violation,
  non-BUILD.md library, design-token change outside DESIGN.md, or
  security-critical change without tests). Docs-sync lives in the same PR as
  the scope (tracked docs); gitignored session-state docs are updated locally
  (§6.5).

## 6.4 You own the PR until it merges — and you own the cleanup after

A PR is not done when you push it. It is done when it **merges**. Between those
two moments it is **yours**, and nothing else in the system is watching it.

**Watch your own PRs until they land.** Not just "did it merge" — the two silent
failure modes:

- **`BEHIND` is the killer.** Automerge does **not** rebase. The moment a sibling
  PR merges, yours goes `BEHIND` and then sits there — automerge enabled, CI
  green, and **nothing will ever merge it**. Nobody is told. Fix:
  `gh pr update-branch <nr>`. *(2026-07-14: #899 went BEHIND within minutes of a
  sibling landing. A background watch caught it and updated. Without it, it would
  still be sitting there.)*
- **Red `ci`.** Rerun once for flakes (`gh run rerun --failed`); if it is real,
  fix it or STOPP. An abandoned red PR is worse than no PR.

Arm a background watch on your own PR (a poll loop on `mergeStateStatus` + the
`ci` conclusion that also runs `update-branch` on `BEHIND`) rather than
remembering to look. Do not babysit **other** sessions' PRs.

**Squash-merge DROPS the closing keyword — close the issue by hand.** `Closes #N`
in your commit body does not survive the squash, so **the issue stays open**. On
merge: `gh issue close <N>`, remove the `wip` label, and unassign yourself if you
are not continuing on it. *(2026-07-14 hygiene pass: #800 and #801 had shipped
two days earlier and were still open and `wip`; #824's own final PR said
"PR 4/4". Nine `wip` issues against four running CCs — a `wip` label is a claim,
and a claim nobody released is indistinguishable from work in progress.)*

**Reap your own worktree and branch once the PR is MERGED** — never before:

1. **RESCUE FIRST.** Gitignored session state (`docs/sessions/`, `docs/reviews/`,
   ADRs 0074+) exists **only inside that worktree**. `--force` destroys it, with
   no warning and no recovery. Copy it to the main copy before you remove
   anything. *(The 2026-07-14 pass rescued 14 such files — four CTO reports, six
   agent reviews, a session log — one command away from being gone forever.)*
2. `git worktree remove --force <path>`. On Windows this routinely fails with a
   file lock → `git worktree unlock <path>` → `rm -rf <path>` → `git worktree
   prune`.
3. Delete the branch, **both** ends: `git branch -D <b>` and
   `git push origin --delete <b>`.

**Only your own, and only what is provably done.** The precondition is
**PR MERGED ∧ the worktree's lock names a DEAD pid** (`.git/worktrees/<n>/locked`
carries the owning session's pid; check it with `tasklist`). A lock naming a LIVE
pid is a running session — touching it is the shared-worktree disaster §6.5
exists to prevent. *(The same 2026-07-14 pass cleared 44 stale local branches, 44
stale remote branches and 7 worktrees. It accumulated in nine days.)*

## 6.5 Parallel sessions (autonomous multi-session flow)

Several Claude Code sessions (2–4, Max x20) run concurrently in isolated git
worktrees. The rules below keep parallel work collision-free; full playbook in
[`docs/runbooks/parallel-sessions.md`](docs/runbooks/parallel-sessions.md).

- **Worktree-per-task (NO exception — the stack-lane too).** Every session
  works in its own `c:/tmp` worktree off `origin/main`; **NEVER the shared main
  working copy.** Two sessions in one copy share one HEAD/index → either's
  `git checkout` silently reverts the other's working tree (real incident
  2026-06-28: a parallel checkout yanked an active branch mid-session; the
  commit survived only because it was already pushed). **Session-start
  pre-flight, before any work:** `git worktree list` (see active sessions +
  their branches) → confirm the issue is not already claimed (`gh issue view
  <N>` + open PRs) → create + enter your worktree (**Path A, recommended:** the
  `EnterWorktree` tool → `.claude/worktrees/<name>`, zero-setup; **or Path B:**
  raw `git worktree add c:/tmp/jbl-<slug> origin/main -b <type>/<slug>` +
  `pwsh scripts/sync-worktree-docs.ps1 <path>` — see the playbook) → `cd` in →
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
  own worktree** (Model 1), passing secrets via a `ConnectionStrings__Postgres`
  env
  override built from `.env`'s `POSTGRES_PASSWORD_DEV` (NOT by copying
  `appsettings.Local.json`; the dev `appsettings` uses a `${...}` placeholder
  the launch must expand, else `28P01`). Every other session runs code + unit +
  architecture + **Testcontainers** (ephemeral DB, parallel-safe) — never
  against the shared dev DB.
- **Local docs in worktrees.** Gitignored session state (`current-work.md`,
  `steg-tracker.md`, `tech-debt.md`, `sessions/`, local `reviews/` and ADRs
  0074+) is absent from a fresh worktree. `.worktreeinclude` lists them; run
  `scripts/sync-worktree-docs.ps1 <worktree-path>` after creating a worktree.
  Secrets (`appsettings.Local.json`, `.env.local`) are NEVER synced into a
  worktree — the stack-owner injects them at runtime via env override
  (`ConnectionStrings__Postgres` from `.env`) so its worktree runs the real
  stack without committing or copying secrets.
- **Backlog = GitHub Issues** (`area:`/`hotspot:`/`P0`–`P3`/lane `BE`·`FE`·
  `BE+FE`/`wip`·`blocked`·`next-up` labels); `steg-tracker.md` is the strategic
  map. **Claim-on-pickup:** the moment you start an issue, assign yourself + add
  `wip` so no other CC duplicates it (lighter coordination model, playbook §9 —
  soft lane affinity + claim signal, not hard per-CC ownership). A PR-babysitter
  runs via cloud `/schedule` on PR events (review + automerge); it should also
  `gh issue close` referenced issues (the automerge squash can drop the
  `Closes #N` keyword → the issue stays open; see playbook §8.1/§9). Side-track
  PRs you own are shepherded to green before new scope.

## 7. Testing

Every new domain class: at least one invariant test. Every new handler: happy
path + validation failure. Every new endpoint: integration test. Lowered
Domain coverage: justified in the PR or rejected. Snapshot tests only for
stable components; E2E updated when critical flows change.

```bash
dotnet test                                  # backend
cd web/jobbliggaren-web && pnpm test            # frontend
cd web/jobbliggaren-web && pnpm playwright test # E2E
dotnet test --filter "Category=Architecture" # architecture
```

## 8. Definition of Done

1. Acceptance criteria (BUILD.md §2) met · 2. unit + integration tests,
coverage not lowered · 3. architecture tests green · 4. manually tested in dev
· 5. Lighthouse > 90 on affected pages · 6. keyboard + screen-reader
accessible · 7. domain events documented · 8. GDPR impact assessed (new PII?
logging? retention?) · 9. ADR written for architecture decisions · 10. code
review done.

## 9. Working with Claude Code

**9.1 On any task:** read the relevant BUILD.md section → check existing
patterns (reuse, don't invent) → identify the layer → test-first for new
domain logic → implement minimally → `dotnet test` + lint → conventional
commit → push branch, `gh pr create` with agent reports inline, set the
`automerge` label (§6).

**9.2 Boundaries.** CC writes code, tests, migrations, CI config, docs;
proposes refactorings; reads prompts from `/prompts/` (does not rewrite them);
creates ADRs for its architecture decisions. **CC MAY edit
`BUILD.md`/`CLAUDE.md`/`DESIGN.md` autonomously** via the normal feature-branch
→ PR → automerge flow (autonomous multi-session flow, 2026-06-25 — the prior
spec-edit pre-approval gate is lifted); Klas reviews the diff post-merge.
Mandatory spec-edit agents still apply (dotnet-architect + code-reviewer; plus
design-reviewer for `DESIGN.md` design-token changes). CC does **not**: deploy
without Klas GO; add top-level dependencies without justification or libraries
outside BUILD.md §3.1 without discussion; violate §5 (a §5 anti-pattern is
never autonomous); start a new session phase without explicit Klas GO.

**Mandatory agent invocation** (before the STOPP report; skipping counts as a
discipline miss; reports go to `docs/reviews/<date>-<phase>-<agent>.md`):

| Agent | When |
|---|---|
| `senior-cto-advisor` | Multi-approach choices, finding triage (in-block vs TD), TD validation. Decision-maker — CC gives no own recommendation. Unambiguous CTO verdicts execute without extra Klas GO. |
| `security-auditor` | PII, auth, secrets, external integrations |
| `code-reviewer` + `dotnet-architect` | Larger changes (>5 files or architectural choices) |
| `dotnet-architect` (mandatory) | All Terraform/IaC scope (ADR 0036 precedent) |
| `db-migration-writer` | New migrations |
| `test-writer` | New domain types or handlers |

**9.3 When unsure:** read first (repo, BUILD.md, existing patterns) → ask
concrete questions → never guess whether a feature should exist.

**9.4 Discovery and verification.** Unsure about file state or existing
patterns → discovery report ("read/map X, report Y, no changes") with raw
full-file output, no truncation. After `str_replace`/paste: prove file state
with grep/diff output. Long pastes (>20 lines): pre-flight the target + new
content, wait for GO. Verbatim text (ADR sections, doc content) is produced by
web-Claude; CC applies. Missing source text after compaction → STOPP and ask.

**9.5 Web search for external facts.** Present-tense questions about
external systems (deploy providers, .NET/Next.js versions, AI models/pricing,
Claude features, NuGet/npm status) → search before answering, never guess
from training data. Official docs > registries > blogs; verify dates; cite
URL + date in the STOPP report.

**9.6/9.7 TD discipline.** Default = fix in-block. A TD may be raised only
for a different phase or a missing functional dependency — full mechanics,
formats, and lifecycle in the **`jobbpilot-td-lifecycle` skill**. When in
doubt, in-block wins (quality > tempo) and senior-cto-advisor decides.

## 10. Swedish UI rules

- UI copy and user-facing errors: Swedish. Comments/docs/commits: English
  (§1). Locale: dates `YYYY-MM-DD` or "14 apr 2026"; 24h time "14:32";
  decimal comma in UI, point in code; currency `1 234 kr` with non-breaking
  space; UTF-8 everywhere (åäö must survive serialization).
- Tone: "du" (never "Du"); direct, concrete Swedish ("Du har 3 aktiva
  ansökningar"); informative, non-blaming errors; never emoji; never
  exclamation marks; never "Hoppsan!"/"Oj då!".

## 11. Tooling

- Pre-commit (Husky + a hand-rolled `git diff --cached` filter, not
  lint-staged): staged `*.cs`/`*.csproj`/`*.props`/`*.targets`/`*.sln`/
  `global.json` → `dotnet format --verify-no-changes` + Domain/Application/
  Architecture unit tests; staged `web/jobbliggaren-web/` files → `pnpm lint`
  (ESLint, no `--fix`) + `pnpm tsc --noEmit`. No Prettier; `json`/`md`/`yaml`
  not auto-formatted.
- `.editorconfig` + committed `.vscode/` settings/extensions.
- Dev env: Docker Compose (`postgres`, `redis`, `seq`) — logging is console
  via MEL; no Serilog/Seq sink wired yet (full observability = TD-104,
  Hetzner phase). Everything runs locally (AWS retired, ADR 0066):
  `ConsoleEmailSender` for mail, `LocalDataKeyProvider` (AES-256-GCM) for
  field encryption. Frontend `.env.local`; backend
  `appsettings.Development.json` + gitignored `appsettings.Local.json`.

## 12. When something looks wrong

Violations of §5, Clean Architecture boundaries, non-BUILD.md libraries,
design-token changes outside DESIGN.md, or security-critical changes without
tests → **STOPP: do not automerge** — flag in a PR comment and wait for Klas.
This is the merge-blocking class referenced by the §6/§6.5 automerge exception
(alongside an unresolved agent Blocker/Major); everything else rides the
autonomous flow.

## 13. Update process

This file changes when a new anti-pattern, standard, or CC boundary is needed.
CC may propose **and apply** changes autonomously via PR + automerge (§9.2;
mandatory dotnet-architect + code-reviewer); Klas may also propose. Never
silently — always via a visible PR diff, which Klas reviews (post-merge under
automerge).

---

**End of CLAUDE.md.** Main spec in [`BUILD.md`](./BUILD.md), design in
[`DESIGN.md`](./DESIGN.md).
