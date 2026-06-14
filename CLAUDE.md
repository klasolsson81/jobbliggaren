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

Top-level `BUILD.md`/`CLAUDE.md`/`DESIGN.md` are edited only on explicit Klas
instruction (reviewed via PR diff). Agents place new docs per this map; when
unsure, ask.

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
- **Errors:** expected failures → `Result<TSuccess, TError>`; unexpected →
  exceptions. `DomainException` → 400 via middleware; `NotFoundException` →
  404. Never `throw new Exception(...)` — always a specific subclass.
- **Async:** `CancellationToken` propagated end-to-end. Never `.Result` or
  `.Wait()`. `Task.Run` only for CPU-bound work. No `ConfigureAwait(false)`
  needed inside ASP.NET Core.
- **3.6 Queries:** `IAppDbContext` directly in handlers — no repository layer.
  `ISpecification<T>` only when the same filter is used in 3+ places.
  `.AsNoTracking()` default for reads. `Include()` only when needed.
  Pagination via `.Skip().Take()` + separate count query.

## 4. TypeScript / Next.js standards

- `strict: true`, no exceptions; `any` is **forbidden** — `unknown` + guards.
  ESLint + Prettier via Husky. Functional components + hooks only.
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
  (`ci` aggregate green; observe-only jobs don't block) → pre-push hooks
  (gitleaks, dotnet format, lint-staged).
- **Automerge (ADR 0065 Amendment 2026-06-07):** CC sets the `automerge`
  label on its own PRs (`gh pr edit <nr> --add-label automerge`); merge on
  green `ci`; Klas reviews the diff **post-merge**. Exception (STOPP instead):
  unresolved agent Blocker/Major, or a spec-edit PR whose edit is not yet
  approved. Docs-sync lives in the same PR as the scope.

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
creates ADRs for its architecture decisions. CC does **not**: edit
BUILD.md/CLAUDE.md/DESIGN.md without explicit Klas instruction (reviewed via
PR diff); deploy without Klas GO; add top-level dependencies without
justification or libraries outside BUILD.md §3.1 without discussion; violate
§5; start a new session phase without explicit Klas GO.

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

- Pre-commit (Husky + lint-staged): `*.cs` → `dotnet format`; `*.{ts,tsx,js,jsx}`
  → eslint --fix + prettier; `*.{json,md,yaml,yml}` → prettier.
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
tests → stop, flag in a PR comment, discuss with Klas before merge.

## 13. Update process

This file changes when a new anti-pattern, standard, or CC boundary is needed:
Klas proposes → PR with discussion → merge on agreement. Never silently.

---

**End of CLAUDE.md.** Main spec in [`BUILD.md`](./BUILD.md), design in
[`DESIGN.md`](./DESIGN.md).
