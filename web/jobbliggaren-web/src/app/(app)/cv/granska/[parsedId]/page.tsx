import Link from "next/link";
import { notFound, redirect } from "next/navigation";
import { getTranslations } from "next-intl/server";
import { ChevronLeft } from "lucide-react";
import { getServerSession } from "@/lib/auth/session";
import { getParsedResume, getCvReview } from "@/lib/api/resumes";
import { assertNever } from "@/lib/dto/_helpers";
import {
  renderProfileSchema,
  type CvReviewDto,
  type ParsedResumeDetailDto,
  type RenderProfile,
} from "@/lib/dto/parsed-resume";
import { PersonnummerWarning } from "@/components/resumes/personnummer-warning";
import { ParseSummary } from "@/components/resumes/parse-summary";
import { OccupationProposals } from "@/components/resumes/occupation-proposals";
import { CvReviewPanel } from "@/components/resumes/cv-review-panel";
import { CvPreview } from "@/components/resumes/cv-preview";

interface Props {
  params: Promise<{ parsedId: string }>;
  searchParams: Promise<{ profile?: string }>;
}

/**
 * /cv/granska/[parsedId] — CV-import, steg 2–3 (Fas 4 STEG B, F1). RSC.
 *
 * Hämtar parse-artefakten (primär) + granskningen (sekundär) parallellt.
 * Parse-resultatet styr sidans utfall (ok → rendera; notFound → 404; auth →
 * redirect; övrigt → civic fel-block). Granskningen degraderas civilt: om den
 * inte är ok renderas parse-vyn ändå + en notis i panelen (sidan 404:ar aldrig
 * på ett granskningsfel). CV-PII läses bara server-side (RSC) — `parsed.content`
 * passeras aldrig vidare till en klient-ö.
 */
export default async function CvReviewPage({ params, searchParams }: Props) {
  const user = await getServerSession();
  if (!user) redirect("/logga-in");

  const t = await getTranslations("pages");
  const { parsedId } = await params;
  const { profile: rawProfile } = await searchParams;

  // Default till "Ats" vid saknad/ogiltig searchParam (case-sensitiv backend).
  const profileResult = renderProfileSchema.safeParse(rawProfile);
  const profile: RenderProfile = profileResult.success
    ? profileResult.data
    : "Ats";

  const [parsedResult, reviewResult] = await Promise.all([
    getParsedResume(parsedId),
    getCvReview(parsedId, profile),
  ]);

  // Parse-resultatet är primärt och styr sidans utfall.
  let parsed: ParsedResumeDetailDto;
  switch (parsedResult.kind) {
    case "ok":
      parsed = parsedResult.data;
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
              seconds: parsedResult.retryAfterSeconds,
            })}
          </p>
        </div>
      );
    case "forbidden":
    case "error":
      return (
        <div className="flex flex-col gap-4">
          <h1 className="jp-h1">{t("cv.review.loadErrorTitle")}</h1>
          <p className="jp-lede">{t("cv.review.errorBody")}</p>
          <div>
            <Link href="/cv" className="jp-btn jp-btn--secondary">
              {t("cv.backLink")}
            </Link>
          </div>
        </div>
      );
    default:
      return assertNever(parsedResult);
  }

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
        <span>{t("cv.backLink")}</span>
      </Link>

      <header className="flex flex-col gap-2">
        <h1 className="jp-h1">{t("cv.review.title")}</h1>
        <p className="jp-cv-meta">
          <span className="jp-cv-meta__file">{parsed.sourceFileName}</span>
        </p>
        <p className="jp-lede">{t("cv.review.lede")}</p>
      </header>

      <div className="jp-cv-preview-actions">
        <CvPreview previewUrl={`/api/cv/parsed/${parsedId}/preview`} initialProfile={profile} />
      </div>

      <PersonnummerWarning personnummer={parsed.personnummer} />

      <ParseSummary confidence={parsed.confidence} />

      <OccupationProposals proposals={parsed.occupationProposals} />

      <CvReviewPanel
        review={review}
        target={{ kind: "parsed", parsedId }}
        profile={profile}
      />

      {/* CTA-söm mot F2: spara-flödet (komplettera + promote). Förbättra-CTA:n
          (F4-10) ligger som sekundär knapp FÖRE den primära spara-knappen —
          den är display-only vägledning, inte en del av spara-flödet. */}
      <div className="jp-cv-cta">
        <div className="jp-cv-cta__actions">
          <Link
            href={`/cv/granska/${parsedId}/forbattra?profile=${profile}`}
            className="jp-btn jp-btn--secondary"
          >
            {t("cv.review.showImprovements")}
          </Link>
          <Link
            href={`/cv/granska/${parsedId}/komplettera`}
            className="jp-btn jp-btn--primary"
          >
            {t("cv.review.continueSave")}
          </Link>
        </div>
      </div>
    </div>
  );
}
