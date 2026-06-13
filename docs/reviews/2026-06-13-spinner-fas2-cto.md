# senior-cto-advisor — Spinner Fas 2 loading-chrome approach (2026-06-13)

**Role:** decision-maker (CC gave no recommendation, CLAUDE.md §9.2).
**Decision:** **Variant A** — self-contained pure-RSC `ModalLoadingShell`.

## Context

Logo Fas 2 (ADR 0070): wire `BrandSpinner` to a real known-slow formless wait —
the `@modal` intercepting-route loading state — closing design-reviewer Major 2.
Doctrine (`project_spinner_usage_doctrine`): spinner only for >1–2s formless
waits; open the surface instantly + spinner + Swedish status line; skeleton
stays the default for content. First consumers Klas named: job-ad modal +
saved-application modal.

## The problem

`loading.tsx` in each intercepting-route dir is Next's `<Suspense>` fallback
while the server detail streams. The content shells
(`JobAdModalShell`/`ApplicationModalShell`) require `title`/`company` that do not
exist during loading.

## Variants weighed

- **A — self-contained RSC loading-chrome** (chosen). New `ModalLoadingShell`
  reuses `jp-modal-scrim`/`jp-modal` + BrandSpinner + status line; the working
  shells untouched.
- **B — optional title + reuse the client shell.** Rejected: breaks SRP (two
  responsibilities in one file), forces `"use client"` into the fallback path
  (defeats RSC streaming — the "paints instantly" property), grows the state
  space of an already-reviewed a11y component.
- **C — extract a shared `ModalChrome` base.** Rejected for this PR: largest
  blast radius (2 shells + 4 page.tsx), and a second change-reason in a
  spinner-wiring PR (memory `feedback_one_concern_per_pr_soc`). The "rule of
  three" the code flags has NOT triggered — the loading surface shares *markup*,
  not *behaviour* (no ESC/focus-trap/scrim-dismiss). Not a TD; a future
  opportunistic touch if a genuine third *interactive* modal context appears.

## Sub-decisions

1. Non-interactive fallback acceptable for <1–2s (no duplicated dismiss
   mechanics); the real shell mounts right after with full a11y.
2. RSC, not client — decisive against B (streams in the static shell, no
   hydration to paint).
3. No footer during loading (YAGNI; nothing to act on before data).
4. All four loading.tsx (auth + guest) — symmetry + a no-auth visual-verify
   path; Klas's prompt requested all four (escalated, but pre-answered).
5. Change-reason discipline is a top reason to reject C.

## Note

Prompt premise vs HEAD: BrandSpinner did not exist on `8b4e2cf` — this PR
introduces it (via `spinner.patch`) *and* wires it. Still one change-reason
("introduce-and-wire BrandSpinner").

## Principles

Fowler "rule of three" (markup ≠ behaviour); Martin SRP/OCP; Hunt/Thomas DRY as
knowledge-piece not text-likeness; Next RSC streaming contract; CLAUDE.md §9.2 /
§9.6.
