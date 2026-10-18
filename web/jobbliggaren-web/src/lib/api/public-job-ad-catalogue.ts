import "server-only";
import { cache } from "react";
import { env } from "@/lib/env";
import { parseResponse } from "@/lib/dto/_helpers";
import {
  publicJobAdCataloguePageSchema,
  type PublicJobAdCataloguePage,
} from "@/lib/dto/public-job-ad-catalogue";
import { forwardedHeaders } from "@/lib/http/forwarded-headers";

const PUBLIC_CATALOGUE_TIMEOUT_MS = 5_000;

/**
 * What the catalogue page needs from the API (ADR 0176 F11). Three outcomes: render, a 404, or a 5xx. `unavailable`
 * is a throw at the page and never a `notFound()`: a budget or an outage must not read as a page that does not exist.
 */
export type PublicJobAdCatalogueResult =
  | { readonly kind: "ok"; readonly page: PublicJobAdCataloguePage }
  | { readonly kind: "notFound" }
  | { readonly kind: "unavailable" };

/**
 * One catalogue page, once per render. `cache()` collapses `generateMetadata` and the page into one call; the client
 * address is relayed through `forwardedHeaders()` so the per-IP budget sees the visitor, and nothing else is relayed;
 * `no-store` keeps an erased ad from being served stale. The API's 404 (past the last page) and 400 (outside the cap)
 * are both a 404 for the page. Nothing here logs a title, a company or an id.
 */
export const getPublicJobAdCatalogue = cache(async (page: number): Promise<PublicJobAdCatalogueResult> => {
  try {
    const res = await fetch(`${env.BACKEND_URL}/api/v1/public/job-ads/catalogue?page=${page}`, {
      method: "GET",
      headers: await forwardedHeaders(),
      cache: "no-store",
      redirect: "manual",
      signal: AbortSignal.timeout(PUBLIC_CATALOGUE_TIMEOUT_MS),
    });
    if (res.status === 404 || res.status === 400) {
      await res.body?.cancel();
      return { kind: "notFound" };
    }
    if (!res.ok) {
      await res.body?.cancel();
      return { kind: "unavailable" };
    }
    return {
      kind: "ok",
      page: await parseResponse(res, publicJobAdCataloguePageSchema, "GET /api/v1/public/job-ads/catalogue"),
    };
  } catch (error) {
    // Next's control flow (a dynamic-rendering bailout) carries a string digest and must pass through.
    if (typeof error === "object" && error !== null && typeof (error as { digest?: unknown }).digest === "string") {
      throw error;
    }
    return { kind: "unavailable" };
  }
});
