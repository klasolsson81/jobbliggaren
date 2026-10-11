import { notFound } from "next/navigation";
import { getFormatter, getTranslations } from "next-intl/server";
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
 * Den publika jobbsidan (ADR 0165): intern sida bakom `proxy.ts`, som skriver om en cookielös `/jobb/<uuid>` hit
 * efter att ha frågat den publika läsningen om annonsen är listad. URL:en förblir `/jobb/<uuid>`.
 *
 * Läsningen sker en gång per rendering (`getPublicJobAd` är `cache()`-lindad och delas med `generateMetadata`,
 * ADR 0160 D9.4), per request med klientens IP vidarebefordrad och utan cache. Svaret avgör bara om sidan
 * renderas: 404 och 410 (annonsen hann upphöra efter kantens 200) blir en riktig 404, och allt annat, alltså 429,
 * 5xx, timeout och ett svar som inte går att läsa, kastar och blir ett 5xx. Ett avbrott får aldrig läsas som en
 * borttagning (D9.5): Google tappar en URL som svarar 404 och saktar bara ned vid 429 och 5xx.
 *
 * Titel, beskrivning och OG byggs av strukturerade fält (titel, företag, datum), aldrig ur annonstexten:
 * en länkförhandsvisning är en kopia hos tredje part som Art. 17(2)-steget inte når, och fritexten kan bära namn.
 */
export async function generateMetadata({ params }: PageProps): Promise<Metadata> {
  const { id } = await params;
  const result = await getPublicJobAd(id);
  if (result.kind !== "ok") return notFoundMetadata();

  const { jobAd } = result;
  const t = await getTranslations("publicjob");
  const tMeta = await getTranslations("metadata");
  const format = await getFormatter();
  const published = formatDate(format, jobAd.publishedAt) ?? "";
  const deadline = formatDate(format, jobAd.expiresAt);

  const documentTitle = `${jobAd.title} – ${jobAd.companyName}`;
  const description =
    deadline === null
      ? t("meta.description", { company: jobAd.companyName, title: jobAd.title, published })
      : t("meta.descriptionWithDeadline", { company: jobAd.companyName, title: jobAd.title, published, deadline });
  const url = absoluteUrl(`/jobb/${jobAd.id.toLowerCase()}`);

  return {
    title: documentTitle,
    description,
    alternates: { canonical: url },
    robots: { index: true, follow: true },
    openGraph: {
      type: "website",
      siteName: tMeta("applicationName"),
      title: documentTitle,
      description,
      url,
      // A page that sets its own `openGraph` replaces the root's wholesale, file-based image included (measured
      // in the production build, `public-job-smoke.mjs`), so the shared image is named here. It is the one
      // `app/opengraph-image.tsx` renders; a per-ad image is out of scope (epic #2090).
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
