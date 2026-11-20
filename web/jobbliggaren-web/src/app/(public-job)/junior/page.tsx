import { notFound } from "next/navigation";
import { getLocale, getTranslations } from "next-intl/server";
import type { Metadata } from "next";
import { PublicJuniorItList } from "@/components/job-ads/public-junior-it-list";
import { getPublicJuniorItJobAds } from "@/lib/api/public-junior-it-job-ads";
import { parseJuniorItParams } from "@/lib/job-ads/junior-it-params";
import { notFoundMetadata } from "@/lib/metadata/not-found-title";

interface PageProps {
  searchParams: Promise<Record<string, string | string[] | undefined>>;
}

/**
 * The open junior IT list, `/junior` (ADR 0186): listed Data/IT ads that are curated or carry a junior headline, for
 * everyone, signed in or not. It reads no cookie and no session, and only `municipality`, `sortBy` and `page`; a value
 * that cannot be one of those is a 404 without asking the API.
 *
 * `noindex, follow` on every answer, and in no sitemap (ADR 0166, the amendment for the junior list). The title,
 * description and link preview are fixed text: no row and no count. One read per render (`getPublicJuniorItJobAds` is
 * wrapped in `cache()` and shared with `generateMetadata`). A page the API does not have is a real 404; every other
 * outcome throws and is a 5xx, so an outage never reads as a page that does not exist.
 */
export async function generateMetadata(props?: PageProps): Promise<Metadata> {
  const params = parseJuniorItParams((await props?.searchParams) ?? {});
  if (params.kind !== "query") return notFoundMetadata();

  const { municipality, sortBy, page } = params.query;
  const result = await getPublicJuniorItJobAds(municipality, sortBy, page);
  if (result.kind === "notFound") return notFoundMetadata();
  if (result.kind === "unavailable") {
    // The page throws and the group's error boundary answers; the tab says what that page says, never "not found".
    const tFallback = await getTranslations("fallback");
    return { title: tFallback("errorTitle"), robots: { index: false, follow: true } };
  }

  const t = await getTranslations("publicjob.junior");
  const tMeta = await getTranslations("metadata");
  const locale = await getLocale();
  const title = t("title");
  const description = t("metaDescription");

  return {
    title,
    description,
    robots: { index: false, follow: true },
    openGraph: {
      type: "website",
      locale: locale === "sv" ? "sv_SE" : "en_US",
      siteName: tMeta("applicationName"),
      title,
      description,
      // A page's own `openGraph` replaces the root's wholesale, so the shared image is named here (ADR 0165 D4).
      images: [{ url: "/opengraph-image", width: 1200, height: 630 }],
    },
  };
}

export default async function PublicJuniorItPage({ searchParams }: PageProps) {
  const params = parseJuniorItParams(await searchParams);
  if (params.kind === "noSuchPage") notFound();

  const { municipality, sortBy, page } = params.query;
  const result = await getPublicJuniorItJobAds(municipality, sortBy, page);
  switch (result.kind) {
    case "ok":
      return <PublicJuniorItList page={result.page} query={params.query} />;
    case "notFound":
      notFound();
    case "unavailable":
      throw new Error("The junior IT list read is unavailable");
  }
}
