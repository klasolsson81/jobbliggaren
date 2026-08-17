import { useTranslations } from "next-intl";
import { PageHeroSkeleton } from "@/components/skeletons/page-hero-skeleton";

/**
 * Route-level loading state for /cv (#739 — finding
 * `p1-no-loading-tsx-any-primary-route` P0). Paints the pagehero + the CV card
 * grid shape immediately on navigation.
 *
 * Re-uses `jp-pagehero` + `jp-cvgrid` + `jp-cv` structural classes so the grid
 * matches on swap; the default two-action aside mirrors "Importera" + "Nytt CV".
 * sr-only `role="status"` announces; visuals decorative. Sync RSC.
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

      {/* This is a SHARED App Router boundary: it is the loading state for the whole /cv
          subtree except /cv/granska/[parsedId]/*, because no other /cv route has a loading.tsx.
          Its consumers do not agree — the hub's lede wraps to two lines from ~600px up, while
          /cv/importera, /cv/[id]/granska and the hub itself at ≤414px all wrap to three.

          3 is chosen against the WORST consumer rather than the file's namesake (CTO bind
          2026-08-17). It makes the hub over-reserve at wide widths by under half a text line,
          and that is the direction to err: an over-reserving band shrinks on swap, while an
          under-reserving one pushes content the reader has already aimed at.

          It closes the band nowhere, at any value — the skeleton's rung and the paragraph's
          line box differ, and the title bar's own mismatch changes at ≤720px where the hero
          title drops a size. #1385 owns that residual, and the fact that this file paints the
          hub's card grid to routes that are not the hub. */}
      <PageHeroSkeleton ledeLines={3} />

      <div className="jp-container jp-page" aria-hidden="true">
        <div className="jp-cvgrid">
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
