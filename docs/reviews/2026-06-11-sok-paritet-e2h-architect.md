# dotnet-architect — Fas E2h chips-i-sökfältet (arkitekturdom)

**Datum:** 2026-06-11
**Agent:** dotnet-architect (INLINE-dom; senior-cto-advisor fattar multi-approach-besluten efter denna)
**Scope:** Klas produktspec 2026-06-11 (chips-i-fältet, LÅST — övertrumfar E2d-CTO VAL 3 Variant A-preferensen) ovanpå E2d (#51, main `1061bc2`).
**Status:** Design-dom. Inga kod-ändringar (read-only).
**Lager-fokus:** FE-ö-komposition + URL-som-SPOT. Backend RÖRS INTE i primär väg — q-wire-kontraktet (EN sträng, 2–100, `SearchQueryParser` D2) är oförändrat.

---

## Sammanfattning

E2h är en ren FE-touch som **strukturellt eliminerar E2d-buggklassen**: när chips deriveras ur URL:en finns ingen "draft vs committad q"-dualitet kvar att förväxla — fältet blir en alternativ rendering av samma URL-state som toolbaren (E2g-principen). Klas-spec:en är produktbeslut (live-commit per tagg, allt blir taggar); jag relitigerar inte den, men dokumenterar två konsekvenser den köper: (1) exakt-match-tokenisering konverterar ord som "Göteborg" till hård dimensions-AND i stället för q-FTS-OR — medvetet per spec; (2) per-ord-live-commits skapar recent-search-rader per mellansteg (verifierat i `RecentJobSearchCapturer`: distinkta filter-hashes → INSERT + evict-äldsta vid cap=20).

Genuina multi-approach-val till CTO: **F1** (chip-derivering: URL vs lokal lista — jag rekommenderar URL starkt), **F3.1** (history: push vs replace per tagg), **F3.3** (recent-capture-spam: acceptera/observera vs liten backend-touch), **F6** (komponentstruktur A/B/C). F2/F4/F5 är design-domar med entydig riktning. Inga TDs lyfts (§9.6). Minus-operatorn: out-of-scope, men tokenizern måste **aktivt neutralisera** ledande `-` — annars shippar E2h en oavsiktlig NOT-feature (se F2-notatet).

---

## On-disk-grund (verifierat)

- `jobb-hero-search.tsx`: draft-state + prev-prop-sentinel; `onSelectSuggestion` → `navigate` + draft-reset — exakt E2d-buggen Klas såg ("sökte direkt + tömde fältet"). Hero renderas **utanför** den searchParams-key:ade Suspense-gränsen (`page.tsx` rad 136–137, 202–203) → ön remountas INTE per sökning. Kritiskt: detta måste förbli så (fokus + utkast överlever live-commits).
- `job-ad-typeahead.tsx`: WAI-ARIA combobox med pil-nav + `aria-activedescendant` + `role="option"`; Tab hanteras INTE (tabbar ut); Enter utan markering bubblar till form-submit. `onMouseEnter` sätter `active`.
- `chip-composition.ts`: ren `composeSuggestionChip(suggestion, current, taxonomy)` med exhaustiv switch — **återanvänds oförändrad** i E2h.
- `search-params.ts`: `buildJobbHref` (SPOT). `q` är EN trimmad sträng på wire.
- `jobb-results-toolbar.tsx`: chips ur props men via **lokala useState-kopior** (`occupationGroupState` m.fl., rad 90–96) — E2g-divergent mönster som överlever bara för att toolbaren remountas via Suspense-keyn. E2d-CTO:ns mandaterade `useSelectedChips`-SPOT är INTE levererad on-disk (toolbar har kvar inline-mappning).
- Backend: `SearchQueryParser` (kastar aldrig; whitespace-kollaps; `< QMinLength=2` → null; `> QMaxLength=100` → trunkering). `RecentJobSearchCaptureBehavior` capture:ar varje lyckad list-query (inloggad + icke-tom kriterie); `RecentJobSearchCapturer` dedupe:ar på exakt filter-hash, `Bump` vid träff, annars INSERT + evict äldsta vid `MaxPerSeeker=20`.

---

## F1 — chip-state-modell (Variant A/B → CTO; stark rekommendation A)

### Variant A — chips deriveras HELT ur URL:en; enda lokala staten är utkast-ordet (REKOMMENDERAS)

- **Dimension-chips** = occupationGroup/region/municipality-params → label via taxonomy-reverse-lookup (samma derivering toolbaren gör). × i fältet = exakt samma URL-operation som toolbar-chip-× — en operation, två renderingar.
- **Fritext-chips** = `q.split(" ")` → en chip per ord. Avgörande wire-fakta: q har **ingen fras-semantik på wire** — `websearch_to_tsquery` AND:ar ociterade ord som lexem (ADR 0062). Per-ord-chips är därmed den **wire-ärliga** renderingen; "EN chip för flerords-q" vore en lögn om en fras-semantik som inte existerar.
- **Round-trip-stabilitet:** `chips(q) = q.split(' ')`, `q(chips) = chips.join(' ')`; backend-parsern kollapsar whitespace ändå → kanonisk form stabil åt båda håll.
- **q-ord-ordning:** bevaras naturligt — ny fritext-chip = append av ord sist i q; derivering bevarar strängordningen. **Dubbletter:** dedupe case-insensitivt vid append (idempotent, speglar `addUnique`; FTS lexem-dedupe:ar ändå — FE-dedupe:n är UX, inte korrekthet).
- **Extern q (recent-sökning "AI engineer"):** renderas som två chips ("AI" + "engineer"). Det är samma wire-state och därmed korrekt. Prev-prop-sentinel för q behövs inte längre för chips (de deriveras), bara för att nollställa utkastet vid extern navigation.
- **useOptimistic(base=props)** för chip-listan (E2g-mönstret) så chipen syns omedelbart vid commit innan RSC-payloaden landat.

### Variant B — lokal chip-lista som synkas mot URL

Avvisas av mig: detta är exakt state-kopia-klassen E2g dödade och som producerade E2d-buggarna ("Göteborg-taggen kvar men sökordet borta" = två sanningar i osynk). Att Klas-spec:en kräver live-commit per tagg gör Variant A *möjlig och naturlig* — varje chip ÄR URL-state i samma ögonblick den skapas, så det finns inget legitimt fönster där en lokal lista skiljer sig från URL:en. B köper ingenting och återinför buggklassen.

**Arkitekt-dom:** Variant A. E2d-buggarna försvinner *strukturellt*: "val söker direkt och tömmer" kan inte uppstå (val = chip i URL = chip i fältet; utkastet rörs inte), "tagg kvar men sökord borta" kan inte uppstå (en sanning).

---

## F2 — tokenisering (entydig riktning; CTO bekräftar; två edge-val)

**Grundregel (per spec):** mellanslag/komma avslutar token → exakt case-insensitiv match mot taxonomi-labels i det redan-laddade FE-trädet → dimension-chip via **`composeSuggestionChip` oförändrad** (tokenizern syntetiserar en `SuggestionDto` ur matchen — kind+conceptId+label — och går genom samma SPOT som förslags-val; ingen andra kind→dimension-väg). Ingen match → fritext-chip (ord i q).

- **Multi-ord-labels ("Stockholms län", "Upplands Väsby") nås endast via förslags-val — ACCEPTABEL semantik.** Det är D2-andan ("disambiguering vid input"): typeahead-dropdownen ÄR vägen till flerords-koncept (efter "Upplands" står "Upplands Väsby" i listan — Tab tar den). Faller användaren igenom (skriver "Upplands Väsby" med mellanslag) blir det två fritext-chips → q-FTS — **recall-bevarande fallback, ingen krasch, inget tyst fel**. Degradering åt rätt håll.
- **Match-korpus = FE-taxonomy-trädet enbart** (regions/municipalities/occupationFields/occupationGroups). Title-labels behövs INTE i auto-match: en Title mappar ändå till q, vilket är exakt vad en fritext-chip är. **Noll backend-touch.**
- **Ambiguitets-regel (deterministisk, krävs):** om en token exakt-matchar **fler än en** kind/label → **fritext-chip** (gissa aldrig — samma anti-gissnings-princip som D2 Variant A+A). Endast unik match → dimension-chip. OccupationField-match materialiseras till barn-grupper (VAL 2a-vägen, redan i `composeSuggestionChip`).
- **Tomma tokens** (dubbla mellanslag/komman, inledande avgränsare): skippas tyst — inga tomma chips.
- **`+ord`:** strippa alla ledande `+`, behandla resten normalt (per spec).
- **`-ord` — VIKTIG UPPTÄCKT:** `websearch_to_tsquery` tolkar ledande `-` som NOT **redan idag** i FTS-grenen. En `-Hogia`-fritext-chip skulle alltså få *oavsiktlig* negations-semantik utan Klas-GO. **Dom: tokenizern strippar ledande `-` (symmetri med `+`) tills minus-fasen designas** — annars shippar E2h en odokumenterad feature i en Klas-pending-fråga. Dokumenteras som ADR 0062-notat-kandidat (backend-parser/FTS-negation + ev. dimensions-NOT = egen fas, Klas-dom pending). CTO bekräftar strip-valet.
- **Ord < 2 tecken (under q-min):** q-minimum gäller den **joinade** strängen, inte per ord ("C utvecklare" är giltig q). Endast en ENSAM 1-teckens-chip ger q som backend-parsern nullar (recall-bevarande no-op — inget fel, men chipen "räknas" då inte). Dom: **chippa ändå** (spec: allt blir taggar) och duplicera INTE parserns hygien-logik i FE (parsern är SPOT för q-normalisering). Konsekvensen är godartad och självläkande (nästa ord gör q giltig).
- **Max-längd (Viktigt):** joinad q > 100 får inte live-committas blint — backendens **validator** avvisar queryn före parserns trunkering, vilket skulle knäcka live-resultaten mitt i skrivflödet. FE-guard krävs: vägra chip-commit som tar joinad q över taket; ordet stannar i utkastet + saklig hjälptext (copy = design-reviewer). Exportera delad konstant (`Q_MAX_LENGTH = 100`) i dto-lib med kommentar som cross-refererar `SearchCriteria.QMaxLength` (samma cross-ref-disciplin som badge-labels).

**Placering:** ren tokenizer i `lib/job-ads/tokenize.ts` — ren funktion `(draft, taxonomy, current) → { nextState, remainingDraft }`, unit-testbar utan DOM (CLAUDE.md §2.4).

---

## F3 — live-commit-mekanik (tre delfrågor)

### F3.1 — history: push vs replace per tagg (Variant A/B → CTO)

- **Variant A — `router.push` per tagg** (konsekvent med popover/toolbar): Back = ångra-senaste-tagg. Mot: ett 4-ords-flöde ger 4+ history-entries; Back blir oanvändbar för att lämna sidan.
- **Variant B — `router.replace` för fältets tokeniserings-commits** (push behålls för popover/toolbar/förslags-val? — nej, då är fältet inkonsekvent internt; B = replace för ALLA fält-commits): att komponera EN sökning är EN logisk akt; history speglar sökningar, inte tangenttryck. Chip-× i fältet har då samma replace-semantik, medan toolbar-× fortsatt pushar — en dokumenterbar asymmetri (fältet = pågående komposition, toolbaren = redigering av etablerad sökning).
- **Min lutning: Variant B** (search-as-you-type-konventionen; Back-användbarhet är civic-utility-pålitlighet), men det är UX-flavored → CTO, ev. Klas-fråga i samma STOPP som "vad gör Sök".
- **Entydigt oavsett variant:** `{ scroll: false }` på fältets commits (default scroll-to-top per push rycker sidan per ord) + commits i `startTransition` (befintligt mönster).

### F3.2 — last och facet-counts

Varje färdig tagg triggar list- + facet-queries. Naturlig takt = ord-takt (~1/s), jämförbar med en popover-klick-serie; suggest-endpointen är redan rate-limited och list-vägen har NBomber-budget observe-only (ADR 0045). **Ingen debounce på commits** — debounce skulle fördröja live-resultaten som är spec:ens poäng. Acceptabelt; noteras som perf-observation mot ADR 0045-budgeten (LoggingBehavior mäter redan).

### F3.3 — recent-search-capture-spam (Variant A/B → CTO-triage)

**Verifierat problem:** per-ord-commits ger distinkta filter-hashes → "Systemutvecklare" / "Systemutvecklare Hogia" / "Systemutvecklare Hogia Göteborg" blir TRE rader; vid cap=20 evictas användarens äldre *riktiga* sökningar av mellansteg.

- **Variant A — acceptera + observera (min rekommendation för E2h):** capture är best-effort-UX, Bump/evict självläker, mellansteg åldras ut. Noll touch, ingen ny mekanik. Omvärdera vid faktisk användarsignal (recent-listan ser skräpig ut).
- **Variant B — backend "refinement-collapse":** när ny kriterie är en strikt utökning av seekerns senaste rad inom kort fönster → ersätt i stället för INSERT. Liten Application/Infrastructure-touch, men ny semantik i en levererad mekanism (ADR 0060) — eget medvetet beslut, inte E2h-fold. Samma fas → om CTO dömer "behövs nu" är det in-block/naturlig split-batch, **inte TD** (§9.6).
- **Avvisas av mig:** FE-styrd capture-hint-param (`capture=false`) — flyttar en server-side-invariant till klient-förtroende; security-auditor-territorium utan motsvarande vinst.

---

## F4 — Tab/Enter i combobox (entydig dom; CTO bekräftar)

- **Tab:** intercepta (preventDefault + välj) **endast** när listan är öppen OCH `active >= 0` (markering finns via pil eller mouseover). Annars normal fokus-flytt. **Shift+Tab interceptas aldrig.** Fokus stannar i inputen efter val (utkastet nollas, chipen committas).
- **Enter:** med markering = välj (finns redan); utan markering = tokenisera kvarvarande utkast → fritext-chip(s) + commit (ersätter dagens form-submit-bubbla — `onSubmit` preventDefault:ar redan).
- **A11y-konsekvens (medvetet APG-avsteg per Klas-direktiv, dokumenteras i kod + review):** Tab är normalt fokus-flytt i combobox-mönstret. Mitigering: (1) villkorad intercept — Tab utan synlig markering beter sig standard, så fältet är aldrig en fokus-fälla; (2) `aria-activedescendant` gör markeringen skärmläsar-synlig innan Tab får ny innebörd; (3) status-raden (`aria-live=polite`, finns redan) annonserar chip-tillägg/-borttagning ("Göteborg tillagd som filter" / "borttagen"); (4) hjälptext under labeln får bära Tab-instruktionen (label/hjälptext-regeln — ALDRIG placeholder). **Känd kvarvarande skavank:** mouseover sätter `active` → Tab efter ofrivillig hovring väljer oväntat. Det följer direkt av Klas-spec ("mouseover ELLER piltangenter"); flaggas till design-reviewer som dokumenterad konsekvens, ev. mitigering (rensa `active` vid mouseleave) är design-beslut.
- nextjs-ui-engineer laddar `jobbpilot-design-a11y`-skillen vid implementation (samma DoD-krav som E2d F5).

---

## F5 — no-JS-fallback + Sök-knappen (entydig dom; CTO bekräftar)

- **No-JS/pre-hydration = exakt dagens kontrakt:** `<input name="q">` med HELA committade q-strängen + hidden inputs för dimensionerna; native GET-submit fungerar som idag (backend-parsern är SPOT och tål rå sträng). **Viktig fallgrop som dikterar detta:** i chips-läget bär inputen bara utkastet — om den behöll `name="q"` skulle en native submit TAPPA committade fritext-ord. Lösning: efter hydration växlar komponenten till chips-läge (input utan committad q; chips + utkast renderas) via hydrated-flagga (`useSyncExternalStore`-mönstret — ingen effect, ingen lint-träff). Kort visuell växling rå-q → chips vid landning med q: acceptabel, dokumenteras.
- **Sök-knappen BEHÅLLS:** (1) no-JS-kravet — den ÄR GET-submiten; (2) JS-rollen = committa kvarvarande utkast (tokenisera) + stänga dropdownen — no-op-commit när utkastet är tomt; (3) civic-utility mental modell (synlig, förutsägbar primäraktion). Frågan "vad gör Sök när allt redan är live" har därmed ett mekaniskt svar (utkast-commit + close) — om Klas vill ge den mer (t.ex. fokus-flytt till resultatlistan, a11y-vänligt) är det design-fas, inte arkitektur.

---

## F6 — komponentstruktur (Variant A/B/C → CTO; rekommendation B)

- **Variant A — bygg om `JobAdTypeahead` till chips-input.** Mot: blandar två change-reasons (suggest/fetch/combobox-a11y vs tokenisering/chips-layout/q-orkestrering) i en komponent — samma SRP-argument som fällde E2d VAL 1 Variant B.
- **Variant B — ny `ChipSearchField` som komponerar (REKOMMENDERAS).** Wrapper-komponent som renderar chips-rad + `JobAdTypeahead`-inputen i samma visuella fält (`jp-hero__searchrow`-stylingen flyttar till wrappern); ren tokenizer i `lib/job-ads/tokenize.ts`; **`composeSuggestionChip` återanvänds oförändrad** (kind→dimension orörd — tokenizern och förslags-valet går genom samma SPOT). `JobAdTypeahead` får minimal kontrakts-utökning för Tab-select (t.ex. `selectOnTab`-prop som aktiverar F4-beteendet) — additiv, befintliga konsumenter opåverkade (OCP). `JobbHeroSearch` förblir ön som äger URL-staten och navigeringen.
- **Variant C — allt in i `JobbHeroSearch`-monolit.** Avvisas — SRP/granskbarhet, samma dom som E2d-CTO VAL 1.

**SPOT-fynd (Viktigt, in-block per §9.6 — samma fas, tredje konsumenten introduceras NU):** E2d-CTO mandaterade en delad `useSelectedChips`-SPOT; on-disk har toolbaren kvar egen inline-mappning **plus lokala useState-kopior av dimensions-listorna** (E2g-divergent mönster som bara överlever via Suspense-remount). E2h ska extrahera ren `buildChipModels(axes, labelResolver) → ChipModel[]` (+ borttagnings-helpers `removeChipFromState(state, axis, conceptId)` / `removeQWord(state, word)`) i `lib/job-ads/`, konsumerad av både toolbar och fält. Label-källorna skiljer (toolbar: server-resolverade labels per ADR 0043; fältet: taxonomy-trädet) → injicerad label-resolver, inte två deriveringar. × i fältet = samma state-operation som toolbar-× — buggklassen "två borttagnings-vägar i osynk" stängs by design. Toolbarens useState-kopior ersätts med prop-derivering i samma touch (liten yta, samma komponentfamilj).

**Backspace i tomt input = ta bort sista chipen:** JA, ingå — etablerat chip-input-mönster, liten yta, kräver bara aria-live-annonsen från F4. Borttagning går genom samma helpers (SPOT).

**Topologi-krav (icke-förhandlingsbart):** `JobbHeroSearch` förblir utanför den searchParams-key:ade Suspense-gränsen och får ALDRIG key:as på sök-state — annars remount per ord → fokus + utkast förloras (återinför E2d-buggkänslan i ny form).

---

## Lager- och SPOT-sammanfattning

| Bit | Plats | Motiv |
|---|---|---|
| Tokenizer (ord→chip-beslut, `+`/`-`-strip, edge-cases) | `lib/job-ads/tokenize.ts` (ren funktion) | Testbar utan DOM (§2.4); FE-ACL mot taxonomy-trädet |
| kind→dimension | `composeSuggestionChip` OFÖRÄNDRAD | SPOT — tokenizer + förslags-val samma väg |
| Chip-modell-derivering + borttagning | `lib/job-ads/` delad (`buildChipModels` + remove-helpers), konsumeras av toolbar + fält | DRY/SPOT (E2d-CTO F4-mandatet, nu med tredje konsument) |
| URL-skrivning | `buildJobbHref` OFÖRÄNDRAD | SPOT, symmetriskt param-bevarande |
| `ChipSearchField` (chips + input, F6 B) | `components/job-ads/` (ny client-komponent) | SRP — komposition, inte ombyggnad |
| q-hygien (min/whitespace/trunkering) | Backend `SearchQueryParser` OFÖRÄNDRAD | SPOT — FE duplicerar inte parserns regler; FE-guard endast max-längd (validator-skydd) |
| Minus-operator (NOT) | INGENSTANS i E2h (tokenizer strippar ledande `-`) | Out-of-scope, Klas-dom pending; backend-beroende (ADR 0062-notat-kandidat vid GO) |
| Recent-capture-justering (F3.3 B) | EJ E2h (CTO-triage; om vald: egen Application/Infra-touch) | Ny semantik i levererad ADR 0060-mekanism — medvetet beslut, ej fold |

**Inga Clean Arch-lagerbrott.** Backend orört i primär väg. Inga nya dependencies.

## §9.6-triage (TD ja/nej)

- F3.3 recent-capture-spam — **INTE TD:** CTO-triage acceptera/observera (A) vs liten backend-touch (B); B är samma fas → in-block/split-batch om vald.
- Minus-operator — **INTE TD:** explicit out-of-scope med backend-beroende; eget Klas-beslut (ADR 0062-notat-kandidat), ej uppskjuten skuld. Tokenizer-strippen är E2h-krav (neutraliserar accidental semantik).
- F6 SPOT-extraktion + toolbar-useState-sanering — **INTE TD:** in-block (samma fas, DRY-krav när tredje konsumenten införs — samma dom som E2d-CTO F4).
- Inga andra fynd motiverar TD.

## Referenser

- Robert C. Martin, *Clean Architecture* (2017) — kap. 7 (SRP: F6), komponent-cohesion (F1/F6)
- Hunt/Thomas, *The Pragmatic Programmer* (1999) — DRY/SPOT (composeSuggestionChip, buildJobbHref, buildChipModels, parser-som-q-SPOT)
- Martin Fowler, *Refactoring* (2018) — Speculative Generality (F3.3 B avvisas som default; F2 ingen FE-dubbel-validering)
- Eric Evans, *DDD* (2003) — kap. 14 ACL (taxonomy-trädet som match-korpus)
- ADR 0067 Beslut 5/5a/5b/5c + notat D2/E2b/E2d/E2g; ADR 0062 (websearch_to_tsquery-semantik, `-` = NOT); ADR 0042 B (OR-inom/AND-mellan); ADR 0060 (recent-capture); ADR 0045 (perf-budget); ADR 0043 (label-resolvering)
- CLAUDE.md §2.4, §4.3, §5.2, §9.6; memory `feedback_no_placeholder_example_text` (F4-hjälptext)
- E2d-domar: `docs/reviews/2026-06-11-sok-paritet-e2d-architect.md`, `-e2d-cto.md`
- jobbpilot-design-a11y-skill (F4 Tab-avstegets mitigering, implementation)
