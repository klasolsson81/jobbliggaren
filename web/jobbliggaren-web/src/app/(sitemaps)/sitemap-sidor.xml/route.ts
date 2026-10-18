import { marketingSitemapResponse } from "@/lib/seo/sitemap-responses";

/** The marketing part of the sitemap (ADR 0174). It reads no data. */
export const dynamic = "force-dynamic";

export function GET(): Response {
  return marketingSitemapResponse();
}
