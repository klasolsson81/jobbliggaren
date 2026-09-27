import { useTranslations } from "next-intl";
import { PageHeroSkeleton } from "@/components/skeletons/page-hero-skeleton";

// The queue shows at most four rows before "Visa N till" (VISIBLE_ROW_CAP).
const QUEUE_ROWS = [0, 1, 2, 3] as const;
const RAIL_CELLS = [0, 1, 2, 3, 4, 5, 6, 7, 8, 9] as const;
const GROUPS = [0, 1] as const;
const GROUP_ROWS = [0, 1] as const;

/**
 * Route-level loading state for /ansokningar (#739 — finding
 * `p1-no-loading-tsx-any-primary-route` P0). Paints the pagehero + the
 * applications list's shape immediately on navigation, instead of freezing
 * the previous page.
 *
 * Re-uses `jp-pagehero` and the list's own structural classes. Scoped to the `(list)` route
 * group, so it is never the fallback of `/ansokningar/[id]` (the `cv/(hub)` precedent,
 * #1385). sr-only `role="status"` announces; visuals decorative. Sync RSC.
 *
 * **The band this file reserves used to model a page layout that no longer exists**
 * (#1467, measured 2026-08-23 at `173e767c`: the hero grew by up to 202px on swap, worst
 * at 375–414). It reserved ONE row-shaped block in the aside and drew the secondary
 * actions as a separate right-aligned row BELOW the hero — but `page.tsx` puts all three
 * controls INSIDE the aside, in two `__btnrow`s under the `--stacked` modifier. So the
 * fallback under-reserved the band and over-reserved beneath it, in the same swap.
 *
 * It now mirrors that structure: the real title (a static translation, so the
 * browser wraps it exactly as the page does — `cv/(hub)/loading.tsx` is the precedent),
 * the `--stacked` modifier via `stacked`, and both rows at the `.jp-btn` height.
 *
 * ⚠ **The bar widths approximate rather than mirror**, and that is where this file can
 * still disagree with the page. Row 2's COMBINED width decides where it wraps, and the
 * wrap is what the band's height is made of — but a fixed bar stands in for a control
 * whose width follows its label, so the two thresholds cannot coincide at every width,
 * and they move apart again in a locale whose labels are longer.
 * The residual is a narrow viewport band around the wrap transition, measured and named in
 * the PR that closes #1467; re-measure it rather than reasoning about it if these labels
 * change.
 */
export default function Loading() {
  const t = useTranslations("pages");
  return (
    <>
      <span role="status" aria-live="polite" aria-busy="true" className="sr-only">
        {t("navLoading.ansokningar")}
      </span>

      <PageHeroSkeleton
        title={t("ansokningar.title")}
        lede={null}
        stacked
        aside={
          <>
            <div className="jp-pagehero__btnrow">
              <span className="jp-skeleton block h-11 w-36" />
            </div>
            <div className="jp-pagehero__btnrow">
              <span className="jp-skeleton block h-11 w-28" />
              <span className="jp-skeleton block h-11 w-64" />
            </div>
          </>
        }
      />

      {/* The list's bound form in its own classes (#1827 M6): the queue's ledger rows, the
          controls, the rail's ten cells, and group heads over framed Lista rows. The rows'
          classes carry hover states, which a placeholder must not answer. */}
      <div className="jp-container jp-page pointer-events-none" aria-hidden="true">
        <section className="jp-attentionqueue">
          <div className="jp-section__head jp-section__head--strong">
            <span className="jp-skeleton block h-6 w-40" />
          </div>
          <ol className="jp-attentionqueue__list">
            {QUEUE_ROWS.map((row) => (
              <li key={row} className="jp-attentionqueue__row">
                <div className="jp-attentionqueue__body">
                  <span className="jp-skeleton block h-3 w-40" />
                  <span className="jp-skeleton mt-2 block h-6 w-80 max-w-full" />
                </div>
                <span className="jp-skeleton block h-9 w-36 [@media(max-width:768px)]:h-11" />
              </li>
            ))}
          </ol>
        </section>

        <section className="jp-allapps">
          <div className="jp-section__head jp-section__head--strong">
            <span className="jp-skeleton block h-6 w-48" />
          </div>
          <div className="jp-appcontrols">
            <div className="jp-appcontrols__search">
              <span className="jp-skeleton block h-(--jp-control-h) w-full" />
            </div>
            <div className="jp-appcontrols__vy">
              <span className="jp-skeleton block h-(--jp-control-h) w-56" />
            </div>
          </div>
          <div className="jp-steprail">
            <div className="jp-steprail__labelrow">
              <span className="jp-skeleton block h-5 w-32" />
            </div>
            <div className="jp-steprail__cells">
              {RAIL_CELLS.map((cell) => (
                <div key={cell} className="jp-steprail__cell">
                  <span className="jp-skeleton block h-5 w-6" />
                  <span className="jp-skeleton block h-3 w-14 max-w-full" />
                </div>
              ))}
            </div>
          </div>
          {GROUPS.map((group) => (
            <section key={group} className="jp-section">
              <div className="jp-section__head jp-section__toggle">
                <span className="jp-skeleton block h-5 w-36" />
              </div>
              <div className="jp-applist">
                {GROUP_ROWS.map((row) => (
                  <div key={row} className="jp-app jp-app--actions">
                    <div className="jp-job__body">
                      <span className="jp-skeleton block h-6 w-64 max-w-full" />
                      <span className="jp-skeleton mt-1.5 block h-5 w-40 max-w-full" />
                      <span className="jp-skeleton mt-2 block h-5 w-56 max-w-full" />
                      <span className="jp-skeleton mt-1 block h-4 w-48 max-w-full" />
                    </div>
                    <div className="jp-app__actions jp-app__actions--row">
                      <span className="jp-skeleton block h-9 w-36 [@media(max-width:768px)]:h-11" />
                      <span className="jp-skeleton block h-9 w-28 [@media(max-width:768px)]:h-11" />
                    </div>
                  </div>
                ))}
              </div>
            </section>
          ))}
        </section>
      </div>
    </>
  );
}
