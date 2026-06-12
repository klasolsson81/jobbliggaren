---
session: Fas B2 query-wiring — Klass 2 (anställningsform + omfattning) sökbar
datum: 2026-06-12
slug: fas-b2-query-wiring
status: levererad — PR öppen, automerge-label, alla gates gröna
commits:
  - feat(jobads): Fas B2 query-wiring — anställningsform + omfattning sökbara (Klass 2)
---

# Fas B2 query-wiring (2026-06-12)

## Mål (Klas-prompt)

Sista datakedjan före Fas E Filter-panel: gör anställningsform (`employment_type`)
+ omfattning (`worktime_extent`) — JobTech Klass 2 — sökbara via
`?employmentType=`/`?worktimeExtent=`. Data finns redan on-disk (STORED-kolumner,
~79 % av Active populerad — re-ingest empiriskt utförd, se discovery #58/#59).
Ingen job_ads-migration. test-writer FÖRST.

## Scope-beslut — Klas-override av CTO-rek

En scope-fråga som prompten inte adresserade entydigt: prompt-punkt 2 listade
`RecentJobSearch` + `FilterHashCalculator` i ripplet, MEN sa "ingen migration" +
listade inte db-migration-writer/CTO i förväntat sluttillstånd. `RecentJobSearch`
persisterar kriterier som flattenade `text[]`-kolumner (ej jsonb-VO) → att lägga
Klass 2 där kräver en `recent_job_searches`-migration.

- **senior-cto-advisor (decision-maker):** Variant B-i-PR (VO + SavedSearch-jsonb +
  filter + facet) + **genuin TD** för RecentJobSearch-rippeln. Motivering: CCP/SRP
  (sökbarhet vs. recent-identitet = olika change-reasons); migrationen utanför
  "ingen migration"-GO. Nyckelinsikt: capture-vägen bär inte Klass 2 förrän TD-PR
  → silent-dedup strukturellt obefintlig i mellantiden. CTO erbjöd override-not.
- **Klas (AskUserQuestion):** **Variant A** — hela ripplet i samma PR. Memory
  `project_b2_variant_a_full_ripple`.

## Vad som byggdes (Variant A — full ripple)

Klass 2 modellerade som **ORTOGONALA dimensioner** (enkel `IN(...)`-equality, AND
mot allt — till skillnad mot Municipality/Region som geo-union:as).

- **Domain:** `SearchCriteria`-VO (`EmploymentType`/`WorktimeExtent` + cap=400/
  regex/normalisering/equality/hashcode), `FilterHashCalculator` (canonical-JSON:
  emp/wt **mellan** region och sortBy — additiv format-bump, benign cache-dubblett,
  ingen versionering), `RecentJobSearch` (shadow-backing-fält + Capture-projektion).
- **Application:** `JobAdFilterCriteria` (2 dims) + alla 5 konsumenter (ListJobAds/
  RunSavedSearch/ListRecentSearches-CountAsync/FacetCounts/capture), `FacetDimension`
  (enum-append), `ICapturesRecentSearch` + capture-guard, ListJobAds/GetFacetCounts/
  Create/UpdateSavedSearch + validatorer, SavedSearchDto/RecentJobSearchDto (råa
  listor UTAN labels — Fas E-concern).
- **Infrastructure:** `SearchCriteriaConverters` (jsonb missing-key→tom = additivt,
  ingen saved_searches-migration), `JobAdSearchQuery` (ApplyCriteria ortogonal IN +
  ShadowColumn + ExcludeDimension egen-lista), `RecentJobSearchConfiguration`
  (2 `text[]`-kolumner), migration `20260612120000_B2RecentJobSearchKlass2Columns`
  (NOT NULL DEFAULT `'{}'`).
- **Api:** `?employmentType=`/`?worktimeExtent=` på GET / + /facet-counts; PATCH-body.
- **FE:** `saved-searches.ts` zod-drift städad (ssyk→occupationGroup/municipality +
  Klass 2, MAX_CONCEPT_IDS 10→400) + test. Inga Filter-panel-ändringar.

## Mandatory agents (§9.2)

- **senior-cto-advisor** — scope-beslut (Variant B+TD, Klas override:ade till A).
- **test-writer FÖRST** — VO-invarianter + FilterHash + validatorer + ApplyCriteria-
  grenar (Testcontainers) + capture-behavior; RED→GREEN. Andra pass: fixade 33
  befintliga test-anrop till utökade signaturer + Empty-meddelande-assert + jsonb-
  back-compat-fact.
- **db-migration-writer** — recent_job_searches-migrationen (manuellt scaffoldad —
  Api EF-design-build blockerades temporärt av en SavedSearchCriteriaInput-anropare
  jag missat, sedan fixad).
- **code-reviewer** ✓ Approved (0 Blocker/0 Major/1 Minor — migration-staging).
- **dotnet-architect** OK (0/0/2 — staging + Equals-kommentar; in-block-fixad).
- **security-auditor** ✓ APPROVED (0/0/0 — DoS-cap-paritet komplett, ingen ny PII,
  jsonb default-deny bevarad, authz oförändrad).

## Gates

Domain 471 · Application 789 · Api.IntegrationTests 473 (Testcontainers — EJ
InMemory, VO-Contains-fällan) · Worker 70 · Architecture 78 · FE vitest 839 ·
tsc/eslint rena. Full sln-build grön (0 warning/0 error).

## Operativt

- Stoppade Api/Worker för full sln-build (exe-fil-lås), startade om efter
  (Klas-regel `feedback_restart_stack_after_commit_stop`). Api 5049 + Worker uppe.
- Migration applicerad på lokala dev-DB:n via container-psql (DDL + `__EFMigrations
  History`-rad) — `dotnet ef database update` strulade på Local.json-lösenord; DDL
  additiv (2 kolumner NOT NULL DEFAULT `'{}'`).

## Lärdomar

- `JobAdFilterCriteria`-record fick 2 nya required positional params → kompilatorn
  fångade alla produktionsanropare; befintliga TEST-anropare (positionella
  Create/Input/Command) fångades först vid full sln-build, inte per-projekt-build.
- DTO-kontrakt-tester (positional-order-vakthundar) tvingade medveten uppdatering
  vid DTO-fält-tillägg — exakt deras syfte.

## Pending / nästa

- Fas E Filter-panel konsumerar denna query-wiring (separat PR, FE).
- Rendered-verifiering av Klass 2 i Filter-panel = Fas E (ingen UI denna PR).
