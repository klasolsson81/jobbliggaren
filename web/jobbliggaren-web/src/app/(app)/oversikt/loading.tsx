import { useTranslations } from "next-intl";
import { PageHeroSkeleton } from "@/components/skeletons/page-hero-skeleton";

/**
 * Route-level loading state for /oversikt (#739 — finding
 * `p1-no-loading-tsx-any-primary-route`). The page fans out over several
 * endpoints and is `force-dynamic`, so navigation to it dead-clicked until the
 * whole dashboard rendered. This paints the pagehero + the notice ledger shape
 * immediately.
 *
 * Re-uses the real structural classes (`jp-pagehero`, `jp-section`,
 * `jp-appsummary`) so the shape matches on swap. sr-only `role="status"`
 * announces; visuals are decorative. Sync RSC, flat-grey skeletons, no
 * animation.
 *
 * ⚠ **No pagehero aside.** The authenticated Översikt hero has none — the
 * TodayCard it used to mirror was removed in #726 — and `PageHeroSkeleton`
 * omits the `__aside` element entirely for `aside={null}`, which is what
 * keeps the band from over-reserving a wrapped row at narrow widths (#1385).
 */
export default function Loading() {
  const t = useTranslations("pages");
  return (
    <>
      <span role="status" aria-live="polite" aria-busy="true" className="sr-only">
        {t("navLoading.oversikt")}
      </span>

      {/* kicker = the greeting overline Översikt renders above its title. */}
      <PageHeroSkeleton kicker aside={null} />

      <div className="jp-container jp-page" aria-hidden="true">
        {/* The "Kräver åtgärd" card between the toolbar and the first section is
            DELIBERATELY not reserved. It renders only while the account has
            stated no desired occupation, so a fallback cannot know whether it is
            coming; reserving its ~140px would over-reserve for every account
            that has. An unreserved conditional block is the lesser shift. */}
        {/* Notice toolbar: the time-only stamp, the icon-only refresh control and its
            receipt region on the left (#1556), mark-all on the right. */}
        <div className="mb-3 flex items-center justify-between gap-4">
          <span className="jp-skeleton block h-4 w-48" />
          <span className="jp-skeleton block h-4 w-36" />
        </div>

        {/* Mina ansökningar — the only section carrying a standing summary
            above its notice rows (#1548). */}
        <section className="jp-section">
          <div className="jp-section__head">
            <span className="jp-skeleton block h-5 w-40" />
            <span className="jp-skeleton block h-4 w-8" />
          </div>
          <div className="jp-appsummary">
            <div className="jp-appsummary__anchor">
              <span className="jp-skeleton block h-6 w-48" />
              <span className="jp-skeleton block h-6 w-40" />
            </div>
            <span className="jp-skeleton block h-6 w-full max-w-3xl" />
          </div>
          <div className="flex flex-col gap-3">
            {[0, 1].map((row) => (
              <span key={row} className="jp-skeleton block h-4 w-2/3 max-w-full" />
            ))}
          </div>
        </section>

        {/* Jobbannonser — notices only. */}
        <section className="jp-section">
          <div className="jp-section__head">
            <span className="jp-skeleton block h-5 w-44" />
            <span className="jp-skeleton block h-4 w-8" />
          </div>
          <div className="flex flex-col gap-3">
            {[0, 1].map((row) => (
              <span key={row} className="jp-skeleton block h-4 w-2/3 max-w-full" />
            ))}
          </div>
        </section>

        {/* Företagsbevakning — notices UNDER two standing summaries. The comment that stood here
            said "notices only" for this section too; that became false with #1558, which gave the
            section `CompanySummary`, and #1681 del 3 adds `CriteriaSummary` beside it. Both render
            unconditionally (anchor, empty state or an unreadable line), so one anchor-shaped row
            each is reserved rather than nothing.

            The criteria block's own per-watch ROWS are deliberately NOT reserved: their number
            varies 0-20 and a fallback cannot know it, so reserving a guess would over-reserve for
            the common account and under-reserve for the full one. Same call, and the same reasoning,
            as the "Kräver åtgärd" card above — an unreserved variable block is the lesser shift. */}
        <section className="jp-section">
          <div className="jp-section__head">
            <span className="jp-skeleton block h-5 w-44" />
            <span className="jp-skeleton block h-4 w-8" />
          </div>
          {[0, 1].map((summary) => (
            <div key={summary} className="jp-appsummary">
              <div className="jp-appsummary__anchor">
                <span className="jp-skeleton block h-6 w-48" />
                <span className="jp-skeleton block h-6 w-40" />
              </div>
            </div>
          ))}
          <div className="flex flex-col gap-3">
            {[0, 1].map((row) => (
              <span key={row} className="jp-skeleton block h-4 w-2/3 max-w-full" />
            ))}
          </div>
        </section>
      </div>
    </>
  );
}
