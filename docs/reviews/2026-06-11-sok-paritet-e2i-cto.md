# senior-cto-advisor — Fas E2i spegel-sökfält (CTO-dom)

**Datum:** 2026-06-11
**Agent:** senior-cto-advisor (decision-maker)
**Underlag:** `docs/reviews/2026-06-11-sok-paritet-e2i-architect.md` (läst i sin helhet) + kodverifiering av `chip-composition.ts` Title-grenen (rad 43–46: `q: suggestion.label` ersätter hela q — architectens premiss bekräftad i källkod).
**Låsta Klas-beslut (döms inte om):** spegel-fält (normal text-input, inga chips i fältet); ALLA taggar inkl. fritext-q-ord i filter-raden med ×; ×-borttagning uppdaterar fältets text; **"Rensa alla filter" rensar ALLT inkl. sökorden och tömmer fältet** (Klas AskUserQuestion 2026-06-11 — architect-fråga 2 därmed avgjord, alternativ a).

---

## CTO-rekommendation

### VAL 1 — text⇄state-modell

**Beslut: Variant C′** — fältets text är användarens redigerings-buffert, URL:en är persistent sanning, invariant **I1: parse(fieldText) ⊆ urlState** (delmängd, inte likhet), delta-parse vid commit-punkter, kirurgisk text-edit vid ×, representabilitets-gated serialize ENDAST vid extern base-divergens, rundtripps-teoremet `parse(serialize(s)) ⊆ s` som property-aktig unit-test.

**Motivering mot principer:**

- **Invarianter skyddas i modellen, inte i handlers (Evans 2003, "Aggregates"; CLAUDE.md §2.2):** C′ är det enda alternativet där invarianten är *formulerbar och bevisbar*. Likhets-invarianten i ren C är falsifierad redan av första popover-valet; delmängds-invarianten I1 är exakt det anspråk texten faktiskt gör. En invariant som inte kan hålla är ingen invariant — det är en förhoppning.
- **SPOT/DRY (Hunt/Thomas 1999, kap. 7):** URL:en förblir enda persistenta sanningen (E2g-arvet); texten är en vy med eget redigerings-ansvar. ×-borttagning går fortsatt genom `removeChipFromState` — en knowledge piece, ett ställe. Toolbar→fält-kommunikation återanvänder URL-bussen i stället för ny cross-island-kanal — ingen andra sanning införs.
- **SRP (Martin 2017, kap. 7):** parse, diff, apply-delta, serialize, representabilitets-predikat är separata rena funktioner i `tokenize.ts` med var sin change-reason. Detta uppfyller även §2.4 (testbart utan DOM/komponent).
- **Fitness function (Ford/Parsons/Kua 2017):** rundtripps-teoremet som property-test är en arkitektonisk fitness function, inte ett vanligt exempel-test — den vaktar I1 mot framtida tokenizer-ändringar. Obligatorisk del av scopet, inte nice-to-have.

**Avvisade alternativ:**

- **Ren A (full serialize/re-parse):** texten re-ordnas kanoniskt under användarens caret — bryter ordagrant det Klas godkände (texten han skrev står kvar). Rundtrippen är dessutom obevisbar för hela label-rymden (ambiguösa labels, komma-labels, operator-prefix, cross-boundary-capture) och ren A saknar plan för undantagen. Detta är snabblösningen förklädd till symmetri.
- **Ren B (q-only-spegling):** bryter Klas låsta preview — "Göteborg" får inte försvinna ur fältet när ordet taggas. Inget arkitekturresonemang trumfar låst produktval. Avvisas utan vidare prövning.
- **Ren C (likhets-invariant):** läcker vid varje extern state-ändring (popover, toolbar-×, popstate, recent-search) och invarianten är obevisbar från start. C′ är C med rätt invariant och rätt resync-mekanism — det är skillnaden mellan design och hopp.

**Trade-offs accepterade:** fältet är *best-effort-spegel*, filter-raden är *total spegel* — icke-representabla dimensioner syns enbart i raden. Asymmetrin är medveten och ska stå i komponent-doc-kommentaren precis som architecten kräver. Komplexiteten (diff/delta i stället för replace) är priset för en bevisbar invariant; alternativen har lägre kodkostnad och högre korrekthetsskuld.

**Klas-GO:** krävs ej. C′ är entydigt motiverad mot principer och implementerar de låsta produktvalen. **CC bygger direkt.**

---

### VAL 2 — greedy longest-match multi-ord i tokenizern

**Beslut: Godkänd** exakt per architect-regeln: längsta **unika** n-gram vinner; ambiguöst n-gram → prova kortare; ambiguitet på längd 1 → fritext (befintligt basfall); n-gram spänner aldrig över komma; max-n-gram-längd exporteras från `buildLabelIndex`.

**Motivering:**

- **Nödvändig, inte opportunistisk:** rundtripps-teoremet i VAL 1 är obevisbart utan multi-ord-match — `serialize` av "Upplands Väsby" som sedan parse:as till två q-ord bryter I1. Detta är en dependency av beslutet ovan, inte feature creep (YAGNI-invändning ogiltig).
- **"Gissa aldrig"-regeln bevaras (Clean Code, Martin 2008 — principle of least astonishment):** ambiguitet degraderar deterministiskt nedåt till fritext i stället för att tokenizern väljer åt användaren. Disambiguering sker där den hör hemma: explicit förslags-val via `composeSuggestionChip` (ADR 0067 Beslut 5b, SPOT).
- **Ingen magic constant (CLAUDE.md §5.1):** max-n-gram härleds ur taxonomin och exporteras från indexet — bounded, datadrivet, självunderhållande.
- Bonusen att handskrivet "Upplands Väsby"/"Stockholms län" auto-matchar är Klas-visionens beteende — paritetslinjen (Platsbanken-baseline) stärks.

**Klas-GO:** krävs ej. **CC bygger direkt.**

---

### VAL 3 — M2 parse-timing

**Beslut: Caret-segment-exkludering godkänd.** Commit-punkter = avgränsar-keystroke i aktiva ordet (omedelbart), Enter/Sök (finalizeAll), förslags-val. `parseSearchText(text, caretIndex)` exkluderar caret-segmentet tills caret lämnar det eller avgränsare/Enter kommer. **Ingen blur-finalize. Ingen debounced full-reparse.** q-max-guarden flyttar in i parse (vägrade ord = kvar som text + `limitNotice`); `router.replace {scroll:false}` och toolbar-push-asymmetrin består (E2h VAL 2).

**Motivering:**

- **Debounced full-reparse avvisas på två grunder:** (1) den committar halvfärdiga mid-text-ord ("götebor") till URL = fel state som persistent sanning — korrupt data i sanningskällan är värre än fördröjd vy; (2) server-refetch per debounce-fönster bryter ADR 0045-hygienen (perf-regression utan motivering = disciplinmiss per CLAUDE.md §2.5). Dessutom bryter den omedelbar-vid-delimiter-kravet i kanten.
- **Blur-finalize avvisas per YAGNI (Martin 2017; Fowler):** utanför spec, och blur-commits är klassisk källa till överraskande state-ändringar när användaren klickar i popovern (focus lämnar fältet ≠ användaren är klar).
- **Caret-exkludering är generaliseringen av befintligt mönster** (`remainderSeed`) — inte ny mekanism utan utvidgning av etablerad (CLAUDE.md §9.1: befintliga mönster före nya). Konsekvensen att träfflistan visar gamla filtret medan ett ord redigeras mitt i är deterministisk och förklarlig — rätt trade-off mot momentana filter-släpp + q-commit av fragment.

**Klas-GO:** krävs ej. **CC bygger direkt.**

---

### VAL 4 — architect-rekommendationer (a)–(d)

**(a) Popover-val speglas EJ i fältets text — BEKRÄFTAD.**
Detta är inget öppet produktval: det följer av Klas låsta beslut. "Normal ruta som speglar söket" = det användaren *skrev*; filter-raden med ALLA taggar = den totala spegeln. Att skriva in popover-val i texten återinför exakt det kanonisk-ordnings-hopp som ren A avvisades för, och bryter I1:s riktning (texten ska aldrig göra anspråk den inte kan bära — ambiguösa popover-labels skulle degradera till q vid nästa parse). Fältet = buffert för eget bidrag; raden = total sanning. **CC bygger direkt; semantiken dokumenteras i komponent-doc + PR-body** så Klas ser den post-merge — genuint produktval återstår inte, men synligheten ska finnas.

**(b) Title-grenen ERSÄTT → APPEND-med-dedupe — BEKRÄFTAD.**
Kodverifierat: `chip-composition.ts:46` gör `{ ...current, q: suggestion.label }` — med q-ord som taggar i raden raderar ett Title-val tyst användarens samtliga övriga sök-taggar. Det är en destruktiv sido-effekt användaren inte bett om (least astonishment) och inkonsekvent med funktionens egen dokumenterade semantik "OR-inom + dedupe" för alla andra grenar. Append-med-dedupe gör Title-grenen konsekvent med modulens kontrakt — detta är att *laga* en semantisk inkonsekvens som E2i exponerar, inte scope creep. **Kontraktsändring i delad funktion: egna test-uppdateringar i `chip-composition.test.ts` + explicit omnämnande i PR-body** (architectens krav, bekräftat). **CC bygger direkt.**

**(c) M4-rivningen — BEKRÄFTAD.**
`chip-search-field.tsx` + `.jp-chipfield`/`.jp-filterchip--field`-CSS + `onEmptyBackspace` + `inputRef` raderas; `chip-search-field`-testerna raderas med komponenten. Död kod tas bort, inte deprecate:as (YAGNI; Fowler 2018, "Dead Code" — repot har git-historik som arkiv). `selectOnTab` består (Klas-spec), aria-annonserna "Lade till/Tog bort" består och är *viktigare* nu när den visuella feedbacken sitter långt från fältet (DoD §8.6). No-JS-kontraktet bekräftas särskilt: synliga inputen förblir **namnlös** post-hydration — `name="q"` på spegel-texten skulle native-submitta "Göteborg systemutvecklare" som q och dubbel-filtrera. Hydration-text-växlingen dokumenteras. **CC bygger direkt.**

**(d) q-taggens aria-label-copy + ev. ikon — DELEGERAS till design-reviewer.**
Korrekt instans-tilldelning: "Ta bort filter X" är semantiskt fel för ett sökord (ett sökord är inte ett filter i användarens mentala modell) — men copy- och särskiljnings-beslutet ägs av design-reviewer (DESIGN.md + a11y-skill), inte CTO. **CC invokerar design-reviewer in-scope i samma batch** (rendered-granskning av filter-raden ingår ändå i E2i-flödet) — ingen separat fas, ingen TD.

---

### "Rensa alla filter" — verkställighet av Klas-beslutet

Klas har valt alternativ (a): nolla ALLT inkl. q, fältet töms. Verkställs som del av scopet. **Premiss-skiftet dokumenteras i PR-body:** E2e-domen "q bevaras vid Rensa alla" byggde på att q inte var en tagg i raden — E2i river premissen och Klas har explicit omprövat. Detta förhindrar att framtida sessioner läser E2e-domen som gällande (granskningstrail, Fowler 2018 / CLAUDE.md §9.7-andan).

---

### In-block-fixar (§9.6)

Allt ovan är ETT scope (E2i, nuvarande fas) — inga splittringar:

- `tokenize.ts`: ord-sekvens-index, `parseSearchText(text, caretIndex?)`, `diffParse`/`applyParseDelta`, `serializeSearchText` + `isTextRepresentable`, exporterad max-n-gram-längd; `tokenize.test.ts` skrivs om in-scope inkl. rundtripps-property-testet
- `chip-composition.ts:43-46`: Title-grenen → append-med-dedupe + testuppdatering
- Toolbar: `includeQ: true`-flipp + Rensa-alla-scope-ändringen (rensa allt, töm fält)
- M4-rivningen komplett (komponent + CSS + props + tester)
- Komponent-doc: best-effort-spegel vs total spegel, popover-semantik, hydration-text-växling

### Genuina TDs (lyfts)

**Inga.** Architectens bedömning bekräftad mot §9.6: allt hör till nuvarande fas, inga saknade dependencies. Minus-strippen förblir Klas-pending egen fas (redan beslutad, inget nytt lyft).

### Testdisciplin

Architectens ordning bekräftad och är bindande: delta-sync, caret-exkludering, representabilitets-gaten och rundtripps-teoremet är rena funktioner — **unit-tester FÖRE komponent-wiring** (CLAUDE.md §2.4, test-writer-flöde §9.2).

### Klas-GO-sammanfattning

Inga STOPP krävs. Samtliga genuina produktval är redan tagna av Klas (spegel-fält, taggar-med-×, Rensa-alla-scope). VAL 4a/4b är konsekvens-beslut av låsta val och dokumenteras i PR-body för post-merge-granskning per ADR 0065 Amendment. **CC går direkt till implementation per §9.6 punkt 5.**

### Referenser

- Robert C. Martin, *Clean Architecture* (2017), kap. 7 (SRP); *Clean Code* (2008, least astonishment)
- Hunt/Thomas, *The Pragmatic Programmer* (1999), kap. 7 (DRY/SPOT)
- Eric Evans, *Domain-Driven Design* (2003), "Aggregates" (invarianter skyddas i modellen)
- Martin Fowler, *Refactoring* 2nd ed (2018), "Dead Code"
- Ford/Parsons/Kua, *Building Evolutionary Architectures* (2017) — fitness functions
- CLAUDE.md §2.4, §2.5, §5.1, §9.1, §9.6 · ADR 0045, 0062, 0065, 0067 · E2g/E2h-domarna

---

## Addendum 2026-06-12: code-reviewer-Majors (triage)

**Underlag:** `docs/reviews/2026-06-11-sok-paritet-e2i-code-review.md` (0 Blocker, 3 Major, 4 Minor) + kodverifiering av `jobb-hero-search.tsx:139–156` (sentinel + `lastCommitted`), `tokenize.ts:211–326` (`applyClaimsDelta`/`removeMatch`), `chip-composition.ts:102–118` (per-län-normalisering). Major 2 var redan delegerad direkt till implementation av code-reviewer och triageras inte här — men Beslut 2:s mekanism samspelar med dess fix (noteras nedan).

### BESLUT 1 — roundtrip-race i own-commit-detektorn

**Beslut: Fixa med recentCommits-lista (bounded, cap ~10).** Alternativet "acceptera risken" avvisas.

**Motivering mot principer:**

- **Programming by Coincidence (Hunt/Thomas 1999, kap. 6):** alternativet vilar på antagandet att Next App Router alltid kancellerar föregående in-flight-navigation så att mellanliggande props aldrig flushas. Det är odokumenterat framework-beteende, inte kontrakt. Att bygga korrekthet på odokumenterat beteende är att programmera på sammanträffande — och felmoden är exakt E2d/E2h-felklassen ("texten serialiseras om under caret") som hela E2i finns för att döda. CLAUDE.md §9.3: "Gissa aldrig."
- **Least astonishment (Martin 2008):** text som skrivs om mitt under skrivning vid långsam uppkoppling + snabb skribent är värsta sortens intermittent bugg — oreproducerbar lokalt, garanterad i fält.
- **SRP-bonus (Martin 2017, kap. 7):** dagens `lastCommitted` dubbelagerar (delta-bas + detektor). Med listan separeras rollerna rent: `lastCommitted` = arbets-stat/delta-bas, `recentCommits` = own-roundtrip-detektor. Fixen är alltså också en ansvars-städning, inte bara en lagning.

**Implementations-direktiv (korrekthetskritiskt, utöver kandidat-skissen):**

1. Varje `commit()` appendar `next` till `recentCommits` (cap ~10, FIFO-prune vid overflow — bounded per §5.1-andan, ingen obegränsad tillväxt).
2. Sentinelen: base som `sameUrlState`-matchar NÅGON post = EGEN → prune t.o.m. träffen (täcker både in-order-leverans och kancellerad mellanliggande), texten rörs EJ. Ingen träff = EXTERN → synka text, nolla listan.
3. **`lastCommitted` får INTE regredera vid egen mellanliggande roundtrip.** Dagens `setLastCommitted(base)` körs ovillkorligt — med S2 in-flight och S1-props landande skulle delta-basen tappa S2 (exakt stale-bas-felet som motiverade hela mekaniken). Regel: vid EGEN träff behåller `lastCommitted` sitt värde om listan fortfarande har nyare poster efter prune; först när listan är tom är `base` ikapp och `lastCommitted = base`. Vid EXTERN: `lastCommitted = base`.
4. Test som simulerar mellanliggande egen props-leverans (S1-props efter S2-commit): texten orörd, delta-basen fortsatt S2. Detta är en fitness function för race-fönstret (Ford/Parsons/Kua 2017), inte ett exempel-test.

Benignt hörn accepterat: extern ändring som råkar vara state-identisk med en in-flight-commit klassas som egen — texten speglar redan den staten, ingen divergens.

**CC bygger direkt** — korrekthetfix inom låst arkitektur, inget produktval.

### BESLUT 2 — per-län-normalisering/field-removal bryter I1 i remove-riktningen

**Beslut: I1-enforcement-pass sist i `applyClaimsDelta`.** Alternativet "dokumenterad konsekvens utan fix" avvisas.

**Motivering mot principer:**

- **Invarianter skyddas i modellen, inte i dokumentation (Evans 2003, "Aggregates"; CLAUDE.md §2.2):** I1 (`parse(text) ⊆ state`) är C′-modellens bärande invariant — huvuddomens VAL 1 valdes UTTRYCKLIGEN för att den är "formulerbar och bevisbar". En invariant med dokumenterade permanenta undantag är ingen invariant; då har vi köpt C′:s komplexitet och fått ren C:s hopp. Fält som visar ord som inte filtrerar är exakt den divergens modellen byggdes för att utesluta.
- **E2b-domen bär fixen:** per-län-normaliseringen är dokumenterad KOSMETIK ("denormaliserat state förblir korrekt backend-side, union tål redundans") — den är ingen korrekthets-bärare och får därför vika när den kolliderar med ett explicit text-anspråk. Användaren som skrev "västra götalands län" har gjort ett anspråk på hela länet; URL-minimalism trumfar inte det (least astonishment, Martin 2008).
- **Bokförings-lögnen läks samtidigt:** `appliedClaims.matches = next.matches` (tokenize.ts:281) blir SANN när enforcement-passet garanterar att varje claim faktiskt finns i staten — fixen reparerar både staten och bokföringen med en mekanism (SPOT, Hunt/Thomas 1999).

**Implementations-direktiv:**

1. Sist i `applyClaimsDelta`, efter removes+adds: för VARJE `next.matches`-claim, verifiera närvaro i staten; saknas → åter-appenda RÅTT (direkt på axeln, UTAN `composeSuggestionChip`-normalisering — det är normaliseringen som släckte claimet; att köra den igen vore flip-flop). OccupationField-claim ⇒ samtliga barn-grupper närvarande.
2. Enforcement-re-adds annonseras INTE i `addedLabels` — de är invariant-underhåll, inte användarsynliga tillägg (claimet annonserades vid sin ursprungliga add eller stod redan i texten).
3. Senare extern normalisering (t.ex. popover-kommun-add som släcker text-claimat län) är OK: den går extern-divergens-vägen → texten synkas → I1 håller igen. Enforcement-passet gäller commit-punkternas egen väg.
4. Regressionstester för bägge kedjorna: (a) "västra götalands län göteborg " → state innehåller region+kommun (redundant union, E2b-tålt); (b) radera "Data/IT" ur "Data/IT Systemutvecklare " → Systemutvecklare kvar i state. Rundtripps-property-testet ska fortsatt passera.
5. **Synergi med Major 2:** när förslags-valets `prevClaims` läggs om till faktisk applicering via `applyClaimsDelta`-vägen (code-reviewers direktiv) täcks även den vägen av enforcement-passet — ytterligare skäl att fixa Major 2 genom delta-vägen, inte parallellt.

**CC bygger direkt** — invariant-försvar, inget produktval.

### BESLUT 3 — deviation-ack: `lastCommitted` ersätter useOptimistic

**Beslut: ACK.** Avvikelsen från architect-skissen godkänns retroaktivt.

**Motivering mot principer:**

- **Rätt verktyg för rätt kontrakt:** `useOptimistic`:s kontrakt är optimistisk UI-feedback UNDER en transition, med revert till base utanför. Behovet här är en stabil delta-bas ÖVER flera sekventiella in-flight-commits — ett krav useOptimistic per kontrakt inte uppfyller (revert till stale base = verifierat i test att nyss committade dimensioner tappas). Att tvinga in det vore accidental complexity.
- **YAGNI (Martin 2017; Fowler):** overlayens enda syfte var omedelbar chip-rendering i fältet — den konsumenten dog med Klas-valet "normal ruta som speglar söket" (M4-rivningen). Architect-skissen skrevs mot en premiss som inte längre gäller; mekanism utan konsument behålls inte.
- **Render-determinism (CLAUDE.md §2.4):** `lastCommitted` som state är läsbar/skrivbar i render-sentinelen och deterministisk — testbar utan transition-timing-mockning.
- **Process-anmärkningen står:** code-reviewer har rätt i att valet skulle ha gått till CTO FÖRE implementation (§9.6 punkt 3). Utfallet ack:as; flödesmissen noteras som disciplinpåminnelse, inte ombyggnadsskäl.

Ack:en är villkorad av Beslut 1: detektor-rollen flyttar till `recentCommits`-listan; `lastCommitted` renodlas till arbets-stat/delta-bas (inkl. icke-regress-regeln i Beslut 1.3).

**CC bygger direkt.**

### In-block-fixar (§9.6)

Allt hör till E2i (nuvarande fas), inga saknade dependencies — **inga TDs lyfts**:

- `jobb-hero-search.tsx:139–156`: `recentCommits`-lista + icke-regress av `lastCommitted` (Beslut 1) + `setCaret(null)` i extern-grenen (Minor 2)
- `tokenize.ts` `applyClaimsDelta`: I1-enforcement-pass (Beslut 2) + boundary-bevarande vid bar operator-token (Minor 1)
- Major 2-fixen via delta-vägen (redan delegerad; synergi per Beslut 2.5)
- `chip-composition.ts:50–57`: `splitQWords`-import + kommentar (Minor 3)
- Tester: race-simulering (Beslut 1.4), kedjorna a/b (Beslut 2.4), Title-label-med-taxonomi-ord (Major 2)

Minor 4 (genererande rundtripps-property) = FYI till test-writer vid nästa touch per code-reviewers egen klassning — ingen TD, ingen gate.

### Klas-GO-sammanfattning

**Inga STOPP krävs.** Samtliga tre beslut är korrekthets-/mekanikval inom redan låsta produktbeslut och en redan dömd arkitekturmodell (C′). CC går direkt till implementation per §9.6 punkt 5; re-review av code-reviewer efter Major-åtgärder + utestående design-reviewer-granskning (VAL 4d) före PR.

### Referenser (addendum)

- Hunt/Thomas, *The Pragmatic Programmer* (1999), kap. 6 "Programming by Coincidence", kap. 7 (DRY/SPOT)
- Eric Evans, *Domain-Driven Design* (2003), "Aggregates" — invarianter skyddas i modellen
- Robert C. Martin, *Clean Architecture* (2017), kap. 7 (SRP); *Clean Code* (2008, least astonishment)
- Ford/Parsons/Kua, *Building Evolutionary Architectures* (2017) — fitness functions
- CLAUDE.md §2.2, §2.4, §5.1, §9.3, §9.6 · E2b-domen (denormaliserat state) · E2d/E2h-felklassen
