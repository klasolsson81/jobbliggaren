"use client";

import { Fragment, useRef, useState } from "react";
import { usePathname, useSearchParams } from "next/navigation";
import { useTranslations } from "next-intl";
import { X } from "lucide-react";
import { InfoDialog } from "@/components/common/info-dialog";
import { Segment } from "@/components/ui/segment";
import { CvReviewStrip, type StripDimension } from "@/components/resumes/cv-review-strip";
import { categoryLabel } from "@/lib/resumes/review-labels";
import {
  matchesFilter,
  outcomeCounts,
  parseReviewFilter,
  reviewFilterSearch,
  type ReviewFilter,
  type ReviewOutcomeFilter,
} from "@/lib/resumes/review-filter";
import type {
  CriterionVerdict,
  RubricCategory,
  ScoreBandLabel,
} from "@/lib/dto/parsed-resume";

/**
 * The review ledger (#2083): the dimension strip, the outcome filter and the table.
 *
 * The URL is the only state (senior-cto-advisor, `docs/reviews/2026-10-10-cv-ledger-form-cto.md`).
 * The filter is read from `useSearchParams()` and written with `history.replaceState`, which Next
 * syncs into its router without a request: a filter is a view over a review already fetched, and a
 * navigation would recompute the review on the server for nothing. Holding no state of its own
 * keeps back/forward, the profile switch and a status change's revalidation in step with the URL.
 *
 * The rows arrive rendered on the server, beside the three fields the filter reads, so the CV text
 * they carry is never a prop of this island.
 */

export interface LedgerRow {
  readonly criterionId: string;
  readonly category: RubricCategory;
  readonly verdict: CriterionVerdict;
  readonly node: React.ReactNode;
}

export interface LedgerGroup {
  readonly category: RubricCategory;
  readonly band: ScoreBandLabel | null;
  /** Already sorted: Underkänt, Delvis, Godkänt, Ej bedömt; critical first; then rubric order. */
  readonly rows: ReadonlyArray<LedgerRow>;
}

const OUTCOME_OPTIONS: ReadonlyArray<ReviewOutcomeFilter> = ["todo", "pass", "na", "all"];

function countVerdicts(rows: ReadonlyArray<LedgerRow>): Record<CriterionVerdict, number> {
  const counts: Record<CriterionVerdict, number> = { Fail: 0, Warn: 0, Pass: 0, NotAssessed: 0 };
  for (const row of rows) counts[row.verdict] += 1;
  return counts;
}

export function CvReviewLedger({
  groups,
  hasActionColumn,
  tableLabel,
  notice,
}: {
  groups: ReadonlyArray<LedgerGroup>;
  hasActionColumn: boolean;
  /** `aria-label` or `aria-labelledby` naming the table. */
  tableLabel: { "aria-label": string } | { "aria-labelledby": string };
  /** The preamble notice, which stands between the strip and the filter. */
  notice?: React.ReactNode;
}) {
  const t = useTranslations("resumes");
  const tEnum = useTranslations("resumes.enums");
  const pathname = usePathname();
  const searchParams = useSearchParams();
  const filter = parseReviewFilter(
    searchParams,
    groups.map((group) => group.category),
  );
  const cells = useRef(new Map<RubricCategory, HTMLButtonElement>());
  // The live region speaks only after the user has changed the filter, never on arrival.
  const [changed, setChanged] = useState(false);

  function apply(next: ReviewFilter) {
    // A fresh object, never `history.state`: Next's patch skips its router update for a state
    // carrying `__NA` (the binding condition in the CTO report).
    window.history.replaceState({}, "", `${pathname}${reviewFilterSearch(searchParams.toString(), next)}`);
    setChanged(true);
  }

  function toggleDimension(category: RubricCategory) {
    apply({ ...filter, dim: filter.dim === category ? null : category });
  }

  function clearDimension() {
    const previous = filter.dim;
    apply({ ...filter, dim: null });
    if (previous !== null) cells.current.get(previous)?.focus();
  }

  const allRows = groups.flatMap((group) => group.rows);
  const counts = outcomeCounts(allRows, filter.dim);
  const visibleGroups = groups
    .filter((group) => filter.dim === null || group.category === filter.dim)
    .map((group) => ({ group, rows: group.rows.filter((row) => matchesFilter(row, filter)) }))
    .filter(({ rows }) => rows.length > 0);
  const shown = visibleGroups.reduce((sum, { rows }) => sum + rows.length, 0);
  const columns = hasActionColumn ? 4 : 3;

  const dimensions: StripDimension[] = groups.map((group) => ({
    category: group.category,
    band: group.band,
    counts: countVerdicts(group.rows),
  }));

  return (
    <>
      <CvReviewStrip
        dimensions={dimensions}
        selected={filter.dim}
        onToggle={toggleDimension}
        cellRef={(category, element) => {
          if (element) cells.current.set(category, element);
          else cells.current.delete(category);
        }}
      />

      {notice}

      <div className="jp-cvledger__filters">
        <Segment<ReviewOutcomeFilter>
          value={filter.visa}
          onChange={(visa) => apply({ ...filter, visa })}
          aria-label={t("review.filter.groupLabel")}
          options={OUTCOME_OPTIONS.map((option) => ({
            value: option,
            label: t(`review.filter.${option}`),
            count: counts[option],
          }))}
        />
        <div className="jp-cvledger__tools">
          {filter.dim !== null && (
            <button
              type="button"
              className="jp-btn jp-btn--ghost jp-btn--sm"
              onClick={clearDimension}
            >
              <X size={16} aria-hidden="true" />
              {t("review.filter.clearDimension")}
            </button>
          )}
          <InfoDialog
            title={t("review.categoriesTitle")}
            paragraphs={[t("review.summaryNote")]}
            ariaLabel={t("review.summaryNoteAria")}
            triggerClassName="jp-labelhelp__trigger"
          />
        </div>
      </div>

      <p className="sr-only" role="status">
        {changed ? t("review.filter.shown", { shown }) : ""}
      </p>

      <div className="jp-cvledger__frame">
        <table role="table" className="jp-cvledger__table" data-cols={columns} {...tableLabel}>
          <thead role="rowgroup">
            <tr role="row" className="jp-cvledger__headrow">
              <th role="columnheader" scope="col">{t("review.table.status")}</th>
              <th role="columnheader" scope="col">{t("review.table.criterion")}</th>
              <th role="columnheader" scope="col">{t("review.table.evidence")}</th>
              {hasActionColumn && (
                <th role="columnheader" scope="col" className="jp-cvledger__actionhead">
                  {t("review.table.action")}
                </th>
              )}
            </tr>
          </thead>
          {visibleGroups.map(({ group, rows }) => (
            <tbody role="rowgroup" key={group.category}>
              <tr role="row" className="jp-cvledger__grouprow">
                <th role="rowheader" scope="rowgroup" colSpan={columns} className="jp-cvledger__group">
                  <span className="jp-cvledger__groupname">{categoryLabel(tEnum, group.category)}</span>
                  <span className="jp-cvledger__groupcount">
                    {filter.visa === "all"
                      ? t("review.table.groupCount", { total: group.rows.length })
                      : t("review.table.groupShown", { shown: rows.length, total: group.rows.length })}
                  </span>
                </th>
              </tr>
              {rows.map((row) => (
                <Fragment key={row.criterionId}>{row.node}</Fragment>
              ))}
            </tbody>
          ))}
          {visibleGroups.length === 0 && (
            <tbody role="rowgroup">
              <tr role="row" className="jp-cvledger__emptyrow">
                <td role="cell" colSpan={columns} className="jp-cvledger__empty">
                  {t("review.filter.empty")}
                </td>
              </tr>
            </tbody>
          )}
        </table>
      </div>
    </>
  );
}
