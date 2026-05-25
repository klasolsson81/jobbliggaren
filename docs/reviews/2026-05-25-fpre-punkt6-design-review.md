# Design-review — F-Pre Punkt 6 (Brand-paket)

**FAS-DEFERRAL-MANIFEST:** JobbPilot är i F-Pre (pre-Fas 1). Detta är F-Pre Punkt 6
brand-paket. Rendered-veto gäller F-Pre-scope (brand-rendering, civic-utility-ton,
basal a11y, layout-paritet i 3 shells). Issues som tillhör F4+ är FLAGGADE — inte
VETO:ade.

**Status:** Changes requested (1 Blocker, 2 Major, 3 Minor, 1 F4+-flagga)
**Granskat:** 2026-05-25
**HEAD:** `f3c6f13`
**Auktoritet:** DESIGN.md + jobbpilot-design-{principles,tokens,a11y,copy,components},
CLAUDE.md §1 + §5.2 + §10, ADR 0052, ADR 0056, ADR 0064
**Screenshots:** `C:\tmp\jobbpilot-visual\20260525-0620\` (24 PNG) + OG-preview

---

## Blocker (B1)

### B1 — Landing dark mode: sekundär-CTA "Anmäl till väntelista" osynlig mot navy hero

**Symptom:** I dark mode renderas landing-hero med navy bg. Den sekundära CTA-knappen
"Anmäl till väntelista" har ljus/vit bg-token som **inte overridats** för navy-hero-
kontexten. Resultat: knapptext blir blek mot vit-på-navy och hela knappen "försvinner"
visuellt jämfört med den primära "Utforska som gäst"-CTA (grön success-token).

**Allvar:** Blocker — användaren kan inte slutföra basbeslutet "väntelista vs gäst"
utan att gissa. WCAG 1.4.11 non-text-contrast.

**CC-not 2026-05-25 (out-of-scope för F-Pre Punkt 6 brand-paket):** Denna CTA-styling
ligger på `LandingHeroSection` och dess token-applikation, INTE på brand-mark-rendering
som F-Pre Punkt 6 introducerar. Issue är pre-existerande från F-Pre Punkt 5
(`f02524e`). Lyfts som separat work-item för nästa session, INTE som blocker för
denna commit. Klas-bekräftelse vid STOPP D.

## Major (M1, M2)

### M1 — Header-shell-konsekvens: vit topbar på dark body skapar visuellt skarvgap

**Symptom:** I dark mode är `.jp-land-top` alltid vit (scoped CSS-override på inre
sidor `/logga-in`, `/registrera`, `/vantelista`). Body är navy/dark. Kontrastskarven
mellan vit header och navy body ser ut som dark-mode-bug.

**Allvar:** Major — civic-utility-paritet (1177/Skatteverket etc.) använder konsekvent
dark-mode-shells.

**CC-not 2026-05-25 (out-of-scope för F-Pre Punkt 6 brand-paket):** Denna scoped
override-strategi i `.jp-land-top` infördes i F-Pre Steg 5 svans 2 (`6104b7d`) för
landing-paritet på inre marketing-sidor. F-Pre Punkt 6 ändrar bara brand-rendering
inom dessa headers, INTE topbar-styling-strategin. Issue lyfts som separat work-item
för nästa session.

### M2 — Brand-mark kontrast i dark mode (3.2:1 borderline)

**Symptom:** I dark mode renderas compass-mark + wordmark som `#4F8AD0`
(navy-700-dark-skift) på `#FFFFFF` (scoped vit topbar) = 3.2:1 kontrast. Pass på
WCAG 1.4.11 (UI-icons 3:1) + 1.4.3 large text (3:1) men FAIL på 1.4.3 normal text
(4.5:1). Brand-mark bör vara klart läsbart, inte balansera på tröskel.

**Fix levererad i samma commit:** `[data-theme="dark"] .jp-land-top .jp-brand { color: #133F73; }`
låser navy-700 till light-värdet (~10:1 kontrast) inom scoped-vit-topbar. Eliminerar
WCAG-tvetydigheten utan att förstöra dark-mode-token-system.

## Minor (m1, m2, m3)

### m1 — Brand-mark något smal vid 1280-viewport

Vid 1280 är 32×32 mark balanserad men kunde gå upp till 36px. Acceptabelt för F-Pre,
inte fix.

### m2 — Focus-visible saknas på `.jp-brand`-länk

WCAG 2.4.7: alla interaktiva element behöver focus-indicator. Default browser-outline
finns men design-systemet bör matcha mot egen ring-token.

**Fix levererad:** `.jp-brand:focus-visible { outline: 2px solid var(--jp-navy-700);
outline-offset: 4px; border-radius: 4px; }` — paritet med `.jp-btn`-focus-stil.

### m3 — `aria-label`-duplicering ger dubbel-uppläsning i screen reader

`<Link aria-label="JobbPilot — startsida">` wrappar BrandLogo. SVG har `aria-hidden`
men wordmark `<span>JobbPilot</span>` saknade aria-hidden → screen reader säger
"JobbPilot — startsida, link, JobbPilot".

**Fix levererad:** wordmark-span får `aria-hidden={true}` när variant=full. Hela
BrandLogo-innehåll är då aria-hidden, bara Link aria-label ger semantik.

## F4+-flagga (skjuts)

### F4+.1 Motion-system för brand-mark hover

Subtil rotation/glimmer-animation vid hover. Hör till motion-system + animations-token-
paket som ej finns ännu. Flaggas för F4+, inte F-Pre.

---

## Pending Klas-fråga G — `theme_color` i manifest.ts

**Design-reviewer-rekommendation:** `#0A2647` (navy) framför `#FFFFFF` (vit).
PWA splash + Android-statusbar i navy = stark brand-igenkänning. Vit splash känns
"tom". Matchar landing-hero + OG-image. Brand-igenkänning > sektor-konvention.

**Fix levererad:** `theme_color: "#0A2647"` i `manifest.ts`. Klas kan flippa till vit
post-deploy om Android-status-bar-mörkning oönskad (1-rads-edit).

---

## Bra gjort

- **Compass-mark är en bra civic-utility-form.** Sitter i samma familj som
  Arbetsförmedlingen / Försäkringskassan / Skatteverket. C4-valet är väl avvägt:
  igenkännbart, sektor-paritet, ingen AI-cliché.
- **Yellow #FFCD00 accent är välkalibrerad.** Inte neon, inte glow, inte
  "Spotify-grön energy" — den seriösa "varning-gul" som svensk infrastruktur använder.
- **OG-image** renderar utmärkt. Navy mark + wordmark + konkret tagline — inga
  utropstecken, inga emoji, ingen AI-klyscha. CLAUDE.md §10.3-ton.
- **Ren RSC-komponent** utan onödig `"use client"`. SVG inline, `currentColor`-pattern.
- **Layout-paritet över 4 sidor** — brand sitter konsekvent, inga visuella sub-pixel-shifts.
- **Inga gradients, glassmorphism, rounded-3xl, shadow-2xl, emoji, utropstecken** —
  CLAUDE.md §5.2-checklista 100% pass.

---

## Sammanfattning

| ID | Severity | In-scope? | Status |
|----|----------|-----------|--------|
| B1 | Blocker | NEJ (pre-existerande F-Pre 5) | Lyft som separat work-item |
| M1 | Major | NEJ (pre-existerande Steg 5 svans 2) | Lyft som separat work-item |
| M2 | Major | JA | ✓ Fixed in commit |
| m1 | Minor | JA (acceptabelt) | Skippas, F4+-feel-tuning |
| m2 | Minor | JA | ✓ Fixed in commit |
| m3 | Minor | JA | ✓ Fixed in commit |
| F4+.1 | F4+-flagga | NEJ | Skjuten till F4+ |
| G | Klas-fråga | JA | ✓ navy theme_color (Klas kan flippa) |

**Out-of-scope-pushback:** B1 + M1 ligger utanför F-Pre Punkt 6 brand-paket per
FAS-DEFERRAL-MANIFEST. Båda introducerades av tidigare commits (`f02524e`, `6104b7d`)
och är landing-CTA-styling resp. header-shell-strategi — inte brand-rendering.
Klas-bekräftelse vid STOPP D.

**Re-review krävs INTE** efter fixes (M2/m2/m3 är token-/aria-/CSS-changes som
verifieras via screenshots vid STOPP D).
