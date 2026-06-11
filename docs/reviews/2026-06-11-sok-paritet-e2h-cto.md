# senior-cto-advisor — Fas E2h chips-i-sökfältet (besluts-dom)

**Datum:** 2026-06-11
**Agent:** senior-cto-advisor (decision-maker, inte advisor)
**Scope:** Klas produktspec 2026-06-11 (chips-i-fältet, LÅST — övertrumfar E2d-CTO VAL 3 Variant A-preferensen per medvetet Klas-produktbeslut) ovanpå E2d (#51, main `1061bc2`).
**Underlag:** `docs/reviews/2026-06-11-sok-paritet-e2h-architect.md` (LÄST HELT), E2d-domar (`-e2d-architect.md`, `-e2d-cto.md`), Klas-spec-memory (`project_e2h_chip_in_field_spec`), CLAUDE.md §2.4/§4.3/§5.2/§9.6.
**Status:** Read-only beslutsdom. Ingen kod ändrad.
**Klas-pending vid domen:** minus-operatorn (`-ord` = NOT) — Klas sa "fortsätt" utan att svara på minus/plus-frågan. Minus är därmed PENDING (tokenizer-strip ingår i E2h, se F2); plus-strip ingår per spec + architect.

---

## Beslutsöversikt (snabbtabell)

| Val | Beslut | Klas-GO krävs? |
|---|---|---|
| **VAL 1** (F1 chip-state-modell) | **Variant A** — chips deriveras HELT ur URL:en; enda lokala staten = utkast-ordet | Nej — CC implementerar |
| **VAL 2** (F3.1 history per fält-commit) | **Variant B** — `router.replace` för ALLA fältets commits; klassad som MEKANIK, inte produktval | Nej — CC implementerar; asymmetrin dokumenteras i PR-body för Klas post-merge-granskning |
| **VAL 3** (F3.3 recent-capture-spam) | **Variant A** — acceptera + observera; trigger dokumenterad | Nej — noll touch; omvärdering vid användarsignal |
| **VAL 4** (F6 komponentstruktur) | **Variant B** — ny `ChipSearchField` komponerar `JobAdTypeahead` (+ additiv `selectOnTab`) + ren tokenizer i lib | Nej — CC implementerar, inkl. in-block SPOT-extraktion |
| **F2** (tokenizer-regler) | Bekräftad i sin helhet, inkl. **`-`-strip som E2h-KRAV** | Nej — CC implementerar |
| **F4** (Tab/Enter + APG-avsteg) | Bekräftad — fyra mitigeringar är DoD-krav; mouseover-skavanken → design-reviewer | Nej — CC implementerar |
| **F5** (no-JS + Sök-knappen) | Bekräftad — hydrated-flagga-växling; Sök BEHÅLLS (utkast-commit + close) | Nej — CC implementerar |

**Klas-STOPP-klassning sammanfattad:** **NOLL blockerande Klas-STOPP i E2h.** Alla val är entydigt motiverade mot principer → CC bygger direkt (CLAUDE.md §9.6 punkt 5). Ett icke-blockerande Klas-pending kvarstår: **minus-operatorn (NOT)** — egen framtida fas med backend-beroende (ADR 0062-notat vid GO); E2h neutraliserar den via tokenizer-strip och blockeras inte av att frågan är öppen.

---

## VAL 1 — chip-state-modell (F1)

### Beslut
**Variant A — chips deriveras HELT ur URL:en.** Dimension-chips ur dimension-params (taxonomy-reverse-lookup), fritext-chips ur `q.split(" ")` (en chip per ord). Enda lokala staten är utkast-ordet. `useOptimistic(base=props)` som overlay för omedelbar chip-rendering (E2g-mönstret). Prev-prop-sentinel behålls ENDAST för utkast-nollställning vid extern navigation.

### Motivering mot principer

- **SPOT/DRY (Hunt/Thomas 1999):** URL:en är redan den etablerade single point of truth för sök-state (E2g, Accepted). En chip-lista som *också* hålls lokalt är samma knowledge piece på två ställen — exakt definitionen av DRY-brott. Variant A gör fältet till en alternativ *rendering* av samma sanning toolbaren renderar; × i fältet = samma URL-operation som toolbar-× — en operation, två renderingar.
- **Wire-ärlighet (ADR 0062, verifierat av architect):** q har ingen fras-semantik på wire — `websearch_to_tsquery` AND:ar ociterade ord som lexem. Per-ord-chips är den enda rendering som inte ljuger om wire-semantiken; "EN chip för flerords-q" vore en falsk modell (Evans 2003 — modellen ska spegla domänens faktiska semantik, inte en önskad). Extern q ("AI engineer" från recent-sökning) → två chips är därmed *korrekt*, inte en kompromiss.
- **Round-trip-stabilitet:** `chips(q) = q.split(' ')`, `q(chips) = chips.join(' ')`; backend-parsern (SPOT för q-hygien) kollapsar whitespace → kanonisk form stabil åt båda håll. Case-insensitiv dedupe vid append är UX-idempotens, inte korrekthetslogik — duplicerar inte parserns ansvar.
- **Strukturell bug-eliminering (Fowler 2018 — ta bort felklassen, inte felet):** E2d-buggarna ("val söker direkt + tömmer", "tagg kvar men sökord borta") var symptom på draft-vs-committad-dualitet. Variant A gör buggklassen *orepresenterbar*: val = chip i URL = chip i fältet; utkastet rörs aldrig av commits. Det är skillnaden mellan att fixa en bugg och att göra den omöjlig.
- **Klas-spec:en gör A naturlig:** live-commit per tagg betyder att varje chip ÄR URL-state i samma ögonblick den skapas — det finns inget legitimt fönster där en lokal lista får skilja sig från URL:en.

### Avvisade alternativ

**Variant B (lokal chip-lista synkad mot URL):** Avvisad. Detta är exakt state-kopia-klassen E2g dödade och E2d-CTO F4 redan dömde ("chip-state deriveras ur URL+props, ALDRIG egen useState-kopia"). B återinför två sanningar + synk-kod som måste hållas korrekt för evigt, och köper ingenting — A levererar samma UX utan synk-ytan. Att återinföra en dömd buggklass i den fas vars hela syfte är att eliminera den vore arkitektonisk kapitulation.

### Trade-offs accepterade
Optimistisk chip (useOptimistic) kan i ett kort fönster visa state som RSC-payloaden ännu inte bekräftat — accepterat, samma medvetna fönster som E2g redan etablerat. Fritext-chip-borttagning kräver q-rebuild (join av kvarvarande ord) i stället för list-splice — trivialt och wire-ärligt.

---

## VAL 2 — history per fält-commit (F3.1)

### Klassning: MEKANIK, inte produktval
Klas-spec:en låser produktbeteendet: live-commit per tagg, live-resultat, fältet nollställs inte förrän explicit Sök. Vad användaren ser och kan göra är beslutat. Push-vs-replace ändrar inget av detta — det avgör enbart webbläsar-historikens granularitet, en implementations-mekanik av den redan låsta interaktionen. Jag avgör den utan Klas-STOPP, men den dokumenteras explicit i PR-body så Klas post-merge-granskning (ADR 0065 Amendment) är informerad och en override är medveten.

### Beslut
**Variant B — `router.replace` för ALLA fältets commits** (tokeniserings-commits, förslags-val, chip-× i fältet, Backspace-borttagning, Sök-knappens utkast-commit). Popover/toolbar behåller `push`. Entydigt oavsett: `{ scroll: false }` + `startTransition` på fältets commits.

### Motivering mot principer

- **History speglar användar-meningsfulla akter, inte tangenttryck:** att komponera EN sökning är EN logisk akt (följer direkt av Klas-spec: kompositionen avslutas av explicit Sök/Enter). Fyra ord = fyra history-entries (Variant A) gör Back-knappen oanvändbar för att lämna sidan — användaren måste trycka Back 4+ gånger. Back-knappens pålitlighet är civic-utility-pålitlighet (CLAUDE.md §1): 1177/GOV.UK-användaren förväntar sig att Back lämnar sidan, inte ångrar ett ord.
- **Ramverkets egen kanoniska konvention (Next.js Learn — "Adding Search and Pagination", officiella docs):** Next.js officiella mönster för search-as-you-type-input är `replace(...)` av exakt detta skäl — URL-uppdatering per inmatningssteg ska inte spamma history-stacken. Vi följer ramverkets dokumenterade idiom, inte en egen uppfinning.
- **Intern konsistens (Hunt/Thomas 1999 — Principle of Least Surprise):** B tillämpas på ALLA fält-commits — fältet är internt konsekvent. Mixad push/replace inom samma fält vore oförutsägbart.

### Avvisade alternativ

**Variant A (`router.push` per tagg):** Avvisad. "Back = ångra-tagg" låter elegant men chip-× är redan den synliga, direkta ångra-mekanismen — Back-som-ångra duplicerar den till priset av en förstörd Back-för-att-lämna. Dessutom inkonsekvent med spec:ens mentala modell (kompositionen är pågående tills Sök).

### Trade-offs accepterade
**Dokumenterad asymmetri:** toolbar-× pushar fortsatt, fält-commits replace:ar. Motivering som ska stå i kodkommentar + PR-body: *fältet = pågående komposition (en logisk akt), toolbaren = redigering av en etablerad sökning (diskreta akter)*. Asymmetrin är semantiskt grundad, inte slarv. Kostnaden: en användare som komponerat klart kan inte Back:a till mellansteg — accepterat, chip-× täcker behovet.

---

## VAL 3 — recent-capture-spam (F3.3)

### Beslut
**Variant A — acceptera + observera.** Noll touch i E2h. Omvärderingstrigger dokumenterad: om recent-listan i verklig användning domineras av mellansteg (Klas-signal eller användarsignal) → Variant B (backend refinement-collapse) som EGEN medveten Application/Infrastructure-touch med CTO + ev. ADR 0060-notat.

### Motivering mot principer

- **Speculative Generality (Fowler 2018, kap. 3):** Variant B bygger ny semantik ("strikt utökning inom tidsfönster → ersätt i stället för INSERT") i en levererad, fungerande mekanism (ADR 0060) innan något observerat problem finns. Det är precis det smell Fowler beskriver: maskineriet före behovet. Mekanismen självläker dessutom (Bump vid exakt träff, evict-äldsta åldrar ut mellansteg) — skadan är begränsad och temporär by design.
- **YAGNI/minsta lösning:** capture är best-effort-UX, inte en invariant. Att designa refinement-heuristik (vad är "strikt utökning"? vilket tidsfönster? vad händer vid borttagning av chip mitt i?) utan användardata är gissningsdesign — heuristiken riskerar att kollapsa *riktiga* avsiktliga varianter ("Systemutvecklare" OCH "Systemutvecklare Göteborg" kan båda vara sparvärda).
- **Evolutionär arkitektur (Ford/Parsons/Kua 2017):** rätt mönster är observera → mät → besluta. LoggingBehavior + recent-listans faktiska utseende ger signalen gratis.

### Avvisade alternativ

**Variant B (backend refinement-collapse) — avvisad SOM E2h-fold, inte för evigt:** ny semantik i Accepted ADR 0060-mekanism är ett eget medvetet beslut (samma princip som E2d-CTO VAL 2b: "inte fel i sig — fel *här*"). Om triggern slår till är det in-block/naturlig split-batch i den touchen, **inte TD** (§9.6 — samma fas, ingen saknad dependency; det är en villkorad framtida utvidgning, inte uppskjuten skuld).

**Variant C (FE-capture-hint-param) — avvisad definitivt:** architect-domen bekräftad. Att låta klienten styra en server-side-capture-invariant flyttar förtroende till fel sida av gränsen (security-auditor-territorium) för noll vinst. Klient-hintad serversemantik är ett anti-pattern oavsett hur oskyldig parametern ser ut.

---

## VAL 4 — komponentstruktur (F6)

### Beslut
**Variant B — ny `ChipSearchField` (client-komponent i `components/job-ads/`) som komponerar `JobAdTypeahead`** (chips-rad + input i samma visuella fält; `jp-hero__searchrow`-stylingen flyttar till wrappern), **ren tokenizer i `lib/job-ads/tokenize.ts`**, **`composeSuggestionChip` återanvänds OFÖRÄNDRAD**, `JobAdTypeahead` får additiv `selectOnTab`-prop. `JobbHeroSearch` förblir ön som äger URL-state + navigation.

### Motivering mot principer

- **SRP (Martin 2017, kap. 7):** suggest/fetch/combobox-a11y (JobAdTypeahead) och tokenisering/chips-layout/q-orkestrering (ChipSearchField) är två change-reasons. Variant A blandar dem i en komponent — samma SRP-brott som fällde E2d VAL 1 Variant B. Två change-reasons = två moduler.
- **OCP (Martin 2017, kap. 8):** `selectOnTab` är additiv utvidgning — befintliga JobAdTypeahead-konsumenter opåverkade. Komposition framför ombyggnad är öppen-för-utvidgning-stängd-för-modifiering i praktiken (GoF 1994 — "favor composition").
- **Testbarhet (CLAUDE.md §2.4):** tokenizern som ren funktion `(draft, taxonomy, current) → { nextState, remainingDraft }` i lib är unit-testbar utan DOM — alla F2-edge-cases (`+`/`-`-strip, ambiguitet, tomma tokens, max-längd-guard) testas som rena datafall. Det är omöjligt i Variant A/C utan komponent-rendering.
- **SPOT (Hunt/Thomas 1999):** tokenizern syntetiserar `SuggestionDto` och går genom `composeSuggestionChip` — tokenizer-vägen och förslags-vals-vägen delar EN kind→dimension-mappning. Ingen andra väg får existera.

### Avvisade alternativ

**Variant A (bygg om JobAdTypeahead):** Avvisad — SRP-brott (ovan) + riskerar regression i en levererad, a11y-granskad komponent när två ansvar flätas in i samma fil.
**Variant C (JobbHeroSearch-monolit):** Avvisad — samma dom som E2d-CTO VAL 1 Variant B/C (SRP, granskbarhet, SWE@Google 2020 kap. 9 — granskbar diff).

### In-block-fixar (§9.6 — fixas NU i E2h, INTE TD)

1. **`buildChipModels(axes, labelResolver) → ChipModel[]` + remove-helpers (`removeChipFromState`, `removeQWord`) extraheras till `lib/job-ads/`,** konsumerade av BÅDE toolbar och fält. E2d-CTO mandaterade `useSelectedChips`-SPOT; on-disk har toolbaren kvar inline-mappning + lokala useState-kopior (E2g-divergent, överlever bara via Suspense-remount). E2h inför den TREDJE konsumenten — att då lämna dupliceringen vore att medvetet skapa skulden i samma touch som motiverar dess eliminering (DRY, Hunt/Thomas 1999; samma dom som E2d-CTO F4). Label-källorna skiljer (toolbar: server-resolverade per ADR 0043; fält: taxonomy-träd) → injicerad label-resolver, inte två deriveringar.
2. **Toolbarens useState-kopior ersätts med prop-derivering i samma touch** — liten yta, samma komponentfamilj, dödar den latenta E2g-buggklassen som idag bara maskeras av remount.
3. **Backspace i tomt input = ta bort sista chipen** — ingår. Etablerat chip-input-mönster, liten yta, går genom samma remove-helpers (SPOT) + aria-live-annons (F4).

### Topologi-krav (icke-förhandlingsbart, bekräftat)
`JobbHeroSearch` förblir UTANFÖR den searchParams-key:ade Suspense-gränsen och får ALDRIG key:as på sök-state — remount per ord förlorar fokus + utkast och återinför E2d-buggkänslan i ny form. Detta är ett granskningskrav på PR-diffen.

---

## F2 — tokenizer-regler (BEKRÄFTADE, entydiga)

Samtliga architect-domar bekräftas:

1. **Mellanslag/komma avslutar token** → exakt case-insensitiv unik match mot FE-taxonomy-trädet → dimension-chip via `composeSuggestionChip` OFÖRÄNDRAD (tokenizern syntetiserar SuggestionDto — samma SPOT som förslags-val). Ingen match → fritext-chip (q-ord).
2. **Ambiguitets-regel:** token matchar fler än en kind/label → fritext-chip. Gissa aldrig (D2 anti-gissnings-prejudikat). Endast unik match → dimension-chip. OccupationField → barn-grupper (E2d VAL 2a-vägen, redan i composeSuggestionChip).
3. **Multi-ord-labels nås endast via förslags-val** — acceptabel semantik; fallthrough = två fritext-chips → q-FTS = recall-bevarande degradering åt rätt håll. Title-labels behövs inte i auto-match (Title→q ≡ fritext-chip). Noll backend-touch.
4. **Tomma tokens skippas tyst.**
5. **`+`-strip** per spec.
6. **`-`-strip är E2h-KRAV, inte option:** `websearch_to_tsquery` tolkar ledande `-` som NOT *redan idag* — utan strip shippar E2h en oavsiktlig, odokumenterad negations-feature i en fråga som är explicit Klas-pending. Att neutralisera accidental semantik i en pending produktfråga är inte en design-preferens utan ett krav på beslutsintegritet: Klas ska fatta minus-beslutet medvetet, inte ärva det ur en parser-bieffekt. (Samma princip som `feedback_adr_mechanism_vs_env_phase_triage`: semantik-frågor avgörs explicit, inte av mekanik-bieffekter.) ADR 0062-notat skrivs vid Klas-GO för minus-fasen.
7. **Ord < 2 tecken chippas ändå** (spec: allt blir taggar); q-min gäller joinade strängen; ensam 1-teckens-chip → parser-null = recall-bevarande no-op, självläkande. FE duplicerar INTE parserns hygien-regler (parsern är SPOT för q-normalisering — Hunt/Thomas: en knowledge piece, ett ställe).
8. **FE-guard ENDAST för max-längd:** joinad q > 100 får inte live-committas (validatorn avvisar före parserns trunkering → knäckta live-resultat mitt i flödet). Ordet stannar i utkastet + saklig hjälptext (copy → design-reviewer). Delad konstant `Q_MAX_LENGTH = 100` i dto-lib med cross-ref-kommentar mot `SearchCriteria.QMaxLength` (samma disciplin som badge-labels, memory `project_crossref_badge_status`). Detta är inte parser-duplicering — det är kontrakts-skydd mot en validator-gräns, en annan knowledge piece.

**F3.2 bekräftad:** ingen debounce på commits (debounce skulle sabotera spec:ens kärna — live-resultat per tagg); ord-takt ≈ popover-klick-takt; noteras som perf-observation mot ADR 0045-budget (LoggingBehavior mäter redan, §2.5).

---

## F4 — Tab/Enter + APG-avsteg (BEKRÄFTAT)

- Tab-intercept ENDAST vid öppen lista + `active >= 0`; Shift+Tab interceptas ALDRIG; fokus stannar i inputen efter val. Enter med markering = välj; utan markering = tokenisera utkast → fritext-chip(s) + commit.
- **APG-avsteget är medvetet per Klas-direktiv (LÅST spec) — de fyra mitigeringarna är DoD-krav, inte rekommendationer:** (1) villkorad intercept (aldrig fokus-fälla), (2) `aria-activedescendant` gör markeringen skärmläsar-synlig, (3) aria-live-annons för chip-tillägg/-borttagning, (4) Tab-instruktion i label/hjälptext — ALDRIG placeholder (memory `feedback_no_placeholder_example_text`, hård Klas-regel). En PR utan alla fyra är inte DoD-klar (CLAUDE.md §8 punkt 6).
- **Mouseover-sätter-active-skavanken** (Tab efter ofrivillig hovring väljer oväntat) följer av spec:en ("mouseover ELLER piltangenter") → flaggas till design-reviewer som dokumenterad konsekvens; ev. mitigering (rensa `active` vid mouseleave) är design-beslut, inte CTO. INTE Klas-STOPP.
- nextjs-ui-engineer laddar `jobbpilot-design-a11y`-skillen vid implementation (DoD, samma som E2d F5).

---

## F5 — no-JS + Sök-knappen (BEKRÄFTAT)

- **No-JS/pre-hydration = dagens kontrakt exakt:** `<input name="q">` med hela committade q + hidden inputs; native GET-submit (backend-parsern tål rå sträng — SPOT). Efter hydration: växling till chips-läge via hydrated-flagga (`useSyncExternalStore`-mönstret — ingen effect). Fallgropen som dikterar detta är verifierad: chips-lägets input bär bara utkastet — med kvarvarande `name="q"` skulle native submit TAPPA committade ord. Kort visuell växling rå-q → chips vid landning: accepterad, dokumenteras.
- **Sök-knappen BEHÅLLS:** (1) den ÄR no-JS-GET-submiten (civic-utility progressive enhancement, CLAUDE.md §5.2 + GOV.UK-doktrin, samma som E2d-CTO VAL 1); (2) JS-roll = tokenisera kvarvarande utkast + stäng dropdown (no-op vid tomt utkast); (3) synlig förutsägbar primäraktion. "Vad gör Sök"-frågan har därmed ett mekaniskt svar — **inget Klas-STOPP behövs.** Vill Klas ge knappen mer (t.ex. fokus-flytt till resultatlistan) är det design-fas, opt-in senare.

---

## §9.6-triage — sammanställd TD-dom

| Fynd | TD? | Motivering |
|---|---|---|
| F3.3 recent-capture-spam | **Nej (ej TD)** | Variant A vald (observera); ev. framtida B = eget medvetet beslut vid signal, in-block/split-batch då — villkorad utvidgning, ej uppskjuten skuld |
| Minus-operator (NOT) | **Nej (ej TD)** | Explicit out-of-scope, Klas-pending produktfråga med backend-beroende (ADR 0062-notat vid GO) — eget beslut, ej skuld. Tokenizer-strippen är E2h-krav och neutraliserar accidental semantik |
| `buildChipModels` + remove-helpers + toolbar-useState-sanering | **Nej (ej TD)** | **In-block E2h** — samma fas, tredje konsumenten införs NU; att lämna dupliceringen vore medvetet skapad skuld (samma dom som E2d-CTO F4) |
| Backspace-tar-sista-chip | **Nej (ej TD)** | In-block — liten yta, samma helpers, samma komponent |
| Mouseover-Tab-skavanken | **Nej (ej TD)** | Design-reviewer-fråga i E2h-PR:n, ej skuld |

**Inga TDs lyfts ur E2h.**

---

## Sammanfattad Klas-STOPP-klassning

**NOLL blockerande Klas-STOPP.** CC bygger hela E2h direkt: VAL 1 (A), VAL 2 (B — mekanik-klassad, dokumenteras i PR-body), VAL 3 (A — noll touch), VAL 4 (B + in-block-fixar), F2/F4/F5 enligt bekräftade domar.

**Icke-blockerande Klas-pending (informeras i STOPP-rapporten efter PR, kräver inget svar för E2h):**
1. **Minus-operatorn (NOT):** egen framtida fas — backend-parser/FTS-design + ev. dimensions-NOT + ADR 0062-notat + CTO. E2h strippar ledande `-` så frågan förblir genuint öppen.
2. **VAL 2-asymmetrin** (fält-replace vs toolbar-push) dokumenteras i PR-body — Klas post-merge-granskning kan override:a; en override är då medveten (replace→push är en enradsändring, ingen arkitektur-risk).

**Veto-agenter i E2h-PR:n:** design-reviewer (chips-UI, q-max-hjälptext-copy, mouseover-Tab-skavanken, APG-avstegs-mitigeringarna), code-reviewer + dotnet-architect-rapporten bifogas (>5 filer). Ingen security-auditor-trigger i primär väg (ingen PII/auth/secrets-touch; Variant C-capture-hinten som skulle ha triggat den är avvisad).

---

## Referenser

- Robert C. Martin, *Clean Architecture* (2017) — kap. 7 (SRP: VAL 4), kap. 8 (OCP: selectOnTab), kap. 13 (komponent-cohesion: VAL 1/VAL 4)
- Hunt/Thomas, *The Pragmatic Programmer* (1999) — DRY/SPOT (URL-som-sanning, composeSuggestionChip, buildChipModels, parser-som-q-SPOT, Q_MAX_LENGTH-cross-ref), Least Surprise (VAL 2 intern konsistens)
- Martin Fowler, *Refactoring* 2nd ed (2018) — kap. 3 Speculative Generality (VAL 3 B-avvisning som default; ingen FE-dubbel-validering), buggklass-eliminering framför bugg-fix (VAL 1)
- Eric Evans, *DDD* (2003) — modell-integritet/wire-ärlig rendering (VAL 1 per-ord-chips), kap. 14 ACL (taxonomy-trädet som match-korpus)
- Gamma/Helm/Johnson/Vlissides, *Design Patterns* (1994) — komposition framför modifiering (VAL 4 B)
- Ford/Parsons/Kua, *Building Evolutionary Architectures* (2017) — observera→mät→besluta (VAL 3)
- Winters/Manshreck/Wright, *Software Engineering at Google* (2020) — kap. 9 granskbar diff (VAL 4 C-avvisning)
- Next.js Learn — "Adding Search and Pagination" (officiella docs): `replace` för search-as-you-type-input (VAL 2)
- ADR 0067 Beslut 5/5a/5b/5c + D2/E2b/E2d/E2g-notat; ADR 0062 (`websearch_to_tsquery`, `-` = NOT); ADR 0042 B; ADR 0060 (recent-capture); ADR 0045 (perf-budget); ADR 0043 (label-resolvering); ADR 0065 (post-merge-granskning)
- CLAUDE.md §1, §2.4, §2.5, §4.3, §5.2, §8 p.6, §9.6
- Memory: `project_e2h_chip_in_field_spec` (LÅST Klas-spec), `feedback_no_placeholder_example_text`, `feedback_adr_mechanism_vs_env_phase_triage`, `project_crossref_badge_status`
- Underlag: `docs/reviews/2026-06-11-sok-paritet-e2h-architect.md`, `docs/reviews/2026-06-11-sok-paritet-e2d-cto.md`, `docs/reviews/2026-06-11-sok-paritet-e2d-architect.md`

---

## Addendum: utkast-sentinel-avvikelsen (code-reviewer Major 1)

**Dom: ACK.** Implementationens avvikelse från VAL 1-formuleringen ("sentinel behålls ENDAST för utkast-nollställning vid extern navigation") är korrekt — formuleringen var en kvarleva från E2d:s draft/q-dualitet och var fel i Variant A-modellen. Verifierat on-disk (`jobb-hero-search.tsx:96–140`).

**Motivering:**

1. **Sentinelen kan inte göra jobbet den ombads göra.** I Variant A orsakar varje chip-commit en egen RSC-roundtrip → base-byte. En naiv prop-sentinel kan inte skilja "egen commit landade" från "extern navigation" och skulle radera användarens NYA halvskrivna ord när föregående ords roundtrip landar — exakt den fokus/utkast-förlust-buggklass topologi-kravet (VAL 4) finns för att förhindra. Att följa instruktionen bokstavligt hade återinfört buggklassen VAL 1 valdes för att eliminera (Fowler 2018 — buggklassen, inte buggen).
2. **Felmods-asymmetrin avgör.** Sentinel-falskt-positiv = destruktiv förlust av användarens pågående inmatning mitt i skrivflödet. Sentinel-frånvaro = ett godartat kvarlämnat ord som är användarens eget, inget URL-state och inget korrekthets-brott (URL:en förblir SPOT för committat state). Välj alltid den icke-destruktiva felmoden (Least Surprise, Hunt/Thomas 1999; civic-utility-pålitlighet, CLAUDE.md §1).
3. **Alternativ mekanik avvisas.** En self-commit-flagga (ref sätts i `commit()`, konsumeras i sentinelen) kan tekniskt skilja fallen, men inför muterbar cross-render-koordination — skör under `startTransition`/flera in-flight-commits — för att åtgärda en kosmetisk icke-bugg. Speculative Generality (Fowler 2018, kap. 3); maskineriet före behovet.
4. **Bieffekten är rätt åtgärdad.** Stale q-max-notisen nollas nu via prev-base-sentinel vid VARJE base-byte (`:136–140`) — korrekt, eftersom notisens premiss förbrukas av både egen commit och extern navigation. Det var notisen, inte utkastet, som var det genuina problemet i Major 1.

**Krav (kvarstår från code-reviewer):** avvikelsen dokumenteras i PR-body med referens till detta addendum — Major 1:s kärna var *odokumenterad* avvikelse, inte fel avvikelse. Inline-kommentaren (`:96–98`) + detta addendum + PR-body-not stänger fyndet. Ingen TD (§9.6 — inget kvarvarande arbete).

**Process-not:** avvikelse-med-bättre-motivering är rätt beteende, men ska flaggas till CTO *innan* review-rundan nästa gång — ack-i-efterhand fungerade här för att mekaniken var entydig; vid tvetydighet hade det kostat en omimplementation.
