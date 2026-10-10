import type {
  CriterionVerdict,
  CvCriterionVerdictDto,
  CvReviewDto,
  RubricCategory,
} from "@/lib/dto/parsed-resume";

/**
 * The CV review ledger's two filters (#2083): a dimension (`dim`) and an outcome (`visa`),
 * combined with AND and carried in the URL so a filtered view survives a reload and can be
 * shared. Pure: no React, no i18n.
 *
 * `todo` is the verdict, never the user's status. A row marked Åtgärdad is still a Fail until the
 * engine stops finding it, so it stays under Att åtgärda and does not jump away from the button
 * the user just pressed.
 */

export const REVIEW_OUTCOME_FILTERS = ["todo", "pass", "na", "all"] as const;
export type ReviewOutcomeFilter = (typeof REVIEW_OUTCOME_FILTERS)[number];

/** Alla is the default, so Ej bedömt is never hidden by default (ADR 0074). */
export const DEFAULT_OUTCOME_FILTER: ReviewOutcomeFilter = "all";

export const DIM_PARAM = "dim";
export const OUTCOME_PARAM = "visa";

export interface ReviewFilter {
  readonly dim: RubricCategory | null;
  readonly visa: ReviewOutcomeFilter;
}

/** The fields of a verdict the filter reads; rows carry them beside their rendered node. */
export interface ReviewRowKey {
  readonly category: RubricCategory;
  readonly verdict: CriterionVerdict;
}

/** An unknown or missing `visa` reads as Alla. */
export function parseOutcomeFilter(raw: string | null): ReviewOutcomeFilter {
  return REVIEW_OUTCOME_FILTERS.find((filter) => filter === raw) ?? DEFAULT_OUTCOME_FILTER;
}

/**
 * Reads the filter from the query. A `dim` this profile does not have (an ATS dimension in a
 * Visual link, or any unknown value) reads as no dimension; an unknown `visa` reads as Alla.
 */
export function parseReviewFilter(
  params: { get(name: string): string | null },
  categories: ReadonlyArray<RubricCategory>,
): ReviewFilter {
  const rawDim = params.get(DIM_PARAM);
  const dim = categories.find((category) => category === rawDim) ?? null;
  return { dim, visa: parseOutcomeFilter(params.get(OUTCOME_PARAM)) };
}

export function matchesOutcome(
  verdict: CriterionVerdict,
  visa: ReviewOutcomeFilter,
): boolean {
  switch (visa) {
    case "todo":
      return verdict === "Fail" || verdict === "Warn";
    case "pass":
      return verdict === "Pass";
    case "na":
      return verdict === "NotAssessed";
    case "all":
      return true;
  }
}

export function matchesFilter(row: ReviewRowKey, filter: ReviewFilter): boolean {
  return (
    (filter.dim === null || row.category === filter.dim) &&
    matchesOutcome(row.verdict, filter.visa)
  );
}

/** How many rows each outcome option would show under the selected dimension. */
export function outcomeCounts(
  rows: ReadonlyArray<ReviewRowKey>,
  dim: RubricCategory | null,
): Record<ReviewOutcomeFilter, number> {
  const inDim = rows.filter((row) => dim === null || row.category === dim);
  return {
    todo: inDim.filter((row) => matchesOutcome(row.verdict, "todo")).length,
    pass: inDim.filter((row) => matchesOutcome(row.verdict, "pass")).length,
    na: inDim.filter((row) => matchesOutcome(row.verdict, "na")).length,
    all: inDim.length,
  };
}

const OUTCOME_RANK: Record<CriterionVerdict, number> = {
  Fail: 0,
  Warn: 1,
  Pass: 2,
  NotAssessed: 3,
};

export interface ReviewGroup {
  readonly category: RubricCategory;
  readonly verdicts: ReadonlyArray<CvCriterionVerdictDto>;
}

/**
 * The ledger's groups: one per dimension, in the order the review lists its dimensions. Within a
 * group: Underkänt, Delvis, Godkänt, Ej bedömt; within one outcome the critical criteria first,
 * then the rubric's own order (`verdicts` arrives in rubric order and `sort` is stable).
 *
 * A verdict whose dimension the review does not list still gets a group, after the listed ones —
 * a row the backend sent is never dropped by the page.
 */
export function groupVerdicts(review: CvReviewDto): ReviewGroup[] {
  const critical = new Set(review.criticalFails.map((v) => v.criterionId));
  const order: RubricCategory[] = review.categories.map((c) => c.category);
  for (const verdict of review.verdicts) {
    if (!order.includes(verdict.category)) order.push(verdict.category);
  }

  return order.map((category) => ({
    category,
    verdicts: review.verdicts
      .filter((verdict) => verdict.category === category)
      .sort(
        (a, b) =>
          OUTCOME_RANK[a.verdict] - OUTCOME_RANK[b.verdict] ||
          Number(!critical.has(a.criterionId)) - Number(!critical.has(b.criterionId)),
      ),
  }));
}

/**
 * The query for `next`, keeping every other parameter (`profile`) and leaving a default out, so
 * the unfiltered page has the same URL it always had.
 */
export function reviewFilterSearch(current: string, next: ReviewFilter): string {
  const params = new URLSearchParams(current);
  if (next.dim === null) params.delete(DIM_PARAM);
  else params.set(DIM_PARAM, next.dim);
  if (next.visa === DEFAULT_OUTCOME_FILTER) params.delete(OUTCOME_PARAM);
  else params.set(OUTCOME_PARAM, next.visa);
  const query = params.toString();
  return query === "" ? "" : `?${query}`;
}
