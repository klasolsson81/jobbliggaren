import "server-only";
import { env } from "@/lib/env";
import { getSessionId } from "@/lib/auth/session";
import {
  jobAdMatchBatchSchema,
  type JobAdMatchBatch,
} from "@/lib/dto/job-ad-match";

const EMPTY_BATCH: JobAdMatchBatch = { entries: {} };

function authHeaders(sessionId: string): HeadersInit {
  return {
    Authorization: `Bearer ${sessionId}`,
    "Content-Type": "application/json",
  };
}

/**
 * F4-13 (ADR 0076) — batch graderade match-taggar för `/jobb`-listan. Speglar
 * `getJobAdStatusBatch` (ADR 0063): anonym/utan-session/!ok/throw → tom batch
 * (ingen 401-friktion på den publika söksidan, inga taggar visas — civil
 * degradering). Backend-validator cap:ar batchen vid 100 IDs.
 *
 * POSITIVE-ONLY: bara annonser som tjänade in en positiv grad finns i
 * `entries` — frånvarande annons ⇒ ingen tagg. FE behöver därför ingen
 * "ingen match"-logik.
 */
export async function getJobAdMatchTags(
  jobAdIds: ReadonlyArray<string>
): Promise<JobAdMatchBatch> {
  if (jobAdIds.length === 0) return EMPTY_BATCH;

  const sessionId = await getSessionId();
  if (!sessionId) return EMPTY_BATCH;

  try {
    const res = await fetch(`${env.BACKEND_URL}/api/v1/me/job-ad-match-tags`, {
      method: "POST",
      headers: authHeaders(sessionId),
      body: JSON.stringify({ jobAdIds }),
      cache: "no-store",
    });
    if (!res.ok) return EMPTY_BATCH;
    const data: unknown = await res.json();
    return jobAdMatchBatchSchema.parse(data);
  } catch {
    return EMPTY_BATCH;
  }
}
