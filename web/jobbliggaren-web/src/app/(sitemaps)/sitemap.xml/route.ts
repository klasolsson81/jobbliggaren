import { sitemapIndexResponse } from "@/lib/seo/sitemap-responses";

/** The sitemap index robots.txt names (ADR 0174): the marketing part and the job parts. It reads no data. */
export const dynamic = "force-dynamic";

export function GET(): Response {
  return sitemapIndexResponse();
}
