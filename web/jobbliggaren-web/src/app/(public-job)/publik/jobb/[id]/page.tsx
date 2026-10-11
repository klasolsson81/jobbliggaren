import { notFound } from "next/navigation";
import { getFormatter, getLocale, getTranslations } from "next-intl/server";
import type { Metadata } from "next";
import { PublicJobAd } from "@/components/job-ads/public-job-ad";
import { getPublicJobAd } from "@/lib/api/public-job-ad";
import { formatDate } from "@/lib/i18n/format";
import { notFoundMetadata } from "@/lib/metadata/not-found-title";
import { absoluteUrl } from "@/lib/site-url";

interface PageProps {
  params: Promise<{ id: string }>;
}

/**
 * The public job page (ADR 0165): an internal page behind `proxy.ts`, which rewrites a cookieless `/jobb/<uuid>` here
 * after asking the public read whether the ad is listed. The address in the browser stays `/jobb/<uuid>`.
 *
 * One read per render (`getPublicJobAd` is wrapped in `cache()` and shared with `generateMetadata`; ADR 0160 D9.4).
 * A 404 or 410 (the ad ended after the edge's 200) is a real 404; every other outcome throws and is a 5xx, so an
 * outage never reads as a removal (D9.5).
 *
 * Title, description and OpenGraph are built from structured fields only, never from the ad text: a link preview is
 * a copy held by a third party, and the free text can name a person.
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

  const documentTitle = `${jobAd.title} – ${jobAd.companyName}`;
  const description =
    deadline === null
      ? t("meta.description", { company: jobAd.companyName, title: jobAd.title, published })
      : t("meta.descriptionWithDeadline", { company: jobAd.companyName, title: jobAd.title, published, deadline });
  const url = absoluteUrl(`/jobb/${jobAd.id}`);

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
      // A page's own `openGraph` replaces the root's wholesale, file-based image and locale included (measured in
      // the production build), so both are named here. The image is the one `app/opengraph-image.tsx` renders.
      images: [{ url: "/opengraph-image", width: 1200, height: 630 }],
    },
  };
}

export default async function PublicJobPage({ params }: PageProps) {
  const { id } = await params;
  const result = await getPublicJobAd(id);

  switch (result.kind) {
    case "ok":
      return <PublicJobAd jobAd={result.jobAd} />;
    case "notFound":
      notFound();
    case "unavailable":
      throw new Error("The public job-ad read is unavailable");
  }
}
