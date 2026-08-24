# AGENTS.md — Jobbliggaren shared core

> One §-namespace across this file and `CLAUDE.md`, which holds the §-index,
> §§1.5/6.5/9/11/13 and the `@AGENTS.md` import. Main spec: `BUILD.md` · design:
> `DESIGN.md`. Budget: CI-guarded (ADR 0135). An agent touching dev tooling,
> pre-commit gates, the compose stack or a fail-fast config option reads
> `CLAUDE.md` §11 first — it sits there for budget reasons, not because it is
> CC-specific.

## 1. Identity

Jobbliggaren is a Swedish job-application manager built as a **civic utility** —
think 1177 or Digg in tone, never Linear or Vercel. When unsure, choose what
feels *serious and trustworthy* over fun or trendy.

**Product owner:** Klas Olsson, .NET/fullstack student (NBI/Handelsakademin).
High quality bar, direct Swedish, no AI clichés. Write every commit as if it
must survive a Mastercard-level code review.

**Language policy (2026-06-12):** code identifiers in English; UI copy in
Swedish (`messages/sv/`); new docs, ADRs, session logs, reviews, commit
messages, and comments in **English**; chat replies to Klas in **Swedish**.
Existing Swedish docs are not mass-translated.

## 1.6 Docs map

| Location | Purpose |
|---|---|
| `docs/current-work.md` (+`-archive.md`) | Session-state source of truth (+ archived blocks) |
| `docs/sessions/` | Per-session logs |
| `docs/decisions/` (+`README.md` index) | ADRs — create via `/new-adr` (adr-keeper); next number from the index |
| `docs/runbooks/` | Operational procedures |
| `docs/research/` (+`issues/`) | Findings, planning, open questions |
| `docs/reviews/` | Agent review reports |
| `docs/spec-rationale.md` | Non-normative derivations, incidents and dated measurements, §-keyed |

**The backlog is GitHub Issues, and nothing else** (Klas-direktiv 2026-08-02). The
TD register — `docs/tech-debt.md`, its archive, and the `jobbpilot-td-lifecycle`
skill — is **retired**; see §9.6, and read
[#1172](https://github.com/klasolsson81/jobbliggaren/issues/1172) before concluding the
register is missing anything.
**A `TD-NNN` marker surviving in a tracked doc, ADR, runbook, workflow, or code
comment is a historical provenance citation**, like a commit hash: it records why
something exists and is not a pointer into a register you can still open. Where a
marker instead names work that was **never built**, it is not provenance but a
forward pointer into nothing, and **it shall be converted** to the issue that owns
that work the next time anyone touches that file. Some remain unconverted — this is a
standing requirement, not a claim that the sweep was exhaustive.

Top-level `BUILD.md`/`CLAUDE.md`/`AGENTS.md`/`DESIGN.md` may be edited autonomously via the
normal feature-branch → PR → automerge flow (§9.2/§6); Klas reviews the diff
post-merge. Mandatory spec-edit agents per §9.2 (CLAUDE.md). Agents place new
docs per this map; when unsure, ask.

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
impossible without it. ADR 0009 accepts the coupling knowingly. **Application must NEVER
reference a provider, relational, or EF-Identity package** — `Npgsql`,
`Npgsql.EntityFrameworkCore.PostgreSQL`, `Microsoft.EntityFrameworkCore.Relational`,
`.SqlServer`, `.Sqlite`, `Microsoft.AspNetCore.Identity.EntityFrameworkCore`.
`DomainLayerTests` fails the build on any of these Application actually uses. The
list here is a snapshot — the test file is authoritative.

**2. Port.** Application reaches the database only through `IAppDbContext`,
which exposes `DbSet<T>` per aggregate root and `SaveChangesAsync` — and
deliberately not `ChangeTracker` or `Database` (ADR 0009 §Beslut) — plus
`Detach`. Ordinary
core EF Core over those `DbSet<T>`s is in bounds and needs no justification:
`AsNoTracking` (§3.6 default), `Include`, `IgnoreQueryFilters`, `ToListAsync`,
`ExecuteUpdate`/`ExecuteDelete`, `EF.Property`, `DbUpdateException`.

**3. Member — the trap the package boundary does not catch.** Some members
behind core-looking names are provider extensions: `EF.Functions.Like` is core,
but `EF.Functions.JsonExists`/`ILike` ship in the Npgsql package, and
`AsSplitQuery` is relational-only. When a query needs one, it goes behind an
Application-owned port implemented in Infrastructure —
**never** by adding the package to `Jobbliggaren.Application.csproj`. Contrast
`EF.Property`: shadow-column reads ARE core and belong inline, no port.

So the line to stop at is the **provider** boundary, not the EF Core boundary.
If you are importing EF Core in **Domain** — stop. If you are reaching for
`Npgsql` or anything `.Relational` in **Application** — stop; the query belongs
behind a port.

*Derivations and worked instances: `docs/spec-rationale.md` §2.*

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
  *column* statistic can change the plan. Continuous DML excuses
  the table only where autovacuum **demonstrably** re-arms — check
  `last_autoanalyze`, never assume. Place the call where its failure is survivable: fail-loud in a
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
  so adding it is an undiscussed dependency add (§9.2). BUILD.md §3.1 records the
  delivered mechanisms above.
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
`messages/sv/`) · direct DOM manipulation.

**Tests:** a **production fact asserted off a premise production cannot produce**
— a hand-seeded row, a hand-built argument to a production entry point, or a
stubbed port return whose value the real adapter never emits. The obligation
attaches to the **assertion, not to the seam**: a stub, a `db.X.Add`, or a direct
`UPDATE` carries none when the state it creates is one `src/` does produce —
convenience is not the offence — and going *through* a production entry point is
no exemption when the argument is not. Read the trigger against **the
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
another test is not provenance.

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
show the thing itself, and nowhere else. **A factually wrong comment — wrong number, wrong gate name, stale
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
  split #836, 2026-07-27):** the driving agent — CC and Codex alike (Klas-direktiv
  2026-08-22, ADR 0135) — creates PRs and pushes without asking. **Two labels,
  two meanings, and they are not interchangeable:**
  - **`automerge` = INTENT** — *"this PR should merge when it is ready."* True at
    `gh pr create`; set it then. The driving agent sets it; the PR-babysitter may set it too.
  - **`agents-done` = PERMISSION** — *"the mandatory agents (§9.2) have reported and
    no Blocker/Major is unresolved."* **The session that ran the §9.2 panel sets it,
    and only after actually waiting them in — whichever tool drove it** (Klas-direktiv
    2026-08-22, ADR 0135 Amendment 2: every driving tool has its own invocation path;
    the label attests that the panel ran clean, never which runtime ran it). A session
    that has not run it does not set it, and does not merge. Never the babysitter.

  `label-automerge.yml` arms auto-merge only when **both** are present; merge on
  green `ci`; Klas reviews the diff **post-merge**. **A push that carries content
  of its own removes `agents-done` and disables auto-merge** — the reviewers
  answered against a diff that is no longer the one merging; wait them in against
  the new head and set it again. That is also why a re-check after a verdict is
  **report-only** (§9.6): a reviewer that applies its own fix pushes content, and
  tears down the gate it was invoked to close. **Bringing the branch up to base does not**
  (`.github/scripts/is-pure-base-merge.sh`, fail-closed: every error and every
  shape it cannot vouch for disarms).
  Spec-edits no longer require pre-approval — they ride the same flow (§9.2,
  CLAUDE.md). Exception (STOPP instead): an unresolved agent
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
`total:` with `failed: 0`.

## 8. Definition of Done

1. Acceptance criteria (BUILD.md §2) met · 2. unit + integration tests,
coverage not lowered · 3. architecture tests green (§7 — read the `total:` line)
· 4. manually tested in dev — **a surface's non-resting states (error, refusal,
outcome/receipt, empty, loading) that the delta introduces, changes, or makes
reachable are rendered before a design verdict** (trigger, cost bound and mechanics:
`docs/runbooks/frontend-visual-verification.md`) · 5. Lighthouse > 90 on affected pages · 6. keyboard
+ screen-reader accessible · 7. domain events documented · 8. GDPR impact
assessed (new PII? logging? retention?) · 9. ADR written for architecture
decisions · 10. code review done.

## 10. Swedish UI rules

- UI copy and user-facing errors: Swedish. Comments/docs/commits: English
  (§1). Locale: dates `YYYY-MM-DD` or "14 apr 2026"; 24h time "14:32";
  decimal comma in UI, point in code; currency `1 234 kr` with non-breaking
  space; UTF-8 everywhere (åäö must survive serialization).
- Tone: "du" (never "Du"); direct, concrete Swedish ("Du har 3 aktiva
  ansökningar"); informative, non-blaming errors; never emoji; never
  exclamation marks; never "Hoppsan!"/"Oj då!".

## 12. When something looks wrong

Violations of §5, Clean Architecture boundaries, non-BUILD.md libraries,
design-token changes outside DESIGN.md, or security-critical changes without
tests → **STOPP: do not automerge** — flag in a PR comment and wait for Klas.
This is the merge-blocking class referenced by the §6/§6.5 automerge exception
(alongside an unresolved agent Blocker/Major); everything else rides the
autonomous flow.

**One §5 block is carved out: `Comments:`.** It is graded in `code-reviewer`'s
charter and routed by §9.6 — never a STOPP. A talkative comment that blocks a
merge costs more than it saves. Every
other §5 list stays fully STOPP-blocking.

**Scope clarification (Klas-direktiv 2026-07-16):** the security clause gates
on the two conditions it names — missing tests or an unresolved
security-auditor **Blocker/Major** — not on the subject matter alone. A
security-critical change **with** tests and a security-auditor **APPROVE
(0 Blocker / 0 Major, issued against the final diff)** rides the normal
automerge flow (§6); Klas reviews the diff post-merge and verifies FE surfaces
live (the FAS-DEFERRAL pattern). The earlier practice of holding tested,
auditor-approved security PRs for a manual pre-merge ("§12-gated — Klas
mergar") is retired. This clarification touches only the security clause — the other §12
classes (§5 anti-patterns, Clean Architecture boundaries, non-BUILD.md
libraries, design tokens) remain fully STOPP-blocking, and every applicable
class must clear independently. Migration-bearing PRs are likewise untouched —
whether they ride automerge stays a per-case call (EF migrations remain the
most dangerous hotspot, §6.5).

