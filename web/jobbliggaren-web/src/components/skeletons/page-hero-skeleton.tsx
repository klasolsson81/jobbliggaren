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
 * here, and a lede that wraps to three lines renders taller, so the band grew on
 * `/cv/granska/[parsedId]` and on `/cv`. Measured CLS stayed **0** — a route swap replaces
 * nodes rather than moving them, so this was visible jumpiness and not an ADR 0045 regression —
 * but "matches the envelope" is the honest claim, not "does not shift".
 *
 * **`title`/`lede` close it.** A pagehero title and lede are static translations, so a
 * fallback can render the REAL text and let the browser do the wrapping — then the band
 * cannot disagree with the page at any viewport. Where the copy is genuinely unknown at
 * fallback time the default bars stand in, and reserve one lede line.
 *
 * Flat neutral grey `.jp-skeleton` blocks sized with Tailwind utilities, no
 * pulse/shimmer/glow (civic-utility, mirrors JobAdListSkeleton/AuthCardSkeleton).
 * `aria-hidden`: the announce to assistive tech is owned by the route
 * `loading.tsx` (an sr-only `role="status"`), so the visual shape stays
 * decorative. Sync RSC (no interactivity).
 *
 * `aside` overrides the right-hand block for pages whose header aside is not two
 * buttons (e.g. Översikt renders a card there); **`null` renders no `__aside` element at
 * all**, which an empty node cannot do — `.jp-pagehero__inner` is a wrapping flex row, so
 * an empty aside costs nothing beside `__main` but takes a whole line plus the row `gap`
 * once it wraps, and the band then over-reserves at exactly the narrow widths a hero with
 * no aside is most sensitive at (#1385). `kicker` adds the mono overline row that
 * Översikt renders above its title (`.jp-pagehero__kicker`), so the band height
 * matches on those pages (the plate is `align-items: flex-start`, so a missing
 * row would let the band grow on swap).
 *
 * `asideClassName` appends a page's own aside MODIFIER, and children alone cannot
 * substitute for it (#1467): `.jp-pagehero__aside--stacked` sets `flex-direction: column`
 * and, under `@media (max-width: 720px)`, `width: 100%` + `align-items: stretch`. A
 * fallback that passes stacked rows into the unmodified base class lays them out as a
 * wrapping ROW at every width, so the band disagrees with the page by a whole row. The
 * prop carries the modifier, not arbitrary styling: the real pages compose the same two
 * class names on the same element, and `ansokningar/loading.tsx` records what that cost
 * when it did not.
 */
export function PageHeroSkeleton({
  aside,
  asideClassName,
  kicker = false,
  title,
  lede,
}: {
  aside?: ReactNode;
  /** The page's own `.jp-pagehero__aside--*` modifier, appended to the base class. */
  asideClassName?: string;
  kicker?: boolean;
  /** The page's real title. Given, it is rendered instead of the title bar. */
  title?: string;
  /** The page's real lede. Given, it is rendered instead of the lede bar. */
  lede?: string;
}) {
  return (
    <section className="jp-pagehero" aria-hidden="true">
      <div className="jp-pagehero__inner">
        <div className="jp-pagehero__main">
          {kicker && <span className="jp-skeleton mb-2 block h-3 w-24" />}
          {title === undefined ? (
            <span className="jp-skeleton block h-11 w-64 max-w-full" />
          ) : (
            <h1 className="jp-pagehero__title">{title}</h1>
          )}
          {lede === undefined ? (
            <span className="jp-skeleton mt-2 block h-4 w-96 max-w-full" />
          ) : (
            <p className="jp-pagehero__lede">{lede}</p>
          )}
        </div>
        {aside !== null && (
          <div
            className={
              asideClassName
                ? `jp-pagehero__aside ${asideClassName}`
                : "jp-pagehero__aside"
            }
          >
            {aside ?? (
              <>
                <span className="jp-skeleton block h-10 w-32" />
                <span className="jp-skeleton block h-10 w-28" />
              </>
            )}
          </div>
        )}
      </div>
    </section>
  );
}
