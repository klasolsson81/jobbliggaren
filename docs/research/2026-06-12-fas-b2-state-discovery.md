# Discovery — faktiskt Fas B2-läge (Klass 2: Anställningsform + Omfattning)

**Datum:** 2026-06-12
**Utförd av:** Claude Code (discovery-disciplin, CLAUDE.md §9.4)
**HEAD:** `fe5041c` (`docs(spec): CLAUDE.md-prune … (#57)`)
**Trigger:** Klas-prompt "Fas B2-backend" antog att `employment_type`/`worktime_extent`-kolumnerna + POCO + allowlist + query-wiring SAKNADES. Discovery beordrad FÖRST (inga ändringar) per promptens scope-punkt 0.
**Beslut:** senior-cto-advisor-triage 2026-06-12 (decision-maker, CLAUDE.md §9.6) — re-ingest FÖRST, query-wiring efteråt. Klas-GO (AskUserQuestion 2026-06-12): "Re-ingest först (CTO-rek)".

---

## Slutsats (TL;DR)

**Premissen är inverterad.** B2:s **data-lager är komplett och mergat på main** (migration + POCO + allowlist + shadow-mapping + backfill-jobb + tester). Det som återstår är **query-wiringen** — och den är i ADR 0067 **medvetet gated bakom re-ingest** (Beslut 6 + C1/C2/D1/D2-notat). Rätt nästa steg är därför **re-ingest mot nuvarande on-disk-state**, inte mer kod.

---

## FINNS on-disk (B2 data-lager — komplett)

| Komponent | Fil | Verifierat |
|---|---|---|
| STORED-migration | `Migrations/20260608205054_F6P7JobAdKlass2SearchColumns.cs` | `employment_type_concept_id` (`raw_payload->'employment_type'->>'concept_id'`) + `worktime_extent_concept_id` (`raw_payload->'working_hours_type'->>'concept_id'`), båda STORED + partial B-tree-index `WHERE … IS NOT NULL`. Kommentaren dokumenterar korrekt att kolumnerna är NULL för 100% av raderna tills re-ingest. |
| JobTechHit-POCO | `JobSources/Platsbanken/JobTechSearchResponse.cs:109-113,189-211` | `[JsonPropertyName("employment_type")] JobTechEmploymentType` + `[JsonPropertyName("working_hours_type")] JobTechWorkingHoursType`. Namnglapp (wire `working_hours_type` → taxonomi/kolumn `worktime_extent`) dokumenterat. |
| Sanitizer-allowlist | `JobSources/Platsbanken/JobTechPayloadSanitizer.cs:54` | `"employment_type"` + `"working_hours_type"` i `AllowedKeys` → passerar PII-strippningen (default-deny). |
| Shadow-mapping (EF) | `Persistence/Configurations/JobAdConfiguration.cs:113-119` | `EmploymentTypeConceptId` + `WorktimeExtentConceptId` shadow-properties mappade mot kolumnerna; namnglapp-fälla (`working_hours_type`-path) dokumenterad. |
| Backfill-jobb | `Application/JobAds/Jobs/BackfillJobAdKlass2/BackfillJobAdKlass2Job.cs` | Tunn wrapper kring `JobAdRefetchBackfillRunner`; predikat `EmploymentTypeConceptId IS NULL`; per-ID-refetch re-skriver hela `raw_payload` → båda Klass 2-kolumnerna populeras; idempotent restart-vänligt. |
| Admin-endpoint | `Api/Endpoints/AdminJobAdsEndpoints.cs:97-104` | `POST /api/v1/admin/job-ads/backfill-klass2` (RequireAuthorization Admin), fire-and-forget Hangfire-enqueue, 202 + jobId. |
| Tester | `tests/.../JobAdGeneratedColumnsTests.cs`, `tests/.../JobTechHitDeserializationTests.cs` | Finns. |

## SAKNAS on-disk (query-wiringen — den faktiska luckan)

| Komponent | Fil | Gap |
|---|---|---|
| Filter-SPOT | `Application/JobAds/Abstractions/JobAdFilterCriteria.cs:27-31` | Endast `OccupationGroup`/`Municipality`/`Region`/`Q`. Inga Klass 2-fält. |
| ApplyCriteria | `Infrastructure/JobAds/JobAdSearchQuery.cs:171-254` | Grenar för OccupationGroup + geo-union (Municipality/Region) + Q. Ingen equality-gren för employment/worktime. |
| Query + validator | `Application/JobAds/Queries/ListJobAds/ListJobAdsQuery.cs` + `…/ListJobAdsQueryValidator.cs` | Inga params/regler för Klass 2. |
| Endpoint-bindning | `Api/Endpoints/JobAdsEndpoints.cs:31-67` | Ingen `?employmentType=`/`?worktimeExtent=`. |
| SearchCriteria-VO | `Domain/SavedSearches/SearchCriteria.cs:72-76` | Medvetet utelämnade. Kommentar: *"EmploymentType/WorktimeExtent-VO-fält följer sin query-wiring post re-ingest (CTO (a) — sekvensering, ej omprövning av Beslut 6)."* |
| FacetDimension | `Application/JobAds/Abstractions/FacetDimension.cs:18-29` | Medvetet utelämnade. Kommentar: *"Anställningsform/omfattning (B2-dims) UTESLUTS medvetet tills full re-ingest … De tillkommer additivt … i samma PR som re-ingestens data, med GROUP BY-gren + Testcontainers-rad."* |

---

## ADR-sekvensen (varför wiringen är gated)

ADR 0067 gatar query-wiringen bakom data-tillgänglighet på minst tre ställen, varje gång med samma falsk-klar-motivering:

- **Implementerings-notat C2 (CTO (a)):** *"EmploymentType/WorktimeExtent-VO-fälten följer sin query-wiring-touch (post re-ingest, D1-grannskapet) … VO-fält utan ApplyCriteria-gren/query-param/data vore tyst-noll-träff-bevakningar eller död Domain-kod."*
- **Implementerings-notat D2:** *"EmploymentType/WorktimeExtent uteslutna: NULL-data tills re-ingest Klass 2 körts (samma falsk-klar-gate som D1/FacetDimension). Tillkommer additivt vid B2-data, ej i D2-kontraktet."*
- **`FacetDimension`-kodkommentaren** (verbatim ovan).

## CTO-triage 2026-06-12 (sammanfattning)

`docs/reviews/`-spår finns ej separat skrivet (CTO-domen levererades inline i sessionen). Domen:

1. **Sekvens:** kör re-ingest FÖRST mot nuvarande state; wira query-lagret efteråt mot sann data. Bygg INTE wiringen denna session.
2. **Falsk-klar:** "byggd + FE-gated + Testcontainers-syntetisk" bevisar SQL-grenens korrekthet men inte prod-tillståndets klarhet — ADR:ns gate skyddar mot att on-disk-tillståndet ser klart ut men ger tyst-noll. VO-ripplet till SavedSearch-persistens (converter, Create/Update-commands+validators, RecentJobSearch, FilterHashCalculator) riskerar dessutom tyst-noll-träff-**bevakningar** (ADR 0067 rad 41/115) — värre än ingen bevakning.
3. **Scope:** ingen legitim "liten" batch — hela Klass 2-query-ytan (filter + VO + facet) delar change-reason "data blev tillgänglig" (CCP, Martin *Clean Architecture* kap. 13).
4. **Klas-GO:** krävs — promptens sekvens skrevs under fel premiss (data-lagret antogs saknas); override under fel premiss är gissning, inte medvetet val (CLAUDE.md §9.6 p.5/6).

Principkällor: Martin (2017) kap. 7/13, Fowler (2018) kap. 3, Beck (YAGNI), Ford/Parsons/Kua (2017), Evans (2003) kap. 2/5. Memory: `feedback_adr_mechanism_vs_env_phase_triage`, `feedback_di_with_handlers_same_commit`.

---

## Operativt nästa steg — re-ingest Klass 2

**Förutsättning:** Api (`http://localhost:5049`) + Worker uppe; admin-autentiserad session (samma metod som backfill-ssyk 2026-05-24). Lokalt gäller `Hangfire:PrepareSchemaIfNecessary=true` (AWS-GRANT-incidenten 42501 var AWS-specifik, ej relevant lokalt — ADR 0066).

```bash
# 1. Trigga backfill (202 Accepted + jobId; Worker kör fire-and-forget, ~2,5h)
curl -X POST http://localhost:5049/api/v1/admin/job-ads/backfill-klass2 \
  -H "Authorization: Bearer <admin-session-token>"
```

```sql
-- 2. Verifiera populering (när Hangfire-jobbet klart)
SELECT count(*) FILTER (WHERE employment_type_concept_id IS NOT NULL)  AS emp_pop,
       count(*) FILTER (WHERE worktime_extent_concept_id IS NOT NULL)  AS wt_pop,
       count(*)                                                        AS total
FROM job_ads;
-- Förväntat: emp_pop/wt_pop ≫ 0 (ej 100% — annonser utan fälten i JobTech-payload förblir NULL, korrekt).
```

```sql
-- 3. (valfritt) övervaka Hangfire-jobbet
SELECT id, statename, createdat FROM hangfire.job ORDER BY createdat DESC LIMIT 5;
```

**Efter populering:** query-wiringen (JobAdFilterCriteria + ApplyCriteria + ListJobAdsQuery/validator + `?employmentType=`/`?worktimeExtent=`-bindning + SearchCriteria-VO-expansion + FacetDimension-append) byggs i en efterföljande PR mot sann data, med test-writer FÖRST + Testcontainers-integrationstester (ADR 0067 Beslut 6/7-sekvens).
