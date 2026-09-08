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

// The catalogue: where the watches are listed, edited and created. Both the anchor link and the
// empty state's CTA point here, and that is one destination rather than two — `CriteriaSection`
// owns the "Ny smart bevakning" trigger on this very page.
const CATALOGUE_HREF = "/foretag/branschbevakningar";

// Ties the list to the anchor that names it, so a screen reader announces "N smarta bevakningar,
// list, N items" rather than a bare "list" under a section headed "Företagsbevakning" — a different
// function from this block's (design-reviewer Minor 5).
const TOTALS_ID = "oversikt-criteria-totals";

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
 * företag": those are what identify the catalogue on `/foretag/branschbevakningar`, and moving a
 * number is not moving a catalogue.</p>
 *
 * <p>Every criterion renders, in the handler's own order (`OrderByDescending(CreatedAt)`) — any
 * other order would be a claim about importance nobody derived. The count is bounded by
 * `CompanyWatchCriterion.MaxPerUser` = 20, the domain's own derived cap; no second display cap is
 * introduced here, because choosing one would be choosing a number, which is what #1681 exists to
 * forbid. What keeps twenty rows readable is treatment rather than truncation: a hairline ledger,
 * one weight tier between the anchor and the rows, and the too-broad advice stated once beneath the
 * list rather than verbatim per row (design-reviewer, 2026-09-07).</p>
 */
export function CriteriaSummary({ criteria, reference }: CriteriaSummaryProps) {
  const t = useTranslations("oversikt.criteriaSummary");
  // The neutral heading fallback, the derived label's "m.fl." suffix and the too-broad CTA are the
  // catalogue's own strings, read rather than transcribed: one untitled watch must not be called two
  // different things on two surfaces. Same choice, same reason, as `CompanySummary` reading
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
            can stand higher on this same page. */}
        <Link className="jp-btn jp-btn--emphasis" href={CATALOGUE_HREF}>
          {t("emptyCta")}
        </Link>
      </div>
    );
  }

  // Stated once for the whole block when ANY row is refused, never once per row. Both arms count:
  // the ads arm is what the rows show, and a matching-only refusal is still a watch the advice would
  // help. The row's own line says the status; this says what to do about it.
  const anyTooBroad = items.some((i) => i.ads.tooBroad || i.matching.tooBroad);

  return (
    <div className="jp-appsummary">
      <p className="jp-appsummary__anchor">
        {/* No ad total beside the count, and its absence is the decision: a sum over predicates
            that may overlap is not an exact number, and "exakta siffror" is the requirement. The
            per-criterion numbers below are the exact ones, and each links to exactly what it
            counted. */}
        <span id={TOTALS_ID} className="jp-appsummary__totals tabular-nums">
          {t("anchor", { count: items.length })}
        </span>
        <Link className="jp-appsummary__link" href={CATALOGUE_HREF}>
          {t("link")}
        </Link>
      </p>

      <ul className="jp-appsummary__watches" aria-labelledby={TOTALS_ID}>
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
                variant="summary"
              />
            </li>
          );
        })}
      </ul>

      {anyTooBroad && (
        <p className="jp-matchline jp-appsummary__advice">
          {t("tooBroadAdvice")}{" "}
          <Link className="jp-nudgelink" href={CATALOGUE_HREF}>
            {tRow("ads.matchingTooBroadCta")}
          </Link>
        </p>
      )}
    </div>
  );
}
