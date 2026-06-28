import "server-only";
import { env } from "@/lib/env";
import { getSessionId } from "@/lib/auth/session";
import {
  jobAdMatchBatchSchema,
  jobAdMatchDetailSchema,
  type JobAdMatchBatch,
  type JobAdMatchDetail,
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
 *
 * #300 PR-5 (ADR 0084): `includeRelated` (default false) skickas som BODY-fält.
 * `true` ⇒ backend graderar yrken som LIKNAR de valda som `Related` (rung mellan
 * Basic och Good). AV ⇒ inga related-taggar (behaviour-inert default). Master-
 * switchen härleds ur `?relaterade=on` uppströms och trådas in via callern.
 */
export async function getJobAdMatchTags(
  jobAdIds: ReadonlyArray<string>,
  includeRelated = false
): Promise<JobAdMatchBatch> {
  if (jobAdIds.length === 0) return EMPTY_BATCH;

  const sessionId = await getSessionId();
  if (!sessionId) return EMPTY_BATCH;

  try {
    const res = await fetch(`${env.BACKEND_URL}/api/v1/me/job-ad-match-tags`, {
      method: "POST",
      headers: authHeaders(sessionId),
      body: JSON.stringify({ jobAdIds, includeRelated }),
      cache: "no-store",
    });
    if (!res.ok) return EMPTY_BATCH;
    const data: unknown = await res.json();
    return jobAdMatchBatchSchema.parse(data);
  } catch {
    return EMPTY_BATCH;
  }
}

/**
 * F4-16 (ADR 0076, CTO D3) — matchnings-DETALJ för EN annons (modal/fullsida-
 * sektionen). Speglar `getJobAdMatchTags`-degraderingsmönstret: utan session /
 * !ok / parse-fail / throw → `null` (ingen matchnings-sektion, civil
 * degradering — kastar aldrig, blockerar aldrig modal-renderingen).
 *
 * Backend `GET /api/v1/me/job-ad-match-tags/{jobAdId}` → `JobAdMatchDetailDto |
 * null` (200 med `null`-body = "ingen matchnings-sektion": anonym / ingen
 * träffdata). `.nullable()` fångar det `null`:et och degraderar identiskt med
 * resten.
 *
 * #300 PR-5 (ADR 0084): `includeRelated` (default false) skickas som QUERY-param
 * (`?includeRelated=true`). `true` ⇒ en related-yrkes-annons graderas `Related`
 * (i stället för att inte tjäna in någon grad alls). Trådas in via callern ur
 * `?relaterade=on` så modalen visar samma grad som listans badge. Värdet är
 * "true"/"false" (ASP.NET bool-binding tar inte "1").
 */
export async function getJobAdMatchDetail(
  jobAdId: string,
  includeRelated = false
): Promise<JobAdMatchDetail | null> {
  const sessionId = await getSessionId();
  if (!sessionId) return null;

  try {
    const res = await fetch(
      `${env.BACKEND_URL}/api/v1/me/job-ad-match-tags/${jobAdId}?includeRelated=${includeRelated}`,
      {
        headers: authHeaders(sessionId),
        cache: "no-store",
      }
    );
    if (!res.ok) return null;
    const data: unknown = await res.json();
    return jobAdMatchDetailSchema.nullable().parse(data);
  } catch {
    return null;
  }
}
