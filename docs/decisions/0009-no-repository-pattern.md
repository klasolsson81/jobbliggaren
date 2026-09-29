# ADR 0009 — Inga Repositories; direkt IAppDbContext + IUnitOfWork

**Datum:** 2026-04-19
**Status:** Accepted
**Kontext:** Fas 0 kod-scaffolding, session 6. Formaliserar löfte i ADR 0001 §3.
**Beslutsfattare:** Klas Olsson
**Relaterad:** ADR 0001 §3, ADR 0008 (UnitOfWork-behavior), CLAUDE.md §5.1

## Kontext

ADR 0001 §3 lovar: "Ingen Repository-pattern (kommer dokumenteras separat i framtida ADR). Direkt DbContext i Application-handlers med IUnitOfWork-abstraktion." CLAUDE.md §5.1 listar Repository pattern ovanpå EF Core som explicit anti-pattern.

Inför Fas 1 när de första handlers skrivs behöver denna policy vara formaliserad med motivering så att code-reviewer-agenten och framtida utvecklare har ett tydligt beslutsunderlag.

Repository-pattern är ett vanligt .NET-mönster och en vanlig förfrågan. Utan dokumenterat beslut riskeras "men alla gör ju så"-argumentation.

## Beslut

Application-handlers injicerar `IAppDbContext` direkt. `IAppDbContext` är ett interface definierat i `JobbPilot.Application`, implementerat i `JobbPilot.Infrastructure` av EF Core:s `AppDbContext : DbContext, IAppDbContext`.

`IUnitOfWork` är pipeline behavior-abstraktionen (ADR 0008) som hanterar transaction-scope runt commands via `SaveChangesAsync()`. Queries kör utan UoW-behavior.

Specificering av `IAppDbContext`:
- Exponerar `DbSet<T>` properties för varje aggregate root
- Exponerar `SaveChangesAsync(CancellationToken)` 
- Exponerar inget EF Core-specifikt utöver det (inga `ChangeTracker`, `Database`, etc. i interfacet)

## Konsekvenser

**Positivt:**

- EF Core:s DbContext är redan en Unit of Work och ett in-memory Repository — att lägga Repository ovanpå är dubbelarbete utan nytt värde
- `IQueryable<T>` behöver inte wrappas eller läcka igenom ett abstrakt repository-interface (ett klassiskt Clean Arch-brott i Repository-implementationer)
- Handlers är direkta, läsbara och utan extra lager att navigera igenom
- Tester mot riktig databas (Testcontainers/PostgreSQL) ger higher-fidelity garantier än Repository-mockar
- Lättare att dra nytta av EF Core-features (value conversions, owned entities, compiled queries) utan att abstrahera bort dem

**Negativt:**

- Handlers är direkt beroende av EF Core-interfaces (via `IAppDbContext`) — databas-byte kräver Infrastructure-refactor
- Ingen enkel "swap in-memory store" för tester — kräver Testcontainers eller SQLite (som har egna begränsningar)
  *(See Amendment 2026-09-29 below.)*

**Mitigering:**

- Databas-byte är ett Infrastructure-concern (Clean Architecture-garantin) — Repository-pattern löser inte detta problem
- Testcontainers är standard i jobbpilot-teststack (se CLAUDE.md §7)
- EF Core In-Memory provider är förbjuden (false positives på transaktioner, constraints, concurrency) — SQLite i-memory är acceptabelt för enkla tester men Testcontainers är gold standard
  *(Narrowed by Amendment 2026-09-29 below.)*

## Alternativ övervägda

**Alt 1 — Generic Repository `IRepository<T>`:** Klassiskt mönster, avvisat. Abstraherar IQueryable utan att lösa databas-byteproblemet (som är Infrastructure-concern). Lägger till ett lager att navigera och testa utan arkitekturellt värde. Se CLAUDE.md §5.1.

**Alt 2 — Repository per aggregate (`IApplicationRepository`, `IJobSeekerRepository`):** Mer specifikt och testbart, men fortfarande dubbelarbete ovanpå EF Core. Handlers slutar direkt mot interface som mirrors DbSet-operations med extra namngivningssteg.

**Alt 3 — Specification pattern från start:** Skjuts till Fas 1+ när komplex query-logik faktiskt finns. CLAUDE.md §3.6 tillåter `ISpecification<T>` om samma filtrering används på 3+ ställen. En Specification i Application-lagret bryter inte mot principen — en Repository gör det.

**Alt 4 — Dapper för queries, EF Core för commands:** Välmotiverat i hög-prestanda-system. Overengineering för JobbPilots skala i Fas 0–1. Kan introduceras per query om profildata visar behov.

## Implementationsstatus

**Beslutsdatum:** 2026-04-19 (Fas 0 kod-scaffolding session 6)

**Ej implementerat än:** `IAppDbContext` och `AppDbContext` skrivs i Fas 1. Denna ADR dokumenterar beslutet innan implementation.

**Påverkar:**
- `JobbPilot.Application` — definierar `IAppDbContext`-interface
- `JobbPilot.Infrastructure` — implementerar `AppDbContext : DbContext, IAppDbContext`
- Architecture tests — verifierar att inget `Repository`-suffix finns i Application eller Infrastructure (utom eventuella Specification-klasser)

## Amendment 2026-09-27 (ADR 0146)

**Trigger:** ADR 0146's replay policy — `UnitOfWorkBehavior` clearing tracked state and re-running a
command on a fresh read after a lost optimistic-concurrency race on `job_seekers`.

`IAppDbContext` gains one new member, `ClearTracking()`, beside `Detach` (`IAppDbContext.cs:49-66`).
It maps to `ChangeTracker.Clear()` and detaches every tracked entity, not just one: after a
`DbUpdateConcurrencyException`, `UnitOfWorkBehavior` calls it (`UnitOfWorkBehavior.cs:44`) before
re-running the pipeline, so the re-read materialises fresh rows instead of resolving back to the
stale tracked instance — and the failed attempt's tracked audit row with it — via EF's identity
resolution.

**What does not change:** the original decision's sentence *"Exponerar inget EF Core-specifikt utöver
det (inga `ChangeTracker`, `Database`, etc. i interfacet)"* still holds, for the tracker **as an
object**. `IAppDbContext` exposes no `ChangeTracker` property, no `Database` property, and nothing
else EF-Core-shaped beyond `DbSet<T>`, `SaveChangesAsync`, `Detach` and now `ClearTracking`.
`ClearTracking()` is a narrow, named operation on the tracker — not the tracker itself — the same
shape `Detach(object entity)` already was. The same PR shows the boundary from the other side:
`AccountHardDeleter`, which already holds the concrete `AppDbContext` rather than the port, calls
`db.ChangeTracker.Clear()` directly in its own catch clause (`AccountHardDeleter.cs:315-320`) without
going through `IAppDbContext` at all — the tracker as an object stays off the port regardless of
which side of it a caller sits on.

**Referenser:** ADR 0146 D2, D4; `IAppDbContext.cs:49-66`; `UnitOfWorkBehavior.cs:44`;
`AccountHardDeleter.cs:315-320`.

## Amendment 2026-09-29 (#1918) — the InMemory fake is allowed for unit tests

**Trigger:** code-reviewer's ungraded escalation on PR #1924 (#1918), 2026-09-29. Two texts stood
against each other:
- **For the ban:** the original decision's line *"EF Core In-Memory provider är förbjuden (false
  positives på transaktioner, constraints, concurrency)"*, which code-reviewer's charter grades as a
  Major.
- **Against it:** the house fake `IAppDbContext`. `TestAppDbContextFactory`
  (`tests/Jobbliggaren.Application.UnitTests/Common/TestAppDbContextFactory.cs:16`,
  `UseInMemoryDatabase`) serves 119 files in Application.UnitTests (measured 2026-09-29 with `git grep
  -l "TestAppDbContextFactory.Create" -- tests/Jobbliggaren.Application.UnitTests`). AGENTS.md
  §2.4 tests handlers "with fake DbContext + NSubstitute".

**Klas's answer, 2026-09-29 (AskUserQuestion, verbatim):** *"(a) Faken får finnas (Rekommenderat)"*.
Option (a) read: the fake is allowed for handler unit tests, and ADR 0009 and the charter are corrected
so that the ban covers what InMemory cannot witness, which is pinned against Testcontainers.

**Klas's second answer, 2026-09-29 (AskUserQuestion, verbatim):** *"(a) Ja, alla fakens tester
(Rekommenderat)"*

**What changes:** the ban is narrowed to its reason.
- **Allowed:** the InMemory provider as the house fake for unit tests, where it stands in for
  the port and the assertion is about the logic under test.
- **Forbidden:** asserting through it anything it cannot witness — transactions, constraints,
  concurrency (ADR 0146's xmin token), jsonb and other provider translation. Those are pinned against
  a real PostgreSQL through Testcontainers, which stays the gold standard for integration tests.
- **Updated in the same PR:** code-reviewer's and test-writer's charters.

**Referenser:** ADR 0146; AGENTS.md §2.4; `.claude/agents/code-reviewer.md` (area 4);
`.claude/agents/test-writer.md` ("What NOT to write").
