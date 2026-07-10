import Link from "next/link";
import { notFound, redirect } from "next/navigation";
import { getTranslations } from "next-intl/server";
import { ChevronLeft } from "lucide-react";
import { getServerSession } from "@/lib/auth/session";
import { getResumeById, getResumeReview } from "@/lib/api/resumes";
import { assertNever } from "@/lib/dto/_helpers";
import {
  renderProfileSchema,
  type CvReviewDto,
  type RenderProfile,
} from "@/lib/dto/parsed-resume";
import { CvReviewPanel } from "@/components/resumes/cv-review-panel";

interface Props {
  params: Promise<{ id: string }>;
  searchParams: Promise<{ profile?: string }>;
}

/**
 * /cv/[id]/granska — den KANONISKA CV-granskningen (Fas 4b PR-8.4, stänger #657).
 * RSC. Granskar ett befordrat, sparat CV (Resume-id) i stället för importstagingen
 * (`/cv/granska/[parsedId]`). Skillnaden mot den parsade vyn: den kanoniska
 * granskningen bär finding-statusledgern (userStatus/stale/isIgnorable), så varje
 * åtgärdbar anmärkning får en per-anmärkning statuskontroll.
 *
 * Hämtar Resume-detaljen (PRIMÄR — styr 404/namn/header) + granskningen (SEKUNDÄR
 * — degraderas civilt till `null`; sidan 404:ar aldrig på ett granskningsfel)
 * parallellt. Resultatet speglar `/cv/[id]`-detaljvyns auth/fel-form. CV-PII läses
 * bara server-side; evidensen är redan personnummer-redigerad vid motorns choke
 * point innan den når klienten.
 *
 * Skal-val (CCP): hela CV-underträdet (`/cv/[id]`, `/cv/granska/[parsedId]`)
 * använder h1 + tillbaka-länk-skalet, inte `jp-pagehero` — koherens inom familjen
 * väger tyngre här. (Design-reviewer: flagga om du vill ha pagehero i stället.)
 */
export default async function CanonicalCvReviewPage({
  params,
  searchParams,
}: Props) {
  const user = await getServerSession();
  if (!user) redirect("/logga-in");

  const t = await getTranslations("pages");
  const { id } = await params;
  const { profile: rawProfile } = await searchParams;

  // Default till "Ats" vid saknad/ogiltig searchParam (case-sensitiv backend).
  const profileResult = renderProfileSchema.safeParse(rawProfile);
  const profile: RenderProfile = profileResult.success
    ? profileResult.data
    : "Ats";

  const [resumeResult, reviewResult] = await Promise.all([
    getResumeById(id),
    getResumeReview(id, profile),
  ]);

  // Resume-detaljen är primär och styr sidans utfall.
  switch (resumeResult.kind) {
    case "ok":
      break;
    case "unauthorized":
      redirect("/logga-in");
    case "notFound":
      notFound();
    case "rateLimited":
      return (
        <div className="flex flex-col gap-4">
          <h1 className="jp-h1">{t("common.rateLimitedTitle")}</h1>
          <p className="jp-lede">
            {t("common.rateLimitedBody", {
              seconds: resumeResult.retryAfterSeconds,
            })}
          </p>
          <div>
            <Link href="/cv" className="jp-btn jp-btn--secondary">
              {t("cv.backLink")}
            </Link>
          </div>
        </div>
      );
    case "forbidden":
    case "error":
      return (
        <div className="flex flex-col gap-4">
          <h1 className="jp-h1">{t("cv.granska.loadErrorTitle")}</h1>
          <p className="jp-lede">{t("cv.granska.errorBody")}</p>
          <div>
            <Link href="/cv" className="jp-btn jp-btn--secondary">
              {t("cv.backLink")}
            </Link>
          </div>
        </div>
      );
    default:
      return assertNever(resumeResult);
  }

  const resume = resumeResult.data;

  // Granskningen degraderas civilt — bara "ok" ger en panel, övrigt → notis.
  const review: CvReviewDto | null =
    reviewResult.kind === "ok" ? reviewResult.data : null;

  return (
    <div className="flex flex-col gap-6">
      <Link
        href="/cv"
        className="inline-flex items-center gap-1 text-body-sm text-text-primary hover:underline self-start"
      >
        <ChevronLeft size={16} aria-hidden="true" />
        <span>{t("cv.granska.backLink")}</span>
      </Link>

      <header className="flex flex-col gap-2">
        <h1 className="jp-h1">{t("cv.granska.title")}</h1>
        <p className="jp-cv-meta">
          <span className="jp-cv-meta__file">{resume.name}</span>
        </p>
        <p className="jp-lede">{t("cv.granska.lede")}</p>
      </header>

      <CvReviewPanel
        review={review}
        target={{ kind: "canonical", resumeId: id }}
        profile={profile}
      />
    </div>
  );
}
