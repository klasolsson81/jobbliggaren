# Session log 2026-06-13 — Logo-översyn Fas 1: Sigillet brand mark

## Scope

Logo redesign following the JobbPilot → Jobbliggaren rename (ADR 0069). The
4-point compass brand mark is replaced by **"Sigillet"** — a filled civic
register-seal. Concept was iterated and locked with Klas in chat: concept B
"Sigillet", smooth disc + inner ring (the milled-stamp edge was rejected as too
retro at small sizes), green + gold, and — for the spinner — gold arc + pulsing
rows. Implemented Phase 1 (the static mark + all brand surfaces); the
`BrandSpinner` is split to a Phase 2 follow-up PR per the design-reviewer Major
2 + Klas's decision (ship the mark now, wire + document + visual-verify the
spinner separately).

## Delivered — Phase 1 (branch `feat/jobbliggaren-mark-spinner`, from main `017eb89`)

- `brand-mark-svg.tsx` (+test): compass → seal; props 2 → 3 fills
  (primary/accent/**paper**) via `--jp-mark-*`. Geometry: green disc r45, paper
  inner ring r37, three ledger rows (middle gold + green check = logged entry).
- `brand-logo.tsx` (+test): `currentColor` → explicit `--jp-mark-*` tokens;
  wordmark navy → `--jp-ink-1`; dark scoped-white-topbar override navy `#133F73`
  → ink `#0C1A2E`.
- `icon.svg` / `apple-icon.tsx` / `opengraph-image.tsx` / `twitter-image.tsx`:
  green seal; `manifest.ts` `theme_color` navy → green `#15603F`.
- `globals.css`: `--jp-mark-primary/-accent/-paper` (theme-stable; paper =
  fixed `#FFFFFF` because the rows sit on the green disc, not the page surface);
  spinner CSS removed with the spinner (Phase 2); retired dead `--jp-brand-accent`
  `#FFCD00` (zero consumers, grep-verified); navy ramp annotated consumer-less
  (F-städ).
- `ADR 0070` (new; partially supersedes ADR 0068 Beslut 1 logo-mark note) +
  README index + ADR 0068 status line. `DESIGN.md` §11 + line 66 + design skills
  (principles rule 5, tokens SKILL + references) synced.

## Gates

tsc clean · vitest **868/868** · eslint 0 errors · `next build` green · satori OG
1200×630 + apple 180×180 **runtime-rendered** (verified via `next start` + curl).

- code-reviewer: ✓ Approved (0 Block / 0 Major / 3 Minor — all fixed/non-blocking).
- design-reviewer: motion **approved** (functional status, reduced-motion-safe,
  ADR 0070 Alt E); contrast + token discipline praised. Major 1 (spec-drift)
  **fixed**. Major 2 (spinner ships without consumer + clashes with the
  skeleton-based loading doctrine) **resolved by HOLD** (Klas decision) → Phase 2.
- Reports: `docs/reviews/2026-06-13-logo-code-reviewer.md`,
  `docs/reviews/2026-06-13-logo-design-reviewer.md`.

## Could not run in the remote container

`pnpm visual-verify` (browser header screenshots, light/dark, viewports) — the
Playwright Chromium CDN is blocked by the network policy. The formal in-app
header lockup + wordmark theming verification is **pending Klas's stack**. The
satori OG/apple renders are real in-app evidence of the mark itself.

## Phase 2 (follow-up PR)

`BrandSpinner` "Sigillet i rörelse" (gold arc rotates along the inner ring +
rows pulse sequentially; pure CSS, `prefers-reduced-motion` → static seal;
component prototyped and locked in chat 2026-06-13). Deliver it wired to a real
loading state, with a documented spinner-vs-skeleton usage doctrine, and
visual-verified.
