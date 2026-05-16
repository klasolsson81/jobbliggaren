# JobbPilot — Full Token Reference (v2, slate-baserad)

All CSS custom properties defined in `globals.css`. The canonical palette is
`--jp-*` defined once in `:root {}` (light) and overridden in
`[data-theme="dark"]`. The `@theme inline {}` block bridges them into Tailwind
semantic utilities (`bg-surface-primary` etc.) — see `theme-block.md`. These
`--jp-*` hex values are authoritative; they match `JobbPilotNEWDESIGN/TOKENS.md`.

> Light is default. Dark mode is **supported** (civic slate scale, no
> decorative hue) via `data-theme="dark"` on `<html>` — see `dark-mode.md`.

---

## Surface tokens

| Token | Light | Dark | Tailwind class | Use |
|---|---|---|---|---|
| `--jp-surface-primary` | `#FFFFFF` | `#020617` | `bg-surface-primary` | Canvas, kort, modal. "Papper". |
| `--jp-surface-secondary` | `#F8FAFC` | `#0F172A` | `bg-surface-secondary` | Sidebar + topbar (chrome). |
| `--jp-surface-tertiary` | `#F1F5F9` | `#1E293B` | `bg-surface-tertiary` | Hover på rader. |
| `--jp-surface-sunken` | `#F1F5F9` | `#000000` | `bg-surface-sunken` | Dim innehåll, datum-chips, footer. Mörkare än canvas i båda lägen. |
| `--jp-surface-inverse` | `#0F172A` | `#F8FAFC` | `bg-surface-inverse` | Inverterad yta. |

## Text tokens

| Token | Light | Dark | Tailwind class | Use |
|---|---|---|---|---|
| `--jp-text-primary` | `#0F172A` | `#F8FAFC` | `text-text-primary` | Brödtext, rubriker. |
| `--jp-text-secondary` | `#475569` | `#94A3B8` | `text-text-secondary` | Lede, etiketter, metadata, mono caps-labels, all informationsbärande sekundärtext. |
| `--jp-text-tertiary` | `#94A3B8` | `#64748B` | `text-text-tertiary` | **DEKORATIVT ENDAST** — fails body contrast på vit (~2.6:1). Aldrig informationsbärande text, aldrig mono data/labels (se `contrast-table.md`). |
| `--jp-text-inverse` | `#FFFFFF` | `#0F172A` | `text-text-inverse` | Text på inverterad bakgrund. |

## Brand palette (myndighetsblå)

| Token | Light | Dark | Tailwind class | Note |
|---|---|---|---|---|
| `--jp-brand-50` | `#EAF2FB` | `#1E3A5F` | `bg-brand-50` | Selektions-bg, "idag" i kalender. |
| `--jp-brand-100` | `#C8DDF1` | `#1E40AF` | `bg-brand-100` | Avatar-bg. |
| `--jp-brand-300` | `#6BA1DC` | `#60A5FA` | `bg-brand-300` | Disabled brand-state. |
| `--jp-brand-500` | `#1F6EB8` | `#3B82F6` | `bg-brand-500` | Mellanton (sällan). |
| `--jp-brand-600` | `#0B5CAD` | `#60A5FA` | `bg-brand-600` | **PRIMARY** — åtgärd, aktiv flik, selektions-prick. |
| `--jp-brand-700` | `#094B8C` | `#BFDBFE` | `bg-brand-700` | Hover på primärknapp, länkfärg. |
| `--jp-brand-900` | `#062F57` | `#062F57` | `bg-brand-900` | Mörkaste skiftning (sällan). |

I dark läser selektion som "dimmed blue" (`brand-50`) medan action är ljusare
(`brand-600` = blue-400). Knapptext på primary i dark är mörk (`#0F172A`).

## Status: success (grön)

| Token | Light | Dark | Tailwind class |
|---|---|---|---|
| `--jp-success-50` | `#ECFDF5` | `#052E1A` | `bg-success-50` |
| `--jp-success-600` | `#059669` | `#4ADE80` | `text-success-600` |
| `--jp-success-700` | `#047857` | `#86EFAC` | `text-success-700` |

## Status: warning (amber)

| Token | Light | Dark | Tailwind class |
|---|---|---|---|
| `--jp-warning-50` | `#FFFBEB` | `#2A1D05` | `bg-warning-50` |
| `--jp-warning-600` | `#D97706` | `#FBBF24` | `text-warning-600` |
| `--jp-warning-700` | `#B45309` | `#FDE68A` | `text-warning-700` |

## Status: danger (röd)

| Token | Light | Dark | Tailwind class |
|---|---|---|---|
| `--jp-danger-50` | `#FEF2F2` | `#2E1014` | `bg-danger-50` |
| `--jp-danger-600` | `#DC2626` | `#F87171` | `text-danger-600` |
| `--jp-danger-700` | `#B91C1C` | `#FECACA` | `text-danger-700` |

## Status: info (neutral slate)

| Token | Light | Dark | Tailwind class |
|---|---|---|---|
| `--jp-info-50` | `#F1F5F9` | `#1E293B` | `bg-info-50` |
| `--jp-info-600` | `#475569` | `#94A3B8` | `text-info-600` |
| `--jp-info-700` | `#334155` | `#CBD5E1` | `text-info-700` |

## Border tokens

| Token | Light | Dark | Tailwind class | Use |
|---|---|---|---|---|
| `--jp-border` | `#E2E8F0` | `#1E293B` | `border-border-default` | Hairlines. Standardvalet (dekorativa avgränsare). |
| `--jp-border-strong` | `#CBD5E1` | `#334155` | `border-border-strong` | Tabellhuvud, kanban-kolumner — informationsbärande, klarar 3:1 mot canvas. |
| `--jp-border-modal` | `#E2E8F0` | `#64748B` | `border-border-modal` | Modal/popover-**gräns** (strukturell, ej dekorativ). Dark slate-500 klarar WCAG 1.4.11 ≥3:1 mot dimmad canvas. Light = `--jp-border`-värde (light ej defekt). ADR 0041. |
| `--jp-border-soft` | `#F1F5F9` | `#1E293B` | — | Mjukaste avgränsare. |
| `--jp-border-hairline` | `#E2E8F0` | `#1E293B` | — | Alias för `--jp-border`. |
| `--jp-border-brand` | `var(--jp-brand-600)` | — | `border-border-brand` | Brand-kant (banner, selektion). |

## Focus ring

| Token | Light | Dark |
|---|---|---|
| `--jp-focus` | `#0B5CAD` | `#60A5FA` |
| `--color-focus-ring-offset` | `var(--jp-surface-primary)` | `var(--jp-surface-primary)` |

## Shadows (endast två — använd sparsamt)

| Token | Light | Dark | Note |
|---|---|---|---|
| `--jp-shadow-sm` | `0 1px 2px rgba(0,0,0,0.04)` | `0 1px 2px rgba(0,0,0,0.6)` | Popovers. |
| `--jp-shadow-md` | `0 2px 4px rgba(0,0,0,0.06)` | `0 2px 4px rgba(0,0,0,0.7)` | Dropdowns. |

Aldrig drop-shadows på cards eller knappar. `shadow-lg`/`xl`/`2xl` förbjudna.
Djup skapas via border/hairline.

## Radius tokens (strikt civic)

| Token | Värde | Tailwind | Use |
|---|---|---|---|
| `--jp-r-sm` | `2px` | `rounded-sm` | Inputs, badges, pill-räknare. |
| `--jp-r-md` | `4px` | `rounded-md` | **DEFAULT** — knappar, panels, sökruta. |
| `--jp-r-lg` | `6px` | `rounded-lg` | Större paneler, dropdowns. |
| `--jp-r-pill` | `9999px` | `rounded-pill` | Endast statusprickar och pills. |

Radier > 6px förbjudna (pill undantaget). Inga 8/10/12px.

## Typografi-familjer

| Token | Värde |
|---|---|
| `--jp-font-sans` | `var(--font-sans), -apple-system, BlinkMacSystemFont, "Segoe UI", system-ui, sans-serif` |
| `--jp-font-mono` | `var(--font-mono), "SF Mono", Menlo, Consolas, monospace` |

`--font-sans` (Hanken Grotesk) och `--font-mono` (JetBrains Mono) injiceras via
`next/font/google` som CSS-variabler. Aldrig Inter/Roboto/Arial/system-ui som
primär font; aldrig mono för brödtext.

## Full typografi-skala

Global text-tracking `-0.005em` på `body` (optisk täthet).

| Roll | Storlek | Vikt | Line-height | Letter-spacing | Färg |
|---|---|---|---|---|---|
| Display H1 | 56px | 600 | 1.05 | -0.025em | `--jp-text-primary` |
| H1 | 28px | 600 | 1.2 | -0.02em | `--jp-text-primary` |
| H2 | 20px | 600 | 1.3 | -0.015em | `--jp-text-primary` |
| H3 | 18px | 600 | 1.3 | -0.01em | `--jp-text-primary` |
| Lede | 17px | 400 | 1.55 | 0 | `--jp-text-secondary` |
| Body | 16px | 400 | 1.55 | -0.005em (global) | `--jp-text-primary` |
| Small | 14px | 400 | 1.5 | 0 | `--jp-text-secondary` |
| Mono caps | 11.5px | 500 | 1.4 | 0.08em + UPPERCASE | `--jp-text-secondary` (ALDRIG tertiary) |
| Mono inline | 13px | 500 | 1.4 | 0 | `--jp-text-secondary` / `--jp-text-primary` (aldrig tertiary) |

Tailwind `@theme`-skala (för `text-*`-utilities): `--text-display` 56px,
`--text-h1` 28px, `--text-h2` 20px, `--text-h3` 18px, `--text-h4` 17px,
`--text-body-lg` 17px, `--text-body` 16px, `--text-body-sm` 14px,
`--text-caption` 13px, `--text-label` 14px, `--text-mono` 13px.

Display 56px/600 endast landing hero. Mono caps 11.5px/500/0.08em UPPERCASE för
kickers och kolumnhuvuden (`UPPDATERAD · MAJ 2026`), på `--jp-text-secondary` —
aldrig `--jp-text-tertiary`. Mono inline-data (datum, ID:n, räknare som
användaren läser) 13px/500 på `--jp-text-secondary` eller `--jp-text-primary`.
Aldrig all caps i sans.

> **Omkalibrerad per ADR 0038** (GOV.UK-läsbarhetsgolv) — supersederar v2-
> handoffens täthet för typografi/fältstorlek. Civic-ledger-formen (flata
> tabeller, hairlines, mono-ID:n, inga cards) är oförändrad — endast
> skala/färg/fältstorlek är omkalibrerad. `--jp-text-tertiary` är dekorativt
> endast (se `contrast-table.md`).

## Spacing (4px-grid)

| Värde | Användning |
|---|---|
| 4px | Tight stacking, dot + label |
| 8px | Inline gap, ikon till text |
| 12px | Mellan formelement, inom rad |
| 16px | Stat-värde till label, mellan tabellrader |
| 24px | Mellan sektionsblock, panel-padding |
| 28px | Sidpadding (`--jp-pad-x`) |
| 48px | Mellan major sections |
| 64px | Mellan fristående kapitel (designsystem-sidan) |

## Density-multiplikator

Sätts via `[data-density]` på app-roten (`<html>`):

| Mode | `--jp-density` |
|---|---|
| `compact` | `0.85` |
| `standard` | `1` (default) |
| `luftig` | `1.18` |

Påverkar:
- `--jp-row-h` = `calc(36px * var(--jp-density))`
- `--jp-section-y` = `calc(28px * var(--jp-density))`
- `--jp-pad-x` = `calc(28px * var(--jp-density))`

Standard radhöjd i flat ledger ~36px (density 1.0). Vertical-align top på
celler med två rader (företag + tjänst). Padding 12–14px vertikalt, 0
horisontalt (cellgränser är hairlines). Hårdkoda aldrig padding där density
gäller.

## Komponent-fältstorlek (ADR 0038)

| Komponent | Höjd | sm-variant |
|---|---|---|
| Input / Textarea / Select | 44px | 40px |
| Button | 40px | 36px |

Radius oförändrad (4px, `--jp-r-md`), transition oförändrad (80ms linear).
Toolbar-knappar kvarstår som dokumenterat undantag (28px) — men inputs/knappar
i innehållsytor är 44/40. Civic-ledger-formen är oförändrad.
