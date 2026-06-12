# senior-cto-advisor — Fas E2j sök-commit-modellen (CTO-dom)

**Datum:** 2026-06-12
**Agent:** senior-cto-advisor (decision-maker)
**Underlag:** `docs/reviews/2026-06-12-sok-paritet-e2j-architect.md` (läst i sin helhet) · ADR 0060 Beslut 3 + Mekanik-not 1/2/5/6 (läst verbatim) · E2i-domarna `docs/reviews/2026-06-11-sok-paritet-e2i-cto.md` + addendum 2026-06-12 (I1, C′, `recentCommits`, `commit()`-vägen, `applyClaimsDelta`).
**Empiriskt fynd (bekräftat denna session):** `recent_job_searches` full vid cap=20 för en seeker av live-`router.replace`-mellanstegsspam som evictar äkta committade sökningar.
**Status:** Beslutsdom (read-only). Inga kodändringar.

> **Roll-avgränsning:** Jag fäller mekanik-besluten (CTO-bestämt → CC bygger direkt) och binder implementations-invarianterna. Fyra delbeslut är PRODUKTVAL med produkt-/GDPR-vikt → Klas-STOPP. Jag ger min rekommendation per produktval så Klas-override är medveten. Klas har sista ordet.

---

## Sammanfattning av domen

Architect-resonemanget håller. Klas empiriska fynd är en äkta defekt (over-capture + data-minimerings-regression), inte en preferens. Jag fäller:

| # | Fråga | Dom | Typ |
|---|---|---|---|
| 1 | Capture-trigger | **Variant B (commit-flagga på befintlig query)** — B ≠ ADR 0060:s avvisade B | PRODUKTVAL (rek. B), Klas-STOPP |
| 2 | ADR 0060-amendment | **Amendment, inte ny ADR** — Klas-GO på substansen | Klas-STOPP (substans) |
| 3 | Sök-knapp | **Behåll** (4 jobb) | PRODUKTVAL (rek. behåll), Klas-STOPP |
| 4 | ×-semantik | native × bort = bestämt; **(ii) text + claimade filter** | PRODUKTVAL (i/ii/iii), Klas-STOPP |
| 5 | commit utanför `JobbUrlState` | **Väg 2 — commit strikt utanför state, FE strippar efter mount** | CTO-BESTÄMT |
| 6 | Toolbar-commits bär commit=1 | **Ja** (avsiktliga, diskreta) | PRODUKTVAL (rek. ja), Klas-STOPP |
| 7 | security-auditor | **JA, obligatorisk** | CTO-BESTÄMT |

---

## VAL 1 — Capture-trigger (är architectens "B ≠ avvisad-B" korrekt?)

**Dom: JA — architectens resonemang är korrekt och tillräckligt för att INTE bryta mot ADR 0060. Variant B (commit-flagga) är rekommenderad capture-trigger.** PRODUKTVAL (produkt + GDPR-vikt) — Klas bekräftar trigger-valet.

**Motivering mot principer:**

- **ADR 0060 Beslut 3 avvisade en *separat command med egen round-trip* — inte ett predikat på en befintlig query.** Jag prövade de fyra avvisnings-grunderna verbatim mot commit-flaggan och de träffar inte:
  - *"Trust-flytt till klient"* — den enda trust som flyttas är NÄR användarens EGEN historik fångas. JobSeeker-lookup sker fortfarande server-side via `currentUser.UserId` (Capturer-invarianten orörd). Klienten kan inte capture:a för annan seeker, inte injicera annan data, inte kringgå auth. Worst case är benignt: klienten över-/under-captar sin egen bekvämlighets-historik. Lägsta tänkbara känslighet.
  - *"Dubbla round-trips"* — eliminerad. Flaggan rider på list-queryn som ändå körs. Noll extra HTTP. Detta var avvisade-B:s tyngsta tekniska argument, och commit-flaggan har det inte.
  - *"Race mellan list-render och capture"* — eliminerad. Samma query, samma pipeline, samma post-UnitOfWork-ordning (Mekanik-not 1). Ingen ny race.
  - *"FE måste persistera filter-shape"* — falskt. Filter-shapen ligger redan i URL:en (E2g-arvet). FE sätter en boolean på en query den redan bygger.
- **Architectens slutsats är arkitektoniskt riktig: commit-flaggan ligger NÄRMARE ADR 0060:s *accepterade* Variant A än dess avvisade B.** Vi behåller behaviorn, markör-patternet (`ICapturesRecentSearch`), best-effort-semantiken och UoW-ordningen — vi adderar **ett predikat** till markör-kontraktet. Detta är open/closed-konformt (Martin 2017 kap. 8): behaviorns no-op-kedja (rad 39–63) får ett villkor till, ingen ny abstraktion, ingen ny port, ingen Domain-påverkan. `SearchCriteria`-VO:t och Capturer-invarianten är orörda (SPOT — Hunt/Thomas 1999).
- **"Capture endast vid commit" KRÄVER en explicit FE-signal — det finns ingen serverside-heuristik som kan rekonstruera commit-intent.** Att gissa vore Programming by Coincidence (Hunt/Thomas 1999 kap. 6) — exakt den anti-grund CTO redan dömt mot i E2i-addendum Beslut 1. Backend ser `router.replace` och `router.push` som identiska `GET /api/v1/job-ads`; intentet måste bäras explicit.
- **GDPR Art. 5(1)(c) — data-minimering:** Variant B är inte bara en UX-fix utan en materiell minimerings-förstärkning. Vi persisterar då endast de söktermer användaren explicit committade — den minimala mängd ändamålet (snabbåtkomst till avsiktliga sökningar) kräver. Det stärker också Art. 13-disclosurens sanningshalt ("vi sparar sökningar du kör" blir bokstavligt sant).

**Avvisade alternativ:**

- **Variant A (status quo):** empiriskt falsifierad. ADR 0060 Beslut 3 (2026-05-20) skrevs i en värld där en sökning = ett `router.push` per intention. E2i (2026-06-11) rev premissen med live-`router.replace` per ord. A levererar nu motsatsen till sitt syfte (committade sökningar evictas av mellanstegsspam) + är en data-minimerings-regression. A är defekten, inte ett alternativ.
- **Variant C (separat command/endpoint):** ÄR den literala ADR 0060-avvisade B. Löser inget B inte löser billigare (YAGNI — Beck; Martin 2017). Återinför round-trip + race + filter-shape-duplicering.
- **Variant D (live-capture med dedup/coalesce):** avvisas hårt. (1) Löser fel problem — Klas mentala modell är "jag tryckte Sök ⇒ den sparades", inte "systemet gissade via en timer". (2) Stateful + heuristisk sessions-fönster-spårning = exakt det tidsberoende blur-/debounce-mönster CTO redan avvisade i E2i VAL 3. (3) Sämst GDPR: data-minimering är *insamlings*-minimering (Art. 5(1)(c)), inte retention-städning efteråt — D samlar in mellanstegen och städar sen, B samlar aldrig in dem. Mer kod, mer state, sämre posture.

---

## VAL 2 — ADR 0060-amendment (amendment / ny ADR / inget?)

**Dom: Amendment till ADR 0060 — inte ny ADR, inte "inget". Klas-GO krävs på amendment-substansen** (det är en av de fyra Klas-STOPP-punkterna nedan; sammanfaller med VAL 1 eftersom amendmentet kodifierar trigger-valet).

**Motivering mot principer:**

- **Beslut 3:s *substans* består oförändrad** — post-handler-behavior, markör-driven, best-effort, Variant A. Vi river den inte; vi preciserar trigger-villkoret ("varje lyckad ICapturesRecentSearch-query" → "...*med commit-intent*"). Mekanik-konkretisering inom ett Accepted-mandat dokumenteras som amendment, inte ny ADR — precedens E2b/D2-notaten.
- **Men amendment KRÄVS** (inte "inget") eftersom Beslut 3:s avvisnings-text för "Variant B" annars blir en framtida fälla: en läsare ser "explicit FE-signal avvisad" och felklassar commit-flaggan som ett ADR-brott. Detta är precis "ADR-mekanik-ordalydelse ≠ miljö/fas-entydig"-situationen som memory `feedback_adr_mechanism_vs_env_phase_triage` (TD-13 C1 J3) varnar för: en Accepted-mekaniks ordalydelse kolliderar med en ny fas-verklighet (live-sök fanns inte 2026-05-20). Per den lärdomen triggar detta CTO-triage (gjord här), inte CC-omdöme — och utfallet är amendment.
- **Varför Klas-GO på substansen:** amendmentet *omtolkar en Accepted-ADR:s avvisade variant*. Det är en strategisk transition Klas ska se medvetet, även om mekaniken är entydig. Detta är inte CC:s att verkställa tyst. (memory `feedback_klas_can_override_adr_verbatim_source`: om Klas ber CC skriva ADR-prosan direkt överrider det §9.4 webb-Claude-verbatim — men GO på *substansen* står kvar oavsett vem som skriver.)

**Amendmentet ska (bindande innehåll, författas av adr-keeper efter Klas-GO):**
(a) konstatera att E2i:s live-sök rev Beslut 3:s implicita premiss "en query = en intention";
(b) precisera att commit-flaggan ≠ avvisade Variant B (separat command) — med de fyra grunderna avförda (tabellen i VAL 1);
(c) uppdatera Mekanik-not 2 (default-browse-guarden får sällskap av en commit-guard);
(d) notera GDPR-data-minimerings-förstärkningen (Art. 5(1)(c)).

---

## VAL 3 — Sök-knapp (behåll/ta bort)

**Dom: Behåll. PRODUKTVAL — rek. starkt behåll.** Klas avgör.

**Motivering mot principer:** knappen har fyra distinkta, icke-redundanta jobb — tre blir *nödvändiga* så fort capture-på-commit införs:

1. **Finalisera pågående ord (CTO VAL 3-arvet från E2i):** caret-segmentet exkluderas från parse (tokenize caret-exkludering). Utan knappen finns ingen väg att committa ett pågående sista ord annat än att skriva mellanslag — en upptäckbarhets-fälla (least astonishment, Martin 2008).
2. **Commit-signalen (VAL 1):** Sök/Enter är den naturliga commit-punkten som sätter `commit=1`. Färre explicita commit-punkter = otydligare capture för användaren.
3. **No-JS-submit:** `<form action="/jobb" method="get">` submittar via knappen utan JS. Att ta bort submit-kontrollen bryter progressive enhancement (CLAUDE.md §5.2).
4. **A11y:** WAI-ARIA combobox hanterar förslags-val; "kör sökningen" är en separat affordance. En screenreader-användare som skrev fritext utan att välja förslag behöver explicit "Sök".

Om Klas ändå vill ta bort: de fyra jobben måste lösas på annat vis (Enter-only + synlig instruktion + commit-intent härlett ur Enter/förslags-val). Görbart men strikt sämre.

---

## VAL 4 — ×-semantik

### Arkitektur-bestämt (CTO, inget val): native × MÅSTE bort

Bekräftat. `<input type="search">` renderar WebKit/Blinks `::-webkit-search-cancel-button` som rensar `value` men inte committar någon delta (ingen avgränsar-keystroke) → filter överlever i URL; Firefox visar aldrig knappen → cross-browser-inkonsekvens. Suppress native (`appearance-none`) + rendera **kontrollerad custom clear-knapp** vars onClick går genom en interceptbar React-väg. Detta är korrekthet + cross-browser, inget produktval — gäller oavsett vilken semantik Klas väljer nedan.

### PRODUKTVAL: vad rensar ×?

**Dom: rek. (ii) — rensa text + de filter texten gjorde anspråk på (`parse(text)`-delmängden via `applyClaimsDelta` med `next = EMPTY_CLAIMS`), lämna popover-dimensioner.** Klas avgör (i/ii/iii).

**Motivering mot principer:**

- **(ii) är minst förvånande och konsistent med C′:s grundaxiom (E2i VAL 1):** *fältet äger sitt eget bidrag, inte hela staten.* "× på sökfältet rensar det jag skrev i fältet och dess effekt; mina popover-val rör den inte" speglar exakt vad fältet visar. I1 (`parse(text) ⊆ state`) hålls trivialt: tom text ⇒ `parse(∅) ⊆ state`.
- **(i) text-only ÄR dagens bugg** — "text borta, jobb kvar filtrerade", precis det Klas rapporterade. Avvisas som default; det är problemet, inte lösningen.
- **(iii) ingen × bryter Platsbanken-paritet** (memory `project_platsbanken_parity_baseline`) + standard search-input-konvention (Safari/Algolia/GOV.UK clear-×). Avvisas mot paritets-baseline.

**Implementations-invariant (bindande, inte ett val):** (ii):s clear är en **egen commit** och MÅSTE gå genom `commit()`-vägen (`recentCommits`-registrering, E2i addendum Beslut 1) — annars bryts own-roundtrip-detektorn och texten serialiseras om vid props-retur (E2d/E2h-felklassen). Konkret: × sätter `text=""`, kör delta-logiken med tomma claims mot `lastCommitted`, committar, nollar `prevClaims` till `EMPTY_CLAIMS`. Symmetriskt med befintlig `onFieldChange` — ingen ny invariant-risk.

---

## VAL 5 — commit-signal utanför `JobbUrlState` (bind exakt mekanik)

**Dom: Väg 2 — `commit` hålls STRIKT utanför `JobbUrlState`/`sameUrlState`/`serializeSearchText`/`buildJobbHref`. JS strippar `?commit=1` efter mount. CTO-BESTÄMT** (rör E2i:s ömtåligaste invarianter — mekanik, inte produktval).

**Motivering mot principer:**

- **Separation of Concerns (Martin 2017 kap. 7): `commit` är en *signal*, inte ett *tillstånd*.** Tillstånd hör i `JobbUrlState` (q/occupationGroup/region/municipality/sortBy); en fire-and-forget commit-intent gör det inte. Att blanda in den i state-shapen vore att låta en transient signal förorena sanningsmodellen — accidental complexity.
- **E2i-invariant-skydd (verifierat mot invariant-checklistan):** om `commit` nådde `sameUrlState` skulle own-roundtrip-detektorn (`recentCommits`) miss-matcha; om den nådde `serializeSearchText`/`updateTextForStateChange` skulle den läcka in i fält-texten. Båda är E2d/E2h-felklassen. Därför: `commit` ALDRIG i `JobbUrlState`, ALDRIG i `sameUrlState`-jämförelsen, ALDRIG i `buildJobbHref`/`serialize`.

**Bindande mekanik:**

1. **No-JS:** statiskt `<input type="hidden" name="commit" value="1">` i formet. No-JS-submit ÄR per definition en commit (användaren tryckte Sök) → `commit=1` alltid-på i no-JS-formet är korrekt.
2. **JS — live-`router.replace` (onFieldChange-delimiter):** bygger href via `buildJobbHref(next)` UTAN `commit`. Aldrig commit på live-replace.
3. **JS — commit-punkter** (`onSubmitText`/Enter/Sök, `onSelectSuggestion`/förslags-val, ×-clear (ii), och toolbar-commits om VAL 6 = ja): adderar `commit=1` som query-string-suffix **ovanpå** `router.push`-strängen — inte via state, inte via `buildJobbHref`. `commit` ingår INTE i `resultsKey`/chip-state.
4. **FE strippar `?commit=1` efter mount** (`router.replace` till ren URL) så en delad/bokmärkt `?...&commit=1`-länk inte re-capture:ar hos mottagaren. Detta är en E2i-känslig `router.replace` — den MÅSTE gå genom `commit()`-vägen så `recentCommits`-detektorn registrerar den som egen roundtrip (annars triggar strip-replacen en falsk extern-divergens-synk av texten). Strip-replacen bär INTE `commit=1` (det vore självmotsägande).
5. **Backend ser `commit=1` identiskt i JS och no-JS** → uniform behavior-gate. Uniformiteten Klas efterfrågar uppnås på BE-sidan utan att FE delar state-shape.

**Edge bunden:** en delad `?...&commit=1`-länk → mottagarens första list-query bär `commit=1` → capture i mottagarens EGEN historik. Benignt (egen historik, ingen cross-tenant), men strip-efter-mount (punkt 4) eliminerar fönstret i praktiken, och security-auditor kvitterar det explicit.

---

## VAL 6 — Toolbar-commits bär commit=1?

**Dom: Ja — `removeChip`/`clearAllFilters`/`onSortChange` bär `commit=1`. PRODUKTVAL (rek. ja, dimensionerar insamlingsvolym) — Klas bekräftar.**

**Motivering mot principer:**

- **Toolbar-handlingar är avsiktliga, diskreta `router.push`** — till skillnad från live-typing. Att ta bort ett filter och se färre/fler träffar ÄR en sökning användaren kan vilja återfinna. Konsistent med "commit = avsiktlig handling, inte mellansteg".
- **GDPR-avvägning är benign:** även med toolbar-commits fångas endast diskreta, användar-initierade tillstånd — inte keystroke-spam. Volymökningen är bunden av antalet avsiktliga toolbar-klick, inte av ordlängd. Data-minimeringen (Art. 5(1)(c)) är fortsatt uppfylld: vi fångar avsiktliga sökningar, inte mellansteg.

**Varför Klas bekräftar:** detta dimensionerar insamlingsvolymen (fler commit-punkter = mer capture). Det är en proportionalitets-/produktavvägning Klas äger. Min rek. är ja, men en Klas som vill minimera maximalt kan välja "endast hero-fält/Enter/förslags-val bär commit=1, toolbar bär inte" — strikt mindre capture, marginellt sämre återfinnbarhet. Medvetet override-utrymme.

---

## VAL 7 — security-auditor obligatorisk?

**Dom: JA, obligatorisk. CTO-BESTÄMT** (CLAUDE.md §9.2 — kod som rör PII).

PII-insamlingsvägen ändras: NÄR söktermer (PII) persisteras flyttas. Även om ändringen *minskar* insamling måste auditorn verifiera:
(a) live-`router.replace` fångar bevisligen INTE längre (ingen läcka via glömd commit-flagga på någon live-väg);
(b) commit-flaggan kan inte forgeras till skadlig capture (worst case benignt = egen historik — auditorn konstaterar det);
(c) delad/bokmärkt `?commit=1`-länk-edgen + strip-efter-mount-mitigeringen (VAL 5 punkt 4);
(d) Art. 13-disclosuren fortsatt korrekt (nu mer sanningsenlig). Obligatorisk invocation, inte valfri.

---

## EXPLICIT KLAS-STOPP-LISTA (svara innan CC kodar)

CC presenterar dessa till Klas med mina rekommendationer. CC ger INGEN egen rek utöver min dom — jag är domaren, Klas äger produktvalen.

1. **Capture-trigger** *(produkt + GDPR-vikt)*
   - **A** — capture varje list-query (status quo; spam består, data-minimerings-regression)
   - **B** — commit-flagga `&commit=1` endast vid Enter/Sök/förslags-val; live-replace utelämnar **← CTO-rek**
   - **D** — live-capture med dedup-fönster (stateful, heuristisk, sämre GDPR)

2. **ADR 0060-amendment-substans** *(omtolkar Accepted-ADR:s avvisade variant)*
   - **GO** — författa amendment (preciserar Beslut 3 mot live-sök-premissen; B ≠ avvisad B) **← CTO-rek**
   - (Avslag/ny ADR vore inkonsekvent med E2b/D2-precedensen — men Klas äger transitionen)

3. **Sök-knappen**
   - **Behåll** — finaliserar pågående ord, commit-signal, no-JS-submit, a11y **← CTO-rek**
   - **Ta bort** — Enter-only; kräver annan lösning för de fyra jobben

4. **× i sökfältet** *(native × bort = redan bestämt, oavsett val)*
   - **(i)** rensar endast texten (filter kvar — dagens förvirring)
   - **(ii)** rensar texten + de filter texten gjorde anspråk på; popover-val kvar **← CTO-rek**
   - **(iii)** ingen ×-knapp (bryter Platsbanken-paritet)

5. **Toolbar-handlingar (ta bort chip / Rensa alla / byt sort) bär commit=1?** *(dimensionerar insamlingsvolym)*
   - **Ja** — avsiktliga, diskreta handlingar sparas som sökningar **← CTO-rek**
   - **Nej** — endast hero-fält-commits sparas (strikt mindre capture)

CTO-BESTÄMT (inget Klas-STOPP, CC bygger direkt efter Klas svarat på 1–5 ovan): native × bort + kontrollerad knapp · `commit` utanför `JobbUrlState`/`sameUrlState`/`serialize` (VAL 5 väg 2 + strip-efter-mount) · × och commit-punkter via `commit()`-vägen · security-auditor triggas.

---

## IMPLEMENTATIONS-BINDNINGAR (invarianter CC INTE får bryta)

1. **`commit` strikt utanför `JobbUrlState`/`sameUrlState`/`serializeSearchText`/`buildJobbHref`** (VAL 5 väg 2). Transient query-string-suffix på commit-punkternas `router.push`; aldrig på live-`router.replace`; aldrig i `resultsKey`/chip-state.
2. **Alla commit-punkter + ×-clear (ii) + FE-strip-efter-mount går genom `commit()`-vägen** (E2i addendum Beslut 1 — `recentCommits` own-roundtrip-registrering). Annars serialiseras texten om = E2d/E2h-felklassen.
3. **`commit`-markören på query följer ADR 0060:s markör-pattern** — `bool Commit = false` default-property på `ListJobAdsQuery` (record-property matchar `ICapturesRecentSearch`-shapen automatiskt, paritet `Since`/`Page`). Behaviorn (rad 39–63) får **ett villkor** till i no-op-kedjan: `commit == false ⇒ no-op`. Ingen ny abstraktion, ingen ny port, ingen Domain-påverkan. `SearchCriteria`-VO + Capturer-invarianten orörda.
4. **Best-effort + UoW-ordning + default-browse-guard (Mekanik-not 1/2) består** — commit-guarden är additiv, inte ersättande. Tom sökning capture:as fortfarande aldrig (commit-guard OCH browse-guard).
5. **No-JS-formet bär statiskt `commit=1`**; spegel-input förblir namnlös post-hydration (E2i M4-kontraktet — `name="q"` skulle dubbel-filtrera). Verifieras att hydration-text-växlingen + namnlöshet består.
6. **test-writer FÖRE produktionskod** (CLAUDE.md §2.4 / §9.2): commit-guarden ändrar capture-*beteendet* — backend behavior-test som bevisar `commit=false ⇒ no-op` och `commit=true ⇒ capture` skrivs först. FE delta-/own-roundtrip-test för ×-clear (ii) + strip-efter-mount likaså.
7. **DI:** commit-flaggan rör INTE backend-shape utöver en query-property + ett behavior-villkor — ingen ny registrering. Om security-auditor eller implementation kräver ny port (förväntas EJ) → tillbaka till CTO. Pipeline-ordningen (Mekanik-not 1 + arch-test) är oförändrad.
8. **`commit` får inte bli en magic string** (CLAUDE.md §5.1) — query-param-namnet konstant-deklareras där FE bygger href + där BE binder, samma disciplin som övriga param-namn.

---

## SEKVENS (efter Klas-GO på Klas-STOPP-listan)

**TDD-flöde, backend behavior FÖRST** (§2.4, §9.2):

1. **test-writer** — backend behavior-test: `commit=false ⇒ no-op`, `commit=true ⇒ capture`, default-browse + commit-guard interaktion (tom sökning capture:as ej även med commit=1). FÖRE produktionskod.
2. **Backend commit-guard** — `bool Commit = false` på `ListJobAdsQuery`; `RecentJobSearchCaptureBehavior` läser markören och no-op:ar vid `false`. Verifiera arch-test (pipeline-ordning) grön. DI orört.
3. **ADR 0060-amendment** — adr-keeper författar (a)–(d) efter Klas-GO (kan ske parallellt med steg 2; blockerar inte kod men ska in i samma PR-body).
4. **FE commit-punkter** — `commit=1`-suffix på `onSubmitText`/Enter/Sök + `onSelectSuggestion` (+ toolbar-commits om VAL 6 = ja). `commit` utanför `JobbUrlState`/`sameUrlState`/`serialize`. Live-`router.replace` utelämnar. No-JS hidden `commit=1`.
5. **FE strip-efter-mount** — `router.replace` till ren URL via `commit()`-vägen (own-roundtrip-registrering).
6. **×-knapp** — suppress native (`appearance-none`) + kontrollerad clear-knapp; semantik (ii) via `applyClaimsDelta`/`EMPTY_CLAIMS` genom `commit()`-vägen. FE-test för delta + own-roundtrip.
7. **security-auditor** — invokeras på den samlade PII-insamlingsväg-ändringen (a)–(d i VAL 7). Rapport i PR-body.
8. **code-reviewer + (om FE-ytan är >5 filer) dotnet-architect-re-touch + design-reviewer** för ×-knapp-rendered (a11y-copy, paritet) — in-scope, samma batch.

Backend-flaggan + test före FE-commit-punkter eftersom FE:s `commit=1` är meningslös tills BE-guarden honorerar den (annars broken intermediate state — memory `feedback_di_with_handlers_same_commit`-andan: behavior-ändring + dess gate i samma logiska leverans).

---

## FAS-DISCIPLIN (§9.6)

**Allt här är in-scope E2j. Inga TD-kandidater.**

- Capture-trigger, amendment, sök-knapp, ×-semantik, commit-mekanik, security-audit — samtliga adresserar **nuvarande fas defekt** (Klas empiriska fynd 2026-06-12). Inget hör till annan fas; ingen saknad dependency (behaviorn, markör-patternet, `commit()`-vägen, `applyClaimsDelta` finns alla on-disk).
- **Minus-operatorn (NOT) är redan separat Klas-pending fas** (memory `project_e2j_search_commit_model` + `project_e2h_chip_in_field_spec`: backend-fas/scope-fråga) — EJ E2j, inget nytt lyft.
- **Privacy-policy + Art. 13-inline-disclosure** (ADR 0060 Mekanik-not 6) är fortsatt Klas-uppgift, redan känd, ingen ny TD — men security-auditor ska bekräfta att den blir *mer* sanningsenlig, inte mindre, efter commit-trigger.

Default = fixa in-block. Inga tidströskel-utlyftningar (§9.6, 4h-regeln borttagen 2026-05-11). Stora fynd inom rätt fas fixas i samma batch eller naturlig split-batch.

---

## Referenser

- ADR 0060 (RecentJobSearches auto-capture) Beslut 3 + Mekanik-not 1/2/5/6 · ADR 0042 Beslut B (SearchCriteria) · ADR 0049 Mekanik-not 3/4 (markör-pattern) · ADR 0045 (perf-hygien) · ADR 0065 (PR-flöde/amendment) · ADR 0024 amend (Art. 17-cascade)
- E2i-domarna: `docs/reviews/2026-06-11-sok-paritet-e2i-cto.md` + addendum 2026-06-12 (I1, C′, `recentCommits`, `commit()`-vägen, `applyClaimsDelta`, delta-bas-icke-regress)
- Architect-dom E2j: `docs/reviews/2026-06-12-sok-paritet-e2j-architect.md`
- Eric Evans, *Domain-Driven Design* (2003), "Aggregates" (invarianter i modellen) · Robert C. Martin, *Clean Architecture* (2017) kap. 7 (SRP/SoC), kap. 8 (open/closed); *Clean Code* (2008, least astonishment) · Hunt/Thomas, *The Pragmatic Programmer* (1999) kap. 6 (Programming by Coincidence), kap. 7 (DRY/SPOT) · Kent Beck (YAGNI) · GOV.UK / Algolia / Safari search-input clear-mönster
- GDPR Art. 5(1)(c) (data-minimering), Art. 6(1)(f) (berättigat intresse), Art. 13 (informationsskyldighet)
- CLAUDE.md §2.2, §2.4, §2.5, §5.1, §9.2, §9.6 · memory `feedback_adr_mechanism_vs_env_phase_triage`, `feedback_klas_can_override_adr_verbatim_source`, `project_platsbanken_parity_baseline`, `project_e2j_search_commit_model`, `feedback_di_with_handlers_same_commit`
