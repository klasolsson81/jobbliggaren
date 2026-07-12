import Link from "next/link";
import { notFound, redirect } from "next/navigation";
import { getTranslations } from "next-intl/server";
import { ChevronLeft } from "lucide-react";
import { getServerSession } from "@/lib/auth/session";
import { getResumeById, getTemplateCatalog } from "@/lib/api/resumes";
import { assertNever } from "@/lib/dto/_helpers";
import { TemplateBuilder } from "@/components/resumes/template-builder";

interface Props {
  params: Promise<{ id: string }>;
}

/**
 * /cv/[id]/mall — mallbyggaren (Fas 4b PR-8b 8b.3, CTO-bind). RSC. Ett eget
 * syskon-route till WYSIWYG-innehållsredigeraren (`/cv/[id]`) och den kanoniska
 * granskningen (`/cv/[id]/granska`): visuell konfiguration + tung live-preview är en
 * distinkt uppgift med egen ändrings-anledning (CCP — peer-förmågor får peer-routes).
 *
 * Hämtar Resume-detaljen (PRIMÄR — styr 404/namn/header + de persisterade optionerna)
 * och den slutna optionskatalogen (mall/accent/täthet-namn + BE-auktoritativ atsSafe/
 * hex) parallellt. Katalogen är ESSENTIELL: byggaren kan inte renderas utan optioner,
 * så en icke-ok katalog degraderas till ett felskal (till skillnad från granska-vyns
 * granskning som degraderas till null). Auth/fel-formen speglar granska-vyn.
 *
 * Skal-val (CCP): hela CV-underträdet använder h1 + tillbaka-länk-skalet (inte
 * `jp-pagehero`) — koherens inom familjen (dokumenterat i granska/page.tsx, CTO-bind
 * Q4). Innehållet wrappas i `jp-container jp-page` (V3-native routes äger sin egen
 * bredd-container, app-shell.tsx V3_NATIVE_ROUTES).
 */
export default async function CvTemplateBuilderPage({ params }: Props) {
  const user = await getServerSession();
  if (!user) redirect("/logga-in");

  const t = await getTranslations("pages");
  const { id } = await params;

  const [resumeResult, catalogResult] = await Promise.all([
    getResumeById(id),
    getTemplateCatalog(),
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
        <div className="jp-container jp-page">
          <div className="flex flex-col gap-4">
            <h1 className="jp-h1">{t("common.rateLimitedTitle")}</h1>
            <p className="jp-lede">
              {t("common.rateLimitedBody", {
                seconds: resumeResult.retryAfterSeconds,
              })}
            </p>
            <div>
              <Link href={`/cv/${id}`} className="jp-btn jp-btn--secondary">
                {t("cv.mall.backLink")}
              </Link>
            </div>
          </div>
        </div>
      );
    case "forbidden":
    case "error":
      return (
        <div className="jp-container jp-page">
          <div className="flex flex-col gap-4">
            <h1 className="jp-h1">{t("cv.mall.loadErrorTitle")}</h1>
            <p className="jp-lede">{t("cv.mall.errorBody")}</p>
            <div>
              <Link href={`/cv/${id}`} className="jp-btn jp-btn--secondary">
                {t("cv.mall.backLink")}
              </Link>
            </div>
          </div>
        </div>
      );
    default:
      return assertNever(resumeResult);
  }

  const resume = resumeResult.data;

  // Katalogen är essentiell för byggaren — en icke-ok degraderas till felskal (401
  // → logga-in, övrigt → generiskt fel med tillbaka-länk), aldrig en trasig byggare.
  if (catalogResult.kind !== "ok") {
    if (catalogResult.kind === "unauthorized") redirect("/logga-in");
    return (
      <div className="jp-container jp-page">
        <div className="flex flex-col gap-4">
          <h1 className="jp-h1">{t("cv.mall.loadErrorTitle")}</h1>
          <p className="jp-lede">{t("cv.mall.errorBody")}</p>
          <div>
            <Link href={`/cv/${id}`} className="jp-btn jp-btn--secondary">
              {t("cv.mall.backLink")}
            </Link>
          </div>
        </div>
      </div>
    );
  }

  return (
    <div className="jp-container jp-page">
      <div className="flex flex-col gap-6">
        <Link
          href={`/cv/${id}`}
          className="inline-flex items-center gap-1 text-body-sm text-text-primary hover:underline self-start"
        >
          <ChevronLeft size={16} aria-hidden="true" />
          <span>{t("cv.mall.backLink")}</span>
        </Link>

        <header className="flex flex-col gap-2">
          <h1 className="jp-h1">{t("cv.mall.title")}</h1>
          <p className="jp-cv-meta">
            <span className="jp-cv-meta__file">{resume.name}</span>
          </p>
          <p className="jp-lede">{t("cv.mall.lede")}</p>
        </header>

        <TemplateBuilder
          resumeId={id}
          initialOptions={resume.templateOptions}
          catalog={catalogResult.data}
        />
      </div>
    </div>
  );
}
