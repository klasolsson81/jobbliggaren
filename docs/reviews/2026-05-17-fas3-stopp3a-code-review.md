# Code-review: FAS 3 STOPP 3a /ansokningar backend-vertikal (ManualPosting + cross-aggregat-read-join)

**Status:** GO (APPROVED — 0 Blocker, 0 Major, 2 Minor in-block-fixbara)
**Granskat:** 2026-05-18
**Auktoritet:** CLAUDE.md §2 (Clean Arch/DDD/CQRS), §3 (C#-standarder), §5.1 (anti-patterns), §7 (test), §10 (svenska)
**Scope:** Backend — Domain + Application + Infrastructure + frontend DTO-kontrakt (Zod). EJ design-estetik (design-reviewer separat), EJ säkerhet (security-auditor GO klar), EJ SQL-djupverifiering (architect-gate + testflytt-SQL-bevis separat).
**HEAD:** 72c3ca8 (verifierad — arbetskopian ej committad)
**Trail verifierad:** architect-design 15-steg + (D)-inmemory-fix + CTO-1 (shadow-FK) + CTO-2 (väg B testflytt, shadow-FK rivet) + testflytt-rapport + security-rapport.

---

## Sammanfattning

Vertikalen är **spec-trogen mot hela besluts-trailen** och **arkitektoniskt ren**. Clean Arch-gränserna intakta, DDD-invarianten korrekt placerad i aggregatet, CQRS additivt bakåtkompatibelt, testpyramiden korrekt omklassad per CTO-2. Shadow-FK (CTO-1) är fullständigt rivet — ingen död kod kvar i Application-vertikalen. Inga Blocker, inga Major. Två Minor som är in-block-fixbara (§9.6) men inte mergeblockerande.

---

## Spec-trohetsverifiering (besluts-trailen)

| Beslut | Krav | Verifierat | Status |
|---|---|---|---|
| ManualPosting Variant A | `sealed record` i `Domain/Applications/`, privat ctor + `Create→Result` | `ManualPosting.cs:13,20,28` | OK |
| Source struken (Klas, plan §59) | Ingen Source i 4 ytor: VO/Input/Command/validator | `ManualPosting.cs` (ingen Source), `CreateApplicationCommand.cs:23-27` (`ManualPostingInput` ingen Source), validator ingen Source | OK |
| Invariant JobAdId⊕ManualPosting | I `Application.Create` FÖRE `new`, ej i handler | `Application.cs:68-72` | OK §2.2 |
| (null,null)→Success bevarat | Degenererat cover-letter-only ej regresserat | `Application.cs:68` (endast "icke-båda"), test `ApplicationManualPostingInvariantTests.cs:53-73` | OK |
| Utökad signatur, ej overload | En väg in i aggregatet | `Application.cs:49-54` (en `Create`, ManualPosting sist före clock) | OK |
| J1 PublishedAt nullable | ManualPosting-gren projicerar `PublishedAt=null`, ALDRIG CreatedAt | 3 read-handlers gren 2 `(DateTimeOffset?)null` | OK |
| TD-80 URL-whitelist | Identisk http(s)-scheme-whitelist som JobAd | `ManualPosting.cs:48-60` | OK §5.1 defense-in-depth |
| ADR 0048 EN LEFT JOIN | GroupJoin/DefaultIfEmpty FÖRE materialisering, query-filter ärvs, `IgnoreQueryFilters` förbjudet | 3 handlers; SQL-bevis i testflytt-rapport (1 LEFT JOIN, `j.deleted_at IS NULL`) | OK §2.3/§3.6 |
| (D)-fix join-härledd JobAdGuid | `j != null ? (Guid?)j.Id.Value : null` (architect-godkänt) | 3 handlers identiskt | OK (se Not 1) |
| CTO-2 väg B: shadow-FK rivet | Ingen `JobAdRef`/`EF.Property` i Application-vertikalen | Verifierat 0 träffar (kvarvarande EF.Property = orelaterad JobAd-taxonomi F2-P9) | OK |
| CTO-2 testflytt | 4 read-handler-klasser → `Api.IntegrationTests` (Npgsql), unit-filer borttagna | git status bekräftar D + ?? -mönster; testflytt-rapport 32 grön | OK §7 |
| Migration 4 nullable kol | Ingen default/backfill/index, Down=4 DROP | `20260517222003_*.cs` | OK |
| EF optional owned | `OwnsOne` + explicit `HasColumnName` + `IsRequired(false)` | `ApplicationConfiguration.cs:40-54`; snapshot `b.Navigation("ManualPosting")` utan required | OK |
| Zod additivt deploy-säkert | `jobAdSummaryDtoSchema` + `jobAd: ...nullable().optional()` | `applications.ts:36-58` | OK |

Samtliga 13 spec-punkter trogna. Source-strykningen genomförd konsekvent i alla fyra ytor.

---

## Område 1: Clean Architecture — OK

- `ManualPosting.cs`: importerar endast `JobbPilot.Domain.Common`. Ingen EF, ingen Mediator. Domain-renhet intakt.
- `Application.cs`: ingen extern import tillagd; ManualPosting-property + ctor + Create. AggregateRoot-mönster bevarat.
- Invariant i `Application.Create` (`Application.cs:68-72`), **ej** i handler — `CreateApplicationCommandHandler.cs:50-53` delegerar rakt till `DomainApplication.Create`. CLAUDE.md §2.2 uppfyllt.
- Read-DTO ut genom Application-gränsen: `JobAdSummaryDto`/`ApplicationDto`/`ApplicationDetailDto` är records, inga domänobjekt läcker (§2.3). `j.Source.Value`/`j.Company.Name` projiceras till primitiver i query-trädet.
- EF-konfiguration ligger i Infrastructure (`ApplicationConfiguration.cs`), korrekt lager.

## Område 2: DDD — OK

- `ManualPosting` = `sealed record`, privat ctor, statisk `Create→Result`. VO-precedens (Company/ExternalReference) följd. Record-default value-equality korrekt (test `ApplicationManualPostingInvariantTests.cs:119` `ShouldBe(manual)` verifierar).
- Invariant-matris korrekt och testad: alla 4 fall i `ApplicationManualPostingInvariantTests.cs` inkl. BLOCKING `(null,null)→Success`-regressionsskydd (rad 53-73) och event-ej-raisat-vid-brott (rad 86-95).
- `ManualPosting?` med `private set` (`Application.cs:12`) — konsekvent med övriga props; EF sätter via owned-mappning.
- `ApplicationCreatedDomainEvent`-signatur oförändrad (`Application.cs:78`) — korrekt, ingen lyssnare behöver ManualPosting (YAGNI, architect §2).

## Område 3: CQRS — OK

- Command returnerar `Result<Guid>` (`CreateApplicationCommand.cs:12`), handler `Result.Failure<Guid>` propagerar `ManualPosting.Create`-fel (`CreateApplicationCommandHandler.cs:45-46`) och domän-invariant-fel (rad 52-53). Korrekt.
- Queries returnerar DTOs; `JobAdSummaryDto? JobAd` additivt **sist** på `ApplicationDto` (rad 10) och `ApplicationDetailDto` (rad 13) — bakåtkompatibelt, rå `JobAdId Guid?` bevarad.
- `AsNoTracking()` på alla tre read-handlers (§3.6). Separat `CountAsync` i GetApplications (rad 39) — projection-fri count, korrekt §3.6.
- EN LEFT JOIN-form bevisad i testflytt-rapporten (verbatim SQL: en `LEFT JOIN job_ads`, `j.deleted_at IS NULL` på join-grenen, ingen N+1). 3-grens-projektion (JobAd→ManualPosting[Source="Manual",PublishedAt=null]→null) konsekvent i alla tre handlers.
- Pipeline `GroupBy` in-memory EFTER materialisering (`GetPipelineQueryHandler.cs:63-66`) — korrekt mönster, all data i den ENA queryn (N+1-fri).

## Område 4 (§3 C#-standarder) — OK

- File-scoped namespaces överallt. Nullable enabled, inga `!`-suppressions utan grund (`Application.cs` rena; test `ManualPosting!.Title` är legitim post-`ShouldNotBeNull`).
- Inga `any`/`dynamic`. `Async`-suffix + `CancellationToken` propagerad genom alla handlers (`CreateApplicationCommandHandler.cs:18,27`, read-handlers).
- Result-pattern konsekvent. `IDateTimeProvider clock` injicerad — ingen `DateTime.UtcNow` direkt (`Application.cs:74` `clock.UtcNow`).
- Namngivning: `CreateApplicationCommandHandler`, `JobAdSummaryDto` — konventionsenligt.
- `IReadOnlyList<T>` på exponerade collections (`ApplicationDetailDto.cs:11-12`).

## Område 5 (§5.1 anti-patterns) — OK

- Ingen primitive obsession: ManualPosting är VO, ej lösa kolumner i Application. EF mappar via `OwnsOne`.
- Ingen Repository, ingen AutoMapper, ingen `DateTime.Now`, inga magic-string-statusar (literal `"Manual"` är Source-projektions-literal, motiverad i architect §1/§5b — ej domän-konstant-magic-string; konsekvent med `j.Source.Value`-axeln).
- Shadow-FK (CTO-1 `JobAdRef`) **fullständigt rivet**: 0 träffar i Application-vertikalen. `ApplicationConfiguration.cs` har ingen shadow-property; join-nyckel `a => a.JobAdId, j => j.Id` (relationell, Npgsql-verifierad grön i testflytt-rapport). Inga dead `using`. Verifierat mot config + 3 handlers + snapshot.
- `EF.Property`-träffarna i `JobAdSearch.cs`/`JobAdConfiguration.cs` tillhör orelaterad JobAd-taxonomi (F2-P9/ADR 0043) — utanför denna touch, ej regression.

## Område 7 (test) — OK

- Nya Domain-tester: `ManualPostingTests.cs`, `ApplicationManualPostingInvariantTests.cs` (invariant-matris + regressionsskydd). Korrekt nivå (UnitTests).
- Nya Application-tester: `CreateApplicationManualPostingHandlerTests.cs`, `CreateApplicationCommandValidatorTests.cs`. Korrekt nivå.
- Testflytt CTO-2 väg B: 4 read-handler-klasser → `Api.IntegrationTests/Applications/*IntegrationTests.cs` (Npgsql/Testcontainers), gamla InMemory-filer borttagna (git `D`). Testflytt-rapport bekräftar alla scenarier 1:1 bevarade + 1 addition (soft-deleted-fallback) — coverage ej sänkt (ADR 0044 PASS enligt STOPP-rapport).
- Call-site-migrering 4→5-arg: stickprov bekräftar `null` konsekvent på position 4 (manualPosting), före clock, i Domain- och Application-testfiler. Inga felaktiga injektioner i icke-Create-anrop. `seeker.Id, null, null, null, clock`-formen korrekt (jobAdId+coverLetter+manualPosting alla null).
- Build 0 fel, full Release-svit 1260/1260 0 failed/skipped, ADR 0044-golv alla PASS (per STOPP-rapport + testflytt-rapport SQL-bevis).

## Område 10 (svenska) — OK

- Domänfel-meddelanden svenska, korrekt ton: "Jobbtitel är obligatorisk.", "Företag får vara max 200 tecken.", "Annonslänk måste vara en giltig http(s)-URL.", "En ansökan kan inte vara både kopplad till en annons och manuellt angiven." Inga utropstecken, ingen emoji, informativa ej skyllande. Felkoder engelska (`ManualPosting.TitleRequired`) — kod/copy-separation korrekt §10.1.

---

## Minor (2 — in-block-fixbara §9.6, EJ mergeblockerande)

### Minor 1 — Spec-divergens i join-nyckel-form (dokumenterad, funktionellt korrekt)

**Fil:** `GetApplicationsQueryHandler.cs:49`, `GetApplicationByIdQueryHandler.cs:39`, `GetPipelineQueryHandler.cs:35`
**Observation:** Join-nyckeln är `a => a.JobAdId, j => j.Id`. CTO-2 §4.2 specificerade `a => a.JobAdId, j => (JobAdId?)j.Id`. Den faktiska formen saknar den explicita `(JobAdId?)`-casten på höger nyckel.
**Bedömning:** Inte ett fel. Testflytt-rapportens verbatim Npgsql-SQL bevisar korrekt `LEFT JOIN ... ON a0.job_ad_id = j0.id` med query-filter applicerat; full svit 1260/1260 grön. Formen är funktionellt ekvivalent och EF-translaterar korrekt (converter på båda sidor symmetrisk under Npgsql). Divergensen är en syntaktisk förenkling som inte påverkar genererad SQL eller semantik. Noteras för trail-spårbarhet; ingen åtgärd krävd för merge.
**Rekommendation:** Acceptera som-är. Ev. kommentar-rad som pekar på testflytt-rapportens SQL-bevis vore self-documenting, men är polish, ej krav.

### Minor 2 — `JobAdGuid`-semantik: rå JobAdId-axeln nollställs vid soft-deletad JobAd

**Fil:** 3 read-handlers, `JobAdGuid = j != null ? (Guid?)j.Id.Value : null`
**Observation:** `ApplicationDto.JobAdId` härleds nu ur joinad JobAd (j), ej ur `Application.JobAdId`. För en ansökan vars JobAd är soft-deletad blir `dto.JobAdId == null` trots att `Application.JobAdId` har värde. Detta avviker från architect-design §5b ursprungsform (`a.JobAdId == null ? null : a.JobAdId.Value.Value` — rå FK bevarad).
**Bedömning:** **Medvetet och architect-godkänt.** (D)-inmemory-fix-rapporten §"Semantik" deklarerar detta explicit önskat per ADR 0048: "FK exponeras ej mot rad användaren ej får se; matchar JobAdSummary-fallback i samma projektion." Konsekvent — `JobAdId` och `JobAd`-summary nollställs tillsammans. Ingen åtgärd; dokumenteras här för spårbarhet eftersom det är en beteendeförändring på den råa `JobAdId`-axeln för soft-deleted-fallet.
**Rekommendation:** Acceptera. Architect äger designen och har motiverat avvikelsen mot ADR 0048.

---

## Bra gjort

- Source-strykningen konsekvent genomförd i alla fyra ytor utan dead axis — exakt CLAUDE.md §5.1.
- TD-80 URL-whitelist återanvänd verbatim i `ManualPosting.Create` (defense-in-depth: validator + VO-factory + samma OWASP A01-yta som JobAd).
- Invariant-test-matris komplett inkl. BLOCKING `(null,null)→Success`-regressionsskydd och event-ej-raisat-vid-brott.
- Shadow-FK (CTO-1 sunk cost) fullständigt rivet — ingen concorde-fallacy, ingen inert kod kvar (CTO-2 §7 + YAGNI följt).
- Testpyramid korrekt omklassad: relationell join-logik flyttad till Npgsql-integrationsnivå (ärlig signal, Beck 2002) utan att röra `TestAppDbContextFactory` (C:s 42-fil-risk undviken).
- Zod additivt + `nullable().optional()` ger deploy-skew-resiliens (3a backend före 3b frontend) — single-source-integritet (ADR 0020) bevarad.
- DTO-fält additiva **sist** — strikt bakåtkompatibelt.

---

## Slutsats

**GO.** 0 Blocker, 0 Major. 2 Minor är dokumenterade medvetna avvikelser (architect/CTO-ägda), funktionellt korrekta och bevisade gröna — ej mergeblockerande, ej heller in-block-åtgärdskrav. Vertikalen är spec-trogen mot hela trailen (architect 15-steg + (D)-fix + CTO-1→CTO-2 + testflytt), Clean Arch-ren, atomiskt komplett för J3-commit. Klar för atomisk J3-push efter Klas GO.
