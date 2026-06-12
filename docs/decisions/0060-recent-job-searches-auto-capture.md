# ADR 0060 — RecentJobSearches: auto-capture-aggregat med post-handler-pipeline-behavior

**Datum:** 2026-05-20
**Status:** Accepted
**Kontext:** JobbPilot F6 Prompt 4a (HANDOVER-v3.md §0, §7.5, §9 — Platsbanken-paritet "Senaste sökningar"-affordance). Klas-direktiv 2026-05-20: stegvis leverans (P4a först, P4b SavedJobAds separat backend-prompt). RecentJobSearches är skild domän från SavedSearch (ADR 0039 manuell-spara, frontend ej längre konsumerar).
**Beslutsfattare:** Klas Olsson (produktägare; explicit Accepted-direktiv i F6 P4a-startprompten 2026-05-20)
**Relaterad:** [ADR 0009](./0009-no-repository-pattern.md) (no Repository), [ADR 0011](./0011-strongly-typed-ids.md) (strongly-typed IDs), [ADR 0020](./0020-frontend-dto-validation-with-zod.md) (Zod DTO-mirror), [ADR 0022](./0022-application-audit-with-pipeline-behavior.md) (Audit pipeline), [ADR 0031](./0031-cross-tenant-failed-access-detection.md) (failed-access-detection), [ADR 0032](./0032-job-ingestion-platsbanken.md) §5 (UNIQUE-violation upsert-pattern), [ADR 0039](./0039-savedsearch-aggregate-and-query-run-semantics.md) (manuell SavedSearch, **frontend ej längre konsumerar** — amend nedan), [ADR 0042](./0042-search-surface-information-architecture.md) Beslut B (SearchCriteria multi-shape), [ADR 0043](./0043-taxonomy-acl-for-search-surface.md) (taxonomi-ACL för label-resolve), [ADR 0045](./0045-performance-budgets-and-fitness-functions.md) (fitness functions för perf-regression), [ADR 0049](./0049-pii-field-encryption-envelope.md) Mekanik-not 3/4 (markör-interface-pattern), [ADR 0055](./0055-platsbanken-popover-replaces-disclosure-filter.md) (Platsbanken-popover — amend nedan: Senaste-hero-chip BE-stöd levererat).

> **Livscykel-/proveniens-not:** Skriven 2026-05-20 av Claude Code (adr-keeper-disciplin) på explicit Klas-direktiv i F6 P4a-startprompten — medveten override av CLAUDE.md §9.4 webb-Claude-verbatim-konventionen (memory `feedback_klas_can_override_adr_verbatim_source`). Besluts-substansen är grundad i senior-cto-advisor-dom 2026-05-20 (Fråga 1–3) + dotnet-architect-design 2026-05-20 (Q1–Q4 verifierings-rond). Inga nya beslut konstruerade. Status **Accepted** per Klas explicit-direktiv.

---

## Kontext

JobbPilot v3 UI-refactor kräver Platsbanken-paritet på "Senaste sökningar"-affordance (HANDOVER-v3.md §0 veto, §9). SavedSearch-domänen (ADR 0039) är **manuell-spara** med run-semantik — inte vad användaren ser i Platsbankens hero. Klas-direktiv 2026-05-20: SavedSearch behålls dolt på backend-sidan men frontend slutar konsumera den; en ny **auto-fångad** sökhistorik per JobSeeker krävs för:

1. **`/sokningar`-route** — listvy över de senaste filterna användaren faktiskt har kört (cap=20 per JobSeeker).
2. **Hero-chip "Senaste"-dropdown** — snabbåtkomst med affordance "(N nya sedan senast)" via delta-räknare.
3. **DELETE-action** — användaren kan rensa enskilda rader (ingen retention-policy utöver evict-äldsta vid cap).

CTO-beslut F6 P4a 2026-05-20 stängde tre multi-approach-val (FilterHash-strategi, capture-mekanik, count-projektion) — denna ADR dokumenterar dem som domänkontrakt.

## Beslut

### Beslut 1 — Separat aggregate `RecentJobSearch` (Domain)

`RecentJobSearch` är **aggregate root** i `JobbPilot.Domain.RecentJobSearches` med `RecentJobSearchId` som `readonly record struct` per ADR 0011. SavedSearch (ADR 0039) **bevaras orört** — auto-capture är annan semantik (frekvens, evict, delta-räknare). DRY-vinst via konsumtion av `SearchCriteria` VO (ADR 0042 Beslut B) som parameter till `Capture()`: invariant-källan stannar i SearchCriteria (Evans 2003 kap. 5 — VO är single source of truth för sin invariant). Cap-konstanten `MaxPerSeeker = 20` deklareras i Domain (CLAUDE.md §5.1 — ingen magic number) men enforce:as mekaniskt i Capturer-implementationen.

### Beslut 2 — FilterHash = SHA-256 av canonical-JSON, Domain-beräknad (CTO Variant A)

Uniqueness-identitet `UNIQUE(job_seeker_id, filter_hash)` bär INSERT vs. Bump-distinktionen. `FilterHashCalculator.Compute(criteria)` är Domain-static helper som canonical-serialiserar `{q, ssyk:sorted, region:sorted, sortBy}` (listorna redan sorted+distinct ordinal från SearchCriteria.NormalizeList) och hashar via SHA-256. Lagras som `varchar(64)`.

**Avvisade alternativ:**
- **Variant B (plaintext canonical-string):** Större index, sämre cache locality, ingen praktisk vinst utöver läsbarhet (debug-info finns redan i Q/Ssyk/Region/SortBy-källkolumnerna).
- **Variant C (PostgreSQL generated column):** Bryter Clean Arch dependency rule (Martin 2017 kap. 22) — Domain skulle inte kunna räkna hash deterministiskt utan databas; Domain-tester skulle kräva DB-fixture.

### Beslut 3 — Auto-capture via post-handler `IPipelineBehavior` med opt-in markör (CTO Variant A)

`RecentJobSearchCaptureBehavior<TMessage, TResponse>` placeras i pipeline efter `UnitOfWorkBehavior`, före `AuditBehavior` (kanonisk ordning kodifierad i `MediatorPipelineBehaviors.InOrder` + arch-test). Single generic constraint `where TMessage : IMessage` + runtime-markör-check `if (message is not ICapturesRecentSearch capt)` (paritet `FieldEncryptionKeyPrefetchBehavior`-stilen, ADR 0049 Mekanik-not 3/4). `ListJobAdsQuery` implementerar `ICapturesRecentSearch` (record-properties matchar interface-shape automatiskt). Response-side markör `IRecentSearchCaptureResponse { int TotalCount }` på `PagedResult<T>` ger typad TotalCount-extraktion utan att behavior känner till konkret DTO-typ (open/closed, Martin 2017 kap. 8).

Capture är **best-effort**: behavior wrappar Capturer-anropet i `try/catch + log warn` — capture-fel bryter aldrig queryn (hård invariant; fall här skulle ge 500 på söksidan, oacceptabelt). No-op vid (1) ingen markör, (2) ingen response-markör, (3) anonym användare, (4) SearchCriteria-validering failar (default-browse capture:as ej).

**Avvisade alternativ:**
- **Variant B (explicit `CaptureRecentSearchCommand` från FE):** Trust-flytt till klient, dubbla round-trips, race conditions mellan list-render och capture, FE måste persistera filter-shape.
- **Variant C (inline i ListJobAdsQueryHandler):** Bryter SRP — handler skulle göra två saker. Capture-fel måste hanteras i hot path. Handler-tester kompliceras.
- **Variant D (domain-event från ListJobAdsQuery):** Bryter CQRS-konventionen — queries raisar inte events i denna kodbas.

### Beslut 4 — `CurrentCount`-projektion: re-query per row + cap=20 (CTO Variant A)

`ListRecentSearchesQueryHandler` iterar items sekventiellt (`foreach await` — `DbContext` är inte thread-safe, Microsoft Learn) och gör en COUNT-query per row via `JobAdSearch.ApplyCriteria(db.JobAds.AsNoTracking(), ssyk, region, q).CountAsync(ct)`. **Avsiktlig N+1** capped vid `RecentJobSearch.MaxPerSeeker = 20`. NewCount = `Math.Max(0, CurrentCount - LastSeenCount)`.

YAGNI-domen (Beck) hänger på cap-invarianten: Hangfire-cache eller batch-SQL är defensive optimization för icke-bevisat problem; ADR 0045 fitness function observerar p95 på endpointen och triggar evolution om budget bryts. Arch-test fångar EF-translator-N+1 från lazy navigation — Variant A:s **explicit** loop är inte vad arch-testet detekterar.

**Avvisade alternativ:**
- **Variant B (Hangfire-cache):** Stale-data ger inkonsistens i `NewCount`-aritmetik (UX-bugg). Hangfire-dependency för icke-hot-path-query — premature defensive optimization.
- **Variant C (single batch-SQL):** Heterogena filter per rad → UNION ALL med N subqueries inline = i praktiken samma N count-operations, mindre läsbar, EF Core-translator kämpar med dynamisk N-UNION.

### Beslut 5 — Cap-enforcement via Capturer-implementationen (Infrastructure)

`IRecentJobSearchCapturer.CaptureAsync` (Application-port; Infrastructure-impl `RecentJobSearchCapturer`) gör (a) JobSeeker-lookup, (b) FilterHash-beräkning, (c) try-bump-existing, (d) vid INSERT: räkna sibling-rader; om ≥ `MaxPerSeeker` → evict äldsta `LastViewedAt` i samma scope, (e) Capture+Add → SaveChangesAsync, (f) vid `DbUpdateException` med UNIQUE-violation (`IDbExceptionInspector.IsUniqueConstraintViolation`, ADR 0032 §5 race-pattern): detach + reload + Bump. Egen UoW-scope (capture är side-effect; ej del av huvud-querys UoW).

Race-fönster där två parallella captures båda evictar gör cap temporärt cap-1 — acceptabelt (cap är affärsregel, inte säkerhets-invariant; nästa capture återställer).

### Beslut 6 — DELETE = hard-delete, ingen soft-delete

Auto-fångade rader saknar audit-trail-värdighet (vs. SavedSearch som är användar-skapad). `DeleteRecentSearchCommandHandler` är `IAuditableCommand` (ADR 0022 — `RecentJobSearch.Deleted` event-name) men aggregaten har **inget `DeletedAt`-fält**. Cross-tenant-skydd per ADR 0031 (skilj okänt id från forbidden i log, exponera samma NotFound-response).

### Beslut 7 — Persistence-yta

PostgreSQL-tabell `recent_job_searches`:

- `id uuid PK`, `job_seeker_id uuid NOT NULL`, `filter_hash varchar(64) NOT NULL`, `q varchar(100) NULL`
- `ssyk_list text[] NOT NULL`, `region_list text[] NOT NULL` (Npgsql 10 auto-mappning via shadow-backing-fields `_ssyk`/`_region`, paritet `ResumeConfiguration._topSkills`)
- `sort_by integer NOT NULL`, `last_viewed_at timestamptz NOT NULL`, `last_seen_count integer NOT NULL DEFAULT 0`, `created_at timestamptz NOT NULL`
- `UNIQUE(job_seeker_id, filter_hash)` — `ux_recent_job_searches_seeker_hash` (hard-invariant)
- `INDEX(job_seeker_id, last_viewed_at DESC)` — `ix_recent_job_searches_seeker_viewed_at` (list-query order)

Migration: `20260520190720_AddRecentJobSearches` (additive, ingen påverkan på andra modeller).

### Beslut 8 — API-yta

`/api/v1/me/recent-searches` (auth-gated):
- `GET /` → `IReadOnlyList<RecentJobSearchDto>` (label-berikad via `ITaxonomyReadModel`).
- `DELETE /{id:guid}` → 204 NoContent / 404 NotFound.

Ingen `/run`-yta (semantik = redirect till `/jobb` med filter — frontend-koncern).

### Beslut 9 — Frontend DTO-mirror (Zod)

`web/jobbpilot-web/src/lib/dto/recent-searches.ts` speglar backend `RecentJobSearchDto` (ADR 0020). `sortBy` serialiseras numeriskt (samma konvention som SavedSearchDto, paritet SAVED_SEARCH_SORT_ORDER). `ssykLabels/regionLabels` default till `[]` (ADR 0043 graceful degradation). `currentCount`/`newCount` validerade som nonneg int. API-helper `getRecentSearches()`/`deleteRecentSearch(id)` följer `ApiResult<T>`-pattern (ADR 0030).

## Konsekvenser

### Positiva

- **Domänkohesion bevarad** — SavedSearch (manuell) och RecentJobSearches (auto) är separata bounded aggregates med distinkta semantiker; ingen kopplad mutation.
- **DRY på filter-validering** — invariant-källan stannar i SearchCriteria-VO; RecentJobSearch tar redan-validerade kriterier.
- **Markör-pattern återanvänt** — `ICapturesRecentSearch` följer `IRequiresFieldEncryptionKey`-konventionen (ADR 0049); låg kognitiv börda för framtida nya capture-källor.
- **N+1 disciplinerad** — cap=20 + ADR 0045 fitness function ger evolution-path utan premature optimization.
- **Race-säker via etablerat pattern** — ON CONFLICT-mönster återanvänt från UpsertExternalJobAd (ADR 0032 §5).

### Negativa / accepterade trade-offs

- **64-tecken hash icke-läsbar i raw DB-dump** — acceptabelt; källkolumnerna ger debug-info.
- **Extra pipeline-behavior** (7→8 i kedjan) — no-op-overhead för commands + queries utan markör.
- **Cap-overflow-race fönster** — kortvarig cap-1-temporal; affärsregel ej säkerhets-invariant.
- **Inga skrivskydd för system-genererade events** — ListJobAds från Worker/bakgrundsjobb capture:as inte (anonym `ICurrentUser.UserId == null` triggar no-op); ingen hittills känd risk.

### Öppna frågor

- **Frontend-rendering** (F6 P4a frontend, återupptas i Klas's chat-session efter merge): `/sokningor`-listrefactor + hero-dropdown + DELETE-affordance + ADR 0055 amend-aktivering av Senaste-chip.
- **SavedJobAds (per-annons-bookmark, F6 P4b)** — separat backend-prompt; ingen domän-koppling till denna ADR.
- **Backend-cleanup av SavedSearch-domänen** — backlog (framtida fas om städ blir prioriterat); inga konsumenter på frontend efter F6 P4a, backend-yta orörd tills uppfattat värde.

## Mekanik-noter

### Mekanik-not 1 — Pipeline-ordning

`MediatorPipelineBehaviors.InOrder` (Application/Common/MediatorPipelineBehaviors.cs):
```
Logging → Validation → Authorization → AdminAuthorization → FieldEncryptionKeyPrefetch → UnitOfWork → RecentJobSearchCapture → Audit
```

Arch-test `WorkerLayerTests.MediatorPipeline_should_have_expected_behaviors_in_order` är auktoritativ — ändringar kräver ADR-uppdatering.

### Mekanik-not 2 — Capture skipping för default-browse

Behavior anropar `SearchCriteria.Create(query.Ssyk ?? [], query.Region ?? [], query.Q, query.SortBy)`. Vid `IsFailure` (ex: empty-invariant — alla fält tomma) → no-op return. Default-browse-queries (`GET /api/v1/job-ads` utan filter) capture:as alltså aldrig — bevisas av architectural test/integration test för auto-capture-flow.

### Mekanik-not 3 — JobAdSearch.ApplyCriteria är Application-static

`JobbPilot.Application.JobAds.Queries.JobAdSearch.ApplyCriteria` är Application-static (ADR 0039 Beslut 1) — `ListRecentSearchesQueryHandler` kan kalla direkt eftersom båda är Application. Domain har ingen IQueryable-yta.

### Mekanik-not 4 — Frontend ej längre konsumerar SavedSearch

ADR 0039 amend nedan dokumenterar detta. Backend `GET /api/v1/saved-searches` förblir live (ingen breaking change) tills explicit backend-cleanup-beslut tas.

### Mekanik-not 5 — GDPR Art. 17-cascade (HardDeleteAccountsJob)

RecentJobSearches saknar databas-FK till JobSeekers (ADR 0011 strongly-typed soft-reference-mönster, paritet med SavedSearches). `AccountHardDeleter.HardDeleteAccountAsync` (ADR 0024 D6) uppdaterades därför 2026-05-20 till att explicit `RemoveRange` båda `SavedSearches` och `RecentJobSearches` per JobSeeker innan `db.JobSeekers.Remove(jobSeeker)`. Detta var **också** en in-block-fix av en pre-existing SavedSearches-cascade-lucka (CLAUDE.md §9.6 — samma fas, samma blast-radius). [ADR 0024 amend 2026-05-20](./0024-audit-retention-and-art17-cascade.md#amendment-2026-05-20--cascade-ut%C3%B6kad-till-savedsearches--recentjobsearches-per-adr-0060) dokumenterar cascade-tabellen.

Integration-test `HardDeleteAccountsJobIntegrationTests.RunAsync_CascadesHardDelete_ToSavedSearchesAndRecentJobSearches` verifierar end-to-end (seed SavedSearch + RecentJobSearch för soft-deletad seeker → kör HardDelete-jobb → båda raderna verifieras borta).

Crypto-erasure (TD-13/ADR 0049) berör **inte** `recent_job_searches` — kolumnerna är klartext (söktermer som q är PII men ej känslig nog för envelope-encryption per ADR 0049 Beslut 3 scope-avgränsning — taxonomi-koder + fritext-söktermer; ej cover_letter/follow_up-content-nivå). Q-strängar lever i klartext i Postgres-backups tills retention-fönstret rullar av (PostgreSQL pg_dump-rotation per `ADR 0050`-skala) — accepterat backup-residue per security-auditor-bedömning 2026-05-20 (volym/risk-balans + GDPR Art. 32 "lämpliga tekniska åtgärder", söktermer är lägre känslighet än Resume-content/CV-PII).

### Mekanik-not 6 — GDPR Art. 13 information-skyldighet

JobbPilot börjar samla in en ny PII-kategori (sökhistorik per identifierad användare) vid F6 P4a-leverans. GDPR Art. 13(1)(c) + 13(2)(a) kräver att användaren informeras om ändamål, rättslig grund, lagringstid. Rättslig grund: **berättigat intresse (Art. 6(1)(f))** — UX-förbättring för återbesök, balanstest enligt EDPB-rekommendationer (proportional användaransvar, opt-out via individuell DELETE per rad + via konto-radering).

Disclosure-mekanik (deferral till F6 P4a frontend / Klas-uppgift):

1. **Privacy-policy-uppdatering** — sektion om sökhistorik (ändamål, retention, opt-out).
2. **Inline-disclosure i `/sokningar`** — kort textförklaring i `RecentSearchList`-tomtillstånd / hjälptext.
3. **Opt-out** — individuell DELETE per rad finns redan (denna ADR Beslut 8); full opt-out via konto-radering. Global "stäng av RecentJobSearches"-toggle i `/installningar` är **deferred** till framtida fas om Klas ser att det krävs (security-auditor: "TD-Fas-3 om Klas tar berättigat intresse + radera-per-rad som tillräckligt för MVP").

**Klas-uppgift:** Privacy-policy-uppdatering i samma frontend-batch som F6 P4a frontend levereras. Markeras som blockerande för F6 P4a frontend-merge, **inte** för denna BE-merge (data-insamling startar vid frontend-merge eftersom CC inte kan rendera nya rader utan FE-konsumtion av endpoint).

---

# Amendment 2026-05-20 — ADR 0039 (manuell SavedSearch behålls dolt)

Per ADR 0060 Beslut 1: frontend slutar konsumera SavedSearch-domänen i samma commit-batch som F6 P4a frontend levereras. Backend-domän + endpoints (`/api/v1/saved-searches`, CRUD + run) förblir live oförändrade — ingen breaking change mot framtida konsumenter. Cleanup framtida fas om backend-städ blir prioriterat. ADR 0039 övriga beslut (Domain-aggregat, jsonb-criteria, run-semantik, last_run_at Fas 5) **består**.

# Amendment 2026-05-20 — ADR 0055 (Senaste-chip BE-stöd levererat)

Per ADR 0060 Beslut 8 (RecentJobSearches API-yta) är backend-stödet för "Senaste sökningar"-hero-chip nu levererat. ADR 0055:s tidigare deferral av Senaste-chip (i amendment 2026-05-19 — "deferrad tills BE-stöd") flyttas från deferral-listan. Frontend-rendering av Senaste-chip kan aktiveras i F6 P4a frontend-batch.

**"Sparade"-chip kvar deferred** tills F6 P4b SavedJobAds-domänen är levererad (per-annons-bookmark, separat backend-prompt).

---

# Amendment 2026-06-12 — Beslut 3 preciseras: capture-trigger blir commit-intent-gatad (Platsbanken sök-paritet Fas E2j)

**Status:** Accepted (Klas-GO på amendment-substansen 2026-06-12 via AskUserQuestion — capture-trigger Variant B).
**Beslutsunderlag:** dotnet-architect-dom (`docs/reviews/2026-06-12-sok-paritet-e2j-architect.md`) + senior-cto-advisor-dom (`docs/reviews/2026-06-12-sok-paritet-e2j-cto.md`, decision-maker). Empiriskt fynd bekräftat i dev-DB 2026-06-12: `recent_job_searches` full vid cap=20 för en seeker, fylld av live-mellanstegsspam.

> **Livscykel-/proveniens-not:** Författad av Claude Code grundad i E2j architect- + CTO-domarna (samma proveniens-mönster som ADR 0060:s ursprungliga not + E2b/D2-notaten i ADR 0067). Klas gav GO på amendment-substansen; mekaniken är CTO-bestämd. Inga nya arkitekturbeslut konstruerade utöver dom-substansen.

## (a) Premissen "en query = en intention" revs av E2i:s live-sök

Beslut 3 (2026-05-20) skrevs i en värld där en sökning motsvarade ett `router.push` per användarintention. ADR 0067 Fas E2i (2026-06-11, spegel-sökfältet) införde **live-sök**: hero-fältets text committas till URL:en via `router.replace` per ord medan användaren skriver. Eftersom `/jobb`:s resultat-RSC (`JobbResults`) kör `ListJobAdsQuery` vid varje URL-ändring, triggade varje mellansteg en ny auto-capture. Beslut 3:s implicita premiss — "varje lyckad `ICapturesRecentSearch`-query ≈ en avsiktlig sökning" — höll inte längre. Cap=20 fylldes av mellanstegsvarianter ("system", "systemut", "systemutvecklare", "systemutvecklare göt"…) som evictade äkta committade sökningar, och funktionen levererade motsatsen till sitt syfte (snabbåtkomst till sökningar man faktiskt kört).

## (b) Trigger preciseras: `ICapturesRecentSearch.Commit` (commit-intent-gate)

Markör-interfacet får en `bool Commit { get; }`-property. `RecentJobSearchCaptureBehavior` no-op:ar när `Commit == false`. FE sätter `?commit=1` ENDAST vid avsiktlig commit (Enter/Sök/förslags-val/toolbar-handlingar); live-förhandsvisning per ord (`router.replace`) utelämnar flaggan. Backend kan inte skilja `router.replace` från `router.push` (båda är `GET /api/v1/job-ads` med identiska parametrar) — commit-intentet måste bäras explicit (att gissa serverside vore Programming by Coincidence, Hunt/Thomas 1999 kap. 6).

**Detta är INTE Beslut 3:s avvisade Variant B** ("explicit `CaptureRecentSearchCommand` från FE"). De fyra avvisnings-grunderna prövades verbatim mot commit-flaggan och träffar inte:

| Beslut 3-invändning mot avvisade Variant B | Gäller commit-flaggan? |
|---|---|
| "Trust-flytt till klient" | Nej materiellt. Enda trust som flyttas är NÄR användarens EGEN historik fångas. JobSeeker-lookup sker fortfarande server-side via `ICurrentUser.UserId` (Capturer-invarianten orörd). Klienten kan inte capture:a för annan seeker, injicera annan data eller kringgå auth. Worst case benignt: över-/under-capture av egen bekvämlighets-historik. |
| "Dubbla round-trips" | Eliminerad. Flaggan rider på list-queryn som ändå körs. Noll extra HTTP. |
| "Race mellan list-render och capture" | Eliminerad. Samma query, samma pipeline, samma post-UnitOfWork-ordning (Mekanik-not 1). |
| "FE måste persistera filter-shape" | Falskt. Filter-shapen ligger redan i URL:en (ADR 0042/E2g). FE sätter en boolean på en query den redan bygger. |

Commit-flaggan ligger arkitektoniskt **närmare Beslut 3:s accepterade Variant A** (post-handler-behavior, markör-driven) än dess avvisade B: behaviorn, markör-patternet, best-effort-semantiken och UoW-ordningen behålls — ett commit-predikat adderas till markör-kontraktet (open/closed, Martin 2017 kap. 8). `SearchCriteria`-VO:t och Capturer-invarianten är orörda; `Commit` ingår INTE i filter-identiteten (FilterHash, Beslut 2) — det är en transient signal-param, inte en del av sökningens identitet.

## (c) Mekanik-not 2 (default-browse-guard) får sällskap av en commit-guard

Behaviorns no-op-kedja blir: ingen markör → no-op · **`Commit == false` → no-op (NY)** · anonym användare → no-op · alla fyra dimensioner tomma (default-browse) → no-op · `SearchCriteria.Create` failar → no-op. Commit-guarden är **additiv, inte ersättande**: en commit på tom sökning ("Sök" utan filter) capture:as fortfarande aldrig (browse-guarden består — Mekanik-not 2 oförändrad i sak).

## (d) GDPR Art. 5(1)(c) — data-minimering stärkt

Commit-gaten är inte bara en UX-fix utan en materiell data-minimerings-förstärkning (Mekanik-not 5/6-grannskapet). Vi persisterar nu endast de söktermer (PII) användaren explicit committade — den minimala mängd ändamålet (snabbåtkomst till avsiktliga sökningar) kräver — i stället för varje mellanstegs-keystroke-variant. Det stärker också Art. 13-disclosurens sanningshalt: "vi sparar sökningar du kör" blir bokstavligt sant. Insamlings-minimering (inte retention-städning) är rätt GDPR-mekanism (Art. 5(1)(c) reglerar insamling). security-auditor verifierade PII-insamlingsväg-ändringen (`docs/reviews/2026-06-12-sok-paritet-e2j-security-audit.md`).

**FE-mekanik (utanför denna ADR:s domän, dokumenterad i ADR 0067 impl-notat E2j):** `commit` hålls strikt utanför `JobbUrlState`/`sameUrlState`/`buildJobbHref` (signal, inte tillstånd — Martin 2017 kap. 7); commit-punkterna adderar `?commit=1` som transient suffix på `router.push`/`replace`; live-`router.replace` utelämnar; no-JS-formet bär statiskt hidden `commit=1` (no-JS-submit ÄR per definition en commit); FE strippar `?commit=1` efter mount så en delad/bokmärkt länk inte re-capture:ar.
