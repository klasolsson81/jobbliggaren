import type { ReactNode } from "react";

/**
 * Skeleton for the shared `.jp-pagehero` band (#739 — route-level loading
 * skeletons). Reproduces the pagehero envelope so a navigation click paints the
 * page's gradient header shape immediately, before the dynamic RSC render
 * resolves — instead of the old page freezing (the P0 dead-click finding
 * `p1-no-loading-tsx-any-primary-route`).
 *
 * There is no shared PageHero React component — the `.jp-pagehero` markup is
 * inlined per page — so this skeleton re-uses the SAME structural classes
 * (`jp-pagehero`/`__inner`/`__main`/`__aside`) as the real pages, and the layout
 * (gradient plate, padding, flex) matches automatically.
 *
 * ⚠ **It does not follow that the swap never shifts, and this docblock used to claim it
 * did.** The claim was measured false (#1062, design-reviewer M-A): the lede was one 16px bar
 * here, and a lede that wraps to three lines renders 74.4px, so the band grew 168 → 231px on
 * `/cv/granska/[parsedId]` and 38px on `/cv`. Measured CLS stayed **0** — a route swap replaces
 * nodes rather than moving them, so this was visible jumpiness and not an ADR 0045 regression —
 * but "matches the envelope" is the honest claim, not "does not shift".
 *
 * `ledeLines` closes most of it: pass the number of lines the real lede wraps to at the
 * narrowest viewport it is measured on. What remains on a 3-line lede is **14.8px**, and the
 * decomposition matters because the obvious guess is wrong — only 4.4px is the title bar
 * (`h-11` = 44px against a rendered 48.4px); **10.4px is these lede bars**, whose
 * `16px + 8px` rungs do not sum to the paragraph's `8px + 3 × 24.8px`. Closing it means
 * matching the real line box, not raising the title default.
 *
 * Flat neutral grey `.jp-skeleton` blocks sized with Tailwind utilities, no
 * pulse/shimmer/glow (civic-utility, mirrors JobAdListSkeleton/AuthCardSkeleton).
 * `aria-hidden`: the announce to assistive tech is owned by the route
 * `loading.tsx` (an sr-only `role="status"`), so the visual shape stays
 * decorative. Sync RSC (no interactivity).
 *
 * `aside` overrides the right-hand block for pages whose header aside is not two
 * buttons (e.g. Översikt renders a card there); default mirrors the common
 * two-action pagehero (Ansökningar/CV). `kicker` adds the mono overline row that
 * Översikt renders above its title (`.jp-pagehero__kicker`), so the band height
 * matches on those pages (the plate is `align-items: flex-start`, so a missing
 * row would let the band grow on swap).
 */
export function PageHeroSkeleton({
  aside,
  kicker = false,
  ledeLines = 1,
}: {
  aside?: ReactNode;
  kicker?: boolean;
  /** How many lines the real lede wraps to. Default 1 preserves every existing call site. */
  ledeLines?: 1 | 2 | 3;
}) {
  return (
    <section className="jp-pagehero" aria-hidden="true">
      <div className="jp-pagehero__inner">
        <div className="jp-pagehero__main">
          {kicker && <span className="jp-skeleton mb-2 block h-3 w-24" />}
          <span className="jp-skeleton block h-11 w-64 max-w-full" />
          {Array.from({ length: ledeLines }, (_, line) => (
            <span
              key={line}
              // The last line of a wrapped paragraph is short; matching that keeps the
              // shape honest without changing the reserved height.
              className={`jp-skeleton mt-2 block h-4 max-w-full ${
                line === ledeLines - 1 && ledeLines > 1 ? "w-64" : "w-96"
              }`}
            />
          ))}
        </div>
        <div className="jp-pagehero__aside">
          {aside ?? (
            <>
              <span className="jp-skeleton block h-10 w-32" />
              <span className="jp-skeleton block h-10 w-28" />
            </>
          )}
        </div>
      </div>
    </section>
  );
}
