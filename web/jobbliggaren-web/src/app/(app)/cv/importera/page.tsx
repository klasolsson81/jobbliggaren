import Link from "next/link";
import { redirect } from "next/navigation";
import { getTranslations } from "next-intl/server";
import { ChevronLeft } from "lucide-react";
import { getServerSession } from "@/lib/auth/session";
import { CvUploadForm } from "@/components/resumes/cv-upload-form";

/**
 * /cv/importera — CV-import, steg 1 (Fas 4 STEG B, F1). RSC: auth-grind +
 * civic page-hero, sedan den interaktiva `<CvUploadForm />` (klient-ö som äger
 * filväljaren). Inget CV-PII rör servern här — bytesen strömmar via BFF:en
 * (`/api/cv/import`) direkt till backend.
 */
export default async function CvImportPage() {
  const user = await getServerSession();
  if (!user) redirect("/logga-in");

  const t = await getTranslations("pages");

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
        <h1 className="jp-h1">{t("cv.import.title")}</h1>
        <p className="jp-lede">{t("cv.import.lede")}</p>
      </header>

      <CvUploadForm />
    </div>
  );
}
