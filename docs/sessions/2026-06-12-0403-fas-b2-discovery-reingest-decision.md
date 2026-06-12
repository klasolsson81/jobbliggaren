---
session: Fas B2-discovery + re-ingest-beslut
datum: 2026-06-12
slug: fas-b2-discovery-reingest-decision
status: discovery-klar — kod-leverans avförd (re-ingest först per CTO+Klas)
commits:
  - docs(jobads): Fas B2-discovery — data-lager redan komplett, re-ingest-beslut
---

# Fas B2-discovery + re-ingest-beslut (2026-06-12)

## Mål (Klas-prompt)

Bygga "Fas B2-backend (Klass 2: Omfattning + Anställningsform)" — promptens
antagande: migration (`employment_type`/`worktime_extent` STORED) + JobTechHit-POCO
+ allowlist + query-wiring SAKNAS och ska byggas; re-ingest (~2,5h) körs av Klas
efter PR; FE byggs ej (data NULL tills re-ingest). Discovery beordrad FÖRST.

## Vad som hände — premissen inverterades

Discovery (HEAD `fe5041c`, inga ändringar) visade att B2:s **data-lager redan är
komplett och mergat på main**:

- STORED-migration `20260608205054_F6P7JobAdKlass2SearchColumns` (employment_type
  + worktime_extent, partial-index, korrekt NULL-tills-re-ingest-DoD)
- JobTechHit-POCO (`JobTechEmploymentType` + `JobTechWorkingHoursType`)
- Sanitizer-allowlist (`employment_type` + `working_hours_type`)
- JobAdConfiguration shadow-mapping (`EmploymentTypeConceptId` +
  `WorktimeExtentConceptId`)
- `BackfillJobAdKlass2Job` + `POST /api/v1/admin/job-ads/backfill-klass2`
- `JobAdGeneratedColumnsTests` + `JobTechHitDeserializationTests`

Det som faktiskt SAKNAS = **query-wiringen** (`JobAdFilterCriteria`,
`ApplyCriteria`-grenar, `ListJobAdsQuery`+validator, `?employmentType=`/
`?worktimeExtent=`-bindning, `SearchCriteria`-VO-fält, `FacetDimension`-värden).
Den är i ADR 0067 **medvetet gated bakom re-ingest** (Beslut 6 + C1/C2/D1/D2-notat
+ `FacetDimension`-kodkommentaren), varje gång med samma falsk-klar-motivering.

Full rapport: `docs/research/2026-06-12-fas-b2-state-discovery.md`.

## Beslut

Sekvens-/fas-fråga mot Accepted-ADR-mekanik → senior-cto-advisor-triage
(decision-maker, CLAUDE.md §9.6; memory `feedback_adr_mechanism_vs_env_phase_triage`).
CTO-dom:

1. **Re-ingest FÖRST**, wira query-lagret efteråt mot sann data. Bygg inte
   wiringen denna session.
2. **Falsk-klar:** "FE-gated + Testcontainers-syntetisk" bevisar SQL-grenen men
   inte prod-tillståndets klarhet; VO-ripplet till SavedSearch-persistens
   (converter, Create/Update-commands+validators, RecentJobSearch,
   FilterHashCalculator) riskerar tyst-noll-träff-bevakningar (ADR rad 41/115).
3. **Scope:** ingen legitim liten batch — hela Klass 2-query-ytan delar
   change-reason "data blev tillgänglig" (CCP, Martin kap. 13).
4. **Klas-GO krävs** — prompten skrevs under fel premiss.

**Klas-GO (AskUserQuestion 2026-06-12):** "Re-ingest först (CTO-rek)".

## Detour / lärdom

- Promptens scope-punkt 0 (discovery FÖRST) fångade en helt inverterad premiss —
  exakt det discovery-disciplinen finns för. Hade wiringen byggts på antagandet
  hade ~8 filer (inkl. tung SavedSearch-VO-ripple) landat mot all-NULL-data,
  precis den falsk-klar-yta ADR 0067 avvisat tre gånger.
- Backfill-ssyk-precedensen (2026-05-24): admin-curl → jobId → Worker processar.
  AWS-GRANT-incidenten (42501 permission denied for schema hangfire) var
  AWS-specifik — lokalt gäller `PrepareSchemaIfNecessary=true`, ej relevant.

## Nästa session

1. **Klas kör re-ingest** (`backfill-klass2`, ~2,5h) + verifierar
   `emp_pop`/`wt_pop ≫ 0`.
2. **Query-wiring-PR mot sann data:** test-writer FÖRST (SearchCriteria-VO-fält +
   filter-grenar), db-migration ej (kolumner finns), Testcontainers-integration,
   `FacetDimension`-append + GROUP BY-gren. Reviews: code-reviewer +
   dotnet-architect (>5 filer), security-auditor (VO-cap / SavedSearch-yta).
