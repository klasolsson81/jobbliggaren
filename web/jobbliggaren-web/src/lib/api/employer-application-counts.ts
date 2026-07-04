import "server-only";
import { getSessionId } from "@/lib/auth/session";
import { authedFetch } from "@/lib/http/authed-fetch";
import {
  employerApplicationCountBatchSchema,
  type EmployerApplicationCountBatch,
} from "@/lib/dto/employer-application-counts";

const EMPTY_BATCH: EmployerApplicationCountBatch = { countsByJobAdId: {} };

/**
 * #446 (#311) — batch-räknare för /jobb-listans "Du har X tidigare ansökningar
 * till detta företag"-badge. Speglar `getJobAdStatusBatch`/`getJobAdMatchTags`:
 * anonym/utan-session/!ok/throw → tom batch (ingen 401-friktion, ingen badge —
 * civil degradering). Backend-validatorn cap:ar batchen vid 100 IDs.
 *
 * POSITIVE-ONLY: bara annonser med en räknare > 0 finns i `countsByJobAdId` —
 * frånvarande annons ⇒ ingen badge. org.nr färdas aldrig (server-side
 * GROUP-nyckel); request bär bara JobAdIds, svaret bara heltal.
 */
export async function getEmployerApplicationCounts(
  jobAdIds: ReadonlyArray<string>
): Promise<EmployerApplicationCountBatch> {
  if (jobAdIds.length === 0) return EMPTY_BATCH;

  const sessionId = await getSessionId();
  if (!sessionId) return EMPTY_BATCH;

  try {
    const res = await authedFetch(
      sessionId,
      "/api/v1/me/application-history/counts",
      {
        method: "POST",
        body: JSON.stringify({ jobAdIds }),
      }
    );
    if (!res.ok) return EMPTY_BATCH;
    const data: unknown = await res.json();
    return employerApplicationCountBatchSchema.parse(data);
  } catch {
    return EMPTY_BATCH;
  }
}
