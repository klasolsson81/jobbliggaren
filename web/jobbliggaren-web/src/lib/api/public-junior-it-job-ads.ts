import "server-only";
import { cache } from "react";
import { env } from "@/lib/env";
import { parseResponse } from "@/lib/dto/_helpers";
import {
  publicJuniorItJobAdsPageSchema,
  type PublicJuniorItJobAdsPage,
} from "@/lib/dto/public-junior-it-job-ads";
import { forwardedHeaders } from "@/lib/http/forwarded-headers";
import type { JuniorItOrder } from "@/lib/job-ads/junior-it-params";

const PUBLIC_JUNIOR_IT_TIMEOUT_MS = 5_000;

/**
 * What `/junior` needs from the API (ADR 0186). Three outcomes: render, a 404, or a 5xx. `unavailable` is a throw at the
 * page and never a `notFound()`: a budget or an outage must not read as a page that does not exist.
 */
export type PublicJuniorItJobAdsResult =
  | { readonly kind: "ok"; readonly page: PublicJuniorItJobAdsPage }
  | { readonly kind: "notFound" }
  | { readonly kind: "unavailable" };

/**
 * One page of the list, once per render: `cache()` collapses `generateMetadata` and the page into one call when they
 * pass the same three arguments. The client address is relayed through `forwardedHeaders()` so the per-IP budget sees
 * the visitor; `no-store` keeps an ad that has closed from being served stale. The API's 404 (past the last page) and
 * 400 (a value it refuses) are both a 404 for the page. Nothing here logs a title, a company or an id.
 */
export const getPublicJuniorItJobAds = cache(
  async (municipality: string | null, sortBy: JuniorItOrder, page: number): Promise<PublicJuniorItJobAdsResult> => {
    const search = new URLSearchParams({ sortBy, page: String(page) });
    if (municipality !== null) search.set("municipality", municipality);
    try {
      const res = await fetch(`${env.BACKEND_URL}/api/v1/public/job-ads/junior?${search.toString()}`, {
        method: "GET",
        headers: await forwardedHeaders(),
        cache: "no-store",
        redirect: "manual",
        signal: AbortSignal.timeout(PUBLIC_JUNIOR_IT_TIMEOUT_MS),
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
        page: await parseResponse(res, publicJuniorItJobAdsPageSchema, "GET /api/v1/public/job-ads/junior"),
      };
    } catch (error) {
      // Next's control flow (a dynamic-rendering bailout) carries a string digest and must pass through.
      if (typeof error === "object" && error !== null && typeof (error as { digest?: unknown }).digest === "string") {
        throw error;
      }
      return { kind: "unavailable" };
    }
  },
);
