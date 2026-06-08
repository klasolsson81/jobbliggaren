---
session: Platsbanken sök-paritet — Fas A (design-grind + ADR 0067 + ADR 0043-amendment)
datum: 2026-06-08
slug: platsbanken-sok-paritet-fas-a-adr
status: levererad — ADR 0067 Accepted + ADR 0043-amendment, Fas A-PR mot main (branch docs/sok-paritet-adr-0067)
bas-HEAD: cf482b8
branch: docs/sok-paritet-adr-0067
agenter:
  - Explore (kodbas-discovery sök/filter-arkitektur)
  - general-purpose (JobTech-taxonomi web-verifiering)
  - dotnet-architect aaf605e54b7174deb (Clean Arch-design + Klass1/Klass2-payload-korrigering)
  - senior-cto-advisor a2b9d182729075263 (decision-maker, 8 multi-approach-domar)
  - senior-cto-advisor afc84b4a8e6876650 (Q1/Q6-omdom efter Klas-svar)
commits:
  - "docs(decisions): ADR 0067 sök-paritet Accepted + ADR 0043-amendment + index"
  - "docs(reviews+research+tech-debt): agent-domar + discovery-brief + TD-86/100/93-triage"
  - "docs(sessions): Fas A docs-sync (current-work + session-log)"
---

# Session: Platsbanken sök-paritet — Fas A (design-grind + ADR)

Initiativ-start. Klas-direktiv: matcha Platsbankens sök/filter/sortering till 100%
(Län→Kommun, Yrkesområde→Yrke, Omfattning, Anställningsform) + smart fritext-sök som
kombinerar kriterier utan recall-tapp. Stort, fler-fasigt → PR per fas. Session 1 =
design-grind (Fas A): discovery + architect + CTO + ADR. INGEN kod.

## Mål
1. Discovery: nuläge (sök-SPOT, STORED-kolumner, taxonomi, ingestion, FE) + JobTech-taxonomi (web) + Platsbanken-referens (Klas-screenshots).
2. Design: dotnet-architect (Clean Arch/DDD) → senior-cto-advisor (multi-approach decision-maker).
3. ADR 0067 + ADR 0043-amendment. KLAS-STOPP Proposed→Accepted.

## Discovery-fynd

**Kodbas (Explore):** sök-SPOT `JobAdSearchQuery` (Infrastructure, ADR 0062-port). Queryable idag: q (FTS-hybrid + title-LIKE + SSYK-synonym), ssyk[] (occupation-NAME-nivå), region[] (länsnivå), sortBy, page, since. STORED-kolumner: ssyk_concept_id, region_concept_id, search_vector. Filter = `IReadOnlyList<string>` (ej VO → VO-Contains-fällan gäller ej). Taxonomi-snapshot (ADR 0043): Region + OccupationField→Occupation(name). Ingen kommun, ingen ssyk-level-4.

**JobTech (web, live 2026-06-08):** region (21 sv län), municipality (290, broader→region), occupation-field (21), **ssyk-level-4 (400) = "yrkesgrupp"** (ingen `occupation-group`-typ — namnglapp), occupation-name (2179), employment-type (5), worktime-extent (2), work-place-model (3, distans). `municipalityconceptid` FINNS i payload (ADR 0043 Beslut E payload-trigger uppfylld). Distans EJ toppfält → härleds.

**Platsbanken (Klas-screenshots tmp/platsbanken/):** Ort = två-kolumns Län→Kommun-kaskad (multi-checkbox, "Välj alla kommuner", Obestämd ort/Utomlands, Rensa-röd-länk/kolumn, grön pricka aktiv, live-count). **Yrke = Yrkesområde→Yrkesgrupp (ssyk-level-4), INGEN occupation-name-nivå** (bevis: "Revisorer m.fl.", "IT-säkerhetsspecialister"). Filter-panel = Omfattning/Anställningsform/Publicerad/Distans-checkbox/+svensk-specifika. Sortering = Relevans/Datum/Ansökningsdatum. Live-count per val genomgående. Klas-krav: ny mörkgrön färg (ej blå-vit), Rensa som röd text-länk, 100% korrekt taxonomi-mappning.

## Kärninsikt (störst)
**Platsbanken filtrerar yrke på ssyk-level-4 (yrkesgrupp), JobbPilot på occupation-name.** Fundamental nivå-avvikelse → ändrar ADR 0043-modellen. Klas exempel (Data/IT 2700+, IT-säkerhetsspecialister 148) är på yrkesgrupp-nivå.

## Agent-domar

**dotnet-architect:** STORED för alla filtrerbara (entydigt); **KRITISK korrigering — Klass 1 vs Klass 2:** `JobTechHit`-POCO deserialiserar municipality + occupation_group (payload finns → STORED utan re-ingest) men EJ employment_type/worktime_extent (saknas i raw_payload → kräver POCO-tillägg + re-ingest). Single-parent + Kind-diskriminator för kommun (ej Level-int). Distans defer (overifierad). Facet = ny metod ej ny port. Parser = Application-port `ISearchQueryParser`. Fas-split B→B1/B2, C→C1/C2, D→D1/D2.

**senior-cto-advisor (decision-maker):** Q1 Option A (yrke→ssyk-level-4) + reverse-lookup-migration (ej tyst degradering); Q2 STORED + Klass1/Klass2-sekvens; Q3 single-parent bekräftat; Q4 defer; Q5 total nu + per-option metod-ej-port + NBomber-gate; Q6 parser i Application, semantik=Klas-STOPP; Q7 VO=yrke/ort/anställning/omfattning; Q8 Session 1 = endast Fas A; TD-86 absorberas, TD-100→Fas E; ADR 0067 + ADR 0043-amendment.

**CTO Q1/Q6-omdom (efter Klas-svar):** Q1 — Klas CV-roadmap pekar mot ssyk-level-4-matchning, ej Option B; behåll occupation-name-kolumn som substrat (kostnadsfritt), CV-parsing-nivå reserverad TD-93/ADR 0040. Q6 — Klas typeahead-chip-modell bärs över tre lager: utökad suggest (D1, taxonomi-union) + chip=FE-state (E) + residual-parser (D2). Suggest-källa utökas bortom ADR 0042 Beslut C. Kraschsäkerhet by design (residual→FTS OR-term).

## Klas-beslut (AskUserQuestion 2026-06-08)
- Q1: Option A (osäker först pga CV-matchning → CTO löste: ssyk-level-4-matchning + substrat bevarat).
- Q4: Defer distans.
- Q6: Typeahead-chip-komponist (skriv "systemu"→förslag→tabba klart→chip; ren fritext "AI engineer" funkar utan krasch).
- ADR: Skriv som Accepted.

## Levererat
- ADR 0067 (Accepted, 7 beslut) + ADR 0043-amendment 2026-06-08 (kommun + yrke-nivå).
- README-index (0067-rad + 0043-amendment-notering).
- TD-86 absorberad av ADR 0067 (punkt→fas-mappning); TD-100 + TD-93 korsref.
- Discovery-brief (docs/research) + 3 agent-domar (docs/reviews).
- Docs-sync (current-work + denna logg).

## Detours / lärdomar
- Architect-agenten slog i session-limit på första försöket (12:10-reset) → Klas pausade, återupptog efter reset.
- §9.4-override: Klas valde Accepted-skrivning av CC → dokumenterat i ADR 0067 Livscykel-not (memory `feedback_klas_can_override_adr_verbatim_source`).
- Klass1/Klass2-payload-skillnaden var ej synlig i discovery-briefen — architects on-disk-verifiering av `JobTechHit`-POCO fångade den; styr fas-sekvensen.

## Nästa steg
1. Klas granskar Fas A-PR-diff (post-merge, automerge).
2. **Fas B1 (data-layer)** = nästa session med Klas-GO: Klass 1 STORED-kolumner + EF-config + migration (Testcontainers) + taxonomi-snapshot-utökning + seeder. db-migration-writer + test-writer FÖRST.
3. (Senare faser) B2 (Klass 2 + re-ingest), C1/C2 (query + VO + reverse-lookup-migration), D1/D2 (facets + parser), E (FE + färg-identitet).
4. tmp/platsbanken/-screenshots raderas vid initiativ-slut.
