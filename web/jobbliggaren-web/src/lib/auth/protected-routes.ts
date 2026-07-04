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

/**
 * Boundary-aware protected-route check (#583).
 *
 * A bare `PROTECTED_PREFIXES.some((p) => pathname.startsWith(p))` match has no
 * segment boundary: the authed prefix `/cv` therefore also swallows the PUBLIC
 * marketing page `/cv-granskning` (`"/cv-granskning".startsWith("/cv") === true`),
 * redirecting a logged-out visitor to `/logga-in`. Matching only on a segment
 * boundary — the exact prefix, or the prefix followed by `/` — protects `/cv` and
 * `/cv/123` while leaving `/cv-granskning` (and any future public sibling that
 * shares an authed prefix) reachable. Today `/cv` is the only real collision; the
 * boundary forecloses the whole class. (The public `/matchning` explainer never
 * collided: it is shorter than the authed `/matchningar` prefix, so even a bare
 * `startsWith` never matched it. It is pinned public below as a guard.)
 *
 * Edge-runtime safe: pure string logic, no Node / `next/headers` imports.
 */
export function isProtectedPath(pathname: string): boolean {
  return PROTECTED_PREFIXES.some(
    (prefix) => pathname === prefix || pathname.startsWith(`${prefix}/`)
  );
}
