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
 * (`jp-pagehero`/`__inner`/`__main`/`__aside`) as the real pages. The layout
 * (gradient plate, padding, flex) therefore matches automatically and the swap
 * to real content does not shift (CLS).
 *
 * Flat neutral grey `.jp-skeleton` blocks sized with Tailwind utilities, no
 * pulse/shimmer/glow (civic-utility, mirrors JobAdListSkeleton/AuthCardSkeleton).
 * `aria-hidden`: the announce to assistive tech is owned by the route
 * `loading.tsx` (an sr-only `role="status"`), so the visual shape stays
 * decorative. Sync RSC (no interactivity).
 *
 * `aside` overrides the right-hand block for pages whose header aside is not two
 * buttons (e.g. Översikt renders a card there); default mirrors the common
 * two-action pagehero (Ansökningar/CV).
 */
export function PageHeroSkeleton({ aside }: { aside?: ReactNode }) {
  return (
    <section className="jp-pagehero" aria-hidden="true">
      <div className="jp-pagehero__inner">
        <div className="jp-pagehero__main">
          <span className="jp-skeleton block h-11 w-64 max-w-full" />
          <span className="jp-skeleton mt-2 block h-4 w-96 max-w-full" />
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
