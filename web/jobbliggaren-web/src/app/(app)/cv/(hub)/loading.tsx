import { useTranslations } from "next-intl";
import { PageHeroSkeleton } from "@/components/skeletons/page-hero-skeleton";

/**
 * Route-level loading state for the CV hub (#739 — finding
 * `p1-no-loading-tsx-any-primary-route` P0). Paints the pagehero + the CV card
 * grid shape immediately on navigation.
 *
 * Scoped to the `(hub)` route group by #1385, and the group is the fix rather than a
 * tidy-up: before it, this file was the fallback for the WHOLE `/cv` subtree except
 * `/cv/granska/[parsedId]/*`, so it painted the hub's three-card grid onto a review
 * panel, and painted a hero plate onto four session-gated 404 stubs that render none.
 * A route group changes no URL, and it moves the boundary rather than patching each
 * leaf — so a future `/cv/**` route inherits the generic `(app)` net, not the hub's
 * shape.
 *
 * The hero renders the REAL title and lede rather than bars: they are static
 * translations, so the browser wraps them exactly as the loaded page does and the band
 * cannot disagree with the page at any viewport (`jobb/loading.tsx` is the precedent,
 * and `foretag/sok/loading.tsx` does it on this same `.jp-pagehero`). The aside is one
 * block because the hub renders one control — #1061 removed "Nytt CV" — at the height
 * `.jp-btn` sets. The grid below stays flat-grey: that content is data.
 *
 * Re-uses `jp-pagehero` + `jp-cvgrid` + `jp-cv` structural classes so the grid
 * matches on swap. sr-only `role="status"` announces; visuals decorative. Sync RSC.
 */
const CARDS = [0, 1, 2];
const SKILL_CHIPS = [0, 1, 2, 3];

export default function Loading() {
  const t = useTranslations("pages");
  return (
    <>
      <span role="status" aria-live="polite" aria-busy="true" className="sr-only">
        {t("navLoading.cv")}
      </span>

      <PageHeroSkeleton
        title={t("cv.title")}
        lede={t("cv.lede")}
        aside={<span className="jp-skeleton block h-11 w-36" />}
      />

      <div className="jp-container jp-page" aria-hidden="true">
        {/* #1383 gave the loaded hub a section heading above the grid. Without the same
            reservation here every grid row jumps on swap, which is the one thing this file
            exists to prevent (see the actions note below, and ADR 0045's CLS budget).
            The height is the heading's line box, derived from committed tokens rather than
            from a measurement that decays: `--text-h2` 20px x the `body` line-height 1.55 =
            31px, plus the grid's own `mt-4`. The bar inside is deliberately shorter than the
            box: the box reserves, the bar depicts. */}
        <div className="flex h-[31px] items-center">
          <span className="jp-skeleton block h-5 w-40 max-w-full" />
        </div>
        <div className="jp-cvgrid mt-4">
          {CARDS.map((card) => (
            <article key={card} className="jp-cv">
              <div className="jp-cv__head">
                <div className="min-w-0 flex-1">
                  <span className="jp-skeleton block h-5 w-2/3 max-w-full" />
                  <span className="jp-skeleton mt-2 block h-4 w-1/2 max-w-full" />
                </div>
                <span className="jp-skeleton block h-6 w-20" />
              </div>
              <div className="jp-cv__skills">
                {SKILL_CHIPS.map((chip) => (
                  <span key={chip} className="jp-skeleton block h-6 w-16" />
                ))}
              </div>
              <div className="jp-cv__meta">
                <span className="jp-skeleton block h-3 w-40 max-w-full" />
              </div>
              {/* #1373: fyra kontroller, inte två, och de wrappar till två rader i
                  griddcellen. Skelettet måste reservera samma höjd som kortet landar
                  på (36 + 8 gap + 36 = 80px), annars hoppar varje grid-rad ≥44px när
                  RSC-strömmen kommer — och att förhindra just det är skelettets enda
                  uppgift (ADR 0045, CLS-budget). Bredderna speglar de renderade
                  etiketterna. Andra konsumenten av `.jp-cv__actions`; den första är
                  `components/resumes/resume-card.tsx`. */}
              <div className="jp-cv__actions flex-wrap">
                <span className="jp-skeleton block h-9 w-28" />
                <span className="jp-skeleton block h-9 w-32" />
                <span className="jp-skeleton block h-9 w-20" />
                <span className="jp-skeleton block h-9 w-24" />
              </div>
            </article>
          ))}
        </div>
      </div>
    </>
  );
}
