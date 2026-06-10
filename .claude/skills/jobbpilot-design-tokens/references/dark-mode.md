# JobbPilot — Dark Mode (v3/G1: SUPPORTED)

> **Status: SUPPORTED.** Synkad mot `globals.css` 2026-06-10 (G1, ADR 0068
> grön accent — supersedar både v2-blå och navy-mellanfasen).
>
> Dark mode återinfördes i designsystem v2 (Klas-GO 2026-05-16) efter
> Fas 0-borttagningen (som berodde på shadcn nova-presetens oklch
> indigo-violetter, inte på ett beslut mot dark mode). v3 (ADR 0052) gav
> mörk navy-grå canvas med ljusa input-fält; G1 (ADR 0068) bytte
> interaktionsfärgen till grön accent-ramp.

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
- **Canvas i dark är `#0B1525`** — mörk navy-grå, **INTE svart och INTE
  `#020617`** (v2-slate-värdet är utgånget).
- **Knapp-kontraktet gäller i båda teman:** primärknapp = `accent-800`
  `#15603F` fill + vit text. Aldrig "ljus knapp med mörk text".

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

- **Surfaces:** canvas `#F4F6FA` → `#0B1525` (mörk navy-grå, INTE `#020617`);
  surface `#FFFFFF` → `#1B2B47`; surface-2 `#F4F6FA` → `#142136`; surface-3
  `#E8EDF4` → `#283C5E`. (v3 har ingen egen sunken-token — aliaset
  `--jp-surface-sunken` pekar på surface-2.)
- **Text:** ink-1 `#0C1A2E` → `#F4F7FC`; ink-2 `#455366` → `#C2CFE2`; ink-3
  `#7C8AA0` → `#8DA0BD`; ink-inverse `#FFFFFF` → `#0C1A2E`.
  `--jp-placeholder` `#626B78` är **tema-oberoende** (input-fälten är ljusa
  i båda teman).
- **Accent (G1):** 700 `#15603F` → `#6EE7A8`; 600 `#1E6B4C` → `#A7F3D0`;
  500 `#2E8B63` → `#3E8E68`; 300 `#74C29A` → `#2E5C46`; 100/50 → `#0E2A1E`.
  **800 / 800-hover / 900 skiftas EJ** (knapp-kontraktet: primärknappen
  förblir mörkgrön `#15603F` med vit text). `#6EE7A8` används ENDAST som
  text/länk/fokus/border — aldrig fill bakom vit text.
- **Focus:** `#15603F` → `#6EE7A8` — men `--jp-focus` omdefinieras INTE i
  dark-blocket; den är `var(--jp-accent-700)` och följer accent-skiftet
  automatiskt. Gradient-ytor scopar vit ring i båda teman.
- **Gradient/hero: OFÖRÄNDRAD i dark.** `--jp-hero-*` omdefinieras inte —
  plattan är tema-stabil och får i dark en 1px `--jp-border-soft`-hairline
  som avgränsning mot mörk canvas. Plattans kontroller är tema-stabila vita.
- **Status:** ljusare ramp mot mörk canvas — success `#16793B` → `#5DD894`,
  warning `#B4540B` → `#FBC267`, danger `#BE1B1B` → `#FB8989`, info
  `#1B5396` → `#8FBEEF`; bg-tonerna blir mycket mörka tints (t.ex.
  success-bg `#DFF3E5` → `#143E29`).
- **Borders:** border `#C9D2E0` → `#44598A`; soft `#E3E8F0` → `#2C3F65`;
  strong `#97A4B8` → `#6F86A8`; input `#7C8AA0` → `#6F86A8` (synliga, inte
  hairlines).
- **Navy (LOGO-ONLY):** rampen ljusas i dark (700 `#133F73` → `#4F8AD0`
  osv.; 800/900 skiftas ej) — enbart för `BrandLogo`-substratet; inga
  interaktions-konsumenter.
- **Shadows:** opacity raised så popover/modal-djup syns på mörk canvas
  (t.ex. shadow-pop 0.16/0.08 → 0.55/0.4).

### Komponent-undantag i dark

- **Input-fälten är LJUSA i dark** (user-krav): `#F0F4FB` bg + `#0C1A2E`
  text + `#94A3B8` border — gäller `.jp-input`/`.jp-select`/`.jp-textarea`
  och shadcn `data-slot="input|textarea|select-trigger"`.
- **Headern förblir ljus i dark** (vit remsa): `[data-theme="dark"]
  .jp-header` pinnar om hela light-paletten scoped (inkl. light-accent
  `#15603F` och re-deklarerad `--jp-focus`) så barnen renderas i ljust läge.

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
  they shift automatically. `--primary` = `--jp-brand-600` = alias →
  `--jp-accent-800` (EJ dark-skiftad) → **`--primary-foreground` är vit
  (`#FFFFFF`) i BÅDA teman** — aldrig mörk text på primärknappen.
  `--ring`/`--sidebar-ring` följer `--jp-focus` (G1 WCAG-fix: accent-800
  hade varit osynlig ring i dark).
- Test with NVDA + Windows high-contrast mode in addition to standard dark.
