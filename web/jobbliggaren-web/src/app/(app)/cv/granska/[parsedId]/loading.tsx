import { useTranslations } from "next-intl";
import { BrandSpinner } from "@/components/brand/brand-spinner";
import { PageHeroSkeleton } from "@/components/skeletons/page-hero-skeleton";

/**
 * Suspense fallback for /cv/granska/[parsedId] (Fas 4 STEG B, F1). Next wraps page.tsx in a
 * <Suspense>; this paints while the parse fetch and the compute-on-demand review stream in.
 *
 * A spinner rather than a skeleton for the CONTENT (formless, known-slow wait, per the
 * spinner doctrine) — but the SHELL is known and paints immediately (#1062). Before this
 * change the fallback painted neither hero nor container, so the shell appeared only when the
 * stream landed.
 *
 * ⚠ It is NOT the same shape as /cv/loading.tsx, and an earlier revision of this docblock
 * claimed it was. That precedent leads with its own `sr-only role="status"` and marks the
 * visual block `aria-hidden`; here the announce lives inside BrandSpinner instead, so the
 * markers sit in different places for the same effect.
 *
 * ⚠ The LENGTH of the wait is unmeasured. This docblock used to justify the spinner with
 * "granskningen kan överstiga 1–2 s"; measured 2026-08-17, the review endpoint answers in
 * 24–39 ms warm (staging) and 20–47 ms (canonical). That bounds the engine, not the whole RSC
 * render — so the fallback stays, without the ungrounded time claim.
 */
export default function Loading() {
  const t = useTranslations("pages");
  return (
    <>
      {/* `aside={<></>}` rather than the default: the default paints TWO button blocks and
          this hero has no aside at all. `null` would NOT work — `aside ?? …` hands the
          default back for a nullish value — so the emptiness has to be a non-nullish element.
          `ledeLines={3}` because this lede wraps to three lines: measured, the skeleton band
          was 168px against the loaded band's 231px, a 63px jump on the page's most prominent
          element (#1062, design-reviewer M-A). */}
      <PageHeroSkeleton aside={<></>} ledeLines={3} />

      <div className="jp-container jp-page">
        {/* aria-hidden on the visible copy, not on the container: BrandSpinner carries its own
            role="status" + sr-only label, so hiding the whole block would silence the announce
            entirely, while leaving this <p> exposed makes a screen reader say the same string
            twice. */}
        <div className="jp-cv-loading">
          <BrandSpinner size={44} label={t("cv.review.loading")} />
          <p className="jp-cv-loading__text" aria-hidden="true">
            {t("cv.review.loading")}
          </p>
        </div>
      </div>
    </>
  );
}
