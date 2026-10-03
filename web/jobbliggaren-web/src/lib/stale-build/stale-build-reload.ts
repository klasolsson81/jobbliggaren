import { unstable_isUnrecognizedActionError } from "next/navigation";
import { reloadDocument } from "./reload-document";

/**
 * A stale page never executes a stale Server Action (ADR 0148, #1948).
 *
 * Every CI build of the web image gets its own Server Action ids (Next salts
 * them with a per-build key), and the box replaces the image within hours of
 * every merge. A page rendered before that replacement answers its next
 * Server Action with a 404 and `x-nextjs-action-not-found: 1`, which the
 * router turns into an `UnrecognizedActionError` on the client — the action
 * never ran. The remedy is to reload the document once, so the page gets the
 * build the server runs, and to say so after the reload (the notice reads the
 * second stamp).
 *
 * Two guards, both fail-closed (ADR 0148 D3): a reload needs a readable AND a
 * writable `sessionStorage` stamp, because a loop breaker that cannot keep its
 * state must not act. Within the window a second stale error shows the error
 * surface instead, whose "Försök igen" reloads into the new build by itself
 * (#1949, E5b′). Both values are timestamps and nothing else.
 */
export const STALE_BUILD_RELOAD_STAMP_KEY = "jp-stale-build-reload-at";
export const STALE_BUILD_RELOADED_NOTICE_KEY = "jp-stale-build-reloaded-at";
export const STALE_BUILD_RELOAD_WINDOW_MS = 60_000;

/** The router's own class, by `instanceof` — never a message or a digest. */
export function isStaleBuildActionError(error: unknown): boolean {
  return unstable_isUnrecognizedActionError(error);
}

/**
 * Reads only. The predicate comes first, so a server-thrown error (never an
 * `UnrecognizedActionError`) touches no storage and the call is safe during
 * SSR. Then the stamp: unreadable storage → false; a corrupt stamp counts as
 * absent; a stamp younger than the window → false.
 */
export function mayReloadForStaleBuild(error: unknown, now: number): boolean {
  if (!isStaleBuildActionError(error)) return false;
  let raw: string | null;
  try {
    raw = window.sessionStorage.getItem(STALE_BUILD_RELOAD_STAMP_KEY);
  } catch {
    return false;
  }
  if (raw === null) return true;
  const stampedAt = Number(raw);
  if (!Number.isFinite(stampedAt)) return true;
  return now - stampedAt >= STALE_BUILD_RELOAD_WINDOW_MS;
}

/**
 * Writes both stamps, then reloads. When a write throws, nothing is reloaded
 * and the caller shows its surface instead (fail-closed on the write too).
 */
export function stampAndReload(now: number): boolean {
  try {
    window.sessionStorage.setItem(STALE_BUILD_RELOAD_STAMP_KEY, String(now));
    window.sessionStorage.setItem(STALE_BUILD_RELOADED_NOTICE_KEY, String(now));
  } catch {
    return false;
  }
  reloadDocument();
  return true;
}

/**
 * For a call site that catches its action's rejection instead of letting it
 * reach a boundary (`match-preferences-card.tsx`). True means the document is
 * being replaced: the caller returns and renders no inline error.
 */
export function reloadIfStaleBuild(error: unknown, now: number = Date.now()): boolean {
  return mayReloadForStaleBuild(error, now) && stampAndReload(now);
}
