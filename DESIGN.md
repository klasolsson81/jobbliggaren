# DESIGN.md — Jobbliggaren design system

> Pedagogisk inramning för Jobbliggarens civic-utility-design.
> Fullständiga specer finns i Claude Code-skills under `.claude/skills/jobbpilot-design-*`.
>
> **Huvudspec:** [`BUILD.md`](./BUILD.md)
> **Coding conventions:** [`CLAUDE.md`](./CLAUDE.md)

---

## 1. Design-filosofi

### 1.1 Grundprincip

Jobbliggaren är ett verktyg för stressade jobbsökare. UI:t ska signalera **tillit och pålitlighet**, inte imponera eller underhålla. Målet är att en 55-årig processoperatör i Alingsås som söker sitt nästa jobb ska känna att appen är byggd för att fungera, inte för att sälja.

Referenser som ska kännas i allt vi bygger:
- **GOV.UK Design System** — typografisk hierarki, content-first, minimal dekoration
- **Digg / Sveriges designsystem** — svensk myndighetsprecedent
- **1177 Vårdguiden** — trygg, läsbar, accessible
- **Stripe Dashboard** — datatäthet utan kaos
- **Mercury Bank** — utility över branding

Referenser som **inte** ska kännas:
- Vercel / Linear / Arc — för trendigt, för mycket "vibe"
- Notion — för lekfullt
- Default shadcn/ui ur-lådan — standard-AI-look

### 1.2 Do / don't (snabbkort)

| ✅ Ja | ❌ Nej |
|-------|--------|
| Ljus default + dark mode stöds (auto via `prefers-color-scheme` + manuell toggle) | Forcerad dark utan användarval |
| Mörkgrön accent (`--jp-accent`, ADR 0068) | Neon, lila, cyan-accenter |
| Rak svensk copy | Emojis, utropstecken, "Let's go!" |
| Tabeller och listor | Kort-layouter överallt |
| `border-radius: 4px` | 16px+ rundade hörn |
| Muted statusfärger | Glow, drop shadow, glasmorfism |
| Breadcrumbs + hierarki | Flata sidor utan kontext |
| Systemfont/Source Sans 3 | Display-fonter, scripts |
| Content-first sidor (hero-bannern är en saklig sök-/orienterings-yta, ADR 0068) | Marketing-heros, vibey microcopy |
| Kvantifierad info | Vag "positiv" feedback |

---

## 2. Var finns vad?

Filosofi-sammanfattningar finns i denna fil. Fullständiga specer med tokens, varianter, kod och checklistor finns i skills:

| Område | Skill | Reference-filer |
|--------|-------|-----------------|
| Filosofi + do/don't + beslutramverk | `.claude/skills/jobbpilot-design-principles/SKILL.md` | — |
| Färg, typografi, spacing, radius, tokens | `.claude/skills/jobbpilot-design-tokens/SKILL.md` | tokens-full, contrast-table, dark-mode, theme-block |
| Komponenter (Button, Card, Table, Dialog…) | `.claude/skills/jobbpilot-design-components/SKILL.md` | variants-full, composition-examples |
| Svensk copy, microcopy, felkoder, locale | `.claude/skills/jobbpilot-design-copy/SKILL.md` | error-messages, microcopy-library, locale-formatting |
| Tillgänglighet (WCAG 2.1 AA) | `.claude/skills/jobbpilot-design-a11y/SKILL.md` | wcag-criteria, screen-reader-testing, testing-tools |

**Drift-skydd:** Sammanfattningarna §3–§9 är kurerade från skills-innehåll. När en skill uppdateras verifierar `docs-keeper` att relevant sammanfattning i denna fil fortfarande är i synk. Detta görs automatiskt vid session-end.

---

## 3. Färgsystem (sammanfattning)

Paletten är medvetet begränsad. Civic-produkter bygger tillit genom konsekvens — fler färger skapar kognitiv belastning. Kanon = `globals.css` (v3-neutraler + grön accent per ADR 0068); fullständiga värden i `jobbpilot-design-tokens`-skillen.

- **Accent (mörkgrön, ADR 0068):** `--jp-accent-800` `#15603F` fill (primärknapp, EJ dark-skiftad, vit text — aldrig ljus knapp/mörk text); `--jp-accent-700` `#15603F` light / `#6EE7A8` dark för länkar, aktiv nav, fokus (`#6EE7A8` ALDRIG som fill); `--jp-accent-50` selektion. Ersätter tidigare blå/navy. Logo-marken (Sigillet, ADR 0070) bär grön skiva + guldsignatur `--jp-gold` `#E8C77B` — egen färgsättning utanför interaktions-accenten.
- **Hero-gradient (scoped undantag, ADR 0068):** `--jp-hero-gradient` (118° `#0B2A1E`→`#14503A`→`#1E6B4C`) ENBART på hero-banner-plattan/pagehero/empty-brand/landing-hero — gradients förbjudna överallt annars.
- **Neutraler (v3, ADR 0052; ink-3 mörkad i #296):** ink `#0C1A2E` / `#455366` / `#4F5D72` (text-tertiary mörkad från `#7C8AA0` till en hög-kontrast slate-navy så all 11–12.5px metadata-text klarar WCAG AA — min 5.45:1, issue #296; ink-1/-2 oförändrade av G1), surfaces `#FFFFFF` / `#F4F6FA` / `#E8EDF4`, canvas `#F4F6FA` light / `#0B1525` dark (mörk navy-grå, inte svart), placeholder `#626B78` (WCAG-motiverad).
- **Statusfärger:** success `#16793B`, warning `#A34A06` (mörkad från `#B4540B` för 4.5:1 pill-text, issue #193), danger `#BE1B1B`, info `#1B5396` + bg-varianter — endast för status (aldrig dekoration); oförändrade av accentbytet.
- **Borders:** border `#C9D2E0` (dekorativa hairlines), border-soft `#E3E8F0`, border-strong `#7C8AA0` (informationsbärande dividers, mörkad från `#97A4B8` till 3.5:1-UI-golvet, issue #193 — delar nu värde med border-input, medveten tonalitet), border-input `#7C8AA0`; border-modal/-structural per ADR 0041 (re-homade på v3-border).
- **Skuggor:** bara shadow-card/pop/modal (popovers/dropdowns/modal) — djup skapas via border/hairline, aldrig på cards/knappar
- WCAG AA-kontrast obligatoriskt på alla färgpar. accent-700 på vit = 7.56:1; `#6EE7A8` på dark canvas = 11.9:1. Full tabell i tokens-skillens `contrast-table.md`.

### Dark-mode-stance

Dark mode **stöds** (designsystem v2, Klas-GO 2026-05-16 + ADR — ersätter Fas 0-borttagningen som skedde pga shadcn-presetens oklch indigo-violetter). v2 använder en **civic slate-skala utan dekorativ hue** (`data-theme="dark"` på `<html>`). Light är default; `prefers-color-scheme: dark` honoreras **automatiskt och utan flash** (inline pre-paint-script), manuell toggle överrider och persisteras i localStorage. Sunken-ytor är mörkare än canvas i båda lägen (samma papper-metafor). Light och dark valideras parallellt — aldrig dark som efterhandstillägg.

Exakta tokens och hex-värden (light+dark), kontrast-tabell, density-system och deploy-ready `@theme`/`--jp-*`-block → **jobbpilot-design-tokens**.

---

## 4. Typografi (sammanfattning)

- **Primär:** Source Sans 3 (`next/font/google`, variabel `--font-sans`) — weight 400–800 laddade, hela familjen 200–900 (LP-1 #254; 800 = display-klassen). Ersätter Hanken Grotesk — högre x-höjd/versalkvot (0,736 vs 0,707) ger tydligare text vid samma px samt civic-pedigree (USWDS-default, CSN) (#549 WS4, ADR 0091). Vikt-stegen är tokeniserad (`--jp-fw-*`, #549 WS2) — aldrig numeriska font-weight-litteraler i CSS/TSX.
- **Monospace:** JetBrains Mono (`next/font/google`, variabel `--font-mono`) — endast för bokstavs-/kod-identifierare och versala caps-labels (mono-kickers, kolumnrubriker, `SV`/`EN`, versioner, opaka stöd-koder) där rollen är *etikett/kod*, inte *läs talet*. Aldrig brödtext/rubriker/knapptext. **Aldrig för informationsbärande siffror** (#376 / ADR 0038-amendment).
- **Informationsbärande siffror** (antal, belopp, datum, tider, räknare, stats, ID-/SSYK-siffror användaren läser): Source Sans 3 (sans, `--font-sans`) med `font-variant-numeric: tabular-nums` — entydig läsbarhet vid synnedsättning (`0`≠`8`) med bibehållen kolumn-justering inom samma vikt (sifferbredd växer mellan vikter — verifierat riskfritt i appen idag, ADR 0091). Låg-syn-golvet (§1.1-målanvändaren, ADR 0038-linjen) väger tyngre än mono-kod-estetiken; `tabular-nums` är progressive enhancement (faller graciöst till proportionella figurer).
- **App-UI-roller (ADR 0038 — GOV.UK-läsbarhetsgolv; #549 WS1 / ADR 0068-notat 2026-07-03):** body **16px/400** (golv — aldrig informationsbärande text < 16px), body-sm/small **14px** (min), lede **17px/400**, h3/h4 **18/16px/700/ink-1**, h2 **20px/700/`--jp-heading-2`** (navy-800), h1 **32px/700/`--jp-heading-1`** (navy-700 — enad sidtitel-tier, re-align mot ADR 0052 Beslut 5). Rubriker = navy (information), grönt = interaktion (E2f-kärnan består). Dark: rubriker ink-1.
- **Display (banner-plattan, ADR 0068 G2):** 44px / 800 / line-height 1.1 / letter-spacing −0.025em (32px mobil) — följer F4-platta-komponenten var den används (/jobb-hero + pagehero på alla inre sidor). Landing-plattan: 56px-clamp / 700. Innehållsbredd-kanon app-wide = 1136px (header = platta = innehåll).
- **Inline-data (datum, ID-siffror, räknare):** sans 13px/500 + `tabular-nums`, färg `text-secondary`/`text-primary` (aldrig `text-tertiary`) — siffror läsbara, kolumner justerade (#376; mono-bärande caps-labels förblir mono)
- **Mono caps (labels):** 11.5px / 500 / letter-spacing 0.08–0.16em / UPPERCASE, färg `text-secondary` — kickers, kolumnhuvuden (`UPPDATERAD · MAJ 2026`)
- **Läsbarhetsdoktrin (#549 WS1, CTO D4):** innehålls-/brödtext är **`text-primary` (ink-1) som default** — ljusa tiers är reserverade för äkta metadata. `text-secondary` (ink-2, 7.8:1) = timestamps, `<dt>`-etiketter, eyebrows, kvitton; `text-tertiary` (ink-3, 6.69:1 sedan #296-mörkningen — AA-säker, inte "dekorativ") = demoterad metadata (tider, ids); placeholder = `--jp-placeholder`. Aldrig grå-utils (`text-slate-*` m.fl.); shadcn `--muted-foreground` är remappad till ink-1.
- Civic-ledger-*formen* (flata tabeller, hairlines, mono-etiketter, inga cards) är oförändrad — ADR 0038 omkalibrerar skala/färg/fältstorlek; #376-amendmentet utsträcker samma läsbarhetslinje till siffer-glyfform (informationsbärande tal i sans, koder/labels i mono) (handoffen drev under §1.1-målanvändarens läsbarhetsbehov).
- Global text-tracking −0.005em (optisk täthet). Aldrig all caps i sans. Aldrig letter-spacing-justeringar i brödtext.
- Italic bara i citat och referenser, aldrig för emfas i body.

Komplett skala, line-heights och Tailwind-mappning → **jobbpilot-design-tokens**.

---

## 5. Spacing och layout (sammanfattning)

- **4px-baserad skala.** Vanliga värden: 8, 12, 16, 24, 28, 48, 64.
- **Border-radius:** sm 2px (inputs/badges), md 4px (default — knappar, panels, sökruta), lg 6px (större paneler/dropdowns), pill 9999px (endast statusprickar/pills). Inga andra radier — inga 8/10/12px.
- **Density-system:** `[data-density]` på `<html>` — `compact` 0.85 / `standard` 1.0 (default) / `luftig` 1.18. Multiplicerar `--jp-row-h` (36px), `--jp-section-y` (28px), `--jp-pad-x` (28px). Hårdkoda aldrig padding där density gäller.
- **App shell (Variant B):** vänster sidebar 240px med `border-right` hairline, topbar 56px, innehåll max-width 1080px.
- **Formulär:** max-width 640px, labels alltid ovanför inputs.
- Desktop-first — touch (≤768px) bumpar hit-targets till 44px, ledger-tabeller stackas (utvecklaransvar).

---

## 6. Komponenter (sammanfattning)

shadcn/ui är primitive-lagret. Komponenter kopieras in i `components/ui/` — de ägs av projektet, importeras inte från npm.

Använda i v1: Button, Card, Input, Textarea, Select, Badge, Dialog, Toast, Table, Breadcrumb, Form, Skeleton, Alert.

Aldrig byt ut mot: Material UI, Chakra, Mantine, Headless UI.

**Civic-utility patterns (v2, `.jp-*`-systemet i globals.css):**
- `.jp-table--flat` — ledger-tabell. Ingen zebra-stripe, inga inramade celler, hairlines mellan rader, fetare topp/botten-linje
- `.jp-attention` — rad-baserad feed (Översikt). Prick + text (max 68ch) + dismiss, hairlines, ingen låda
- `.jp-pipeline` — kanban som ledger-rader, kolumner åtskilda av `border-strong`, INGA floating cards
- `.jp-statusDot` (förstaval i tabeller — prick + text, ingen bg) vs `.jp-pill` (accent vid entitet — färgad 50-bg + prick + text)
- `.jp-match` — progress-bar 6px: brand ≥75, info 50–74, warning <50
- `.jp-filterBar` — flat rad mellan två hairlines, fält i naturlig bredd, ingen chrome-box
- `.jp-banner` — info-banner med 3px brand-vänsterkant, används sparsamt
- Knappar: höjd **40px (sm 36px)**, radius 4px, transition 80ms, max EN `--primary` per skärm (ADR 0038)
- Inputs/Select: höjd **44px (sm 40px)**, label alltid ovanför, hint under. **Inga beskrivande placeholder-exempel i sök/filter-fält** (Nielsen/WCAG-anti-pattern). Format-placeholder i auth-formulär OK (`namn@exempel.se` = syntax, ej exempelinnehåll)
- **Kontroll-storlek (filterrader)** — `--jp-control-h` (40px) + `--jp-control-fs` (14px) + `--jp-control-px` (14px) i `globals.css` är SSOT för filterradernas kontroll-storlek (hero-pills + sort-select på /jobb). Ändra storlek/textstorlek HÄR, aldrig inline per komponent — så hela filterraden förblir EN form med EN textstorlek (Klas 2026-06-30). Textstorlek ≥ 14px-golvet (§4 — kontrolltext aldrig under body-sm); mobil bumpar hit-targets till 44px (WCAG 2.5.5).

Regler:
- En primary button per form — aldrig två primärknappar sida vid sida
- Destructive actions kräver alltid bekräftelse-dialog
- Icon-only buttons kräver `aria-label`
- Loading state: ersätt label med "Sparar…", behåll bredd, sätt `disabled`
- Inga stats-kort runt enstaka värden — visa siffran direkt i rad/tabell ovanför listan

Full spec, variant-states och JSX-kompositionsexempel → **jobbpilot-design-components**.

---

## 7. Ikoner

- Bibliotek: **Lucide React** — stroke/outline only, inga filled variants
- Default: `size-4` (16px) inline med text, `size-5` (20px) fristående
- Färg ärvs via `currentColor` — aldrig hårdkodad ikonfärg
- Inga emojis i UI-text, oavsett kontext

---

## 8. Copy-riktlinjer (sammanfattning)

- **Du-tilltal** alltid — "du" inte "Du" eller "ni"
- **Direkt:** 10 ord där möjligt, inte 25
- **Konkret:** siffror, datum, namn — "Intervjun är 14 apr kl 10:00" slår "Du har en kommande intervju"
- **Opretentiös:** inga ordspråk, inga liknelser, ingen peppning
- Inga utropstecken i info/success. OK i error om det förstärker brådska — sparsamt.
- Inga emojis, inga engelska fraser i svensk copy
- Svenska locale-format: "14 apr 2026", "14:32", "33 456 kr"
- AI-samtycken alltid explicita om vad som skickas vart (GDPR Art. 7)

Microcopy-library, felkoder (40+ med svenska translations) och locale-formatting-funktioner → **jobbpilot-design-copy**.

---

## 9. Tillgänglighet (sammanfattning)

WCAG 2.1 AA är golvet, inte målet. Ingen dispens för MVP eller tidspress.

- Lighthouse a11y-score **≥ 95** innan merge
- axe DevTools: **0 violations** per ny sida/komponent
- Synlig fokusring obligatorisk — aldrig `outline: none` utan ersättning
- Tangentbordsnavigation måste fungera för alla interaktiva element
- Formulär: `<label>` alltid kopplad, `aria-invalid` på felfält, feltext (inte bara röd border)
- Design-reviewer klassificerar alla a11y-brister som **Blocker** utan undantag

Komplett WCAG-checklist (20 punkter), screen reader-testplaybook (NVDA + VoiceOver) och verktygsguide (axe, Lighthouse, eslint-plugin-jsx-a11y) → **jobbpilot-design-a11y**.

---

## 10. Motion

- Minimalt. Civic-produkter rör sig inte för att "kännas levande".
- Tillåtna animationer: Fade 150ms (toast, dropdown), Slide 200ms (side panel), Opacity 150ms (hover)
- Ingen bounce, spring, scale-on-hover, parallax, wiggle
- `prefers-reduced-motion: reduce` respekteras — stänger av alla animationer

---

## 11. Logotyp

Logo-marken är **Sigillet** ([ADR 0070](./docs/decisions/0070-sigillet-brand-mark-och-spinner.md), 2026-06-13): ett fyllt civilt registersigill — slät grön skiva (`--jp-accent-800` `#15603F`) + tunn vit inre ring + tre liggar-rader, mittenraden guld (`--jp-gold` `#E8C77B`) med en bock = en loggad post. Semantiskt knutet till namnet Jobbliggaren (liggare = register) och `.jp-table--flat`-formspråket. Ersätter den tidigare 4-uddiga kompassen; ADR 0068:s "kompassen förblir navy + guldprick"-not är därmed superseded.

SSOT: `web/jobbliggaren-web/src/components/brand/brand-mark-svg.tsx` (`BrandMarkSvg`, 3-färgskontrakt primär/accent/papper via `--jp-mark-*`) + `brand-logo.tsx` (`BrandLogo`). Wordmark "Jobbliggaren" i ink. Ytor: `icon.svg`, `apple-icon`, `opengraph-image`, `twitter-image`, `manifest.ts` (theme_color grön `#15603F`).

**Header-lockup** (ADR 0070 Fas 3): `BrandLogo` `full`-varianten är mark + en staplad [wordmark / tagline]. Taglinen "Den svenska jobbansökningshanteraren" (= OG-/social-taglinen) sätts som under-rad i sentence-case (12px, vikt 500, `--jp-ink-2`), `aria-hidden` (`.jp-brand`-länken bär accessible name). Mark 40px, wordmark 24px, alla fyra brand-header-barer 88px (`.jp-header__inner` = app + gäst, `.jp-land-top__inner` = landing + site). `mark`-varianten (minimala kontexter) är oförändrad — bara sigillet, ingen tagline.

**Laddningsindikator — spinner-vs-skeleton-doktrin** (ADR 0070 Fas 2): `BrandSpinner` ("Sigillet i rörelse" — pulserande register + roterande guldbåge; ren CSS, `prefers-reduced-motion` → statisk seal, `role="status"` + sr-only-label). **Skeleton är default (~90 %)** för innehållsladdning med känd form (listor, kort, detaljvyer) — den visar formen som fylls i. `BrandSpinner` används **endast** för känt-långsamma, formlösa väntor (> ~1–2 s): öppna ytan direkt, visa sedan spinnern + en svensk statusrad inuti ("Jobbannonsen läses in…"). Första konsumenter: jobbannons-modalen + sparad-ansökan-modalen (`ModalLoadingShell`). **Aldrig** spinner på snabba sid-/flik-byten — läser som jank och eroderar det seriösa intrycket.

Krav (uppfyllda): SVG; fungerar på ljus och mörk bakgrund; monokrom fallback (sätt accent = papper); civic-ton (geometrisk, stabil, inte lekfull).

---

## 12. Granskning

Design-compliance verifieras av `design-reviewer`-agenten vid varje frontend-diff. Hennes auktoritet är denna fil + skills-detaljerna. Hon har veto-makt på design-frågor — ingen MVP-dispens, inget konsensus-override.

---

**Slut på DESIGN.md.** Fullständiga specer i `.claude/skills/jobbpilot-design-*`. Huvudspec i [`BUILD.md`](./BUILD.md). Coding conventions i [`CLAUDE.md`](./CLAUDE.md).
