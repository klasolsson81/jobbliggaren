import { useTranslations } from "next-intl";
import { PageHeroSkeleton } from "@/components/skeletons/page-hero-skeleton";

/**
 * Route-level loading state for /cv (#739 — finding
 * `p1-no-loading-tsx-any-primary-route` P0). Paints the pagehero + the CV card
 * grid shape immediately on navigation.
 *
 * Re-uses `jp-pagehero` + `jp-cvgrid` + `jp-cv` structural classes so the grid
 * matches on swap. sr-only `role="status"` announces; visuals decorative. Sync RSC.
 *
 * ⚠ The aside is still the skeleton's DEFAULT two blocks, and this line used to say they
 * mirror "Importera" + "Nytt CV". They no longer do: #1061 removed "Nytt CV" and the hub has
 * rendered a single control since `a8e6068a`, so the fallback paints a button that never
 * arrives. Passing a one-block `aside` is the fix and it belongs with #1385 — it is also the
 * 4px that makes the band's miss additive at 375, where the aside wraps below `__main`.
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

          Its consumers do not agree, and none of them tops out at three: measured 2026-08-17,
          the hub wraps to 2 lines at ≥500px, 3 at 414/375 and 4 at 320, while /cv/importera and
          /cv/[id]/granska run 3 / 4 / 5 / 6 across the same widths. The lede is capped at
          `max-width: 60ch`, so above ~500px the count stops moving with the viewport.

          3 is the worst consumer the `1 | 2 | 3` union can EXPRESS, not the worst consumer
          (CTO bind 2026-08-17). It lowers the miss at both ends rather than trading one for the
          other, and where it over-reserves that is the direction to err: an over-reserving band
          shrinks on swap, while an under-reserving one pushes content the reader has aimed at.

          It closes the band nowhere, at any value. The title bar's mismatch is a constant +4.4px
          at every width (`h-11` = 44px against a rendered 48.4px) and does NOT vary by viewport:
          globals.css tries to drop `.jp-pagehero__title` below 720px, but the base rule comes
          later and wins, so the step never applies — a dead token step, filed as #1386. What
          varies is the lede's line count and whether the aside wraps below `__main`; at 375 it
          does, which turns the skeleton's `h-10` aside against the real 44px control into a
          further +4px. #1385 owns the residual and the fact that this file paints the hub's card
          grid to routes that are not the hub. */}
      <PageHeroSkeleton ledeLines={3} />

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
