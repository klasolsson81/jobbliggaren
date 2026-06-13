# design-reviewer — Logo Fas 3 header-lockup + tagline, 2026-06-13

**Verdict:** ✓ Approved — **0 Blocker / 0 Major / 0 Minor**.

Rendered review against `C:/tmp/jobbliggaren-header-final/header-final-{light,dark}.png`
(committed code) + the comparison set (baseline / A / B). Variant choice (A,
sentence-case) was Klas's — not re-litigated; the implementation is reviewed.

## Verified

- **Dark-mode contrast (the one real risk) — holds.** The tagline sets
  `color: var(--jp-ink-2)`, which is light-grey `#C2CFE2` globally in dark (~1.4:1
  on white = would be a 1.4.3 Blocker). BUT all four brand headers are scoped-white
  bars that re-pin the full ink ramp in dark: `[data-theme="dark"] .jp-header`
  (globals.css:579) and `[data-theme="dark"] .jp-land-top` (globals.css:2956) both
  set `--jp-ink-2: #455366`. The tagline lands on **#455366 on white ≈ 7.8:1 (AAA)**
  in both themes, all four headers. `header-final-dark.png` confirms.
- **a11y:** mark `aria-hidden` (full), wordmark + tagline both `aria-hidden`; the
  accessible name is carried by each `.jp-brand` Link's unique `aria-label`
  (app-shell:361, guest-shell:48, landing-topbar:32, site-header:18). No
  double-announce. Focus ring (2.4.7) intact. Static lockup → no reduced-motion
  concern.
- **Token discipline:** `--jp-ink-2` is the correct muted token (not ink-3). Raw
  px (word 24, tagline 12) is consistent with the `.jp-*` component-internal
  convention (precedent: `.jp-modal__company` 15px, `.jp-land-top__stat__num` 17px).
- **Civic aesthetic:** no gradient/glow/glassmorphism, no emoji, radius untouched,
  mark via `--jp-mark-*`. Proportions mark 40 / wordmark 24 / tagline 12 balanced;
  88px header gives air without going hero-scale.
- **Copy:** "Den svenska jobbansökningshanteraren" — civic, du-neutral, OG-consistent.
- **Consistency:** one source (BrandLogo) → all four headers identical. Marketing
  tagline on the legal/auth site-header is acceptable (descriptive product
  signature, not a marketing claim).

## Note (not a finding)

`[data-theme="dark"] .jp-land-top .jp-brand { color: #0C1A2E }` (globals.css:2968)
is now redundant for the wordmark (the scoped ink re-pin already resolves ink-1 to
#0C1A2E). Harmless belt-and-suspenders, pre-dates the full ink pin; a future F-städ
item, out of scope for this PR.
