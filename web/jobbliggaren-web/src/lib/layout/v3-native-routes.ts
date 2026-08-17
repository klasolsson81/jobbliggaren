/**
 * v3-native routes (CTO D1, Variant B 2026-05-19): pages that own their own width
 * layout (edge-to-edge hero + their own `.jp-container`, OR — like /ansokningar after
 * F5 — their own `.jp-container`/`.jp-page` without a hero). Prefix-match, the same
 * idiom as the active-route logic in `app-shell.tsx`. For these routes `AppShell`
 * renders children DIRECTLY in `.jp-content` without the
 * `.jp-shell-transitional-container` — otherwise the hero gets constrained
 * (edge-to-edge breaks) or `.jp-container` is doubled (double max-width/padding on a
 * page that owns its own).
 *
 * REMOVAL TRIGGER: when every `(app)` page has been refactored to its own
 * `.jp-container`/`.jp-page`, this list and `.jp-shell-transitional-container` are
 * removed together (see the globals.css trail, ADR 0052).
 *
 * ⚠ **Opting a prefix in here is a load-bearing obligation, not a formatting choice**,
 * and it prefix-matches: `/cv` opts in every `/cv/**` descendant at once. A page under
 * one of these prefixes that owns no container renders flush to the viewport edge with
 * no max-width and no page padding, because `.jp-content` sets only `flex: 1` and
 * `width: 100%`. That is not a subtle regression — measured on `/cv/granska/[parsedId]`
 * before #1062, the review panel was **3440px wide at a 3440px viewport** with the h1
 * at x=0. Nothing could detect it, which is why this list now lives in its own module:
 * `v3-native-routes.test.ts` reads it and holds every page under these prefixes to the
 * obligation the prefix creates.
 */
export const V3_NATIVE_ROUTES = [
  "/jobb",
  "/ansokningar",
  "/oversikt",
  "/cv",
  // Own their own .jp-container; top-level (not /ansokningar/[id] siblings, so
  // the application-detail modal intercept can't catch them on soft-nav — #316,
  // #332, #313).
  "/aktivitetsrapport",
  "/statistik",
  "/ny-ansokan",
  // #515 — /foretag aligned to the jp-pagehero standard (was legacy /sparade-style by mistake).
  "/foretag",
] as const;

/** True when `pathname` is (or is under) a v3-native route. */
export function isV3Native(pathname: string): boolean {
  return V3_NATIVE_ROUTES.some(
    (r) => pathname === r || pathname.startsWith(r + "/")
  );
}
