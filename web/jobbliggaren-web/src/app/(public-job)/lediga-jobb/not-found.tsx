import { getTranslations } from "next-intl/server";
import type { Metadata } from "next";
import { PUBLIC_CATALOGUE_PATH } from "@/lib/job-ads/public-catalogue-url";
import { notFoundMetadata } from "@/lib/metadata/not-found-title";

export async function generateMetadata(): Promise<Metadata> {
  return notFoundMetadata();
}

/**
 * The catalogue's own 404 (ADR 0176): a `sida` that is not a page, or a page past the end once the list has shrunk.
 * Its next step is the catalogue's first page, not the start page. It reads nothing from the API. A child segment
 * added below `/lediga-jobb` later inherits this boundary unless it brings its own.
 */
export default async function PublicCatalogueNotFound() {
  const t = await getTranslations("publicjob.catalogue.notFound");

  return (
    <main
      id="main"
      tabIndex={-1}
      className="jp-container jp-page flex min-h-[60vh] flex-col justify-center gap-4 focus:outline-none"
    >
      <h1 className="jp-h1">{t("title")}</h1>
      <p className="jp-lede">{t("body")}</p>
      <div>
        <a href={PUBLIC_CATALOGUE_PATH} className="jp-btn jp-btn--secondary">
          {t("toCatalogue")}
        </a>
      </div>
    </main>
  );
}
