# code-reviewer — Logo Fas 3 header-lockup + tagline, 2026-06-13

**Verdict:** ✓ Approved — **0 Blocker / 0 Major / 0 Minor**.
Scope: frontend only (brand component + globals.css + tests + ADR doc).

## Verified

- **RSC boundary:** no change. `brand-logo.tsx`/`brand-mark-svg.tsx`/
  `landing-topbar.tsx`/`site-header.tsx` are RSC; `app-shell.tsx`/`guest-shell.tsx`
  are `"use client"` but render `<BrandLogo />` (no props at any call site) as a
  child — pure component, nothing non-serializable crosses the boundary. Additive
  markup/CSS inside an already-RSC component.
- **API change safe:** default `markSize` 32 → 40 affects all BrandLogo renders,
  but `variant="mark"` has zero production usages (tests only), so only the four
  header `full` usages (all wanting the larger size) change.
- **CSS height bump non-coupled:** both `.jp-header__inner` + `.jp-land-top__inner`
  are `align-items: center` flex rows; no sibling (nav, jp-header-stats, actions,
  login link) couples height to the container. header-stats is independently
  centered + hidden ≤900px. The 88px just re-centers the taller lockup.
  site-header shares `.jp-land-top__inner` → same 88px (intended per ADR amendment).
- **Tokens:** tagline uses `--jp-ink-2` (#455366), the canonical secondary-text
  token (not hardcoded hex, not the forbidden ink-3).
- **Verbatim consistency:** tagline matches `opengraph-image.tsx:63` exactly; OG
  provenance (Klas-STOPP A val H2 2026-05-25) cited correctly.
- **Tests:** 7 meaningful (lockup present, tagline text + aria-hidden, mark variant
  has neither, default 40). Coverage not lowered.
- **Comments English** (§1); naming clean; no anti-patterns.
- **Commit hygiene:** 2 conventional commits, one change-reason, feat + docs
  amendment in the same PR (ADR 0065). Worktree clean (only pre-existing untracked
  `loadtest-reports/`); no prototype residue.
- **ADR amendment** well-scoped: defers the DESIGN.md §11 mirror to Klas
  approve-spec-edit rather than touching DESIGN.md here.

## Note (not a finding)

12px tagline is off the semantic type scale (min token `text-caption` 13px), but
consistent with the brand/header CSS carve-out that sets type values directly
(`jp-header-stats__label` 10.5px, `jp-brand__word` 24px). No new deviation;
tokenising the brand subsystem is a separate cleanup, not this PR.

## Process gates (not code)

`pnpm build` (RSC runtime) + design-reviewer rendered pass — both required per
AGENTS.md. Build run green as the final pre-push gate; design-reviewer Approved.
