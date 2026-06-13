# senior-cto-advisor — Fas E Filter-panel, Klass 2 (BESLUTSFATTARE)

**Datum:** 2026-06-13
**Roll:** Decision-maker (§9.2). CC gav ingen egen rekommendation.
**Klas-GO:** backend+FE-scope inom Fas E (AskUserQuestion 2026-06-13).
**Advisor-underlag:** `docs/reviews/2026-06-13-sok-paritet-e-klass2-architect.md`
**Relaterat:** ADR 0067, ADR 0043 (amendment-mål), ADR 0042 Beslut B, CLAUDE.md §5/§9.6.

---

## BESLUT 1 — Options-källans variant: **Variant B (fryst embedded seed)**

De två platta mängderna (~6 employment-type + ~2–3 worktime-extent) läggs som
frysta data, seedade via samma `MapRows` + samma `taxonomy_concepts`-tabell.
`generate.mjs` rörs inte. Variant D avvisas.

**Motivering:** YAGNI/KISS — Variant A:s enda värde (granskningsbar aktualitet via
generator) är noll för ~8 legaldefinierade, aldrig-växande noder. Precedens:
`occupation-name-to-ssyk-level-4.v30.json` (frusen, migrations-ägd). Seeder +
kind-agnostisk `ResolveLabelsAsync` återanvänds oförändrade → chip-/recent-/
saved-search-labels fungerar automatiskt när raderna seedas. D = mest kod för
minst data utan nytta (uniformitet är medel, inte egenvärde).

**Trade-off-mitigering (in-block):** frys Klass 2 i **separat** embedded
`klass2-taxonomy.json` med härkomst-header ("frozen, hand-curated, legally-
defined JobTech nodes — see ADR 0043-amendment 2026-06-13"), INTE inbäddat i
genererad `taxonomy-snapshot.json` (annars suddas härkomst-gränsen; nästa
generate.mjs-körning kunde skriva över handkurerad data).

## BESLUT 2 — Flat-list-DTO-form: **Ratificerad + precisering**

Två nya topp-nivå-collections på `TaxonomyTreeDto` med delad platt
`TaxonomyOptionDto(string ConceptId, string Label)`, ingen syntetisk rot.
OCP-additivt (hierarkiskt kontrakt orört); ärlig ACL-modell (Evans kap. 14).

**Precisering:** namnge `TaxonomyOptionDto` — återanvänd INTE kommun/yrkesgrupp-
DTO trots identisk `(ConceptId, Label)`-form. Coincidentally lika, ej
conceptually (DRY = knowledge piece, ej kod-likhet). Kommun och anställningsform
delar ingen change-reason.

## BESLUT 3 — ADR-status: **ADR 0043-amendment krävs (bekräftad)**

Utökar ACL:n med 2 kinds + ändrar publik `TaxonomyTreeDto`-form (ett ADR 0043-
kontrakt) — samma karaktär som amendment 2026-06-08 (kommun + yrkesgrupp).
**Amendmentet MÅSTE motivera generator-avvikelsen** (varför Klass 2 frystes
embedded medan kommun/yrkesgrupp genereras) — annars oförklarad härkomst-
inkonsekvens (Mastercard-testet). db-migration-writer triggas EJ (bara nya
seed-rader, `TaxonomyConceptKind` string-persisterad mot enum-append — verifieras
i PR).

## BESLUT 4 — PR-split + leverans-ordning

**Split (3+1 PR, linjär kedja):**
- **PR-1 — backend options-källa** ("expose Klass 2 options in taxonomy ACL"):
  `TaxonomyConceptKind` +2, frusen `klass2-taxonomy.json` + `TaxonomySnapshotFile`-
  utökning, `MapRows`-grenar, `TaxonomyTreeDto` + `TaxonomyOptionDto`, FE
  `taxonomy.ts`-schema, ADR 0043-amendment. **test-writer obligatorisk.**
  db-migration-writer EJ.
- **PR-2 — FE Klass-2 filter-panel** ("render Klass 2 filter panel + chips"):
  `JobbUrlState`/`buildJobbHref`/`ListJobAdsQuery`-modellering, `chip-models.ts`
  `DimensionAxis` +2 + grenar, panel-UI, toolbar-chips.
- **PR-3 — facet-wiring** ("Klass 2 facet-counts"): `FACET_DIMENSIONS` +2,
  `useFacetCounts`-grenar.
- **PR-4 — saved-searches Klass 2-`*Labels`** ("Klass 2-labels i saved-searches-
  listan"): `ListSavedSearches`-resolver-yta.

**Dom PR-3 separat (slås EJ in i PR-2):** rendera dimension vs dekorera med
live-count = två change-reasons, olika risk-profil. Panelen är fullt funktionell
utan counts (degraderar gracefully, E2c-precedens). Bundling = B2-misstaget om
igen (memory `feedback_one_concern_per_pr_soc`).

**Dom PR-4 egen (EJ i PR-1):** saved-searches-presentation ≠ options-källa
(CCP). Möjliggjord, ej blockerare — trivial efter PR-1 (kind-agnostisk resolver).
EJ TD (in-fas Fas E-leverans).

**Leverans-ordning:** PR-1 → PR-2 → PR-3 → (PR-4). Hård dependency PR-1→PR-2→PR-3
(samma taxonomy.ts/chip-models.ts-ytor). PR-4 när som helst efter PR-1.

## BESLUT 5 — UI-form: **STOPP — Klas-produktbeslut krävs**

**Mekanik-dom:** `SearchCriteria.cs:89-90` — både `EmploymentType` och
`WorktimeExtent` är `IReadOnlyList<string>`, multi-värde, OR-inom-dimension,
identiska i form. INGEN single-select-constraint i datamodellen. "Omfattning
radio" är en ren UI-presentations-restriktion ovanpå ett multi-värde-VO —
tekniskt giltig men ett produkt/UX-val, inte mekanisk nödvändighet.

**Varför Klas-beslut:** ADR 0067 Beslut 7 rad 109 säger explicit "Omfattning
radio, Anställningsform checkbox-multi" (Klas-referens-spec från Platsbanken).
Spänningen radio (paritet) vs checkbox-multi (panel-konsekvens + VO-kapacitet)
är en civic-utility-paritets-fråga, inte arkitektur. CC override:ar inte en
explicit Klas-rad med konsistens-argument utan bekräftelse.

**CTO-lutning (svag):** radio för Omfattning per rad 109 — paritet är hela ADR
0067:s syfte; worktime har bara 2-3 ömsesidigt meningsfulla värden. Men om Klas
värderar panel-intern konsekvens högre är checkbox-multi lika försvarbart (noll
backend-ändring). Anställningsform = checkbox-multi oavsett.

**Blockerar endast:** panel-kontrollens typ för Omfattning i PR-2. PR-1 + PR-2-
start (DTO/state/chip-modeller, formoberoende) är ej blockerade.

## In-block-fixar
- `TaxonomyOptionDto` egen typ (BESLUT 2).
- ADR 0043-amendment motiverar generator-avvikelsen (BESLUT 3).
- Separat `klass2-taxonomy.json` med härkomst-header (BESLUT 1).

## TD:er
Inga. Allt in-fas Fas E (PR-1–4). PR-4 = split-gräns, ej TD.

## STOPP-flaggor
- BESLUT 5: Klas radio-vs-checkbox för Omfattning innan panel-kontrollen byggs.

## Referenser
- Martin 2017 kap. 7/8/13; Evans 2003 kap. 14; Fowler 2018 kap. 3; Beck (YAGNI);
  Hunt/Thomas 1999 (DRY); SWE@Google 2020 kap. 9
- ADR 0043 Beslut A/B/C + amendment 2026-06-08; ADR 0067 Beslut 1/4/6/7; ADR 0042 Beslut B
- `SearchCriteria.cs:89-90`, `TaxonomyConceptKind.cs`, `TaxonomyTreeDto.cs`,
  `lib.mjs:19`, `generate.mjs:75-80`, `chip-models.ts:18`, `job-ads.ts:195-200`
