import { useTranslations } from "next-intl";
import {
  Card,
  CardContent,
  CardHeader,
  CardTitle,
} from "@/components/ui/card";
import { StatusPill, type PillTone } from "@/components/ui/status-pill";
import { CvProfileToggle } from "@/components/resumes/cv-profile-toggle";
import { CvCriterionVerdict } from "@/components/resumes/cv-criterion-verdict";
import { CvFindingStatusControl } from "@/components/resumes/cv-finding-status-control";
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
 *   2. Per kategori — band med sin täckning + räknare + de Godkända verdikten.
 *   3. "Ej bedömt" — en kollapsad, lågprioriterad disclosure längst ned.
 * Ingen opak totalpoäng (Goodhart, §5/ADR 0074) — band + räknare per dimension =
 * förklarbart. Honesty-invarianten (ADR 0074): "Ej bedömt" får demoteras men
 * ALDRIG döljas eller om-etiketteras som bedömt — och sedan #1062 B1 inte heller
 * renderas som en LÅG grad: en kategori utan bedömda kriterier bär inget band alls
 * (se `CategoryBand`). När `review` är null (granskningen
 * kunde inte laddas) degraderas vyn civilt — parse-vyn står kvar, granskningen
 * ersätts av en notis (sidan 404:ar aldrig på detta).
 */

/**
 * Vilken CV-granskningen visas för (Q7(e) "canonical variant"). `parsed` =
 * import-stagingen (`/cv/granska/{parsedId}`, ingen statusledger). `canonical` =
 * en befordrad Resume (`/cv/{resumeId}/granska`) som bär finding-statusledgern
 * och därför får per-anmärkning statuskontroller.
 */
export type CvReviewTarget =
  | { kind: "parsed"; parsedId: string }
  | { kind: "canonical"; resumeId: string };

/** Route-basen profil-växeln länkar inom, härledd ur target:en. */
function toggleBasePath(target: CvReviewTarget): string {
  return target.kind === "canonical"
    ? `/cv/${target.resumeId}/granska`
    : `/cv/granska/${target.parsedId}`;
}

/** Severitets-rang för "Att åtgärda"-sorteringen: Underkänt (Fail) före Delvis
 * (Warn). Endast åtgärdbara verdikt sorteras här. */
const SEVERITY_RANK: Record<"Fail" | "Warn", number> = { Fail: 0, Warn: 1 };

/**
 * Bandet med sin TÄCKNING, eller den uttalade frånvaron av ett band (#1062 B1/M1/M2).
 *
 * Bandet är en VIKTAD poäng och räknarna under det en OVIKTAD tally, så `Toppskikt`
 * kunde stå rakt ovanför sin egen `Delvis = 2` utan att något markerade att de två
 * mäter olika saker (M1). Och utan nämnare var `Toppskikt` av 2 bedömda kriterier
 * typografiskt identiskt med `Toppskikt` av 10 (M2) — mätt: 3 av 4 band var lika på
 * ett svagt och ett rent CV. Bandet renderas därför aldrig utan sin täckning bredvid
 * sig, i samma block, så en skärmläsare läser dem i följd.
 *
 * `band === null` är inte "lägsta graden" utan INGEN grad: kategorin har noll bedömda
 * kriterier. Frånvaron skrivs ut i klartext i stället för att förmedlas genom att en
 * pill saknas — samma lärdom som M4:s "Öppen"-tillstånd.
 *
 * ⚠ Tröskeln för att hålla tillbaka ett band ÖVER noll är ett Klas-/rubrikbeslut
 * (ADR 0071: trösklar hör i rubrikdatan, inte i C# och inte här). Noll är §5-
 * invarianten och kräver honom inte.
 */
function CategoryBand({
  category,
  t,
  tEnum,
}: {
  category: CvReviewCategoryDto;
  t: ReturnType<typeof useTranslations<"resumes">>;
  tEnum: ReturnType<typeof useTranslations<"resumes.enums">>;
}) {
  const assessed =
    category.passCount + category.warnCount + category.failCount;
  const total = assessed + category.notAssessedCount;

  if (category.band === null) {
    return (
      <p className="jp-cvreview__band-unassessed">
        {t("review.band.unassessed", { count: total })}
      </p>
    );
  }

  const band = bandLabel(tEnum, category.band);
  return (
    <div className="jp-cvreview__band">
      <StatusPill tone={band.tone}>{band.label}</StatusPill>
      <span className="jp-cvreview__band-coverage">
        {t("review.band.coverage", { assessed, total })}
      </span>
    </div>
  );
}

/** Räknar-rad: visar alltid etikett + siffra (status aldrig enbart färg, WCAG
 * 1.4.1). Toner speglar verdict-tonerna för visuell koppling. */
function CategoryCounts({
  category,
  t,
}: {
  category: CvReviewCategoryDto;
  t: ReturnType<typeof useTranslations<"resumes">>;
}) {
  const counts: ReadonlyArray<{ label: string; value: number; tone: PillTone }> = [
    { label: t("review.counts.pass"), value: category.passCount, tone: "success" },
    { label: t("review.counts.warn"), value: category.warnCount, tone: "warning" },
    { label: t("review.counts.fail"), value: category.failCount, tone: "danger" },
    {
      label: t("review.counts.notAssessed"),
      value: category.notAssessedCount,
      tone: "neutral",
    },
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
  target,
  profile,
}: {
  review: CvReviewDto | null;
  target: CvReviewTarget;
  profile: RenderProfile;
}) {
  const t = useTranslations("resumes");
  const tEnum = useTranslations("resumes.enums");
  const basePath = toggleBasePath(target);

  if (review === null) {
    return (
      <section className="jp-cvreview" aria-labelledby="cvreview-title">
        <h2 id="cvreview-title" className="jp-cvreview__title">
          {t("review.title")}
        </h2>
        <div className="jp-cvreview__profile">
          <CvProfileToggle basePath={basePath} profile={profile} />
        </div>
        <p className="jp-cvreview__unavailable" role="status">
          {t("review.unavailable")}
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
        {t("review.title")}
      </h2>

      <div className="jp-cvreview__profile">
        <CvProfileToggle basePath={basePath} profile={profile} />
      </div>

      <p className="jp-cvreview__summary">
        {t("review.summary", {
          assessedCount: review.assessedCount,
          totalCount: review.totalCount,
        })}{" "}
        <span className="jp-cvreview__rubric">
          {t("review.rubric", { version: review.rubricVersion })}
        </span>
      </p>

      {/* Lager 1 — Att åtgärda */}
      <div
        className="jp-cvreview__todo"
        role="region"
        aria-labelledby="cvreview-todo-title"
      >
        <h3 id="cvreview-todo-title" className="jp-cvreview__todo-title">
          {t("review.todoTitle", { count: actionable.length })}
        </h3>
        {actionable.length === 0 ? (
          // #1062 minor 3: "Inget kräver åtgärd just nu." stod ensamt medan 18 av 35
          // kriterier aldrig bedömdes — en mening som läses som ett utlåtande om HELA
          // CV:t men bara bär de bedömda. Påståendet knyts nu till sitt underlag.
          // ⚠ Vilket NÄSTA STEG som är rätt på ett rent, sparat CV är ett Klas-
          // produktbeslut (CTO Klas-carry 2), så här lagas bara den falska halvan:
          // ingen CTA läggs till.
          <p className="jp-cvreview__todo-empty">
            {t("review.todoEmpty", { assessedCount: review.assessedCount })}{" "}
            {review.totalCount > review.assessedCount &&
              t("review.todoEmptyUnassessed", {
                count: review.totalCount - review.assessedCount,
              })}
          </p>
        ) : (
          <div className="jp-cvreview__verdicts">
            {actionable.map((verdict) => (
              <CvCriterionVerdict
                key={verdict.criterionId}
                verdict={verdict}
                categoryLabel={categoryLabel(tEnum, verdict.category)}
                // Statuskontroll ENBART på den kanoniska granskningen (befordrad
                // Resume) — den parsade stagingen har ingen statusledger.
                footer={
                  target.kind === "canonical" ? (
                    <CvFindingStatusControl
                      resumeId={target.resumeId}
                      criterionId={verdict.criterionId}
                      userStatus={verdict.userStatus}
                      userStatusStaleAt={verdict.userStatusStaleAt}
                      isIgnorable={verdict.isIgnorable}
                    />
                  ) : undefined
                }
              />
            ))}
          </div>
        )}
      </div>

      {/* Lager 2 — Per kategori (band + räknare + det som redan är godkänt) */}
      <div className="jp-cvreview__categories">
        {review.categories.map((category) => {
          const passVerdicts = review.verdicts.filter(
            (verdict) =>
              verdict.category === category.category &&
              verdict.verdict === "Pass",
          );
          return (
            <Card key={category.category}>
              <CardHeader>
                <CardTitle asChild>
                  <h3>{categoryLabel(tEnum, category.category)}</h3>
                </CardTitle>
                <CategoryBand category={category} t={t} tEnum={tEnum} />
              </CardHeader>
              <CardContent>
                <CategoryCounts category={category} t={t} />
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
            {t("review.unassessedSummary", { count: notAssessed.length })}
          </summary>
          <div className="jp-cvreview__verdicts">
            {notAssessed.map((verdict) => (
              <CvCriterionVerdict
                key={verdict.criterionId}
                verdict={verdict}
                categoryLabel={categoryLabel(tEnum, verdict.category)}
              />
            ))}
          </div>
        </details>
      )}
    </section>
  );
}
