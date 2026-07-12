import Link from "next/link";
import { notFound, redirect } from "next/navigation";
import { getFormatter, getTranslations } from "next-intl/server";
import { ChevronLeft } from "lucide-react";
import { getServerSession } from "@/lib/auth/session";
import { formatDate } from "@/lib/i18n/format";
import { getResumeById } from "@/lib/api/resumes";
import { assertNever } from "@/lib/dto/_helpers";
import { findMasterVersion, emptyContent } from "@/lib/resumes/content-utils";
import { Button } from "@/components/ui/button";
import { ResumeContentForm } from "@/components/resumes/resume-content-form";
import { RenameResumeForm } from "@/components/resumes/rename-resume-form";
import { DeleteResumeDialog } from "@/components/resumes/delete-resume-dialog";

interface Props {
  params: Promise<{ id: string }>;
}

/**
 * /cv/[id]-detaljvy (F6 P3a, CTO 2026-05-20 Val 6D + ADR 0058 Beslut 3).
 *
 * **Val 6D: behåll existerande WYSIWYG `<ResumeContentForm />` + lägg
 * v3-cosmetic-shell.** Disclosure-Sektioner-kort-paradigm från Klas-
 * prompt §H **rendras INTE** denna prompt — Klas-prompt §I säger att
 * "Redigera"-knappen per sektion ska vara no-op, vilket gör disclosure-
 * paradigmen funktionellt värdelös. Två paradigm i samma route bryter
 * CCP (Martin 2017). När disclosure-edit-flödet faktiskt byggs (framtida
 * prompt) ersätter det WYSIWYG-formen.
 *
 * v3-cosmetic-shell-uppdateringar denna prompt:
 *  - `jp-h1`-typografi (ersätter v2 `text-h1 font-medium`)
 *  - Tillbaka-länk med ChevronLeft → `/cv`
 *  - `jp-lede` på Senast-uppdaterad-meta
 *  - Inga inline-edit-stubs (no-mock)
 */
export default async function CvDetailPage({ params }: Props) {
  const user = await getServerSession();
  if (!user) redirect("/logga-in");

  const t = await getTranslations("pages");
  const format = await getFormatter();
  const { id } = await params;
  const result = await getResumeById(id);
  switch (result.kind) {
    case "ok":
      break;
    case "unauthorized":
      redirect("/logga-in");
    case "notFound":
      notFound();
    case "rateLimited":
      return (
        <div className="jp-container jp-page flex flex-col gap-4">
          <h1 className="jp-h1">{t("common.rateLimitedTitle")}</h1>
          <p className="jp-lede">
            {t("common.rateLimitedBody", {
              seconds: result.retryAfterSeconds,
            })}
          </p>
          <div>
            <Button asChild variant="outline">
              <Link href="/cv">{t("cv.backLink")}</Link>
            </Button>
          </div>
        </div>
      );
    case "forbidden":
    case "error":
      return (
        <div className="jp-container jp-page flex flex-col gap-4">
          <h1 className="jp-h1">{t("cv.detail.loadErrorTitle")}</h1>
          <p className="jp-lede">{t("cv.detail.errorBody")}</p>
          <div>
            <Button asChild variant="outline">
              <Link href="/cv">{t("cv.backLink")}</Link>
            </Button>
          </div>
        </div>
      );
    default:
      return assertNever(result);
  }

  const resume = result.data;
  const updatedAt = formatDate(format, resume.updatedAt) ?? "";
  const master = findMasterVersion(resume);
  const initialContent = master?.content ?? emptyContent();

  return (
    // jp-container jp-page: CV-familjens breddcontainer (#812). Utan den renderas sidan
    // edge-to-edge — på en ultrawide (3440px) sträcks fälten till 1000+ px och innehållet
    // klistras mot skärmkanterna. Klasserna sätter bara boxmodell (max-width/marginal/
    // padding), så de kombineras direkt med flex-roten. Samma wrap som /cv/[id]/mall.
    <div className="jp-container jp-page flex flex-col gap-6">
      <Link
        href="/cv"
        className="inline-flex items-center gap-1 text-body-sm text-text-primary hover:underline self-start"
      >
        <ChevronLeft size={16} aria-hidden="true" />
        <span>{t("cv.backLink")}</span>
      </Link>

      <header className="flex items-start justify-between gap-4 flex-wrap">
        <div className="flex flex-col gap-2">
          <h1 className="jp-h1">{resume.name}</h1>
          <p className="jp-lede">
            {t("cv.detail.updatedAt")}{" "}
            <span className="font-mono">{updatedAt}</span>
          </p>
        </div>
        <div className="flex items-center gap-2">
          <Button asChild variant="outline">
            <Link href={`/cv/${id}/mall`}>{t("cv.detail.customizeTemplate")}</Link>
          </Button>
          <RenameResumeForm resumeId={id} currentName={resume.name} />
          <DeleteResumeDialog resumeId={id} resumeName={resume.name} />
        </div>
      </header>

      <ResumeContentForm resumeId={id} initialContent={initialContent} />
    </div>
  );
}
