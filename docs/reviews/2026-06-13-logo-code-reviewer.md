# Code-review: Logo-översyn "Sigillet" + BrandSpinner (feat/jobbliggaren-mark-spinner)

**Reviewer:** code-reviewer (agent)
**Date:** 2026-06-13
**Status:** ✓ Approved
**Authority:** CLAUDE.md §4 (TS/Next), §5 (anti-patterns), §7 (testing), §1.6 (docs map), §9.2 (boundaries); DESIGN.md §10 (motion), §12 (design-reviewer veto); jobbpilot-design-a11y §6–7
**Scope:** Frontend only (no backend/Domain/Application/CQRS surface touched — review areas 1–3 N/A). Files: 12 (3 brand components +3 tests, globals.css, 5 brand-asset/metadata files, 2 ADR docs).
**Base:** origin/main @ 017eb89. Changes are in the working tree (uncommitted).

---

## Summary up front

Clean, disciplined frontend change. Strict TS throughout, zero `any`, SSOT
geometry respected, all 4 `BrandMarkSvg` consumers migrated to the 2→3 fill
contract with no orphan, mono-fallback documented, tests extended to lock the
new geometry. `BrandSpinner` is correctly an RSC (no `"use client"`, no hooks,
no handlers, no function props → serialization-safe). No Blockers, no Majors.

Three Minors and two delegations. None block merge. The substantive open items
are **not code-quality** issues — they are design-aesthetic (motion stance) and
rendered-UI (`pnpm visual-verify`) calls that belong to design-reviewer and Klas.

---

## Blockers

None.

## Major

None.

## Minor

1. **SSOT contract comment contradicts the implemented token** — File:
   `web/jobbliggaren-web/src/components/brand/brand-mark-svg.tsx:12`
   Current: the color-role comment states
   `paperFill  inre ring + rader  (--jp-mark-paper → --jp-surface, normalt vitt)`.
   Required: the actual token is `--jp-mark-paper: #FFFFFF` (globals.css:56),
   *deliberately NOT* `--jp-surface` — globals.css:56 carries the explicit
   rationale "fast papper, EJ --jp-surface" because the seal sits on its own
   green disc, not the page surface, and must stay theme-stable. This is the
   geometry/contract **SSOT** consumed by 5 files, so a comment that documents
   the opposite of the rationalized behaviour has outsized misleading potential.
   Fix: change `→ --jp-surface, normalt vitt` to `→ #FFFFFF (fast papper, tema-
   stabilt — EJ --jp-surface; se globals.css)`.
   Motivation: CLAUDE.md §9.4 (SSOT accuracy / no drift in the contract source).
   Delegate to: nextjs-ui-engineer.

2. **`role="status"` omits explicit `aria-live`/`aria-atomic`** — File:
   `web/jobbliggaren-web/src/components/brand/brand-spinner.tsx:16`
   Current: `<span role="status">`. `role="status"` carries an *implicit*
   `aria-live="polite"`, so this is functionally announced — but the JobbPilot
   canonical live-region pattern (jobbpilot-design-a11y §6) is explicit:
   `role="status" aria-live="polite" aria-atomic="true"`. Aligning avoids
   relying on implicit-role behaviour and matches the documented house pattern.
   Motivation: jobbpilot-design-a11y §6 (status/count live regions).
   Delegate to: design-reviewer (a11y verdict is hers) / nextjs-ui-engineer to apply.

3. **DESIGN.md §11 drift (note only — do not edit here)** — File:
   `DESIGN.md:188–192`
   §11 still says the logo is "designas senare — inför klass-launch (fas 8)" and
   names "stiliserad kompass" as a direction. ADR 0070 (Accepted, 2026-06-13)
   supersedes both. DESIGN.md is correctly **not** touched in this PR (top-level
   spec edits require explicit Klas instruction, CLAUDE.md §1.6 approval hook),
   so this is drift-to-track, not a fix to apply. The ADR's Accepted status is
   the live authority.
   Motivation: CLAUDE.md §1.6 (docs map / approval hook).
   Delegate to: docs-keeper to log drift; Klas to authorize the §11 spec edit.

---

## Delegations / out-of-scope verdicts (not code-quality findings)

- **Continuous-rotation motion primitive → design-reviewer.** The spinner arc
  uses `jp-spin 1.15s linear infinite` plus a sequential opacity pulse. DESIGN.md
  §10 lists *allowed* animations explicitly (Fade 150ms, Slide 200ms, Opacity
  150ms hover) and forbids "bounce, spring, scale-on-hover, parallax, wiggle". A
  perpetual rotation is on neither list. Whether it fits the civic stance ("rör
  sig inte för att kännas levande") is a DESIGN.md §10/§12 design-aesthetic call
  with veto power — **design-reviewer's**, not code-review's. The CSS is
  correctly built; only the aesthetic fit is open. `prefers-reduced-motion`
  fallback is present and correct (component block sets `animation: none` +
  `opacity: 1`, cleaner than the global 0.01ms/iteration:1 floor for an
  infinite rotation).

- **`pnpm visual-verify` (rendered-UI gate) → Klas/design-reviewer.** Could not
  run in this environment (Playwright CDN blocked). Per
  web/jobbliggaren-web/AGENTS.md this is mandatory for markedly changed rendered
  UI. tsc/vitest(874/874)/eslint/`next build`/satori (apple+OG) are green per
  the author; the browser screenshot loop is the one outstanding gate before
  Klas's approval. Code review ≠ rendered-UI review.

- **ADR 0070 authoring → adr-keeper (already done).** ADR 0070 is drafted,
  Accepted, and indexed; cross-references to 0068 (partial supersede of Beslut 1
  logo-mark-note) and 0069 are wired in both directions. Not this agent's to
  author — noted as a satisfied dependency.

---

## Verified clean (no finding)

- **RSC-by-default correctness (CLAUDE.md §4):** `BrandSpinner`, `BrandLogo`,
  `BrandMarkSvg` — grep-confirmed zero `"use client"` / `useState` / `useEffect`
  / `onClick` / `onChange`. Pure presentational SVG; no function props cross any
  RSC↔Client boundary → no serialization risk. RSC is the correct choice.
- **Strict TS / no `any` (CLAUDE.md §4):** zero `any` in any changed `.tsx/.ts`.
  (`sizes: "any"` in manifest.ts is the W3C Web App Manifest spec literal for a
  scalable icon, not the TS `any` type.)
- **2→3 fill contract migration — no orphan:** all consumers updated —
  brand-logo.tsx (var-tokens), apple-icon.tsx, opengraph-image.tsx,
  twitter-image.tsx (literal hex). `BrandMarkSvgProps` now requires `paperFill`,
  so a missed consumer would fail tsc; build is green → contract is fully
  satisfied. icon.svg is a hand-mirrored file-convention favicon (documented as
  manual-sync, literal hex justified — no CSS context).
- **Literal-hex in satori/favicon contexts is justified and documented:**
  apple-icon/opengraph/twitter/icon.svg run server-side (satori) or as a raw
  favicon where CSS custom properties do not resolve. Using `#15603F/#E8C77B/
  #FFFFFF` literals there is necessary, not a hardcoded-color anti-pattern; each
  file carries a comment explaining the CSS-less context. In-component TSX
  (brand-logo, brand-spinner) correctly uses `var(--jp-mark-*)`. Agreed with the
  author's framing.
- **globals.css token discipline (CLAUDE.md §5):** `--jp-mark-primary/accent/
  paper` defined once (`:root`, lines 54–56); `--jp-gold` defined once (line 50)
  and consumed via `--jp-mark-accent`. Dead `--jp-brand-accent` (#FFCD00 compass
  dot) retired — grep-confirmed zero consumers in src. Navy ramp correctly left
  in place but annotated consumer-less, with retirement deferred to a named
  F-städ PR (not silently deleted — good).
- **`.jp-brand` navy→ink + dark scoped-topbar repoint:** `.jp-brand` color now
  drives only the wordmark (ink-1); the seal owns its fills via `--jp-mark-*`.
  The `[data-theme="dark"] .jp-land-top .jp-brand` override (globals.css:2930)
  correctly locks the wordmark to #0C1A2E (~16:1) on the always-white landing
  topbar. Comment and behaviour are consistent.
- **reduced-motion (DESIGN.md §10, a11y §7):** handled twice — the global
  `prefers-reduced-motion` floor plus a component-specific block that fully
  stops the arc and pins rows to `opacity: 1`. Belt-and-suspenders, correct.
- **No new gradient/glow/shadow/glassmorphism (CLAUDE.md §5):** the spinner adds
  only flat fills + rotation + opacity. No shadow beyond existing tokens, no
  radius violation (SVG geometry). The only sanctioned gradient
  (`--jp-hero-gradient`) is untouched.
- **`sr-only` label (a11y §6):** Tailwind built-in utility (present in compiled
  lib); `<span className="sr-only">{label}</span>` correctly hides the visible
  label while exposing it to screen readers. Default `"Laddar"` is correct
  Swedish UI copy (CLAUDE.md §10 — no emoji, no exclamation).

---

## Bra gjort

- SSOT geometry honoured: `app/icon.svg` is hand-mirrored from `BrandMarkSvg`
  with an explicit "update SSOT first, paste here" sync note — exactly the right
  discipline for a Next.js file-convention asset that can't import the component.
- Test suite extended to **lock the new contract**, not just smoke it: per-shape
  geometry assertions (disc r=45 + primaryFill, ring r=37 stroke=paperFill,
  middle row = accentFill, check-path stroke = primaryFill), 2-circle/3-rect/
  1-path counts on both `BrandMarkSvg` and `BrandLogo`, and spinner tests for
  role, default + custom label, `aria-hidden`, arc dash-array, staggered row
  classes, and size propagation. This satisfies CLAUDE.md §7 for the new
  component and the changed contract.
- Theme-stability reasoning for `--jp-mark-paper` is genuinely correct and
  well-rationalized in globals.css (the seal carries its own surface, so paper
  must not follow `--jp-surface` into dark) — the only miss is that the SSOT
  *component* comment (Minor 1) doesn't echo that same rationale.
- Dead token retired with grep-verified zero consumers; consumer-less navy ramp
  annotated and deferred to a named cleanup phase rather than deleted in-scope.
  Disciplined branch-by-abstraction hygiene.

---

## Sammanfattning

**0 Blockers · 0 Major · 3 Minor.** Approved for merge from a code-quality
standpoint. The three Minors are non-blocking (one SSOT comment fix →
nextjs-ui-engineer; one explicit-`aria-live` alignment → design-reviewer/
nextjs-ui-engineer; one DESIGN.md §11 drift to log → docs-keeper, no edit here).

Two real gates remain outside code-review's authority and must clear before
final sign-off: (1) **design-reviewer's verdict on the continuous-rotation
motion primitive** (DESIGN.md §10 allow-list does not cover perpetual rotation —
veto territory), and (2) **`pnpm visual-verify` rendered-UI review + Klas
approval** (could not run here; the one outstanding mandatory gate per
AGENTS.md). Re-review not required for the Minors; design-reviewer pass is.
