import Link from "next/link";
import { redirect } from "next/navigation";
import { getTranslations } from "next-intl/server";
import { ChevronLeft } from "lucide-react";
import { getServerSession } from "@/lib/auth/session";
import { CvUploadForm } from "@/components/resumes/cv-upload-form";
import type { Metadata } from "next";

export async function generateMetadata(): Promise<Metadata> {
  const t = await getTranslations("pages");
  return { title: t("cv.import.meta.title") };
}

/**
 * /cv/importera — CV-import, steg 1 (Fas 4 STEG B, F1). RSC: auth-grind +
 * civic page-hero, sedan den interaktiva `<CvUploadForm />` (klient-ö som äger
 * filväljaren). Inget CV-PII rör servern här — bytesen strömmar via BFF:en
 * (`/api/cv/import`) direkt till backend.
 *
 * The hero above was an empty promise until #1062 — this docblock claimed it while the page
 * rendered no hero and no container, edge-to-edge at every viewport.
 */
export default async function CvImportPage() {
  const user = await getServerSession();
  if (!user) redirect("/logga-in");

  const t = await getTranslations("pages");

  // #1060: CV-namnet är en ETIKETT (`Resume.Name`, okrypterad kolumn som syns i
  // CV-listan), inte personens namn — lämnas fältet tomt genererar servern ett
  // icke-PII-namn. Filnamnet används inte (ADR 0096 D-B).
  // Ingen profil-hämtning behövs längre; personnamnet i CV:t sätts alltid från kontot
  // på serversidan.
  return (
    <>
      <section className="jp-pagehero">
        <div className="jp-pagehero__inner">
          <div className="jp-pagehero__main">
            <h1 className="jp-pagehero__title">{t("cv.import.title")}</h1>
            <p className="jp-pagehero__lede">{t("cv.import.lede")}</p>
          </div>
        </div>
      </section>

      <div className="jp-container jp-page flex flex-col gap-6">
        <Link href="/cv" className="jp-backlink self-start">
          <ChevronLeft size={16} aria-hidden="true" />
          <span>{t("cv.backLink")}</span>
        </Link>

        <CvUploadForm />
      </div>
    </>
  );
}
