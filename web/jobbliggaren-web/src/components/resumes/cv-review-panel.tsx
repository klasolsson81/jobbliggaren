import { AlertTriangle } from "lucide-react";
import {
  Card,
  CardContent,
  CardHeader,
  CardTitle,
} from "@/components/ui/card";
import { StatusPill } from "@/components/ui/status-pill";
import { CvProfileToggle } from "@/components/resumes/cv-profile-toggle";
import { CvCriterionVerdict } from "@/components/resumes/cv-criterion-verdict";
import { bandLabel, categoryLabel } from "@/lib/resumes/review-labels";
import type {
  CvReviewDto,
  CvReviewCategoryDto,
  RenderProfile,
} from "@/lib/dto/parsed-resume";

/**
 * CV-granskningspanel (F4-9). RSC. Surfacerar den deterministiska granskningen:
 * profil-växel, sammanfattning, kritiska underkända först, sedan kort per
 * kategori med band + räknare + varje kriteries citerade evidens. När `review`
 * är null (granskningen kunde inte laddas) degraderas vyn civilt — parse-vyn
 * står kvar, granskningen ersätts av en notis (sidan 404:ar aldrig på detta).
 */

/** Räknar-rad: visar alltid etikett + siffra (status aldrig enbart färg, WCAG
 * 1.4.1). Toner speglar verdict-tonerna för visuell koppling. */
function CategoryCounts({ category }: { category: CvReviewCategoryDto }) {
  const counts: ReadonlyArray<{ label: string; value: number; tone: string }> = [
    { label: "Godkänt", value: category.passCount, tone: "success" },
    { label: "Delvis", value: category.warnCount, tone: "warning" },
    { label: "Underkänt", value: category.failCount, tone: "danger" },
    { label: "Ej bedömt", value: category.notAssessedCount, tone: "neutral" },
  ];
  return (
    <dl className="jp-cvreview__counts">
      {counts.map((count) => (
        <div
          key={count.label}
          className="jp-cvreview__count"
          data-tone={count.tone}
        >
          <dt className="jp-cvreview__count-label">{count.label}</dt>
          <dd className="jp-cvreview__count-value">{count.value}</dd>
        </div>
      ))}
    </dl>
  );
}

export function CvReviewPanel({
  review,
  parsedId,
  profile,
}: {
  review: CvReviewDto | null;
  parsedId: string;
  profile: RenderProfile;
}) {
  if (review === null) {
    return (
      <section className="jp-cvreview" aria-labelledby="cvreview-title">
        <h2 id="cvreview-title" className="jp-cvreview__title">
          Granskning
        </h2>
        <div className="jp-cvreview__profile">
          <CvProfileToggle parsedId={parsedId} profile={profile} />
        </div>
        <p className="jp-cvreview__unavailable" role="status">
          Granskningen kunde inte laddas just nu. Tolkningen av ditt CV ovan
          påverkas inte. Försök ladda om sidan om en stund.
        </p>
      </section>
    );
  }

  const hasCriticalFails = review.criticalFails.length > 0;

  return (
    <section className="jp-cvreview" aria-labelledby="cvreview-title">
      <h2 id="cvreview-title" className="jp-cvreview__title">
        Granskning
      </h2>

      <div className="jp-cvreview__profile">
        <CvProfileToggle parsedId={parsedId} profile={profile} />
      </div>

      <p className="jp-cvreview__summary">
        {review.assessedCount} av {review.totalCount} kriterier bedöms i v1.
        Kriterier som inte kan bedömas räknas ärligt som ej bedömda och drar
        aldrig ner granskningen.{" "}
        <span className="jp-cvreview__rubric">Rubrik {review.rubricVersion}</span>
      </p>

      {hasCriticalFails && (
        <div className="jp-cvreview__critical" role="region" aria-label="Kritiska brister">
          <div className="jp-cvreview__critical-head">
            <span className="jp-cvreview__critical-icon" aria-hidden="true">
              <AlertTriangle size={18} />
            </span>
            <h3 className="jp-cvreview__critical-title">
              Kritiska brister att åtgärda först
            </h3>
          </div>
          <div className="jp-cvreview__verdicts">
            {review.criticalFails.map((verdict) => (
              <CvCriterionVerdict key={verdict.criterionId} verdict={verdict} />
            ))}
          </div>
        </div>
      )}

      <div className="jp-cvreview__categories">
        {review.categories.map((category) => {
          const band = bandLabel(category.band);
          const verdicts = review.verdicts.filter(
            (verdict) => verdict.category === category.category,
          );
          return (
            <Card key={category.category}>
              <CardHeader>
                <CardTitle asChild>
                  <h3>{categoryLabel(category.category)}</h3>
                </CardTitle>
                <div className="jp-cvreview__band">
                  <StatusPill tone={band.tone}>{band.label}</StatusPill>
                </div>
              </CardHeader>
              <CardContent>
                <CategoryCounts category={category} />
                {verdicts.length > 0 && (
                  <div className="jp-cvreview__verdicts">
                    {verdicts.map((verdict) => (
                      <CvCriterionVerdict
                        key={verdict.criterionId}
                        verdict={verdict}
                      />
                    ))}
                  </div>
                )}
              </CardContent>
            </Card>
          );
        })}
      </div>
    </section>
  );
}
