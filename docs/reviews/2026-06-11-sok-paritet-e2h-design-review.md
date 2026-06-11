# Design-review: Fas E2h — chips-i-sökfältet (branch `feat/sok-paritet-chip-in-field-e2h`)

**Status:** ✓ Approved — veto hävt vid re-review 2026-06-11 (se "## Re-review" nedan; ursprunglig dom var ⛔ Blocked, 2 Blockers + 4 Major)
**Granskat:** 2026-06-11
**Granskare:** design-reviewer (Opus 4.7)
**Diff:** working tree mot main `1061bc2` + untracked
**Auktoritet:** DESIGN.md, jobbpilot-design-a11y (WCAG 2.1 AA), jobbpilot-design-copy, jobbpilot-design-tokens, ADR 0068 (banner-lokala kontroller), ADR 0047 (Area 5)

## FAS-DEFERRAL-MANIFEST (kvitterat)

- Rendered-granskning pending live-deploy/Klas lokala test — detta är kod/CSS-review.
- Minus-operatorn (NOT) out-of-scope; tokenizerns `-`-strip granskad endast som neutralisering.
- Klas-spec 2026-06-11 är LÅST produktbeslut: chips i fältet, allt blir taggar,
  mellanslag/komma avgränsar, live-resultat, Tab väljer markerat (medvetet APG-avsteg).
  Inga fynd nedan ifrågasätter spec-beslutet — de gäller implementationens a11y/tokens/copy.

---

## Blockers (måste fixas innan merge — VETO)

### B1. Osynlig fokus-ring på chip-×-knapparna (WCAG 2.4.7)

Fil: `web/jobbpilot-web/src/app/globals.css` (`.jp-filterchip--field`, rad ~1047) +
`web/jobbpilot-web/src/components/job-ads/chip-search-field.tsx:50–57`

`.jp-hero__plate` scopar `--jp-focus: #FFFFFF` (globals.css rad 1261–1264 — vit ring
mot grön gradient). De nya ×-knapparna ligger inuti plattan men inne i det **vita**
sökfältet (`.jp-hero__searchrow`, `--jp-hero-pill-bg: #FFFFFF`). Globala
`*:focus-visible` (rad 346) ger dem därmed **vit ring mot vitt fält / ljus chip
(#F4F6FA)** i light mode — fokusindikatorn är osynlig. Detta är exakt problemet som
`.jp-hero__input:focus-visible` (rad 1085–1090) löste med inåtvänd ink-ring; de nya
fokuserbara elementen fick ingen motsvarande scoping.

Krävs (förslag, samma mönster som inputens lösning — tema-stabil ink):

```css
.jp-filterchip--field .jp-filterchip__rm:focus-visible {
  outline: 2px solid var(--jp-hero-sok-bg); /* #0C1A2E, tema-stabil */
  outline-offset: 0; /* searchrow har overflow:hidden — håll ringen innanför */
}
```

Motivering: WCAG 2.4.7 — synlig fokusindikator är icke-förhandlingsbar. Tangentbords-
användaren kan inte se vilken chip-× som är fokuserad.

### B2. Fokus tappas till body när chip tas bort via × (WCAG 2.4.3 / fokushantering)

Fil: `web/jobbpilot-web/src/components/job-ads/chip-search-field.tsx:50–57`

När ×-knappen aktiveras (Enter/Space) tas chipen bort ur DOM → fokus faller till
`<body>`. Tangentbordsanvändaren måste tabba om från dokumentets början för varje
borttagen chip. Backspace-vägen behåller fokus i inputen (korrekt), men ×-vägen —
den enda vägen för en användare som tabbat in på en mitt-chip — strandar.

Krävs: flytta fokus explicit efter borttagning. Enklast och mest förutsägbart:
fokusera typeahead-inputen (ref i `ChipSearchField`, anropa `.focus()` i
×-handlern innan/efter `onRemoveChip`). Alternativ: föregående chips ×-knapp
(APG-chipmönster), men input-fokus matchar "ta bort och skriv vidare"-flödet bättre.

Motivering: fokusförlust vid elementborttagning är ett etablerat a11y-fel
(GOV.UK Design System + APG kräver explicit fokushantering). A11y-fel är Blocker —
inga undantag.

---

## Major (blockerar merge tills åtgärdat)

### M1. Tema-skiftande chip-tokens inne i det tema-stabila vita fältet

Fil: `web/jobbpilot-web/src/app/globals.css` (`.jp-filterchip--field`)

`.jp-filterchip--field` sätter `background: var(--jp-surface-2)` och ärver
`color: var(--jp-ink-1)` + `border: var(--jp-border)` från `.jp-filterchip`.
Alla tre skiftar i dark mode (`#142136` / `#F4F7FC` / `#44598A`) — men fältet de
ligger i är **tema-stabilt vitt** (banner-lokala kontroller, CTO Beslut 1,
dokumenterat i globals.css rad 928–929: "Alla kontroller i plattan är TEMA-STABILA").
I dark mode blir resultatet mörka navy-chips inne i ett vitt fält på den gröna
plattan — kontrasten håller tekniskt, men det bryter den dokumenterade
banner-lokala principen och inverterar fältets visuella logik.

Krävs: banner-lokala tema-stabila värden (samma mönster som `.jp-hero-chip`):

```css
.jp-filterchip--field {
  height: 28px;
  padding: 0 6px 0 10px;
  font-size: 13px;
  background: #F4F6FA;        /* tema-stabil — INTE var(--jp-surface-2) */
  border-color: #C9D2E0;
  color: #0C1A2E;
}
.jp-filterchip--field .jp-filterchip__rm { color: #455366; }
.jp-filterchip--field .jp-filterchip__rm:hover { color: #0C1A2E; }
```

(`.jp-filterchip__rm` ärver annars `--jp-ink-2` = `#C2CFE2` i dark — 1,6:1 mot ljus
tema-stabil chip; därför måste även rm-färgen scopa-pinnas.)

### M2. q-max-varningen annonseras inte för skärmläsare

Fil: `web/jobbpilot-web/src/components/job-ads/jobb-hero-search.tsx:228–232`

När ett ord vägras av q-max-guarden stannar ordet tyst kvar i fältet och
hjälptexten byter innehåll — men `<p id={helpId}>` har ingen live-region. En
skärmläsaranvändare som skriver får ingen signal om att ordet INTE blev en tagg;
`aria-describedby` läses bara vid fokusering, inte vid innehållsbyte.

Krävs: `role="status"` (implicit `aria-live="polite"`) på hjälptext-p:n — swapen
annonseras då vid byte, och default-texten annonseras inte vid mount (live-regioner
annonserar ändringar, inte initialt innehåll). Alternativ: routa varningen genom den
befintliga sr-only-annonsregionen.

**Dom på flödesfrågan (granska särskilt #4):** swap-in-place är RÄTT mönster —
varningen är redan id-kopplad till fältet, texten ger orsak + åtgärd, och en separat
röd textrad på den mörkgröna plattan vore både kontrastproblem och paniksignal som
inte matchar civic-utility-tonen. Ingen särskiljande färg krävs — men annonseringen
ovan är obligatorisk, annars existerar varningen inte för skärmläsaranvändare.

### M3. Grammatisk kongruens i aria-live-annonserna

Fil: `web/jobbpilot-web/src/components/job-ads/jobb-hero-search.tsx:139, 151, 166, 173`

`` `${label} tillagd` `` / `` `${label} borttagen` `` kongruensböjs mot en-genus.
Taxonomi-labels är godtyckliga: "Stockholms län tillagd" är fel svenska (län är
ett-ord → "tillagt"). Kongruens mot godtyckliga labels är olösbart med
efterställd particip.

Krävs: kongruensfri verbform — `` `Lade till ${label}` `` / `` `Tog bort ${label}` ``.
Rak svenska, fungerar för alla genus, samma längd.

### M4. Mouseover sätter markering + Tab = oavsiktligt val (architect-flaggad skavank — min dom: fixa)

Fil: `web/jobbpilot-web/src/components/job-ads/job-ad-typeahead.tsx:267`

`onMouseEnter={() => setActive(i)}` har ingen motsvarande reset vid mouseleave.
Markeringen ligger kvar på en rad musen lämnat — och med `selectOnTab` betyder en
parkerad muspekare över listan att nästa Tab (avsett som fokus-flytt) i stället
committar ett förslag användaren inte tittar på. Drabbar mixed-input-användare
(skriver + har musen vilande över resultatområdet). Påverkar även Enter, men Tab
är värre eftersom Tab-intentionen oftast är att lämna fältet.

Krävs: `onMouseLeave={() => setActive(-1)}` på `<ul role="listbox">` (en rad).
Markeringen följer då alltid antingen aktiv pekare eller aktivt tangentbord —
aldrig en historisk pekarposition. aria-live-annonsen mildrar skadan men
förebyggande är bättre än upptäckt.

---

## Minor (nice-to-fix, blockerar inte)

### Mi1. Tab-ordning: chips-× före input

Acceptabelt som det är: ×-knapparna MÅSTE vara tangentbords-nåbara, chips-antalet är
typiskt litet, och `<label htmlFor="jobb-q">` ger klickbar genväg till inputen.
Om chips-antalet växer (>5–6 vanligt): överväg roving tabindex (ett tab-stopp för
chip-gruppen, pilnavigering inom) per APG:s chip-mönster. Inte nu.

### Mi2. Dimension-chip och fritext-chip ser identiska ut i fältet

Toolbar-chipen bär axel-ikon (Briefcase/MapPin); fält-chipen bär bara label. En
användare kan inte se om "Göteborg" blev kommun-filter eller fritext-ord förrän
toolbaren speglar det. Klas-spec säger "allt blir taggar" men inget om
differentiering — överväg samma 12px-ikon på dimension-chips i fältet vid
rendered-iterationen. Flödesbegriplighet, inte blocker.

### Mi3. Sök-knappens placering vid wrap

`.jp-hero__searchbtn` är 52px fast höjd; när chipfielden wrappar växer raden men
knappen stannar topp-justerad (flex stretch kan inte övertrumfa explicit höjd).
Troligen acceptabelt — verifiera vid rendered-test, ev. `align-items: stretch` +
intern centrering om det ser hängande ut.

### Mi4. Hjälptext-kontrasten passerar med tunn marginal

`--jp-hero-ink-soft` (rgba(255,255,255,0.78)) mot gradientens ljusaste stopp
`#1E6B4C`: beräknad effektiv text ≈ rgb(206,222,216) → **4,62:1** vid 13px — passerar
4,5:1, marginal 0,12. Samma token/storlek används redan av `.jp-hero__searchlabels`
(etablerat). Ingen åtgärd krävs; om rendered-testet upplever den svag är full
`--jp-hero-ink` på just varianten "Söktexten är full…" en sanktionerad förstärkning.

### Mi5. (Utanför E2h-diffen, noteras) Typeahead-listan stängs inte vid blur

Pre-existerande beteende i `job-ad-typeahead.tsx` (open nollas bara av Escape/val) —
fokus kan lämna fältet med öppen lista kvar. Hör inte till E2h-scope; lyfts som
observation för framtida touch, inte TD (§9.6-dom åt CTO vid nästa typeahead-touch).

---

## Bra gjort

- **Ingen placeholder** — hjälptexten bär Tab-/tagg-instruktionen, id-kopplad via
  `aria-describedby` (Klas hård regel följd korrekt, F4-mitigering 4).
- **Tab-avstegets mitigeringar är väl avvägda:** intercept ENDAST vid öppen lista +
  aktiv markering, aldrig Shift+Tab, ingen fokus-fälla. Med M4 fixad är avsteget
  ansvarsfullt implementerat. (Dom på granska-särskilt #1: ja, tillräckliga —
  givet B1+B2+M4.)
- **aria-live-annonser för både tillägg och borttagning** (inkl. Backspace-vägen) —
  rätt mekanism, bara fel böjning (M3).
- **Copy i övrigt klanderfri:** "Ta bort {label}", q-max-texten ger orsak + konkret
  åtgärd, rak svenska, inga utropstecken, ingen AI-klyscha.
- **SPOT-disciplin:** delade `chip-models`-helpers gör fält-× och toolbar-× till
  samma state-operation; toolbarens useState-kopior ersatta med useOptimistic.
- **No-JS/pre-hydration-fallback** bevarar native GET-submit med hela committade q —
  progressiv degradering på riktigt.
- **Q_MAX_LENGTH-FE-guarden** förhindrar backend-400 mitt i live-skrivflödet —
  flödesbegriplighet by design (Area 5).
- **Tokenizern strippar ledande `-`** — ingen oavsiktlig NOT-feature shippas i en
  Klas-pending produktfråga.
- Pill-radius + 28px på fält-chips är sanktionerat (pills/badges-undantaget);
  inga gradients, ingen glassmorphism, inga nya skuggor utanför popover-undantaget.

---

## Sammanfattning

**⛔ Blocked.** 2 Blockers (osynlig fokus-ring på ×-knappar i light mode; fokusförlust
till body vid chip-borttagning), 4 Major (tema-skiftande chip-tokens i tema-stabilt
fält; oannonserad q-max-varning; kongruensfel i annonserna; mouseover+Tab-fantomval).
Alla sex har konkreta, små fixar (~15 rader totalt). Minor 1–4 är FYI inför
rendered-iterationen. Delegera till nextjs-ui-engineer; re-review efter B1–B2 + M1–M4.

---

## Re-review (2026-06-11)

**Status:** ✓ Approved
**Granskare:** design-reviewer (Opus 4.7)
**Scope:** verifiering on-disk av samtliga sex veto-fynd (B1–B2, M1–M4) mot
working tree, branch `feat/sok-paritet-chip-in-field-e2h` vs main `1061bc2`.

### FAS-DEFERRAL-MANIFEST (kvitterat, oförändrat)

- Rendered-granskning fortsatt pending Klas lokala test/deploy — detta är
  kod/CSS-verifiering av åtgärderna, inte rendered-dom.
- Minus-operatorn (NOT) fortsatt out-of-scope (Klas-pending produktfråga).

### Verifiering per fynd

| Fynd | Åtgärd on-disk | Dom |
|---|---|---|
| **B1** Osynlig fokus-ring på chip-× | `globals.css:1066–1072` — `.jp-filterchip--field .jp-filterchip__rm:focus-visible { outline: 2px solid var(--jp-hero-sok-bg); outline-offset: 0; }` + kommentar som förklarar varför global vit ring inte räcker. `--jp-hero-sok-bg` är `#0C1A2E`, definierad en gång i `:root` (rad 104), ej omdefinierad i `[data-theme="dark"]` — tema-stabil. Offset 0 håller ringen innanför searchrowens `overflow:hidden`. Kontrast ring mot chip (`#F4F6FA`) och vitt fält: >13:1. | ✓ Åtgärdat |
| **B2** Fokusförlust vid ×-borttagning | `chip-search-field.tsx:43–51` — `inputRef = useRef<HTMLInputElement>(null)`, `removeChip()` anropar `onRemoveChip(chip)` följt av `inputRef.current?.focus()`; ×-knappen (rad 64) går via `removeChip`. `job-ad-typeahead.tsx:42` exponerar `inputRef?: React.Ref<HTMLInputElement>`-prop, wirad till `<input ref={inputRef}>` (rad 205). Fokus återförs till inputen — "ta bort och skriv vidare"-flödet, exakt den föreslagna lösningen. WCAG 2.4.3-kommentar på plats. | ✓ Åtgärdat |
| **M1** Tema-skiftande chip-tokens | `globals.css:1048–1065` — `.jp-filterchip--field` har nu literaler: bg `#F4F6FA`, border `#C9D2E0`, ink `#0C1A2E`; `__rm` `#455366` / hover `#0C1A2E`. Princip-kommentar ("chipsen får inte tema-skifta till mörka navy-chips i dark… samma princip som G1-blocket") dokumenterar banner-lokal-regeln. ×-ikon `#455366` mot `#F4F6FA` ≈ 7,2:1 (golv 3:1 UI-komponent), chip-text ≈ 14:1. Båda teman validerade via token-analys — inget i `[data-theme="dark"]` påverkar literalerna. | ✓ Åtgärdat |
| **M2** Tyst q-max-swap | `jobb-hero-search.tsx:252` — `<p id={helpId} role="status" className="jp-hero__searchhelp">` med swap mellan default-instruktion och q-max-varningen. `role="status"` = implicit `aria-live="polite"`; mount-innehåll annonseras inte, skiftet gör det. Kommentar refererar M2-domen (swap-in-place rätt mönster, bytet får inte vara tyst). Separat sr-only-region (rad 259) för chip-annonser är distinkt — ingen dubbel-annonsering av samma händelse. | ✓ Åtgärdat |
| **M3** Kongruensfel i annonser | `jobb-hero-search.tsx:161, 174, 188` — `` `Lade till ${l}` ``; rad 195 — `` `Tog bort ${chip.label}` ``. Backspace-vägen (`onRemoveLast`, rad 199–202) routar genom `onRemoveChip` → samma kongruensfria form. Repo-bred grep: inga kvarvarande `tillagd`/`borttagen` i E2h-ytan (övriga träffar är pre-existerande komponenter utanför scope + själva förklaringskommentaren). | ✓ Åtgärdat |
| **M4** Mouseover+Tab-fantomval | `job-ad-typeahead.tsx:260` — `onMouseLeave={() => setActive(-1)}` på `<ul role="listbox">`, med kommentar som refererar M4-domen. Markeringen följer nu alltid aktiv pekare eller aktivt tangentbord; parkerad pekarposition kan inte längre ge Tab-commit av osedd rad. | ✓ Åtgärdat |

### Kvalitetsnoteringar utöver kraven

- Varje fix bär en kommentar som citerar fyndet och varför — granskningstrailen
  överlever in i koden. Bra mönster, fortsätt så.
- B2-lösningen valde input-fokus (min primära rekommendation) framför
  APG-grannchip-alternativet — rätt val för "ta bort och skriv vidare"-flödet.
- Nya tester rapporterade för Tab-utan-markering/Shift+Tab/Backspace-edge +
  annons-assertions (818 vitest gröna, tsc/eslint rena per åtgärds-rapporten) —
  M3/M4-regressionerna är nu låsta i test, inte bara i kod.

### Kvarstående (oförändrat från första review)

- Minor Mi1–Mi4 är fortsatt FYI inför rendered-iterationen — ingen blockerar.
- Mi5 (typeahead-listan stängs inte vid blur) är pre-existerande, utanför
  E2h-scope; §9.6-dom åt CTO vid nästa typeahead-touch.
- Rendered-verifiering (light + dark, riktigt tangentbord + skärmläsare) sker
  vid Klas lokala test per FAS-DEFERRAL-MANIFEST — fokus-ringen (B1) och
  status-annonsen (M2) är de två punkter Klas bör titta/lyssna särskilt på.

### Slutdom

**✓ Approved.** Samtliga 2 Blockers + 4 Major åtgärdade exakt enligt
veto-rapportens krav, verifierade on-disk rad för rad. Vetot är hävt.
Mergeklar ur design-synpunkt.
