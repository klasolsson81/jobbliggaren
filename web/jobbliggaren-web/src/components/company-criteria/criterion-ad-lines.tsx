import Link from "next/link";
import { useTranslations, useFormatter } from "next-intl";
import { formatMagnitude } from "@/lib/company-criteria/format-magnitude";
import { buildCriterionAdsHref } from "@/lib/company-criteria/criterion-ads-href";
import { MATCH_SETTINGS_HREF } from "@/lib/nav/match-settings-href";
import type {
  CriterionAdMagnitude,
  MyMatchingAdCount,
} from "@/lib/dto/company-criteria";

/**
 * Which surface is rendering. It changes the COPY, never the logic, and both values are reached in
 * production — the criterion detail page passes `"detail"`, `CriteriaSummary` passes `"summary"` —
 * so neither arm is a branch nothing can produce.
 *
 * - `"detail"`: the criterion's own page. The companies the numbers count are rendered on that same
 *   page, so "från dessa företag" has an antecedent; and there is exactly ONE criterion, so the
 *   too-broad advice cannot repeat and rides on its own line.
 * - `"summary"`: one row among up to `MaxPerUser` on `/oversikt`. No company is rendered anywhere in
 *   the block, so each label must carry itself; and the advice would repeat verbatim per row, so the
 *   caller collects it and states it once.
 */
export type CriterionAdLinesVariant = "detail" | "summary";

interface CriterionAdLinesProps {
  readonly criterionId: string;
  /**
   * `null` = the ad-count READ degraded (network, rate-limit, parse). Distinct from every
   * no-number state inside the DTO: those are answers, this is the absence of one.
   */
  readonly ads: CriterionAdMagnitude | null;
  readonly matching: MyMatchingAdCount | null;
  readonly variant: CriterionAdLinesVariant;
}

/**
 * The criterion's TWO ad numbers and every honest way of not having them — one component, because
 * it is one knowledge piece (SRP: one authority per rule).
 *
 * <p>Extracted from `(app)/foretag/smarta-bevakningar/[id]/page.tsx` by `senior-cto-advisor`'s
 * binding ruling for #1681 part 3 (in-block requirement 1,
 * `docs/reviews/2026-09-07-1681-part3-form-cto.md`): part 3 puts these same numbers on `/oversikt`,
 * and copying ~60 lines of honesty logic is what guarantees the two surfaces drift — the class ADR
 * 0139's "Båda ytorna läser samma källa" exists to close.</p>
 *
 * <p>⚠ <b>The extraction was not byte-for-byte, and the one behavioural change is named here rather
 * than left to be discovered</b> (`dotnet-architect` + `code-reviewer`, 2026-09-07): the ads link
 * gained `prefetch={false}`, which the detail page's original did not carry (its matching sibling
 * already did). Harmonised deliberately — `CompanySummary` prefetches neither of its count links,
 * and on `/oversikt` up to twenty rows × two links would otherwise warm forty routes nobody asked
 * for.</p>
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
  variant,
}: CriterionAdLinesProps) {
  const t = useTranslations("pages.foretag.criteria");
  // Klas 2026-09-05: the personal count works "på samma sätt som vanlig företagsbevakning", so it
  // reuses that surface's own sentences rather than minting a second vocabulary for one question.
  const tWatch = useTranslations("jobads.companyWatches");
  const format = useFormatter();

  // In a list the advice belongs to the BLOCK, not to the row — one axis up from the same rule the
  // `sharedRefusal` collapse applies within a row (design-reviewer Major 2, 2026-09-07, measured:
  // the 160-character sentence rendered five times verbatim at the per-user cap). The row states the
  // status; `CriteriaSummary` states the advice once beneath the list.
  const inList = variant === "summary";

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
        <p className="jp-matchline">
          {inList ? (
            t("ads.tooBroadShort")
          ) : (
            <>
              {t("ads.adsAndMatchingTooBroad")}{" "}
              <Link className="jp-nudgelink" href="/foretag/smarta-bevakningar">
                {t("ads.matchingTooBroadCta")}
              </Link>
            </>
          )}
        </p>
      ) : sharedRefusal === "notMaterialised" ? (
        <p className="jp-matchline">{t("ads.adsNotMaterialised")}</p>
      ) : ads.tooBroad ? (
        <p className="jp-matchline">
          {inList ? t("ads.tooBroadShort") : t("ads.adsTooBroad")}
        </p>
      ) : ads.notMaterialised ? (
        /* SINGULAR, and that is a fix rather than a nicety (design-reviewer Minor 7): only the ads
           arm is unanswerable here, and the matching line below may well carry a number. The plural
           "Annonssiffrorna" claims both, so it read as a contradiction against the very next line. */
        <p className="jp-matchline">{t("ads.adsNotMaterialisedOnly")}</p>
      ) : adsCount !== null && adsCount > 0 && adsCountText !== null ? (
        <p className="jp-matchline tabular-nums">
          <Link
            className="jp-countlink"
            href={buildCriterionAdsHref(criterionId, 1, "all")}
            prefetch={false}
          >
            {/* "från dessa företag" needs an antecedent, and only the detail page has one — it
                renders the companies themselves. In the summary no company appears anywhere in the
                block, so the label carries itself (design-reviewer Major 3). */}
            {inList
              ? t("ads.linkLabelStandalone", { count: adsCountText })
              : t("ads.linkLabel", { count: adsCountText })}
          </Link>
        </p>
      ) : (
        /* A counted zero — a real answer, and the one the whole family exists to keep sayable. */
        <p className="jp-matchline">
          {inList ? t("ads.noneStandalone") : t("ads.none")}
        </p>
      )}

      {/* Two branches suppress this line entirely rather than adding an answer: a degraded read
          (the line above already says the numbers cannot be shown) and a refusal the ads line has
          just stated for BOTH numbers. */}
      {matching !== null &&
        sharedRefusal === null &&
        (matching.tooBroad ? (
          <p className="jp-matchline">
            {inList ? (
              t("ads.tooBroadShort")
            ) : (
              <>
                {t("ads.matchingTooBroad")}{" "}
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
            {tWatch("matchNudge")}{" "}
            <Link className="jp-nudgelink" href={MATCH_SETTINGS_HREF}>
              {tWatch("matchNudgeCta")}
            </Link>
          </p>
        ) : (
          <p className="jp-matchline tabular-nums">
            {matching.count > 0 ? (
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
