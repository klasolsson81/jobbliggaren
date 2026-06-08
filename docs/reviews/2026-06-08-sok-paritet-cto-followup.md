# CTO-tillägg — Platsbanken sök-paritet (Q1-omdom + Q6-bekräftelse)

**Datum:** 2026-06-08
**Agent:** senior-cto-advisor (agentId `afc84b4a8e6876650`) — decision-maker. Tillägg till `2026-06-08-sok-paritet-cto.md` efter Klas-svar på flaggade frågor.
**Redan beslutat:** Q4 DEFER (Klas-bekräftat), ADR 0067 Accepted (Klas-vald).

---

## Q1 — Dom: Option A + bevara occupation-name-kolumn (`ssyk_concept_id`) + index som queryable substrat. CV-parsing-nivå defereras till TD-93/ADR 0040.

Klas CV-matchnings-behov ("matcha utan AI, på yrken") kräver INTE Option B. Det är en datamodell-tillgänglighets-fråga, ej UI-dimensions-fråga. Datan finns redan: `ssyk_concept_id`-kolumnen + partial-B-tree-index ligger kvar i Option A ("byter roll, raderas ej"). Att exponera occupation-name som tredje picker-nivå (B) löser inte CV-matchning bättre — bara en UI-yta Platsbanken saknar (Speculative Generality, Fowler 2018 kap. 3).

Klas egen instinkt ("mappa CV till rätt SSYK istället för occupation-name-fritext") ELIMINERAR Option B-argumentet: om CV-parsing mappar till ssyk-level-4 matchar CV-sökning på SAMMA nivå som paritets-UI:t filtrerar (Option A `occupation_group_concept_id`) → samma ubiquitous language (Evans kap. 2/14). Men HUR CV-parsing mappar (ssyk-level-4 vs occupation-name vs båda) är ADR 0040 Beslut 3-reserverat (Fas 4-faststart) + TD-93 (Trigger) — får ej föregripas nu.

### ADR 0067 LÅSER
1. Primärt yrke-filter = **ssyk-level-4** (`occupation_group_concept_id`, Klass 1 STORED); picker occupation-field→ssyk-level-4 (Platsbanken-paritet).
2. **`ssyk_concept_id` (occupation-name) + index BEVARAS** — degraderas till (a) synonym/recall-input på FTS-vägen + (b) queryable substrat för framtida CV-matchning. Kostnadsfritt (finns on-disk, ingen migration adderar dem).
3. Reverse-lookup-migration occupation-name→parent ssyk-level-4 för gamla sparade `Ssyk` (Fas C2, oförändrat).

### ADR 0067 LÄMNAR till TD-93/ADR 0040 (korsref, ingen design)
4. Om CV-parsing mappar till ssyk-level-4/occupation-name/båda — odesignat (ADR 0040 Beslut 3 Fas 4-livscykel). Ingen härlednings-design, ingen UI-placeholder (ADR 0042 Beslut F-disciplin).
5. Option A:s nivå-val gör yrkesgrupp-matchning till naturligt default-spår — observation som informerar TD-93, ej låst beslut. TD-93 får korsref till ADR 0067 (substrat bevarat).

**Avvisat:** Option B (Speculative Generality + paritets-brott + föregriper ADR 0040); att låsa CV-parsing-nivån i ADR 0067 (föregriper ADR 0040/TD-93). **Q1-flaggan upplöst:** roadmapen finns men pekar mot ssyk-level-4-matchning, ej Option B. Klas-GO ej behövd för Q1-domen (entydig, ändrar ej Option A); endast ADR Accepted-flip (Klas-bekräftat).

---

## Q6 — Bekräftelse + lager-justering: Klas typeahead-chip-modell bärs över tre lager.

Klas modell är en superset av Session 1-arkitekturen — typeahead-komplettering blir förstaklassmekanism. Tre samverkande delar:

**(a) Typeahead-suggest mot taxonomi + job_ads-titlar = utökad suggest-endpoint (D1).** ADR 0042 Beslut C:s källa (`SuggestJobAdTermsQuery` = job_ads.Title ILIKE-prefix) räcker INTE — Klas vill tabba-komplettera allt i filtreringen (Län/Kommun/Yrkesgrupp/Yrke/Anställningsform). Kräver utökad källa: **union (i) taxonomi-snapshot-labels (via `ITaxonomyReadModel`, in-memory, ADR 0043) + (ii) job_ads-titel-prefix (ADR 0042 Beslut C, oförändrad)**. Taxonomi-delen = in-memory-snapshot-prefix → bryter EJ ADR 0043 extern-hop-förbud. Additiv utökning av 0042 Beslut C → ADR 0067-beslut + korsref 0042. Rate-limit per `SuggestPolicy`-mönster.

**(b) "Tabba-klart → chip" = FE-state, EJ backend-parsning (E).** Tabbat förslag → strukturerat filter-chip (suggest returnerar `{kind, conceptId, label}`) → redan disambiguerat, ingen parsning. Chip-state i FE (React, ADR 0042 Beslut A). Backend tar emot strukturerade filter-listor i `JobAdSearchCriteria` (Ssyk/Region/Municipality/OccupationGroup/EmploymentType) = exakt Fas C-dimensionerna.

**(c) Residual ren fritext = `ISearchQueryParser` + FTS-Q (D2).** Det som INTE tabbades till chip ("AI engineer", valfritt) → `ResidualQ` → FTS-hybrid (ADR 0062). **"Får aldrig krascha/exkludera" uppfyllt by design:** residual-Q går alltid till FTS som OR-bevarande term, aldrig hård AND-exkludering. Parserns roll KRYMPER (residual-sträng, ej hela råsökningen) — disambiguering sker vid input (användarstyrt) ej via gissande backend.

### Semantik (mildrad Klas-STOPP, Fas D2)
Chips = AND-mellan-dimensioner / OR-inom-dimension (ADR 0042-invariant, redan låst). Residual-Q = recall-bevarande FTS-term inom FTS-hybrid (ADR 0062). Session 1:s additiv-vs-AND-fråga mildras: chip-strukturering är medveten; bara chip/residual-kombinationen kvarstår för Klas att bekräfta i D2.

### Fas-justering
| Fas | Justering |
|---|---|
| D1 | + utökad suggest-endpoint (taxonomi-union + job_ads-titel; read-query, ingen ny port — ADR 0062 Beslut 4 + ADR 0043; `SuggestPolicy`-rate-limit; in-memory billig) |
| D2 | krymper: `ISearchQueryParser` för residual-fritext; chip-AND/residual-FTS = Klas-STOPP |
| E | växer: typeahead-chip-komponist (tabba-klart-chip-state + suggest-konsumtion + residual-fält) = kärn-FE; ADR 0042 Beslut A utökas till chip-modell |

Sekvens: D1-suggest före E-chip-FE. D2-parser parallellt/efter E (residual-Q fungerar utan parser — fritext→FTS direkt; parser = förfining).

### Klas-GO-flaggor (Q6)
1. ADR 0067-beslut "typeahead-källa = taxonomi-snapshot-union + job_ads-titel-prefix" + korsref ADR 0042 Beslut C (additivt, ej supersession). Klas-GO ej behövd (entydigt; in-memory bryter ej 0043) — flaggas så Klas ser 0042-suggest-ytan växer.
2. Chip-AND/residual-FTS-kombination = Klas-STOPP Fas D2 (mildare än Session 1).

---

## Sammanfattning
- **Q1:** Option A + bevara occupation-name-substrat (kostnadsfritt) + defer CV-parsing-nivå till TD-93/ADR 0040. Q1-flaggan upplöst.
- **Q6:** arkitekturen bär Klas modell över tre lager (suggest D1 / chip-FE E / residual-parser D2). Suggest-källa utökas bortom 0042 Beslut C. Tyngdpunkt D2→D1+E. Chip/residual = mildrad Klas-STOPP D2.

**Källor:** Evans DDD (2003) kap. 2/14; Fowler Refactoring 2nd (2018) kap. 3; Beck (YAGNI); Martin Clean Architecture (2017) kap. 22; ADR 0040 Beslut 3; ADR 0042 Beslut C/F; ADR 0043; ADR 0062; TD-93; CLAUDE.md §1/§9.6.
