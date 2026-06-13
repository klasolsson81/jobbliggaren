# Session log 2026-06-13 — Logo-översyn Fas 3: header-lockup + tagline

## Scope

Second PR of the session (after spinner Fas 2, #72). Klas: our wordmark/mark is
much smaller than Platsbanken's header. Enlarge the header brand lockup and add
the tagline "Den svenska jobbansökningshanteraren" as a subline under the
wordmark, like Platsbanken's "SWEDISH PUBLIC EMPLOYMENT SERVICE".

Separate PR, own change-reason (SoC). Branch `feat/header-lockup-tagline` from
main `18f037f` (after #72 merged).

## Design decision — Klas chose (visual-verify gate, not built blind)

CC prototyped TWO variants on the running stack (temp edits, reverted) and
visual-verified them on the no-auth landing topbar, then presented for Klas's
choice (AskUserQuestion):

- **Variant A — sentence-case tagline (OG-consistent):** "Den svenska
  jobbansökningshanteraren" in normal case, 12px, muted ink-2 — matches
  `opengraph-image.tsx` exactly. **← Klas chose this.**
- Variant B — uppercase letter-spaced (Platsbanken-faithful): rejected.
- Scope: **both headers** (Klas chose) — app-shell + landing/guest.

Screenshots: `C:/tmp/jobbliggaren-header-verify/` (baseline / A / B) +
`C:/tmp/jobbliggaren-header-final/` (committed implementation).

## Delivered (2 logical commits, one PR)

- **`8726d60`** — `feat(web)`: BrandLogo `full` variant → header lockup (mark +
  stacked [wordmark / tagline]); tagline aria-hidden (the `.jp-brand` Link carries
  the accessible name); default markSize 32 → 40. `mark` variant unchanged.
  globals.css: `.jp-brand__lockup` + `.jp-brand__tagline` (12px, 500, ink-2,
  sentence-case); `.jp-brand__word` 19 → 24px; both header bars 68 → 88px
  (`.jp-header__inner` = app + guest shell; `.jp-land-top__inner` = landing +
  site-header). Applies to all four brand headers via BrandLogo. brand-logo.test
  updated (7 tests).
- **`fcbf567`** — `docs(decisions)`: ADR 0070 Fas 3 amendment (header-lockup +
  tagline; records the variant choice + brand-spec change).

## Gates

- tsc clean · vitest **882/882** (880 main baseline + 2 brand-logo: default
  markSize 40 + tagline) · eslint 0 errors · `next build` green.
- **visual-verify** on the running stack (landing topbar, light + dark) — the
  committed implementation matches the Klas-approved Variant A.

## Reviews

- **design-reviewer** ✓ Approved (0/0/0) — drove the dark-mode contrast risk to
  ground: the scoped-white headers re-pin `--jp-ink-2` → #455366 (~7.8:1 AAA on
  white) in both themes, so the tagline is readable in dark too. a11y/tokens/civic
  all sound. `docs/reviews/2026-06-13-logo-fas3-header-design-reviewer.md`.
- **code-reviewer** ✓ Approved (0/0/0) — RSC clean, API default change safe (mark
  variant has no prod usages), height bump non-coupled, ADR amendment well-scoped.
  `docs/reviews/2026-06-13-logo-fas3-header-code-reviewer.md`.
- **test-writer** — updated brand-logo tests (7).

No CTO — Klas chose the variant directly (a product/aesthetic preference, not a
multi-approach architecture decision).

## Pending

- Klas approve-spec-edit: mirror a one-line header-lockup note into DESIGN.md §11.
- Future F-städ: redundant `[data-theme="dark"] .jp-land-top .jp-brand` color
  override (design-reviewer note, out of scope here).
