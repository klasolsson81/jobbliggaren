import "server-only";
import { getSessionId } from "@/lib/auth/session";
import { authedFetch } from "@/lib/http/authed-fetch";
import { jobsWatermarkSchema, type JobsWatermark } from "@/lib/dto/me-jobs";
import { responseToResult, type ApiResult } from "@/lib/dto/_helpers";

/**
 * #293 / #306 (ADR 0042 Beslut E-amendment 2026-06-28) — användarens
 * oläst-watermark för /jobb. Konsumerar `GET /api/v1/me/jobs/watermark`
 * (auth-gated → `401` för anon). Speglar `me-matches.ts`-mönstret (ADR 0080):
 * en skalär tidsstämpel som FE jämför mot `jobAd.createdAt` för att rendera
 * NY = oläst (`lastSeenJobsAt != null && createdAt > lastSeenJobsAt`).
 *
 * `lastSeenJobsAt == null` (kall start / anon) → ingen NY (W4). En anonym
 * begäran (401) degraderar civilt till `unauthorized` — konsumenten faller
 * då till "ingen watermark" ⇒ ingen NY (cold-start-semantik).
 */
export async function getJobsWatermark(): Promise<ApiResult<JobsWatermark>> {
  const sessionId = await getSessionId();
  if (!sessionId) return { kind: "unauthorized" };

  try {
    const res = await authedFetch(sessionId, "/api/v1/me/jobs/watermark");
    return await responseToResult(
      res,
      jobsWatermarkSchema,
      "GET /api/v1/me/jobs/watermark"
    );
  } catch {
    return { kind: "error" };
  }
}

/**
 * #293 / #306 — avancera /jobb:s oläst-watermark (markera listan sedd).
 * Anropas vid /jobb-laddning EFTER att listan + watermarken hämtats (fetch-
 * then-mark, spegling av `markMatchesSeen` på /matchningar): NY renderas mot
 * den HÄMTADE (gamla) watermarken, sedan flyttas den fram till nu så nästa
 * besök bara visar nytt-sedan-detta-besök. `POST /api/v1/me/jobs/seen` → 204
 * (auth-gated; monoton — flyttar aldrig watermarken bakåt).
 *
 * Idempotent och icke-kritisk: ett fel får ALDRIG blockera sid-renderingen
 * (watermarken flyttas då bara inte fram denna gång) — degraderar civilt likt
 * `markMatchesSeen`/`saveJobAd`. 204 → ok; allt annat → fel-kind.
 */
export async function markJobsSeen(): Promise<ApiResult<void>> {
  const sessionId = await getSessionId();
  if (!sessionId) return { kind: "unauthorized" };

  try {
    const res = await authedFetch(sessionId, "/api/v1/me/jobs/seen", {
      method: "POST",
    });
    if (res.status === 204) return { kind: "ok", data: undefined };
    if (res.status === 401) return { kind: "unauthorized" };
    if (res.status === 403) return { kind: "forbidden" };
    return { kind: "error" };
  } catch {
    return { kind: "error" };
  }
}
