# design-reviewer — PR #1510 (#1505), SCOPAD OMKONTROLL

- **Date:** 2026-08-24
- **PR:** [#1510](https://github.com/klasolsson81/jobbliggaren/pull/1510)
- **Scope:** `git diff 7adb1a0f 1ba31db7` — 10 filer, +133/−46. Report-only.
- **Verdict:** ✓ **Approved — 0 Blocker, 0 öppna Major, 0 öppna Minor**
- **Kvot:** förbrukad. *"Vidare routning går till senior-cto-advisor, inte hit."*
- **Transcribed by the driving session** — charter grants `Read`/`Grep`/`Glob`, no `Write`.

## Fynd-för-fynd

### Major 1 (noll-grenen saknade nästa steg) — **STÄNGT**

`jobb-results.tsx:409-413` annonserar nu `` `${t("list.emptyTitle")} ${t("list.emptyBody")}` `` → "Inga jobb hittades Justera filtren eller töm sökrutan för att se fler annonser." Mätt mot katalogen (`messages/sv/jobads.json` → `ui.list`) och mot skärmen: `job-ad-list.tsx:71-76` renderar exakt samma par i `.jp-empty` när `jobAds.length === 0`, och den grenen nås av samma `totalCount === 0`. Copy-kontroll: konkret nästa steg, inget utropstecken, ingen emoji, ingen "Du". Symmetrisk med de två fel-grenarna i samma switch. Pinnad mot båda strängarna i `jobb-results.test.tsx:169-173`.

### Major 2 (e2e räknade fel) — **STÄNGT MED ANMÄRKNING**

Skopningen **är** orakelbärande. `page.tsx:365` renderar `section[aria-labelledby="jobb-results-title"]` server-side och `Announcer` ligger på 378, inuti sektionen och utanför `<Suspense>`. Hero-regionerna (`jobb-hero-search.tsx:669`, typeaheaden) sitter i `section.jp-hero` (280-351) och kan aldrig bli deskendenter. Assertionen faller vid varje mutation som räknas: förlorad `aria-atomic` → 0, andra atomisk region i sektionen → 2, ändrat `aria-labelledby`-id → 0. Positivkontrollen är fail-loud.

Två anmärkningar, båda stängs genom **strykning**:

- `tests/e2e/jobb-live-region.spec.ts:50-52` — kommentaren är **faktiskt falsk**. `getByLabel(SEARCH_FIELD_LABEL)` matchar även pre-hydration: `jobb-hero-search.tsx:590-600` renderar no-JS-fallbacken `<input id="jobb-q">` under samma `<label htmlFor="jobb-q">`, och den är enabled. Väntan väntar alltså inte på hydration. Att fixen ändå håller beror på att skopningen är hydration-invariant. *"Behåll inte en rad vars kommentar namnger en grind den inte är."*
- Specen är **inte körd**. E2e-lanen är `continue-on-error` — samma villkor som lät den felaktiga versionen passera grön. Dessutom är `:not(${RESULTS_SECTION} *)` (Selectors L4, komplex selektor i `:not()`) utan motstycke i repot. Krävs: en lokal körning, **eller** byt positivkontrollen till `section.jp-hero p[aria-live='polite'].sr-only` (orakel-ekvivalent, ingen oprövad konstruktion).

### Major 3 (`role="alert"` + `Announce`) — **STÄNGT MED ANMÄRKNING**

Mätt: `grep 'role="alert"'` i `jobb-results.tsx` + `foretag-sok-results.tsx` ger noll. Båda ytorna pinnade (`foretag-sok-results.test.tsx:448`, `jobb-results.test.tsx:190`), och pinnarna är dokument-breda så en återinförd roll faller oavsett gren. *"Att strykningen inte dödade något test innan pinnarna var rätt mätning att göra."*

Anmärkning: `jobb-results.test.tsx:195` blev falsk **av deltat** — "not even the rate-limit branch's `role=\"alert\"`" beskriver en roll som inte längre finns. Ren strykning.

### Minor 4 (`job-ad-list.tsx:64-67`) — **STÄNGT**

Kommentaren pekar nu på `Announcer` i `page.tsx` och på att `jobb-results.tsx` skjuter in de två meningarna. Mätt sann mot båda filerna.

### Minor 5 (asymmetrisk regionplacering) — **SKIPPEN ACCEPTERAS, med tillägg**

Ingen a11y-konsekvens: en `role="status"`-region behöver inte ligga i en märkt sektion, och `loading.tsx`s region är tom vid mount, vilket är vad ARIA22 kräver. Men deltat gjorde asymmetrin **strukturellt lastbärande**: `LOAD_REGION` är nu skopad till `section[aria-labelledby='jobb-results-title']`, och `loading.tsx`s region ligger i `div.jp-container.jp-page` utan någon sådan sektion — alltså utanför varje e2e-selektor. Skip-raden i PR-kroppen ska namnge det.

## De två sakerna som ombads vägas

1. `foretag-sok-results.tsx:232-241` — `role="alert"`-motiveringen är **raderad**, den överlevande meningen enbart omflödad, inget nytt påstående tillfört. Samma i testets docblock. **Villkoret hålls.**
2. `jobb-results.tsx:402-407` — den nya formuleringen är **sann**: räknaren renderas av `JobbResultsToolbar`, tom-paret av `JobAdList`s `.jp-empty`, båda i samma render som annonsen.

## Bra gjort

- Strukturell skopning i stället för räkning — orakel som inte kan raceas, och kommentaren dokumenterar den falska gröna körningen i stället för att dölja den.
- `loading.test.tsx`s `renderToString`-pinne mäter den enda halva ingen klientrendering kan se: att regionen skeppas tom i server-HTML.
- Katalog-läsning via `createTranslator` i `job-ad-list-skeleton.test.tsx` — ett omdöpt nyckelnamn faller nu i testet i stället för att annonsera ett rått message-id.

## Tredje anmärkningen

`announcer.test.tsx:36` — påståendet att `aria-label` *"would OVERRIDE the announced text"* är överdrivet. `role="status"` är `nameFrom: author`, så namnet ersätter inte innehållet i annonseringen. **Pinnen i sig är korrekt.**

## Sammanfattning

0 Blocker, 0 öppna Major, 0 öppna Minor. Tre anmärkningar stängs genom strykning och kräver **ingen ny omkontroll** (§9.6). Enda mätning som begärs utöver det: kör `tests/e2e/jobb-live-region.spec.ts` en gång lokalt, eller byt positivkontrollens selektor.

---

## Driving session — vad som gjordes med de tre anmärkningarna

Alla tre stängda genom strykning i `282ae693`:

1. **Hydrationsväntan raderad.** Verifierad falsk först: `jobb-hero-search.tsx:593-601` renderar no-JS-fallbacken med `id="jobb-q"` under samma label, enabled. Väntan väntade på ingenting.
2. **Positivkontrollen bytt** till `section.jp-hero p[aria-live='polite'].sr-only` — hennes orakel-ekvivalenta alternativ, som också tar bort Selectors-L4-konstruktionen.
3. **`jobb-results.test.tsx:195`** och **`announcer.test.tsx:36`** — båda satserna strukna.

E2e-mätningen delegerad till CI (full stack, ren runner) och läst före `agents-done`.
