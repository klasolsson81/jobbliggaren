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

1. **Never hardcode hex values** outside `globals.css` `@theme` block
2. **Never use Tailwind palette defaults** — `bg-slate-*`, `text-zinc-*`,
   `bg-gray-*` are all forbidden; use semantic token names
3. **Always use semantic names** — `bg-background`, `text-foreground`,
   `border-border`, not `bg-white`, `text-black`
4. **Radius > 6px is forbidden** in app UI (except `rounded-pill` for badges)
5. **No shadows larger than `shadow-md`** — depth comes from borders, not shadows

---

## Core color tokens

### Surfaces

| Tailwind class | Token | Use |
|---|---|---|
| `bg-surface-primary` | `--color-surface-primary` | Main content background |
| `bg-surface-secondary` | `--color-surface-secondary` | Panels, sidebar, table head |
| `bg-surface-tertiary` | `--color-surface-tertiary` | Hover states |

### Text

| Tailwind class | Token | Use |
|---|---|---|
| `text-text-primary` | `--color-text-primary` | Body text |
| `text-text-secondary` | `--color-text-secondary` | Help text, timestamps |
| `text-text-tertiary` | `--color-text-tertiary` | Disabled, placeholder |

### Brand (myndighetsblå)

| Tailwind class | Use |
|---|---|
| `bg-brand-600` / `text-brand-600` | **PRIMARY** — buttons, links, focus ring |
| `bg-brand-700` | Hover on primary elements |
| `bg-brand-50` | Tinted background (selected rows, active nav items) |

Full brand palette (50–900) → `references/tokens-full.md`

### Borders

| Tailwind class | Use |
|---|---|
| `border-border-default` | All dividers, input borders |
| `border-border-strong` | Hover, secondary focus |

### Status colors (600 = text/icon, 50 = background)

| Status | Text class | Background class |
|---|---|---|
| Success | `text-success-600` | `bg-success-50` |
| Warning | `text-warning-600` | `bg-warning-50` |
| Danger | `text-danger-600` | `bg-danger-50` |
| Info | `text-info-600` | `bg-info-50` |

Full status palette including 700 hover variants → `references/tokens-full.md`

### Focus ring

Applied globally via CSS — do not set manually per component:

```css
*:focus-visible {
  outline: 2px solid var(--focus-ring);      /* brand-600 */
  outline-offset: 2px;
}
```

---

## Typography scale (core 6)

| Class | Size | Weight | Use |
|---|---|---|---|
| `text-h1` | 28px / lh 36px | 500 | Page header per view |
| `text-h2` | 22px / lh 28px | 500 | Section headers |
| `text-h3` | 18px / lh 24px | 500 | Panel/card headers |
| `text-body` | 14px / lh 22px | 400 | **Default everywhere in app UI** |
| `text-body-sm` | 13px / lh 20px | 400 | Secondary info, timestamps |
| `text-label` | 13px / lh 18px | 500 | Form labels |

Full scale (display, h4, body-lg, caption, mono) → `references/tokens-full.md`

**Font:** Hanken Grotesk via `next/font/google` (400, 500, 600). Never use
`text-xl`, `text-2xl` etc. — always use the semantic token class.

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

## Radius

| Class | Value | Use |
|---|---|---|
| `rounded-sm` | 2px | Inputs, small chips |
| `rounded-md` | 4px | **DEFAULT** — buttons, cards, panels |
| `rounded-lg` | 6px | Larger surfaces |
| `rounded-pill` | 9999px | **Badges and pills ONLY** |

`rounded-xl` (12px) and above are forbidden in app UI.

---

## Common patterns

```tsx
// Primary button
className="bg-brand-600 text-white hover:bg-brand-700 rounded-md px-4 py-2 text-body"

// Card / Panel
className="bg-surface-primary border border-border-default rounded-md p-4"

// Form input
className="bg-surface-primary border border-border-default rounded-sm px-3 h-9 text-body focus:border-brand-600"

// Status badge
className="bg-success-50 text-success-700 rounded-pill px-2 py-0.5 text-xs font-medium"

// Table header cell
className="bg-surface-secondary text-text-secondary text-label px-3 h-9"

// Muted helper text
className="text-text-secondary text-body-sm mt-1"
```

---

## When this skill is not enough

- All 40+ tokens with exact hex values → `references/tokens-full.md`
- Contrast ratios per text/background pair → `references/contrast-table.md`
- Dark mode tokens (v2 roadmap, not active) → `references/dark-mode.md`
- Complete `@theme {}` CSS block ready to paste into `globals.css` →
  `references/theme-block.md`
