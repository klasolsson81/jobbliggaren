# code-reviewer — Fas E2d typeahead-chip-komponist (granskningsdom)

**Status:** ✓ Approved — mergeklar
**Granskat:** 2026-06-11
**Granskare:** code-reviewer (VETO-mandat: Block=HALT)
**Auktoritet:** CLAUDE.md §2.1 (Clean Arch/lager), §4.1–4.3 (TS/React), §5.2 (FE anti-patterns), §9.6 (in-block vs TD)
**Scope:** FE-only — `git diff c3a8b57 -- web/jobbpilot-web/src docs/reviews/2026-06-11-sok-paritet-e2d-*.md`. Ingen backend-/PII-/auth-/secrets-touch → security-auditor ej triggad. Rendered-UI/aesthetik → design-reviewer-scope (ej här).
**Beslutskontext läst helt:** CTO-dom (`…-e2d-cto.md`), architect-dom (`…-e2d-architect.md`).

**Block: 0 — Major: 0 — Minor: 2 — Praise: 8**

---

## Verifierat grönt (oberoende re-körning, ej enbart tilltro till prompt)

| Gate | Resultat |
|---|---|
| `pnpm vitest run` (4 E2d-filer) | **47 passed** (chip-composition 11, jobb-hero-search 7, typeahead 6, hero-filters 23) |
| `pnpm exec tsc --noEmit` | **0 fel** |
| `pnpm exec eslint` (4 källfiler) | **0 fel/varningar** |
| `pnpm build` (RSC-payload-grind, AGENTS.md-krav) | **grön** — `/jobb` server-renderar utan serialiserings-fel |

RSC-grinden är den relevanta här: `page.tsx` skickar `taxonomy`/conceptId-arrays/`q`/`sortBy`/`pageSize` in i `JobbHeroSearch`-ön. Inga funktioner korsar RSC↔client-gränsen (`onChange`/`onSelect` är wirade INNE i ön). Build bekräftar serialiserbarheten — exakt det vitest/tsc/jsdom missar.

---

## Granskning per flaggad punkt (prompten 1–6)

### 1. Clean Arch / lager / SPOT / DRY ✓

- **SPOT bevarad:** `buildJobbHref` (search-params.ts) förblir ENDA URL-skrivaren. `JobbHeroSearch` blir tredje konsument (typeahead-ö) bredvid `JobbHeroFilters` + toolbar — ingen ny URL-builder, ingen duplicerad param-bevarande-logik. Konformerar mot CTO VAL 1=A (Martin SRP kap. 7) och E2g-prejudikatet.
- **DRY mot ort-normalisering:** `composeSuggestionChip` återanvänder `applyMunicipalityChange` (ort-selection.ts) för Municipality-grenen i stället för att återimplementera per-läns-släckningen. Korrekt SPOT mot popoverns normalisering.
- **`useSelectedChips`-hooken (CTO F4) byggdes EJ — och det är RÄTT.** CTO:ns extraktion var villkorad: "*annars* duplicerar E2d toolbar-logiken". `JobbHeroSearch` renderar INGA egna chips (kommentar rad 24: "Chipet renderas i toolbarens chip-rad … ur URL:en"). Toolbaren äger redan URL→chip-deriveringen; E2d adderade ingen andra konsument → ingen DRY-skuld → ingen hook behövs. Att INTE bygga den är korrekt YAGNI, inte en utelämning. **Inget TD** (§9.6: ingen duplicering existerar att betala av).

### 2. prev-prop-sentinel + race vs useTransition ✓ (korrekt + lint-säkert)

`jobb-hero-search.tsx:61-66` — `setState`-under-render-mönstret är Reacts dokumenterade "adjusting state when a prop changes" ("You Might Not Need an Effect"). Verifierat:

- **Lint-säkert:** ingen effect → ingen `react-hooks/set-state-in-effect`-träff. eslint grön bekräftar.
- **Ingen race mot `useTransition`:** sentinelen läser den COMMITTADE `q`-propen, inte transition-pending state. Under pågående `startTransition(router.push(…))` är `q`-propen fortfarande gammal → sentinel triggar inte spuriöst. Vid commit anländer ny `q` → draft synkar. Idempotent vid Title-val (`setDraft(label)` följt av navigate; efter commit `q===label` → samma värde sätts om).
- **Konformerar E2g:** URL=sanning, draft=härlett utkast, ingen state-kopia som driftar (exakt E2g-buggens motgift).

Testat: `…fält-synk mot URL`-testet rerender:ar med ny `q`-prop och verifierar att fältet speglar den. Grönt.

### 3. composeSuggestionChip-korrekthet ✓ (exhaustiv, defensiv, dedupe-korrekt)

- **Exhaustiv switch + `assertNever`:** alla 5 `SuggestionKind`-medlemmar (Title/Region/Municipality/OccupationField/OccupationGroup, verifierat mot `SUGGESTION_KIND_ORDER`) har gren; `default` → `assertNever(kind: never)`. VAL 2c gjort rätt: när EmploymentType/WorktimeExtent adderas vid B2 blir argumentet ej längre `never` → **compile-fel** tvingar additiv gren. Ingen tyst odimensionerad väg. Falsk-klar-skydd per CLAUDE.md §2.5/§9.6.
- **OR-inom + dedupe:** `addUnique` är idempotent; OccupationGroup-dedupe och OccupationField-materialiserings-dedupe testade (`…dedupe:ar mot redan valda barn`).
- **Per-län-normalisering:** Region-grenen släcker länets egna kommuner (`municipalityIdsOfRegion`); Municipality-grenen delegerar till `applyMunicipalityChange`. Cross-län-mix bevaras (test rad 143). Korrekt union-semantik (E2b).
- **null-conceptId defensivt:** varje dimension-gren returnerar `current` oförändrat vid `conceptId === null` (test rad 154). Title-grenen kräver inget conceptId (fri q).
- **Degraderad ACL-fallback:** OccupationField utan taxonomi/okänt id → `q: label` i stället för tyst no-op (test rad 101, ADR 0043 Beslut B graceful). Bra — klicket leder alltid någonstans.

### 4. a11y-combobox ✓ (DoD-krav uppfyllt, F5)

- `role="combobox"` + `aria-expanded` + `aria-controls` + `aria-autocomplete="list"` + `aria-activedescendant` (pekar på aktiv option-id endast när lista öppen + rad markerad).
- `<ul role="listbox">` + `<li role="option" aria-selected>`. ArrowDown/Up wrap-navigerar, Enter väljer markerad (preventDefault hindrar form-submit), Enter UTAN markering bubblar till `<form>` = fri sökning (medveten VAL 3=A-väg), Escape stänger.
- `onMouseDown` + `preventDefault` behåller input-fokus så blur inte stänger listan före valet — korrekt mönster.
- `role="status" aria-live="polite"` annonserar antal förslag / laddning. rateLimited degraderar civilt med saklig copy (ingen emoji/utropstecken — CLAUDE.md §10.3).
- Keyboard-vägen testad end-to-end (`keyboard ArrowDown + Enter selects the active option`). Grönt.

### 5. No-JS GET-fallback ✓ (äkta progressive enhancement)

`<form action="/jobb" method="get">` med `onSubmit`-preventDefault för JS-vägen. Synlig input bär `name="q"` (= det faktiska GET-param-namnet) — INGEN separat hidden `q` → ingen dubbel-submit-kollision. Aktiva dimensioner (occupationGroup/region/municipality) + sortBy (≠default) + pageSize renderas som hidden inputs. `page` utelämnas medvetet (ny sökterm → sida 1). Testat (`renderar en äkta GET-form med q + hidden inputs`). Utan JS submittar formuläret nativt och bevarar filter. Konformerar CLAUDE.md §5.2 + civic-utility-doktrin.

### 6. CSS overflow-escape ✓ (stabilt, dokumenterat)

DOM: `.jp-hero__searchblock` (nu `position: relative`) → `.jp-hero__searchrow` (`overflow:hidden`, opositionerad) → `.jp-hero__searchfield` (typeahead-wrapper, opositionerad) → absolut dropdown. Dropdownens containing block blir `.jp-hero__searchblock` (närmaste positionerade förfader) → den escapar searchrowens klippning. Mekaniken är korrekt och CSS-kommenterad. Inga design-token-brott (endast `position`/`flex` — ingen gradient/shadow/glow). Rendered-verifiering = design-reviewer-scope.

---

## Minor (mergeblockerar EJ)

### Minor 1 — Latent CSS-fragilitet: dropdown-escape förutsätter opositionerade mellanled

`globals.css` — overflow-escapen fungerar ENBART så länge `.jp-hero__searchrow` och `.jp-hero__searchfield` förblir opositionerade. Lägger någon senare `position: relative/absolute` på endera klipps dropdownen igen (containing block flyttar in i `overflow:hidden`-elementet). CSS-kommentaren beskriver mekaniken men inte beroendet. Ingen åtgärd krävs nu — noterat som regressions-risk för framtida hero-CSS-touch. Inte värt en TD (§9.6: ingen annan fas, ingen saknad dependency; ren framtida-vaksamhet).

### Minor 2 — `selectAllLabel(activeGroup)` har en defensiv-död `""`-gren

`jobb-filter-popover.tsx:408` — `label={activeGroup ? selectAllLabel(activeGroup) : ""}`. `""`-fallbacken är onåbar: select-all-raden renderas bara i `else`-grenen (höger-items finns), vilket kräver `activeGroup != null`. Tom-label kan alltså aldrig nå skärmen. Defensivt ofarligt, men en onåbar gren är lätt att misstolka vid framtida läsning. Kan ersättas med en non-null-assert + kommentar eller lämnas. Trivial — ingen funktionell påverkan.

---

## §9.6-triage (TD?) — inga TDs lyfts

| Fynd | TD? | Motivering |
|---|---|---|
| `useSelectedChips`-hook ej byggd | **Nej** | Villkorad på duplicering som inte uppstod (hero-search renderar inga chips). Ingen skuld. |
| VAL 2c EmploymentType gated | **Nej** | Korrekt gated B2-data-dependency; `assertNever` ger compile-tvång vid re-ingest. |
| Minor 1 (CSS-fragilitet) | **Nej** | Framtida-vaksamhet, ingen annan fas/saknad dependency. |
| Minor 2 (död `""`-gren) | **Nej** | Trivial in-block-hygien om man vill; ingen skuld. |

Konformerar CTO/architect-domarnas TD-sammanställning. Inget i E2d är uppskjuten skuld.

---

## Bra gjort (Praise)

1. **Exhaustiv switch + `assertNever`** — VAL 2c "re-ingest-redo" gjort exakt rätt: kompilatorn, inte en runtime-no-op, bär falsk-klar-skyddet.
2. **`composeSuggestionChip` som ren funktion** — testbar utan DOM/router; 11 fokuserade enhetstester täcker varje gren inkl. defensiva kanter.
3. **prev-prop-sentinel i stället för effect** — Reacts kanoniska prop→state-synk, lint-ren, race-fri mot useTransition. Undviker E2g-buggen by design.
4. **buildJobbHref-SPOT respekterad** — tredje URL-skrivaren utan ny builder; symmetriskt param-bevarande.
5. **DRY mot `applyMunicipalityChange`** — Municipality-grenen återanvänder popoverns normalisering i stället för att återimplementera per-läns-släckning.
6. **a11y-combobox komplett** — aria-activedescendant + tangentbordsnav + onMouseDown-fokusbevarande + aria-live-status; keyboard-vägen testad.
7. **Äkta no-JS GET-fallback** — `name="q"` på synlig input + hidden inputs för dimensioner; ingen dubbel-q-kollision; testat.
8. **Graceful degradering genomgående** — degraderad ACL → q-fallback, rateLimited → civil copy, taxonomy null → defensiva no-ops. Civic-utility-pålitlighet (CLAUDE.md §1).

---

## Sammanfattning

E2d konformerar fullständigt mot CTO-/architect-domarna: VAL 1=A (separat ö), VAL 2a (materialisering), VAL 2b (Occupation ute — ingen `Occupation`-medlem i enumen), VAL 2c (gated exhaustiv switch), VAL 3=A default (hela strängen → residual-q, ingen auto-chip-parser). Lager/SPOT/DRY intakt, a11y DoD-uppfylld, no-JS-fallback äkta, RSC-build grön. Två Minors, båda kosmetiska/framtida-vaksamhet, ingen mergeblockerare.

**Ingen VETO. Mergeklar.** De två Minors kan adresseras opportunistiskt vid nästa hero-CSS-/popover-touch eller lämnas.
