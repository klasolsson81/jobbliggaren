import Link from "next/link";
import { notFound, redirect } from "next/navigation";
import { getTranslations } from "next-intl/server";
import { ChevronLeft } from "lucide-react";
import { getServerSession } from "@/lib/auth/session";
import { getParsedResume } from "@/lib/api/resumes";
import { assertNever } from "@/lib/dto/_helpers";
import { CvGapFillForm } from "@/components/resumes/cv-gapfill-form";

interface Props {
  params: Promise<{ parsedId: string }>;
}

/**
 * /cv/granska/[parsedId]/komplettera — CV-import, gap-fill → promote (Fas 4
 * STEG B, F2). RSC.
 *
 * Hämtar parse-artefakten server-side (CV-PII läses ALDRIG i klientbunten) och
 * prefyller gap-fill-formen. Parsern gissar aldrig datum (DQ3-3a) — formen tvingar
 * användaren att fylla i strukturerade datum innan CV:t kan sparas. Vid lyckad
 * befordran navigerar server-actionen vidare till det nya CV:t (/cv/{id}).
 */
export default async function CvGapFillPage({ params }: Props) {
  const user = await getServerSession();
  if (!user) redirect("/logga-in");

  const t = await getTranslations("pages");
  const { parsedId } = await params;
  const result = await getParsedResume(parsedId);

  switch (result.kind) {
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
              seconds: result.retryAfterSeconds,
            })}
          </p>
        </div>
      );
    case "forbidden":
    case "error":
      return (
        <div className="flex flex-col gap-4">
          <h1 className="jp-h1">{t("cv.gapFill.loadErrorTitle")}</h1>
          <p className="jp-lede">{t("cv.gapFill.errorBody")}</p>
          <div>
            <Link
              href={`/cv/granska/${parsedId}`}
              className="jp-btn jp-btn--secondary"
            >
              {t("cv.gapFill.backLink")}
            </Link>
          </div>
        </div>
      );
    default:
      return assertNever(result);
  }

  const parsed = result.data;

  return (
    <div className="flex flex-col gap-6">
      <Link
        href={`/cv/granska/${parsedId}`}
        className="inline-flex items-center gap-1 text-body-sm text-text-secondary hover:text-text-primary self-start"
      >
        <ChevronLeft size={16} aria-hidden="true" />
        <span>{t("cv.gapFill.backLink")}</span>
      </Link>

      <header className="flex flex-col gap-2">
        <h1 className="jp-h1">{t("cv.gapFill.title")}</h1>
        <p className="jp-lede">{t("cv.gapFill.lede")}</p>
      </header>

      <CvGapFillForm
        parsedId={parsedId}
        sourceFileName={parsed.sourceFileName}
        content={parsed.content}
      />
    </div>
  );
}
