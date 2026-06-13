# Session log 2026-06-13 — Logo-översyn Fas 2: BrandSpinner wiring

## Scope

Follow-up PR to the Sigillet brand mark (ADR 0070 Fas 1, #71). Land the
`BrandSpinner` ("Sigillet i rörelse") and wire it to a real loading state,
closing the design-reviewer **Major 2 (spinner-without-consumer)** that was
deliberately held in Fas 1. Plus document the spinner-vs-skeleton doctrine so
Major 2 is formally closed.

Branch `feat/brand-spinner-wiring` from main `8b4e2cf`.

## The central design decision (senior-cto-advisor, Variant A)

The seam: the four `@modal` intercepting routes (`(app)` + `(guest)` × jobb +
ansökningar) suspend on their server fetch; a `loading.tsx` placed in each dir
is Next's `<Suspense>` fallback while the detail streams = "open the empty modal
instantly + spinner". The problem: the content shells
(`JobAdModalShell`/`ApplicationModalShell`) require `title`/`company` that do not
exist yet during loading.

CC presented three variants (A: self-contained RSC loading-chrome; B: optional
title + reuse the client shell; C: extract a shared `ModalChrome` base — the
"rule of three" the `ApplicationModalShell` comment flags). senior-cto-advisor
**chose Variant A**:

- **A** — a self-contained pure-RSC `ModalLoadingShell` reuses the existing
  `jp-modal-scrim`/`jp-modal` surface + `BrandSpinner` + a Swedish status line.
  The two working shells are NOT touched.
- Sub-decisions: (1) non-interactive fallback OK for <1–2s (no ESC/scrim-dismiss
  duplicated); (2) RSC, not client (streams in the static shell, no hydration —
  the whole point of "paints instantly"); (3) no footer during loading;
  (4) all four loading.tsx (symmetry + a no-auth visual-verify path — Klas's
  prompt requested all four); (5) change-reason discipline is a top reason to
  reject C (memory `feedback_one_concern_per_pr_soc`).
- C is NOT a TD — it is a future opportunistic touch if a genuine third
  *interactive* modal context appears (rule of three on behaviour, not markup).

Report: `docs/reviews/2026-06-13-spinner-fas2-cto.md`.

## Delivered (4 logical commits, one PR)

- **`682dcc9`** — `BrandSpinner` (cloud-authored delta applied via `git apply
  spinner.patch`, byte-exact, verified clean against `8b4e2cf`): `brand-spinner.tsx`
  + test (6 cases) + `globals.css` spinner block. Pure RSC + CSS, role=status,
  aria-live=polite, prefers-reduced-motion → static seal. Uses `--jp-mark-*`.
- **`7de127a`** — wiring: `ModalLoadingShell` (+test, 6 cases) + four `loading.tsx`
  + `globals.css` `.jp-modal--loading` (stable min-height → no CLS) +
  `.jp-modal-loading`. Texts: "Jobbannonsen läses in…" / "Ansökan läses in…".
- **`cd865ce`** — spinner-vs-skeleton doctrine in the `jobbpilot-design-components`
  skill (Skeleton = default ~90%; BrandSpinner = narrow known-slow formless
  exception, names the consumers). Closes Major 2. (DESIGN.md §11 is
  approval-gated → skill is the canonical reference; one-line §11 mirror pending
  a Klas approve-spec-edit run.)
- **`aeb443d`** — review minors: drop `aria-modal` from the non-inert transient
  fallback (design-reviewer); doctrine-reference precision (code-reviewer); new
  comments → English (§1).

## Gates

- tsc clean · vitest **880/880** (868 baseline + 6 BrandSpinner + 6
  ModalLoadingShell) · eslint 0 errors (5 pre-existing warnings) · `next build`
  green (intercepting routes compile).
- **visual-verify** (the gate cloud could not run — Playwright Chromium CDN was
  blocked there): run locally on the running stack via a temp one-off Playwright
  script on the no-auth guest path (`/gast/jobb` → click row → intercepting
  modal). Captured light/dark × motion/reduced-motion; runtime assertions
  confirmed `role=dialog[aria-busy]`, `.jp-brand-spinner` visible, text
  "Jobbannonsen läses in…". Reduced-motion shows the static seal. Screenshots:
  `C:/tmp/jobbliggaren-spinner-verify/`. The temp server-side delay used to hold
  the fallback visible (guest mock is synchronous) was reverted; the temp script
  was deleted (never committed).

## Reviews

- **design-reviewer** ✓ Approved-with-Minor (0 Blocker / 0 Major / 3 Minor) —
  Major 2 **formally closed**; mark contract + reduced-motion rendered-verified
  in both themes; no FAS-DEFERRAL-MANIFEST needed (rendered evidence provided).
  `docs/reviews/2026-06-13-spinner-fas2-design-reviewer.md`.
- **code-reviewer** ✓ Approved (0 Blocker / 0 Major / 2 Minor) — RSC boundary
  clean, a11y contract sound, DRY/token discipline held, SoC respected, no temp
  verification residue. `docs/reviews/2026-06-13-spinner-fas2-code-reviewer.md`.
- **test-writer** — authored the 6 ModalLoadingShell tests.

Minor triage (§9.6): 3 fixed in-block (aria-modal, doctrine ref, comment
language); 2 retained with rationale (role=status keeps the specific status text
> generic; 15px matches the existing `.jp-modal__company`).

## Operational

- FE dev server (:3000) went stale mid-session (Jest-worker crash overlay,
  "Next.js 16.2.7 stale") — root cause: `pnpm build` clobbered the running dev
  server's `.next` (memory `feedback_stale_devserver_jest_worker_mask`). Clean
  restart: killed PID, removed `.next`, restarted `pnpm dev` in background; the
  green prod build proved the code was fine. Stack left running per
  `feedback_restart_stack_after_commit_stop`.

## Pending

- Klas approve-spec-edit: mirror a one-line spinner-vs-skeleton pointer into
  DESIGN.md §11 (brand section).
- UPPGIFT B (separate PR, SoC): header-lockup + tagline enlargement (logo Fas 3).
