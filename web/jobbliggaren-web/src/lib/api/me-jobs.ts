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
 * den HÄMTADE (gamla) watermarken, sedan flyttas den fram till det HÄMTADE
 * fönstret så nästa besök bara visar nytt-sedan-detta-besök. `POST
 * /api/v1/me/jobs/seen` → 204 (auth-gated; monoton — flyttar aldrig
 * watermarken bakåt).
 *
 * `seenThrough` = `max(createdAt)` över annonserna vi FAKTISKT renderade på den
 * laddade sidan (#759, syskon till #477 Low 4). Till skillnad från den nyast-
 * först-sorterade matchningslistan kan /jobb vara relevans-/matchrank-sorterad,
 * så vi tar MAX över sidan, inte `list[0]`. Vattenmärket sätts dit, inte till
 * klock-nu — en annons som skapas mellan hämtningen och detta anrop
 * (`createdAt > seenThrough`) förblir korrekt flaggad "Ny". Utelämnad (tom
 * lista / deploy-skew) → backend faller tillbaka på nu.
 *
 * Idempotent och icke-kritisk: ett fel får ALDRIG blockera sid-renderingen
 * (watermarken flyttas då bara inte fram denna gång) — degraderar civilt likt
 * `markMatchesSeen`/`saveJobAd`. 204 → ok; allt annat → fel-kind.
 *
 * `session` = a pre-resolved sessionId. Omitted (default) → read here via
 * `getSessionId()` (cookies), as before. Passed in when the call is deferred off
 * the render path with `after()`: an `after()` callback in a Server Component
 * CANNOT read cookies (Next docs), so the caller reads the session DURING render
 * and passes it in (#741).
 */
export async function markJobsSeen(
  seenThrough?: string,
  session?: string | null,
): Promise<ApiResult<void>> {
  const sessionId = session === undefined ? await getSessionId() : session;
  if (!sessionId) return { kind: "unauthorized" };

  try {
    const res = await authedFetch(sessionId, "/api/v1/me/jobs/seen", {
      method: "POST",
      ...(seenThrough ? { body: JSON.stringify({ seenThrough }) } : {}),
    });
    if (res.status === 204) return { kind: "ok", data: undefined };
    if (res.status === 401) return { kind: "unauthorized" };
    if (res.status === 403) return { kind: "forbidden" };
    return { kind: "error" };
  } catch {
    return { kind: "error" };
  }
}
