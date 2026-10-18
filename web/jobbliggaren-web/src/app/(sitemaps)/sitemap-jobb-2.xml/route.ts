import { jobSitemapPartResponse } from "@/lib/seo/sitemap-responses";

/** Part 2 of the job sitemap (ADR 0174), read per request. The part is this file's, never the request's. */
export const dynamic = "force-dynamic";

export function GET(request: Request): Promise<Response> {
  return jobSitemapPartResponse(2, request);
}
