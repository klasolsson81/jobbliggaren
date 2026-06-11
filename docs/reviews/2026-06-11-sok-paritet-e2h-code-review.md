# Code-review: Fas E2h — chips-i-sökfältet (working tree mot main `1061bc2`)

**Status:** ⚠ Changes requested (0 Blocker, 2 Major, 4 Minor)
**Granskat:** 2026-06-11
**Agent:** code-reviewer
**Auktoritet:** CLAUDE.md §2.4 (testbart först), §4 (TS/Next-standarder), §5.2 (FE-anti-patterns), §7 (testing-krav), §9.6 (in-block default)
**Beslutskontext:** `docs/reviews/2026-06-11-sok-paritet-e2h-architect.md`, `-e2h-cto.md` (VAL 1=A, 2=B, 3=A, 4=B)
**Scope:** FE-only — `chip-models.ts(+test)`, `tokenize.ts(+test)`, `chip-search-field.tsx`, `jobb-hero-search.tsx(+test)`, `job-ad-typeahead.tsx`, `jobb-results-toolbar.tsx`, `globals.css`, `lib/dto/job-ads.ts`
**Gates (rapporterade av CC, ej omkörda här):** tsc, eslint, 812 vitest (+33), pnpm build — gröna

---

## Verifierade granskningspunkter (uppdragets sex)

1. **Tokenizer-korrekthet** — ✓. `endsWithDelimiter`-testet på rå draft + `segments.pop()` skiljer korrekt trailing delimiter från pågående ord; `finalizeAll` hoppar över pop. `sameState` jämför innehåll (inte referens) vilket är nödvändigt eftersom `composeSuggestionChip` kopierar listor även vid no-op — korrekt analyserat i kodkommentaren. q-max-guard: rejected→remainder-loopen är stabil (re-tokenisering av rejected ord re-rejectas tills q krymper, då committas de — önskvärt). Dedupe case-insensitiv, idempotent, `changed`-flaggan korrekt. Taxonomi-match prövas FÖRE q-max-guarden så dimension-ord aldrig kan fastna i guarden — rätt ordning.
2. **useOptimistic + router.replace** — ✓. `setOptimisticState` anropas inne i transitionen (krav), `router.replace` i samma transition; sekventiella commits läser `urlState` (optimistiskt overlay) så ord 2 bygger på ord 1 även före RSC-roundtrip. `setAnnouncement` UTANFÖR transitionen är korrekt: annonsen är en synkron high-priority-update som inte ska kunna avbrytas/fördröjas av navigations-transitionen.
3. **Hydration** — ✓. `useSyncExternalStore(emptySubscribe, () => true, () => false)` är det effect-fria standardmönstret; ingen hydration-mismatch (server-snapshot används under hydreringen, omedelbar re-render efter). Pre-hydration: synligt `<input name="q" defaultValue={q}>` → native GET bär hela q. Post-hydration: synliga inputen saknar `name` (JobAdTypeahead får ingen `name`-prop) och hidden `name="q"` med `committedQ` renderas — aldrig dubbel q, aldrig tappad q. Architect F5-fallgropen är korrekt stängd.
4. **Toolbar-refaktorn** — ✓. Sort-select läser `urlState.sortBy` (optimistiskt = omedelbar select-respons, samma slutvärde), Relevance-gaten härleds fortsatt ur `q`-PROPEN (ADR 0042 Beslut D — EJ optimistic state, korrekt), `clearAllFilters` bevarar q/sortBy/pageSize. Befintliga toolbar-tester (push-URL-kontrakt, "Rensa alla filter", Relevance-disable) är beteende-kompatibla med refaktorn. E2g-divergenta useState-kopiorna är döda — CTO in-block-mandat 1+2 levererat.
5. **SPOT-disciplin** — ✓. EN kind→dimension-väg: tokenizern syntetiserar `SuggestionDto` och går genom `composeSuggestionChip` (oförändrad). `buildChipModels`/`removeChipFromState` delas av fält + toolbar med injicerad label-resolver. Ingen andra derivering hittad (grep). `Q_MAX_LENGTH=100` verifierad mot backend `SearchCriteria.QMaxLength=100` (cross-ref-kommentar finns åt båda håll-disciplinen).
6. **Backspace/Tab-edge-cases** — ✓ i koden (se dock Major 2 om testtäckning). Tab-intercept: `selectOnTab && e.key === "Tab" && !e.shiftKey && active >= 0` + krav på `showList` (guarden ligger efter `if (!showList) return`) → aldrig fokus-fälla; `preventDefault` finns; `items[active]` är säker (active nollställs vid ny resultatmängd i samma batch, modulo-aritmetik i pil-handlers). Backspace-grenen kräver `value === ""` och ligger före showList-guarden så den fungerar även med stängd lista; `onRemoveLast` är idempotent vid key-repeat (removeChipFromState på redan borttagen chip = no-op-state).

**Topologi-kravet (CTO, icke-förhandlingsbart):** ✓ verifierat — `page.tsx` är orörd i diffen; `JobbHeroSearch` renderas utanför Suspense-gränsen utan `key`.

---

## Major (bör fixas/lösas innan merge)

### Major 1 — Odokumenterad avvikelse från CTO VAL 1-detalj: prev-prop-sentinel borttagen helt

**Fil:** `web/jobbpilot-web/src/components/job-ads/jobb-hero-search.tsx:88–91`

CTO-domen (VAL 1, ordagrant): *"Prev-prop-sentinel behålls ENDAST för utkast-nollställning vid extern navigation."* Architect F1 säger samma sak. Implementationen tog bort sentineln HELT och lämnar utkastet orört vid externa URL-ändringar, med inline-motivering ("ett halvskrivet ord är användarens, inte URL:ens").

**Min bedömning av sakfrågan:** avvikelsen är tekniskt försvarbar — sannolikt mer korrekt än CTO-instruktionen. En naiv prop-sentinel kan inte skilja "egen commit:s RSC-roundtrip landade" från "extern navigation": den skulle radera ett NYTT halvskrivet ord mitt i skrivflödet när roundtripen för föregående ord landar — exakt den fokus/utkast-förlust-buggklass topologi-kravet finns för att förhindra.

**Men:** CLAUDE.md §9.6 punkt 5 ger CC mandat att följa CTO-beslut, inte att tyst avvika från dem. Konflikten ska flaggas explicit (code-reviewer-process: konflikt mellan beslutsdom och implementation eskaleras, avgörs inte i koden). Konkret bieffekt av avvikelsen som CTO inte fått väga: **`limitNotice` överlever extern navigation** — har användaren fått "Söktexten är full"-hjälptexten och sedan klickar en recent-sökning (q byts externt till kort), står den felaktiga varningen kvar tills nästa tangenttryck.

**Krävs:**
1. Dokumentera avvikelsen i PR-body bredvid VAL 2-asymmetri-noten (som redan är ett CTO-krav) — alternativt kort CTO-ack via senior-cto-advisor. Klas/CTO-override ska vara medveten, inte upptäckas post-merge.
2. Nollställ `limitNotice` när bas-q ändras externt (t.ex. derivera notisen mot aktuell q-längd, eller resetta i samma mekanism som väljs) — den stale hjälptexten är en faktisk bugg oavsett sentinel-beslutet.

**Delegera till:** Klas/CC (PR-body) + senior-cto-advisor (ratificering); kodfixen för limitNotice är trivial in-block.

### Major 2 — Testgap på APG-avstegets skyddsvillkor (DoD-mitigering 1) och aria-live-annonsen (mitigering 3)

**Filer:** `job-ad-typeahead.test.tsx` (0 träffar på `selectOnTab`/`onEmptyBackspace`), `jobb-hero-search.test.tsx`

CTO F4 klassar de fyra APG-mitigeringarna som **DoD-krav** (CLAUDE.md §8 p.6). Mitigering 1 — den villkorade intercepten som garanterar att Tab ALDRIG blir en fokus-fälla — saknar negativa tester. Det som finns är happy path (Tab MED markering väljer; Backspace i tomt fält tar chip). Saknas (CLAUDE.md §7-principen: happy path + failure path):

- Tab UTAN markering (`active === -1`, öppen lista) → INGEN intercept, normal fokus-flytt — detta är själva fokus-fälla-skyddet; en regression här är en a11y-blocker i produktion och fångas idag av inget test
- Shift+Tab med markering → aldrig intercept
- Backspace med text i fältet → ingen chip-borttagning
- `selectOnTab` ej satt (default, befintliga konsumenter) → Tab interceptas aldrig — OCP-löftet ("additiva props, befintliga konsumenter opåverkade") är otestat
- aria-live-annonsens innehåll ("X tillagd"/"X borttagen") asserts inte någonstans — mitigering 3 är overifierad

**Krävs:** komplettera `job-ad-typeahead.test.tsx` med guard-fallen (de nya propsen ägs av den komponenten — testerna hör hemma där, inte bara indirekt via hero) + en annons-assertion i hero-testet.
**Delegera till:** test-writer.

---

## Minor (nice-to-fix, blockerar ej)

### Minor 1 — Extern q med dubblett-ord ger duplicerade React-keys + fel-ords-borttagning

`chip-search-field.tsx:46` (`key={axis-value}`) + `chip-models.ts:87–94`. Tokenizern dedupe:ar fältets väg, men q ur extern URL (direktlänk/recent-sökning) är okontrollerad: `?q=volvo volvo` → två chips med key `q-volvo` (React-varning, instabil rekonsiliering); `?q=Volvo volvo` → × på lilla "volvo"-chipen tar bort "Volvo" (första ci-träffen). Wire-semantiskt ekvivalent (samma FTS-lexem) så ingen sökkorrekthet påverkas. Föreslås: index-baserad key-suffix för q-chips eller ci-dedupe i `buildChipModels`.

### Minor 2 — `onSelectSuggestion` committar ovillkorligt

`jobb-hero-search.tsx:148–155`. Val av en redan-vald suggestion ger no-op-`router.replace` + omsatt identisk announcement (ingen DOM-ändring → ingen SR-annons, vilseledande tystnad). Tokenizer-vägen har `sameState`-guard; förslags-vägen saknar den. Symmetri-fix: guarda commit med `sameState` (exportera den ur tokenize eller flytta till chip-models).

### Minor 3 — Död `selectRef` i toolbaren

`jobb-results-toolbar.tsx:88, 208`. Deklarerad + fäst men aldrig läst (pre-existerande, men refaktorn rörde hela filen — in-scope hygien per §9.6). Ta bort ref:en + `useRef`-importen.

### Minor 4 — Hårdkodad "100" i q-max-copyn

`jobb-hero-search.tsx:230`. `Q_MAX_LENGTH` finns som delad konstant men hjälptexten hårdkodar "max 100 tecken" — interpolera konstanten så backend-synk (cross-ref-disciplinen konstanten själv dokumenterar) inte kan missa copyn.

---

## Bra gjort

- **Buggklassen orepresenterbar, inte buggen fixad:** chips deriveras helt ur URL:en, enda lokala staten är utkastet — E2d-symptomen ("val söker direkt + tömmer", "tagg kvar men sökord borta") kan inte längre uttryckas i koden. VAL 1=A korrekt och konsekvent implementerad.
- **SPOT genuint levererad:** en enda kind→dimension-väg (`composeSuggestionChip` oförändrad, tokenizern syntetiserar SuggestionDto); `buildChipModels`/`removeChipFromState` konsumeras av båda renderingarna; toolbarens E2g-divergenta useState-kopior eliminerade i samma touch — CTO in-block-mandaten 1–3 levererade utan rest.
- **Tokenizern som ren funktion** med 17 datafalls-tester inkl. ambiguitet, operator-strip, q-max, idempotens, degraderad taxonomi — §2.4 efterlevd; `-`-strippen (NOT-neutralisering) testad så Klas minus-beslut förblir genuint öppet.
- **Q_MAX_LENGTH-cross-refen är korrekt åt båda håll** (verifierad mot `SearchCriteria.QMaxLength=100`) och guardar validator-gränsen utan att duplicera parserns hygien-regler — exakt den gränsdragning CTO F2.8 krävde.
- **No-JS-kontraktet intakt:** name-växlingen rå-input ↔ hidden-q är vattentät; `useSyncExternalStore`-flaggan utan effect och utan mismatch.
- **push/replace-asymmetrin** dokumenterad i kodkommentarer på båda sidor med semantisk motivering — granskningsbar i diffen som CTO krävde.
- Inga §5.2-träffar: ingen `any`, ingen useEffect-fetch (nya ytor), ingen placeholder (Klas hård regel respekterad — hjälptexten bär instruktionen), inga emoji/utropstecken... förutom att "Söktexten är full" är saklig civic-utility-copy.

---

## Sammanfattning och delegationer

0 Blocker — inget veto-HALT. 2 Major, 4 Minor.

| Fynd | Åtgärd | Ägare |
|---|---|---|
| Major 1: CTO-avvikelse sentinel + stale limitNotice | PR-body-not + CTO-ack; limitNotice-fix in-block | CC + senior-cto-advisor |
| Major 2: guard-/annons-testgap (DoD-mitigeringar) | Komplettera typeahead- + hero-tester | test-writer |
| Minor 1–4 | In-block om tid finns, annars FYI i PR | CC |

Re-review behövs ej för Minor; Major 1–2 verifieras i PR-diffen innan automerge-label sätts (CLAUDE.md §6.3 p.4: ej-åtgärdat Major = STOPP till Klas i stället för label).

**Parallella granskare som krävs innan merge:** design-reviewer (chips-UI, q-max-copy, mouseover-Tab-skavanken — CTO-flaggad), dotnet-architect-rapporten finns redan. Ingen security-auditor-trigger (ingen PII/auth/secrets-yta; Variant C-capture-hinten avvisad).
