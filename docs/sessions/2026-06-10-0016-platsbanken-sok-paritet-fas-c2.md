---
session: Platsbanken sök-paritet — Fas C2 (SearchCriteria-VO-expansion + reverse-lookup-migration + jsonb-bakåtkompat)
datum: 2026-06-09/10
slug: platsbanken-sok-paritet-fas-c2
status: levererad (PR mot main, automerge; migrations-apply mot dev-DB väntar Klas-GO)
bas-HEAD: bc54a84
branch: feat/sok-paritet-vo-reverse-lookup-c2
commits:
  - feat(jobads): Platsbanken sök-paritet Fas C2 — VO-expansion + reverse-lookup-migration + Ssyk-avveckling
  - docs: Fas C2 docs-sync (ADR-notat + agent-rapporter + current-work + session-log)
---

# Fas C2 — VO-expansion + reverse-lookup-migration + jsonb-bakåtkompat

## Mål
ADR 0067 Beslut 1 + Beslut 6 + Beslut 7 C2-raden. Gör de nya filter-dimensionerna
persistens-bara (sparade + recent-sökningar), migrera occupation-name-sökningar till
ssyk-level-4, stäng sparade-sökningar-halvan av C1:s no-op-fönster.

## Levererat
- **SearchCriteria-VO:** −Ssyk, +OccupationGroup +Municipality (sträng-listor, EJ VO —
  Npgsql Contains-fällan). Ny Create-signatur (occupationGroup, municipality, region, q,
  sortBy — SPOT-paritet, named args). Fyra ADR 0042 B-invarianter per ny dimension
  (ValidateConceptList-helper, per-dimension-felkoder), Equals/GetHashCode utökade.
- **SearchCriteriaJsonConverter:** nya nycklar OccupationGroup/Municipality (tolerant
  sträng-eller-array, saknad nyckel → tom lista = bakåtkompat-invariant 4); **fail-loud
  JsonException på legacy-"Ssyk"-nyckel** (CTO (f) — aldrig tyst Skip; garantikedja:
  migration + stängd skrivväg + 0 rader). Write emitterar aldrig "Ssyk".
- **Reverse-lookup-migration** `C2SearchParityReverseLookupAndRecentExpansion`:
  (1) DELETE recent_job_searches (CTO (d) — cache-data, ingen hash-versionering),
  (2) ssyk_list → occupation_group_list + municipality_list (DROP+ADD),
  (3) jsonb-transform Ssyk→OccupationGroup ur **frusen migration-ägd embedded resource**
  (occupation-name-to-ssyk-level-4.v30.json, 2179 poster, one-shot-genererad ur JobTech
  broader-relationen — live-verifierad 2179/2179 exakt 1 parent). Nyckel-existens-predikat
  (inkl. "Ssyk":[]), COLLATE "C"-sorterad lagrad form, fail-loud vid omappbart id +
  skalär legacy-form, idempotent, dokumenterat lossy Down. Regex-validering av ids
  (^...\z) före SQL-interpolation. **EJ applicerad mot dev-DB — Klas-GO krävs.**
- **RecentJobSearch + FilterHashCalculator:** _occupationGroup/_municipality ersätter
  _ssyk; ny canonical-JSON {"q","occupationGroup","municipality","region","sortBy"}.
  ICapturesRecentSearch + capture-guard räknar 4 dimensioner → **stänger C1:s live-gap**
  (yrkesgrupp-/kommun-only-sökningar fångas nu).
- **Full Ssyk-avveckling (CTO (e)):** ListJobAdsQuery/Validator/endpoint-param,
  JobAdFilterCriteria, Create/UpdateSavedSearchCommand + validators — Ssyk borta.
  ?ssyk= = obunden param (200 OK, integrationstestad). occupation-name-SUBSTRATET
  (job_ads.ssyk_concept_id + synonym-q-vägen) orört per Beslut 1.
- **Konsument-mappningar täppta:** RunSavedSearch + ListRecentSearches mappar VO-/entity-
  fälten in i filter-SPOT:en (ersätter C1:s tomma listor) → sparade/recent yrkesgrupp-/
  kommun-sökningar filtrerar.
- **Wire-kontrakt-shim (architect F5 — villkorat Blocker löst):** RecentJobSearchDto
  ADDITIV — SsykList/SsykLabels deprecated alltid-tomma (FE-zod kräver ssykList; C2 rör
  ej FE), nya fält sist. SavedSearchDto renamead fritt (ingen FE-konsument).
- **Tooling:** lib.mjs-extraktion (gql/fetchChildren delade) + one-shot-script med
  overwrite-vägran (frysnings-invariant mekaniskt skyddad).

## Beslut / detours
- **CTO-domar (a)–(f)** (`docs/reviews/2026-06-09-sok-paritet-c2-cto.md`): (a) endast
  OccupationGroup+Municipality i VO:t (B2-dims följer wiring-touchen — sekvensering, ej
  omprövning av Beslut 6); (b) broader-relation/frusen artefakt; (c) eager EF-migration
  (lazy on-read + runner avvisade); (d) recent-expansion i C2 + radering; (e) full
  Ssyk-borttagning nu; (f) ersätt + converter fail-loud. Inga nya Klas-STOPP utöver
  standing psql-apply-GO.
- **Discovery-fynd:** JobTech live 2179/2179 single-parent (Beslut 1-antagandet håller —
  källa (a), ingen ADR-amendment); dev-DB saved_searches=0 rader, recent=3 (1 med rå
  SSYK-kod {5132}, omappbar — raderas).
- **ADR-notat:** ADR 0067 implementerings-notat (C2-mekanik + B2-dims-sekvensering);
  ADR 0043 ×4-omformulering (legacy-Ssyk-skälet → B2-headroom).

## Reviews (alla GO)
- senior-cto-advisor: 6 domar. dotnet-architect: F1–F8 (1 villkorat Blocker löst i design).
- db-migration-writer: migration + Testcontainers-tester; idempotent-script verifierat.
- code-reviewer: GO (0/0/4 Minor — stale kommentarer ×3 + skalär-form-notat; alla fixade in-block).
- security-auditor: GO (0/0/2 Minor — \z-regexankare + skalär-typeof-check; båda fixade
  in-block + nytt test). Cap-ytan (BLOCKING-gaten) godkänd utan anmärkning; recent-
  raderingen GDPR-säker (data-minimerings-positiv cache-radering).

## Tester (Testcontainers, ej InMemory)
Domain 440, Application 692, Arkitektur 78, Api-integration 421 (+~20 nya/omskrivna:
C2ReverseLookupMigrationTests ×6, jsonb-backcompat-fail-loud, ?ssyk=-ignorering,
capture-end-to-end yrkesgrupp/kommun), Migrate 6, Worker 70. Alla gröna. Bygg 0 warn/0 err.

## Inga TDs lyfta
Fas E-fynd (deprecated DTO-fält-borttagning + FE MAX_CONCEPT_IDS-synk) ryms i ADR
0067-fasplanen. Inga nya dependencies.

## Nästa
Fas D1 (facet-counts + typeahead-suggest, NBomber-gate) ELLER Fas E (FE-picker — gör
nivåbytet synligt; konsumerar C1-DTO + C2-persistens). E planerad sist per ADR 0067.
Klas-GO för fas-skifte.

## Pending operativt för Klas
- **GO: applicera C2-migrationen mot dev-DB (psql)** — därefter startas Api/Worker om
  (deploy-ordning: migration FÖRE ny binär; fail-loud-konvertern + recent-kolumnbytet).
  Idempotent-script: genereras av CC vid GO.
- Granska C2-PR-diff post-merge (automerge).
- Re-ingest Klass 2 (B2-avslut, valfri timing) — opåverkad av C2.
