import Link from "next/link";
import { useTranslations } from "next-intl";
import { Briefcase } from "lucide-react";
import { CriterionAdLines } from "@/components/company-criteria/criterion-ad-lines";
import { CriterionBreadth } from "@/components/company-criteria/criterion-breadth";
import { buildCriterionAdsHref } from "@/lib/company-criteria/criterion-ads-href";
import { deriveDisplayLabel } from "@/lib/company-criteria/display-label";
import type { ApiResult } from "@/lib/dto/_helpers";
import type {
  CompanyWatchCriterion,
  CriterionReference,
  ListCompanyWatchCriteriaResult,
} from "@/lib/dto/company-criteria";
import { OversiktCard, OversiktCardFoot, OversiktNumber } from "./oversikt-card";

// Parity `criterion-row.tsx`: the middle dot joins the derived label's axes and is a layout glyph,
// not copy.
const SEPARATOR = " · ";

// The catalogue: where the watches are listed, edited and created. The empty state's CTA and the
// wide card's CTA both point here — one destination, not two.
const CATALOGUE_HREF = "/foretag/branschbevakningar";

const ID = "oversikt-card-criteria";
const TOTALS_ID = "oversikt-criteria-totals";

interface CriteriaCardProps {
  readonly criteria: ApiResult<ListCompanyWatchCriteriaResult>;
  /**
   * The SCB reference tree, for the human heading. `null` = the read degraded; the heading then
   * falls back to the user's own label, else the neutral noun. A degraded tree must never blank
   * the numbers — they are the card's point and do not depend on it.
   */
  readonly reference: CriterionReference | null;
}

/**
 * Whether the Branschbevakning card takes a full row — read by the page for the two siblings'
 * spans as well, so one expression decides the reflow.
 */
export function criteriaCardIsWide(
  criteria: ApiResult<ListCompanyWatchCriteriaResult>,
): boolean {
  return criteria.kind === "ok" && criteria.data.length >= 2;
}

/**
 * "Branschbevakning" (ADR 0140 Beslut 5), and the one card whose form follows its count:
 *
 * - ONE watch: its matching count as the big number, the watch's name and breadth beneath, the
 *   active-ad line from `CriterionAdLines`, and a solid info-blue CTA to the matching ads. Where
 *   the matching number is refused, not materialised or not assessed there is no number to carry
 *   — the ad lines state which, and the CTA is absent.
 * - TWO OR MORE: the card reflows to a full row with one ledger row per watch and NO sum.
 *   Criteria are predicates with no uniqueness constraint (`CompanyWatchCriterionConfiguration`
 *   declines a `UNIQUE(user_id, sni_codes, kommun_codes)` on purpose), so two may overlap and a
 *   duplicated one doubles a sum exactly — `senior-cto-advisor` bound "never a sum" for #1681
 *   part 3 (D1), and this card keeps it.
 *
 * Every row renders in the handler's own order (`OrderByDescending(CreatedAt)`) up to the
 * domain's `MaxPerUser` = 20; no display cap is introduced here.
 */
export function CriteriaCard({ criteria, reference }: CriteriaCardProps) {
  const t = useTranslations("oversikt");
  // The neutral fallback heading and the "m.fl." suffix are the catalogue's own strings, read
  // rather than transcribed: one untitled watch must not be called two things on two surfaces.
  const tRow = useTranslations("pages.foretag.criteria");
  const title = t("criteriaSummary.heading");

  if (criteria.kind !== "ok") {
    return (
      <OversiktCard id={ID} title={title} tone="info" span={4} icon={Briefcase}>
        <OversiktNumber value={null} />
        <p className="jp-ov-card__unavailable">{t("criteriaSummary.unavailable")}</p>
      </OversiktCard>
    );
  }

  const items = criteria.data;

  if (items.length === 0) {
    return (
      <OversiktCard id={ID} title={title} tone="info" span={4} icon={Briefcase}>
        <p className="jp-ov-card__emptytitle">{t("criteriaSummary.emptyTitle")}</p>
        <p className="jp-ov-card__emptybody">{t("criteriaSummary.emptyBody")}</p>
        <OversiktCardFoot>
          <Link className="jp-btn jp-btn--emphasis jp-ov-cta" href={CATALOGUE_HREF}>
            {t("criteriaSummary.emptyCta")}
          </Link>
        </OversiktCardFoot>
      </OversiktCard>
    );
  }

  // The same three-step resolution the detail page and `criterion-row.tsx` use, imported rather
  // than re-derived (ADR 0139 "Etikettkällan"): the user's label, else the derived one, else the
  // neutral noun — never a blank line.
  const rowName = (item: CompanyWatchCriterion): string => {
    const userLabel = item.label?.trim() ?? "";
    if (userLabel.length > 0) return userLabel;
    const derived =
      reference === null
        ? null
        : deriveDisplayLabel(item.sniCodes, item.municipalityCodes, reference, {
            moreSuffix: tRow("moreSuffix"),
            separator: SEPARATOR,
          });
    return derived ?? tRow("row.untitled");
  };

  if (items.length === 1) {
    const item = items[0]!;
    // A number exists exactly when the matching count is assessed and not refused — the DTO's
    // own invariant (`count !== null` excludes both refusal arms).
    const counted = item.matching.count;
    return (
      <OversiktCard id={ID} title={title} tone="info" span={4} icon={Briefcase}>
        {counted !== null && (
          <OversiktNumber
            value={counted}
            unit={t("cards.matchingAdsUnit", { count: counted })}
          />
        )}
        <p className="jp-ov-sub">{rowName(item)}</p>
        <CriterionBreadth sniCodes={item.sniCodes} municipalityCodes={item.municipalityCodes} />
        <CriterionAdLines
          criterionId={item.id}
          ads={item.ads}
          matching={item.matching}
          variant="standalone"
          adviceStatedByCaller={false}
          actionOfferedByCaller={false}
          omitMatchingCount={counted !== null}
        />
        {counted !== null && counted > 0 && (
          <OversiktCardFoot>
            <Link
              className="jp-btn jp-ov-cta jp-ov-cta--info"
              href={buildCriterionAdsHref(item.id, 1, "matching")}
              prefetch={false}
              aria-label={t("cards.criteriaCtaAria")}
            >
              {t("cards.matchingCta")}
            </Link>
          </OversiktCardFoot>
        )}
      </OversiktCard>
    );
  }

  // Stated once for the whole card when ANY row is refused, never once per row (design-reviewer,
  // 2026-09-07) — one sentence per refused arm, and a row refusing both is counted under the ads
  // arm only, the same collapse `CriterionAdLines` applies within a row (senior-cto-advisor, #1715).
  // Parity `criteria-section.tsx`, deliberately not extracted — see that component's own comment.
  const anyAdsTooBroad = items.some((i) => i.ads.tooBroad);
  const anyMatchingOnlyTooBroad = items.some((i) => i.matching.tooBroad && !i.ads.tooBroad);

  return (
    <OversiktCard
      id={ID}
      title={title}
      tone="info"
      span={12}
      icon={Briefcase}
      aside={
        <span id={TOTALS_ID} className="jp-ov-card__count">
          {t("criteriaSummary.anchor", { count: items.length })}
        </span>
      }
    >
      <ul className="jp-ov-criteria" aria-labelledby={TOTALS_ID}>
        {items.map((item) => (
          <li key={item.id} className="jp-ov-criteria__row">
            {/* Not a heading: the card owns the only heading tier, and twenty h3s would flood
                heading navigation with summary lines. The enclosing <li> is the programmatic
                context for the links beneath (WCAG 2.4.4). */}
            <p className="jp-ov-criteria__name">{rowName(item)}</p>
            <CriterionBreadth
              sniCodes={item.sniCodes}
              municipalityCodes={item.municipalityCodes}
            />
            <CriterionAdLines
              criterionId={item.id}
              ads={item.ads}
              matching={item.matching}
              variant="standalone"
              adviceStatedByCaller={true}
              actionOfferedByCaller={false}
            />
          </li>
        ))}
      </ul>
      {(anyAdsTooBroad || anyMatchingOnlyTooBroad) && (
        <p className="jp-matchline jp-ov-criteria__advice">
          {[
            anyAdsTooBroad ? t("criteriaSummary.adsTooBroadAdvice") : null,
            anyMatchingOnlyTooBroad ? t("criteriaSummary.matchingTooBroadAdvice") : null,
          ]
            .filter((sentence) => sentence !== null)
            .join(" ")}{" "}
          <Link className="jp-nudgelink" href={CATALOGUE_HREF}>
            {tRow("ads.matchingTooBroadCta")}
          </Link>
        </p>
      )}
      <OversiktCardFoot>
        <Link className="jp-btn jp-ov-cta jp-ov-cta--outline" href={CATALOGUE_HREF}>
          {t("criteriaSummary.link")}
        </Link>
      </OversiktCardFoot>
    </OversiktCard>
  );
}
