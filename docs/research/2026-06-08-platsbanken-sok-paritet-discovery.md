# Platsbanken sök-paritet — discovery + parity-spec (Session 1)

**Datum:** 2026-06-08
**Status:** Discovery klar — underlag för dotnet-architect + senior-cto-advisor (STEG 3) + sök-paritets-ADR.
**Källor:** Klas Platsbanken-screenshots (`tmp/platsbanken/`, ej committade — raderas vid initiativ-slut), JobTech Taxonomy/JobSearch live-läsning 2026-06-08, kodbas-discovery 2026-06-08.
**Initiativ:** 100% paritet med Platsbankens sök/filter/sortering + smart fritext-Sök. Levereras i faser, PR per fas.

> Detta dokument är discovery-/design-underlag, inte ett beslut. Multi-approach-frågorna i §6 avgörs av senior-cto-advisor (CC ger ingen egen rek, `feedback_cto_decides_multi_approach`). ADR Proposed→Accepted = Klas-beslut.

---

## 1. Platsbankens faktiska sök-yta (från Klas-screenshots)

Tre topp-nivå-dropdowns: **Ort**, **Yrke**, **Filter** + ett fritext-sökfält ("Sök på ett eller flera ord — Skriv t.ex. målare Malmö") med grön "Sök"-knapp.

### 1.1 Ort (två-kolumns kaskad)
- **Vänster: Län** (21 st, single-select highlight grön + chevron `›`) + sektion **Övriga**: `Obestämd ort`, `Utomlands` (checkbox).
- **Höger: Kommuner** för valt län (checkbox-multi) + `Välj alla kommuner` (select-all för länet).
- Egen **Rensa** (röd text-länk) per kolumn (Län resp. Kommuner).
- Multi-län: kommuner ackumuleras över flera län (val per län bevaras).
- **Ort-knappen får grön pricka (●)** när minst ett val är aktivt.
- **Live-count:** knapp längst ner "Visa N annonser" uppdateras vid varje val (ex: "Visa 46 annonser" för Kalmar län: Mönsterås + Mörbylånga).

### 1.2 Yrke (två-kolumns kaskad — KRITISK NIVÅ-INSIKT)
- **Vänster: Yrkesområden** (21 st = `occupation-field`, single-select highlight grön + chevron).
- **Höger: Yrken** (checkbox-multi) — MEN dessa är **`ssyk-level-4` (yrkesgrupp)**, INTE `occupation-name`. Bevis: etiketter som "Revisorer m.fl.", "Övriga ekonomer", "IT-säkerhetsspecialister", "Övriga maskinoperatörer", "Stenhuggare m.fl." — "m.fl."/"Övriga"-suffix är SSYK-4-gruppmarkörer.
- **Ingen tredje nivå.** Picker går occupation-field → ssyk-level-4 och stannar där. Selektion (filter) sker på yrkesgrupp-nivå.
- Egen Rensa-länk per kolumn. Live-count "Visa N annonser".
- Klas exempel: hela **Data/IT** = 2700+ jobb; en grupp **IT-säkerhetsspecialister** = 148 jobb. Båda på ssyk-level-4-/occupation-field-nivå.

### 1.3 Filter (singel-kolumn panel)
| Sektion | Kontroll | JobTech-dimension |
|---|---|---|
| Omfattning | radio: Alla / Heltid / Deltid | `worktime-extent` (2) |
| Anställningsform | checkbox-multi: Tillsvidareanställning / Tidsbegränsad anställning / Vikariat / Behovs- eller timanställning / Sommarjobb | `employment-type` (5) — OBS "Sommarjobb"-etikett ≠ taxonomins "Säsongsanställning"; mappning verifieras |
| Publicerad | radio: Alla / Idag / Senaste 7 dagarna / Senaste 30 dagarna | `publication_date`-tröskel (vi har `Since`/`IsNew` redan) |
| Arbetsplats | checkbox: **Möjligt till distansarbete** (info-ikon) | `work-place-model` = Distansarbete (härledning krävs — ej toppfält i payload) |
| Körkort | checkbox: Utan krav på körkort | `driving-licence` (svensk-specifik) |
| Utbildningskrav | radio: Alla / Yrken utan krav på utbildning / Yrken med krav på utbildning | härledd |
| Anställningsstöd | checkbox: Nystartsjobb | härledd |
| Anpassad arbetsplats | checkbox: Öppen för alla | härledd |
| (botten) | "Visa N annonser"-knapp + **Rensa** röd länk överst | live-count |

### 1.4 Sortering
"Sortera efter publiceringsdatum ▾" syns i resultatlistan. Verifierade alternativ (AF-artikel 2025-09-16): **Relevans** (default), **Datum** (publicering), **Ansökningsdatum** (sista ansökningsdag).

### 1.5 Övriga UX-krav från Klas
- **Live-antal per filter** ("Visa N annonser") — facetterad räkning, genomgående.
- **Rensa** som röd text-länk (ej knapp), per sektion. Civic-utility.
- **Ny färg-identitet** önskad: mörkgrön-ish, ej blå-vit (Platsbanken bytte till mörkgrön). Behöver ej kopieras exakt. → DESIGN.md-token-arbete, FE-fas, design-reviewer + Klas-GO.
- **100% korrekt taxonomi-mappning** — alla orter/yrken, korrekt indelade; matcha Platsbankens count per val.

---

## 2. JobTech-taxonomi (web-verifierat live 2026-06-08)

| Dimension | Concept type | ~antal | Relation | I annons-payload |
|---|---|---|---|---|
| Län | `region` | 21 sv (1519 internationellt) | toppnivå | `workplace_address.region` + `regionconceptid` + `region_code` |
| Kommun | `municipality` | 290 | `broader`→region / region `narrower`→kommun | `workplace_address.municipality` + `municipalityconceptid` |
| Yrkesområde | `occupation-field` | 21 | `narrower`→ssyk-level-4 | `occupation_field` {concept_id,label} |
| Yrkesgrupp | `ssyk-level-4` | 400 | `broader`→occupation-field; `narrower`→occupation-name | `occupation_group` {concept_id,label} (namnglapp — fältet heter occupation_group men pekar på ssyk-level-4) |
| Yrke | `occupation-name` | 2179 | `broader`→ssyk-level-4 | `occupation` {concept_id,label} |
| Anställningsform | `employment-type` | 5 | flat | `employment_type` {concept_id,label} |
| Omfattning | `worktime-extent` | 2 (Heltid/Deltid) | flat | `working_hours_type`/`workinghourstype` + `scope_of_work {min,max}` |
| Distans | `work-place-model` | 3 (Distans/Hybrid/På plats) | flat | **Nej** — inget toppfält i AdFields; härleds |

**Kritiska namnglapp:** Det finns INGEN concept-type `occupation-group` — "yrkesgrupp" = `ssyk-level-4`. Det finns INGEN `working-hours-type` — omfattning = `worktime-extent`. Annons-FÄLTEN heter dock `occupation_group`/`working_hours_type` (pekar på ssyk-level-4 resp. worktime-extent).

**Relationer:** via GraphQL (`/v1/taxonomy/graphql`) — `broader(type:…)` / `narrower(type:…)`. REST `/relations` gav 404. Hierarki end-to-end-verifierad: occupation-field → ssyk-level-4 → occupation-name; region → municipality.

---

## 3. JobbPilot nuläge (on-disk, kodbas-discovery)

### 3.1 Queryable idag
`q` (FTS-hybrid + title-LIKE + SSYK-synonym), `ssyk[]` (occupation-NAME-nivå), `region[]` (länsnivå), `sortBy` (PublishedAtDesc/Asc, ExpiresAtDesc/Asc, Relevance), `page`/`pageSize`, `since` (IsNew-badge).

### 3.2 Sök-SPOT
`IJobAdSearchQuery` (Application-port, ADR 0062) → `JobAdSearchQuery` (Infrastructure, `internal sealed`). `ApplyCriteria`/`ApplySort`/`ApplyRelevanceSort`. Filter-record: `JobAdFilterCriteria(IReadOnlyList<string> Ssyk, IReadOnlyList<string> Region, string? Q)`; `JobAdSearchCriteria(Filter, SortBy, Page, PageSize, Since)`. Tre konsumenter (ListJobAds + RunSavedSearch via SearchAsync; ListRecentSearches via CountAsync). **OBS: filter-värden är `string` concept-id, INTE strongly-typed VO** → VO-Contains-fällan (`feedback_ef_strongly_typed_vo_contains_translation`) gäller EJ de befintliga; nya dimensioner ska hållas som strängar för att fortsatt undvika den.

### 3.3 STORED generated columns (JobAdConfiguration)
- `ssyk_concept_id` ← `raw_payload->'occupation'->>'concept_id'` (occupation-NAME, B-tree partial-index)
- `region_concept_id` ← `raw_payload->'workplace_address'->>'region_concept_id'` (B-tree partial-index)
- `search_vector` ← `to_tsvector('swedish', title || ' ' || description)` (GIN, partial WHERE deleted_at IS NULL)

`raw_payload` = `JsonSerializer.Serialize(hit)` av POCO:n `JobTechHit` → kolumn-pekarna matchar POCO:ns `[JsonPropertyName]` (snake_case `municipality_concept_id`, `region_concept_id`), EJ nödvändigtvis API:ts display-namn. POCO:n `JobTechWorkplaceAddress` deserialiserar redan `municipality_concept_id` → **kommun-kolumn följer exakt det beprövade region-mönstret (låg risk).**

### 3.4 Taxonomi-snapshot (ADR 0043)
`ITaxonomyReadModel` → `TaxonomyReadModel` (Infrastructure, in-memory-cache av `taxonomy_concepts`-tabell, seedad från committad `taxonomy-snapshot.json`). `TaxonomyConceptKind`: Region / Occupation / OccupationField. Träd: Län (flat ~21) + **Yrkesområde→Yrke (occupation-field→occupation-NAME)**. `TaxonomyConcept.ConceptId` PK + single `ParentConceptId`. **Ingen Municipality-kind. Ingen ssyk-level-4-kind.** Reverse-lookup → "Okänd kod (id)"-fallback.

### 3.5 Ingestion
`PlatsbankenJobSource` → `JobTechHit` (deserialiserar occupation/occupation_group/occupation_field/workplace_address inkl. municipality_concept_id) → sanitizer-allowlist → raw_payload jsonb. Generated columns projicerar shadow-props vid skriv.

### 3.6 Domän-enums som EJ används i filter
`JobAdStatus` (Active/Expired/Archived — hårt Active-filter), `JobSource`. **Finns INGA enums för** WorkingHoursExtent / EmploymentType / WorkMode / Municipality — dessa data finns i raw_payload men är ej extraherade till queryable kolumner. (Prompten antog att de fanns som enums; on-disk gör de inte det.)

### 3.7 Migrations-mönster
F2P9 (ssyk/region STORED + index), F6P4 (search_vector GIN). Nya STORED-kolumn-tillägg på `job_ads` (~40k rader) = metadata-billig ADD COLUMN men STORED-omberäkning sker per rad vid skriv (verifiera mot Testcontainers).

---

## 4. Paritets-gap (vad som saknas)

| Platsbanken-yta | JobbPilot-status | Gap |
|---|---|---|
| Ort: Län→Kommun-kaskad | Endast länsnivå (`region_concept_id`) | **Kommun-dimension saknas** (ADR 0043 Beslut E payload-trigger nu uppfylld) |
| Yrke: Yrkesområde→Yrkesgrupp (ssyk-level-4) | Filtrerar på occupation-NAME; taxonomi occupation-field→occupation-name | **Fel nivå** — Platsbanken = ssyk-level-4; vi = occupation-name |
| Filter: Omfattning | — | `worktime-extent`-dimension saknas |
| Filter: Anställningsform | — | `employment-type`-dimension saknas |
| Filter: Distans | — | `work-place-model`-härledning saknas |
| Filter: Publicerad | `Since`/`IsNew` finns (badge) | Behöver bli filter (date-tröskel) |
| Live-count per val | totalCount finns (en query) | **Facetterad räkning saknas** (count per filter-option) |
| Sortering: Ansökningsdatum | har ExpiresAt-sort | mappning verifieras (ExpiresAt ≈ sista ansökningsdag?) |
| Svensk-specifika (Körkort/Utbildningskrav/Anställningsstöd/Anpassad) | — | härledning, troligen senare fas |

### 4.1 Recall-gap (TD-86 #1) — separat verifiering
"systemutvecklare" ~198 (JobbPilot) vs 800+ (Platsbanken) observerat 2026-05-23. Korpus är nu ~40k (screenshots visar Platsbanken 40 505; vår ~40k efter retention-konvergens). Hypotes: gap minskar när (a) korpus konvergerat och (b) yrkesgrupp-nivå-filter + korrekt occupation_group-mappning införs. Kräver discovery-mätning (JobTech `/search`-count vs lokal count för samma kriterium) — egen verifierings-punkt, ej blockerande för data-layer-design.

---

## 5. Bärande ADR-constraints (får ej brytas utan CTO-dom)
- **ADR 0062:** FTS-hybrid + `IJobAdSearchQuery`-port; Npgsql-förbud i Application; filter-SPOT delas av 3 konsumenter (kompilator-garanti mot divergens).
- **ADR 0043:** taxonomi-ACL — concept-id aldrig i UI; lokal snapshot, aldrig live-API på sök-väg; `ITaxonomyReadModel`-port; `IAppDbContext` växer ej; Beslut E payload-verifierings-trigger för Kommun (nu uppfylld → **separat förhandlad batch + ADR 0043-amendment, EJ autonom, Klas-GO**). Snapshot-dedup: occupation-name kan tillhöra flera fält → kanoniskt dedupliserad single-parent.
- **ADR 0042:** multi-värde `IReadOnlyList<string>` + 4 invarianter (sorterad+distinct-normalisering, MaxConceptIds-cap, tom-invariant, jsonb-bakåtkompat); kollaps-filter; Relevance kräver q.
- **ADR 0039:** SavedSearch `SearchCriteria`-VO + jsonb-dedupe; SortBy i VO. Nya filter-dimensioner i SearchCriteria → jsonb-shape + dedupe-invariant + bakåtkompat påverkas.
- **ADR 0032:** ingest/sanitizer-allowlist; nya raw_payload-keys (occupation_group, municipality_concept_id, employment_type, working_hours_type) måste passera sanitizer-allowlist för att generated columns ska få värde.
- **ADR 0049/0066:** raw_payload generated-columns/Art.17-mekanik orörd.

---

## 6. Multi-approach-frågor till senior-cto-advisor (CC ger ingen egen rek)

1. **Yrke-nivå (störst):** matcha Platsbanken = filtrera på `occupation_group` (ssyk-level-4)? Alternativ: (A) byt primär yrke-filter occupation-name→ssyk-level-4 + ändra taxonomi-träd occupation-field→ssyk-level-4 (behåll occupation-name endast för synonym/typeahead); (B) stötta båda nivåer (occupation-field→ssyk-level-4→occupation-name, filtrera på vald nivå); (C) behåll occupation-name + lägg ssyk-level-4 additivt. Påverkar ADR 0043-modellen + snapshot + `ssyk_concept_id`-semantiken.
2. **Datamodell nya dimensioner:** STORED generated columns (analogt ssyk/region) för `municipality_concept_id`, `occupation_group_concept_id`, `employment_type_concept_id`, `worktime_extent_concept_id`, ev. `work_place_model_concept_id` — vs raw_payload-query. Per-dimension index-strategi.
3. **Kommun-hierarki:** Län→Kommun i taxonomi-träd (ny `TaxonomyConceptKind.Municipality` + parent=region) + filter-kaskad. ADR 0043-amendment.
4. **Distans:** `work-place-model` ej toppfält → härledning. Finns det på annons via relation/annan key, eller text-heuristik, eller defer? Platsbanken har enkel "Möjligt till distansarbete"-checkbox.
5. **Live facet-counts:** arkitektur för "Visa N annonser" + ev. per-option-count. En aggregerad count-query (GROUP BY) vs N counts vs defer per-option (bara total). Perf mot ADR 0045-budget + faceted-count-kostnad på 40k rader.
6. **Smart fritext-Sök / query-token-parser (TD-86 #3):** "systemutvecklare göteborg heltid" → {yrke, ort, omfattning}. Parsed tokens som ADDITIVA filter (recall-bevarande) vs hårda AND (precision). Hur kombineras med FTS-hybrid utan recall-tapp. Svåraste frågan — ev. Klas-STOPP om strategisk.
7. **Filter i SearchCriteria-VO?** Nya dimensioner i sparad sökning (ADR 0039 jsonb-dedupe + bakåtkompat) vs endast runtime-query.
8. **Fas-/PR-uppdelning** för hela initiativet.

## 7. Föreslagen fas-skiss (CTO justerar)
- **Fas A (denna session):** design + ADR. (KLAS-STOPP Accepted.)
- **Fas B:** data-layer — nya STORED-kolumner + EF-config + migration (Testcontainers-verifierad) + taxonomi-snapshot-utökning (kommun + ssyk-level-4) + seeder.
- **Fas C:** query/filter-layer — utöka filter-SPOT + ApplyCriteria + ListJobAdsQuery + ITaxonomyReadModel + validators; integration-tester.
- **Fas D:** facet-counts + smart fritext-Sök (query-token-parser).
- **Fas E:** FE-UI — Ort/Yrke-kaskad + Filter-panel + live-count + Rensa-länkar + ny färg-identitet (design-reviewer + Klas-GO).
- **Tvärgående:** recall-gap-verifiering (TD-86 #1).
