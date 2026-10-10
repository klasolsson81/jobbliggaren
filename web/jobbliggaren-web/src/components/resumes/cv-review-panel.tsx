import { useTranslations } from "next-intl";
import { CvReviewProfileToggle } from "@/components/resumes/cv-review-profile-toggle";
import { CvCriterionRow, criterionNameId } from "@/components/resumes/cv-criterion-row";
import { CvFindingStatusControl } from "@/components/resumes/cv-finding-status-control";
import { CvReviewLedger, type LedgerGroup } from "@/components/resumes/cv-review-ledger";
import { groupVerdicts } from "@/lib/resumes/review-filter";
import type { CvReviewDto, RenderProfile } from "@/lib/dto/parsed-resume";

/**
 * The CV review as a ledger (#2083, Klas's Claude Design handoff 1c). RSC.
 *
 * An identity row (how much of the CV was assessed, which CV and rubric, the profile switch), then
 * the ledger: a strip of dimensions with their bands, an outcome filter and one table, grouped by
 * dimension, with fixed columns Status · Kriterium · Underlag · Åtgärd.
 *
 * Invariants (ADR 0074, #1062): no total score; a band never stands without its coverage (the
 * strip); Ej bedömt is never hidden by default (the filter starts on Alla) and never relabelled as
 * assessed; every Godkänt, Delvis and Underkänt shows its evidence (the row). When `review` is null
 * the page degrades civilly: a notice stands where the strip would, and the page never 404s on it.
 */

/**
 * Which review is shown. `parsed` = the import staging (`/cv/granska/{parsedId}`, no status
 * ledger). `canonical` = a promoted Resume (`/cv/{resumeId}/granska`), whose findings carry the
 * user's status and therefore an action column.
 */
export type CvReviewTarget =
  | { kind: "parsed"; parsedId: string }
  | { kind: "canonical"; resumeId: string };

function toggleBasePath(target: CvReviewTarget): string {
  return target.kind === "canonical"
    ? `/cv/${target.resumeId}/granska`
    : `/cv/granska/${target.parsedId}`;
}

export function CvReviewPanel({
  review,
  target,
  profile,
  documentName,
  notice,
}: {
  review: CvReviewDto | null;
  target: CvReviewTarget;
  profile: RenderProfile;
  /** The CV's name, shown beside the rubric version in the identity row. */
  documentName?: string;
  /** The preamble notice, placed between the strip and the filter. */
  notice?: React.ReactNode;
}) {
  const t = useTranslations("resumes");

  // On the canonical surface the page's h1 ("Granskning av ditt CV") already names the review, so
  // the panel adds no heading and names its table instead. On staging the h1 is about the imported
  // file and the review is one block among the parse artefacts, so it needs its own visible h2.
  const ownsPageTitle = target.kind === "canonical";
  const hasActionColumn = target.kind === "canonical";

  const metaParts = [
    documentName,
    review === null ? undefined : t("review.rubric", { version: review.rubricVersion }),
  ].filter((part): part is string => part !== undefined);

  const identity = (
    <div className="jp-cvledger__head">
      <div className="jp-cvledger__identity">
        {review !== null && (
          <p className="jp-cvledger__coverage">
            {t("review.summary", {
              assessedCount: review.assessedCount,
              totalCount: review.totalCount,
            })}
          </p>
        )}
        {metaParts.length > 0 && (
          <p className="jp-cvledger__meta">
            {metaParts.map((part) => (
              <span key={part}>{part}</span>
            ))}
          </p>
        )}
      </div>
      <CvReviewProfileToggle basePath={toggleBasePath(target)} profile={profile} />
    </div>
  );

  const groups: LedgerGroup[] =
    review === null
      ? []
      : groupVerdicts(review).map(({ category, verdicts }) => ({
          category,
          band: review.categories.find((c) => c.category === category)?.band ?? null,
          rows: verdicts.map((verdict) => ({
            criterionId: verdict.criterionId,
            category: verdict.category,
            verdict: verdict.verdict,
            node: (
              <CvCriterionRow
                verdict={verdict}
                hasActionColumn={hasActionColumn}
                action={
                  target.kind === "canonical" &&
                  (verdict.verdict === "Fail" || verdict.verdict === "Warn") ? (
                    <CvFindingStatusControl
                      resumeId={target.resumeId}
                      criterionId={verdict.criterionId}
                      labelledBy={criterionNameId(verdict.criterionId)}
                      userStatus={verdict.userStatus}
                      userStatusStaleAt={verdict.userStatusStaleAt}
                      isIgnorable={verdict.isIgnorable}
                    />
                  ) : undefined
                }
              />
            ),
          })),
        }));

  const body =
    review === null ? (
      <>
        <p className="jp-cvreview__unavailable" role="status">
          {t("review.unavailable")}
        </p>
        {notice}
      </>
    ) : (
      <CvReviewLedger
        groups={groups}
        hasActionColumn={hasActionColumn}
        tableLabel={
          ownsPageTitle
            ? { "aria-label": t("review.title") }
            : { "aria-labelledby": "cvreview-title" }
        }
        notice={notice}
      />
    );

  if (ownsPageTitle) {
    return (
      <div className="jp-cvledger">
        {identity}
        {body}
      </div>
    );
  }

  return (
    <section className="jp-cvledger" aria-labelledby="cvreview-title">
      <h2 id="cvreview-title" className="jp-cvreview__title">
        {t("review.title")}
      </h2>
      {identity}
      {body}
    </section>
  );
}
