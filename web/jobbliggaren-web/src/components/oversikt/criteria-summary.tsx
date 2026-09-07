import Link from "next/link";
import { useTranslations } from "next-intl";
import { deriveDisplayLabel } from "@/lib/company-criteria/display-label";
import { CriterionAdLines } from "@/components/company-criteria/criterion-ad-lines";
import type { ApiResult } from "@/lib/dto/_helpers";
import type {
  CriterionReference,
  ListCompanyWatchCriteriaResult,
} from "@/lib/dto/company-criteria";

// Parity `criterion-row.tsx`: the middle dot joins the derived label's axes and is a layout glyph,
// not copy.
const SEPARATOR = " · ";

interface CriteriaSummaryProps {
  /**
   * The criteria as a Result, never degraded to `[]` — the same requirement, and the same reason,
   * as `CompanySummary.watches`: only a Result can tell "you have no smart watches" from "the list
   * could not be read", and the summary must say different things in those two cases.
   */
  readonly criteria: ApiResult<ListCompanyWatchCriteriaResult>;
  /**
   * The SCB reference tree, for the human heading. `null` = the read degraded; headings then fall
   * back to the user's own label, else the neutral fallback. A degraded tree must NOT blank the
   * numbers — they are the point of this block and they do not depend on it.
   */
  readonly reference: CriterionReference | null;
  /**
   * Where the anchor row's link points. `null` = this surface has no authenticated destination at
   * all, and then no link renders anywhere in the block.
   *
   * Obligatory and without a default, for `CompanySummary.linkHref`'s reason: an omitted prop would
   * quietly resolve to a route in `PROTECTED_PREFIXES`, handing a guest a link to `/logga-in` while
   * the label still promised the watches.
   *
   * No prop for the empty state's own CTA, parity `CompanySummary`: that branch requires zero
   * criteria, and its destination is where a criterion is CREATED rather than where the list lives.
   */
  readonly linkHref: string | null;
}

/**
 * Standing state over "Smarta bevakningar" on Översikt (#1681 part 3).
 *
 * <p><b>One summary line per criterion, and that is a bound ruling rather than a preference</b>
 * (`senior-cto-advisor`, `docs/reviews/2026-09-07-1681-part3-form-cto.md`, D1). Klas required
 * <i>"exakta siffror, samma siffror som redan finns på smarta bevakningar, och länkar så man kan se
 * annonserna direkt, samma länkar som redan finns"</i> — and "samma" is a definite reference that
 * measurement resolves: every number in this domain is per criterion, and `buildCriterionAdsHref`
 * takes ONE criterion id, so no combined destination exists to link a sum to.</p>
 *
 * <p><b>Why this does not mirror `CompanySummary`'s single summed anchor row.</b> That sum is exact
 * <i>because</i> employer watches are disjoint by construction — the unique index
 * `ux_company_watches_user_orgnr_active` gives one row per employer, and an ad has one employer.
 * Criteria are PREDICATES and carry no such constraint, deliberately:
 * `CompanyWatchCriterionConfiguration` declines a `UNIQUE(user_id, sni_codes, kommun_codes)` because
 * a whole-industry selection would exceed the btree tuple cap, and records that <i>"a duplicate
 * criterion is a cosmetic, user-deletable nuisance"</i>. Two criteria may therefore overlap, and a
 * duplicated one doubles a sum exactly — so summing would break the FIRST clause of Klas's sentence
 * while pretending to serve the last. What carries over from företagsbevakningen is its PROPERTY —
 * one click from Översikt to exactly what the number counted — never its shape.</p>
 *
 * <p><b>Summary grammar, never catalogue grammar:</b> `jp-appsummary` / `jp-matchline`, never
 * `jp-jobs` / `jp-job`. The different grammar is what keeps this from reading as a second notice
 * list on the same left edge. And no management affordances — no create, edit, delete or "visa
 * företag": those are what identify the catalogue on `/foretag/smarta-bevakningar`, and moving a
 * number is not moving a catalogue.</p>
 *
 * <p>Every criterion renders, in the handler's own order (`OrderByDescending(CreatedAt)`) — any
 * other order would be a claim about importance nobody derived. The count is bounded by
 * `CompanyWatchCriterion.MaxPerUser` = 20, the domain's own derived cap; no second display cap is
 * introduced here, because choosing one would be choosing a number, which is what #1681 exists to
 * forbid.</p>
 */
export function CriteriaSummary({
  criteria,
  reference,
  linkHref,
}: CriteriaSummaryProps) {
  const t = useTranslations("oversikt.criteriaSummary");
  // The neutral heading fallback and the derived label's "m.fl." suffix are the catalogue's own
  // strings, read rather than transcribed: one untitled watch must not be called two different
  // things on two surfaces. Same choice, same reason, as `CompanySummary` reading
  // `jobads.companyWatches.filter` from a foreign namespace.
  const tRow = useTranslations("pages.foretag.criteria");

  if (criteria.kind !== "ok") {
    return (
      <p className="jp-appsummary jp-appsummary--unavailable">{t("unavailable")}</p>
    );
  }

  const items = criteria.data;

  if (items.length === 0) {
    return (
      <div className="jp-appsummary jp-appsummary--empty">
        <p className="jp-appsummary__emptytitle">{t("emptyTitle")}</p>
        <p className="jp-appsummary__emptybody">{t("emptyBody")}</p>
        {/* Emphasised but not solid: one-primary-per-screen is already spent, and the setup card
            can stand higher on this same page. Rendered only where there is somewhere to go — a CTA
            that lands on /logga-in makes the LABEL false rather than the link broken. */}
        {linkHref !== null && (
          <Link className="jp-btn jp-btn--emphasis" href="/foretag/smarta-bevakningar">
            {t("emptyCta")}
          </Link>
        )}
      </div>
    );
  }

  return (
    <div className="jp-appsummary">
      <p className="jp-appsummary__anchor">
        {/* No ad total beside the count, and its absence is the decision: a sum over predicates
            that may overlap is not an exact number, and "exakta siffror" is the requirement. The
            per-criterion numbers below are the exact ones, and each links to exactly what it
            counted. */}
        <span className="jp-appsummary__totals tabular-nums">
          {t("anchor", { count: items.length })}
        </span>
        {linkHref !== null && (
          <Link className="jp-appsummary__link" href={linkHref}>
            {t("link")}
          </Link>
        )}
      </p>

      <ul className="jp-appsummary__watches">
        {items.map((item) => {
          // The same three-step resolution the detail page and `criterion-row.tsx` use, IMPORTED
          // rather than re-derived: ADR 0139 "Etikettkällan" is explicit that the human label is
          // deliberately not resolved server-side, because a second label authority could only
          // drift. A degraded tree yields no derived label, so the user's own label carries the row,
          // and failing that the neutral fallback — never a blank line.
          const userLabel = item.label?.trim() ?? "";
          const derived =
            reference === null
              ? null
              : deriveDisplayLabel(item.sniCodes, item.municipalityCodes, reference, {
                  moreSuffix: tRow("moreSuffix"),
                  separator: SEPARATOR,
                });
          const heading =
            userLabel.length > 0 ? userLabel : (derived ?? tRow("row.untitled"));

          return (
            <li key={item.id} className="jp-appsummary__watch">
              {/* Not a heading element: the section owns the only heading tier here, and twenty h3s
                  would flood heading navigation with summary lines rather than landmarks. The name
                  is the programmatic context for the two links beneath it (WCAG 2.4.4), which is
                  what the enclosing <li> provides. */}
              <p className="jp-appsummary__watchname">{heading}</p>
              <CriterionAdLines
                criterionId={item.id}
                ads={item.ads}
                matching={item.matching}
                canLink={linkHref !== null}
              />
            </li>
          );
        })}
      </ul>
    </div>
  );
}
