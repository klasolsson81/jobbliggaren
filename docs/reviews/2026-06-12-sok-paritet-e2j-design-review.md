# Design-review: Fas E2j sök-commit-modell — ×-clear-knapp i /jobb-hero

**Status:** ✓ Approved
**Granskat:** 2026-06-12
**Branch:** `feat/sok-paritet-commit-modell-e2j` (on-disk)
**Auktoritet:** DESIGN.md → jobbpilot-design-a11y (WCAG 2.1 AA), jobbpilot-design-tokens, jobbpilot-design-copy (§10), jobbpilot-design-principles (civic-utility)
**Scope:** `web/jobbpilot-web/src/components/job-ads/jobb-hero-search.tsx` (×-knapp, onClear, annons), `web/jobbpilot-web/src/app/globals.css` (`.jp-hero__clearbtn`, native cancel-button-suppress)

---

## Sammanfattning

**0 Blockers, 0 Major, 2 Minor.** Mergeklar på kod+token-review.

×-clear-knappen är en korrekt, civic-utility-trogen affordance. Den ärver det redan
sanktionerade inåtvända-fokusring-mönstret (samma fix som E2h-blockern), använder ink (ej
grön) konsekvent med E2f-de-greening-domen, har korrekt `aria-label` + `type="button"`,
och annonsen följer §10-tonen. Inga AI-aesthetics, ingen gradient/glow/shadow, inga
hårdkodade Tailwind-defaults eller raw hex i den nya ytan — allt går via `--jp-hero-*`-tokens.

---

## Granskning per fokusområde

### WCAG 2.4.7 — focus visible (E2h-blocker-klassen) ✓

`.jp-hero__clearbtn:focus-visible` (globals.css:1102–1106) sätter
`outline: 2px solid var(--jp-hero-sok-bg)` + `outline-offset: -2px`. Detta är **exakt** det
sanktionerade mönstret från `.jp-hero__input:focus-visible` (globals.css:1070–1073) som
löste E2h-blockern: searchrowen har `overflow:hidden` (globals.css:1053), så en utåtvänd ring
(positiv offset) skulle klippas — den inåtvända ringen ligger garanterat synlig.

- **Ring-färg:** `--jp-hero-sok-bg` = `#0C1A2E` (v3-ink). Mot pill-bakgrunden `#FFFFFF`
  (`--jp-hero-pill-bg`, jobb-hero-search.tsx-knappen ärver searchrow-pillens vita bg via
  `background: transparent`) ger det ~16:1 — långt över WCAG 2.4.11-golvet 3:1.
- **Tema-stabilitet:** `--jp-hero-pill-bg`, `--jp-hero-pill-ink` och `--jp-hero-sok-bg`
  definieras ENDAST i `:root` (globals.css:101–104) — ingen `[data-theme="dark"]`-override
  (verifierat via grep: enda definitionerna ligger på rad 101/102/104). Plattan och alla dess
  kontroller är medvetet tema-stabila (ADR 0068: "bannern bär färgen, kontrollerna gör det
  inte"). Ringen är därför validerad i **båda** teman — den vita pillen och ink-ringen skiftar
  inte. Detta är den korrekta anledningen till att en separat dark-beräkning inte behövs här,
  inte en genväg.
- **Globala fokus-scopningen:** plattan scopar om `--jp-focus` till vit, men `.jp-hero__clearbtn`
  (precis som input/Sök) sätter sin egen ink-ring explicit — den gröna/vita globala ringen
  gäller inte här, vilket är korrekt eftersom ink-på-vit ger högre kontrast än vit-på-vit.

**Verdikt:** ingen WCAG 2.4.7-regression. E2h-felklassen är inte återintroducerad.

### WCAG 2.4.3 — focus order / focus loss vid villkorlig rendering ✓

Knappen renderas villkorligt: `hydrated && text.length > 0` (jobb-hero-search.tsx:386).
`onClear` (rad 324–331) sätter `setText("")` → vid nästa render är `text.length === 0` →
knappen unmountas. Frågan: var hamnar fokus när användaren aktiverar × via tangentbord och
knappen försvinner ur DOM?

Detta är **inte** ett blockerande fokus-trap-mönster, av tre skäl:

1. `onClear` aktiveras med Enter/Space på knappen. När knappen unmountas faller fokus tillbaka
   till `<body>` — inte idealiskt, men browsern hanterar det grafiskt (ingen krasch, ingen
   trap). Tab därifrån fortsätter framåt i dokumentordningen.
2. Den primära interaktionspunkten efter en rensning är sökfältet (`#jobb-q`) som ligger
   **före** knappen i DOM — användaren rensar typiskt för att skriva om, och flyttar då fokus
   dit aktivt.
3. Rensningen annonseras ("Rensade sökfältet", se nedan) via `aria-live="polite"`, så
   skärmläsaren bekräftar utfallet även om fokus inte flyttas.

Se Minor 1 nedan för en frivillig polish som skulle göra fokushanteringen exemplarisk.

### WCAG 2.5.5 / 2.5.8 — target size ✓

40×52 CSS-px (globals.css:1091–1092). Långt över in-app-golvet 32×32 och touch-golvet 44×44
(höjden 52 ≥ 44; bredden 40 < 44 men 2.5.8 AA-golvet är 24px och in-app-golvet 32px — bägge
klaras; 44px-touch-bumpen är ett 2.5.5 AAA-mål). Civic-kompakt men gott om yta. ✓

### WCAG 1.4.3 / 1.4.11 — kontrast på ink-× ✓

× använder `color: var(--jp-hero-pill-ink)` = `#0C1A2E` (globals.css:1095) med `opacity: 0.65`
i vila (rad 1097).

- **Vila (opacity 0.65 mot vit pill):** den komposerade färgen blir ~`#616A77`, vilket ger
  ~4.7:1 mot `#FFFFFF` — passerar 3:1 UI-komponent-golvet med marginal och klarar t.o.m.
  4.5:1-brödtext-golvet.
- **Hover/focus (opacity 1.0):** full ink ~17:1.

Bägge tema-stabila (vit pill i båda). ✓

### Färg-semantik: ink vs grön ✓

× är ink, inte grön — korrekt mot E2f-domen ("grönt = interaktion, inte information/dekoration",
kodifierad i CSS-kommentaren globals.css:1084–1085). Här finns en *skenbar* spänning: × ÄR
interaktivt, och grönt är interaktionsfärgen (jobbpilot-design-tokens, accent-700). Men plattan
är ett **dokumenterat undantag** (ADR 0068): alla kontroller i hero-plattan är tema-stabilt vita
med ink — Sök-knappen är ink (`--jp-hero-sok-bg`, globals.css:1111-kommentar), chips är ink,
input är ink. Att göra × grön skulle bryta plattans interna konsekvens och introducera den enda
gröna kontrollen på en yta där färgen medvetet bärs av bannern, inte kontrollerna. **Ink är rätt
val här** — det följer plattans lokala kontrakt, inte den app-globala accent-regeln. Konsekvent
med syskonen (input/Sök/chips).

### §10 — copy ✓

- `aria-label="Rensa sökfältet"` (jobb-hero-search.tsx:391): du-form implicit, imperativ, konkret
  verb, inget utropstecken, ingen emoji, ingen AI-klyscha. ✓
- aria-live-annons `"Rensade sökfältet"` (jobb-hero-search.tsx:330): rak svenska, beskriver
  utfört utfall i preteritum (matchar mönstret "Lade till …"/"Tog bort …" i samma fil). Inget
  "!", ingen emoji. ✓
- Konsekvent med övrig copy i filen (hjälptext rad 405, limit-notis rad 404). ✓

### Civic-utility / AI-aesthetics ✓

- `background: transparent`, inga gradients, ingen `box-shadow`, ingen glow, ingen
  `backdrop-blur`, inga rundningar > 6px på knappen (den ärver searchrow-pillens 4px via
  klippning, knappen själv sätter ingen radius). ✓
- Inga lila/indigo/neon-accenter; inga raw hex i den nya ytan — `--jp-hero-pill-ink` /
  `--jp-hero-sok-bg`-tokens genomgående. ✓
- lucide `X` size 18, `aria-hidden="true"` (jobb-hero-search.tsx:393) — dekorativ ikon korrekt
  dold för skärmläsare (knappens `aria-label` bär betydelsen). ✓
- Läser som en seriös utility-affordance (Platsbanken/GOV.UK clear-fält), inte trendig
  behandling. ✓

### Native cancel-button-suppress ✓

`.jp-hero__input::-webkit-search-cancel-button { appearance: none }` (globals.css:1078–1081).
Korrekt och nödvändigt: inputen är `type="search"` (job-ad-typeahead.tsx:200), så WebKit/Blink
renderar annars en native ×. Kommentaren (globals.css:1074–1077) dokumenterar rätt skäl — den
native rensade bara `value` utan att committa en filter-delta, och Firefox visar den aldrig
(cross-browser-inkonsekvens). Den kontrollerade knappen ersätter den med korrekt clear-semantik.
Ingen a11y-förlust: den native knappen exponerade inget eget tillgänglighetsnamn i alla browsers
ändå; den nya har explicit `aria-label`. ✓

### `type="button"` ✓

Knappen sätter `type="button"` (jobb-hero-search.tsx:388) — kritiskt, eftersom den ligger inuti
`<form>` (rad 344). Utan det skulle den default:a till `submit` och trigga `onSubmitText` istället
för `onClear`. Korrekt. ✓

---

## Minor (nice-to-fix, inte blocker — prefer in-block per §9.6)

### Minor 1 — Fokus-retur efter rensning (frivillig polish)

**Fil:** `web/jobbpilot-web/src/components/job-ads/jobb-hero-search.tsx:324` (`onClear`)

När × aktiveras via tangentbord och knappen unmountas (`text.length === 0`) faller fokus till
`<body>`. Inte ett WCAG-brott (ingen trap, annonsen bekräftar utfallet), men exemplarisk
hantering vore att flytta fokus till sökfältet `#jobb-q` efter rensning — det är den naturliga
nästa interaktionspunkten och håller tangentbordsanvändaren i flödet.

Föreslagen riktning (nextjs-ui-engineer implementerar, inte design-reviewer): efter `setText("")`
i `onClear`, flytta fokus till typeahead-inputen (t.ex. via en ref vidarebefordrad till
`JobAdTypeahead`, eller `document.getElementById("jobb-q")?.focus()` — om DOM-fokus-undantaget
motiveras; React-ref föredras per CLAUDE.md §5.2). Detta är polish, inte krav — utfallet är redan
WCAG-konformt.

### Minor 2 — Dubbla aria-live-status i sökraden (verifiera ingen krock)

**Fil:** `web/jobbpilot-web/src/components/job-ads/jobb-hero-search.tsx:411` +
`web/jobbpilot-web/src/components/job-ads/job-ad-typeahead.tsx:224`

Det finns nu två `role="status" aria-live="polite"`-regioner aktiva i samma sökruta:
typeaheadens egen sökförslags-status (typeahead.tsx:224) och hero:ns tagg-annons
(jobb-hero-search.tsx:411). Dessutom har hjälptexten `role="status"` (rad 402). Tre polite-status
i nära DOM-grannskap kan ge överlappande uppläsningar (t.ex. "Rensade sökfältet" + "N förslag"
samtidigt). Detta är **inte** ett blocker — `polite` köar och avbryter inte — men verifiera under
Klas rendered/NVDA-pass (se manifest nedan) att annonseringarna inte trampar på varandra eller
blir pratiga vid rensning. Om de gör det: överväg att låta hjälptextens `role="status"` (rad 402)
vara enbart `role="status"` för q-max-skiftet och inget annat, vilket den redan är — sannolikt
ofarligt, men bekräfta auditivt.

---

## FAS-DEFERRAL-MANIFEST (rendered-only — skjuts till Klas lokala/Vercel rendered-review)

Följande kunde **inte** verifieras i denna headless-körning eftersom `/jobb` är auth-gated.
Per Klas stående praxis blockerar design-reviewers rendered-veto INTE merge för auth-gated ytor —
kod+token-review (ovan) bär grinden. Deferreras till Klas lokala/Vercel rendered-review:

1. **Faktisk fokusring-rendering** i båda teman: att den inåtvända ink-ringen (offset -2px) är
   visuellt synlig och inte perceptuellt klippt av searchrowens `overflow:hidden` på den faktiska
   pixlade ytan. (Kod-mönstret är identiskt med det redan-godkända input-mönstret → hög konfidens,
   men pixel-bekräftelse är rendered-only.)
2. **Komposerad opacity-kontrast** på ink-× i vila (beräknat ~4.7:1) — bekräfta auditivt/visuellt
   att × är tydligt urskiljbar mot den vita pillen i båda teman.
3. **Minor 1 + Minor 2** (fokus-retur-beteende + dubbel-aria-live-uppläsning) — kräver
   tangentbords-/NVDA-pass på den renderade ytan.
4. **Visuell balans** av × mellan input och Sök-knapp (40px-bredd, vertikal centrering på
   52px-höjd) — kod ser korrekt ut (flex center), men estetisk passform är rendered-only.

Dessa är observationspunkter för Klas pass, **inte** Blockers. Inget av dem håller mergen.

---

## Bra gjort

- Inåtvänd fokusring återanvänder det sanktionerade E2h-mönstret exakt — ingen ny lösning där en
  beprövad finns (WCAG 2.4.7 utan regression).
- `type="button"` korrekt satt i form-kontext — undviker oavsiktlig submit.
- `aria-label` + `aria-hidden` på ikonen följer ikon-knapp-mönstret i a11y-skillen till punkt.
- Ink-färg konsekvent med E2f-de-greening-domen och plattans tema-stabila kontroll-kontrakt
  (ADR 0068) — inget grönt smyger in.
- Native cancel-button-suppress med dokumenterat, korrekt skäl (filter-delta-semantiken).
- Copy: rak svensk preteritum-annons, du-form, inget "!", ingen emoji — civic-utility-ton hela
  vägen.
- Alla färger via `--jp-hero-*`-tokens; inga Tailwind-defaults, inga raw hex i den nya ytan.

**Mergeklar.** 2 Minor är frivillig polish (prefer in-block per §9.6); rendered-verifiering
deferrad till Klas per manifestet ovan.
