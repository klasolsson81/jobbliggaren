import Link from "next/link";
import { useTranslations, useFormatter } from "next-intl";
import { formatMagnitude } from "@/lib/company-criteria/format-magnitude";
import { buildCriterionAdsHref } from "@/lib/company-criteria/criterion-ads-href";
import { MATCH_SETTINGS_HREF } from "@/lib/nav/match-settings-href";
import type {
  CriterionAdMagnitude,
  MyMatchingAdCount,
} from "@/lib/dto/company-criteria";

interface CriterionAdLinesProps {
  readonly criterionId: string;
  /**
   * `null` = the ad-count READ degraded (network, rate-limit, parse). Distinct from every
   * no-number state inside the DTO: those are answers, this is the absence of one.
   */
  readonly ads: CriterionAdMagnitude | null;
  readonly matching: MyMatchingAdCount | null;
  /**
   * Whether this surface has an authenticated destination at all. `false` renders every number
   * as plain text and drops the two nudges — `CompanySummary.linkHref === null`'s reason, one
   * prop with one meaning: the guest demo has no `(app)/` page to send a reader to, so a link
   * there resolves to `/logga-in` and the label becomes false rather than the link becoming broken.
   */
  readonly canLink?: boolean;
}

/**
 * The criterion's TWO ad numbers and every honest way of not having them — one component, because
 * it is one knowledge piece (SRP: one authority per rule).
 *
 * <p>Extracted from `(app)/foretag/smarta-bevakningar/[id]/page.tsx` by `senior-cto-advisor`'s
 * binding ruling for #1681 part 3 (in-block requirement 1, `docs/reviews/2026-09-07-1681-part3-form-cto.md`):
 * part 3 puts these same numbers on `/oversikt`, and copying ~60 lines of honesty logic is what
 * guarantees the two surfaces drift — the class ADR 0139's "Båda ytorna läser samma källa" exists
 * to close. Part 3 creates the duplicate, so part 3 owns removing it.</p>
 *
 * <p><b>Seven answers across the two lines, and none of them collapses into another.</b> The ads
 * line: a degraded read · both arms refusing for the same reason (said ONCE) · too broad · not
 * materialised · a number · a counted zero. The matching line: too broad · not materialised · not
 * assessed (no stated occupation) · a number, zero included. <b>Only a counted zero is ever
 * rendered as 0</b> — every other non-number would be a lie about a measurement that never
 * happened (ADR 0120, #859).</p>
 *
 * <p><b>The branch order is load-bearing, not stylistic.</b> The two unanswerable states are tested
 * BEFORE the `> 0` test, because `magnitude` is `null` in both and `null > 0` is `false` — which
 * would silently render "inga aktiva annonser" over a watch nobody counted.</p>
 *
 * <p>Copy comes from `pages.foretag.criteria.ads` and `jobads.companyWatches` — foreign namespaces
 * to `/oversikt` on purpose, the same choice and the same reason as `CompanySummary` reading
 * `jobads.companyWatches.filter`: duplicating the sentences here is how two surfaces drift apart on
 * the next edit.</p>
 */
export function CriterionAdLines({
  criterionId,
  ads,
  matching,
  canLink = true,
}: CriterionAdLinesProps) {
  const t = useTranslations("pages.foretag.criteria");
  // Klas 2026-09-05: the personal count works "på samma sätt som vanlig företagsbevakning", so it
  // reuses that surface's own sentences rather than minting a second vocabulary for one question.
  const tWatch = useTranslations("jobads.companyWatches");
  const format = useFormatter();

  // design-reviewer Major 1 (#1681 part 2) — the two refusals COINCIDE by construction, not by
  // accident: `CriterionMatchingAdSetResolver` derives the matching arm from the SAME magnitude the
  // ads flag comes from. Rendering both meant two blocks, no visual separation, and the advice
  // sentence repeated verbatim — 40 words for one fact, which reads as a fault rather than as two
  // answers. When they agree, say it once.
  const sharedRefusal =
    ads !== null && matching !== null
      ? ads.tooBroad && matching.tooBroad
        ? "tooBroad"
        : ads.notMaterialised && matching.notMaterialised
          ? "notMaterialised"
          : null
      : null;

  // Narrowed on `magnitude` itself rather than on a derived flag, so the type system carries the
  // guarantee instead of a `!` asserting it. The two are equivalent today by the DTO's own
  // constructor; an assertion would be a claim the compiler cannot check.
  const adsCountText =
    ads !== null && ads.magnitude !== null
      ? formatMagnitude(format, { magnitude: ads.magnitude, saturated: ads.saturated })
      : null;
  const adsCount = ads?.magnitude ?? null;

  return (
    <>
      {ads === null ? (
        <p className="jp-matchline">{t("ads.countUnavailable")}</p>
      ) : sharedRefusal === "tooBroad" ? (
        /* One line, because the personal count is refused for the same reason and is suppressed
           below. The CTA is dropped on a surface that cannot link (see `canLink`). */
        <p className="jp-matchline">
          {t("ads.adsAndMatchingTooBroad")}
          {canLink && (
            <>
              {" "}
              <Link className="jp-nudgelink" href="/foretag/smarta-bevakningar">
                {t("ads.matchingTooBroadCta")}
              </Link>
            </>
          )}
        </p>
      ) : sharedRefusal === "notMaterialised" ? (
        <p className="jp-matchline">{t("ads.adsNotMaterialised")}</p>
      ) : ads.tooBroad ? (
        <p className="jp-matchline">{t("ads.adsTooBroad")}</p>
      ) : ads.notMaterialised ? (
        <p className="jp-matchline">{t("ads.adsNotMaterialised")}</p>
      ) : adsCount !== null && adsCount > 0 && adsCountText !== null ? (
        <p className="jp-matchline tabular-nums">
          {canLink ? (
            <Link
              className="jp-countlink"
              href={buildCriterionAdsHref(criterionId, 1, "all")}
              prefetch={false}
            >
              {t("ads.linkLabel", { count: adsCountText })}
            </Link>
          ) : (
            t("ads.linkLabel", { count: adsCountText })
          )}
        </p>
      ) : (
        /* A counted zero — a real answer, and the one the whole family exists to keep sayable. */
        <p className="jp-matchline">{t("ads.none")}</p>
      )}

      {/* Two branches suppress this line entirely rather than adding an answer: a degraded read
          (the line above already says the numbers cannot be shown) and a refusal the ads line has
          just stated for BOTH numbers. */}
      {matching !== null &&
        sharedRefusal === null &&
        (matching.tooBroad ? (
          <p className="jp-matchline">
            {t("ads.matchingTooBroad")}
            {canLink && (
              <>
                {" "}
                <Link className="jp-nudgelink" href="/foretag/smarta-bevakningar">
                  {t("ads.matchingTooBroadCta")}
                </Link>
              </>
            )}
          </p>
        ) : matching.notMaterialised ? (
          /* Deliberately WITHOUT the "ändra bevakningen" call to action the too-broad arm carries.
             Nothing is wrong with this watch; it simply has not been counted yet, and inviting the
             user to edit it would be advice that cannot help. */
          <p className="jp-matchline">{t("ads.matchingNotMaterialised")}</p>
        ) : matching.count === null ? (
          /* NOT ASSESSED — about the user's profile, never about this watch. The resolver returns
             it BEFORE consulting the magnitude, so it pairs with any ads state above. */
          <p className="jp-matchline">
            {tWatch("matchNudge")}
            {canLink && (
              <>
                {" "}
                <Link className="jp-nudgelink" href={MATCH_SETTINGS_HREF}>
                  {tWatch("matchNudgeCta")}
                </Link>
              </>
            )}
          </p>
        ) : (
          <p className="jp-matchline tabular-nums">
            {canLink && matching.count > 0 ? (
              <Link
                className="jp-countlink"
                href={buildCriterionAdsHref(criterionId, 1, "matching")}
                prefetch={false}
              >
                {tWatch("matchingAds", { count: matching.count })}
              </Link>
            ) : (
              tWatch("matchingAds", { count: matching.count })
            )}
          </p>
        ))}
    </>
  );
}
