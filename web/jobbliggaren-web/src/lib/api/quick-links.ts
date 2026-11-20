import "server-only";
import { getSessionId } from "@/lib/auth/session";
import { authedFetch } from "@/lib/http/authed-fetch";
import { readProblemTitle } from "@/lib/http/problem";
import { parseRetryAfter } from "@/lib/dto/_helpers";
import { QUICK_LINK_FULL_CODE } from "@/lib/job-ads/quick-links";
import { isValidId } from "@/lib/validation/guid";

/**
 * ADR 0188 (#2265) — the outcome of a quick-link write. The API answers 204 on success and 409 for two different
 * reasons that the user must hear apart: the cap of three is reached (`QuickLink.MaxTotal`), or a write that raced this
 * one won (`QuickLink.SlotTaken`, or `Concurrency.Conflict` from the replay cap). The problem's `title` carries which.
 */
export type QuickLinkWriteResult =
  | { kind: "ok" }
  | { kind: "full" }
  | { kind: "conflict" }
  | { kind: "notFound" }
  | { kind: "unauthorized" }
  | { kind: "rateLimited"; retryAfterSeconds: number }
  | { kind: "error" };

async function toWriteResult(res: Response): Promise<QuickLinkWriteResult> {
  if (res.status === 204) return { kind: "ok" };
  if (res.status === 401) return { kind: "unauthorized" };
  if (res.status === 404) return { kind: "notFound" };
  if (res.status === 429) {
    return { kind: "rateLimited", retryAfterSeconds: parseRetryAfter(res.headers.get("Retry-After")) };
  }
  if (res.status === 409) {
    return (await readProblemTitle(res)) === QUICK_LINK_FULL_CODE ? { kind: "full" } : { kind: "conflict" };
  }
  return { kind: "error" };
}

async function write(
  path: string,
  init: Omit<RequestInit, "cache" | "headers">,
): Promise<QuickLinkWriteResult> {
  const sessionId = await getSessionId();
  if (!sessionId) return { kind: "unauthorized" };

  try {
    return await toWriteResult(await authedFetch(sessionId, path, init));
  } catch {
    return { kind: "error" };
  }
}

/** Shows or hides the junior quick link: an idempotent `PUT` of `{ enabled }`. */
export function setJuniorQuickLink(enabled: boolean): Promise<QuickLinkWriteResult> {
  return write("/api/v1/me/junior-quick-link", { method: "PUT", body: JSON.stringify({ enabled }) });
}

/**
 * Makes one of the user's recent searches a quick link (`PUT`), or stops it being one (`DELETE`). The id only: a
 * non-GUID never reaches the backend URL (SSRF and path-injection barrier) and cannot exist, so it is a 404.
 */
export async function setRecentSearchQuickLink(id: string, on: boolean): Promise<QuickLinkWriteResult> {
  if (!isValidId(id)) return { kind: "notFound" };
  return write(`/api/v1/me/recent-searches/${encodeURIComponent(id)}/quick-link`, { method: on ? "PUT" : "DELETE" });
}
