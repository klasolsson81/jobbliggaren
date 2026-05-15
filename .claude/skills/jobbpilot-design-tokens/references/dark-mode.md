# JobbPilot — Dark Mode (v2: SUPPORTED)

> **Status: SUPPORTED.** Designsystem v2 — Klas-GO 2026-05-16 + ADR.
>
> This replaces the earlier Fas 0 removal of dark mode. That removal was
> caused by the shadcn nova-preset shipping oklch indigo-violet values that
> broke the civic palette — **not** by a decision against dark mode itself.
> v2 reintroduces dark mode with a **civic slate scale that has no decorative
> hue**, so the trust/utility tone holds in both themes.

---

## Stance

- **Light is the default.** A first-time visitor with no stored preference and
  no OS preference gets light.
- **`prefers-color-scheme: dark` is honored automatically** — and without a
  flash of the wrong theme (inline pre-paint script, see below).
- **A manual toggle overrides** the system preference and is persisted in
  `localStorage` under the key `jp-theme` (values `light` / `dark`). The theme
  is a UI preference, not sensitive data, so `localStorage` is acceptable here
  (contrast with auth/session tokens, which must never be in `localStorage`).
- **Light and dark are validated in parallel** — dark is never a bolt-on.
  Contrast is checked in both themes (see `contrast-table.md`).
- **Sunken is darker than canvas in both themes** — the paper metaphor holds:
  light sunken `#F1F5F9` < canvas `#FFFFFF`; dark sunken `#000000` < canvas
  `#020617`.

---

## Mechanism

Theme is driven by a `data-theme="dark"` attribute on `<html>` — **not** a
`.dark` class. This matches the `ThemeProvider` + `ThemeScript` in the app and
the Tailwind v4 custom variant in `globals.css`:

```css
@custom-variant dark (&:where([data-theme="dark"], [data-theme="dark"] *));
```

Light values live in `:root {}`. Dark values override the same `--jp-*` names
in `[data-theme="dark"] {}`. Because the Tailwind `@theme inline` bridge
references `var(--jp-*)` at runtime, every semantic utility
(`bg-surface-primary`, `text-text-secondary`, …) and every shadcn bridge token
follows the theme automatically — no second Tailwind config, no duplicated
class set.

### No-flash inline script

A small synchronous script runs before paint (in `<head>`, before the app
renders) to set `data-theme` from `localStorage["jp-theme"]`, falling back to
`window.matchMedia("(prefers-color-scheme: dark)")`. This prevents a
light-then-dark flash on first paint for dark-preferring users. The script must
stay inline and render-blocking — do not move it into a deferred bundle.

---

## Token deltas (light → dark)

Full hex per token in `tokens-full.md`. Key shifts:

- **Surfaces:** canvas `#FFFFFF` → `#020617` (slate-950); chrome `#F8FAFC` →
  `#0F172A` (slate-900); hover `#F1F5F9` → `#1E293B` (slate-800); sunken
  `#F1F5F9` → `#000000` (pitch-black, still darker than canvas).
- **Text:** primary `#0F172A` → `#F8FAFC`; secondary `#475569` → `#94A3B8`;
  tertiary `#94A3B8` → `#64748B`.
- **Brand:** selection bg `brand-50` `#EAF2FB` → `#1E3A5F` (dim blue);
  primary/action `brand-600` `#0B5CAD` → `#60A5FA` (blue-400);
  `brand-700` `#094B8C` → `#BFDBFE` (text on selection). Primary button text in
  dark is dark (`#0F172A`), matching `.jp-btn--primary`.
- **Status:** 600/700 lift to the 400/300-range (e.g. success-600 `#059669` →
  `#4ADE80`) so they read on the dark canvas; 50-backgrounds become very dark
  tints (e.g. success-50 `#ECFDF5` → `#052E1A`).
- **Borders:** `border` `#E2E8F0` → `#1E293B`; `border-strong` `#CBD5E1` →
  `#334155`.
- **Focus:** `#0B5CAD` → `#60A5FA`.
- **Shadows:** opacity raised (0.04/0.06 → 0.6/0.7) so popover/dropdown depth
  remains visible on dark.

---

## Requirements (still enforced in both themes)

1. All contrast pairs re-verified for dark surfaces — light and dark validated
   separately (`contrast-table.md`).
2. Status is **never communicated by color alone** in either theme — always a
   dot + label or icon + text.
3. `prefers-reduced-motion` still respected.
4. WCAG 2.1 AA remains the floor — dark mode does not lower the bar.
5. `design-reviewer` reviews the dark rendering of any new page/component, not
   only the light one.

---

## Implementation notes

- Override the same `--jp-*` token names under `[data-theme="dark"]` — never a
  separate Tailwind config or a parallel class set.
- shadcn bridge tokens (`--background`, `--primary`, …) reference `--jp-*`, so
  they shift automatically. The only explicit dark override is
  `--primary-foreground` / `--sidebar-primary-foreground` → `#0F172A`
  (dark text on the light-blue primary in dark).
- Test with NVDA + Windows high-contrast mode in addition to standard dark.
