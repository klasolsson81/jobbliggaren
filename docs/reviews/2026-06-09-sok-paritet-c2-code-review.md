# code-reviewer — Platsbanken sök-paritet Fas C2 (VO-expansion + reverse-lookup + Ssyk-borttagning)

**Status:** ✓ Approved — **GO** (4 Minor, 0 Blocker, 0 Major — alla Minor åtgärdade in-block, se CC-not sist)
**Granskat:** 2026-06-09/10 (branch `feat/sok-paritet-vo-reverse-lookup-c2`, ocommittad diff: 55 modifierade + 8 untracked kodfiler)
**Auktoritet:** CLAUDE.md §2.1–2.4, §3, §5, §9.6; ADR 0067 Beslut 1/6/7 + implementerings-notat; ADR 0042 Beslut B; ADR 0039 Beslut 1/3; ADR 0043
**Bindande underlag:** `2026-06-09-sok-paritet-c2-cto.md` ((a)–(f)) + `-c2-architect.md` (F1–F8)
**Test-state (CC-verifierat):** Domain 440 / Application 692 / Arkitektur 78 / Api-integration 420 (Testcontainers) / Migrate 6 / Worker 70 — alla gröna; 0 warn/0 err

---

## Område 1 — Clean Architecture / DDD: ✓ INTAKT

- **Domain serialiserings-/EF-fritt:** `SearchCriteria`/`FilterHashCalculator`/`RecentJobSearch` importerar endast BCL (`Utf8JsonWriter` för canonical-hash är etablerat mönster — hash-kontraktet ÄR ett domän-kontrakt). Ingen EF-/Mediator-/Npgsql-import i Domain.
- **VO-invarianter i Create:** alla fyra ADR 0042 B-invarianter verifierade — `NormalizeList` trim/distinct/sort ordinal; `MaxConceptIds=400`-cap per dimension via `ValidateConceptList` (DRY med bevarade per-dimension-felkoder, exakt architect F1); generaliserad tom-invariant med architect-specad svensk copy; bakåtkompat i Infrastructure-konvertern. Custom `Equals`/`GetHashCode` ordinal sekvensjämförelse i kanonisk ordning — jsonb-dedupe-garantin intakt.
- **Filter-SPOT-disciplin (ADR 0039 Beslut 1):** exakt tre `JobAdFilterCriteria`-konstruktioner i `src/` — **alla med named arguments**. Grep-verifierat.
- **Lager-läckor:** inga.

## Område 2 — CTO-dom-efterlevnad: ✓ ALLA SEX

| Dom | Verifierat |
|---|---|
| (a) ENDAST OccupationGroup+Municipality | VO:t bär exakt OccupationGroup/Municipality/Region/Q/SortBy. ADR 0067-notatet på plats. |
| (b) Frusen artefakt via broader-relation | Resursen: 2179 poster, 0 format-avvikelser, sorterad på nyckel (node-validerat i granskningen). |
| (c) Eager EF-migration, frusen resurs | Läser ENDAST embedded resource; set-baserad temp-tabell-transform; idempotent; fail-loud DO-block. |
| (d) Recent-expansion + radering | DELETE först i Up(); FilterHashCalculator ny canonical-JSON utan "ssyk", ingen hash-versionering; capture-guarden räknar alla fyra dimensioner (C1:s live-gap stängt + integrationstestat end-to-end). |
| (e) Ssyk helt borta | Grep-verifierat: borta ur query/interface/FilterCriteria/endpoint-param/commands. Kvarvarande `src/`-träffar = legitimt substrat (SsykConceptId-kolumn, backfill-jobb, synonym-expander) per Beslut 1. In-block-verifieringskravet uppfyllt: `ListJobAdsSsykNoOpTests` testar `?ssyk=X` → 200 + samma resultat, inkl. `?ssyk=has space` → 200 (validerings-400:an försvann med bindningen — medvetet, kommenterat). ×4-multiplikatorn: endast kommentar ändrad. |
| (f) Converter fail-loud på "Ssyk" | `case "Ssyk": throw` med F2:s feltext + garantikedjan i XML-doc. Write emitterar aldrig nyckeln (verifierat on-disk via rå `criteria::text`-läsning). |

## Område 3 — Architect F5-shimmen: ✓ KONTRAKTET OBRUTET

`RecentJobSearchDto` strikt additiv: 11 befintliga fält oförändrade i positionsordning, `SsykList`/`SsykLabels` deprecated + alltid `[]` (verifierat i handler, unit-test OCH integrationstest), fyra nya fält SIST. `RecentJobSearchDtoContractTests` låser positionsordningen reflektivt. `DeriveLabel`-kedjan q → yrkesgrupp → kommun → region → "Alla annonser" per F6. `SavedSearchDto` renamead fritt med korrekt motivering (FE konsumerar ej).

## Område 4 — Migrationen: ✓ ALLA FYRA KRAV

Ordning DELETE → DDL (DROP+ADD, motiverat) → transform; injection-skydd (regex-validering före interpolation, fail-loud); idempotens (nyckel-existens-predikat + nyckel-strip, integrationstestad omkörning); `COLLATE "C"` på både DISTINCT-argument och ORDER BY — den dokumenterade avvikelsen från F3-skissens exakta syntax är korrekt motiverad (PostgreSQL-kravet) och semantiskt identisk; Down lossy-doc; ingen taxonomy_concepts-läsning. `BuildReverseLookupSql()` internal + `InternalsVisibleTo` → testerna kör EXAKT migrationens SQL. Mycket stark testsvit (mappad rad, `"Ssyk":[]`, omappbart id, sorterad-distinct, idempotens).

## Område 5 — Kod-hygien: 4 Minor (nedan)

Konsekvent kommentarstil per fil, inga magic strings, hög XML-doc-kvalitet (inkl. F1-kravet om jsonb-nyckel-kontraktet + rättad stale `MaxConceptIds` i `RecentJobSearch`), testnamn per konvention. Anti-pattern-skanning (§5): rent. Tooling: `lib.mjs`-extraktionen gjord (fail-loud-regeln i EN kopia); one-shot-scriptet vägrar skriva över — utmärkt.

## Område 6 — DI: ✓ INGET SAKNAS

Inga nya interfaces/tjänster — konvertern statisk, migrationen EF-instansierad, resursen embedded. `DependencyInjection.cs` orörd, korrekt.

---

## Minor

1. **Stale kommentar `RecentJobSearchConfiguration.cs:52–53`** — "Ssyk/Region" → "OccupationGroup/Municipality/Region".
2. **Missvisande converter-beskrivning `SavedSearchConfiguration.cs:31–36`** — "ingen data-migration" är faktafel efter C2; hänvisa till `SearchCriteriaConverters.cs` (SPOT).
3. **Stale endpoint-exempel `RateLimitingOptions.cs:71`** — `?ssyk` → `?occupationGroup/?municipality`.
4. **Robusthetsnotat: skalär legacy-form** i migrationens transform ger rått Postgres-fel i st.f. pedagogisk RAISE. Fail-loud-egenskapen bevaras (ingen tyst korruption) → Minor.

## Bra gjort

- Test-writer-FÖRST-disciplinen syns i artefakterna; kontraktstester låser canonical-hash-JSON:en byte-exakt.
- `BuildReverseLookupSql()` internal-yta — migrations-SQL:en testas utan testkopia.
- Medvetna avvikelser dokumenteras i stället för att tystas (COLLATE-syntaxen, jsonb-nyckelordningens icke-observerbarhet, `?ssyk=has space`-kontraktsändringen).
- One-shot-scriptets självskydd operationaliserar CTO (c).1 i kod.
- Dimension-förväxlingsgrindar i hash- och capture-testerna.

## Dom

**GO.** Inga Blockers, inga Major. CTO-domarna (a)–(f) + architect F1–F8 implementerade trognt. Standing gates består: psql-apply efter Klas-GO; rapporten in i PR-body innan `automerge`-label.

---

**CC-not (in-block-åtgärd 2026-06-10, §9.6):** Minor 1–3 fixade (kommentarer uppdaterade). Minor 4 åtgärdad starkare än föreslaget: i stället för doc-mening lades en `jsonb_typeof`-check i fail-loud-DO-blocket med pedagogiskt RAISE (sammanföll med security-auditor Minor 2) + nytt Testcontainers-test. Re-verifierat: bygg 0 warn/0 err + sviterna omkörda gröna.
