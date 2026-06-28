# JobbPilot — Contrast Ratio Table (v3 + G1 grön accent)

> **Synkad mot `globals.css` 2026-06-10 (G1, ADR 0068).**

WCAG 2.1 AA requirements:
- Body text (< 18.66px bold, < 24px regular): **4.5:1 minimum**
- Large text (≥ 18.66px bold or ≥ 24px regular): **3:1 minimum**
- UI components, icons, and information-bearing dividers: **3:1 minimum**

Verify new combinations at https://webaim.org/resources/contrastchecker

> Light and dark are validated **separately**. A pair that passes in light is
> not assumed to pass in dark — recompute per theme.

---

## Light mode — verified pairs

| Text token | Background token | Ratio | WCAG | Notes |
|---|---|---|---|---|
| `ink-1` (#0C1A2E) | `surface` (#FFFFFF) | ~17.5:1 | AAA ✓ | Body text, rubriker |
| `ink-2` (#455366) | `surface` (#FFFFFF) | ~7.8:1 | AAA ✓ | Lede, metadata, mono-labels |
| `ink-3` (#4F5D72) | `surface` (#FFFFFF) | ~6.7:1 | AA ✓ | Demoterad metadata-tier (mörkad från #7C8AA0/3.5:1, issue #296; min 5.45:1 över surfaces/info-bg) — placeholder = `--jp-placeholder` |
| `accent-700` (#15603F) | `surface` (#FFFFFF) | 7.56:1 | AAA ✓ | Länkar, aktiv nav, titlar, fokusring |
| `accent-700` (#15603F) | `canvas` (#F4F6FA) | ~7.0:1 | AAA ✓ | Länk på canvas |
| vit (#FFFFFF) | `accent-800` (#15603F) | 7.56:1 | AAA ✓ | Vit text på primärknapp (fill-kontraktet) |
| vit (#FFFFFF) | `accent-800-hover` (#1E6B4C) | ~6.4:1 | AA ✓ | Vit text på primärknapp-hover |
| `placeholder` (#626B78) | `surface` (#FFFFFF) | 5.39:1 | AA ✓ | Placeholder light-fält |
| `placeholder` (#626B78) | dark-input-fält (#F0F4FB) | 4.89:1 | AA ✓ | Placeholder i dark-temats ljusa fält (tema-oberoende token) |

## Light mode — borders / dividers

| Token | Against | Ratio | Notes |
|---|---|---|---|
| `border` (#C9D2E0) | `surface` (#FFFFFF) | ~1.5:1 | Synlig avgränsare men ej informationsbärande ensam |
| `border-input` (#7C8AA0) | `surface` (#FFFFFF) | ~3.5:1 | Input-vila — klarar 3:1 UI-golvet |
| `border-strong` (#7C8AA0) | `surface` (#FFFFFF) | ~3.5:1 | AA ✓ UI — klarar 3:1-golvet (höjt från #97A4B8/2.5:1, issue #193); delar nu värde med border-input |

## Light mode — status pairs

| Text token | Background token | Ratio | WCAG | Notes |
|---|---|---|---|---|
| `success` (#16793B) | `success-bg` (#DFF3E5) | ~4.7:1 | AA ✓ | Pill-text |
| `success` (#16793B) | `surface` (#FFFFFF) | ~5.5:1 | AA ✓ | Statusikon/text |
| `leaf-600` (#1C7530) | `leaf-50` (#DFF3E5) | ~5.0:1 | AA ✓ | "Ny"-tag / .jp-job__newflag (mörkad från #2C8A3F/3.76:1, issue #193) |
| `warning` (#A34A06) | `warning-bg` (#FCE9D1) | ~5.0:1 | AA ✓ | Pill-text (mörkad från #B4540B/4.2:1, issue #193) |
| `warning` (#A34A06) | `surface` (#FFFFFF) | ~5.9:1 | AA ✓ | Felfri som text på vit |
| `danger` (#BE1B1B) | `danger-bg` (#FBE0E0) | ~5.0:1 | AA ✓ | Pill-text |
| `danger` (#BE1B1B) | `surface` (#FFFFFF) | ~6.2:1 | AA ✓ | Felmeddelande-text |
| `info` (#1B5396) | `info-bg` (#DEE9F8) | ~6.3:1 | AA ✓ | Pill-text |
| `info` (#1B5396) | `surface` (#FFFFFF) | ~7.7:1 | AAA ✓ | Info-text |

---

## Dark mode — verified pairs

| Text token | Background token | Ratio | WCAG | Notes |
|---|---|---|---|---|
| `ink-1` (#F4F7FC) | `canvas` (#0B1525) | ~17.0:1 | AAA ✓ | Body text, rubriker |
| `ink-1` (#F4F7FC) | `surface` (#1B2B47) | ~13.2:1 | AAA ✓ | Text på kort |
| `ink-2` (#C2CFE2) | `canvas` (#0B1525) | ~11.6:1 | AAA ✓ | Sekundärtext |
| `accent-700` (#6EE7A8) | `canvas` (#0B1525) | 11.9:1 | AAA ✓ | Länkar, aktiv nav, fokus — **ENDAST text/länk/fokus/border, aldrig fill** |
| `accent-700` (#6EE7A8) | `surface` (#1B2B47) | ~9.2:1 | AAA ✓ | Länk på kort |
| `accent-600` (#A7F3D0) | `canvas` (#0B1525) | ~14.3:1 | AAA ✓ | Länk-hover |
| vit (#FFFFFF) | `accent-800` (#15603F) | 7.56:1 | AAA ✓ | Primärknapp i dark — accent-800 skiftas EJ |
| mörk text (#0C1A2E) | dark-input-fält (#F0F4FB) | ~15.8:1 | AAA ✓ | Ljusa input-fält i dark (user-krav) |

## Dark mode — status pairs

| Text token | Background token | Ratio | WCAG | Notes |
|---|---|---|---|---|
| `success` (#5DD894) | `success-bg` (#143E29) | ~6.7:1 | AA ✓ | Pill-text |
| `warning` (#FBC267) | `warning-bg` (#3F2A0B) | ~8.4:1 | AAA ✓ | Pill-text |
| `danger` (#FB8989) | `danger-bg` (#3F1419) | ~6.8:1 | AA ✓ | Pill-text |
| `info` (#8FBEEF) | `info-bg` (#1B3358) | ~6.6:1 | AA ✓ | Pill-text |

---

## Gradient-ytor (hero-plattan, pagehero, empty-brand, land-hero)

Gradienten är tema-stabil (samma i light + dark). Fokusringen i
gradient-scope är **VIT** (`--jp-focus: #FFFFFF`) — grön ring syns inte
mot grönt.

| Text | Background | Ratio | WCAG | Notes |
|---|---|---|---|---|
| vit (#FFFFFF) | `hero-from` (#0B2A1E) | ~15.4:1 | AAA ✓ | Gradient-start |
| vit (#FFFFFF) | `hero-mid` / `hero-bg` (#14503A) | ~9.4:1 | AAA ✓ | Gradient-mitt = solid ankare |
| vit (#FFFFFF) | `hero-to` (#1E6B4C) | ~6.4:1 | AA ✓ | Gradient-slut — sämsta stoppet, fortfarande AA |
| `hero-pill-ink` (#0C1A2E) | `hero-pill-bg` (#FFFFFF) | ~17.5:1 | AAA ✓ | Tema-stabila vita kontroller i plattan |
| vit (#FFFFFF) | `hero-sok-bg` (#0C1A2E) | ~17.5:1 | AAA ✓ | Sök-knapp (ink, INTE grön) |

---

## Pairs that FAIL — do not use

| Text | Background | Issue |
|---|---|---|
| vit text | `accent-700` dark (#6EE7A8) som fill | ~1.5:1 — kontraktsbrott. #6EE7A8 är ENDAST text/länk/fokus/border; fill = `accent-800` (skiftas EJ). |
| Ljus knapp + mörk text som "primary" | — | Bryter knapp-kontraktet (ADR 0068): primärknapp är alltid mörkgrön accent-800 + vit text, båda teman. |
| Grön fokusring | gradient-ytor | Syns inte mot grönt — gradient-scope sätter `--jp-focus: #FFFFFF`. |
| `border` (hairline-bruk) | som ensam informationsbärande avgränsare | ~1.5:1 light — komplettera med text/ikon eller starkare separation. |

---

## Adding new color combinations

Before shipping any new text/background pair not in this table:
1. Check ratio at https://webaim.org/resources/contrastchecker
2. Verify the threshold for its use case (body = 4.5:1, large/UI = 3:1)
3. Verify **in both light and dark** — they are validated separately
4. Add to this table
5. Flag to `design-reviewer` for confirmation

Ratios are computed values; treat any pair within ~0.2 of a threshold as
borderline and re-check with the live checker before shipping.
