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
| Ljus bakgrund default | Dark mode default |
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

Paletten är medvetet begränsad. Civic-produkter bygger tillit genom konsekvens — fler färger skapar kognitiv belastning.

- **Primärblå (myndighetsblå):** brand-600 `#0B5CAD` — knappar, länkar, fokusring
- **Neutral grå-skala:** fyra ytor (primary → tertiary → inverse), tre text-nivåer
- **Statusfärger:** success grön, warning amber, danger röd, info grå-blå — alltid i 50/600/700-varianter
- **Borders:** border-default `#D8D6D0`, border-strong `#B8B6B0`
- **Skuggor:** bara shadow-sm och shadow-md — djup skapas via border, inte shadow
- WCAG AA-kontrast obligatoriskt på alla färgpar. brand-600 på vit = 6.1:1.

Exakta tokens och hex-värden, kontrast-tabell, dark-mode-stance och deploy-ready `@theme`-block → **jobbpilot-design-tokens**.

---

## 4. Typografi (sammanfattning)

- **Primär:** Hanken Grotesk (Google Fonts, open source) — weight 400, 500, 600
- **Monospace:** JetBrains Mono — för IDs, org-nummer, tabellvärden
- **7 roller för app-UI:** h1 (28px), h2 (22px), h3 (18px), h4 (16px/500), body (14px default), body-sm (13px), label (13px/500)
- Aldrig all caps. Aldrig letter-spacing-justeringar.
- Italic bara i citat och referenser, aldrig för emfas i body.

Komplett skala, line-heights och Tailwind-mappning → **jobbpilot-design-tokens**.

---

## 5. Spacing och layout (sammanfattning)

- **4px-baserad skala.** Default padding: p-4 (16px). Default gap: gap-4 (16px).
- **Border-radius:** sm 2px (inputs), md 4px (default — knappar, cards), lg 6px (panels), pill 9999px (badges only).
- **App shell:** vänster sidebar 240px (collapsed 60px), max-width 1280px på innehåll.
- **Formulär:** max-width 640px, labels ovanför inputs.
- Desktop-first i v1 — vyer ska fungera på mobil men är inte optimerade.

---

## 6. Komponenter (sammanfattning)

shadcn/ui är primitive-lagret. Komponenter kopieras in i `components/ui/` — de ägs av projektet, importeras inte från npm.

Använda i v1: Button, Card, Input, Textarea, Select, Badge, Dialog, Toast, Table, Breadcrumb, Form, Skeleton, Alert.

Aldrig byt ut mot: Material UI, Chakra, Mantine, Headless UI.

Regler:
- En primary button per form — aldrig två primärknappar sida vid sida
- Destructive actions kräver alltid bekräftelse-dialog
- Icon-only buttons kräver `aria-label`
- Loading state: ersätt label med "Sparar…", behåll bredd, sätt `disabled`

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
