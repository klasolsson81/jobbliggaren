# Design-review: F6 P5 Punkt 6 — page-hero + brand-empty-states

**Status:** APPROVED (pre-screenshot, kräver Klas visual-verify post-deploy)
**Granskat:** 2026-05-24
**Auktoritet:** DESIGN.md §1 (civic-utility), §3 (tokens), §6 (komponenter),
§9 (a11y); HANDOVER-v4 §0-§6 (Klas-godkänd 2026-05-24)
**Scope:** token/spec-conformance, INTE rendered-veto (ingen FAS-DEFERRAL-
MANIFEST krävs; visual-verify ligger hos Klas)

---

## Blockers

Inga.

## Major

Inga.

## Minor

Inga.

## Granskning per krav

### 1. CSS verbatim mot HANDOVER §2.1

- **Block 1 `.jp-pagehero`** (globals.css L987-1050): 1:1 paritet mot
  HANDOVER §2.1 block 1 (rader 126-191). Verifierat selektor-för-selektor:
  bg, padding, flex, kicker (11.5px mono 0.10em uppercase), title (34px/700/
  -0.022em), lede (16px, 60ch), aside, chip (rgba whites), knapp-overrides.
- **Block 2 `.jp-empty--brand`** (globals.css L2299-2348): 1:1 paritet mot
  HANDOVER §2.1 block 2 (rader 196-241). Verbatim: padding 56px 32px,
  flex column, kicker, title 20px/700/-0.015em + text-wrap balance, body
  52ch, actions, primary/ghost overrides.
- **A11y-tillägg** (L1051-1056 + L2345-2348): `:focus-visible { outline: 2px
  solid #fff; outline-offset: 2px }` — utöver spec, motiverat av HANDOVER
  §4.2 (egen judgement-fotnot). Korrekt placerat per scope.

### 2. Token-discipline

Endast tillåtna tokens används: `--jp-hero-bg`, `--jp-hero-ink`,
`--jp-hero-ink-soft`, `--jp-r-md`, `--jp-font-mono`. Hex enbart `#fff`,
`#EAF1FA`, `#08213F` — alla från HANDOVER. `rgba(255,255,255,*)` enbart för
transparenta tokens på navy (chip + ghost border) per spec.

### 3. Civic-utility (DESIGN.md §1)

- Ingen gradient (verifierat: bara `var(--jp-hero-bg)` solid)
- Ingen box-shadow på .jp-pagehero / .jp-empty--brand
- Inga radius >6px: `--jp-r-md` (4px-class). Chip + knapp ärver.
- Ingen glow, ingen backdrop-blur
- Mono-skala för kicker/chip-label per §4

### 4. Anti-AI-aesthetics

Copy-kontroll verifierad (verbatim mot HANDOVER §2.3/§2.4):
- Ansökningar kicker "Pipeline" (L90), title "Inga ansökningar ännu" (L91),
  body verbatim (L92-95) — match
- CV kicker "CV-varianter" (L86), title "Inga CV ännu" (L87), body verbatim
  (L88-92) — match
- CTA-namn: "Skapa första ansökan" (L98), "Sök annonser först" (L101),
  "Skapa första CV" (cv L95) — match
- Inga emoji, inga utropstecken, ingen "Whoops!"-klyscha

### 5. A11y (DESIGN.md §9 WCAG 2.1 AA)

- H1-struktur: enda H1 per sida i `.jp-pagehero__title`
  - oversikt-page L270 — enda H1
  - ansokningar L74 — enda H1 (felfall behåller egen H1 separat per render-path)
  - cv L68 — enda H1 (felfall separat)
- Kontrast vit på `--jp-hero-bg` (#0A2647): 14.5:1 per HANDOVER §4.2 — AAA
- Soft-ink (#BBCCE5) på navy: 8.4:1 — AAA för body
- Fokus-ring: vit 2px outline + 2px offset mot navy = >3:1 (WCAG 2.4.7) — OK
- Lucide-ikoner i CTA har `aria-hidden="true"` (verifierat ansokningar L81,
  L98, L101; cv L76, L95)
- Inga aria-labels saknas — text-content bär semantiken

### 6. Out-of-scope-respekt (HANDOVER §5)

- `.jp-hero` (jobb): orörd — verifierat via grep, ingen edit i den sektionen
- `.jp-empty` (ljus variant): orörd — modifier `--brand` ärver utan att
  påverka basklassen
- `/sokningar`, `/sparade`, `/installningar`, `/admin/granskning`,
  detaljsidor: inga ändringar i `V3_NATIVE_ROUTES`-listan utöver `/cv`-
  tillägg (app-shell.tsx L68) — korrekt scope
- Felfall i ansokningar (L24-46) och cv (L32-49) behåller
  `.jp-page__title-block` resp. `jp-h1/jp-lede` — korrekt (felmeddelande
  ska inte få titelband)

### 7. App-shell-integration

`/cv` korrekt tillagd i `V3_NATIVE_ROUTES` (app-shell.tsx L68) — opt-ar
ut ur `.jp-shell-transitional-container` så `.jp-pagehero` går
edge-to-edge per HANDOVER §3.

---

## Pre-screenshot-noter (upplysning, blockerar INTE merge)

Följande kräver Klas visual-verify på dev post-deploy:

1. **TodayCard i `.jp-pagehero__aside`** (oversikt L276-280) — vit kort mot
   navy band, prototypen visar att TodayCards ~320px-bredd ryms i `flex
   0 0 auto` med `flex-wrap: wrap` (smala viewports wrapar under main).
   Verifiera att kortet inte slår tillbaka till >1200px kolumn-collapse.
2. **`.jp-empty--brand` höjd** — 56px 32px padding + flex column. På
   tomma `/ansokningar` och `/cv` — verifiera att panelen inte ser
   "för kort" ut mot full viewport-höjd (kan behöva min-height-justering
   om Klas signalerar).
3. **Dark mode** — `--jp-hero-bg` är fast navy oavsett tema per
   token-definition. Verifiera att lede/kicker (soft-ink) inte tappar
   kontrast om dark-mode-token över-skriver `--jp-hero-ink-soft`.

---

## Sammanfattning

Implementation matchar HANDOVER-v4 §0-§6 verbatim. CSS-blocken är 1:1 mot
spec. Token-discipline, civic-utility-regler, anti-AI-aesthetics, copy och
out-of-scope-respekt: alla golv klarade. A11y-tillägg (fokus-ring) är
välmotiverad och spec-driven.

**Verdict:** APPROVED för push. Visual-verify hos Klas post-deploy är
förväntat naturligt nästa steg (pre-screenshot-noter ovan är icke-blockande
upplysning, inte rendered-veto).
