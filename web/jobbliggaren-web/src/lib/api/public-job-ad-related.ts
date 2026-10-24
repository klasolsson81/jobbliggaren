import "server-only";
import { cache } from "react";
import { env } from "@/lib/env";
import { parseResponse } from "@/lib/dto/_helpers";
import type { PublicJobAdSummary } from "@/lib/dto/public-job-ad-catalogue";
import { publicJobAdRelatedSchema } from "@/lib/dto/public-job-ad-related";
import { forwardedHeaders } from "@/lib/http/forwarded-headers";
import { isValidId } from "@/lib/validation/guid";

/** The section is secondary content: a stall may delay the ad by this much at most, never fail it. */
const PUBLIC_JOB_AD_RELATED_TIMEOUT_MS = 2_000;

/**
 * What the page needs from the related read (ADR 0182). Two outcomes, because the section has two: rows, or no section.
 * Every failure is `unavailable`, and the page answers 200 without the section, so an outage or a spent budget never
 * reaches the ad itself.
 */
export type PublicJobAdRelatedResult =
  | { readonly kind: "ok"; readonly rows: readonly PublicJobAdSummary[] }
  | { readonly kind: "unavailable" };

/**
 * The related ads under one public job page, read once per render and only from the page body, in parallel with the
 * ad's own read. The client address is relayed through `forwardedHeaders()` so the per-IP budget sees the visitor;
 * `no-store` keeps an erased ad from being served stale (ADR 0106). Any status but 200 is `unavailable`: a 404 here can
 * only be the race after the edge's probe, and the ad's own read decides the page. Nothing in this module logs a
 * title, a company or an id; the lines `parseResponse` writes carry the schema path and the error class, never a value.
 */
export const getPublicJobAdRelated = cache(async (id: string): Promise<PublicJobAdRelatedResult> => {
  if (!isValidId(id)) return { kind: "unavailable" };

  try {
    const res = await fetch(`${env.BACKEND_URL}/api/v1/public/job-ads/${encodeURIComponent(id)}/related`, {
      method: "GET",
      headers: await forwardedHeaders(),
      cache: "no-store",
      redirect: "manual",
      signal: AbortSignal.timeout(PUBLIC_JOB_AD_RELATED_TIMEOUT_MS),
    });
    if (res.status !== 200) {
      await res.body?.cancel();
      return { kind: "unavailable" };
    }
    const related = await parseResponse(res, publicJobAdRelatedSchema, "GET /api/v1/public/job-ads/{id}/related");
    return { kind: "ok", rows: related.items };
  } catch (error) {
    // Next's control flow (a dynamic-rendering bailout) carries a string digest and must pass through.
    if (typeof error === "object" && error !== null && typeof (error as { digest?: unknown }).digest === "string") {
      throw error;
    }
    return { kind: "unavailable" };
  }
});
