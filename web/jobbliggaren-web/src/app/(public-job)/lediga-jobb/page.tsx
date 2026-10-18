import { notFound, permanentRedirect } from "next/navigation";
import { getLocale, getTranslations } from "next-intl/server";
import type { Metadata } from "next";
import { PublicJobAdCatalogue } from "@/components/job-ads/public-job-ad-catalogue";
import { getPublicJobAdCatalogue } from "@/lib/api/public-job-ad-catalogue";
import { parseCataloguePageParam } from "@/lib/job-ads/catalogue-page-param";
import { PUBLIC_CATALOGUE_PATH, publicCatalogueUrl } from "@/lib/job-ads/public-catalogue-url";
import { notFoundMetadata } from "@/lib/metadata/not-found-title";

interface PageProps {
  searchParams: Promise<Record<string, string | string[] | undefined>>;
}

/**
 * The public catalogue, `/lediga-jobb` (ADR 0176): every listed ad, newest first, 20 to a page. One page for everyone:
 * it reads no cookie and no session. Only `sida` is read; every other parameter is ignored and kept out of the
 * canonical. `sida=1` is a 308 to the bare path, and a `sida` that is not a page is a 404 without asking the API.
 *
 * One read per render (`getPublicJobAdCatalogue` is wrapped in `cache()` and shared with `generateMetadata`). A page
 * the API does not have is a real 404; every other outcome throws and is a 5xx, so an outage never reads as a page
 * that does not exist. The title, description and preview carry no row and no count.
 */
export async function generateMetadata(props?: PageProps): Promise<Metadata> {
  const param = parseCataloguePageParam((await props?.searchParams)?.sida);
  if (param.kind !== "page") return notFoundMetadata();

  const result = await getPublicJobAdCatalogue(param.page);
  if (result.kind !== "ok") return notFoundMetadata();

  const t = await getTranslations("publicjob.catalogue");
  const tLanding = await getTranslations("landing");
  const tMeta = await getTranslations("metadata");
  const locale = await getLocale();
  const label = tLanding("common.catalogueLink");
  const title = param.page === 1 ? label : t("titlePaged", { label, page: param.page });
  const description = t("metaDescription");
  const url = publicCatalogueUrl(param.page);

  return {
    title,
    description,
    alternates: { canonical: url },
    robots: { index: true, follow: true },
    openGraph: {
      type: "website",
      locale: locale === "sv" ? "sv_SE" : "en_US",
      siteName: tMeta("applicationName"),
      title,
      description,
      url,
      // A page's own `openGraph` replaces the root's wholesale, so the shared image is named here (ADR 0165 D4).
      images: [{ url: "/opengraph-image", width: 1200, height: 630 }],
    },
  };
}

export default async function PublicCataloguePage({ searchParams }: PageProps) {
  const param = parseCataloguePageParam((await searchParams).sida);
  if (param.kind === "redirectToFirst") permanentRedirect(PUBLIC_CATALOGUE_PATH);
  if (param.kind === "noSuchPage") notFound();

  const result = await getPublicJobAdCatalogue(param.page);
  switch (result.kind) {
    case "ok":
      return <PublicJobAdCatalogue page={result.page} />;
    case "notFound":
      notFound();
    case "unavailable":
      throw new Error("The public catalogue read is unavailable");
  }
}
