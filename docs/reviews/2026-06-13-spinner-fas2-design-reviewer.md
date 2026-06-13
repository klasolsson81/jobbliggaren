# design-reviewer — Spinner Fas 2 (BrandSpinner + ModalLoadingShell), 2026-06-13

**Verdict:** ✓ Approved-with-Minor — **0 Blocker / 0 Major / 3 Minor**.
**Major 2 (spinner-without-consumer): formally CLOSED.**
**FAS-DEFERRAL-MANIFEST:** not required — rendered verification was provided
(four screenshots), so the spinner is reviewed directly; nothing deferred.

Rendered review against the four screenshots in `C:/tmp/jobbliggaren-spinner-verify/`
(light/dark × motion/reduced-motion) + source + CSS diff + the two test files.

## Minor

1. `aria-modal="true"` on a non-interactive fallback without focus-trap/inert —
   semantically untrue for a transient surface; dropping is marginally more
   honest (no WCAG AA violation either way). → **FIXED in-block** (`aeb443d`):
   aria-modal dropped; role=dialog + aria-busy + aria-label kept.
2. Dialog name + the spinner's `role=status` live region carry the same string
   (mild double-announce; not an SC violation — they serve different AT modes).
   → **Retained**: the specific status text in the live region is more
   informative than a generic label, and focus stays in the background during
   the transient fallback so the dialog name is rarely read.
3. `.jp-modal-loading__text` `font-size: 15px` is off the type scale. →
   **Retained**: matches the existing `.jp-modal__company` (15px); the `.jp-*`
   CSS system uses raw px, not scale tokens.

## Verified good

- Mark contract holds in dark (`spinner-dark-*.png`): seal stays green
  (`--jp-mark-primary` not dark-shifted), gold row + paper ring intact — ADR 0070.
- Reduced-motion: complete static seal (rows opacity 1, arc at rest), not a
  frozen mid-frame.
- Zero AI-aesthetic in the CSS diff (no gradient/glow/blur/drop-shadow/hardcoded
  hex; only `var(--jp-ink-2)`). Depth from the sanctioned `--jp-shadow-modal`.
- Reuses `jp-modal-scrim`/`jp-modal` (radius `--jp-r-lg` = ADR 0052-sanctioned),
  `min-height:220px` reserves height → no CLS. Pure RSC.
- Copy civic-correct: Unicode ellipsis, no exclamation, no emoji, no AI cliché
  (`jobbpilot-design-copy` §4).
- Doctrine documentation closes Major 2: the spinner now has a real,
  doctrine-justified consumer (loading.tsx → ModalLoadingShell → BrandSpinner).

## Scope

Fas 2 (spinner wiring) only — no scope-creep into the header-lockup (UPPGIFT B).
