# JobbPilot — Full Token Reference

All CSS custom properties defined in `globals.css` `:root {}`. These are the
authoritative values. The `@theme {}` block maps them into Tailwind — see
`theme-block.md`.

---

## Surface tokens

| Token | Hex | Tailwind class |
|---|---|---|
| `--color-surface-primary` | `#FFFFFF` | `bg-surface-primary` |
| `--color-surface-secondary` | `#F7F7F5` | `bg-surface-secondary` |
| `--color-surface-tertiary` | `#EDECE7` | `bg-surface-tertiary` |
| `--color-surface-inverse` | `#1A1A1A` | `bg-surface-inverse` |

## Text tokens

| Token | Hex | Tailwind class |
|---|---|---|
| `--color-text-primary` | `#1A1A1A` | `text-text-primary` |
| `--color-text-secondary` | `#5A5A5A` | `text-text-secondary` |
| `--color-text-tertiary` | `#8A8A85` | `text-text-tertiary` |
| `--color-text-inverse` | `#FFFFFF` | `text-text-inverse` |

## Brand palette (myndighetsblå)

| Token | Hex | Tailwind class | Note |
|---|---|---|---|
| `--color-brand-50` | `#EAF2FB` | `bg-brand-50` | Tinted backgrounds |
| `--color-brand-100` | `#C8DDF1` | `bg-brand-100` | Hover tints |
| `--color-brand-300` | `#6BA1DC` | `bg-brand-300` | Decorative only |
| `--color-brand-500` | `#1F6EB8` | `bg-brand-500` | — |
| `--color-brand-600` | `#0B5CAD` | `bg-brand-600` | **PRIMARY** — links, buttons, focus |
| `--color-brand-700` | `#094B8C` | `bg-brand-700` | Hover on primary |
| `--color-brand-900` | `#062F57` | `bg-brand-900` | Dark brand accents |

## Status: success (green)

| Token | Hex | Tailwind class |
|---|---|---|
| `--color-success-50` | `#E8F3EC` | `bg-success-50` |
| `--color-success-600` | `#0F7A2E` | `text-success-600` |
| `--color-success-700` | `#0B5E24` | `text-success-700` |

## Status: warning (amber)

| Token | Hex | Tailwind class |
|---|---|---|
| `--color-warning-50` | `#FAF2DE` | `bg-warning-50` |
| `--color-warning-600` | `#946200` | `text-warning-600` |
| `--color-warning-700` | `#734D00` | `text-warning-700` |

## Status: danger (red)

| Token | Hex | Tailwind class |
|---|---|---|
| `--color-danger-50` | `#FBEBEB` | `bg-danger-50` |
| `--color-danger-600` | `#B42121` | `text-danger-600` |
| `--color-danger-700` | `#8C1919` | `text-danger-700` |

## Status: info (neutral blue-grey)

| Token | Hex | Tailwind class |
|---|---|---|
| `--color-info-50` | `#EEF1F5` | `bg-info-50` |
| `--color-info-600` | `#4A5A7A` | `text-info-600` |
| `--color-info-700` | `#384560` | `text-info-700` |

## Border tokens

| Token | Hex | Tailwind class |
|---|---|---|
| `--color-border-default` | `#D8D6D0` | `border-border-default` |
| `--color-border-strong` | `#B8B6B0` | `border-border-strong` |
| `--color-border-brand` | `var(--color-brand-600)` | `border-border-brand` |

## Focus ring

| Token | Value |
|---|---|
| `--focus-ring` | `var(--color-brand-600)` |
| `--focus-ring-offset` | `#FFFFFF` |

## Shadows

| Token | Value | Note |
|---|---|---|
| `--shadow-sm` | `0 1px 2px rgba(0,0,0,0.04)` | Cards |
| `--shadow-md` | `0 2px 4px rgba(0,0,0,0.06)` | Dropdowns, modals |

No larger shadows. `shadow-lg`, `shadow-xl`, `shadow-2xl` are forbidden.

## Radius tokens

| Token | Value | Tailwind | Use |
|---|---|---|---|
| `--radius-sm` | `2px` | `rounded-sm` | Inputs, chips |
| `--radius-md` | `4px` | `rounded-md` | **DEFAULT** |
| `--radius-lg` | `6px` | `rounded-lg` | Larger surfaces |
| `--radius-pill` | `999px` | `rounded-pill` | Badges only |

## Full typography scale

| Role | Size | Line-height | Weight | Tailwind class |
|---|---|---|---|---|
| `display` | 36px | 44px | 500 | `text-display` |
| `h1` | 28px | 36px | 500 | `text-h1` |
| `h2` | 22px | 28px | 500 | `text-h2` |
| `h3` | 18px | 24px | 500 | `text-h3` |
| `h4` | 16px | 22px | 500 | `text-h4` |
| `body-lg` | 16px | 24px | 400 | `text-body-lg` |
| `body` | 14px | 22px | 400 | `text-body` |
| `body-sm` | 13px | 20px | 400 | `text-body-sm` |
| `caption` | 12px | 16px | 400 | `text-caption` |
| `label` | 13px | 18px | 500 | `text-label` |
| `mono` | 13px | 18px | 400 | `text-mono` |
