# DESIGN.md — JobbPilot design system

> Pedagogisk inramning för JobbPilots civic-utility-design.
> Fullständiga specer finns i Claude Code-skills under `.claude/skills/jobbpilot-design-*`.
>
> **Huvudspec:** [`BUILD.md`](./BUILD.md)
> **Coding conventions:** [`CLAUDE.md`](./CLAUDE.md)

---

## 1. Design-filosofi

### 1.1 Grundprincip

JobbPilot är ett verktyg för stressade jobbsökare. UI:t ska signalera **tillit och pålitlighet**, inte imponera eller underhålla. Målet är att en 55-årig processoperatör i Alingsås som söker sitt nästa jobb ska känna att appen är byggd för att fungera, inte för att sälja.

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
| Myndighetsblå primärfärg | Neon, lila, cyan-accenter |
| Rak svensk copy | Emojis, utropstecken, "Let's go!" |
| Tabeller och listor | Kort-layouter överallt |
| `border-radius: 4px` | 16px+ rundade hörn |
| Muted statusfärger | Glow, drop shadow, glasmorfism |
| Breadcrumbs + hierarki | Flata sidor utan kontext |
| Systemfont/Hanken Grotesk | Display-fonter, scripts |
| Content-first sidor | Hero-sektioner, vibey microcopy |
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

Paletten är medvetet begränsad. Civic-produkter bygger tillit genom konsekvens — fler färger skapar kognitiv belastning. v2 är **slate-baserad** (kall neutral, papper-metafor).

- **Primärblå (myndighetsblå):** brand-600 `#0B5CAD` — knappar, länkar, fokusring, aktiv selektion
- **Neutral slate-skala:** surfaces `#FFFFFF` / `#F8FAFC` / `#F1F5F9`, text `#0F172A` / `#475569` / `#94A3B8`
- **Statusfärger:** success grön, warning amber, danger röd, info slate — alltid i 50/600/700-varianter, endast för status (aldrig dekoration)
- **Borders:** border-default `#E2E8F0` (slate-200, dekorativa hairlines), border-strong `#CBD5E1` (slate-300, informationsbärande dividers — klarar 3:1 mot vit canvas)
- **Skuggor:** bara shadow-sm och shadow-md (popovers/dropdowns) — djup skapas via border/hairline, aldrig på cards/knappar
- WCAG AA-kontrast obligatoriskt på alla färgpar. brand-600 på vit = 6.1:1.

### Dark-mode-stance

Dark mode **stöds** (designsystem v2, Klas-GO 2026-05-16 + ADR — ersätter Fas 0-borttagningen som skedde pga shadcn-presetens oklch indigo-violetter). v2 använder en **civic slate-skala utan dekorativ hue** (`data-theme="dark"` på `<html>`). Light är default; `prefers-color-scheme: dark` honoreras **automatiskt och utan flash** (inline pre-paint-script), manuell toggle överrider och persisteras i localStorage. Sunken-ytor är mörkare än canvas i båda lägen (samma papper-metafor). Light och dark valideras parallellt — aldrig dark som efterhandstillägg.

Exakta tokens och hex-värden (light+dark), kontrast-tabell, density-system och deploy-ready `@theme`/`--jp-*`-block → **jobbpilot-design-tokens**.

---

## 4. Typografi (sammanfattning)

- **Primär:** Hanken Grotesk (`next/font/google`, variabel `--font-sans`) — weight 400, 500, 600
- **Monospace:** JetBrains Mono (`next/font/google`, variabel `--font-mono`) — för IDs, SSYK-koder, datum, tid, versioner, mono caps-labels, pill-räknare. Aldrig brödtext/rubriker/knapptext.
- **App-UI-roller (ADR 0038 — GOV.UK-läsbarhetsgolv):** body **16px/400** (golv — aldrig informationsbärande text < 16px), body-sm/small **14px** (min), lede **17px/400**, h3 **18px/600**, h2 **20px/600**, h1 **28px/600**
- **Display (v2 landing):** 56px / 600 / line-height 1.05 / letter-spacing −0.025em — endast landing hero
- **Mono inline (data — datum, ID, räknare):** 13px/500, färg `text-secondary`/`text-primary` (aldrig `text-tertiary`)
- **Mono caps (labels):** 11.5px / 500 / letter-spacing 0.08–0.16em / UPPERCASE, färg `text-secondary` — kickers, kolumnhuvuden (`UPPDATERAD · MAJ 2026`)
- **`text-tertiary` är endast dekorativt** (≈2.6:1 — separatorer, inaktiva ikoner). Informationsbärande text alltid `text-secondary` (7.4:1) eller `text-primary`.
- Civic-ledger-*formen* (flata tabeller, hairlines, mono-IDs, inga cards) är oförändrad — ADR 0038 omkalibrerar endast skala/färg/fältstorlek (handoffen drev under §1.1-målanvändarens läsbarhetsbehov).
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

Prioriteras senare — designas inför klass-launch (fas 8). Krav: SVG, fungerar på ljus och mörk bakgrund, en-tonig + positiv/negativ-variant, monokrom fallback, civic-ton (geometrisk, stabil, inte lekfull).

Föreslagna riktningar: stiliserad kompass (pilot-metafor), monogram "JP", Platsbanken-aktig cirkel med subtil twist.

---

## 12. Granskning

Design-compliance verifieras av `design-reviewer`-agenten vid varje frontend-diff. Hennes auktoritet är denna fil + skills-detaljerna. Hon har veto-makt på design-frågor — ingen MVP-dispens, inget konsensus-override.

---

**Slut på DESIGN.md.** Fullständiga specer i `.claude/skills/jobbpilot-design-*`. Huvudspec i [`BUILD.md`](./BUILD.md). Coding conventions i [`CLAUDE.md`](./CLAUDE.md).
