import "server-only";
import { env } from "@/lib/env";
import { getSessionId } from "@/lib/auth/session";
import {
  hasAppliedSchema,
  jobAdStatusBatchSchema,
  type JobAdStatusBatch,
} from "@/lib/dto/job-ad-status";

function authHeaders(sessionId: string): HeadersInit {
  return {
    Authorization: `Bearer ${sessionId}`,
    "Content-Type": "application/json",
  };
}

/**
 * ADR 0063 — batch-status för `/jobb`-listans Sparad/Har-ansökt-taggar.
 * Anonym/utan-session → tom batch (no 401-friktion på publik söksida).
 * Backend validator capps:ar batchen vid 100 IDs.
 */
export async function getJobAdStatusBatch(
  jobAdIds: ReadonlyArray<string>
): Promise<JobAdStatusBatch> {
  if (jobAdIds.length === 0) return { savedIds: [], appliedIds: [] };

  const sessionId = await getSessionId();
  if (!sessionId) return { savedIds: [], appliedIds: [] };

  try {
    const res = await fetch(`${env.BACKEND_URL}/api/v1/me/job-ad-status`, {
      method: "POST",
      headers: authHeaders(sessionId),
      body: JSON.stringify({ jobAdIds }),
      cache: "no-store",
    });
    if (!res.ok) return { savedIds: [], appliedIds: [] };
    const data = await res.json();
    return jobAdStatusBatchSchema.parse(data);
  } catch {
    return { savedIds: [], appliedIds: [] };
  }
}

/**
 * ADR 0063 — single has-applied för modal-footer initial-state.
 * Misslyckad lookup → false (toggle visas ändå, klick kan retry).
 */
export async function hasAppliedJobAd(jobAdId: string): Promise<boolean> {
  const sessionId = await getSessionId();
  if (!sessionId) return false;

  try {
    const res = await fetch(
      `${env.BACKEND_URL}/api/v1/me/applications/has-applied/${jobAdId}`,
      { headers: authHeaders(sessionId), cache: "no-store" }
    );
    if (!res.ok) return false;
    const data = await res.json();
    return hasAppliedSchema.parse(data).hasApplied;
  } catch {
    return false;
  }
}
