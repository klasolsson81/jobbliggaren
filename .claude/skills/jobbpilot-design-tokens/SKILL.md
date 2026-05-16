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

> Canonical tokens for colors, typography, spacing, and radius.
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
4. **Radius > 6px is forbidden** in app UI (except `rounded-pill` for badges)
5. **No shadows larger than `shadow-md`** — depth comes from borders, not shadows
6. **v2 is slate-based** (cool neutral, paper metaphor) and **dark mode is
   supported** — never hardcode a light-only color; let the `--jp-*` token
   shift per theme

---

## Core color tokens

v2 is **slate-based** with myndighetsblå as the single accent. Every token
carries a light value (`:root`) and a dark value (`[data-theme="dark"]`) — the
semantic Tailwind utility resolves the current theme automatically. Token
tables below carry exact v2 hex; in prose use the token name, not raw hex.

### Surfaces

| Tailwind class | Token | Light | Dark | Use |
|---|---|---|---|---|
| `bg-surface-primary` | `--jp-surface-primary` | `#FFFFFF` | `#020617` | Canvas, kort, modal. "Papper". |
| `bg-surface-secondary` | `--jp-surface-secondary` | `#F8FAFC` | `#0F172A` | Sidebar, topbar (chrome). |
| `bg-surface-tertiary` | `--jp-surface-tertiary` | `#F1F5F9` | `#1E293B` | Hover på rader. |
| `bg-surface-sunken` | `--jp-surface-sunken` | `#F1F5F9` | `#000000` | Dim/footer. Mörkare än canvas i båda lägen. |

### Text

| Tailwind class | Token | Light | Dark | Use |
|---|---|---|---|---|
| `text-text-primary` | `--jp-text-primary` | `#0F172A` | `#F8FAFC` | Brödtext, rubriker |
| `text-text-secondary` | `--jp-text-secondary` | `#475569` | `#94A3B8` | Lede, metadata, mono caps-labels, all informationsbärande sekundärtext |
| `text-text-tertiary` | `--jp-text-tertiary` | `#94A3B8` | `#64748B` | **DEKORATIVT ENDAST** — fails body contrast on white (~2.6:1). Aldrig informationsbärande text, aldrig mono data/labels (cross-ref `references/contrast-table.md`) |

### Brand (myndighetsblå)

| Tailwind class | Token | Light | Dark | Use |
|---|---|---|---|---|
| `bg-brand-600` / `text-brand-600` | `--jp-brand-600` | `#0B5CAD` | `#60A5FA` | **PRIMARY** — buttons, links, focus ring |
| `bg-brand-700` | `--jp-brand-700` | `#094B8C` | `#BFDBFE` | Hover on primary, link text |
| `bg-brand-50` | `--jp-brand-50` | `#EAF2FB` | `#1E3A5F` | Tinted bg (selected rows, active nav) |

Full brand palette (50–900) light+dark → `references/tokens-full.md`

### Borders

| Tailwind class | Token | Light | Dark | Use |
|---|---|---|---|---|
| `border-border-default` | `--jp-border` | `#E2E8F0` | `#1E293B` | Decorative hairlines. **Standardvalet.** |
| `border-border-strong` | `--jp-border-strong` | `#CBD5E1` | `#334155` | Information-bearing dividers (kanban-kolumner, tabellhuvud) — 3:1 vs canvas |

### Status colors (600 = text/icon, 50 = background, 700 = pill text)

| Status | Token 600 (light/dark) | Token 50 (light/dark) |
|---|---|---|
| Success | `#059669` / `#4ADE80` | `#ECFDF5` / `#052E1A` |
| Warning | `#D97706` / `#FBBF24` | `#FFFBEB` / `#2A1D05` |
| Danger | `#DC2626` / `#F87171` | `#FEF2F2` / `#2E1014` |
| Info | `#475569` / `#94A3B8` | `#F1F5F9` / `#1E293B` |

Full status palette incl. 700 variants light+dark → `references/tokens-full.md`

### Focus ring

Applied globally via CSS — do not set manually per component:

```css
*:focus-visible {
  outline: 2px solid var(--jp-focus);   /* #0B5CAD light, #60A5FA dark */
  outline-offset: 2px;
  border-radius: var(--jp-r-sm);
}
```

---

## Typography scale

Global text-tracking `-0.005em` on `body` (optisk täthet, set in globals.css).

| Role | Size | Weight | Line-height | Letter-spacing | Use |
|---|---|---|---|---|---|
| Display H1 | 56px | 600 | 1.05 | -0.025em | Landing hero **only** |
| H1 | 28px | 600 | 1.2 | -0.02em | Page header per view |
| H2 | 20px | 600 | 1.3 | -0.015em | Section headers |
| H3 | 18px | 600 | 1.3 | -0.01em | Panel/card headers |
| Lede | 17px | 400 | 1.55 | 0 | Intro paragraph |
| Body | 16px | 400 | 1.55 | -0.005em | **Default everywhere in app UI** |
| Small | 14px | 400 | 1.5 | 0 | Secondary info, timestamps |
| Mono caps | 11.5px | 500 | 1.4 | 0.08em + UPPER | Kickers, kolumnhuvuden — `text-text-secondary` (ALDRIG tertiary) |
| Mono inline | 13px | 500 | 1.4 | 0 | IDs, datum, tid, räknare — `text-text-secondary` eller `text-text-primary` (aldrig tertiary) |

Full Tailwind `@theme` scale (`text-display`/`h1`/…/`mono`) →
`references/tokens-full.md`

**Fonts via `next/font/google`:** Hanken Grotesk → `--font-sans` (400/500/600);
JetBrains Mono → `--font-mono`. Never Inter/Roboto/Arial/system-ui as primary;
never mono for body, headings, or button text. Never `text-xl`/`text-2xl` —
always the semantic token class.

> **Recalibrated per ADR 0038** (GOV.UK-läsbarhetsgolv) — supersedes the v2
> handoff density for typografi/field-size; civic-ledger-formen (flata tabeller,
> hairlines, mono-ID:n, inga cards) är oförändrad. Informationsbärande text och
> mono-data/labels ligger på `text-secondary` eller `text-primary` —
> `text-tertiary` är dekorativt endast.

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

## Radius

| Class | Token | Value | Use |
|---|---|---|---|
| `rounded-sm` | `--jp-r-sm` | 2px | Inputs, badges, pill counters |
| `rounded-md` | `--jp-r-md` | 4px | **DEFAULT** — buttons, panels, search |
| `rounded-lg` | `--jp-r-lg` | 6px | Larger panels, dropdowns |
| `rounded-pill` | `--jp-r-pill` | 9999px | **Status dots and pills ONLY** |

`rounded-xl` (12px) and above are forbidden in app UI. No 8/10/12px radii.

---

## Common patterns

```tsx
// Primary button (height 40px, sm 36px, radius 4px, transition 80ms)
className="bg-brand-600 text-white hover:bg-brand-700 rounded-md h-10 px-3 text-[16px]"

// Card / Panel — depth from border, never shadow
className="bg-surface-primary border border-border-default rounded-md p-4"

// Form input (44px, sm 40px, slate-200 border, brand-600 focus + 3px brand-50 ring)
className="bg-surface-primary border border-border-default rounded-md px-2.5 h-11 text-[16px] focus:border-brand-600"

// Status pill (700 text on 50 bg)
className="bg-success-50 text-success-700 rounded-pill px-2 py-0.5 text-[11.5px] font-medium"

// Information-bearing divider (kanban column / table head) — strong, 3:1
className="border-border-strong"

// Muted helper text — text-secondary (informationsbärande, aldrig tertiary)
className="text-text-secondary text-[14px] mt-1"
```

---

## When this skill is not enough

- All tokens with exact light+dark hex values → `references/tokens-full.md`
- Contrast ratios per pair, light **and** dark → `references/contrast-table.md`
- Dark mode (v2, SUPPORTED — mechanism, deltas, no-flash) →
  `references/dark-mode.md`
- v2 `--jp-*` + `@theme inline` structure as in `globals.css` →
  `references/theme-block.md`
