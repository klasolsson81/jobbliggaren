---
name: jobbpilot-design-tokens
description: >
  Canonical reference for JobbPilot's locked design tokens: colors, typography,
  spacing, radius. Use this skill whenever CSS files are edited, Tailwind
  classes are added or modified, or visual styling decisions are made in
  frontend code. Triggers on: css, tailwind, color, colour, radius, spacing,
  padding, margin, font, typography, token, hex, className, bg-, text-,
  border-, rounded-, style, globals.css, theme.
---

# JobbPilot Design Tokens

> **Synkad mot `globals.css` 2026-06-10 (G1, ADR 0068 grön accent — supersedar
> både v2-blå och navy-mellanfasen).** Källa = `web/jobbpilot-web/src/app/globals.css`
> (`:root` + `[data-theme="dark"]` + `@theme`-blocken). Vid avvikelse mellan
> denna skill och globals.css vinner globals.css alltid.
>
> **G2-not 2026-06-10 (ADR 0068 G2-notat):** display-rubriken (44px/800, 32px
> mobil) följer F4-platta-komponenten var den används (/jobb-hero + pagehero på
> alla inre sidor; landing-plattan 56px-clamp). Innehållsbredd-kanon app-wide =
> **1136px** (header = platta = innehåll; `.jp-page` använder `padding-block`).
> `.jp-empty--brand` har 0 konsumenter — dubbel-grön (två staplade gradient-
> plattor på samma sida) är förbjuden.
>
> - Civic-utility aesthetic context → `jobbpilot-design-principles`
> - Component-specific token usage → `jobbpilot-design-components`
> - WCAG contrast compliance → `jobbpilot-design-a11y`

---

## Usage rules

1. **Never hardcode hex values** — the canonical palette is `--jp-*` defined
   once in `globals.css` `:root {}` (light) + `[data-theme="dark"] {}` (dark)
2. **Never use Tailwind palette defaults** — `bg-slate-*`, `text-zinc-*`,
   `bg-gray-*` are all forbidden; use semantic token names
3. **Always use semantic names** — `bg-surface-primary`, `text-text-primary`,
   `border-border-default`, not `bg-white`, `text-black`, `bg-slate-50`
4. **Radius per ADR 0052:** 6px default (knappar, kort, rader), 4px inputs/
   checkboxar, 8px modal. F4-hero-plattan är 6px (`--jp-r-md`, ADR 0068);
   `--jp-r-xl` (12px) har noll konsumenter on-disk och städas i F-städ. Pills/badges =
   `rounded-pill`
5. **Gradient-undantaget (ADR 0068):** `--jp-hero-gradient` får ENBART användas
   på hero-plattan (`.jp-hero__plate`), `.jp-pagehero`, `.jp-empty--brand` och
   `.jp-land-hero`. Gradients är förbjudna överallt annars — civic-utility-
   regeln gäller fortsatt för all övrig UI
6. **Knapp-kontraktet (G1):** primärknapp = `--jp-accent-800` fill (#15603F,
   dark-skiftas ALDRIG) + vit text. `#6EE7A8` (dark-accent-700) används ENDAST
   som text/länk/fokus/border — ALDRIG som fill bakom vit text. Aldrig "ljus
   knapp med mörk text"
7. **Shadows:** depth comes from borders. De sanktionerade undantagen är
   `--jp-shadow-card` / `--jp-shadow-pop` (popover/dropdown) /
   `--jp-shadow-modal` (modal) — inga andra drop shadows
8. **Dark mode is supported** (v3 mörk navy-grå canvas `#0B1525`, ljusa
   input-fält) — never hardcode a light-only color; let the `--jp-*` token
   shift per theme

---

## Core color tokens

v3-neutraler (ink/surface, ADR 0052) + **grön accent-ramp** (G1, ADR 0068)
som enda interaktionsfärg. Every token carries a light value (`:root`) and a
dark value (`[data-theme="dark"]`). Token tables below carry exact on-disk
hex; in prose use the token name, not raw hex.

### Accent (grön — interaktionsfärgen, G1/ADR 0068)

| Token | Light | Dark | Use |
|---|---|---|---|
| `--jp-accent-900` | `#0B2A1E` | (skiftas EJ) | Mörkaste steg (hero-from, hover på vit knapp i pagehero) |
| `--jp-accent-800` | `#15603F` | **(skiftas EJ)** | **FILL: primärknapp, checked checkbox** — alltid vit text |
| `--jp-accent-800-hover` | `#1E6B4C` | **(skiftas EJ)** | Fill-hover, båda teman |
| `--jp-accent-700` | `#15603F` | `#6EE7A8` | **TEXT/BORDER: länkar, aktiv nav, titlar, fokus** |
| `--jp-accent-600` | `#1E6B4C` | `#A7F3D0` | Länk-hover |
| `--jp-accent-500` | `#2E8B63` | `#3E8E68` | Mellanton (input-fokus-glow-tint) |
| `--jp-accent-300` | `#74C29A` | `#2E5C46` | — |
| `--jp-accent-100` | `#D3E7DC` | `#0E2A1E` | Avatar-bg |
| `--jp-accent-50` | `#E9F2ED` | `#0E2A1E` | Tinted bg (selekterad rad, popover-selektion) |
| `--jp-gold` | `#E8C77B` | — | **Signatur — sigillets guldrad** via `--jp-mark-accent` (ADR 0070) |

**Knapp-kontraktet:** 800/800-hover/900 dark-skiftas ALDRIG — primärknappen
förblir mörkgrön med vit text i båda teman. `#6EE7A8` är ENDAST
text/länk/fokus/border, aldrig fill.

**Tailwind-alias:** `bg-brand-600` → `--jp-brand-600` → **`--jp-accent-800`**
(fill-kontraktet), `text-brand-700` → `--jp-accent-700`, `bg-brand-50` →
`--jp-accent-50` osv. — v2-brand-namnrymden är en tunn alias-brygga till
accent-rampen (G1 alias-flip). Full mappning → `references/tokens-full.md`.

### Logo-marken (Sigillet, ADR 0070)

Logo-marken är **Sigillet** (grön skiva + guld + papper) och sätter sina fyll via
`--jp-mark-primary` (= `--jp-accent-800` `#15603F`), `--jp-mark-accent`
(= `--jp-gold` `#E8C77B`) och `--jp-mark-paper` (`#FFFFFF`, tema-stabil — ring/rader
sitter på den gröna skivan, ej på sid-ytan). Den tidigare navy-kompassen + guldpricken
`#FFCD00` (`--jp-brand-accent`) är pensionerade (ADR 0070 supersederar ADR 0068:s
logo-mark-not). Navy-rampen (`--jp-navy-*`) är därmed helt utan konsument och städas i
egen F-städ-fas. Full ramp → `references/tokens-full.md`.

### Surfaces

| Token | Light | Dark | Tailwind (alias) | Use |
|---|---|---|---|---|
| `--jp-surface` | `#FFFFFF` | `#1B2B47` | `bg-surface-primary` | Kort, popover, modal. "Papper". |
| `--jp-surface-2` | `#F4F6FA` | `#142136` | `bg-surface-secondary` (även `-sunken`) | Page bg under canvas, popover-foot |
| `--jp-surface-3` | `#E8EDF4` | `#283C5E` | `bg-surface-tertiary` | Hover på rader |
| `--jp-canvas` | `#F4F6FA` | `#0B1525` | — (body/`.jp-shell`) | Sidans baslager. Dark = mörk navy-grå, **INTE svart** |

### Text

| Token | Light | Dark | Tailwind (alias) | Use |
|---|---|---|---|---|
| `--jp-ink-1` | `#0C1A2E` | `#F4F7FC` | `text-text-primary` | Brödtext, rubriker |
| `--jp-ink-2` | `#455366` | `#C2CFE2` | `text-text-secondary` | Lede, metadata, all informationsbärande sekundärtext |
| `--jp-ink-3` | `#7C8AA0` | `#8DA0BD` | `text-text-tertiary` | ~3.5:1 på vit — fails body. Dekorativt/large endast. **ALDRIG placeholder** (använd `--jp-placeholder`) |
| `--jp-ink-inverse` | `#FFFFFF` | `#0C1A2E` | `text-text-inverse` | Text på inverterad yta |
| `--jp-placeholder` | `#626B78` | (tema-oberoende) | — | Placeholder i inputs — WCAG AA ≥4.5:1 mot både `#FFFFFF` (5.39:1) och dark-temats ljusa fält `#F0F4FB` (4.89:1) |

### Borders (synliga, inte hairlines)

| Token | Light | Dark | Tailwind (alias) | Use |
|---|---|---|---|---|
| `--jp-border` | `#C9D2E0` | `#44598A` | `border-border-default` | Standardvalet (kort, header, popover) |
| `--jp-border-soft` | `#E3E8F0` | `#2C3F65` | — | Mjukaste avgränsare (popover-grupper, usermenu-sep) |
| `--jp-border-strong` | `#97A4B8` | `#6F86A8` | `border-border-strong` | Starkare avgränsare (checkbox-box) |
| `--jp-border-input` | `#7C8AA0` | `#6F86A8` | — | Input-vila — höjd kontrast så fält syns mot vit/surface-2 (samma hex som ink-3 light, medveten tonalitet, inte alias) |

### Status colors

Bas-token = text/ikon, `-bg` = pill/banner-bakgrund. Tailwind-aliasen
`*-50` → `--jp-*-bg` och `*-500/600/700` → bas-tokenen.

| Status | Token (light/dark) | Bg (light/dark) |
|---|---|---|
| Success | `#16793B` / `#5DD894` | `#DFF3E5` / `#143E29` |
| Warning | `#B4540B` / `#FBC267` | `#FCE9D1` / `#3F2A0B` |
| Danger | `#BE1B1B` / `#FB8989` | `#FBE0E0` / `#3F1419` |
| Info | `#1B5396` / `#8FBEEF` | `#DEE9F8` / `#1B3358` |

Dekorativa accenter (leaf/coral/amber) → `references/tokens-full.md`.

### Hero / gradient (DOKUMENTERAT undantag — ADR 0068)

| Token | Värde | Use |
|---|---|---|
| `--jp-hero-from` | `#0B2A1E` | Gradient-start (= accent-900) |
| `--jp-hero-mid` | `#14503A` | Gradient-mitt (60%) |
| `--jp-hero-to` | `#1E6B4C` | Gradient-slut |
| `--jp-hero-gradient` | `linear-gradient(118deg, from 0%, mid 60%, to 100%)` | **ENBART** `.jp-hero__plate` / `.jp-pagehero` / `.jp-empty--brand` / `.jp-land-hero` |
| `--jp-hero-bg` | `#14503A` | SOLID ankare — pagehero-knapp-text/border läser denna |
| `--jp-hero-ink` / `-ink-soft` | `#FFFFFF` / `rgba(255,255,255,0.78)` | Text på gradienten |

Gradienten är tema-stabil (oförändrad i dark; plattan får 1px
`--jp-border-soft`-hairline i dark). Alla kontroller i plattan är
tema-stabila vita med ink-text (`--jp-hero-pill-*`, `--jp-hero-sok-bg`
`#0C1A2E`) — bannern bär färgen, kontrollerna gör det inte.

### Focus ring

Applied globally via CSS — do not set manually per component:

```css
*:focus-visible {
  outline: 2px solid var(--jp-focus);   /* var(--jp-accent-700): #15603F light, #6EE7A8 dark */
  outline-offset: 2px;
  border-radius: var(--jp-r-sm);
}
```

`--jp-focus: var(--jp-accent-700)` — ingen separat dark-hex; accent-700
skiftar själv. **Gradient-ytor scopar om till VIT ring**
(`.jp-hero__plate`, `.jp-pagehero`, `.jp-empty--brand`, `.jp-land-hero`
sätter `--jp-focus: #FFFFFF` — grön ring syns inte mot grönt). Popovers
återställer till `var(--jp-accent-700)`. shadcn `--ring` följer `--jp-focus`.

---

## Typography scale

Global text-tracking `-0.005em` på `body` (16px, line-height 1.55 — sätts i
globals.css). Tailwind `@theme`-skala (on-disk):

| Token | Size | Use |
|---|---|---|
| `text-display` | 56px | Landing hero **only** |
| `text-h1` | 28px | Page header per view |
| `text-h2` | 20px | Section headers |
| `text-h3` | 18px | Panel/card headers |
| `text-h4` | 16px | Mindre rubriker |
| `text-body-lg` | 17px | Lede/intro |
| `text-body` | 16px | **Default everywhere in app UI** |
| `text-body-sm` | 14px | Secondary info, timestamps |
| `text-caption` | 13px | Caption |
| `text-label` | 14px | Form-labels |
| `text-mono` | 13px | IDs, datum, räknare (JetBrains Mono) |

**Dokumenterat undantag (ADR 0068):** hero-plattans display-rubrik är
44px/800 — gäller ENBART `.jp-hero__title`, inte H1-token-skalan (28px).

Mono caps-labels (kickers, kolumnhuvuden, uppercase + letter-spacing
0.08em) och mono inline-data ligger på `--jp-ink-2` eller `--jp-ink-1` —
aldrig `--jp-ink-3` (informationsbärande text, ADR 0038-golvet består).

**Fonts via `next/font/google`:** Hanken Grotesk → `--font-sans`;
JetBrains Mono → `--font-mono`. Never Inter/Roboto/Arial/system-ui as
primary; never mono for body, headings, or button text. Never
`text-xl`/`text-2xl` — always the semantic token class.

---

## Spacing (4px grid)

Use Tailwind spacing utilities — all values are multiples of 4px:

| Tailwind | px | When to use |
|---|---|---|
| `p-2` / `gap-2` | 8px | Tight: chip inner, icon gap |
| `p-3` / `gap-3` | 12px | Button inner padding |
| `p-4` / `gap-4` | 16px | **Default padding and gap** |
| `p-6` / `gap-6` | 24px | Section padding |
| `p-8` / `gap-8` | 32px | Between major sections |

---

## Density multiplier

Set via `[data-density]` on `<html>`. Multiplies layout rhythm tokens:

| Mode | `--jp-density` |
|---|---|
| `compact` | 0.85 |
| `standard` | 1.0 (default) |
| `luftig` | 1.18 |

Affects: `--jp-row-h` = `calc(36px * density)`, `--jp-section-y` =
`calc(28px * density)`, `--jp-pad-x` = `calc(28px * density)`. Never hardcode
padding/row-height where density applies — read the token.

---

## Radius (v3-kanon — ADR 0052)

| Class | Token | Value | Use |
|---|---|---|---|
| `rounded-sm` | `--jp-r-sm` | 4px | Inputs, checkboxar, hero-pills/chips |
| `rounded-md` | `--jp-r-md` | 6px | **DEFAULT** — knappar, kort, rader, popovers |
| `rounded-lg` | `--jp-r-lg` | 8px | Modal, större paneler |
| `rounded-xl` | `--jp-r-xl` | 12px | **Oanvänd** (F4-plattan är 6px per ADR 0068; tokenen kvar tills F-städ) — aldrig shadcn-primitiver (shadcn `--radius-xl` cappas till 8px) |
| `rounded-pill` | `--jp-r-pill` | 9999px | Status dots, pills, avatar |

ADR 0052-regeln: 6px rad/kort-default, 4px inputs, 8px modal, 12px ENDAST
hero. Radier > 8px utanför hero-plattan är förbjudna.

---

## Common patterns

```tsx
// Primärknapp — accent-800 fill (EJ dark-skiftad) + vit text, hover = accent-800-hover.
// I .jp-systemet: className="jp-btn jp-btn--primary". Tailwind-alias:
className="bg-brand-600 text-white hover:bg-[var(--jp-accent-800-hover)] rounded-md"

// Card / Panel — depth from border, never shadow
className="bg-surface-primary border border-border-default rounded-md p-4"

// Länk — accent-700 (skiftar till #6EE7A8 i dark), hover accent-600
className="text-brand-700 hover:text-[var(--jp-accent-600)]"

// Status pill (status-text på status-bg)
className="bg-success-50 text-success-700 rounded-pill px-2 py-0.5 text-[13px] font-medium"

// Placeholder — alltid --jp-placeholder, aldrig ink-3
className="placeholder:text-[var(--jp-placeholder)]"

// Muted helper text — ink-2 (informationsbärande, aldrig ink-3)
className="text-text-secondary text-[14px] mt-1"
```

---

## When this skill is not enough

- All tokens with exact light+dark hex values → `references/tokens-full.md`
- Contrast ratios per pair, light **and** dark → `references/contrast-table.md`
- Dark mode (v3/G1 — mechanism, deltas, no-flash) →
  `references/dark-mode.md`
- `--jp-*` + `@theme inline`-struktur som i `globals.css` →
  `references/theme-block.md`
