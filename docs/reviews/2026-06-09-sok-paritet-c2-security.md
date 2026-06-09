# security-auditor — Platsbanken sök-paritet Fas C2 (VO-expansion + reverse-lookup-migration + jsonb-bakåtkompat)

**Status:** ✓ GO — inga Blockers, inga Major (2 Minor — båda åtgärdade in-block, se CC-not sist)
**Granskat:** 2026-06-10 (branch `feat/sok-paritet-vo-reverse-lookup-c2`, ocommittad diff)
**Auktoritet:** GDPR Art. 5(1)(c)/17/32, CLAUDE.md §5.4/§9.2, ADR 0042 Beslut B + amendment 2026-06-09, ADR 0043 (hermetisk disciplin), ADR 0060, ADR 0067 Beslut 1/6/7, CTO-dom (a)–(f) + architect F1–F8 2026-06-09, security-auditor-prejudikat M1 2026-05-16 + F6 P4a 2026-05-20

Granskade filer (urval): `SearchCriteria.cs`, `20260609214512_C2SearchParityReverseLookupAndRecentExpansion.cs`, `Resources/occupation-name-to-ssyk-level-4.v30.json`, `SearchCriteriaConverters.cs`, `RecentJobSearchConfiguration.cs`, Create/Update-validators, `ListJobAdsQueryValidator.cs`, `RecentJobSearchCaptureBehavior.cs`, `ICapturesRecentSearch.cs`, `RecentJobSearchDto.cs` + `ListRecentSearchesQueryHandler.cs`, `JobAdsEndpoints.cs`/`SavedSearchesEndpoints.cs`, `RecentJobSearch.cs`/`FilterHashCalculator.cs`, `tools/taxonomy-snapshot/{lib.mjs,generate-occupation-group-mapping.mjs}`, `C2ReverseLookupMigrationTests.cs`, `JobbPilot.Infrastructure.csproj`.

---

## Fokusyta 1 — Cap/DoS på nya VO-dimensioner: ✓ KOMPLETT

Cap-matrisen är heltäckande. Alla fem inkommande ytor bär cap + regex; Domain är sanningskälla, validators är defense-in-depth:

| Yta | Cap (=`SearchCriteria.MaxConceptIds` 400, Domain-konstant refererad) | Regex `^[A-Za-z0-9_-]{1,32}` | SortBy |
|---|---|---|---|
| `SearchCriteria.Create` (Domain, sanningskälla) | ✓ per dimension via `ValidateConceptList` | ✓ per element | `Enum.IsDefined` ✓ |
| `ListJobAdsQueryValidator` | ✓ OccupationGroup/Municipality/Region | ✓ | `IsInEnum` ✓ |
| `CreateSavedSearchCommandValidator` (POST-body) | ✓ | ✓ | ✓ |
| `UpdateSavedSearchCommandValidator` (PATCH-body via `SavedSearchCriteriaInput`) | ✓ inom `When(Criteria is not null)` | ✓ | ✓ |
| Capture-vägen (`RecentJobSearchCaptureBehavior`) | ✓ via re-anrop av `SearchCriteria.Create` (failure → no-op, ingen persist) | ✓ | ✓ |

- **PATCH-endpoint-body-vägen specifikt verifierad** — ingen lucka.
- **`MaxConceptIds` = 400 oförändrad** — C1-låsningen respekterad.
- jsonb-läsvägen (converter) re-validerar via `SearchCriteria.Create` → även korrupt/handredigerad DB-rad kan inte materialisera ett VO över cap (default-deny, fail-loud).
- `ResolveTaxonomyLabelsQueryValidator` ×4 (=1600) behållen med endast kommentarsändring — konsekvent med CTO (e).
- Endpoint-grupperna behåller `RequireAuthorization()` + `ListReadPolicy` — ingen auth-/rate-limit-regression.
- 22 cap/regex-relaterade asserts i `SearchCriteriaTests` — test-writer-gaten uppfylld på cap-ytan.

## Fokusyta 2 — Reverse-lookup-migrationen (persisterad användardata): ✓

**(a) SQL-interpolation — injection-skyddet räcker.** `LoadFrozenMapping` validerar BÅDE nyckel och värde mot concept-id-grammatiken med fail-loud `InvalidOperationException` innan interpolation. Charset-grammatiken utesluter `'`, `\`, `;`, whitespace och alla tecken som kan bryta en enkel-citerad Postgres-literal (`standard_conforming_strings` default på → `\` vore ändå inert). Källan är dessutom en committad, git-granskad frusen artefakt — dubbelt skyddad (defense-in-depth, inte enda barriär). Temp-tabellnamn och övrig SQL är statiska literaler. Godkänt. (Se Minor 1 om `$`-ankaret.)

**(b) Fail-loud vid omappbara ids:** DO-blocket abortar FÖRE UPDATE — ingen partiell transform, ingen tyst amputation, ingen rad i tom-invariant-brott (Saltzer/Schroeder). RAISE-meddelandet bär saved_search-id (Guid) + omappbart concept-id — operatörsriktad migrations-output, icke-PII, parametriserad (ingen sekundär injection). Testat inkl. assert att raden är orörd.

**(c) Idempotens:** nyckel-strip + nyckel-existens-predikat (fångar `"Ssyk":[]`) + disjunkta id-universum → omkörning träffar 0 rader. `COLLATE "C"` på både DISTINCT-argument och ORDER BY ger ordinal-paritet i lagrad form (ADR 0042 invariant 1). Integrationstestet kör **exakt** migrationens SQL via `internal BuildReverseLookupSql()` + `InternalsVisibleTo` — ingen testkopia som kan glida. Förebildligt.

**(d) `DELETE FROM recent_job_searches` — inget GDPR-problem.** Sökhistorik knuten till JobSeekerId är personuppgift (Art. 4(1)), men raderingen är data-minimerings-*positiv* (Art. 5(1)(c)/5(1)(e)) — ingen lagrings- eller audit-skyldighet bryts. Entiteten är sedan ADR 0060/F6 P4a-granskningen explicit klassad som efemär, självåterbyggande cache utan audit-trail-värdighet (hard-delete är dess etablerade mönster; den ingår medvetet INTE i audit-loggen). Granskningstrailen för raderingen bärs av migrations-XML-doc + standing psql-apply-GO där Klas ser SQL:en. Right-to-deletion stärks; right-to-access avser nuläget (cachen återbyggs). Godkänt.

**(e) `Down()` lossy-dokumentation:** klass-XML-doc + inline-kommentar motiverar irreversibiliteten (1-till-många, 0 rader vid apply, ingen prod per ADR 0066, kunskapen committad i resursen). Uppfyller CTO-constraint 5.

## Fokusyta 3 — Fail-loud-konvertern: ✓ ingen dataläcka

- `"Ssyk"`-caset kastar **innan** värdet läses; meddelandet är statisk text utan rad-innehåll. Domain-invariant-failure-vägen exponerar endast `result.Error.Code`. `ReadStringOrStringArray`-felen bär endast fältnamn. Ingen criteria-data i något exception-meddelande. ✓
- **500-ytan är deploy-ordning, inte attack-yta — bekräftat:** en angripare kan inte plantera `"Ssyk"`-nyckeln; jsonb-kolumnen skrivs uteslutande genom konverterns `Write` (emitterar aldrig nyckeln) och command-ytan bär inte fältet (okänd JSON-prop ignoreras → `SearchCriteria.Empty`-400 — fail-säkert). Fail-loud kan bara fyras av operatörsfel (omigrerad DB) — dokumenterat + krav på migration-före-binär.

## Fokusyta 4 — Capture/data-minimering (Art. 5(1)(c)): ✓ invarianten består

- Default-browse-guarden räknar nu Q + OccupationGroup + Municipality + Region — "alla annonser, inget filter" capture:as fortfarande aldrig; F6 P4a High-2-invarianten är *utökad*, inte försvagad, och stänger samtidigt C1:s live-gap.
- Concept-ids är icke-PII taxonomi-koder; ingen ny PII-kategori. Q-hanteringen oförändrad.
- **Loggning ej utökad:** `LogCaptureFailed` loggar fortfarande endast exception-typ + message-typ (F6 P4a High-1-mönstret intakt). Inga nya logganrop med criteria-värden i diffen.

## Fokusyta 5 — Tooling-scripten: ✓ hermetisk disciplin

- `lib.mjs` + `generate-occupation-group-mapping.mjs` är off-runtime/off-build — verifierat: ingen referens i workflows, Husky-hooks eller package-/csproj-targets. Migrationen läser ENDAST embedded resource.
- Ingen secret-yta: publik HTTPS-endpoint (JobTech, öppet API), inga nycklar, inga npm-deps. One-shot-scriptet vägrar skriva över befintlig fil (`existsSync`-guard) — frysnings-invarianten mekaniskt skyddad. ADR 0043-disciplinen uppfylld.

## Fokusyta 6 — RecentJobSearchDto wire-shim: ✓

`SsykList: []` / `SsykLabels: []` hårdkodas i handlern — kan per konstruktion aldrig bära data; ingen informationsläcka. Nya fält sist (zod stripper okända nycklar). Deprecated-status dokumenterad med Fas E-borttagningsplan. Kontrakttest finns.

---

## Minor (bör åtgärdas, blockerar ej)

1. **`$`-ankaret i .NET-regex tillåter avslutande `\n`** — `^...$` matchar även `"abc\n"`. I migrationens `LoadFrozenMapping` sker ingen Trim före interpolation → ett resurs-värde med trailing newline skulle passera. Kan **inte** bryta sig ur den enkel-citerade literal:en → ingen injection-risk, men grammatiken är inte exakt den som utlovas. Åtgärd: `$` → `\z` i migrationens `ConceptIdPattern`; Domain/validator-kopiorna ofarliga (trim före match) men synkas för symmetri.
2. **Legacy skalär-form `"Ssyk":"id"`** ger korrekt abort men kryptiskt rått Postgres-fel ("cannot extract elements from a scalar") i stället för pedagogiskt RAISE. Fail-safe består — ej självförklarande. Åtgärd: `jsonb_typeof`-check i fail-loud-DO-blocket med tydligt meddelande, alt. doc + test-fixture. Risken hypotetisk (0 rader).

## Praise

- Injection-skyddet rätt byggt: grammatik-validering med fail-loud i stället för escaping-gymnastik, ovanpå committad granskningsbar källa — defense-in-depth i två lager ✓
- `internal BuildReverseLookupSql()` + `InternalsVisibleTo` → testerna kör exakt produktions-SQL:en ✓
- Ingen användardata i något exception-/loggmeddelande genom hela kedjan ✓
- Cap/regex-speglingen komplett på samtliga fem ytor med Domain-konstanten som single source ✓
- Frysnings-invarianten mekaniskt enforce:ad (overwrite-vägran), inte bara kommenterad ✓
- Skrivvägen för legacy-nyckeln stängd i samma batch — eager-transformen tät by construction ✓

## Sammanfattning

0 Blockers, 0 Major, 2 Minor. Cap-ytan på de nya dimensionerna — CTO:ns standing BLOCKING-gate för denna audit — är **godkänd utan anmärkning**. `DELETE FROM recent_job_searches` är GDPR-säker. Standing operativ gate består: psql-apply först efter Klas-GO.

**GO — säkerhetsmässigt mergeklar.**

---

**CC-not (in-block-åtgärd 2026-06-10, §9.6):** Båda Minor åtgärdade i samma batch — (1) `\z`-ankare i migrationens `ConceptIdPattern` + symmetri-synk i `SearchCriteria.cs` och fyra validators; (2) `jsonb_typeof <> 'array'`-check FÖRST i fail-loud-DO-blocket med pedagogiskt RAISE + subquery-typeof-filter i array-checken (hindrar LATERAL-evaluering på icke-array-rader) + nytt Testcontainers-test `ReverseLookup_ShouldAbortWithClearMessage_WhenSsykIsScalarLegacyForm` (assertar feltexten + att raden är orörd).
