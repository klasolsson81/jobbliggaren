# JobbPilot — Contrast Ratio Table

WCAG 2.1 AA requirements:
- Body text (< 18.66px bold, < 24px regular): **4.5:1 minimum**
- Large text (≥ 18.66px bold or ≥ 24px regular): **3:1 minimum**
- UI components and icons: **3:1 minimum**

Verify new combinations at https://webaim.org/resources/contrastchecker

---

## Verified pairs (from DESIGN.md §2.3)

| Text token | Background token | Ratio | WCAG | Notes |
|---|---|---|---|---|
| `text-primary` (#1A1A1A) | `surface-primary` (#FFFFFF) | 17.0:1 | AAA ✓ | Body text |
| `text-secondary` (#5A5A5A) | `surface-primary` (#FFFFFF) | 7.4:1 | AA ✓ | Help text |
| `text-tertiary` (#8A8A85) | `surface-primary` (#FFFFFF) | 3.9:1 | AA ✓ body-sm | Borderline — use ≥ 13px |
| `brand-600` (#0B5CAD) | `surface-primary` (#FFFFFF) | 6.1:1 | AA ✓ | Links, primary buttons |
| `brand-700` (#094B8C) | `surface-primary` (#FFFFFF) | 7.3:1 | AA ✓ | Hover state |
| `text-inverse` (#FFFFFF) | `brand-600` (#0B5CAD) | 6.1:1 | AA ✓ | White text on primary button |
| `text-inverse` (#FFFFFF) | `surface-inverse` (#1A1A1A) | 17.0:1 | AAA ✓ | Dark surfaces |

## Status color pairs

| Text token | Background token | Ratio | WCAG | Notes |
|---|---|---|---|---|
| `success-600` (#0F7A2E) | `success-50` (#E8F3EC) | ~5.2:1 | AA ✓ | Success badge |
| `success-700` (#0B5E24) | `success-50` (#E8F3EC) | ~6.4:1 | AA ✓ | Hover |
| `warning-600` (#946200) | `warning-50` (#FAF2DE) | ~4.7:1 | AA ✓ | Warning badge |
| `warning-700` (#734D00) | `warning-50` (#FAF2DE) | ~6.1:1 | AA ✓ | Hover |
| `danger-600` (#B42121) | `danger-50` (#FBEBEB) | ~5.5:1 | AA ✓ | Danger badge |
| `danger-600` (#B42121) | `surface-primary` (#FFFFFF) | ~5.5:1 | AA ✓ | Error text on white |
| `info-600` (#4A5A7A) | `info-50` (#EEF1F5) | ~5.0:1 | AA ✓ | Info badge |

## Surface-on-surface pairs

| Text token | Background token | Ratio | WCAG |
|---|---|---|---|
| `text-primary` (#1A1A1A) | `surface-secondary` (#F7F7F5) | 16.5:1 | AAA ✓ |
| `text-secondary` (#5A5A5A) | `surface-secondary` (#F7F7F5) | 7.2:1 | AA ✓ |
| `text-primary` (#1A1A1A) | `surface-tertiary` (#EDECE7) | 15.3:1 | AAA ✓ |

## Pairs that FAIL — do not use

| Text | Background | Ratio | Issue |
|---|---|---|---|
| `text-tertiary` (#8A8A85) | `surface-secondary` (#F7F7F5) | ~3.7:1 | Fails AA for body text (< 18px) — use only for caption or decorative |
| Any brand-* below 600 | white | < 3:1 | Not enough contrast |

## Adding new color combinations

Before shipping any new text/background pair not in this table:
1. Check ratio at https://webaim.org/resources/contrastchecker
2. Verify it meets the threshold for its use case (body = 4.5:1, large/UI = 3:1)
3. Add to this table
4. Flag to `design-reviewer` for confirmation
