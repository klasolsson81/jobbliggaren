import {
  Card,
  CardContent,
  CardHeader,
  CardTitle,
} from "@/components/ui/card";
import { StatusPill, type PillTone } from "@/components/ui/status-pill";
import { CvProfileToggle } from "@/components/resumes/cv-profile-toggle";
import { CvCriterionVerdict } from "@/components/resumes/cv-criterion-verdict";
import { bandLabel, categoryLabel } from "@/lib/resumes/review-labels";
import type {
  CvReviewDto,
  CvReviewCategoryDto,
  CvCriterionVerdictDto,
  RenderProfile,
} from "@/lib/dto/parsed-resume";

/**
 * CV-granskningspanel (F4-9). RSC. Surfacerar den deterministiska granskningen i
 * tre lager top-down (REVIEW-IA-REDESIGN B):
 *   1. "Att åtgärda" — alla åtgärdbara verdikt (Underkänt/Delvis) över ALLA
 *      kategorier, severitets-sorterade (Underkänt före Delvis, kritiska först).
 *   2. Per kategori — band + räknare + de Godkända verdikten (det som redan är bra).
 *   3. "Ej bedömt" — en kollapsad, lågprioriterad disclosure längst ned.
 * Ingen opak totalpoäng (Goodhart, §5/ADR 0074) — band + räknare per dimension =
 * förklarbart. Honesty-invarianten (ADR 0074): "Ej bedömt" får demoteras men
 * ALDRIG döljas eller om-etiketteras som bedömt. När `review` är null (granskningen
 * kunde inte laddas) degraderas vyn civilt — parse-vyn står kvar, granskningen
 * ersätts av en notis (sidan 404:ar aldrig på detta).
 */

/** Severitets-rang för "Att åtgärda"-sorteringen: Underkänt (Fail) före Delvis
 * (Warn). Endast åtgärdbara verdikt sorteras här. */
const SEVERITY_RANK: Record<"Fail" | "Warn", number> = { Fail: 0, Warn: 1 };

/** Räknar-rad: visar alltid etikett + siffra (status aldrig enbart färg, WCAG
 * 1.4.1). Toner speglar verdict-tonerna för visuell koppling. */
function CategoryCounts({ category }: { category: CvReviewCategoryDto }) {
  const counts: ReadonlyArray<{ label: string; value: number; tone: PillTone }> = [
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

  // Lager 1 — "Att åtgärda": alla åtgärdbara verdikt över alla kategorier.
  // Kritisk-flaggan är en INTERN sortnyckel (inte en separat region): ett verdikt
  // vars criterionId finns i criticalFails sorteras överst inom sin severitet.
  const criticalIds = new Set(review.criticalFails.map((v) => v.criterionId));
  const isActionable = (
    v: CvCriterionVerdictDto,
  ): v is CvCriterionVerdictDto & { verdict: "Fail" | "Warn" } =>
    v.verdict === "Fail" || v.verdict === "Warn";

  const actionable = review.verdicts.filter(isActionable).sort((a, b) => {
    const bySeverity = SEVERITY_RANK[a.verdict] - SEVERITY_RANK[b.verdict];
    if (bySeverity !== 0) return bySeverity;
    // Inom samma severitet: kritiska först (true → 0, false → 1).
    return (
      Number(!criticalIds.has(a.criterionId)) -
      Number(!criticalIds.has(b.criterionId))
    );
  });

  // Lager 3 — "Ej bedömt": demoterade till en kollapsad disclosure längst ned.
  const notAssessed = review.verdicts.filter(
    (v) => v.verdict === "NotAssessed",
  );

  return (
    <section className="jp-cvreview" aria-labelledby="cvreview-title">
      <h2 id="cvreview-title" className="jp-cvreview__title">
        Granskning
      </h2>

      <div className="jp-cvreview__profile">
        <CvProfileToggle parsedId={parsedId} profile={profile} />
      </div>

      <p className="jp-cvreview__summary">
        {review.assessedCount} av {review.totalCount} kriterier bedöms.
        Kriterier som inte kan bedömas räknas ärligt som ej bedömda och drar
        aldrig ner granskningen.{" "}
        <span className="jp-cvreview__rubric">Rubrik {review.rubricVersion}</span>
      </p>

      {/* Lager 1 — Att åtgärda */}
      <div
        className="jp-cvreview__todo"
        role="region"
        aria-labelledby="cvreview-todo-title"
      >
        <h3 id="cvreview-todo-title" className="jp-cvreview__todo-title">
          Att åtgärda ({actionable.length})
        </h3>
        {actionable.length === 0 ? (
          <p className="jp-cvreview__todo-empty">
            Inget kräver åtgärd just nu.
          </p>
        ) : (
          <div className="jp-cvreview__verdicts">
            {actionable.map((verdict) => (
              <CvCriterionVerdict
                key={verdict.criterionId}
                verdict={verdict}
                categoryLabel={categoryLabel(verdict.category)}
              />
            ))}
          </div>
        )}
      </div>

      {/* Lager 2 — Per kategori (band + räknare + det som redan är godkänt) */}
      <div className="jp-cvreview__categories">
        {review.categories.map((category) => {
          const band = bandLabel(category.band);
          const passVerdicts = review.verdicts.filter(
            (verdict) =>
              verdict.category === category.category &&
              verdict.verdict === "Pass",
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
                {passVerdicts.length > 0 && (
                  <div className="jp-cvreview__verdicts">
                    {passVerdicts.map((verdict) => (
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

      {/* Lager 3 — Ej bedömt (kollapsad, lågprioriterad, men aldrig dold) */}
      {notAssessed.length > 0 && (
        <details className="jp-cvreview__unassessed">
          <summary className="jp-cvreview__unassessed-summary">
            Ej bedömt ({notAssessed.length})
          </summary>
          <div className="jp-cvreview__verdicts">
            {notAssessed.map((verdict) => (
              <CvCriterionVerdict
                key={verdict.criterionId}
                verdict={verdict}
                categoryLabel={categoryLabel(verdict.category)}
              />
            ))}
          </div>
        </details>
      )}
    </section>
  );
}
