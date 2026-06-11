# senior-cto-advisor — Fas E2d typeahead-chip-komponist (besluts-dom)

**Datum:** 2026-06-11
**Agent:** senior-cto-advisor (decision-maker, inte advisor)
**Scope:** ADR 0067 Beslut 5b ("tabba-klart → chip = FE-state") + Fas E2d. "Volvo Göteborg Heltid"-visionen.
**Underlag:** `docs/reviews/2026-06-11-sok-paritet-e2d-architect.md` (LÄST HELT), ADR 0067 Beslut 5/5a/5b/5c + D2-notat (verifierat on-disk), CLAUDE.md §2.1/§4.3/§5.2/§9.6.
**Status:** Read-only beslutsdom. Ingen kod ändrad.
**Bekräftad semantik (Klas-grind 2026-06-11):** chips = AND-mellan-dimensioner / OR-inom-dimension (ADR 0042 B); ort = union region∪kommun (E2b); omatchad fritext = filtrerande q via residual-parser (recall-bevarande, aldrig boost-only, ADR 0062/D2 VAL 1 A+A).

---

## Beslutsöversikt (snabbtabell)

| Val | Beslut | Klas-GO krävs? |
|---|---|---|
| **VAL 1** (ö-topologi F1) | **Variant A** — separata öar, URL = enda sanning, GET-form-fallback KRAV | Nej — CC implementerar |
| **VAL 2a** (OccupationField F2a) | **Variant A** — materialisera till barn-OccupationGroups | Nej — CC implementerar |
| **VAL 2b** (Occupation F2b) | **Variant A** — förbli ute (status quo) | Nej för A. **JA om Klas vill ha B** (ADR-amendment) |
| **VAL 2c** (EmploymentType F2c) | **GATED** — exhaustiv switch, wira EJ | Nej — CC designar intention, wirar ej |
| **VAL 3** (auto-chip-vid-submit F3) | **PRODUKTVAL → Klas-STOPP bekräftad** | **JA** — Klas väljer A/B/C. Resten av E2d byggs oberoende |
| **F4** (chip-derivering) | Bekräftad — URL-derivering, delad `useSelectedChips`-hook in-scope | Nej — CC implementerar |
| **F5** (a11y combobox) | Bekräftad — i E2d-scope, DoD-krav | Nej — CC implementerar |
| **F6 (a)(b)(c)** (ärvda Minors) | Bekräftad — in-block-fix, INGEN TD | Nej — CC implementerar |

**Klas-STOPP-klassning sammanfattad:** ETT genuint Klas-STOPP — **VAL 3** (auto-chip-vid-submit, produktval + kombinationssemantik-grind). Allt annat är entydigt motiverat mot principer → CC implementerar direkt utan extra Klas-GO (CLAUDE.md §9.6 punkt 5). VAL 2b Variant B är inte ett E2d-beslut — det är ett separat ADR-amendment-ärende som bara aktualiseras om Klas vill ha occupation-name-chips.

---

## VAL 1 — Island-topologi (F1)

### Beslut
**Variant A — separata öar, URL som enda delad sanning.** Typeahead blir en tredje URL-skrivare (`JobbHeroSearch`-ö) via befintlig `buildJobbHref`-SPOT, sida vid sida med `JobbHeroFilters` och toolbaren. **GET-form-fallback bevaras som KRAV (ej val).**

### Motivering mot principer

- **SRP (Martin 2017, kap. 7):** En modul = en change-reason. Sökfält-input (suggest/debounce/AbortController/combobox-a11y) ändras av andra skäl än kaskad-popovers (dual-axis/facet-counts/per-län-normalisering). Två change-reasons = två öar. Variant B kollapsar dem till en ~600-raders client-komponent med två change-reasons — ett rakt SRP-brott.
- **Component cohesion — CCP (Martin 2017, kap. 13):** "Gather into components those classes that change for the same reasons and at the same times." Typeahead och popovers ändras INTE av samma skäl. Att tvinga ihop dem (B) bryter CCP; att hålla dem isär (A) följer den.
- **DRY/SPOT (Hunt/Thomas 1999):** `buildJobbHref` är redan single point of truth för URL-skrivning. En tredje konsument är symmetriskt korrekt — A duplicerar ingen URL-builder. B:s enda reella vinst ("en commit-väg") uppnås redan i A genom att de tre öarna delar `buildJobbHref`.
- **Granskbar diff (SWE@Google 2020, kap. 9):** A ger tre granskbara öar med isolerad testbarhet (CLAUDE.md §2.4 "testbart först"). B:s monolit-ö är svår att granska och testa isolerat — strider mot Mastercard-nivå-granskningskravet (CLAUDE.md §1).
- **No-JS progressive enhancement (CLAUDE.md §5.2 + GOV.UK-doktrin):** Civic-utility kräver att sökytan fungerar utan JS. A server-renderar `<form action="/jobb" method="get">` + `<input name="q">` + hidden inputs identiskt med idag och upgrade:ar progressivt. GET-form-fallback är ett **krav**, inte en variant — det följer direkt av JobbPilots civic-utility-identitet (CLAUDE.md §1).
- **Konformitet mot Accepted prejudikat (E2g):** A följer det redan-låsta mönstret "URL = enda sanning, `useOptimistic(base=props)`, ingen gemensam client-förälder pga medveten streaming-ö-split". Att avvika från ett Accepted, fungerande mönster utan tvingande skäl är onödig risk (Fowler 2018 — undvik spekulativ omarkitektur).

### Avvisade alternativ

**Variant B (sammanslagen ö):** Avvisad. Bryter SRP (Martin kap. 7) och CCP (Martin kap. 13) genom att slå ihop två change-reasons. Större client-bundle laddas även för ren fritext-användare (12factor/perf-disciplin, ADR 0045). Strider mot den medvetna streaming-ö-splitten (Accepted designval). Dess enda vinst (en URL-skrivare) finns redan i A via delad `buildJobbHref`. "Färre öar" är inte ett design-värde i sig — SRP och granskbarhet är.

**Variant C (typeahead ⊃ JobbHeroFilters som child):** Avvisad — "sämsta av båda" (architect-domen, bekräftad). Bryter den medvetna ö-splitten: JobbHeroFilters slutar vara självständig ö → remountas/omrenderas med föräldern → kan återinföra popover-stängning-vid-toggle-buggen som E2g varnade för. Prop-drilling av commit + hela URL-staten = tightare koppling än A utan B:s konsistens-vinst. Bryter LSP-andan (en self-contained ö degraderas till barn med ändrad livscykel).

### Trade-offs accepterade
Tre öar måste var och en bära hela URL-staten som props (q + tre dimensioner + sortBy + pageSize) för att inte radera varandras params. Detta finns redan idag (JobbHeroFilters bär q "så filter-klick inte raderar q"); A adderar en tredje plats där prop-listan hålls i synk. Accepterat — symmetrin är samma "param-bevarande-disciplin" som redan gäller E2b/toolbar. Om en framtida dimension tillkommer måste tre öar uppdateras; det är en synlig, granskbar kostnad, inte en dold koppling. SRP-vinsten väger tyngre än prop-synk-bekvämligheten.

---

## VAL 2 — kind→dimension-mappning (F2)

Givna (entydiga, ingen variant): Region→`region`, Municipality→`municipality`, OccupationGroup→`occupationGroup`, Title→`q` (fritext). Följer direkt av URL-dimensionerna + `JobAdFilterCriteria`.

### VAL 2a — OccupationField

#### Beslut
**Variant A — materialisera OccupationField-chip till alla barn-OccupationGroups ("Hela {yrkesområdet}"-semantik).** Ren in-memory lookup i det redan-laddade taxonomy-trädet. Ingen backend-touch.

#### Motivering mot principer

- **Anti-corruption layer (Evans 2003, kap. 14):** FE-ACL:n (`taxonomy.occupationFields[].occupationGroups[]`) bär redan OccupationFields barn on-FE. Mappningen är en in-memory lookup i samma träd `JobbHeroFilters` redan itererar — ingen extern hop, inget nytt kontrakt. ACL:n är redan på plats; vi konsumerar den.
- **Civic-utility-pålitlighet (CLAUDE.md §1):** Ett OccupationField-förslag som inte mappar till något är en **död förslag-rad** (chip utan mål = återvändsgränd) — exakt anti-patternet Beslut 5a/VAL 4 avvisade Occupation med. Ett förslag användaren ser måste leda någonstans. A gör förslaget meningsfullt; B/C gör det inte.
- **DRY (Hunt/Thomas 1999):** A återanvänder två etablerade mönster — Ort-pickerns "Hela länet"-materialisering (E2f) och E2g `DeriveLabel`-mängdlikhet ("alla grupper i ETT yrkesområde" → "Data/IT"). Ingen ny semantik uppfinns.
- **Bounded/DoS-säkert (ADR 0045 + MaxConceptIds=400):** Ett yrkesområde har ≤~50 yrkesgrupper; cap=400 håller med marginal även vid flera field-chips. Samma URL-längd som "Välj alla yrkesgrupper" redan producerar idag. Inom budget.

#### Avvisade alternativ
**Variant B (släng ur mappningen):** Avvisad — ger en död rad i korpusen (förvirrande UX, bryter civic-utility-pålitlighet). Om man inte vill ha det valbart ska det utelämnas ur korpusen helt (backend-touch + regression av Beslut 5a som medvetet inkluderade OccupationField). Sämre på alla axlar.

**Variant C (mappa OccupationField → q):** Avvisad — semantiskt fel. OccupationField ÄR strukturerad taxonomi med exakta barn-ids; att degradera till FTS-fritext kastar bort precisionen, ger sämre recall än A, och bryter "disambiguering vid input"-andan (D2). Primitive obsession i förklädnad (struktur → sträng).

#### Trade-offs accepterade
Chip representerar N ids men visas som ETT chip. Om användaren sedan av-markerar EN yrkesgrupp i popovern "spricker" abstraktionen — men det är samma minus-en-materialisering E2f redan löste för Ort (etablerat mönster, ingen ny risk). URL blir längre (~50 ids) — samma som "Välj alla yrkesgrupper" idag.

### VAL 2b — Occupation (occupation-name, EJ i korpus idag)

#### Beslut
**Variant A — förbli ute (status quo).** occupation-name når som recall via q-FTS-synonym (`IOccupationSynonymExpander`). `SuggestionKind` får INGEN `Occupation`-medlem i E2d. **Om Klas vill ha occupation-name-chips (Variant B) är det ett separat ADR-amendment-ärende, INTE E2d-mekanik — då STOPP till Klas.**

#### Motivering mot principer

- **Respektera Accepted beslut (CLAUDE.md §9.6 + `feedback_adr_mechanism_vs_env_phase_triage`):** ADR 0067 Beslut 5a/VAL 4 utesluter Occupation EXPLICIT — verifierat verbatim on-disk: "Occupation-name-dimensionen avvecklad ur sök-identiteten (CTO (e)/(f)): `Ssyk` utgick ur `SearchCriteria`-VO:t, `JobAdFilterCriteria`..." (ADR 0067 line 117) och D2 VAL 1 A+A avvisade token-lyft (line 126). Att inkludera Occupation nu är inte mekanik-konkretisering — det är **omtolkning av ett Accepted beslut**, vilket per `feedback_adr_mechanism_vs_env_phase_triage` triggar CTO-triage/Klas-GO, inte CC-omdöme. Min triage-dom: amendment-territorium.
- **Inget chip utan mål (Evans 2003 — modell-integritet):** `JobAdFilterCriteria` har inget Ssyk-fält efter C2. Ett Occupation-"chip" vore ett chip utan URL-dimension att skriva till — samma dödläge som F2a/Variant B. Strukturen tillåter inte ett meningsfullt Occupation-chip idag.
- **Speculative Generality undviks (Fowler 2018, kap. 3):** Variant B skulle bygga en suggest-källa + ny `SuggestionKind` + parent-mappning för en dimension som inte finns. Att bygga maskineriet före behovet är just det smell Fowler varnar för.
- **Proportionalitet (KISS/YAGNI):** Variant B kostar backend-touch (`TaxonomyReadModel.LoadAsync`-utökning), ny `SuggestionKind`-medlem (int-på-wire-ordinal + FE `SUGGESTION_KIND_ORDER`-synk), **korpus ×~6** (400→2579 suggestable → prefix-scan-perf mot ADR 0045), security-auditor (ny suggest-källa, DoS/prefix-scan) + test-writer (mappnings-determinism), och risk för dubbla förslag (occupation-name + occupationGroup för samma yrkesgrupp). Allt detta för en vinst (chip i stället för q) som residual-q + synonym-expander redan levererar som recall. Oproportionerligt.

#### Avvisade alternativ
**Variant B (utöka korpus + SuggestionKind.Occupation + broader-mappning):** Avvisad **som E2d-mekanik**. Den är inte fel i sig — den är fel *här*. Den kräver ADR-amendment mot Accepted Beslut 5a, backend-touch, två veto-agenter (security-auditor + test-writer), och ×6 korpus-perf-bedömning mot ADR 0045. Det är ett eget, medvetet Klas-beslut — inte något CC eller jag foldar in i E2d tyst.

#### §9.6-triage (TD?)
Variant B är **INTE TD**. TD lyfts bara vid annan-fas eller saknad-dependency (§9.6). Här är det i stället ett **ADR-amendment + eget touch** om Klas vill ha det — en medveten utvidgning, inte uppskjuten skuld. Lyfts inte som TD; flaggas som Klas-beslut OM efterfrågat. "Volvo systemutvecklare Göteborg"-visionens systemutvecklare-del löses redan via residual-q (recall-bevarande, Beslut 5 by design).

### VAL 2c — EmploymentType/WorktimeExtent ("Heltid") — GATED

#### Beslut
**GATED — designa intentions-redo, WIRA EJ.** Falsk-klar-disciplinen **bekräftad**. NULL-data tills re-ingest Klass 2 (B2), som ALDRIG körs autonomt.

#### Motivering mot principer

- **Falsk-klar förbjuden (CLAUDE.md §2.5/§9.6 + B2-gate):** NULL-data → en wirad EmploymentType-dimension = tyst-noll-filter = falsk klar. Att wira den nu vore att leverera en kodväg som ser klar ut men returnerar fel resultat. Förbjudet — samma gate som D1/D2/FacetDimension.
- **Exhaustiveness som kompilator-tvång (Hunt/Thomas 1999 — fail fast; Beck — gör det rätta enkelt):** kind→dimension-mappningen ska vara en `switch (kind)` exhaustiv mot `SuggestionKind` (TS exhaustiveness, `never`-default). När en framtida `SuggestionKind`-medlem (EmploymentType) tillkommer ger en glömd mappning **COMPILE-fel** — inte en tyst död väg. Det är "re-ingest-redo" gjort rätt: kompilatorn tvingar fram den additiva raden vid B2, ingen latent död kodväg existerar däremellan.
- **Ingen död kodväg idag (verifierat):** `SuggestionKind` har ingen EmploymentType/WorktimeExtent-medlem → de kan aldrig komma ur suggest-korpusen → typeahead-ön kan aldrig auto-chippa dem. Korrekt by design. E2d lägger INTE till en tredje Filter-pill ("Omfattning") och INTE en EmploymentType-gren i `buildJobbHref`/`JobAdFilterCriteria`.

#### §9.6-triage (TD?)
**INTE TD.** Korrekt gated B2-data-dependency (saknad funktion-dependency). Den naturliga additiva mappnings-raden tillkommer vid B2-touch — kompilatorn ser till det. Ingen TD-lyftning. (Architect-bedömningen bekräftad.)

---

## VAL 3 — auto-chip-vid-submit (F3, "volvo göteborg heltid" + Enter utan tabb)

### Beslut
**Klas-STOPP-bedömningen BEKRÄFTAS. Detta är ett PRODUKTVAL, inte mekanik-konkretisering. → Klas väljer A/B/C innan just denna del byggs.** Resten av E2d (VAL 1 + VAL 2a + F4 + F5 + F6) **byggs oberoende** — VAL 3 blockerar INTE hela E2d.

### Motivering mot principer

- **Substansen > lagret (`feedback_adr_mechanism_vs_env_phase_triage`):** D2 VAL 1 avvisade token-lyft EXPLICIT — verifierat verbatim: "Avvisat: Variant B (1:1 token-lyft, dubblerar FE-chip-ansvar + tyst dimensions-AND mot recall-andan)" (ADR 0067 line 126). Auto-chip-vid-submit ÄR en token-parser: splitta submit-strängen i tokens, matcha varje mot taxonomi-labels. Att flytta parsern från backend till FE ändrar **lagret men inte substansen** — det är fortfarande token→dimension utan explicit användarval, exakt den avvisade vägen. En agent-flaggad semantik-avvikelse mot Accepted-ADR-mekanik triggar CTO-triage/Klas, inte CC-omdöme. Min triage-dom: detta korsar gränsen.
- **Kombinationssemantik är Klas-STOPP-gated (ADR 0067 verbatim):** "chip/residual-kombinationen presenteras för Klas-GO" (line 131) och "endast chip/residual-kombinationen bekräftas av Klas i Fas D2" (line 83). Auto-chip-vid-submit ändrar submit-semantiken: Enter på fritext = q-FTS-OR idag (recall-bevarande); med auto-chip kan Enter tyst konvertera delar av q till hård dimensions-AND. "göteborg" som exakt kommun-match blir `municipality=`-AND i stället för q-FTS-OR → en annons som nämner Göteborg i texten men har `municipality=null` faller bort. Det är precis den recall-bevarande-vs-hård-AND-gränsen Beslut 5/D2 vaktar, och den är explicit Klas-grindad. Klas-grinden 2026-06-11 fyllde i AND/OR-semantiken för *valda chips* — den sa inget om *automatisk* chip-konvertering vid submit utan användarval. Det är en separat semantik-fråga.
- **Visionen kräver inte auto-chip (Fowler 2018 — minsta lösning som uppfyller behovet):** "Volvo Göteborg Heltid"-visionen nås FULLT via Variant A (explicit tabb/klick): "göte" → tabba Göteborg-chip → "heltid" → (vid B2) tabba Heltid-chip → "volvo" blir residual-q. Visionen kräver smidig tabb-komplettering (F5 a11y), inte en submit-tids token-parser. Den enklare vägen uppfyller behovet — den mer komplexa inför recall-risk + parser för marginell "magi".

### Klas-STOPP-fråga och varianter (Klas väljer)

- **Variant A — endast explicit tabb/klick chippar (ingen auto-chip-vid-submit).** "volvo göteborg heltid" + Enter utan tabb → HELA strängen → q-FTS (residual-parser, D2). Konformerar 100% med Beslut 5b + D2. Kraschsäkert by design. **Min preferens på principgrund** — minst risk, ingen ny parser, ingen semantik-ändring, ren Accepted-väg.
- **Variant B — auto-chip vid exakt singel-token-label-match endast.** HELA trimmade strängen matchar exakt EN taxonomi-label → chip; annars hela strängen → q. "göteborg" ensamt → kommun-chip; "volvo göteborg heltid" → q (ingen flertoken-splitt). Undviker flertoken-AND-risken men inför ändå en match-vid-submit-mekanik (mildare). Mellanväg om Klas vill ha viss magi.
- **Variant C — full flertoken auto-chip.** Splitta, matcha varje token, resten → q. Maximal "magisk" UX men maximal recall/semantik-risk + FE-parser. **Avråds på arkitektur- och principgrund** — det är vad D2 VAL 1 avvisade på backend-sidan; lagerbytet ändrar inte substansen.

### Blockering-omfattning (explicit svar på Klas-prompten)
**VAL 3 blockerar INTE resten av E2d.** Per Klas-prompten ("om CTO klassar det som produktval: STOPP-fråga till Klas innan bygge av just den delen — resten av E2d fortsätter"): CC bygger VAL 1 (Variant A-ö), VAL 2a (OccupationField-materialisering), F4 (chip-derivering ur URL), F5 (a11y combobox + tab-till-chip) och F6 (a/b/c) **oberoende**. Submit-beteendet implementeras som Variant A:s rena väg (hela strängen → residual-q) som **default** — vilket är den Accepted-konforma baslinjen oavsett Klas-val. Om Klas senare väljer B eller C är det ett additivt påbygg ovanpå den redan-byggda ön, inte en omarkitektur. Detta är inte en kompromiss — Variant A ÄR den korrekta default-semantiken; B/C är opt-in-magi ovanpå.

---

## F4 — chip-state-derivering (bekräftad)

### Rekommendation
**Bekräftad: chip-state deriveras ur URL+props (E2g-principen), ALDRIG egen useState-kopia.** CC implementerar direkt.

- **E2g-prejudikat (Accepted):** `useState`-kopia synkar ej vid externa URL-ändringar — det var E2g-buggen. Chips i sökfält-ön deriveras ur samma RSC-props (occupationGroup/region/municipality conceptId[] → reverse-lookup-label via taxonomy-trädet), exakt som toolbaren redan gör.
- **DRY/SPOT (Hunt/Thomas 1999):** Toolbaren mappar redan conceptId→label+dimension-ikon kind-agnostiskt ur URL. Extrahera en delad `useSelectedChips(taxonomy, occupationGroup, region, municipality)`-hook som SPOT för "URL-dimensioner → chip-modeller", konsumerad av både toolbar och sökfält-ö. **In-scope E2d** — annars duplicerar E2d toolbar-logiken, vilket vore ett DRY-brott i samma touch. (Architect lutade in-scope; jag bekräftar in-scope på DRY-grund: att lämna duplicering i den touch som inför andra konsumenten vore att skapa skulden medvetet.)
- **Dedupe + OR-inom-dimension:** vid tabb-val, kontrollera `conceptId` mot befintlig dimensions-lista före append (samma `selected.includes(conceptId)` som popoverns `toggle()`) → idempotent. Två förslag samma kind → append distinct-normaliserat till rätt dimensions-lista = OR-inom-dimension (ADR 0042 B, redan låst). Ingen ny logik.

---

## F5 — A11y (WAI-ARIA combobox + tab-till-chip; bekräftad)

### Rekommendation
**Bekräftad: i E2d-scope, DoD-krav (CLAUDE.md §8 punkt 6).** En chip-komponist utan keyboard-navigering vore inte DoD-klar. CC implementerar; nextjs-ui-engineer laddar `jobbpilot-design-a11y`-skillen vid implementation.

Konkreta krav (architect-domen bekräftad):
1. **`aria-activedescendant` + ArrowDown/Up/Enter-navigering** i förslagslistan (idag finns bara Escape — största a11y-luckan). Varje option: `id` + `role="option"` + `aria-selected`.
2. **Tab-till-chip:** chips tangentbords-nåbara med `×`-knapp — återanvänd toolbarens redan-a11y-granskade chip-mönster (DRY).
3. **`role="listbox"` på `<ul>` + `role="option"` per rad** (idag `<button>` i `<li>`).
4. **WCAG 2.1 AA** via `jobbpilot-design-a11y`-skill — combobox + chip-input är ett känt mönster med exakta ARIA-krav.

---

## F6 — Ärvda E2d-Minors (a)(b)(c) (bekräftad in-block, INGEN TD)

### Beslut
**Bekräftad: alla tre = in-block-fix i E2d, INGEN TD.** Samma fas, samma `JobbFilterPopover`-komponent, ingen saknad dependency → §9.6 default = fixa in-block.

- **(a) `selectAllLabel` "Hela {länsnamn}" per-grupp:** gör `selectAllLabel` till `(group: PopoverGroup) => string`-funktion (renast — SPOT, föräldern äger copy), eller derivera ur `activeGroup.label` när `groupAxis` finns. Verifiera att Yrke ("Välj alla yrkesgrupper", saknar groupAxis) fortsatt fungerar via statisk fallback. Kontrakts-justering, ej design.
- **(b) `dialogLabel`-prop så popoverns `aria-label` ≈ triggerns namn:** idag `aria-label={leftTitle}` ("Län"/"Yrkesområde") men triggern säger "Ort"/"Yrke" → screenreader-mismatch. Lägg `dialogLabel?: string` (default `leftTitle` för bakåtkompat); `JobbHeroFilters` skickar "Ort"/"Yrke". Ren WCAG 2.1 AA-fix.
- **(c) tri-state `aria-checked="mixed"` på select-all vid partiellt val:** `CheckRow` tar `checked: boolean | "mixed"`; select-all beräknar mixed = `rightAnySelected && !selectAllChecked`. Verifiera mot E2f minus-en-materialisering (materialiserade ids → select-all tekniskt icke-mixed eftersom region-id:t är borta; ok).

**Principgrund (§9.6 + Fowler 2018):** Alla tre är liten yta, samma komponent, rätt fas. Att lyfta dem som TD vore "spara TD så scope inte växer"-anti-patternet (§9.6) — vi måste ändå fixa dem, och de hör till E2d:s egen komponent. In-block.

---

## §9.6-triage — sammanställd TD-dom

| Fynd | TD? | Motivering |
|---|---|---|
| VAL 2b Variant B (Occupation-korpus) | **Nej (ej TD)** | ADR-amendment + eget Klas-touch om efterfrågat — medveten utvidgning, ej uppskjuten skuld |
| VAL 2c EmploymentType | **Nej (ej TD)** | Korrekt gated B2-data-dependency; exhaustiv switch ger compile-tvång vid B2 |
| F4 `useSelectedChips`-extraktion | **Nej (ej TD)** | In-scope E2d — DRY-krav i samma touch som inför andra konsumenten |
| F6 (a)(b)(c) | **Nej (ej TD)** | In-block-polish, samma fas/komponent |

**Inga TDs lyfts ur E2d.** Allt är antingen in-block (rätt fas) eller gated dependency (ingen kod) eller separat Klas-beslut (ej skuld).

---

## Sammanfattad Klas-STOPP-klassning

**ETT Klas-STOPP:** VAL 3 (auto-chip-vid-submit) — produktval + kombinationssemantik-grind. Klas väljer A/B/C. CTO-preferens: **A** (ren Accepted-väg). Resten av E2d byggs oberoende med Variant A:s default-submit-semantik.

**Allt övrigt = CC implementerar direkt** (entydigt motiverat mot principer, CLAUDE.md §9.6 punkt 5): VAL 1 (A), VAL 2a (A), VAL 2b (A/status quo), VAL 2c (gated design), F4, F5, F6.

**Villkorat Klas-GO:** VAL 2b Variant B aktualiseras ENDAST om Klas vill ha occupation-name-chips → då ADR-amendment + security-auditor + test-writer, ej E2d-fold.

---

## Referenser
- Robert C. Martin, *Clean Architecture* (2017) — kap. 7 (SRP, VAL 1/F1), kap. 13 (CCP/CRP komponent-cohesion, VAL 1)
- Eric Evans, *Domain-Driven Design* (2003) — kap. 14 (Anti-Corruption Layer, VAL 2a/2b taxonomy-ACL), modell-integritet (VAL 2b chip-utan-mål)
- Martin Fowler, *Refactoring* 2nd ed (2018) — kap. 3 (Speculative Generality, VAL 2b/VAL 3-avvisning; minsta-lösning, VAL 3)
- Hunt/Thomas, *The Pragmatic Programmer* (1999) — DRY/SPOT (buildJobbHref, useSelectedChips, exhaustiv switch); fail-fast (VAL 2c)
- Kent Beck — "gör det rätta enkelt" (VAL 2c exhaustiveness-tvång)
- Winters/Manshreck/Wright, *Software Engineering at Google* (2020) — kap. 9 (granskbar diff, VAL 1 SRP-på-ö)
- ADR 0067 Beslut 5/5a/5b/5c + D2 VAL 1 A+A (verifierat verbatim on-disk: line 77 chip=FE-state, line 117 Ssyk utgick, line 126 token-lyft avvisat, lines 83/131 kombinationssemantik Klas-STOPP)
- ADR 0042 Beslut B (OR-inom/AND-mellan), ADR 0062 (FTS-hybrid OR-recall), ADR 0045 (perf-budget), ADR 0043 (taxonomi-ACL)
- CLAUDE.md §1 (civic-utility/Mastercard-nivå), §2.1 (lager), §2.4 (testbart), §2.5 (perf-dom/falsk-klar), §4.3/§5.2 (RSC-default, no-JS-fallback), §9.6 (in-block vs TD)
- memory `feedback_adr_mechanism_vs_env_phase_triage` (VAL 2b/VAL 3 — ADR-mekanik-avvikelse → CTO-triage/Klas)
- jobbpilot-design-a11y-skill (WCAG 2.1 AA combobox, F5)
- dotnet-architect-domen `docs/reviews/2026-06-11-sok-paritet-e2d-architect.md`
