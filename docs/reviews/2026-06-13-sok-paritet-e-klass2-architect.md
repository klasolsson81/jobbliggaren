# dotnet-architect — Fas E Filter-panel, Klass 2 options-källa (ADVISOR)

**Datum:** 2026-06-13
**Roll:** Advisor (variant-analys, ingen kod). senior-cto-advisor är beslutsfattare.
**Kontext:** Fas E Filter-panel, Klass 2 (`employmentType` + `worktimeExtent`).
Klas-GO för backend+FE-scope inom Fas E (AskUserQuestion 2026-06-13).
**Relaterat:** ADR 0067 (Platsbanken-sök-paritet), ADR 0043 (taxonomi-ACL),
CLAUDE.md §2.1/§5/§9.6.

---

## Sammanfattning

Klass 2 kan redan **filtreras och facetteras** end-to-end, men
**options-discovery + label-resolution saknas helt** i ACL:n (ADR 0043) —
chips skulle visa "Okänd kod (...)" och panelen har inga val att rendera.
Ren options-/presentations-arkitektur. Tre+ varianter ramas in; CTO dömer.
Kärnspänning: de två mängderna är platta, föräldralösa, ~8 stabila noder —
snapshot-aktualitets-argumentet (ADR 0043 Variant A:s själva motiv) är mycket
svagare här än för kommun/yrkesgrupp.

## Verifierat nuläge (on-disk 2026-06-13)

| Lager | Klass 2-status | Fil |
|---|---|---|
| Endpoint-bind (GET / + /facet-counts) | KLAR | `JobAdsEndpoints.cs:108-109,120-121` |
| `FacetDimension`-enum | KLAR (`EmploymentType`, `WorktimeExtent`) | `FacetDimension.cs:28-32` |
| STORED-kolumner + `ApplyCriteria` | KLAR (~79% populerad, PR #60) | (B2) |
| `TaxonomyConceptKind` | **SAKNAS** (5 kinds, ingen Klass 2) | `TaxonomyConceptKind.cs:9-34` |
| `taxonomy-snapshot.json` + `TaxonomySnapshotFile` | **SAKNAS** | `TaxonomySnapshotFile.cs:16-20` |
| `MapRows` seeder | **SAKNAS** | `TaxonomySnapshotSeeder.cs:131-185` |
| `generate.mjs` | **SAKNAS** (bara municipality + ssyk-level-4) | `generate.mjs:75-84` |
| `TaxonomyTreeDto` (publikt kontrakt) | **SAKNAS** (2 collections) | `TaxonomyTreeDto.cs:18-20` |
| `ResolveLabelsAsync` / `labelByConceptId` | Kind-agnostisk — fylls automatiskt OM seedern lägger raderna | `TaxonomyReadModel.cs:160-162` |
| `SuggestionKind` (publik ACL-enum) | Saknar Klass 2 (men EJ önskvärt i suggest) | `SuggestionKind.cs:16-33` |
| FE `taxonomy.ts` DTO + `chip-models.ts` (`DimensionAxis`) | **SAKNAS** | `taxonomy.ts:67-71`, `chip-models.ts:18` |
| FE `FACET_DIMENSIONS` | **SAKNAS** (3 dims) | `job-ads.ts:195-199` |
| `saved-searches.ts` (råa listor) | Bär listorna **utan** `*Labels` (medvetet) | `saved-searches.ts:66-67` |

Nyckelobservation: `labelByConceptId` + `ResolveLabelsAsync` är
**kind-agnostiska** — plattar alla `TaxonomyConcept`-rader. Lägger seedern
Klass 2-rader fungerar chip-reverse-lookup för toolbar **och** recent-search-
labels automatiskt. Seed-källan + DTO-formen är gärningen, inte resolvern.

## Variant-analys (options-källan)

### Variant A — full snapshot-paritet (generator-integrerad)
Utöka `TaxonomyConceptKind` (+2), snapshot-JSON, `TaxonomySnapshotFile`,
`MapRows`, `generate.mjs`, `TaxonomyTreeDto` (flat-list-slot), FE-DTO.
- **För:** Maximal konsistens — en mekanism för alla dimensioner.
  `generate.mjs` förblir granskningsbar reproduktion (ADR 0043 Beslut B).
- **Emot:** `fetchChildren(child, parent)` är byggt för `broader→parent`;
  Klass 2 är **föräldralösa rot-mängder** → ny hämtnings-primitiv
  (`fetchConceptsByType`) för 8 noder som ändras ~aldrig. Aktualitets-vinsten
  (A:s existensberättigande) är nästan noll här.

### Variant B — fryst embedded seed (handgenererad)
Lägg de 2 platta mängderna som frysta data i snapshoten (el. separat embedded
resource), seedad via samma `MapRows` + samma `taxonomy_concepts`-tabell.
`generate.mjs` rörs inte.
- **Precedens:** `occupation-name-to-ssyk-level-4.v30.json` migrations-
  immutabilitet — exakt mönstret: liten, stabil, handkurerad mängd.
- **För:** Matchar domändatan (~8 legaldefinierade noder). Minsta yta.
  Chip-reverse-lookup gratis via samma read-model.
- **Emot:** Bryter "allt i snapshoten kommer från generate.mjs". Härkomst
  vilar på kommentar + commit. Mildras via separat `klass2-taxonomy.json`
  med "frozen, hand-curated, see ADR"-header.

### Variant C — FE-fixerad enum (endast fullständighet)
**Avvisas:** §5-brott (magic strings), DRY-brott (dubbel sanningskälla),
trasiga chip-labels för stale saved-searches. Exakt det ADR 0043 Beslut A
redan avvisade.

### Variant D (architect-föreslagen) — fryst seed via generator-tillägg utan broader-join
Ny enkel `fetchByType('employment-type')`-primitiv i `lib.mjs` (icke-rekursiv),
men frusen referensdata med samma immutabilitets-disciplin som B.
- **För:** Bevarar script-härkomst utan att låtsas en föräldralös typ är hierarki.
- **Emot:** Mest kod för minst data; berättigat endast om CTO värderar
  generator-härkomst högt.

## Inramning på de 7 punkterna

**2. Flat-list-DTO.** Inte syntetisk rot (struktur-lögn). Renast: två nya
topp-nivå-collections med delad platt `TaxonomyOptionDto(string ConceptId,
string Label)`:
```
public sealed record TaxonomyTreeDto(
    IReadOnlyList<TaxonomyRegionDto> Regions,
    IReadOnlyList<TaxonomyOccupationFieldDto> OccupationFields,
    IReadOnlyList<TaxonomyOptionDto> EmploymentTypes,
    IReadOnlyList<TaxonomyOptionDto> WorktimeExtents);
```
Open-Closed: additivt, hierarkiskt kontrakt orört.

**3. Generator-vs-fryst.** Aktualitets-argumentet (bär A för kommun/yrkesgrupp)
gäller inte här. Precedensen `occupation-name-to-ssyk-level-4.v30.json` är
närmaste analogin. Lutar mot fryst (B), ev. D om script-härkomst värderas.

**4. ResolveLabels-täckning.** Ingen kodändring i resolvern krävs (kind-
agnostisk). Villkor: seedern lägger Klass 2-rader i `taxonomy_concepts`.
Klass 2 ska EJ in i `suggestable` — "Heltid" som fritext-förslag är inte
paritet (facett-checkbox, inte sök-term). `SuggestionKind` orörd.

**5. Facet-counts FE-wiring.** Backend klar. FE återstår: `FACET_DIMENSIONS`
+2, `useFacetCounts("EmploymentType"/"WorktimeExtent")`, `chip-models.ts`
`DimensionAxis` +2 + map-grenar + label-resolver-grenar, `JobbUrlState`/
`buildJobbHref`/`ListJobAdsQuery` modellerar Klass 2.

**6. ADR-status.** **ADR 0043-amendment krävs** (utökar ACL med 2 kinds +
ändrar `TaxonomyTreeDto`:s publika form — ett ADR 0043-kontrakt). Inte ny ADR,
inte bara 0067-notat. Samma karaktär som "ADR 0043-amendment 2026-06-08
(kommun + yrkesgrupp)". Om CTO väljer B/D blir amendmentet rätt ställe att
motivera varför Klass 2 frystes medan kommun/yrkesgrupp genereras. ADR-mekanik
→ CTO-triage (ej CC-omdöme).

**7. Kandidat-PR-split (CTO ratificerar).**
- **PR-1 — backend options-källa** ("expose Klass 2 options i ACL"):
  `TaxonomyConceptKind` +2, snapshot/embedded-källa (B/D), `TaxonomySnapshotFile`,
  `MapRows`, `TaxonomyTreeDto` + `TaxonomyOptionDto`, FE `taxonomy.ts`-schema.
  `test-writer` obligatorisk. db-migration-writer EJ (bara nya rader, ingen DDL
  — verifieras i PR).
- **PR-2 — FE Klass-2 filter-panel** ("render Klass 2 i panel + chips"):
  `JobbUrlState`/`buildJobbHref`/`ListJobAdsQuery`/`chip-models`, toolbar-chips,
  panel-UI.
- **PR-3 — facet-wiring** ("facet-counts för Klass 2"): `FACET_DIMENSIONS` +2,
  `useFacetCounts`. Ev. merge med PR-2 om panel-utan-counts är ofullständig.
- **Öppen split-fråga:** saved-searches Klass 2-`*Labels` — egen PR-4
  ("Klass 2-labels i saved-searches-listan", separat `ListSavedSearches`-yta)
  eller i PR-1? CTO:s split-dom.

## Arkitektur-inramning (inga beslut)
- **Clean Arch:** alla varianter håller lager-disciplinen; `TaxonomyConceptKind`
  förblir `internal` (ACL-vokabulär läcker aldrig).
- **DDD:** taxonomi förblir icke-Domain referensdata (Evans kap. 14).
- **§5:** Variant C bryter; A/B/D respekterar.
- **Avgörande spänning för CTO:** A:s aktualitetsmotiv är svagt för en platt/
  stabil/legaldefinierad ~8-nods-mängd; B/D matchar domändatan + repo-
  precedensen bättre men kräver att ADR 0043-amendmentet motiverar avvikelsen.

## Referenser
- CLAUDE.md §2.1, §5.1, §9.6
- ADR 0043 Beslut A/B/C/E; ADR 0067 Beslut 1/4/6
- `TaxonomyConceptKind.cs`, `TaxonomyTreeDto.cs`, `TaxonomyReadModel.cs:160-174`,
  `TaxonomySnapshotSeeder.cs:131-185`, `generate.mjs:75-84`, `FacetDimension.cs:28-32`,
  `chip-models.ts:18`, `job-ads.ts:195-201`, `saved-searches.ts:66-67`
