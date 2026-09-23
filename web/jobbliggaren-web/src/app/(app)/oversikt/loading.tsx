import { useTranslations } from "next-intl";
import { PageHeroSkeleton } from "@/components/skeletons/page-hero-skeleton";

/**
 * Route-level loading state for /oversikt (#739 — finding
 * `p1-no-loading-tsx-any-primary-route`). The page fans out over several
 * endpoints and is `force-dynamic`, so navigation to it dead-clicked until the
 * whole dashboard rendered. This paints the pagehero + the six card frames of
 * the bento grid (ADR 0140) immediately.
 *
 * Re-uses the real structural classes (`jp-pagehero`, `jp-ov-grid`, `jp-ov-card`)
 * so the shape matches on swap. sr-only `role="status"` announces; visuals are
 * decorative. Sync RSC, flat-grey skeletons, no animation.
 *
 * ⚠ **No pagehero aside.** The authenticated Översikt hero has none — the
 * TodayCard it used to mirror was removed in #726 — and `PageHeroSkeleton`
 * omits the `__aside` element entirely for `aside={null}`, which is what
 * keeps the band from over-reserving a wrapped row at narrow widths (#1385).
 *
 * The Branschbevakning card is reserved at `span 4`, the one-watch form: a
 * fallback cannot know whether the account holds two or more watches, and
 * reserving the full-row form would over-reserve for every account that holds
 * one. Same call, same reasoning, as leaving the setup callout unreserved before.
 */
export default function Loading() {
  const t = useTranslations("pages");
  const tOversikt = useTranslations("oversikt");
  return (
    <>
      <span role="status" aria-live="polite" aria-busy="true" className="sr-only">
        {t("navLoading.oversikt")}
      </span>

      {/* The title and lede are the page's own static translations, rendered for real so they
          wrap as the loaded page does (#1385). */}
      <PageHeroSkeleton aside={null} title={tOversikt("hero.title")} lede={tOversikt("hero.lede")} />

      <div className="jp-container jp-page" aria-hidden="true">
        {/* Toolbar: the time-only stamp + refresh on the left, the single gear on the right. */}
        <div className="jp-oversikt-toolbar">
          <div className="jp-oversikt-toolbar__left">
            <span className="jp-skeleton block h-4 w-48" />
          </div>
          <span className="jp-skeleton block h-8 w-8" />
        </div>

        <div className="jp-ov-grid">
          {/* Kräver dig — three action rows. */}
          <div className="jp-ov-card jp-ov-card--list" data-span="8">
            <div className="jp-ov-card__head">
              <span className="jp-skeleton block h-6 w-32" />
              <span className="jp-skeleton block h-4 w-14" />
            </div>
            {[0, 1, 2].map((row) => (
              <span key={row} className="jp-skeleton block h-[72px] w-full" />
            ))}
          </div>

          {/* Mina ansökningar — number, four bars, button. */}
          <div className="jp-ov-card" data-span="4">
            <div className="jp-ov-card__head">
              <span className="jp-skeleton block h-10 w-10" />
              <span className="jp-skeleton block h-5 w-36" />
            </div>
            <span className="jp-skeleton mt-[18px] block h-10 w-40" />
            <div className="mt-4 flex flex-col gap-2">
              {[0, 1, 2, 3].map((row) => (
                <span key={row} className="jp-skeleton block h-4 w-full" />
              ))}
            </div>
            <span className="jp-skeleton mt-[18px] block h-11 w-full" />
          </div>

          {/* Matchning · Bevakade företag · Branschbevakning — one number card each. */}
          {(["accent", "follow", "info"] as const).map((tone) => (
            <div key={tone} className={`jp-ov-card jp-ov-card--${tone}`} data-span="4">
              <div className="jp-ov-card__head">
                <span className="jp-skeleton block h-10 w-10" />
                <span className="jp-skeleton block h-5 w-36" />
              </div>
              <span className="jp-skeleton mt-[18px] block h-10 w-48" />
              <span className="jp-skeleton mt-3 block h-4 w-2/3" />
              <span className="jp-skeleton mt-[18px] block h-11 w-full" />
            </div>
          ))}

          {/* Senaste händelser — three feed rows. */}
          <div className="jp-ov-card jp-ov-card--list" data-span="12">
            <div className="jp-ov-card__head">
              <span className="jp-skeleton block h-6 w-44" />
              <span className="jp-skeleton block h-4 w-14" />
            </div>
            {[0, 1, 2].map((row) => (
              <span key={row} className="jp-skeleton block h-9 w-full" />
            ))}
          </div>
        </div>

        {/* Mark-all row, last on the page. */}
        <div className="jp-notice-bulk">
          <span className="jp-skeleton block h-4 w-44" />
        </div>
      </div>
    </>
  );
}
