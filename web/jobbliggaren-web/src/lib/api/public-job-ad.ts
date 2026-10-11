import "server-only";
import { cache } from "react";
import { env } from "@/lib/env";
import { parseResponse } from "@/lib/dto/_helpers";
import { publicJobAdDtoSchema, type PublicJobAdDto } from "@/lib/dto/public-job-ad";
import { forwardedHeaders } from "@/lib/http/forwarded-headers";
import { isValidId } from "@/lib/validation/guid";

const PUBLIC_JOB_AD_TIMEOUT_MS = 5_000;

/**
 * What the render needs from the public read (ADR 0160, ADR 0165). Three outcomes, because the page can
 * answer three ways: render, a 404, or a 5xx. `unavailable` is a throw at the page and never a `notFound()`:
 * Google drops a URL that answers 404 and only slows down on 429 and 5xx, so a budget or an outage must not
 * read as a removal (D9.5).
 */
export type PublicJobAdResult =
  | { readonly kind: "ok"; readonly jobAd: PublicJobAdDto }
  | { readonly kind: "notFound" }
  | { readonly kind: "unavailable" };

/**
 * The anonymous per-record read, once per render. `cache()` collapses `generateMetadata` and the page into one
 * call (ADR 0160 D9.4); the client address is relayed through `forwardedHeaders()` so the per-IP budget sees the
 * visitor (D5); and `no-store` keeps an erased ad from being served stale (D6, ADR 0106).
 *
 * A 404 or a 410 here can only be the race after the edge's probe answered 200 (the ad ended in between), and
 * both are a 404 for the page. Nothing in this module logs a title, a company or a description: the only
 * log line is `parseResponse`'s shape-mismatch line, which carries the schema path and no value.
 */
export const getPublicJobAd = cache(async (id: string): Promise<PublicJobAdResult> => {
  if (!isValidId(id)) return { kind: "notFound" };

  try {
    const res = await fetch(`${env.BACKEND_URL}/api/v1/public/job-ads/${encodeURIComponent(id)}`, {
      method: "GET",
      headers: await forwardedHeaders(),
      cache: "no-store",
      redirect: "manual",
      signal: AbortSignal.timeout(PUBLIC_JOB_AD_TIMEOUT_MS),
    });
    if (res.status === 404 || res.status === 410) {
      await res.body?.cancel();
      return { kind: "notFound" };
    }
    if (!res.ok) {
      await res.body?.cancel();
      return { kind: "unavailable" };
    }
    return { kind: "ok", jobAd: await parseResponse(res, publicJobAdDtoSchema, "GET /api/v1/public/job-ads/{id}") };
  } catch (error) {
    // Next's control flow (a dynamic-rendering bailout) carries a string digest and must pass through.
    if (typeof error === "object" && error !== null && typeof (error as { digest?: unknown }).digest === "string") {
      throw error;
    }
    return { kind: "unavailable" };
  }
});
