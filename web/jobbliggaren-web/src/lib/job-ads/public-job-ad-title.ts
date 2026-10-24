import type { PublicJobAdDto } from "@/lib/dto/public-job-ad";

/**
 * The title of a public job page: `og:title` (ADR 0165 D4) and the title the share row hands on (ADR 0183), so the two
 * are one string. Structured fields only, never the ad text.
 */
export function publicJobAdTitle(jobAd: Pick<PublicJobAdDto, "title" | "companyName">): string {
  return `${jobAd.title} – ${jobAd.companyName}`;
}
