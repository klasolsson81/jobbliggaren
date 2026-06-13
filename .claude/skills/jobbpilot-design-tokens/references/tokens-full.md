# JobbPilot — Full Token Reference (v3 + G1 grön accent)

> **Synkad mot `globals.css` 2026-06-10 (G1, ADR 0068).** All CSS custom
> properties defined in `web/jobbpilot-web/src/app/globals.css`. The canonical
> palette is `--jp-*` defined once in `:root {}` (light) and overridden in
> `[data-theme="dark"]`. The `@theme inline {}` block bridges them into
> Tailwind semantic utilities (`bg-surface-primary` etc.) — see
> `theme-block.md`. Vid avvikelse vinner globals.css.

Light is default. Dark mode is **supported** (mörk navy-grå canvas, ljusa
input-fält) via `data-theme="dark"` on `<html>` — see `dark-mode.md`.

---

## Accent (grön — interaktionsfärgen, G1/ADR 0068)

| Token | Light | Dark | Note |
|---|---|---|---|
| `--jp-accent-900` | `#0B2A1E` | (skiftas EJ) | Mörkaste steg; = `--jp-hero-from` |
| `--jp-accent-800` | `#15603F` | **(skiftas EJ)** | **FILL: primärknapp, checked** — alltid vit text (knapp-kontraktet) |
| `--jp-accent-800-hover` | `#1E6B4C` | **(skiftas EJ)** | Fill-hover båda teman |
| `--jp-accent-700` | `#15603F` | `#6EE7A8` | **TEXT/BORDER:** länkar, aktiv nav, titlar, fokus |
| `--jp-accent-600` | `#1E6B4C` | `#A7F3D0` | Länk-hover |
| `--jp-accent-500` | `#2E8B63` | `#3E8E68` | Mellanton; light-rgb (46,139,99) används som input-fokus-glow `rgba(...,0.20)` |
| `--jp-accent-300` | `#74C29A` | `#2E5C46` | — |
| `--jp-accent-100` | `#D3E7DC` | `#0E2A1E` | Avatar-bg |
| `--jp-accent-50` | `#E9F2ED` | `#0E2A1E` | Selektions-bg (popover-rad, selekterad) |
| `--jp-gold` | `#E8C77B` | — | **Signatur — sigillets guldrad** via `--jp-mark-accent` (ADR 0070) |

`#6EE7A8` (dark-accent-700) används ENDAST som text/länk/fokus/border —
ALDRIG fill bakom vit text.

## Navy (utan konsument sedan ADR 0070)

Den gamla kompassen är pensionerad (ADR 0070 — Sigillet bär grön + guld via
`--jp-mark-*`). Navy-rampen har därmed inga konsumenter kvar och städas i egen
F-städ-fas.

| Token | Light | Dark |
|---|---|---|
| `--jp-navy-900` | `#08213F` | (skiftas EJ) |
| `--jp-navy-800` | `#0A2647` | (skiftas EJ) |
| `--jp-navy-700` | `#133F73` | `#4F8AD0` |
| `--jp-navy-600` | `#1B5396` | `#6FA4E3` |
| `--jp-navy-500` | `#2E6CC2` | `#3D75B8` |
| `--jp-navy-300` | `#7FA9DF` | `#2C5894` |
| `--jp-navy-100` | `#D6E3F4` | `#1F3866` |
| `--jp-navy-50` | `#EAF1FA` | `#1F3866` |

## Surface tokens

| Token | Light | Dark | Tailwind class (alias) | Use |
|---|---|---|---|---|
| `--jp-surface` | `#FFFFFF` | `#1B2B47` | `bg-surface-primary` | Kort, popover, modal. "Papper". |
| `--jp-surface-2` | `#F4F6FA` | `#142136` | `bg-surface-secondary` / `bg-surface-sunken` | Page bg under canvas, popover-foot |
| `--jp-surface-3` | `#E8EDF4` | `#283C5E` | `bg-surface-tertiary` | Hover på rader |
| `--jp-canvas` | `#F4F6FA` | `#0B1525` | — (`body` / `.jp-shell`) | Sidans baslager. Dark = mörk navy-grå, **INTE svart** |

## Text tokens

| Token | Light | Dark | Tailwind class (alias) | Use |
|---|---|---|---|---|
| `--jp-ink-1` | `#0C1A2E` | `#F4F7FC` | `text-text-primary` | Brödtext, rubriker |
| `--jp-ink-2` | `#455366` | `#C2CFE2` | `text-text-secondary` | Lede, metadata, all informationsbärande sekundärtext |
| `--jp-ink-3` | `#7C8AA0` | `#8DA0BD` | `text-text-tertiary` | ~3.5:1 på vit — fails body. Dekorativt/large endast; ALDRIG placeholder |
| `--jp-ink-inverse` | `#FFFFFF` | `#0C1A2E` | `text-text-inverse` | Text på inverterad yta |
| `--jp-placeholder` | `#626B78` | (tema-oberoende) | — | Placeholder — AA mot `#FFFFFF` (5.39:1) och `#F0F4FB` (4.89:1); input-fältet är ljust i båda teman |

## Border tokens (synliga, inte hairlines)

| Token | Light | Dark | Tailwind class (alias) | Use |
|---|---|---|---|---|
| `--jp-border` | `#C9D2E0` | `#44598A` | `border-border-default` | Standardvalet |
| `--jp-border-soft` | `#E3E8F0` | `#2C3F65` | — (alias `--jp-border-hairline`) | Mjukaste avgränsare |
| `--jp-border-strong` | `#97A4B8` | `#6F86A8` | `border-border-strong` | Starkare avgränsare (checkbox-box) |
| `--jp-border-input` | `#7C8AA0` | `#6F86A8` | — | Input-vila — samma hex som ink-3 light (medveten tonalitet, separat semantik) |
| `--jp-border-modal` | `var(--jp-border)` | (följer) | `border-border-modal` | ADR 0041-token, re-homed på v3-border |
| `--jp-border-structural` | `var(--jp-border)` | (följer) | `border-border-structural` | ADR 0041-amendment-token, re-homed på v3-border |

## Status

Bas-token = text/ikon; `-bg` = pill/banner-bakgrund. Tailwind-alias:
`*-50` → `--jp-*-bg`; `*-500`/`*-600`/`*-700` → bas-tokenen (alla tre samma).

| Token | Light | Dark |
|---|---|---|
| `--jp-success` | `#16793B` | `#5DD894` |
| `--jp-success-bg` | `#DFF3E5` | `#143E29` |
| `--jp-warning` | `#B4540B` | `#FBC267` |
| `--jp-warning-bg` | `#FCE9D1` | `#3F2A0B` |
| `--jp-danger` | `#BE1B1B` | `#FB8989` |
| `--jp-danger-bg` | `#FBE0E0` | `#3F1419` |
| `--jp-info` | `#1B5396` | `#8FBEEF` |
| `--jp-info-bg` | `#DEE9F8` | `#1B3358` |

## Dekorativa accenter

| Token | Light | Dark |
|---|---|---|
| `--jp-leaf-600` | `#2C8A3F` | `#5BCB7B` |
| `--jp-leaf-50` | `#DFF3E5` | `#143E29` |
| `--jp-coral-600` | `#DA2A47` | `#F47185` |
| `--jp-coral-50` | `#FCE4E9` | `#3A1722` |
| `--jp-amber-500` | `#E89A1A` | (skiftas EJ) |
| `--jp-amber-50` | `#FBEBC8` | (skiftas EJ) |

## Hero / gradient (G1 "F4 Hybrid", ADR 0068 — dokumenterat undantag)

Tema-stabila (omdefinieras INTE i dark; plattan får 1px
`--jp-border-soft`-hairline i dark). Gradient ENBART `.jp-hero__plate` /
`.jp-pagehero` / `.jp-empty--brand` / `.jp-land-hero`.

| Token | Värde | Use |
|---|---|---|
| `--jp-hero-from` | `#0B2A1E` | Gradient 0% |
| `--jp-hero-mid` | `#14503A` | Gradient 60% |
| `--jp-hero-to` | `#1E6B4C` | Gradient 100% |
| `--jp-hero-gradient` | `linear-gradient(118deg, var(--jp-hero-from) 0%, var(--jp-hero-mid) 60%, var(--jp-hero-to) 100%)` | Plattans bakgrund |
| `--jp-hero-bg` | `#14503A` | **SOLID ankare** — pagehero-knapp-text/border |
| `--jp-hero-ink` | `#FFFFFF` | Text på gradienten |
| `--jp-hero-ink-soft` | `rgba(255, 255, 255, 0.78)` | Lede/kicker på gradienten |
| `--jp-hero-pill-bg` | `#FFFFFF` | Banner-lokala vita kontroller (tema-stabila) |
| `--jp-hero-pill-ink` | `#0C1A2E` | Kontroll-text (v3-ink) |
| `--jp-hero-pill-border` | `#CBD5E1` | Kontroll-border |
| `--jp-hero-sok-bg` | `#0C1A2E` | Sök-knapp = v3-ink, tema-stabil — INTE grön |

## Focus

| Token | Light | Dark |
|---|---|---|
| `--jp-focus` | `var(--jp-accent-700)` → `#15603F` | (samma var) → `#6EE7A8` |
| `--color-focus-ring-offset` | `var(--jp-surface-primary)` | (följer) |

Gradient-ytor (`.jp-hero__plate`, `.jp-pagehero`, `.jp-empty--brand`,
`.jp-land-hero`) scopar `--jp-focus: #FFFFFF`. `.jp-popover` återställer till
`var(--jp-accent-700)`. shadcn `--ring`/`--sidebar-ring` = `var(--jp-focus)`.

## Shadows (v3 — undantag: popover/modal får skugga)

| Token | Light | Dark | Note |
|---|---|---|---|
| `--jp-shadow-card` | `0 1px 2px rgba(15,27,45,0.05), 0 1px 0 rgba(15,27,45,0.04)` | `0 1px 2px rgba(0,0,0,0.5), 0 1px 0 rgba(0,0,0,0.4)` | Kort |
| `--jp-shadow-pop` | `0 10px 30px rgba(8,23,48,0.16), 0 2px 6px rgba(8,23,48,0.08)` | `0 10px 30px rgba(0,0,0,0.55), 0 2px 6px rgba(0,0,0,0.4)` | Popover/dropdown |
| `--jp-shadow-modal` | `0 30px 80px rgba(8,23,48,0.35)` | `0 30px 80px rgba(0,0,0,0.7)` | Modal/drawer |
| `--jp-shadow-sm` | `0 1px 2px rgba(0,0,0,0.04)` | `0 1px 2px rgba(0,0,0,0.6)` | v2-alias-nivå (bridge/inline) |
| `--jp-shadow-md` | `0 2px 4px rgba(0,0,0,0.06)` | `0 2px 4px rgba(0,0,0,0.7)` | v2-alias-nivå (bridge/inline) |

Aldrig drop-shadows på knappar eller godtyckliga ytor — djup via border.

## Radius tokens (v3-kanon — ADR 0052)

| Token | Värde | Tailwind | Use |
|---|---|---|---|
| `--jp-r-sm` | `4px` | `rounded-sm` | Inputs, checkboxar, hero-pills |
| `--jp-r-md` | `6px` | `rounded-md` | **DEFAULT** — knappar, kort, rader, popovers |
| `--jp-r-lg` | `8px` | `rounded-lg` | Modal, större paneler |
| `--jp-r-xl` | `12px` | `rounded-xl` | **ENDAST hero-plattan** — shadcn `--radius-xl` cappas till `--jp-r-lg` (8px) |
| `--jp-r-pill` | `9999px` | `rounded-pill` | Status dots, pills, avatar |

ADR 0052: 6px rad/kort, 4px inputs, 8px modal, 12px ENDAST hero.

## Typografi-familjer

| Token | Värde |
|---|---|
| `--jp-font-sans` | `var(--font-sans), -apple-system, BlinkMacSystemFont, "Segoe UI", system-ui, sans-serif` |
| `--jp-font-mono` | `var(--font-mono), "SF Mono", Menlo, Consolas, monospace` |

`--font-sans` (Hanken Grotesk) och `--font-mono` (JetBrains Mono) injiceras
via `next/font/google`. Aldrig Inter/Roboto/Arial/system-ui som primär font;
aldrig mono för brödtext.

## Typografi-skala (Tailwind `@theme`, on-disk)

Global text-tracking `-0.005em` på `body` (16px, line-height 1.55).

| Token | Värde |
|---|---|
| `--text-display` | 56px (landing hero only) |
| `--text-h1` | 28px |
| `--text-h2` | 20px |
| `--text-h3` | 18px |
| `--text-h4` | 16px |
| `--text-body-lg` | 17px |
| `--text-body` | 16px |
| `--text-body-sm` | 14px |
| `--text-caption` | 13px |
| `--text-label` | 14px |
| `--text-mono` | 13px |

**Dokumenterat undantag (ADR 0068):** `.jp-hero__title` 44px/800 — ENBART
hero-plattan, inte H1-skalan. Mono caps-labels och mono inline-data på
`--jp-ink-2`/`--jp-ink-1` — aldrig `--jp-ink-3` (ADR 0038-golvet består).

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
| 64px | Mellan fristående kapitel |

## Density-multiplikator

Sätts via `[data-density]` på `<html>`:

| Mode | `--jp-density` |
|---|---|
| `compact` | `0.85` |
| `standard` | `1` (default) |
| `luftig` | `1.18` |

Påverkar `--jp-row-h` = `calc(36px * var(--jp-density))`, `--jp-section-y` =
`calc(28px * var(--jp-density))`, `--jp-pad-x` = `calc(28px * var(--jp-density))`.
Hårdkoda aldrig padding där density gäller.

## v2-kompat-alias → v3-kanon (städas i F-städ efter nollkonsumtion)

| v2-alias | Pekar på |
|---|---|
| `--jp-surface-primary` | `var(--jp-surface)` |
| `--jp-surface-secondary` | `var(--jp-surface-2)` |
| `--jp-surface-tertiary` | `var(--jp-surface-3)` |
| `--jp-surface-sunken` | `var(--jp-surface-2)` |
| `--jp-surface-inverse` | `var(--jp-ink-1)` |
| `--jp-text-primary` | `var(--jp-ink-1)` |
| `--jp-text-secondary` | `var(--jp-ink-2)` |
| `--jp-text-tertiary` | `var(--jp-ink-3)` |
| `--jp-text-inverse` | `var(--jp-ink-inverse)` |
| `--jp-brand-50` | `var(--jp-accent-50)` |
| `--jp-brand-100` | `var(--jp-accent-100)` |
| `--jp-brand-300` | `var(--jp-accent-300)` |
| `--jp-brand-500` | `var(--jp-accent-500)` |
| `--jp-brand-600` | **`var(--jp-accent-800)`** (primary = fill-kontraktet, EJ dark-skiftad) |
| `--jp-brand-700` | `var(--jp-accent-700)` (länk/hover) |
| `--jp-brand-900` | `var(--jp-accent-900)` |
| `--jp-brand-accent` | `#FFCD00` (kompass-prick — UTGÅR, ADR 0070; sigillet använder `--jp-gold`) |
| `--jp-success-50` | `var(--jp-success-bg)` — `-500/-600/-700` → `var(--jp-success)` |
| `--jp-warning-50` | `var(--jp-warning-bg)` — `-500/-600/-700` → `var(--jp-warning)` |
| `--jp-danger-50` | `var(--jp-danger-bg)` — `-500/-600/-700` → `var(--jp-danger)` |
| `--jp-info-50` | `var(--jp-info-bg)` — `-500/-600/-700` → `var(--jp-info)` |
| `--jp-border-hairline` | `var(--jp-border-soft)` |
| `--jp-border-modal` | `var(--jp-border)` |
| `--jp-border-structural` | `var(--jp-border)` |

## Komponent-fältstorlek (on-disk `.jp-*`)

| Komponent | Höjd | Varianter |
|---|---|---|
| `.jp-input` / `.jp-select` / `.jp-textarea` | 48px (textarea min 110px) | — |
| `.jp-btn` | 44px | `--sm` 36px, `--lg` 52px |
| Hero-sökrad (`.jp-hero__input`/`__searchbtn`) | 52px | — |

Dark mode: input-fälten är LJUSA (`#F0F4FB` bg, `#0C1A2E` text, border
`#94A3B8`) — user-krav, gäller både `.jp-input` och shadcn `data-slot`-fälten.
