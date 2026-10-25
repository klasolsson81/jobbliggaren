import { notFound } from "next/navigation";
import { getFormatter, getLocale, getTranslations } from "next-intl/server";
import type { Metadata } from "next";
import { PublicJobAd } from "@/components/job-ads/public-job-ad";
import { getPublicJobAd } from "@/lib/api/public-job-ad";
import { getPublicJobAdRelated } from "@/lib/api/public-job-ad-related";
import { formatDate } from "@/lib/i18n/format";
import { publicJobAdTitle } from "@/lib/job-ads/public-job-ad-title";
import { publicJobAdUrl } from "@/lib/job-ads/public-job-ad-url";
import { notFoundMetadata } from "@/lib/metadata/not-found-title";
import { SHARE_IMAGE } from "@/lib/seo/share-image";

interface PageProps {
  params: Promise<{ id: string }>;
}

const SENTENCE_END = /[.!?…]$/u;

/**
 * The public job page (ADR 0165): an internal page behind `proxy.ts`, which rewrites a cookieless `/jobb/<uuid>` here
 * after asking the public read whether the ad is listed. The address in the browser stays `/jobb/<uuid>`.
 *
 * One read per render (`getPublicJobAd` is wrapped in `cache()` and shared with `generateMetadata`; ADR 0160 D9.4).
 * A 404 or 410 (the ad ended after the edge's 200) is a real 404; every other outcome throws and is a 5xx, so an
 * outage never reads as a removal (D9.5). The page body also reads the related ads (ADR 0182), in parallel and only
 * here, never in `generateMetadata`: nothing of another ad reaches the title, the description or OpenGraph, and a
 * failed related read only leaves its section out.
 *
 * Title, description, OpenGraph and the Twitter card are built from structured fields only, never from the ad text: a
 * link preview is a copy held by a third party, and the free text can name a person.
 */
export async function generateMetadata({ params }: PageProps): Promise<Metadata> {
  const { id } = await params;
  const result = await getPublicJobAd(id);
  if (result.kind !== "ok") return notFoundMetadata();

  const { jobAd } = result;
  const t = await getTranslations("publicjob");
  const tMeta = await getTranslations("metadata");
  const format = await getFormatter();
  const locale = await getLocale();
  const published = formatDate(format, jobAd.publishedAt) ?? "";
  const deadline = formatDate(format, jobAd.expiresAt);

  const documentTitle = publicJobAdTitle(jobAd);
  // A title that already ends its sentence gets no full stop from the template, so "Säljare!" never reads "Säljare!.".
  const titleEnd = SENTENCE_END.test(jobAd.title) ? "punctuated" : "open";
  const description =
    deadline === null
      ? t("meta.description", { company: jobAd.companyName, title: jobAd.title, titleEnd, published })
      : t("meta.descriptionWithDeadline", { company: jobAd.companyName, title: jobAd.title, titleEnd, published, deadline });
  const url = publicJobAdUrl(jobAd.id);
  // The compact card's square brand mark, the same for every ad (ADR 0165 Amendment 2026-10-25), never the root's
  // large card: a page's own `openGraph` and `twitter` replace the root's wholesale (measured in the production build),
  // so both name it here.
  const image = {
    url: SHARE_IMAGE.path,
    width: SHARE_IMAGE.width,
    height: SHARE_IMAGE.height,
    alt: tMeta("applicationName"),
  };

  return {
    title: documentTitle,
    description,
    alternates: { canonical: url },
    robots: { index: true, follow: true },
    openGraph: {
      type: "website",
      locale: locale === "sv" ? "sv_SE" : "en_US",
      siteName: tMeta("applicationName"),
      title: documentTitle,
      description,
      url,
      images: [image],
    },
    twitter: { card: "summary", title: documentTitle, description, images: [image] },
  };
}

export default async function PublicJobPage({ params }: PageProps) {
  const { id } = await params;
  const [result, related] = await Promise.all([getPublicJobAd(id), getPublicJobAdRelated(id)]);

  switch (result.kind) {
    case "ok":
      return <PublicJobAd jobAd={result.jobAd} related={related.kind === "ok" ? related.rows : []} />;
    case "notFound":
      notFound();
    case "unavailable":
      throw new Error("The public job-ad read is unavailable");
  }
}
