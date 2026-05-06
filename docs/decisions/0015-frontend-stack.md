# ADR 0015 — Frontend-stack för JobbPilot (STEG 4a)

**Datum:** 2026-05-06
**Status:** Accepted
**Kontext:** STEG 4a — Frontend-bootstrap
**Beslutsfattare:** Klas Olsson
**Relaterad:** ADR 0003 (Design as skills), BUILD.md §3.1, DESIGN.md

---

## Kontext

Backend (.NET 10, Clean Architecture) är komplett med auth-endpoints efter STEG 3. STEG 4a startar frontend-bootstrapen. Frontenden ska ligga i `web/jobbpilot-web/` och är en civic-utility-applikation — tonen är 1177 och Digg, inte Linear eller Vercel.

Inga auth-flöden byggs i detta steg — enbart scaffolding och design-baseline.

BUILD.md §3.1 specificerar pnpm, Next.js App Router, TypeScript strict, Tailwind v4 och shadcn/ui, men lämnar öppna val om:

1. **Tailwind-konfigurationsstrategi** — §3.1 antyder hybrid-läge med `tailwind.config.ts`, men Tailwind v4 rekommenderar CSS-first-konfiguration.
2. **Färgmodell** — §3.1 nämner inte explicit HSL eller OKLCH; shadcn v4 har bytt default från HSL till OKLCH.

Dessa två öppna frågor kräver ett explicit beslut eftersom de påverkar hur design-tokens struktureras i `globals.css` och hur design-skills (`jobbpilot-design-tokens`) förhåller sig till theme-block-referensfilen.

## Beslut

JobbPilots frontend byggs med följande stack:

| Komponent | Val | Version |
|-----------|-----|---------|
| Pakethanterare | pnpm | 10.33.3 |
| Framework | Next.js (App Router) | 16.2.4 |
| TypeScript | strict + noUncheckedIndexedAccess | 6.0.3 |
| CSS-framework | Tailwind CSS v4, CSS-first config | 4.2.4 |
| Komponentbibliotek | shadcn/ui, new-york style | shadcn CLI 4.7.0 |
| Färgmodell | OKLCH (shadcn v4-default) | — |
| Font | Hanken Grotesk via next/font/google, Inter fallback | — |
| Ikoner | lucide-react | 1.14.0 |

**Två explicita avvikelser från BUILD.md §3.1:**

1. **CSS-first-konfiguration** — Tailwind v4 konfigureras via `@import "tailwindcss"` + `@theme {}` i `globals.css`, inte via `tailwind.config.ts`. Ingen JS-konfigurationsfil.
2. **OKLCH-färgvariabler** — Shadcn v4 genererar OKLCH-variabler som default. JobbPilots design-tokens (definierade som hex i `tokens-full.md`) bridgas till OKLCH-custom properties i `globals.css`.

BUILD.md §3.1 behöver uppdateras för att reflektera CSS-first. Flaggat som teknisk skuld — Klas uppdaterar BUILD.md vid nästa BUILD.md-genomgång.

## Alternativ som övervägdes

### Alt A — npm som pakethanterare

**För:** Ingen extra installation, default i Node-ekosystemet.
**Emot:** pnpm är snabbare, workspace-medvetet, och specificerat i BUILD.md §3.1. Avvisat utan vidare diskussion.

### Alt B — Pages Router

**För:** Dokumenterat, stabilt, lättare att hitta tutorials.
**Emot:** App Router är Next.js 16-default och krävs för React Server Components. BUILD.md §10.1 förutsätter App Router-arkitekturen. Avvisat.

### Alt C — Tailwind hybrid-läge (tailwind.config.ts + CSS-variabler)

**För:** Matchar nuvarande ordalydelse i BUILD.md §3.1. Välkänt för utvecklare med Next.js 14-bakgrund.
**Emot:** CSS-first är Tailwind v4:s officiella rekommenderade konfigurationsmetod. Hybrid-läget (`@config`) är kompatibilitetsläget för migration från v3 — inte det primära flödet i v4. Design-skills-referensfilen `theme-block.md` är explicit designad för CSS-first-pattern med `@theme {}`-block. Att ha en `tailwind.config.ts` parallellt med CSS-first-tokens skapar dubbel konfigurationsyta. Avvisat.

### Alt D — HSL-färgvariabler (shadcn v3-mönstret)

**För:** Bekant från shadcn v3-tutorials och äldre Next.js-projekt. Lättare att googla.
**Emot:** Shadcn v4 genererar OKLCH som default — att manuellt backa till HSL kräver explicit override och avviker från shadcn CLI-output. JobbPilots design-tokens är definierade som hex-värden i `tokens-full.md`; OKLCH bridgar hex-värden korrekt via CSS custom properties. OKLCH ger bättre perceptuell enhetlighet i ljusskalan, vilket är synligt i status-färger (success/warning/danger). Avvisat.

## Konsekvenser

### Positiva

- **Single source of truth för design-tokens** i `globals.css` — inget delat tillstånd mellan JS-konfigurationsfil och CSS-variabler.
- **OKLCH ger perceptuellt enhetliga färgskalor** — status-färger ser konsistenta ut vid olika ljusstyrkor, vilket är viktigt i en civic-utility-kontext.
- **CSS-first är bättre dokumenterat i Tailwind v4-tooling och CLI-verktyg** framåt. Hybrid-läget är bakåtkompatibelt, inte framtidsriktat.
- **Ingen `tailwind.config.ts` att underhålla** — design-tokens lever enbart i `globals.css`.
- **Hanken Grotesk** matchar civic-utility-tonen (humanistisk grotesk, lättläst i långa formulärflöden).

### Negativa

- **CSS-first är undertutorialiserat** — de flesta tredjepartstutorials för Next.js + shadcn visar fortfarande Tailwind v3/hybrid-konfiguration. Mitigering: `theme-block.md`-referensfilen i `jobbpilot-design-tokens`-skill är klar att klistra in.
- **OKLCH kräver moderna webbläsare** som stöder `oklch()`. Alla moderna webbläsare stöder detta sedan 2024. Mitigering: hex-fallback i CSS custom properties där det är kritiskt.
- **BUILD.md §3.1 är nu ur fas** med faktisk implementation. Mitigering: denna ADR är auktoritativ; BUILD.md uppdateras av Klas i separat pass.

## Implementation

Filstruktur som byggs i STEG 4a:

```
web/jobbpilot-web/
├── src/
│   ├── app/
│   │   ├── (marketing)/
│   │   │   └── page.tsx          # Landningssida
│   │   ├── globals.css           # @theme + civic design-tokens
│   │   └── layout.tsx            # Root layout + Hanken Grotesk
│   └── components/
│       └── ui/                   # shadcn-komponenter
│           ├── button.tsx
│           ├── input.tsx
│           └── card.tsx
├── components.json               # shadcn-konfiguration
├── tsconfig.json                 # strict + noUncheckedIndexedAccess
├── package.json
└── pnpm-lock.yaml
```

Design-tokens från `jobbpilot-design-tokens`-skill bridgas till `globals.css` via `@theme {}`-block enligt `theme-block.md`-referensfilen.

## Referenser

- BUILD.md §3.1 — frontend-teknologistack (behöver uppdateras)
- DESIGN.md — design-system-index
- ADR 0003 — Design-system som Claude Code-skills (CSS-first-tokens designade för skill-pattern)
- `.claude/skills/jobbpilot-design-tokens/references/theme-block.md` — Tailwind v4 CSS-first token-referens
- `.claude/skills/jobbpilot-design-tokens/references/tokens-full.md` — hex-värden för design-tokens
