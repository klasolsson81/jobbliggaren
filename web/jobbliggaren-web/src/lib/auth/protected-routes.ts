/**
 * Authenticated route prefixes for the `(app)` route group (ADR 0017 defense-in-depth).
 *
 * INVARIANT: this list MUST mirror the top-level URL segments of `src/app/(app)/`.
 * Every `(app)` route renders behind `AppLayout`, which re-verifies the session
 * server-side (`getServerSession`) and redirects unauthenticated users. The edge
 * middleware uses this list as a cheap first gate (cookie presence) so an
 * unauthenticated deep-link is redirected to `/logga-in?next=<pathname>` before it
 * reaches the Server Component.
 *
 * The invariant is frozen by `protected-routes.test.ts`, which derives the expected
 * set by reading the `(app)` directory — so adding an `(app)` route without listing
 * it here (or leaving a stale prefix) fails CI. See #513.
 *
 * Edge-runtime safe: pure data, no Node / `next/headers` imports (the middleware
 * this feeds runs on the edge runtime).
 */
export const PROTECTED_PREFIXES = [
  "/aktivitetsrapport",
  "/ansokningar",
  "/cv",
  "/foretag",
  "/installningar",
  "/jobb",
  "/matchningar",
  "/ny-ansokan",
  "/oversikt",
  "/sokningar",
  "/sparade",
  "/statistik",
] as const;
