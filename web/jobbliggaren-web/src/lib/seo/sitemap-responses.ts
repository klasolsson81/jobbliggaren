import "server-only";
import { getPublicJobAdIds } from "@/lib/api/public-job-ad-ids";
import { pickForwardedHeaders } from "@/lib/http/forwarded-headers";
import { publicJobAdUrl } from "@/lib/job-ads/public-job-ad-url";
import { RETRY_AFTER_DEFAULT_SECONDS } from "@/lib/job-ads/public-job-ad-answer";
import { sitemapIndexLocs } from "@/lib/seo/job-sitemap";
import { marketingSitemapLocs } from "@/lib/seo/marketing-sitemap";
import { MAX_URLS_PER_SITEMAP, sitemapIndexXml, urlsetXml } from "@/lib/seo/sitemap-xml";

/**
 * The answers of the sitemap's four files (ADR 0174). Every answer is `no-store`: the API's bound is the only window,
 * and no cache downstream may stretch it. A job part that cannot be read is a 503 with `Retry-After` and no body,
 * never an empty part, a 404 or a 410, because an outage must not read as nothing listed or as a removal.
 */
function xml(body: string): Response {
  return new Response(body, {
    status: 200,
    headers: { "Content-Type": "application/xml; charset=utf-8", "Cache-Control": "no-store" },
  });
}

function unavailable(retryAfterSeconds: number): Response {
  return new Response(null, {
    status: 503,
    headers: { "Retry-After": String(retryAfterSeconds), "Cache-Control": "no-store" },
  });
}

/** The index reads no data. */
export function sitemapIndexResponse(): Response {
  return xml(sitemapIndexXml(sitemapIndexLocs()));
}

/** The marketing part reads no data. */
export function marketingSitemapResponse(): Response {
  return xml(urlsetXml(marketingSitemapLocs()));
}

/**
 * One job part. `part` is the route file's own and never read from the request; only the request's forwarding headers
 * are relayed. A part over a file's limit is refused, with a count and nothing else in the log.
 */
export async function jobSitemapPartResponse(part: number, request: Request): Promise<Response> {
  const result = await getPublicJobAdIds(part, pickForwardedHeaders(request.headers));
  if (result.kind === "unavailable") return unavailable(result.retryAfterSeconds);

  if (result.ids.length > MAX_URLS_PER_SITEMAP) {
    // eslint-disable-next-line no-console
    console.error("Job sitemap part over the per-file limit, answered 503", { part, count: result.ids.length });
    return unavailable(RETRY_AFTER_DEFAULT_SECONDS);
  }
  return xml(urlsetXml(result.ids.map(publicJobAdUrl)));
}
