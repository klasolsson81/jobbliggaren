# Design-review: Fas E2d — typeahead-chip-komponist (branch `feat/sok-paritet-chip-komponist-e2d`)

**Status:** ✓ Approved (kod/CSS) — rendered-GO pending live-deploy (manifest)
**Granskat:** 2026-06-11
**Auktoritet:** DESIGN.md, jobbpilot-design-a11y / -copy / -components / -tokens-skills, ADR 0068, ADR 0047 (Area 5)
**Diff-bas:** `git diff c3a8b57 -- web/jobbpilot-web/src` + ny fil `jobb-hero-search.tsx` (untracked mot bas)

## FAS-DEFERRAL-MANIFEST (bindande, citerat)
- Rendered UI-granskning på Vercel = **PENDING live-deploy** (auth-gated /jobb). Denna review är kod/CSS-statisk. Rendered-GO ligger hos Klas senare.
- VAL 3 (auto-chip-vid-submit) = Klas-STOPP, EJ byggd i denna PR — granskas ej här.
- Spec-edits (DESIGN.md / tokens-skill) kräver Klas `approve-spec-edit.sh` — föreslås, appliceras EJ.

Inga Blockers → **ingen veto**, manifestet behöver ej åberopas mot HALT.

---

## Area 1 — Civic-utility aesthetik

**Godkänd.** Inga AI-design-mönster.
- Ingen emoji, inga utropstecken i någon copy (typeahead, rateLimited, label, popover-strängar).
- Ingen gradient/glow/glasmorfism införd. Dropdownen är `shadow-md` (popover-lager — uttryckligt OK per komponent-skillen och scope-direktivet ≤md).
- Radius: dropdownen `rounded-md` (= `--jp-r-md` 6px), inom taket. `jp-hero__searchfield` ärver pill-radien — ingen ny radius-överträdelse.
- Flat lista, ingen ikon-dekoration på optionerna (Search-ikonen sitter bara på Sök-knappen, `aria-hidden`). Markerad rad = `bg-surface-tertiary` (token, ingen färg-glow). Detta är exakt civic-utility-mönstret komponent-skillen efterlyser.
- Inga hårdkodade hex / Tailwind-defaults. Alla färgklasser går via token-aliasen (`border-border-default`, `bg-surface-primary`, `bg-surface-tertiary`, `text-text-primary`, `text-text-secondary`).

ADR 0068: typeahead-ön introducerar INGEN ny accent-yta — Sök-knappen behåller ink-bakgrunden (`--jp-hero-sok-bg`, tema-stabil, EJ grön per CTO Beslut 1), markerad rad använder neutral `surface-tertiary` (ej accent-50). Korrekt — selektions-accenten i listboxen är medvetet neutral, vilket undviker dubbel-grön-risken i en gradient-närliggande yta.

## Area 2 — Design tokens

**Godkänd.** Genomgående token-disciplin. Den enda råa CSS:en (`flex: 1; min-width: 0; width: 100%` i `.jp-hero__searchfield`) är layout, inte färg/spacing-token — legitimt. `position: relative` på `.jp-hero__searchblock` är välmotiverat (containing-block för dropdownen så den escapar `searchrow`-overflowen) och påverkar inte sökradens visuella uttryck.

## Area 3 — Accessibility (WAI-ARIA combobox + listbox)

**Godkänd — mönstret är komplett och korrekt.** Detta är fasens tyngsta yta och den är byggd enligt boken:

- `role="combobox"` på inputen + `role="listbox"` på `<ul>` + `role="option"` på raderna. Inga separata tab-stopp — fokus stannar i inputen (option-mönstret, ej menu-mönstret). Korrekt val för en sökruta.
- `aria-expanded` följer `showList`, `aria-controls={listId}`, `aria-autocomplete="list"` — fullständig combobox-koppling.
- `aria-activedescendant` pekar på rätt option-id ENDAST när `showList && active >= 0`, annars `undefined`. Markerad rad = `aria-selected={i === active}`. Detta är den dokumenterade aria-activedescendant-strategin (virtuell fokus, fysisk fokus kvar i input) — korrekt.
- Tangentbord: ArrowDown/ArrowUp wrap:ar (modulo), Enter på markerad rad `preventDefault` + väljer; Enter UTAN markerad rad bubblar avsiktligt till `<form>` = fri sökning. Escape stänger + nollställer `active`. Test täcker ArrowDown×2 + Enter → andra raden vald. Komplett kedja.
- `onMouseDown` + `preventDefault` (ej `onClick`) bevarar input-fokus så blur inte stänger listan före valet — den klassiska combobox-fällan är korrekt undviken och kommenterad.
- Live region: `role="status" aria-live="polite"` annonserar "Hämtar förslag…" / "N förslag" (sr-only). Polite, ej assertive — rätt för rutinuppdatering (a11y-skill §6).
- Fokus-ring bevarad: nya `<input>` får `className="jp-hero__input"` som bär eget `:focus-visible` (globals.css:1060). Bytet från shadcn `<Input>` till rå `<input>` tappar alltså INTE fokusindikatorn. Verifierat.
- Markerad-rad-kontrast: `text-text-primary` på `bg-surface-tertiary` — ink-1 på slate-50-nivå, klarar 4.5:1 i båda teman (samma par som popover-hover-raderna, redan token-verifierat).
- `name`-prop + no-JS `<form action="/jobb" method="get">` med hidden inputs = äkta progressive enhancement. Inputen har riktig `<label htmlFor="jobb-q">` (instruktionen i labeln, INGEN placeholder — Klas hård regel uppfylld, memory `feedback_no_placeholder_example_text`).

**Tri-state mixed (popover, E2d-Minor m4 / E2f Minor 4):** `aria-checked={indeterminate ? "mixed" : checked}` på CheckRow, driven av `selectAllMixed = !selectAllChecked && rightAnySelected`. Korrekt WAI-ARIA tri-state-checkbox-semantik — vid partiellt kommun-val hör skärmläsaren "delvis markerad", inte "omarkerad". Test (`Hela {länsnamn}-raden är tri-state 'mixed' vid partiellt val`) verifierar beteendet. E2f-domens Minor 4 är därmed **åtgärdad korrekt**.

## Area 4 — Svensk copy

**Godkänd.**
- Label: "Sök efter yrke, arbetsgivare eller ort" — saklig, du-implicit, ingen exempeltext-i-ruta. Korrekt.
- rateLimited: "För många sökningar på kort tid. Förslagen pausas en stund. Du kan fortsätta skriva och söka ändå." — informativ degradering, konkret konsekvens + handlingsväg, inget utropstecken, ingen skuld. Mönstergill civic-degradering (copy-skill §3 + regel 6).
- Live-region: "Hämtar förslag…" med korrekt trepunkt-Unicode (`…`), "N förslag" — kortfattat. Korrekt.
- **"Hela {länsnamn}" (E2d-Minor):** `selectAllLabel={(g) => \`Hela ${g.label}\`}` ger "Hela Stockholms län" per aktivt län (var statiskt "Hela länet"). Per-grupp-precision enligt copy-skillen. Yrke behåller statiskt "Välj alla yrkesgrupper" via `() => "..."`. Korrekt — E2f-domens "Hela {länsnamn}"-punkt **åtgärdad**.
- **dialogLabel (E2d-Minor):** `aria-label={dialogLabel ?? leftTitle}` → "Ort"/"Yrke" matchar nu triggerns pill-namn istället för kolumn-titeln "Län"/"Yrkesområde". Skärmläsaren annonserar samma sak som pillen användaren klickade. Korrekt — E2f-domens dialogLabel-punkt **åtgärdad**. Test-svit uppdaterad konsekvent (`{ name: "Ort" }`/`{ name: "Yrke" }`).

## Area 5 — Task-completion / flödesbegriplighet (ADR 0047)

**Godkänd på den statiska ytan; en rendered-observation noterad nedan (Minor).** Walk av interaktionspaten:

- **Completable without guessing:** Två tydliga vägar — (a) skriv fri text → Enter/Sök-knapp = fri sökning; (b) skriv → välj taxonomi-förslag → strukturerat dimension-chip via `composeSuggestionChip` + `router.push`. Title-förslag behåller texten i fältet (`setDraft(suggestion.label)`); dimension-förslag rensar utkastet tillbaka till committad q. Beteendet är förutsägbart och self-explanatory.
- **System status visible & anchored:** URL är enda sanningen (E2g-disciplin) — chipet materialiseras i toolbarens chip-rad + popover-räknare ur URL:en, ingen divergerande lokal kopia. `draft` speglar URL-q via prev-prop-sentinel under render (dokumenterat React-mönster, ingen set-state-in-effect). Status visar nuvarande state, inte ett nästa-state förklätt som nuvarande. Bra.
- **Irreversible actions:** Inga — sökning/filtrering är fullt reversibel (URL-navigering). Ej tillämpligt.
- **Separate tasks not intertwined:** Sök-ön (`JobbHeroSearch`) och filter-ön (`JobbHeroFilters`) är separata öar som skriver samma URL via `buildJobbHref` (SPOT, CTO VAL 1 Variant A). Ingen visuell sammansmältning av två formulär — sökrutan är en `<form>`, popoverna egna dialoger. Korrekt separation.

## Blockers
Inga.

## Major
Inga.

## Minor

1. **rateLimited/status-`<p>` renderas inuti pill-raden — kontrast + klippning att verifiera live (rendered, manifest-deferd).** `JobAdTypeahead`-wrappern är nu `.jp-hero__searchfield` (`flex:1`) inuti `.jp-hero__searchrow` (`overflow:hidden`, `bg: var(--jp-hero-pill-bg)`). rateLimited-`<p class="text-body-sm text-text-secondary">` är ett barn av wrappern. På /jobb-hero-ytan innebär det att (a) texten ligger på pill-bakgrunden, inte den vita app-canvasen som `text-text-secondary` är kontrast-verifierad mot, och (b) den kan klippas av `searchrow`-overflowen (samma overflow som dropdownen medvetet escapar via `position:relative`-tricket — men rateLimited-`<p>` har ingen sådan escape, den är inline i flex-flödet). **Verifiera vid rendered-GO:** att rateLimited-raden faktiskt syns (ej overflow-klippt) OCH att `text-text-secondary` mot `--jp-hero-pill-bg` klarar 4.5:1. Om den klipps/fail:ar: flytta raden utanför `searchrow` (syskon till `.jp-hero__searchrow` i `.jp-hero__searchblock`) eller ge den hero-lokal ink-token. Inte en Blocker — rateLimited är ett sällan-tillstånd (429), och i den generiska `relative flex flex-col`-wrappern (icke-hero-bruk) är paret redan korrekt. Men det är den enda ytan där bytet shadcn-`<Input>` → rå `<input>` + hero-wrapper kan ha en oavsedd kontext-konsekvens.

2. **`aria-activedescendant`-konsistens vid mus-hover utan tangentbord (FYI):** `onMouseEnter={() => setActive(i)}` sätter `active`, vilket också uppdaterar `aria-activedescendant`. Det är korrekt och ofarligt (virtuell fokus följer pekaren), men innebär att en skärmläsar-användare som råkar svepa musen får `activedescendant` flyttad. Inget WCAG-fel — noteras bara som medveten design. Ingen åtgärd.

## Bra gjort

- WAI-ARIA combobox-mönstret är komplett — `role`/`aria-expanded`/`aria-controls`/`aria-autocomplete`/`aria-activedescendant`/`aria-selected` alla på plats, tangentbordskedjan (Arrow/Enter/Escape) korrekt, virtuell-fokus med fysisk fokus i input.
- `onMouseDown`-preventDefault-fällan korrekt undviken och kommenterad — vanlig combobox-bugg som ofta missas.
- Progressive enhancement på riktigt: no-JS `<form method="get">` + hidden inputs bär alla aktiva filter; JS-vägen är ren enhancement. Civic-utility-robusthet.
- INGEN placeholder — labeln bär instruktionen (Klas hård regel uppfylld utan att behöva påpekas).
- Fokus-ring bevarad genom `<Input>`→`<input>`-bytet (jp-hero__input bär egen `:focus-visible`).
- E2f-domens tre punkter (tri-state mixed, "Hela {länsnamn}", dialogLabel) alla åtgärdade korrekt OCH testtäckta.
- Tri-state mixed är ärlig accessible state — partiellt val låter inte som omarkerat.

## Sammanfattning

**0 Blockers, 0 Major, 2 Minor (1 rendered-att-verifiera, 1 FYI).** Kod/CSS-utförandet är genomgående korrekt — combobox-a11y enligt boken, token-disciplin intakt, copy civic-utility-ren, E2f-arvet åtgärdat och testtäckt. Ingen veto. Minor 1 (rateLimited-radens kontrast/klippning i hero-pillen) verifieras vid Klas rendered-GO på live-deploy per manifestet; den blockerar inte merge eftersom det är ett 429-sällantillstånd och bytet är säkert i den generiska wrappern. Re-review ej nödvändig — vid rendered-GO räcker en punktkoll av Minor 1.
