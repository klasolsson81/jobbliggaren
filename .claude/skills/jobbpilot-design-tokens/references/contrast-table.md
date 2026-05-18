# JobbPilot — Contrast Ratio Table (v2 slate)

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
| `text-primary` (#0F172A) | `surface-primary` (#FFFFFF) | ~17.9:1 | AAA ✓ | Body text, rubriker |
| `text-secondary` (#475569) | `surface-primary` (#FFFFFF) | ~7.4:1 | AA ✓ | Lede, metadata, **mono caps-labels**, mono inline-data (ADR 0038) |
| `text-tertiary` (#94A3B8) | `surface-primary` (#FFFFFF) | ~2.6:1 | ✗ body | **Dekorativt endast** — aldrig informationsbärande text, aldrig mono data/labels (ADR 0038) |
| `brand-600` (#0B5CAD) | `surface-primary` (#FFFFFF) | 6.1:1 | AA ✓ | Länkar, primärknappar, fokusring |
| `brand-700` (#094B8C) | `surface-primary` (#FFFFFF) | ~8.2:1 | AA ✓ | Hover, länktext |
| `text-inverse` (#FFFFFF) | `brand-600` (#0B5CAD) | 6.1:1 | AA ✓ | Vit text på primärknapp |

## Light mode — borders / dividers

| Token | Against | Ratio | WCAG | Notes |
|---|---|---|---|---|
| `border` (#E2E8F0) | `surface-primary` (#FFFFFF) | ~1.2:1 | n/a | Dekorativ hairline — ej informationsbärande, undantaget 3:1 |
| `border-strong` (#CBD5E1) | `surface-primary` (#FFFFFF) | ~3.0:1 | AA ✓ | **Informationsbärande** dividers (kanban-kolumner, tabellhuvud) — klarar 3:1 |

## Light mode — status pairs

| Text token | Background token | Ratio | WCAG | Notes |
|---|---|---|---|---|
| `success-700` (#047857) | `success-50` (#ECFDF5) | ~5.3:1 | AA ✓ | Pill-text |
| `success-600` (#059669) | `surface-primary` (#FFFFFF) | ~3.3:1 | AA ✓ large/UI | Statusprick, ikon |
| `warning-700` (#B45309) | `warning-50` (#FFFBEB) | ~5.0:1 | AA ✓ | Pill-text |
| `danger-700` (#B91C1C) | `danger-50` (#FEF2F2) | ~6.0:1 | AA ✓ | Pill-text |
| `danger-600` (#DC2626) | `surface-primary` (#FFFFFF) | ~4.6:1 | AA ✓ | Felmeddelande-text |
| `info-700` (#334155) | `info-50` (#F1F5F9) | ~9.6:1 | AAA ✓ | Pill-text |
| `info-600` (#475569) | `surface-primary` (#FFFFFF) | ~7.4:1 | AA ✓ | Neutral statusprick |

## Light mode — surface-on-surface

| Text token | Background token | Ratio | WCAG |
|---|---|---|---|
| `text-primary` (#0F172A) | `surface-secondary` (#F8FAFC) | ~17.3:1 | AAA ✓ |
| `text-secondary` (#475569) | `surface-secondary` (#F8FAFC) | ~7.1:1 | AA ✓ |
| `text-primary` (#0F172A) | `surface-tertiary` (#F1F5F9) | ~16.5:1 | AAA ✓ |

---

## Dark mode — verified pairs

| Text token | Background token | Ratio | WCAG | Notes |
|---|---|---|---|---|
| `text-primary` (#F8FAFC) | `surface-primary` (#020617) | ~18.1:1 | AAA ✓ | Body text, rubriker |
| `text-secondary` (#94A3B8) | `surface-primary` (#020617) | ~6.5:1 | AA ✓ | Lede, metadata, **mono caps-labels**, mono inline-data (ADR 0038) |
| `text-tertiary` (#64748B) | `surface-primary` (#020617) | ~3.6:1 | ✗ body / ✓ large | **Dekorativt endast** — aldrig mono data/labels (ADR 0038) |
| `brand-600` (#60A5FA) | `surface-primary` (#020617) | ~7.0:1 | AA ✓ | Länkar, primary (action) |
| `text-inverse` (#0F172A) | `brand-600` (#60A5FA) | ~7.0:1 | AA ✓ | Mörk text på ljusblå primary |
| `text-primary` (#F8FAFC) | `surface-secondary` (#0F172A) | ~16.0:1 | AAA ✓ | Text på sidebar |
| `text-primary` (#F8FAFC) | `surface-sunken` (#000000) | ~19.3:1 | AAA ✓ | Sunken är mörkare än canvas |

## Dark mode — borders / dividers

| Token | Against | Ratio | Notes |
|---|---|---|---|
| `border` (#1E293B) | `surface-primary` (#020617) | ~1.6:1 | Dekorativ hairline — undantaget **endast** där annan separation bär (rad-bg, yt-shift, inre divider innanför migrerad kant). Är kanten enda boundary (kort/sektion/panel/sidebar) → använd `border-structural`. ADR 0041-amendment |
| `border-strong` (#334155) | `surface-primary` (#020617) | ~2.6:1 | Informationsbärande — komplettera alltid med text/ikon, aldrig endast färg |
| `border-modal` (#64748B) | `surface-primary` (#020617) | ~3.6:1 | **Strukturell** modal/popover-gräns — WCAG 1.4.11 ✓ (≥3:1 även mot `bg-black/50`-dimmad canvas). ADR 0041. Light = `#E2E8F0`. |
| `border-structural` (#64748B) | `surface-primary` (#020617) | ~3.6:1 | **Strukturell** yt-chrome-kant — kort/sektion/panel/sidebar där kanten är enda perceptuella boundary i dark. WCAG 1.4.11 ✓ (≥3:1 även mot dimmad canvas). = `--jp-info-500` / `--jp-border-modal` dark. ADR 0041-amendment 2026-05-18. Light = `#E2E8F0`. |

## Dark mode — status pairs

| Text token | Background token | Ratio | WCAG | Notes |
|---|---|---|---|---|
| `success-700` (#86EFAC) | `success-50` (#052E1A) | ~9.6:1 | AAA ✓ | Pill-text |
| `warning-700` (#FDE68A) | `warning-50` (#2A1D05) | ~11.4:1 | AAA ✓ | Pill-text |
| `danger-700` (#FECACA) | `danger-50` (#2E1014) | ~7.6:1 | AA ✓ | Pill-text |
| `info-700` (#CBD5E1) | `info-50` (#1E293B) | ~8.3:1 | AAA ✓ | Pill-text |

---

## Pairs that FAIL — do not use

| Text | Background | Issue |
|---|---|---|
| `text-tertiary` (light #94A3B8) | `surface-primary` (#FFFFFF) | ~2.6:1 — fails body. Dekorativt endast — aldrig mono inline-data/caps-labels (ADR 0038). |
| `text-tertiary` (light #94A3B8) | `surface-secondary` (#F8FAFC) | < 2.5:1 — fails. Dekorativt endast. |
| Any `brand-*` < 600 (light) | white | < 3:1 — not enough contrast for text/UI |
| `border` (default hairline) | as an information-bearing divider | < 3:1 — use `border-strong` when the divider carries meaning |

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
