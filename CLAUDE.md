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

## 1.6 Docs map

| Location | Purpose |
|---|---|
| `docs/current-work.md` (+`-archive.md`) | Session-state source of truth (+ archived blocks) |
| `docs/sessions/` | Per-session logs |
| `docs/decisions/` (+`README.md` index) | ADRs — create via `/new-adr` (adr-keeper); next number from the index |
| `docs/runbooks/` | Operational procedures |
| `docs/research/` (+`issues/`) | Findings, planning, open questions |
| `docs/reviews/` | Agent review reports |

**The backlog is GitHub Issues, and nothing else** (Klas-direktiv 2026-08-02). The
TD register — `docs/tech-debt.md`, its archive, and the `jobbpilot-td-lifecycle`
skill — is **retired**; see §9.6. Its 44 live entries were disposed of in the same
pass, and the breakdown lives in **one** place —
[#1172](https://github.com/klasolsson81/jobbliggaren/issues/1172) — which also carries
every parked entry **inline**, because both register files were gitignored and
"archived" would have meant deleted.
**A `TD-NNN` marker surviving in a tracked doc, ADR, runbook, workflow, or code
comment is a historical provenance citation**, like a commit hash: it records why
something exists and is not a pointer into a register you can still open. Where a
marker instead names work that was **never built**, it is not provenance but a
forward pointer into nothing, and **it shall be converted** to the issue that owns
that work the next time anyone touches that file. Some remain unconverted — this is a
standing requirement, not a claim that the sweep was exhaustive.

Top-level `BUILD.md`/`CLAUDE.md`/`DESIGN.md` may be edited autonomously via the
normal feature-branch → PR → automerge flow (§9.2/§6); Klas reviews the diff
post-merge. Mandatory spec-edit agents apply (dotnet-architect + code-reviewer;
design-reviewer for DESIGN.md design-token changes). Agents place new docs per
this map; when unsure, ask.

## 2. Core principles

**2.1 Clean Architecture is non-negotiable.** Domain depends on nothing —
not Mediator, not EF Core. Application depends on Domain and defines every
interface Infrastructure implements. Infrastructure implements them (EF Core,
external clients). Api/Worker compose DI only.

**The EF Core dependency rule, precisely** (ADR 0009, enforced by
`tests/Jobbliggaren.Architecture.Tests/DomainLayerTests.cs`). Three axes:

**1. Package.** **Domain = zero EF Core, no exceptions.** **Application MAY
reference the `Microsoft.EntityFrameworkCore` package** — §3.6 puts
`IAppDbContext` directly in handlers with no repository layer, and that is
impossible without it. ADR 0009 accepts the coupling knowingly; its
Konsekvenser/Negativt records "Handlers är direkt beroende av EF Core-interfaces
(via `IAppDbContext`)". Ratified trade-off, not drift. **Application must NEVER
reference a provider, relational, or EF-Identity package** — `Npgsql`,
`Npgsql.EntityFrameworkCore.PostgreSQL`, `Microsoft.EntityFrameworkCore.Relational`,
`.SqlServer`, `.Sqlite`, `Microsoft.AspNetCore.Identity.EntityFrameworkCore`.
`DomainLayerTests` fails the build on any of these Application actually uses
(NetArchTest reads type references in IL, not `PackageReference` entries). The
list here is a snapshot — the test file is authoritative.

**2. Port.** Application reaches the database only through `IAppDbContext`,
which exposes `DbSet<T>` per aggregate root and `SaveChangesAsync` — and
deliberately not `ChangeTracker` or `Database` (ADR 0009 §Beslut) — plus
`Detach`, added later for the ADR 0032 §5 upsert-retry. Ordinary
core EF Core over those `DbSet<T>`s is in bounds and needs no justification:
`AsNoTracking` (§3.6 default), `Include`, `IgnoreQueryFilters`, `ToListAsync`,
`ExecuteUpdate`/`ExecuteDelete`, `EF.Property`, `DbUpdateException`.

**3. Member — the trap the package boundary does not catch.** Some members
behind core-looking names are provider extensions: `EF.Functions.Like` is core,
but `EF.Functions.JsonExists`/`ILike` ship in the Npgsql package, and
`AsSplitQuery` is relational-only. When a query needs one, it goes behind an
Application-owned port implemented in Infrastructure
(`IJobAdRequirementBackfillFilter` — its doc comment cites this rule back) —
**never** by adding the package to `Jobbliggaren.Application.csproj`. Contrast
`EF.Property`: shadow-column reads ARE core and belong inline, no port.

So the line to stop at is the **provider** boundary, not the EF Core boundary.
If you are importing EF Core in **Domain** — stop. If you are reaching for
`Npgsql` or anything `.Relational` in **Application** — stop; the query belongs
behind a port.

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
  **A bulk-load path ANALYZEs the table it loaded** — in the job, once per
  completed run, never per batch — when that table is written by one periodic
  job or startup seeder, is read-only between runs, **and** has some column
  reaching a `WHERE`, join, `ORDER BY`, `GROUP BY` or `DISTINCT`. Those are the
  clauses whose estimates statistics inform; where no column reaches one, no
  *column* statistic can change the plan (verified 2026-07-25: the only **`src`**
  readers of `taxonomy_concepts`/`taxonomy_relations` are two predicate-free
  `ToListAsync` calls in `TaxonomyReadModel.LoadAsync`). Continuous DML excuses
  the table only where autovacuum **demonstrably** re-arms — check
  `last_autoanalyze`, never assume: `company_register` held zero statistics at a
  million rows. Place the call where its failure is survivable: fail-loud in a
  retry-bounded job, typed-catch-and-log at host startup (a typed catch that
  logs is not the §5 catch-all ban). Why:
  `ScbCompanyRegisterStore.AnalyzeAsync` (#560).

## 4. TypeScript / Next.js standards

- `strict: true`, no exceptions; `any` is **forbidden** — `unknown` + guards.
  ESLint via Husky (no Prettier on web). Functional components + hooks only.
- Files: components `PascalCase.tsx` (one export); hooks `useCamelCase.ts`;
  types in `types.ts` per folder; tests co-located (`Button.test.tsx`).
- Data: Server Components by default; `"use client"` only where interactivity
  requires it. **Client mutations go through Server Actions** — `useTransition`
  for pending state, `useOptimistic` where optimistic rendering is wanted — with
  one delivered exception: a **binary upload** goes through a BFF route
  (`app/api/cv/import/route.ts`), because Server Actions cannot stream
  `multipart/form-data` (`duplex: "half"`). That is the only **mutation** path
  outside Server Actions — several other client `fetch`es are POST-shaped *reads*.
- **Short-lived client reads** — keystroke-driven suggest, popover counts,
  draft-preview counts, on-demand document/blob fetches — use `AbortController`
  in a `useEffect`, and never a mutation path (ADR 0042 Beslut C is the
  precedent). The shape is a self-contained
  hook *or* a component-local `useEffect` — `lib/hooks/use-facet-counts.ts` is the
  former, `components/resumes/cv-preview.tsx` the latter. Debounce where input
  drives it; a one-shot read on an `enabled` flip needs none, and neither does a
  mount fetch of a component-local artefact that cannot be rendered server-side
  (`resumes/template-builder.tsx`'s blob preview).
- **Periodic refresh** is a visibility-aware `setInterval` + `fetch` in a
  dedicated client component (`shell/header-stats.tsx`, 10 min).
- §5's `useEffect`-for-data-fetching ban is about **a page's initial data**, which
  belongs in a Server Component. It does not reach a lazy, user-driven or periodic
  fetch — those are the two bullets above.
- Forms: React Hook Form + Zod — never loose `useState` for large forms.
- **Do not reach for TanStack Query.** It is not in `package.json` and never was,
  so adding it is an undiscussed dependency add (§9.2) — and on the read-suggest
  surface specifically, a reversal of ADR 0042 Beslut C, which is a Klas-GO
  supersession rather than a library choice. BUILD.md §3.1 records the delivered
  mechanisms above.
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
for data fetching (a page's **initial data** — see §4 for the delivered poll and
short-lived-client-read shapes, which this does not reach) · `console.log` in production · emoji in UI copy ·
exclamation marks (civic tone) · gradients/drop shadows > `shadow-sm`/glow/
glassmorphism — **sole exception:** the hero plate's dark-green gradient
(`--jp-hero-gradient`, scoped per ADR 0068) · radius > 6px except pills/badges
· `localStorage` for sensitive data · hardcoded UI strings (use `next-intl` +
`messages/sv.json`) · direct DOM manipulation.

**Tests:** a **production fact asserted off a premise production cannot produce**
— a hand-seeded row, a hand-built argument to a production entry point, or a
stubbed port return whose value the real adapter never emits. The obligation
attaches to the **assertion, not to the seam**: a stub, a `db.X.Add`, or a direct
`UPDATE` carries none when the state it creates is one `src/` does produce —
convenience is not the offence — and going *through* a production entry point is
no exemption when the argument is not (a hand-built `rawPayload` carrying the key
the ingest sanitizer strips is the measured case). Read the trigger against **the
state the assertion actually rests on**, neither a generalisation of it nor an
incidental detail beside it: a soft-deleted `ResumeVersion` is producible where a
soft-deleted **Master** is not, while a plan guard rests on a table's statistics
— a statistics regime production's own writers do produce — and never on the
identity of the rows its fixture generated. Where that state is produced by **no
path in `src/`**, the test **names the actor that produced it**
(`ResumeVersion.SoftDelete()`, `PurgeStaleRawPayloadsJob`, "the clock", "rows
written before migration X"); where that actor is callable in the test, the test
**asserts the actor's own predicate or transform admits the state**
(`PurgeThisAdsPayloadAsync` is the worked form); where the actor is retired, the
test **pins that the current writer does not produce the shape**
(`Write_EmitsNewKeys_AndNeverSsyk`) — the pin lives wherever the writer is
testable, and the seam **names the pin when it lives elsewhere**. A genuinely
unreachable state is permitted **only when declared unreachable**, and then may
assert only that the read side degrades safely if the invariant breaks — never
what production does.
**"No domain method exists" is a reject, not a disclosure**, and seam parity with
another test is not provenance: #843's fiction was authorised by explicit parity
with a legitimate seam whose SQL was identical.

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

**Comments — graded, not merge-blocking (Klas-direktiv 2026-08-04/05):** prose
restating what the next line already shows · a comment re-arguing a decision an
ADR or the commit message already owns (fix reasoning belongs in the commit
message, which is not reviewed as code) · **a live** measured number in a tracked
file — it decays within a commit or two, so publish the command that regenerates
it; a *dated historical* measurement of a finished event ("PR #1206 took 11
rounds") is §1.6 provenance and does not decay. Comment where the code cannot
show the thing itself, and nowhere else: comment mass is what turned review into
rounds. **A factually wrong comment — wrong number, wrong gate name, stale
§-reference — is a defect and is fixed. Imperfect phrasing is not** ("en kommentar
är ingen bugg"); it is graded in `code-reviewer`'s charter and routed by §9.6.
**Unlike every other list in §5, this block is not a §12 STOPP class** — see §12.

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
- **Automerge (ADR 0065 Amendment 2026-06-07; autonomous flow 2026-06-25; two-label
  split #836, 2026-07-27):** CC creates PRs and pushes without asking. **Two labels,
  two meanings, and they are not interchangeable:**
  - **`automerge` = INTENT** — *"this PR should merge when it is ready."* True at
    `gh pr create`; set it then. CC sets it; the PR-babysitter may set it too.
  - **`agents-done` = PERMISSION** — *"the mandatory agents (§9.2) have reported and
    no Blocker/Major is unresolved."* **Only the owning session sets this**, and only
    after actually waiting them in. Never the babysitter.

  `label-automerge.yml` arms auto-merge only when **both** are present; merge on
  green `ci`; Klas reviews the diff **post-merge**. **A push that carries content
  of its own removes `agents-done` and disables auto-merge** — the reviewers
  answered against a diff that is no longer the one merging; wait them in against
  the new head and set it again. That is also why a re-check after a verdict is
  **report-only** (§9.6): a reviewer that applies its own fix pushes content, and
  tears down the gate it was invoked to close. **Bringing the branch up to base does not** —
  `.github/scripts/is-pure-base-merge.sh` compares the pushed tree against the
  tree an automatic merge would produce and leaves the gate alone when they are
  identical, which is what `gh pr update-branch` produces. It is fail-closed:
  every error and every shape it cannot vouch for disarms.
  Spec-edits to BUILD/CLAUDE/DESIGN no longer require pre-approval (§9.2) —
  they ride the same flow. Exception (STOPP instead): an unresolved agent
  Blocker/Major, **or any §12 merge-blocking condition** (a §5 anti-pattern,
  Clean-Architecture boundary violation, non-BUILD.md library, design-token change
  outside DESIGN.md, or security-critical change without tests). Docs-sync lives in
  the same PR as the scope (tracked docs); gitignored session-state docs are updated
  locally (§6.5).

  *Why the split exists:* one label carried both meanings, so any actor legitimately
  expressing intent unavoidably granted permission. **The two incidents that measured
  it, and the mechanics, live in one place — `label-automerge.yml`'s header (#836)** —
  and are deliberately not restated here. The rule was never missing; it was
  unenforceable, so **§12 gains no new class here.**

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
  `steg-tracker.md`, `sessions/`, local `reviews/` and ADRs
  0074+) is absent from a fresh worktree. `.worktreeinclude` lists them; run
  `scripts/sync-worktree-docs.ps1 <worktree-path>` after creating a worktree.
  Secrets (`appsettings.Local.json`, `.env.local`) are NEVER synced into a
  worktree — the stack-owner injects them at runtime via env override
  (`ConnectionStrings__Postgres` from `.env`) so its worktree runs the real
  stack without committing or copying secrets.
- **Backlog = GitHub Issues** (`area:`/`hotspot:`/**`mvp`**/`P0`–`P3`/lane `BE`·`FE`·
  `BE+FE`/`wip`·`blocked` labels; `next-up` is on zero open issues as of 2026-08-02 and
  `mvp` replaced it in practice); `steg-tracker.md` is the strategic
  map.

  **`mvp` is the label you pick work from, and it is a second axis, not a fourth
  priority.** Klas-direktiv 2026-08-02: a couple of real test users on
  `jobbliggaren.se` **within a month of that date**. An issue earns `mvp` when **a real
  test user meets it, or it blocks going live** — that is the criterion, and **the second
  clause is doing real work**: measured 2026-08-02, 11 of 21 labelled issues carry
  `area:infra`/`area:auth` and no product-surface `area:` — the deploy stack (#196),
  backup (#197), key rotation (#198), the log sink (#1175). *(Area is a **proxy** for
  which clause applies, not an adjudicator: #1171 is `area:auth` and is a clause-1 case —
  a user meets a missing password reset — while #853 and #1033 are `area:docs` and are
  clause-2.)* Ties resolve toward labelling: a mis-labelled issue costs one backlog row,
  a mis-skipped user-facing defect ships.

  On the product side, *"a real test user meets it"* resolves to the **core features**
  Klas named: `/jobb` · `/ansokningar` · `/foretag` · the **smart watches** (industry +
  municipality) on the company page · `/cv/granska`. *(The CV **builder** is paused, so
  builder FEATURES are not MVP — but a builder-adjacent defect a user still meets is,
  e.g. #1061, where `/cv` offers entry points into the paused builder.)*

  **`P0`–`P3` grades severity and urgency; `mvp` says whether the item is in scope for
  reaching real users.** They are different questions and they cross — measured
  2026-08-02: three `mvp` issues are `P3` and eight non-`mvp` issues are `P2`. An
  ordinal scale cannot carry two orthogonal axes, and overloading `P0` to mean MVP
  would destroy the severity information on nearly every open issue (55 of 58 carry a
  `P`, measured 2026-08-02). *(Klas put it
  both as "kärnfunktion slår prio-siffra" and "MVP-kritiskt = hög prio"; these agree in
  practice — no non-`mvp` issue carries `P0`/`P1` — but the two-axis split is how the
  spec resolves them, not a quote.)*

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
  Automerge does **not** rebase: when a sibling lands, yours goes `BEHIND` and
  then sits there forever with green `ci` and automerge on, and nobody is told.
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
  assuming either of the other two. *(2026-07-14 hygiene pass,
  all measured: 44 dead local + 44 dead remote branches; #800/#801 shipped and
  still `wip` two days on; 9 `wip` claims against 4 running CCs.)*
  **The dead-REMOTE-branch half is mechanised since #725** —
  `.github/workflows/delete-merged-branches.yml` deletes remote branches whose PR
  has merged, daily or on `workflow_dispatch`. Do not re-file it, and do not read
  a surviving branch as a *new* defect before checking the sweep's last run. It is
  a **scheduled** sweep and not a merge-event handler for a measured reason:
  events triggered by `GITHUB_TOKEN` do not start workflow runs, so the merges
  that leave branches behind — every app-merge — are exactly the ones whose
  `pull_request: closed` event never fires. **Two mechanisms, one cause — don't
  collapse them:** that suppressed *workflow run* is also why CodeQL stopped
  running on main, whereas `delete_branch_on_merge` is a repo *setting* that
  never travels through the workflow engine at all — it simply follows the
  merging identity, and the app is not it. Same actor, different machinery; a fix
  aimed at the wrong one of the two does nothing. **Your LOCAL branches are still
  yours**, as is the `wip`/issue-close half of that measurement.
- **Never reap a worktree you did not create — and never one whose PR has not
  merged.** The general case belongs to the SessionStart reaper: a PR usually
  merges *after* its session has ended, so "clean up when it merges" is not a
  same-session action (ADR 0094). But that reaper only ever touches trees
  carrying a close-stamped marker, and a tree made with a raw `git worktree add`
  has none — measured 2026-07-14: **0 markers across 13 worktrees, 1121
  "no-marker" skips, one reap in the hook's entire history**. So it will not
  collect yours. The tree **you** made this session, whose PR **you** watched
  merge, you may remove yourself (rescue its gitignored docs first). Anyone
  else's: never, for any reason.
  **Liveness is the boolean the OWNER sets** (`.jbl-worktree.json` → `closed_at`),
  never an inference *you* make about someone else — ADR 0094 rejected age/pid
  liveness proxies outright: doubt resolves to skip, never to "probably fine".
  "I created it" is knowledge; "its lock looks stale" is a guess that yanks a
  live tree.
  And **land your `current-work.md` / `steg-tracker.md` edits in
  the main copy before you stop** — the rescue saves gitignored files the main
  copy does *not* have; it cannot save your edits to ones it already does.

## 7. Testing

Every new domain class: at least one invariant test. Every new handler: happy
path + validation failure. Every new endpoint: integration test. Lowered
Domain coverage: justified in the PR or rejected. Snapshot tests only for
stable components; E2E updated when critical flows change. Test premises follow
§5 `Tests:`.

```bash
dotnet test                                     # backend (every test project)
cd web/jobbliggaren-web && pnpm test            # frontend
cd web/jobbliggaren-web && pnpm playwright test # E2E
dotnet test --project tests/Jobbliggaren.Architecture.Tests  # architecture
```

**These suites run on Microsoft.Testing.Platform, not VSTest.** The VSTest-shaped
flags — `--filter`, `--logger`, `--collect`, `--nologo` — are rejected as invalid
command-line arguments: exit **5**, `Unknown option`, then a help dump. A path
passed positionally, project, directory or solution alike, fails differently:
exit **1**, the platform's catch-all, one line and no summary block at all.
Either way **zero tests run**. Select one project with `--project`, every project
with `--solution`, and a subset **inside** a project with MTP's own filters after
`--`: `--filter-class`, `--filter-method`, `--filter-trait`, each taking `*`
wildcards; a selector that matches nothing runs to completion and exits **8**. A
`Category` trait excludes nothing from a default run — the `Category=SmokeTest`
tests run in it. **The proof that a suite ran is the `total:` line, never the
exit code** — which after a pipe measures the pipe, and which exit 1 never prints
at all. §8 point 3 rests on that: "architecture tests green" is a non-zero
`total:` with `failed: 0`. #1311 was not a quiet failure — every form above says
`Zero tests ran` or names the right flag. It survived because nobody read the
line.

## 8. Definition of Done

1. Acceptance criteria (BUILD.md §2) met · 2. unit + integration tests,
coverage not lowered · 3. architecture tests green (§7 — read the `total:` line)
· 4. manually tested in dev · 5. Lighthouse > 90 on affected pages · 6. keyboard
+ screen-reader accessible · 7. domain events documented · 8. GDPR impact
assessed (new PII? logging? retention?) · 9. ADR written for architecture
decisions · 10. code review done.

## 9. Working with Claude Code

**9.1 On any task:** read the relevant BUILD.md section → check existing
patterns (reuse, don't invent) → identify the layer → test-first for new
domain logic → implement minimally → `dotnet test` + lint → conventional
commit → push branch, `gh pr create` with agent reports inline, set the
`automerge` label → **run the mandatory agents, wait in ALL of them, resolve every
Blocker/Major** — batched, and closed by scoped re-checks (§9.6; the procedure is
the `jobbpilot-review-discipline` skill) — **and only then set `agents-done`** (§6).

**9.2 Boundaries.** CC writes code, tests, migrations, CI config, docs;
proposes refactorings; creates ADRs for its architecture decisions. **CC MAY edit
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
| `senior-cto-advisor` | Multi-approach choices, finding triage (in-block vs follow-up PR vs issue). Routes a finding; never re-grades one — severity belongs to the agent that reported it (§9.6). Decision-maker — CC gives no own recommendation. Unambiguous CTO verdicts execute without extra Klas GO. |
| `security-auditor` | PII, auth, secrets, external integrations; **accepting a vulnerability rather than repairing it** — growing `pnpm.auditConfig.ignoreGhsas`, lowering `--audit-level`, or suppressing `NuGetAudit`/NU1901-NU1904 (ADR 0065 Amendment 2026-07-28 Beslut 4). Reducing exposure is not a trigger. Also every exposure-*increasing* change to the suppression surface itself: an `overrides` entry removed or its target lowered, a new override key **in open form**, a gated key becoming open, a removal from `ignoredBuiltDependencies`, and `pnpm/action-setup` raised **past 9** — that last is a migration, not a bump, since pnpm 11 reads none of this configuration, so every repair and the single acceptance go dead while the gate still reports clean. Full enumeration in her Triggers section, keyed to audit area 8 — it is written there, but a trigger only reachable from inside the file it triggers has no invocation path, so the class belongs here. She is that area's **named consumer** of `.github/scripts/audit-suppression-guard.sh`: the blocking gate audits with the ignore list *applied* and so cannot see an accepted advisory that has begun reaching production. The guard also runs in observe-only `audit` on every PR — but Dependabot PRs auto-merge without invoking any agent, and no cadence consults the measurement, so on the auto-merged patch/minor Dependabot PRs — the bulk of what drives that drift — **there is no reader at all**. Nor is there an obligation to read it on the manually reviewed remainder: the guard's `::warning::` does surface, in the Checks view, but `audit` is `continue-on-error` and absent from `ci`'s `needs`, so a finding changes nothing in the merge signal. The readerless set is therefore *larger* than the auto-merged one, not equal to it. That gap is named in ADR 0065's amendment and triaged there as a follow-up PR rather than a TD; **no owner is assigned**, and it is not closed. |
| `code-reviewer` + `dotnet-architect` | Larger changes (>5 files or architectural choices) |
| `dotnet-architect` (mandatory) | All Terraform/IaC scope (ADR 0036 precedent) |
| `db-migration-writer` | New migrations |
| `test-writer` | New domain types or handlers |

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
(v2.1.198+), and a background subagent keeps only a fixed built-in set — `Read`,
`Grep`, `Glob`, `Bash`, `PowerShell`, `Edit`, `Write`, `NotebookEdit`,
`WebFetch`, `WebSearch`, `TodoWrite`, `Skill`, `ToolSearch`, `EnterWorktree`,
`ExitWorktree`, `Monitor`, `TaskStop`, `SendMessage`, `Artifact` — with
everything else removed whether inherited or listed, so **the same definition
resolves to different tools in the foreground and the background**. `Agent` and
`ExitPlanMode` are the exceptions: they follow the first filter wherever the
subagent runs, and `Agent` drops only at the depth limit — so the charters'
Delegation sections are live, not dead text. That removal
"reports no error" unless it empties the list entirely: a charter section whose
tool never arrived comes back thin rather than failed, which is the shape to
suspect before believing a short report.

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

**9.6 Where a finding goes** (formerly §9.6/§9.7, which several ADRs still cite together)**.** Default is still **fix in-block** — quality > tempo,
and senior-cto-advisor decides when it is genuinely ambiguous. What changed
2026-08-02 is the alternative: **there is no TD register to raise anything into.**

**The list below answers "where does what is not fixed go" — never "what should
happen to a finding."** The default disposal is the fix itself; a destination is
for the remainder, and choosing one is the exception that needs a reason.
Measured 2026-08-10: the backlog grew +62 net in the eight days after the
register retired (4.3 filed per closed), and 48 of the 60 issues filed in the
last week were `area:infra`, one of them user-facing — not because the rule was
wrong but because "fix in-block" above was read as a router. **A session
therefore leaves the backlog no larger than it found it** — issues filed ≤
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
  GDPR implication** — which is why (2) could not reach these cases. ADR 0132's Amendment §1 derived
  the ground and ADR 0133 followed it: **one derivation, two homes**, which is the duplication this
  paragraph ends. Both ADRs are gitignored, so this paragraph is written to stand alone.

  **The bound is ONE condition, measured, in three parts — all three required.** The only data
  subject whose Art. 5, Art. 12–22 or Chapter V position is affected is the **controller himself**,
  or there is none at all: **(i) no registered bearer** — every account is one the controller himself
  holds, measured against the account table; **(ii) no reached bearer** — every send has reached him,
  measured against the send log; **(iii) no reader** — the copy carrying the affected statement is
  not publicly readable, measured against the live surfaces. **(iii) is not an instance of (i)**, and
  writing it out is the whole point: publishing a false transparency statement about a live
  processing breaches Art. 5(1)(a)/12(1) with **no registered data subject at all**, so (i) can
  hold — vacuously — while (iii) fails. Both delivered instances rested on all three.

  ⚠ **The three parts measure the criterion; they do not replace it.** They are the registers where a
  bearer has arisen so far — account, send, public reading — and **none of them measures CONTENT**. A
  bearer none of the three reaches fails the condition all the same: a referee named inside a CV the
  controller himself uploaded, or a sole trader in `company_register` whose company name is a
  person's name, holds no account, received no send, and sits behind no public page. **Read the
  criterion first and the parts as its instruments** — the opposite of the lapse clause below, where
  the general sentence under-triggers and the enumeration governs. The two are not the same shape,
  and applying one's lesson to the other gets it exactly backwards.

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
  is then **resolved by acceptance** — which is what §12 means by *unresolved*; its *0 Blocker /
  0 Major* wording describes the ordinary case, not a signed acceptance — so the PR rides the normal
  flow. **Every other applicable §12 class must still clear independently**, and a §5 `Security:`
  class clears through Klas, not through this route. *(Measured against both delivered instances:
  neither hit §5 `Security:` at all. That list is CODE anti-patterns; a GDPR-implicated Major is
  typically a legal-posture finding with no code form — so a reader who goes looking for a §5 class
  and finds none has not found a problem.)*

  ⚠ **This is not a lowered bar, and reading it as one is the failure mode to avoid.** It writes down
  the bound that was already applied, so a third instance cites it instead of reinventing it — the
  duplication was the measured cost, not the standard. **Neither ground survives a widening.**
  Bearer-absence ends the moment there is a bearer — any of the three parts, or a bearer none of them
  reaches — and Art. 24(1) scales the measures **up** as the processing grows; together they license
  one acceptance, at one size, until the size changes. **An acceptance widened by a single
  non-controller data subject is an acceptance of a third party's rights, which this route does not
  grant and `security-auditor` does not sign.**

  **The lapse fires on ANY trigger the acceptance names, and one sentence is not a trigger set.**
  *"The first personal data reaching the processor that is not the controller's"* is the **ground**,
  not the operative form: a draft carrying it alone was graded **under-triggering**
  (`security-auditor` M-1, ADR 0133). The operative set is the acceptance's own, enumerated in one
  home and counted nowhere else. The delivered pair carries four — and one of them, **the copy
  becoming publicly readable, fires with no personal data reaching any processor at all.**

  **A GDPR Blocker is never in any of these three categories**, and neither is a Major whose bound
  cannot be measured. **(2) and (3) are granted by Klas — (3) also signed by `security-auditor` —
  and recorded in an ADR or a CLAUDE.md update, never by the session in a PR body. (1) is not a Klas
  grant at all** — per its own text above it is
  filed as an issue with the escalation named in it, and the PR proceeds. *(This closes
  `code-reviewer`'s standing escalation, raised on ADR 0132 and again on ADR 0133 (both 2026-08-16),
  that §9.6 offered no positive route here. §13 is why it lands in this file: the boundary is a CC
  boundary. That it belongs here **rather than in each ADR** is ADR 0133's own preamble — an ADR
  decides one processor's case, not a standing rule.)*
- **The finding does not hold** — its premise is false or revoked → say so plainly, with
  the measurement. Neither a fix nor an issue. This is a real outcome, not a way out.
- **Minor / nice-to-have** → a **GitHub issue**, and a line in a PR
  body is not disposal because it has no reader. The reason is **visibility between
  parallel CCs**, not issue inflation, so an issue no other CC would need to see may be
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
cannot be told from a claim that has decayed. Six of the retired register's entries
turned out already fixed the moment anyone measured them: they were true when written
and rotted in place, because nothing in that register's lifecycle ever required
re-measuring an entry, and an issue is re-read by no one on its own. A parked or
deferred item makes **no** truth claim and needs no measurement — but it must then be
written as scheduling ("not MVP scope, not verified"), never as fact ("still applies",
"no longer relevant").

**Closing a Blocker/Major — the scoped re-check** (measured 2026-08-09, PRs #1249/#1254:
0 blocking findings in under three minutes, against full rounds of twenty that generated
fresh sentences to defend). A fix landing after an agent's verdict goes back to **the agent
that issued it** — only the issuer can say its own finding is closed, and a fresh reviewer
re-reviews the whole PR. The re-check is **report-only** and scoped to the **fix delta**;
it grades that delta only — **no phrasing findings, and no new findings on lines the delta
did not touch, except a finding the re-checking charter itself grades as a Blocker or
defines repo-wide rather than per-diff**, which is always reported. That carve-out is not
optional: an unconditional gag would silence a GDPR or a11y veto the charter holds, and
§9.6 does not overrule a charter's own exceptions. What the re-check raises is routed by
this section as any finding is — a **new-in-delta Blocker/Major** is fixed and then
re-checked against the new delta, since §6 and §12 keep it merge-blocking. Nothing is fixed
in-block *during* a re-check: each in-block fix invalidates the check just run. Verify HEAD
is unchanged immediately before setting `agents-done`. **Charters and the skill carry
pointers here, never restatements** — a restatement that survives an edit to this section is
the drift #1173 measured, where a retired rule lived on in a satellite file for three
months. Batching, the delta command, the report-only prompt and the label checklist are the
`jobbpilot-review-discipline` skill's, and **§12 gains no new class here.**

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
- Dev env: Docker Compose (`postgres`, `redis`, `seq`) — MEL logs to console
  **and to Seq**: `AddJobbliggarenLogging` (shared by Api + Worker) attaches the
  Seq provider **only when `Seq:ServerUrl` is set**; the Hetzner residual is the
  *production* Seq, not the wiring. Everything runs locally: `LocalDataKeyProvider`
  (AES-256-GCM) for field encryption, and mail via `AddEmailSender`'s
  `Email:Provider` switch — **three** `IEmailSender` impls, not one:
  `ConsoleEmailSender` (Development/Test **only**; it logs the recipient address
  and the whole body, confirmation and activation links included — the gate is
  real recipients, not sink durability, since dev's Seq does persist that line.
  **That sink is accepted on two conditions, and the second is a condition to
  re-measure, never a standing fact.** (1) It is loopback-bound **and
  admin-authenticated** — auth was added 2026-08-04 (#1198) precisely because
  the binding alone had been *measured wrong for months* while the compose
  file's own comment vouched for it. (2) It holds no real-user PII — which was
  **measured FALSE on 2026-08-04**: 41 activation/confirmation links in
  plaintext plus one real address. That sink was discarded in the same PR — enabling auth
  required an empty volume — so the count is zero right now and **refills at the next dev
  registration**, because `ConsoleEmailSender` still logs the whole body. Nothing
  re-measures condition 2 on a cadence; [#1208](https://github.com/klasolsson81/jobbliggaren/issues/1208)
  owns that gap),
  `NullEmailSender` (what `Provider=Console` falls back to outside Dev/Test),
  and `ScalewayEmailSender` (`Provider=Scaleway` — Scaleway Transactional Email in
  `fr-par` over the **HTTPS API, never SMTP**; fail-loud without
  `Email:Scaleway:Region` **and** both `Email:Scaleway:SecretKey`/`ProjectId`, #183).
  **The count is still three because each provider REPLACED the last** — Resend,
  which Klas removed entirely on 2026-08-08; then SES, which AWS confined to
  sandbox by refusing production access on 2026-08-14; now Scaleway. `Resend` and
  `Ses` both now throw like any other unknown value, and `AddEmailSenderGateTests`
  pins that. **There is no .NET SDK and no package** — the arm is `HttpClient` +
  `System.Text.Json`, so `NoAmazonReferenceTests` went back to a total ban.
  The port lost its typed idempotency-key parameter in the Resend removal, and no
  provider since has had an equivalent to restore it for: neither SES v2
  `SendEmail` nor Scaleway's `POST /emails` carries one. What actually prevented
  double delivery was never the provider key but the claim-then-send spine (plus
  `StrandedMatchReaperJob`) and `ICooldownGate`, which ADR 0103 already states
  works *"regardless of Resend's own idempotency-key dedup"*; the residual
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
  and was declined deliberately — the Api suite sits **one `WebApplicationFactory` below**
  EF's process-global `ManyServiceProvidersCreatedWarning` ceiling, and the next host
  fells whichever collection fixture initialises after it
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
  "repair" it toward the current key: measured 2026-08-04, the same block injects two
  `FieldEncryption__*` options #802 removed, injects no master key (so a re-apply
  hard-fails at startup), and names `src/JobbPilot.*` Dockerfile paths that do not
  exist. Renaming one string buys one-of-N consistency and makes a record read as
  maintained. Retirement — and restoration — is a cutover ADR, never a cleanup sweep
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
  out-of-sync template fails the next stack-owner's boot one crash at a time (`#544`
  org.nr-HMAC + `#692` CV-fingerprint peppers both did — measured 2026-07-19).

## 12. When something looks wrong

Violations of §5, Clean Architecture boundaries, non-BUILD.md libraries,
design-token changes outside DESIGN.md, or security-critical changes without
tests → **STOPP: do not automerge** — flag in a PR comment and wait for Klas.
This is the merge-blocking class referenced by the §6/§6.5 automerge exception
(alongside an unresolved agent Blocker/Major); everything else rides the
autonomous flow.

**One §5 block is carved out: `Comments:`.** It is graded in `code-reviewer`'s
charter and routed by §9.6 — never a STOPP. A talkative comment that blocks a
merge costs more than it saves, which is what 2026-08-04/05 measured. Every
other §5 list stays fully STOPP-blocking.

**Scope clarification (Klas-direktiv 2026-07-16):** the security clause gates
on the two conditions it names — missing tests or an unresolved
security-auditor **Blocker/Major** — not on the subject matter alone. A
security-critical change **with** tests and a security-auditor **APPROVE
(0 Blocker / 0 Major, issued against the final diff)** rides the normal
automerge flow (§6); Klas reviews the diff post-merge and verifies FE surfaces
live (the FAS-DEFERRAL pattern). The earlier practice of holding tested,
auditor-approved security PRs for a manual pre-merge ("§12-gated — Klas
mergar") is retired: a gate that is always pressed through adds latency, not
review. This clarification touches only the security clause — the other §12
classes (§5 anti-patterns, Clean Architecture boundaries, non-BUILD.md
libraries, design tokens) remain fully STOPP-blocking, and every applicable
class must clear independently. Migration-bearing PRs are likewise untouched —
whether they ride automerge stays a per-case call (EF migrations remain the
most dangerous hotspot, §6.5).

## 13. Update process

This file changes when a new anti-pattern, standard, or CC boundary is needed.
CC may propose **and apply** changes autonomously via PR + automerge (§9.2;
mandatory dotnet-architect + code-reviewer); Klas may also propose. Never
silently — always via a visible PR diff, which Klas reviews (post-merge under
automerge).

---

**End of CLAUDE.md.** Main spec in [`BUILD.md`](./BUILD.md), design in
[`DESIGN.md`](./DESIGN.md).
