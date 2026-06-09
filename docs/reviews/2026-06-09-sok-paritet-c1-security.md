# security-auditor — Platsbanken sök-paritet Fas C1 (query/filter-layer)

**Datum:** 2026-06-09
**Status:** GO — ✓ Approved (0 Blocker, 0 Major, 1 Minor — åtgärdad in-block)
**Auktoritet:** GDPR Art. 5/32, CLAUDE.md §5.4, ADR 0042/0043/0045/0067, Saltzer/Schroeder default-deny.

## Scope
Ren read-väg över **publik, icke-PII** jobbannons-data (JobTech-taxonomi-concept-ids). Inga migrations, ingen PII-yta, ingen auth-ändring, ingen AI-väg, inga secrets, inga loggrader tillkomna.

## Områdesutfall
- **SQL-injection (fri):** OccupationGroup/Municipality-grenarna använder `list.Contains(EF.Property<string?>(...))` — identiskt med Region; EF översätter till parametriserat `IN (@p0…)`. Ingen rå SQL/string-konkatenering (inga `FromSql*`/`ExecuteSql*`). Plain string-listor (ej VO) → EF-10/Npgsql-translation-fallgropen gäller ej.
- **DoS (cap 10→400):** taket fortfarande **ändligt** (= ssyk-universum). IN(400) mot B-tree-indexerad STORED-kolumn trivialt (matchar CTO-DoS-analys, ADR 0045 300ms p95). "Markera alla" = tom lista, ej 400 materialiserade ids.
- **Auth/authz:** `MapGroup.RequireAuthorization()` bevarad; `GET /` ListReadPolicy; `GET /taxonomy/labels` TaxonomyReadPolicy + `Cache-Control: private, no-store`. Nya params ändrar ej auth-ytan. 1600-cap reverse-lookup = O(n) in-memory dict-lookup, auth+rate-limitat.
- **PII/GDPR/tredjeland:** ej aktivt — concept-ids är publik referensdata. Ingen ny datakategori/AI/sub-processor/transfer.
- **Logghygien:** inga loggrader tillkomna, inget PII-/secret-läckage.
- **Defense-in-depth:** båda nya dimensionerna har cap + ConceptIdPattern-regex i ListJobAdsQueryValidator (default-deny). Deprecerad Ssyk valideras fortsatt.

## Minor (åtgärdad in-block 2026-06-09)
1. **`ResolveTaxonomyLabelsQueryValidator` saknade ConceptIdPattern-regex** (hade bara MaximumLength(32)+NotEmpty). I praktiken ofarligt (ids → in-memory TryGetValue, ingen SQL; okänt id → graceful "Okänd kod"), men reflekteras i JSON-DTO:n. **Fixad:** ConceptIdPattern (`^[A-Za-z0-9_-]{1,32}$`) speglas nu — symmetri med övriga concept-id-ytor, charset-cap mot XSS-stuffing. Test tillagt (charset-rejektion + välformad-pass).

## Sammanfattning
**GO.** Ingen DoS-vektor utöver CTO-analysen, ingen PII, ingen injection, ingen auth-regression. 1 Minor åtgärdad in-block. Mergeklar säkerhetsmässigt.
