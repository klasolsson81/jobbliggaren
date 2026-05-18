# ADR 0039 — SavedSearch-aggregat: SearchCriteria-VO, query-baserad run-semantik och Fas 2/5-gränsdragning

**Datum:** 2026-05-16
**Status:** Accepted 2026-05-16 (Klas-GO 2026-05-16; senior-cto-advisor-beslut 2026-05-16, dotnet-architect-design samma datum) — *Beslut 3 delvis superseded 2026-05-16 av ADR 0042 (Ssyk/Region single→multi; SortBy-i-VO hålls)*
**Beslutsfattare:** Klas Olsson
**Relaterad:** ADR 0005 (go-to-market — JobAd/sök auth-gated, JobSeeker-scoped), ADR 0009 (ingen Repository — direkt `IAppDbContext`), ADR 0011 (strongly-typed IDs), ADR 0032 (JobTech-integration — `ListJobAds` ssyk/region/q-yta som `SearchCriteria` speglar), ADR 0049 (Accepted — TD-13 PII-fält-kryptering: Beslut 3 bevarar denna ADR:s `JobAdSearch`-taxonomi-SPOT orörd genom medveten `raw_payload`-exklusion), CLAUDE.md §2.3 (CQRS), §9.6 (in-block vs TD/fas-regeln), BUILD.md §5.1/§9.x/§16/§18 (Fas 2-milstolpe)

---

## Kontext

F2 Saved Searches är den sista oimplementerade Fas 2-leverabeln (BUILD.md §18 — milstolpe "söka jobb på Platsbanken + spara sökningar"). BUILD.md §5.1 anger `SavedSearch` som aggregate root som äger en `SearchCriteria`-VO och refererar `JobSeekerId`; §16 anger schemat `saved_searches` (criteria jsonb, notification_enabled bool, last_run_at timestamptz null); §9.x anger sex endpoints inklusive `POST /{id}/run`.

Fyra designval är icke-uppenbara och bestående:

1. **SearchCriteria-VO:ts form** — vilka av `ListJobAdsQuery`-fälten (Ssyk/Region/Q/SortBy/Page/PageSize) tillhör en *sparad sökning* vs runtime-pagination.
2. **Run-semantik** — `POST /{id}/run` returnerar en jobblista (read-model) men `last_run_at` är konceptuellt en skriv-sidoeffekt. CLAUDE.md §2.3: "Commands returnerar `Result<T>` där `T` är det som ändrats; Queries returnerar DTOs direkt." `UnitOfWorkBehavior` SaveChangar efter command-handlers, **inte** queries.
3. **`last_run_at`-fas-tillhörighet** — kolumnen finns i §16-schemat men dess domän-betydelse ("senast körd" för notification-cadence) uppstår först i Fas 5.
4. **`notification_enabled`** — flaggan finns i §16-schemat men dispatch (e-post vid nya träffar) är Fas 5-infrastruktur.

Sök-logiken som `run` ska återanvända (`ApplyFilters`/`ApplySort` i `ListJobAdsQueryHandler`) är grön och integrationstestad, med icke-trivial nyans (Postgres generated-column shadow-properties via `EF.Property`, `LOWER`/`EF.Functions.Like`, CA1304/CA1311-suppress, NULL-ExpiresAt-sortering). Duplicering av den logiken skulle garantera drift.

## Beslut

**Beslut 1 — Run-query-återanvändning (DRY/SoC).** `ApplyFilters`/`ApplySort` lyfts från `private static` i `ListJobAdsQueryHandler` till en delad intern modul i `JobbPilot.Application.JobAds.Queries`. Signaturen generaliseras `ListJobAdsQuery` → primitiva `(string? ssyk, string? region, string? q)` + `JobAdSortBy`. Befintlig handler blir en tunn adapter. `RunSavedSearchQueryHandler` anropar samma modul. Refaktorn av grön kod + dess tester är **in-scope** (CLAUDE.md §9.6 — fas-tillhörighet, ej tidströskel; behaviour-preserving Extract Function, befintliga tester är regressions-säkring). Avvisat: nested `IMediator`-dispatch (Alt A) och duplicering (Alt C).

**Beslut 2 — Run är en Query, inte en Command. `last_run_at`-skrivlogik skjuts till Fas 5.** `RunSavedSearchQuery` returnerar `PagedResult<JobAdDto>` utan skriv-sidoeffekt. `last_run_at`-kolumnen skapas i Fas 2-migrationen (schema-stabil per BUILD §16) men sätts inte i Fas 2. "Senast körd" får domän-betydelse först när Fas 5:s notification-jobb kör sökningen schemalagt. Avvisat: command som returnerar read-model (Alt 1 — bryter §2.3) och fire-and-forget MarkRun (Alt 2 — race, klient-ansvar för domän-bokföring).

**Beslut 3 — `SortBy` ingår i `SearchCriteria`-VO:t.** En sparad sökning är en reproducerbar fråga; sortering determinerar första-sida-resultatet under paginering och är därför del av användarens avsikt och VO:ts strukturella likhet (Evans 2003 kap. 5; Vernon 2013 kap. 6). `SearchCriteria(string? Ssyk, string? Region, string? Q, JobAdSortBy SortBy)`. `Page`/`PageSize` exkluderas (runtime-pagination, ej del av sökningens identitet). VO:t har en `Create`-factory → `Result<SearchCriteria>` som trimmar strängar (tom/whitespace → null) och kräver minst ett av Ssyk/Region/Q non-null (`SortBy` ensamt är inget filter).

> **Beslut 3 — DELVIS SUPERSEDED 2026-05-16 av ADR 0042.**
> `Ssyk`/`Region` ändras `string?` → `IReadOnlyList<string>` (multi-värde — genuint Fas 2-produktbehov: OR-bevakning över yrken/regioner). ADR 0042 Beslut B låser fyra nya invarianter (sorterad+distinct-normalisering för record-equality, maxantal-cap, generaliserad tom-invariant, jsonb-bakåtkompat). **`SortBy`-i-VO:t-delen av Beslut 3 hålls oförändrad** — kärnresonemanget (sortering är del av användarens avsikt och VO-likhet) står kvar; endast Ssyk/Region single→multi superseras. ADR-immutabilitet: ovanstående brödtext är medvetet orörd; detta är ett additivt supersession-notat (Nygard 2011, samma mönster som ADR 0026→0027 / 0037→0038). Se [`0042-search-surface-information-architecture.md`](./0042-search-surface-information-architecture.md).
>
> **Korsref-notat 2026-05-17 (additivt — `SearchCriteria`-VO/jsonb-dedupe ORÖRT):** ADR 0043 (Taxonomi-ACL för sök-ytan, Proposed) ändrar **endast** sök-ytans inmatnings-/presentationsyta (svenska namn-väljare istället för concept-id-fritext). `SearchCriteria`-VO-formen (`IReadOnlyList<string>` concept-id), jsonb-converter, comparer och dedupe-invarianter (ADR 0042 Beslut B.1) är **oförändrade** — dedupe vilar på concept-id-sekvenslikhet, namn ingår aldrig i VO:t. Ingen ny migration på `saved_searches`. Redan-sparade sökningar renderas via ACL-reverse-lookup; okänt id → `"Okänd kod (<id>)"`-fallback (graceful degradation, ingen invariant-risk). Se [`0043-taxonomy-acl-for-search-surface.md`](./0043-taxonomy-acl-for-search-surface.md).

**Beslut 4 — `notification_enabled` lagras, ingen dispatch.** `bool NotificationEnabled` default `false` (opt-in, speglar `Preferences.WeeklySummary = false`), satt via `SetNotification(bool, IDateTimeProvider)`. Ingen Hangfire, inget dispatch-event, ingen utskickskod i Fas 2. All notification-dispatch är Fas 5.

**Domänform (dotnet-architect, in-scope — princip-styrt):** `SavedSearch : AggregateRoot<SavedSearchId>` med `JobSeekerId` (strongly-typed ref), `Name` (required/trim/max-längd), `Criteria`, `NotificationEnabled`, `LastRunAt?`, `CreatedAt/UpdatedAt/DeletedAt`. Metoder: `Create`, `Rename`, `UpdateCriteria`, `SetNotification`, `SoftDelete` — speglar `Application.cs`. Events: `SavedSearchCreated/Renamed/Deleted`. **Inget Run-event** (Fas 2 YAGNI). EF: `OwnsOne(s => s.Criteria, c => c.ToJson())` (speglar `Preferences`), global query filter `DeletedAt == null`, tabell `saved_searches`. Handler-scoping: `ICurrentUser.UserId` → `db.JobSeekers`-uppslag → `.Where(s => s.JobSeekerId == jobSeekerId)` (speglar `GetApplicationsQueryHandler`; **ingen** `ICurrentJobSeeker`-port — det är TD-59 Fas 6).

## Konsekvenser

**Positiva:**
- Sök-logiken förblir SPOT (en kanonisk källa) — `run` och `list` kan aldrig divergera.
- CQRS-invarianten (§2.3) hålls intakt; ingen konstgjord command som returnerar read-model.
- `SearchCriteria`-VO:t modellerar användarens fulla avsikt → korrekt reproducerbarhet och VO-likhet.
- `criteria` jsonb + `last_run_at`/`notification_enabled`-kolumner är schema-kompletta i Fas 2 → ingen migration-skuld när Fas 5 aktiverar logiken.

**Negativa + mitigering:**
- Grön `ListJobAdsQueryHandler` + tester refaktoreras. *Mitigering:* behaviour-preserving extraktion; befintliga `ListJobAds`-tester körs oförändrade som regressions-grind.
- `last_run_at` exponeras i API men är alltid `null` i Fas 2 (kan förvirra konsument). *Mitigering:* dokumenteras i ADR + endpoint-kommentar; frontend visar inte fältet i Fas 2.
- `notification_enabled` kan sättas `true` utan effekt i Fas 2. *Mitigering:* medveten scope-gräns dokumenterad här; UI-copy lovar inte notiser i Fas 2.

## Alternativ som övervägdes

### Beslut 1: Alt A — nested `IMediator`-dispatch (avvisat)
`RunSavedSearchQueryHandler` skickar `ListJobAdsQuery` via mediator. **Emot:** kör hela pipeline-stacken (ADR 0008) en andra gång inuti en körande pipeline — dubbelvaliderar/auktoriserar, döljer flöde, kopplar query-kontrakt. En query-handler är ingen dispatcher.

### Beslut 1: Alt C — duplicera filter/sort-logiken (avvisat)
**Emot:** generated-column/LOWER/CA-suppress-nyansen är skör; två kopior driftar garanterat (Hunt/Thomas DRY — knowledge piece, ej kod-likhet).

### Beslut 2: Alt 1 — `RunSavedSearchCommand` returnerar `PagedResult` (avvisat)
**Emot:** bryter CLAUDE.md §2.3 explicit ("Commands returnerar `Result<T>` där `T` är det som ändrats"); en projektion är inte "det som ändrats". Att böja en stark invariant för en kosmetisk timestamp.

### Beslut 2: Alt 2 — Query + fire-and-forget MarkRun-command (avvisat)
**Emot:** domän-bokföring blir klient-ansvar, race mellan run och mark, extra round-trip; bryter Tell-Don't-Ask (Evans — aggregat skyddar egna tillståndsövergångar).

### Beslut 3: exkludera `SortBy` ur VO:t (avvisat)
**Emot:** sortering determinerar paginerat resultat → en sparad sökning utan sortering är en ofullständig spegling av användarens avsikt; VO-likhet skulle inte spegla domän-likhet.

## Implementationsstatus

Accepted 2026-05-16 efter Klas-GO på samtliga fyra beslut + domänform. Implementation pågår i samma session (F2 Saved Searches end-to-end). Beslut 2:s Fas 2/5-gräns och Beslut 4:s scope-gräns är Klas-godkända.

## Krav på Klas-GO innan implementation

| Punkt | Kräver Klas-GO? |
|---|---|
| Beslut 1 (Alt B, refaktor in-scope) | Nej — entydigt mot DRY/SoC/CQRS |
| Beslut 3 (SortBy i VO) | Nej — entydigt mot Evans/Vernon |
| Domänform (architect) | Nej — princip-styrt, speglar befintliga mönster |
| Beslut 2 (`last_run_at` → Fas 5) | **JA** — Fas 2/5-scope-gräns |
| Beslut 4 (notification lagra-ej-dispatch) | **JA** — scope-gräns |
| ADR 0039 Accepted-flip | **JA** — ADR-accept = Klas-STOPP |

---

*Referenser: Robert C. Martin, Clean Architecture (2017) kap. 7, 22; Eric Evans, DDD (2003) kap. 5, 14; Vaughn Vernon, IDDD (2013) kap. 6; Fowler, Refactoring 2nd ed (2018) Extract Function; Hunt/Thomas, Pragmatic Programmer (1999) DRY; Nygard, Documenting Architecture Decisions (2011); Microsoft Learn — CQRS pattern. CLAUDE.md §2.3, §9.2, §9.6; ADR 0008.*
