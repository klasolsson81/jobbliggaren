import "server-only";
import { env } from "@/lib/env";
import { publicJobAdIdsSchema } from "@/lib/dto/public-job-ad-ids";
import { boundedRetryAfter, RETRY_AFTER_DEFAULT_SECONDS } from "@/lib/job-ads/public-job-ad-answer";
import { JOB_SITEMAP_PART_COUNT } from "@/lib/seo/job-sitemap";

const PUBLIC_JOB_AD_IDS_TIMEOUT_MS = 5_000;

/** A part's ids, or a wait. There is no third outcome: an outage must never read as an empty part (ADR 0120). */
export type PublicJobAdIdsResult =
  | { readonly kind: "ok"; readonly ids: readonly string[] }
  | { readonly kind: "unavailable"; readonly retryAfterSeconds: number };

/**
 * The ids of one job-sitemap part (ADR 0174), read once per request. The API owns the freshness bound, so the web
 * caches nothing, and it sends exactly the forwarding headers it is handed, so the API's per-IP budget sees the
 * crawler and no cookie or `Authorization` can reach it.
 *
 * A 429 or 503 keeps the API's wait, bounded. Every other failure gets the default wait: a timeout, a network error,
 * any other status, or a body that does not parse. Nothing here logs an id or a body; a refused body is logged by its
 * kind alone.
 */
export async function getPublicJobAdIds(
  part: number,
  forwarded: Readonly<Record<string, string>>
): Promise<PublicJobAdIdsResult> {
  try {
    const res = await fetch(
      `${env.BACKEND_URL}/api/v1/public/job-ads/listed-ids?part=${part}&parts=${JOB_SITEMAP_PART_COUNT}`,
      {
        method: "GET",
        headers: { ...forwarded },
        cache: "no-store",
        redirect: "manual",
        signal: AbortSignal.timeout(PUBLIC_JOB_AD_IDS_TIMEOUT_MS),
      }
    );
    if (res.status === 429 || res.status === 503) {
      await res.body?.cancel();
      return { kind: "unavailable", retryAfterSeconds: boundedRetryAfter(res.headers.get("retry-after")) };
    }
    if (res.status !== 200) {
      await res.body?.cancel();
      return { kind: "unavailable", retryAfterSeconds: RETRY_AFTER_DEFAULT_SECONDS };
    }

    const parsed = publicJobAdIdsSchema.safeParse(await res.json());
    if (!parsed.success) {
      // eslint-disable-next-line no-console
      console.error("Job sitemap part refused: the listed-ids body does not match its schema", { part });
      return { kind: "unavailable", retryAfterSeconds: RETRY_AFTER_DEFAULT_SECONDS };
    }
    return { kind: "ok", ids: parsed.data.ids };
  } catch (error) {
    // Next's control flow carries a string digest and must pass through.
    if (typeof error === "object" && error !== null && typeof (error as { digest?: unknown }).digest === "string") {
      throw error;
    }
    return { kind: "unavailable", retryAfterSeconds: RETRY_AFTER_DEFAULT_SECONDS };
  }
}
