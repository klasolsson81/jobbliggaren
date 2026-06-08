# Arkitektur-design — Platsbanken sök-paritet (Session 1, dotnet-architect)

**Datum:** 2026-06-08
**Agent:** dotnet-architect (agentId `aaf605e54b7174deb`)
**Roll:** teknisk inramning + Clean Arch/DDD/perf-lins. Beslutar INTE multi-approach — ramar in + rekommenderar där principen är entydig. Allt **CTO-BESLUT** går till senior-cto-advisor.
**Underlag:** discovery (2026-06-08), ADR 0062/0043/0042/0039/0032, on-disk-verifiering 2026-06-08.

---

## 0. On-disk-verifiering + KRITISK KORRIGERING (Klass 1 vs Klass 2)

Briefens §3 stämmer i allt väsentligt. Verifierade påståenden: filter-värden är `string` (ej VO, VO-Contains-fällan gäller ej); `ssyk_concept_id` ← occupation-NAME; kommun följer region-mönstret; `occupation_group`/`occupation_field` är separata top-level POCO-props som serialiseras korrekt (redan i raw_payload för rader skrivna efter F6 P4-fixen 2026-05-20); `TaxonomyConceptKind` = Region/Occupation/OccupationField, single ParentConceptId.

**KRITISK KORRIGERING (ändrar Q2 + Q4 + Q8):** `JobTechHit`-POCO:n (`JobTechSearchResponse.cs:27-93`) deserialiserar **inte** `employment_type`, `working_hours_type`, `scope_of_work` eller någon work-place-model-key. Sanitizer-allowlisten kör på `JsonSerializer.Serialize(hit)` — och `hit` saknar dessa props → **`raw_payload` saknar dessa keys helt idag.**

**Två klasser av dimensioner:**
- **Klass 1 (payload finns):** `municipality_concept_id`, `occupation_group_concept_id` — POCO + allowlist + payload på plats. STORED-kolumn = exakt klon av ssyk/region-mönstret. **Ingen re-ingest** (ADD COLUMN populerar från befintlig raw_payload).
- **Klass 2 (payload saknas):** `employment_type`, `worktime_extent`, `work_place_model` — kräver FÖRST `JobTechHit`-POCO-tillägg + allowlist-verifiering, SEDAN STORED-kolumn, SEDAN **full re-ingest** för att populera ~40k rader (NULL tills snapshot-cron kört).

---

## 1. Yrke-nivå (Q1) — CTO-BESLUT

Platsbanken filtrerar yrke på `ssyk-level-4` (~400); vi på `occupation-name` (`ssyk_concept_id`, ~2179).

- **Option A — byt primärt yrke-filter occupation-name → ssyk-level-4.** Ny kolumn `occupation_group_concept_id` (Klass 1, payload finns). Träd occupation-field→ssyk-level-4 (krymper ~2179→~400 noder → ADR 0043-dedup-skuld nästan borta, 359 multi-par→nära 0). `ssyk_concept_id` degraderas till synonym/recall-input. **BRYTER ADR 0042 SearchCriteria-bakåtkompat:** gamla sparade `Ssyk`-listor bär occupation-name-ids → tysta noll-träffar om nivån byts. Kräver reverse-lookup-migration (occupation-name→parent ssyk-level-4) ELLER accepterad graceful degradation.
- **Option B — stötta båda nivåer (field→ssyk-level-4→occupation-name).** Båda kolumner + index. `TaxonomyConceptKind` 4 värden. Bevarar bakåtkompat. Högst komplexitet; tredje nivån avviker från Platsbankens två-nivå-yta (YAGNI/Speculative Generality om ej uttalat mål).
- **Option C — behåll occupation-name + ssyk-level-4 additivt.** Semantiskt oklart (två yrke-filter, UX-divergens mot Platsbanken). Lägst paritet.

**Architect-inramning:** Paritets-målet pekar mot **A**. B:s tredje nivå = kapacitet Platsbanken inte har. A:s enda allvarliga kostnad = SearchCriteria-bakåtkompat (tysta noll-träffar värre än "Okänd kod"-label — CTO väger). A förenklar ADR 0043-dedup (Evans kap. 14). `JobAdFilterCriteria` förblir `IReadOnlyList<string>` oavsett (VO-Contains-fällan undviks).

## 2. Datamodell nya dimensioner (Q2) — STORED vs raw_payload

**Architect-rek (entydig):** STORED generated columns för alla filtrerbara dimensioner — entydigt mot ssyk/region/search_vector-precedensen. Inline jsonb-query på facetterade counts mot 40k bryter sannolikt ADR 0045-budget. Partial B-tree `WHERE <col> IS NOT NULL` per dimension. Skriv-overhead (STORED-omberäkning vid ingest) = ej hot-path (snapshot-cron), samma trade-off som ADR 0061/0062 GIN. **CTO-sekvens-beslut:** Klass 1 nu (ingen re-ingest); Klass 2 egen sub-fas (POCO + re-ingest). Att klumpa ihop döljer att Klass 2-kolumner är NULL tills cron — falsk "klar".

## 3. Kommun-hierarki (Q3) — Architect-rek (entydig)

Single `ParentConceptId` räcker oavsett djup — varje nod har en parent; `Kind` ÄR diskriminatorn (ingen redundant `Level`-int). `LoadAsync`-grupperingen blir `Kind`-medveten (impl-ändring, ej modell-ändring). `TaxonomyConceptKind` += `Municipality` (parent=Region), `OccupationGroup` (parent=OccupationField, om Q1=A/B). DTO additivt: `TaxonomyRegionDto` += `IReadOnlyList<TaxonomyMunicipalityDto> Municipalities` (speglar OccupationField→Occupations). Port-signatur oförändrad. Kommun→län = 1:1 → ingen multi-parent-dedup. **Kräver ADR 0043-amendment + Klas-GO.**

## 4. Distans / work-place-model (Q4) — Architect-rek: DEFER

Ej payload-fält, ej POCO. (a) relation-lookup = extern hop, bryter ADR 0042 rad 21/ADR 0043 → avvisas. (b) annan payload-key = overifierad. (c) text-heuristik = låg precision, förorenar FTS. (d) defer. Rekommenderar **defer** — overifierad datakälla = exakt ADR 0043 Beslut E:s avvisningsgrund (YAGNI/Speculative Generality). Payload-verifierings-trigger i ADR-text (ej TD). Om Klas vill ha distans i leveransen → CTO-beslut (b)-discovery vs (c)-heuristik.

## 5. Live facet-counts (Q5) — Rek + CTO (A vs C)

**Total-count lever redan** (`SearchAsync` kör separat `CountAsync`; `IJobAdSearchQuery.CountAsync` finns) — leverera först, noll ny arkitektur. **Per-option-count saknas.** (A) GROUP BY-aggregat per dimension (med dimensionen exkluderad ur WHERE = facett-semantik) — N dimensioner = N aggregat-queries. (B) N separata counts = N+1-explosion, avvisas (ADR 0060 cappade redan vid 20). (C) defer per-option. **Architect-rek:** per-option = **ny metod på `IJobAdSearchQuery`** (`FacetCountsAsync`), EJ ny port — bevarar filter-SPOT (ADR 0062 Beslut 3) + faller under ADR 0062 Beslut 4 provider-assembly-axel. Ny omätt hot-path → NBomber innan (ADR 0045). CTO väger A vs C (defer tills total+UI mätt = YAGNI-konservativt). Facett-exkluderings-semantiken måste specas explicit (annars fel siffror vs Platsbanken).

## 6. Query-token-parser (Q6) — STRATEGISK, Klas-STOPP + CTO

**Architect-arkitektur (entydig):** parsern bor i **Application bakom `ISearchQueryParser`-port**, ren CPU-funktion (ingen Npgsql), byggd som **generalisering av `IOccupationSynonymExpander`-ACL-mönstret** (Evans kap. 14). Kontrakt: `Parse(string) → ParsedSearchQuery(SsykConceptIds, RegionConceptIds, EmploymentTypeConceptIds, ResidualQ)`. Handlern slår ihop parsade tokens + explicit filter → `JobAdSearchCriteria`. Infrastructure-porten oförändrad. **Additiv (OR, recall-bevarande) vs AND (precision)** = produktbeslut med direkt recall/precision-tradeoff som rör TD-86 #1 (recall-gap 198 vs 800+) → **Klas-STOPP + CTO**. Egen fas (D), efter dimensionerna parsern ska parsa till finns.

## 7. Filter i SearchCriteria-VO vs runtime (Q7) — Architect-rek (entydig)

Test: "del av sökningens identitet?" → VO; "runtime-presentation?" → query-fält. **VO:** occupation_group, municipality, employment_type, worktime_extent, work_place_model (när levererad) — alla sökningens identitet (ADR 0039 Beslut 3 / Evans kap. 5). **Runtime:** facet-counts, parsade tokens (rå Q sparas, parsning per körning). Varje VO-fält bär 4 kostnader (Equals/HashCode, normalisering, cap+regex, jsonb-bakåtkompat). VO-expansion = egen testbar batch (test-writer FÖRST), separat från data-layer. **Q1=A interagerar med Q7:** `Ssyk`-nivåbyte ÄR en VO-bakåtkompat-händelse — CTO ser dem ihop.

## 8. Fas-/PR-uppdelning (Q8) — Architect-justering

- **B→B1/B2:** B1 = Klass 1 (municipality+occupation_group, ingen re-ingest); B2 = Klass 2 (employment_type+worktime_extent — POCO-tillägg + re-ingest, kolumner NULL tills cron, explicit i DoD).
- **C→C1/C2:** C1 = runtime-query-väg; C2 = SearchCriteria-VO-expansion + jsonb-bakåtkompat (test-writer FÖRST, security-auditor cap).
- **D→D1/D2:** D1 = total-count (trivialt) + per-option facet-counts (CTO A-vs-C); D2 = query-parser (Klas-STOPP).
- **E:** FE oförändrad (kaskad + filter-panel + live-count + Rensa + färg-identitet; design-reviewer VETO + Klas-GO).
- **Tvärgående:** recall-gap-mätning (TD-86 #1) efter B2+C1.

### Blast-radius-flaggor
| Yta | Accepted-ADR-garanti | Kräver |
|---|---|---|
| Nya STORED-kolumner job_ads (40k) | ADR 0032 + ADR 0043 "shadow-prop ORÖRD"/Beslut E | Klas-GO + ADR 0043-amendment + Testcontainers |
| Kommun-dimension | ADR 0043 Beslut E payload-trigger (uppfylld) | Klas-GO + ADR 0043-amendment (ej autonom) |
| Yrke-nivå-byte (Q1=A) | ADR 0042 SearchCriteria-bakåtkompat + ADR 0043 träd | CTO + Klas-GO + migration-strategi gamla sparade sökningar |
| SearchCriteria-VO-expansion | ADR 0039 jsonb-dedupe + bakåtkompat | test-writer FÖRST + security-auditor (cap) |
| Per-option facet-counts | ADR 0045 perf (ny hot-path) | NBomber (CTO A-vs-C) |
| Query-token-parser | ADR 0062 FTS-semantik + TD-86 recall | Klas-STOPP + CTO (additiv-vs-AND) |
| `IJobAdSearchQuery` facet-metod | ADR 0062 Beslut 3 SPOT | Ny metod, ej ny port |

## Beslutskarta
| Q | Roll | Verdikt |
|---|---|---|
| Q1 Yrke-nivå | CTO-BESLUT | Paritet→A; kostnad=SearchCriteria-bakåtkompat; A förenklar dedup |
| Q2 Datamodell | Rek + CTO-sekvens | STORED för alla filtrerbara; Klass 1 nu, Klass 2 egen sub-fas (re-ingest) |
| Q3 Kommun | Rek (entydig) | Single-parent; Kind=diskriminator; DTO additivt; ADR 0043-amendment |
| Q4 Distans | Rek: DEFER | Overifierad payload (ADR 0043 Beslut E-grund) |
| Q5 Facets | Rek + CTO (A/C) | Total lever; per-option = ny metod ej ny port; NBomber innan |
| Q6 Parser | STRATEGISK Klas-STOPP+CTO | Application-port `ISearchQueryParser`; additiv-vs-AND = produktbeslut |
| Q7 VO vs runtime | Rek (entydig) | Yrke/ort/anställning/omfattning=VO; facets/tokens=runtime |
| Q8 Faser | Rek-justering | B→B1/B2, C→C1/C2, D→D1/D2 |

**Källor:** Martin *Clean Architecture* (2017) kap. 14, 22; Evans *DDD* (2003) kap. 5, 14; Vernon *IDDD* (2013) kap. 6; Fowler *Refactoring* 2nd (2018) kap. 3 + Introduce Parameter Object; Beck (YAGNI); Ford/Parsons/Kua (2017) kap. 4; Hunt/Thomas (DRY/SPOT); Microsoft Learn (generated columns, CQRS); ADR 0062/0043/0042/0039/0032/0045; CLAUDE.md §1/§2.1/§2.3/§2.5/§5.1/§9.6.
