# ADR 0067 — Sök-paritet med Platsbanken: yrkesgrupp-nivå-skifte, nya filter-dimensioner, facet-counts och typeahead-chip-sök

**Datum:** 2026-06-08
**Status:** Accepted 2026-06-08 (Klas-GO STOPP — Klas valde explicit Accepted-skrivning efter granskning av architect- + CTO-rapporterna 2026-06-08)
**Beslutsfattare:** Klas Olsson (produktägare)
**Relaterad:** [ADR 0062](./0062-fts-hybrid-search-and-infrastructure-query-port.md) (FTS-hybrid + `IJobAdSearchQuery`-port-SPOT — bevaras + utökas), [ADR 0043](./0043-taxonomy-acl-for-search-surface.md) (taxonomi-ACL — **amendas 2026-06-08:** kommun-dimension + yrkesgrupp-nivå-skifte, se ADR 0043-amendment), [ADR 0042](./0042-search-surface-information-architecture.md) (sök-yta-IA — Beslut B multi-värde-invarianter tillämpas; Beslut C typeahead-källa utökas; Beslut E presentation-vs-VO; Beslut F CV-matchning-out), [ADR 0039](./0039-savedsearch-aggregate-and-query-run-semantics.md) (SearchCriteria-VO + jsonb-dedupe — VO-expansion), [ADR 0040](./0040-smart-cv-derived-saved-search.md) (Smart CV-härlett filter — Q1 occupation-name-substrat korsref, CV-parsing-nivå reserverad Fas 4), [ADR 0045](./0045-performance-budget-and-fitness-functions.md) (perf-budget — read-query 300 ms p95, facet-count NBomber-gate), [ADR 0032](./0032-jobtech-integration.md) (JobTech-integration — POCO/sanitizer-allowlist + re-ingest), [ADR 0060](./0060-recent-job-searches-auto-capture.md) (N+1-count-cap), [ADR 0064](./0064-landing-stats-precompute.md) (precompute-cache-precedens vid perf-brott). TD-86 (sök/filter-hardening — **ERSATT/absorberad** av detta initiativ), TD-100 (yrkesfilter-UI-paritet — Fas E acceptance criteria), TD-93 (CV-matchning — occupation-name-substrat-korsref). CLAUDE.md §1 (civic-utility), §2.1/§2.3/§2.5 (lager/CQRS/perf-dom), §9.6 (in-block vs TD).

> **Livscykel-/proveniens-not:** Skriven 2026-06-08 av Claude Code (adr-keeper-disciplin) på explicit Klas-direktiv i Platsbanken-sök-paritets-startprompten ("ADR skrivs") + Klas val "Skriv som Accepted" (AskUserQuestion 2026-06-08) — medveten override av CLAUDE.md §9.4 webb-Claude-verbatim-konventionen (memory `feedback_klas_can_override_adr_verbatim_source`). Besluts-substansen är grundad i agent-domar 2026-06-08: dotnet-architect (Clean Arch/DDD-design + Klass1/Klass2-payload-korrigering — `docs/reviews/2026-06-08-sok-paritet-architect.md`), senior-cto-advisor (decision-maker, 8 multi-approach-domar + Q1/Q6-omdom efter Klas-svar — `docs/reviews/2026-06-08-sok-paritet-cto.md` + `-cto-followup.md`), discovery (`docs/research/2026-06-08-platsbanken-sok-paritet-discovery.md`). Klas-flaggade produktbeslut (Q1 CV-roadmap, Q4 distans, Q6 sök-semantik) besvarade av Klas 2026-06-08. Samma proveniens-mönster som ADR 0062 rad 9 / ADR 0050. Status **Accepted** per Klas explicit-val; Klas granskar prosan post-hoc per automerge-policy (ADR 0065).

---

## Kontext

JobbPilots `/jobb`-sökyta (ADR 0042/0043/0062) når inte 100% paritet med Platsbanken (arbetsformedlingen.se/platsbanken). Klas-direktiv 2026-06-08: matcha Platsbankens sök/filter/sortering till 100% (Län→Kommun, Yrkesområde→Yrke, Omfattning, Anställningsform) + en smart fritext-sök som kombinerar kriterier utan att tappa relevanta annonser.

Discovery 2026-06-08 (Klas-screenshots av Platsbankens UI + JobTech Taxonomy/JobSearch live-läsning + kodbas-kartläggning, fullständigt i `docs/research/2026-06-08-platsbanken-sok-paritet-discovery.md`) avtäckte paritets-gapet och **en fundamental nivå-avvikelse**:

- **Platsbankens "Yrke"-picker filtrerar på `ssyk-level-4` (yrkesgrupp, ~400)**, inte `occupation-name` (yrke, ~2179). Bevis: andra kolumnen visar SSYK-4-gruppetiketter ("Revisorer m.fl.", "IT-säkerhetsspecialister", "Övriga ekonomer"). JobbPilot filtrerar idag på `ssyk_concept_id` = **occupation-name-nivå** (ADR 0043 Beslut E). Det är fel dimension mot paritets-målet.
- **Kommun saknas** — JobbPilot har endast länsnivå (`region_concept_id`). ADR 0043 Beslut E sköt upp kommun pending en payload-verifierings-trigger; trigger-villkoren (payload-bekräftelse + användarsignal) är nu båda uppfyllda (`municipalityconceptid` finns i payloaden; Klas-direktivet är användarsignalen).
- **Omfattning, anställningsform, distans** är BUILD.md §2.1-scope men ej queryable. Distans är inte ett tillförlitligt payload-fält (måste härledas).
- **Live facet-count** ("Visa N annonser" per filterval) saknas.
- **Smart fritext-sök** (TD-86 query-token-parser) ej byggd. Klas-modell: typeahead-driven token/chip-komponist — tabba-komplettera allt i filtreringen (Län/Kommun/Yrkesgrupp/Yrke/Anställningsform) + ren fritext-fallback som aldrig kraschar sökningen.
- **Recall-gap** (TD-86 #1): "systemutvecklare" ~198 lokalt vs 800+ på Platsbanken — diskvalificerande för publik launch, rotorsak overifierad.

Detta är ett stort, fler-fasigt initiativ. Denna ADR är design-grindens beslut (Fas A); implementation levereras i efterföljande faser med PR per fas. Besluten är fattade av Klas + senior-cto-advisor (decision-maker) + dotnet-architect (Clean Arch-inramning) 2026-06-08; denna ADR strukturerar dem.

Bärande on-disk-korrigering (architect 2026-06-08): JobTech-payload-fälten delar sig i **två klasser**. **Klass 1** (`municipality_concept_id`, `occupation_group`) deserialiseras redan av `JobTechHit`-POCO:n och finns i `raw_payload` → STORED-kolumn populeras utan re-ingest. **Klass 2** (`employment_type`, `working_hours_type`) deserialiseras INTE av POCO:n → saknas i `raw_payload` → STORED-kolumn ger NULL tills POCO-tillägg + full re-ingest. Denna skillnad styr fas-sekvensen.

## Beslut

### Beslut 1 — Yrke-filter byter nivå occupation-name → ssyk-level-4 (Option A); occupation-name-substratet bevaras

Det primära yrke-filtret i paritets-UI:t blir **`ssyk-level-4` (yrkesgrupp)** via en ny STORED generated column `occupation_group_concept_id ← raw_payload->'occupation_group'->>'concept_id'` (Klass 1 — payload finns). Pickern går occupation-field → ssyk-level-4 (exakt Platsbankens två-nivå-yta; ingen tredje occupation-name-nivå i UI:t).

`ssyk_concept_id` (occupation-name) **bevaras** — kolumn + partial-B-tree-index finns redan on-disk och raderas inte. Den **byter roll** till (a) synonym-/recall-input på FTS-fritext-vägen (befintlig `IOccupationSynonymExpander`) och (b) **queryable substrat för framtida CV-matchning utan AI** (TD-93/ADR 0040). Retain-beslutet är kostnadsfritt (ingen migration adderar dem).

`JobAdFilterCriteria`-formen förblir `IReadOnlyList<string>` concept-id (strängar, ej strongly-typed VO) → EF Core 10 + Npgsql `Contains`-translation-fällan (`feedback_ef_strongly_typed_vo_contains_translation`) undviks. Endast filter-targets shadow-kolumn ändras.

**Reserverat till TD-93/ADR 0040 (ingen design här):** om CV-parsing mappar CV-yrken till ssyk-level-4 eller occupation-name eller båda. Option A:s nivå-val gör yrkesgrupp-matchning till det naturliga default-spåret (CV-sökning matchar då samma ubiquitous language som paritets-UI:t — Evans 2003 kap. 2/14), men det är en observation som informerar TD-93, inte ett låst beslut. Ingen UI-placeholder för CV-matchning (ADR 0042 Beslut F-disciplin).

**Bakåtkompat (Klas-GO-bärande):** gamla sparade sökningar i `saved_searches.criteria` bär occupation-name-concept-ids i `Ssyk`-listan. Nivåbytet kräver en **reverse-lookup-migration** (occupation-name → parent ssyk-level-4 via taxonomins `broader`-relation, deterministisk single-parent, distinct-normaliseras av ADR 0042 Beslut B invariant 1). Vald framför graceful degradation: tysta noll-träffar på en sparad bevakning vars syfte är att köras är värre än en synlig "Okänd kod"-label (CLAUDE.md §1). Levereras i Fas C2 tillsammans med VO-expansionen (Beslut 6).

**Avvisat:** Option B (occupation-name som exponerad UI-filterdimension nu) — Speculative Generality (Fowler 2018 kap. 3), bryter Platsbanken-paritet (tredje nivå Platsbanken saknar), föregriper ADR 0040 Beslut 3; Klas CV-roadmap pekar mot ssyk-level-4-matchning, inte mot B. Option C (occupation-name + ssyk-level-4 som konkurrerande UI-filter) — semantiskt oklart, paritets-/ubiquitous-language-brott.

### Beslut 2 — Nya filter-dimensioner via STORED generated columns; Klass 1 före Klass 2

Alla filtrerbara taxonomi-dimensioner modelleras som **STORED generated columns** med partial B-tree-index `WHERE <col> IS NOT NULL` — exakt ssyk/region-precedensen (F2P9) + search_vector (ADR 0062). Inline `raw_payload->>...`-query för filtrerbara dimensioner avvisas: ad-hoc jsonb-extraktion mot ~40k rader bryter sannolikt ADR 0045 read-query-budget (300 ms p95) och facetterad räkning blir ohållbar (CLAUDE.md §2.5).

**Sekvens (hård regel, styrd av payload-tillgänglighet):**
- **Klass 1 — `municipality_concept_id`, `occupation_group_concept_id`:** POCO + sanitizer-allowlist + payload på plats → STORED ADD COLUMN populerar från befintlig `raw_payload` **utan re-ingest**. Levereras i Fas B1.
- **Klass 2 — `employment_type_concept_id`, `worktime_extent_concept_id`:** `JobTechHit`-POCO:n deserialiserar dem inte → `raw_payload` saknar keys → STORED-kolumnen blir NULL för alla ~40k rader tills (1) POCO-tillägg (`JobTechEmploymentType`/`JobTechWorktimeExtent` + `[JsonPropertyName]`), (2) sanitizer-allowlist-verifiering, (3) STORED-kolumn, (4) **full re-ingest via snapshot-cron**. Att klumpa Klass 2 med Klass 1 döljer NULL-tillståndet = "falsk klar". Levereras i Fas B2 med explicit DoD att kolumnen är NULL tills cron kört.

Varje STORED-kolumn-migration mot `job_ads` (~40k rader) verifieras mot **Testcontainers Postgres** (ej InMemory — STORED-omberäkning + VO-Contains-fällan). Skriv-overhead (STORED-omberäkning vid ingest) är ej hot-path (snapshot-cron) — samma trade-off som ADR 0061/0062 GIN.

### Beslut 3 — Distans (work-place-model) defereras med payload-verifierings-trigger

`work-place-model` (Distans/Hybrid/På plats) är inte ett toppfält i JobSearch AdFields och inte en POCO-prop — distans måste härledas. Alla härlednings-vägar är svaga: relation-lookup = extern hop (bryter ADR 0042 rad 21 + ADR 0043 ACL); annan payload-key = overifierad; text-heuristik (`description ILIKE '%distans%'`) = låg precision som förorenar FTS-relevansen (ADR 0062). Klas-bekräftat 2026-06-08: **defer**.

Distans revisitas via en **payload-verifierings-trigger** (ej TD — overifierad extern datakälle-dependency, exakt ADR 0043 Beslut E:s instrument per CLAUDE.md §9.6): riktad raw_payload-prov-discovery bekräftar en stabil distans-key → Klass 2-behandling (POCO + re-ingest). Sker först när kärn-dimensionerna (yrke/ort/anställningsform/omfattning) gett paritet. Att jaga en overifierad härledning nu vore att riskera FTS-förorening eller ADR-brott för en enda checkbox (civic-utility: pålitlighet > täckning, CLAUDE.md §1).

### Beslut 4 — Facet-counts: total nu, per-option via ny port-metod (NBomber-gate)

**Total-count** ("Visa N annonser" för aktuellt filter) lever redan: `IJobAdSearchQuery.SearchAsync` kör en separat `CountAsync`, och `CountAsync`-porten finns. Levereras utan ny arkitektur.

**Per-option-count** ("Mönsterås (12)", "Mörbylånga (34)") implementeras som en **ny metod på `IJobAdSearchQuery`** (`FacetCountsAsync(JobAdFilterCriteria, FacetDimension) → IReadOnlyDictionary<string,int>`), **inte** en ny port. Mekanik: GROUP BY-aggregat per dimension med den facetterade dimensionen exkluderad ur WHERE (facett-semantik — count för dimension X reflekterar alla andra aktiva filter men inte X självt). En ny port skulle duplicera `ApplyCriteria`-filtret → SPOT-brott (ADR 0062 Beslut 3); en ny metod återanvänder filter-SPOT:en och faller under ADR 0062 Beslut 4:s provider-assembly-axel (GROUP BY-LINQ ⊂ Npgsql ⊂ Infrastructure).

Per-option-count är en **ny omätt hot-path** mot ~40k rader. **NBomber-mätning mot ADR 0045 (300 ms p95) är BLOCKING före per-option går live** (CLAUDE.md §2.5). Vid budget-brott: fallback till en precompute-/cache-strategi (ADR 0064-analog) — då blir det en perf-/kostnadsfråga som eskalerar till Klas (ADR 0045 Klas-lås). Facett-exkluderings-semantiken specas explicit i Fas D1 (annars fel siffror vs Platsbanken). Levereras i Fas D1.

**Avvisat:** N separata counts (N+1-explosion, ADR 0060 cappade redan vid 20); full defer av per-option (kärn-UX-krav, Platsbanken visar count på varje val).

### Beslut 5 — Smart fritext-sök: typeahead-chip-komponist + residual-FTS, över tre lager

Klas-modell (2026-06-08): användaren skriver "systemu" → typeahead-förslag → tabbar klart → strukturerat sök-chip; "göte" → "Göteborg" → tabbar klart. Man tabb-kompletterar **allt som finns i filtreringen** (Län/Kommun/Yrkesgrupp/Yrke/Anställningsform) OCH skriver ren fritext som inte finns i filtreringen (t.ex. "AI engineer") utan att sökningen kraschar. Tre samverkande delar i tre lager:

**(a) Utökad typeahead-suggest-källa (Fas D1).** ADR 0042 Beslut C:s `SuggestJobAdTermsQuery` (job_ads.Title ILIKE-prefix) **utökas** till en **union: (i) taxonomi-snapshot-labels** (via `ITaxonomyReadModel`, in-memory, ADR 0043) **+ (ii) job_ads-titel-prefix** (ADR 0042 Beslut C, oförändrad). Suggest returnerar `{kind, conceptId, label}` per förslag. Taxonomi-delen är in-memory-snapshot-prefix → bryter EJ ADR 0043:s extern-hop-förbud på sök-vägen. Detta är en **additiv utökning av ADR 0042 Beslut C** (korsref, ej supersession — job_ads-titel-vägen består). Read-query, ingen ny port; rate-limit per ADR 0042 Beslut C `SuggestPolicy`-mönster.

**(b) "Tabba-klart → chip" = FE-state (Fas E).** Ett tabbat förslag blir ett strukturerat filter-chip (FE känner `{kind, conceptId}`) → ingen parsning. Chip-state bor i FE (React, ADR 0042 Beslut A kollaps-/chip-yta). Backend tar emot redan-strukturerade filter-listor i `JobAdSearchCriteria` (Ssyk/Region/Municipality/OccupationGroup/EmploymentType) — exakt Fas C-dimensionerna.

**(c) Residual ren fritext = `ISearchQueryParser` (Application) + FTS-Q (Fas D2).** Det som inte tabbades till ett chip → `ResidualQ` → FTS-hybrid (ADR 0062). Parsern bor i **Application bakom porten `ISearchQueryParser`** (ren CPU, ingen Npgsql — Martin 2017 kap. 22), byggd som generalisering av `IOccupationSynonymExpander`-ACL-mönstret (Evans 2003 kap. 14, DRY). Kontrakt: `Parse(string) → ParsedSearchQuery(SsykConceptIds, RegionConceptIds, EmploymentTypeConceptIds, ResidualQ)`. Parsern körs på residual-strängen, inte hela råsökningen — disambiguering sker vid input (användarstyrt) snarare än via gissande backend.

**Kraschsäkerhet (Klas-krav) by design:** residual-Q går alltid till FTS-hybrid som recall-bevarande OR-term, aldrig som hård AND-exkludering. En fritext som inte matchar någon dimension är bara en FTS-sökterm — den kan ge noll träffar men kan aldrig krascha eller tomfiltrera sökningen.

**Kombinationssemantik (mildrad Klas-STOPP, Fas D2):** chips = AND-mellan-dimensioner / OR-inom-dimension (ADR 0042-invariant, redan låst); residual-Q = recall-bevarande FTS-term inom FTS-hybrid (ADR 0062). Session 1:s additiv-vs-AND-fråga mildras eftersom chip-strukturering är medveten; endast chip/residual-kombinationen bekräftas av Klas i Fas D2.

### Beslut 6 — VO-expansion: nya dimensioner i SearchCriteria; facets/tokens runtime

Per ADR 0039 Beslut 3-testet ("del av sökningens identitet?" → VO; "runtime-presentation?" → query-fält):
- **VO (sparas i `SearchCriteria`):** occupation_group, municipality, employment_type, worktime_extent (work_place_model om/när levererad). Var och en bär ADR 0042 Beslut B:s fyra invarianter (sorterad+distinct-normalisering för record-equality/jsonb-dedupe, `MaxConceptIds`-cap, tom-invariant, jsonb-bakåtkompat via tolerant converter).
- **Runtime (ej VO):** facet-counts, parsade tokens (rå `Q` sparas; parsning sker per körning, fryses aldrig i jsonb).

VO-expansion är en egen testbar batch (test-writer FÖRST, security-auditor för cap) och designas **tillsammans med** Beslut 1:s reverse-lookup-migration (Fas C2) eftersom båda rör samma jsonb-bakåtkompat-yta. Saknat jsonb-fält i gamla rader → tom lista (befintligt tolerant-converter-mönster, ADR 0042 implementerings-notat 2026-05-17 Yta A3).

### Beslut 7 — Initiativet levereras i faser; Fas A (denna ADR) är Session 1:s enda leverans

| Fas | Innehåll | Klas-GO |
|---|---|---|
| **A** | Design + ADR 0067 + ADR 0043-amendment (denna leverans) | Accepted-flip = Klas (utförd) |
| **B1** | Klass 1 STORED (`municipality_concept_id` + `occupation_group_concept_id`) + EF-config + migration (Testcontainers) + taxonomi-snapshot-utökning (kommun-noder + ssyk-level-4-noder) + seeder. Ingen re-ingest. | JA (40k-migration + ADR 0043-amendment) |
| **B2** | Klass 2 STORED (`employment_type` + `worktime_extent`) — `JobTechHit`-POCO-tillägg + allowlist + migration + full re-ingest (NULL tills cron, explicit i DoD) | JA (POCO + re-ingest) |
| **C1** | Runtime-query: utöka filter-SPOT (`JobAdFilterCriteria`) + `ApplyCriteria` + `ListJobAdsQuery`/Validator + `ITaxonomyReadModel`-DTO (kommun + ssyk-level-4) + integration-tester | JA (Beslut 1 nivåbyte) |
| **C2** | `SearchCriteria`-VO-expansion + Beslut 1 reverse-lookup-migration + jsonb-bakåtkompat (test-writer FÖRST, security-auditor cap) | JA (sparad-sökning-migration) |
| **D1** | Total-count (trivialt) + per-option facet-counts (`FacetCountsAsync`, NBomber FÖRE) + utökad typeahead-suggest (taxonomi-union) | NBomber-utfall kan eskalera |
| **D2** | `ISearchQueryParser` för residual-fritext — chip-AND/residual-FTS-semantik = Klas-STOPP | JA (Klas-STOPP) |
| **E** | FE-UI: Ort/Yrke-kaskad + Filter-panel + live-count + Rensa-textlänkar + typeahead-chip-komponist + ny färg-identitet | design-reviewer VETO + Klas-GO |
| **Tvärgående** | Recall-gap-mätning (TD-86 #1) efter B2+C1 | — |

Session 1 levererar **endast Fas A** (CLAUDE.md §9.2 — fas-skifte till kod-mot-40k-data kräver explicit Klas-GO; design-grinden stänger med ADR:er, ej migrationer). Fas B1-start = nästa session efter Klas läst ADR:erna.

**FE-paritets-detaljer (Fas E, från Klas-referens):** två-kolumns kaskad-pickers (Ort: Län→Kommun med "Välj alla kommuner" + Obestämd ort/Utomlands; Yrke: Yrkesområde→Yrkesgrupp); Filter-panel (Omfattning radio, Anställningsform checkbox-multi, Publicerad radio — befintlig `Since`/`IsNew`); live-count "Visa N annonser" per val; **Rensa** som röd text-länk per sektion (ej knapp); **ny färg-identitet** (mörkgrön-riktning, ej blå-vit — Platsbanken-inspirerad men ej kopierad; DESIGN.md-token-arbete, design-reviewer + Klas-GO). Sortering: Relevans / Datum (publicering) / Ansökningsdatum (ExpiresAt-mappning verifieras). Svensk-specifika filter (Körkort/Utbildningskrav/Anställningsstöd/Anpassad arbetsplats) = senare fas, ej paritets-kärna.

### Implementerings-notat 2026-06-09 (Fas C2) — VO-expansionens leverans-sekvens + reverse-lookup-mekanik

**Källa:** senior-cto-advisor-dom (a)–(f) 2026-06-09 (`docs/reviews/2026-06-09-sok-paritet-c2-cto.md`) + dotnet-architect F1–F8 (`-c2-architect.md`). Additivt notat — Beslut 1–7-brödtexten är orörd (ADR-immutabilitet). Notatet dokumenterar implementation av redan Accepted-beslut; inga nya arkitekturval.

- **Beslut 6-sekvensering (CTO (a)):** C2 levererar `SearchCriteria.OccupationGroup` + `.Municipality`. **EmploymentType/WorktimeExtent-VO-fälten följer sin query-wiring-touch** (post re-ingest, D1-grannskapet) — samma data-tillgänglighets-sekvensering som C1-CTO-dom (c) ("falsk klar"-disciplin: VO-fält utan ApplyCriteria-gren/query-param/data vore tyst-noll-träff-bevakningar eller död Domain-kod). Klassificeringen i Beslut 6 (alla fyra ÄR VO-dimensioner) omprövas inte — endast leveransen sekvenseras.
- **Beslut 1 reverse-lookup-mekanik (CTO (b)/(c)):** mappningskälla = taxonomins `broader`-relation (live-verifierad 2026-06-09: 2179/2179 occupation-names har exakt 1 ssyk-level-4-parent — Beslut 1:s determinism-antagande håller, ingen amendment). Mekanism = eager EF-data-migration (`C2SearchParityReverseLookupAndRecentExpansion`) med **frusen migration-ägd embedded resource** (`occupation-name-to-ssyk-level-4.v30.json`, one-shot-genererad, regenereras aldrig — migrations-immutabilitet); set-baserad jsonb-transform med nyckel-existens-predikat, COLLATE "C"-sorterad lagrad form, fail-loud vid omappbara ids, dokumenterat lossy `Down()`. Lazy on-read + runner-jobb avvisade (CTO (c)).
- **Occupation-name-dimensionen avvecklad ur sök-identiteten (CTO (e)/(f)):** `Ssyk` utgick ur `SearchCriteria`-VO:t, `JobAdFilterCriteria`, `ListJobAdsQuery`/validator/endpoint-param, `ICapturesRecentSearch` och Create/Update-commands. occupation-name-SUBSTRATET (job_ads.ssyk_concept_id + synonym-q-vägen) är orört per Beslut 1. `SearchCriteriaJsonConverter` fail-loud:ar på legacy-`"Ssyk"`-nyckel (aldrig tyst Skip).
- **RecentJobSearch-expansion i C2 (CTO (d)):** `_ssyk` → `_occupationGroup` + `_municipality`; `FilterHashCalculator` ny canonical-JSON (`{"q","occupationGroup","municipality","region","sortBy"}`); befintliga recent-rader RADERADES i migrationen (cache-data utan audit-trail-värdighet; ingen hash-versionering — YAGNI).
- **Wire-kontrakt-shim (architect F5):** `RecentJobSearchDto.SsykList`/`SsykLabels` behålls deprecated alltid-tomma (FE-zod kräver `ssykList`; C2 rör inte FE); nya fält sist. Tas bort i Fas E med FE-picker-bytet (?ssyk= → ?occupationGroup=).

## Konsekvenser

### Positiva
- **100% Platsbanken-paritet** på yrke-nivå (ssyk-level-4), ort (Län→Kommun), omfattning, anställningsform + live-count + smart typeahead-chip-sök.
- **ADR 0043-dedup-skuld krymper** — yrke-trädet går occupation-field→ssyk-level-4 (~400 noder, nästan 1:1 parent-relation) i stället för occupation-field→occupation-name (~2179 noder, 359 multi-field-par som krävde deterministisk kanonisering). Beslut 1 betalar ner befintlig design-skuld (Evans kap. 14).
- **Filter-SPOT bevaras och stärks** — alla nya dimensioner går via `JobAdFilterCriteria` (ADR 0062 Beslut 3); facet-counts via ny metod på samma port, ej ny port.
- **CV-matchning (TD-93/ADR 0040) får ett kostnadsfritt queryable substrat** — occupation-name-kolumnen bevaras.
- **Smart-sök är kraschsäker by design** — residual-fritext går alltid till FTS som recall-bevarande term.
- **TD-86 absorberas in-fas** i stället för att kvarstå som paraply-skuld.

### Negativa / accepterade trade-offs
- **Nya STORED-kolumner på `job_ads` (~40k)** + reverse-lookup-migration av sparade sökningar = blast-radius mot ADR 0032-migrationsyta + ADR 0043 "shadow-prop ORÖRD"-garanti (häves medvetet via ADR 0043-amendment) + ADR 0039/0042 jsonb-bakåtkompat. Mitigering: fas-split (B1/B2/C1/C2), Testcontainers-verifiering, test-writer FÖRST.
- **Per-option facet-counts = ny hot-path** mot 40k. Mitigering: NBomber-gate (ADR 0045), cache-fallback (ADR 0064).
- **Klass 2-dimensioner kräver full re-ingest** innan de är populerade. Mitigering: explicit i Fas B2 DoD.
- **Distans levereras inte** i paritets-kärnan. Mitigering: payload-verifierings-trigger; civic-utility prioriterar pålitlighet > täckning.
- **Yrke-nivåbytet skapar en bakåtkompat-händelse** för gamla sparade sökningar. Mitigering: reverse-lookup-migration (ej tyst degradering).

## Alternativ som övervägdes

Fullständig optionsanalys i `docs/reviews/2026-06-08-sok-paritet-architect.md` + `-cto.md` + `-cto-followup.md`. Sammanfattning:
- **Yrke-nivå:** Option B (båda nivåer) avvisad — Speculative Generality, paritets-brott, föregriper ADR 0040. Option C (additivt) avvisad — semantiskt oklart.
- **Datamodell:** raw_payload-query för filtrerbara dimensioner avvisad — ADR 0045-budgetbrott. Klass 1+2 i en migration avvisad — "falsk klar".
- **Kommun:** separat `Level`-int avvisad — redundant med `Kind` (DRY-brott).
- **Distans:** relation-lookup (extern hop) + text-heuristik (FTS-förorening) avvisade.
- **Facet-counts:** ny port avvisad (SPOT-brott); N separata counts avvisade (N+1); full defer avvisad (kärn-UX).
- **ADR-struktur:** split i flera ADR:er avvisad — REP/CCP-brott, cross-ref-spindelnät (ADR 0045-precedens).

## Implementation

Se Beslut 7-fastabellen. Fas A (denna ADR + ADR 0043-amendment + agent-rapporter + TD-86-split + TD-100/TD-93-korsref + docs-sync) levereras i en PR (ADR 0065-flöde). Efterföljande faser: egna PR:er, Klas-GO per fas-skifte.

**Agent-domar (verbatim i docs/reviews/):** dotnet-architect 2026-06-08 (`a...`), senior-cto-advisor 2026-06-08 (Session 1 + Q1/Q6-omdom).

## Referenser

- Eric Evans, *Domain-Driven Design* (2003) — kap. 2 (ubiquitous language), 5 (Value Objects), 14 (Anticorruption Layer) — Beslut 1/5/6
- Robert C. Martin, *Clean Architecture* (2017) — kap. 8 (OCP), 13 (REP/CCP), 22 (lager) — Beslut 2/4/5, ADR-struktur
- Martin Fowler, *Refactoring* 2nd ed (2018) kap. 3 (Speculative Generality) + Introduce Parameter Object — Beslut 1 (Option B-avvisning), filter-SPOT
- Kent Beck (XP) — YAGNI — Beslut 1/3
- Ford/Parsons/Kua, *Building Evolutionary Architectures* (2017) kap. 2/4 — fitness functions / blast-radius — Beslut 4/7
- Hunt/Thomas, *The Pragmatic Programmer* (1999) — DRY/SPOT — Beslut 2/4/5
- Microsoft Learn — PostgreSQL generated columns, CQRS — Beslut 2
- [JobTech Taxonomy API](https://taxonomy.api.jobtechdev.se/) + [JobSearch AdFields](https://gitlab.com/arbetsformedlingen/job-ads/jobsearch/jobsearch-api/-/blob/main/docs/AdFields.md) — taxonomi-typer + payload-fält (live-verifierat 2026-06-08)
- Discovery: `docs/research/2026-06-08-platsbanken-sok-paritet-discovery.md`
- Agent-domar: `docs/reviews/2026-06-08-sok-paritet-architect.md`, `-cto.md`, `-cto-followup.md`
- Relaterade ADR: 0032, 0039, 0040, 0042, 0043, 0045, 0060, 0062, 0064; TD-86, TD-93, TD-100; CLAUDE.md §1/§2.1/§2.3/§2.5/§9.2/§9.6

---

*ADR-index underhålls av docs-keeper. ADR 0067 fastställer Platsbanken-sök-paritets-initiativets design: yrke-filter-nivåskifte till ssyk-level-4 (occupation-name-substrat bevarat), nya STORED-dimensioner (kommun/yrkesgrupp/anställningsform/omfattning) i Klass 1/Klass 2-sekvens, distans-defer, facet-counts via port-metod, typeahead-chip-sök med residual-FTS, VO-expansion, och fas-uppdelning A–E. Kompletteras av ADR 0043-amendment 2026-06-08 (kommun-dimension + yrkesgrupp-nivå).*
