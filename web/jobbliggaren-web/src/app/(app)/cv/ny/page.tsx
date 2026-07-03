import Link from "next/link";
import { redirect } from "next/navigation";
import { getTranslations } from "next-intl/server";
import { ChevronLeft } from "lucide-react";
import { getServerSession } from "@/lib/auth/session";
import { CreateResumeForm } from "@/components/resumes/create-resume-form";

/**
 * /cv/ny — skapa ett nytt CV, fullsida. RSC: auth-grind + civic page-hero,
 * sedan den interaktiva `<CreateResumeForm />` (klient-ö för useActionState).
 * Detta är hard-load- / no-JS- / delbar-länk-fallbacken; soft-nav från /cv
 * fångas i stället av @modal/(.)cv/ny och visas som modal (ADR 0053). Samma
 * `CreateResumeForm` i båda (DRY).
 */
export default async function NyCvPage() {
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
        <h1 className="jp-h1">{t("cv.new.title")}</h1>
        <p className="jp-lede">{t("cv.new.lede")}</p>
      </header>

      <div className="max-w-lg">
        <CreateResumeForm />
      </div>
    </div>
  );
}
