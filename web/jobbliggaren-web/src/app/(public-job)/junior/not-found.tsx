import { getTranslations } from "next-intl/server";
import type { Metadata } from "next";
import { JUNIOR_IT_PATH } from "@/lib/job-ads/junior-it-params";
import { notFoundMetadata } from "@/lib/metadata/not-found-title";

export async function generateMetadata(): Promise<Metadata> {
  return notFoundMetadata();
}

/**
 * The junior IT list's own 404 (ADR 0186): a parameter that is not a value of the list, or a page past the end once the
 * list has shrunk. Its next step is the list's first page. It reads nothing from the API.
 */
export default async function PublicJuniorItNotFound() {
  const t = await getTranslations("publicjob.junior.notFound");

  return (
    <main
      id="main"
      tabIndex={-1}
      className="jp-container jp-page flex min-h-[60vh] flex-col justify-center gap-4 focus:outline-none"
    >
      <h1 className="jp-h1">{t("title")}</h1>
      <p className="jp-lede">{t("body")}</p>
      <div>
        <a href={JUNIOR_IT_PATH} className="jp-btn jp-btn--secondary">
          {t("toList")}
        </a>
      </div>
    </main>
  );
}
