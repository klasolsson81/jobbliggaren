# Code-review — Fas 1 Block A3 (TD-31 UseHttpsRedirection env-gate-test)

**Status:** Approve (med Minor-noteringar — inga in-block-blockers)
**Granskat:** 2026-05-10
**Auktoritet:** CLAUDE.md §3 (stil/namngivning/error/async), §5.1 (anti-patterns), §7 (testing), §2.1 (Clean Arch)
**Scope:** `tests/JobbPilot.Api.IntegrationTests/Configuration/UseHttpsRedirectionGateTests.cs` (ny, 241 rader)
**Verify:** 557/557 PASS rapporterat (+3 nya tests). Källkoden i `Program.cs:150–158` matchar testförutsättningarna.

## Sammanfattning

A3 levererar strukturellt anti-regression-skydd för Sec-Major-2-gaten (`Program.cs:155`). Tre testfall täcker matrixen Production×{HttpsEnabled=false,true} + Development. Design återanvänder Postgres/Redis-Testcontainers-mönstret från `ProductionStartupFactory` inkl. TD-37-läxor (env-vars satta FÖRE `Services`-access, populerad `KnownNetworks`, både CS:er). Implementationen är solid; inga Clean-Arch- eller anti-pattern-överträdelser hittade.

## Fynd

### Critical
*Inga.*

### Major
*Inga.*

### Minor

1. **Test-namn matchar inte §3.2-format `<ClassUnderTest>_<Scenario>_<Expected>`.**
   Nuvarande: `GET_api_ready_returns_200_when_HttpsEnabled_false_in_Production`.
   Konventionen exemplifieras i CLAUDE.md §3.2 med `Application_TransitionTo_ThrowsWhenInvalid` (PascalCase + underscores som scenario-separator). Befintliga integration-tester i samma fil-mapp (`ProductionStartupSmokeTests`) följer dock samma snake_case-mönster — så detta är en **repo-wide konsekvens-fråga**, inte A3-specifik regression. Föreslås: lyft som TD-not för Fas 1 Block B (test-naming-normalisering) snarare än in-block-fix.

2. **Edge-case-täckning: enbart GET /api/ready.** Sec-Major-2-scenariot är ALB-health-check (vilket är GET /api/ready) — så täckningen matchar scenariot exakt. POST/PUT-redirect, `X-Forwarded-Proto: https` (ALB-terminerad HTTPS där middleware ska SKIP redirect) och andra endpoints är inte testade. Bedömning: **out-of-scope för TD-31** — gatens regression-yta är pipeline-registrering, inte middleware-beteende per verb/header. `X-Forwarded-Proto`-stigen täcks redan av `ForwardedHeadersConfig`-test-suite (separat ansvar). Inget krav på utökning; nämns för transparens.

3. **`GC.SuppressFinalize(this)` i bas-`DisposeAsync` (rad 138).** CA1816-rationale är giltig (basen är abstract men implementerar `IAsyncLifetime`/`IAsyncDisposable` via `WebApplicationFactory`). Sealed-derivat ärver finalizer-suppress korrekt. Noteras enbart — inget action-krav.

4. **Konstantduplicering: `"127.0.0.1/32"`, `"jobbpilot:"`, `"postgres:18"`, `"redis:8-alpine"`** dupliceras mellan `ProductionStartupFactory` och `HttpsRedirectionGateFactoryBase`. Inte i scope för A3, men kandidat för shared `IntegrationTestConstants`-static i framtida cleanup (Fas 1 Block B eller TD-not).

### Nit

5. **`using` utan braces på enrads if** (rad 133–134): `if (File.Exists(...)) File.Delete(...)`. Stil-konsistens-fråga; `dotnet format`-konfigurationen släpper igenom så detta är dokumenterat OK i repot.

## Punktvis bedömning mot frågorna

| § | Fråga | Bedömning |
|---|-------|-----------|
| 3.1 | file-scoped namespace, nullable | ✓ |
| 3.2 | test-namn-format | ✗ snake_case (Minor 1) — repo-wide-fråga |
| 3.4 | InitializeAsync exception-safety | ✓ `Task.WhenAll` startar containers innan env-vars sätts; partial-fail vid second container leaves first running men `DisposeAsync` körs av xunit oavsett — Testcontainers-Ryuk garbage-collectar |
| 7   | coverage tillräcklig | ✓ matrix Prod×{true,false}+Dev täcker gatens tre logiska grenar |
| 5.1 | DateTime.Now / static / dynamic / catch-all | ✓ inget förekommer |
| —   | test-isolation | ✓ tre separata Collections + xunit `parallelizeTestCollections=false` (verifierat i `xunit.runner.json`) gör process-global env-var race:fri |
| —   | cost-benefit 3×containers | **Pattern-konsistens vinner.** Shared base med byte av env-var mid-test skulle kräva host-rebuild (`WebApplicationFactory.Server` är cachad efter första `CreateClient`) — komplexare än värdet av ~30 sek CI-besparing. Accepterad kostnad. |

## Approve-status

**APPROVE** för commit på `main` per ADR 0019. Inga in-block-fixar krävs. Minor 1 (test-naming) lyfts som TD-not för Fas 1 Block B om Klas vill normalisera repo-wide. Minor 4 (konstantduplicering) lyfts som städ-TD om volymen växer.

## Föreslagna in-block-fixar

*Inga.* A3 är mergeklar som den står.
