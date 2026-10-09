import "server-only";

import { getSessionId } from "@/lib/auth/session";
import { authedFetch } from "@/lib/http/authed-fetch";
import { responseToResult, type ApiResult } from "@/lib/dto/_helpers";
import {
  accountDetailsSchema,
  accountSearchResponseSchema,
  pendingEmailChangeSchema,
  type AccountDetailsDto,
  type AccountSearchCriteria,
  type AccountSearchResponse,
  type PendingEmailChangeDto,
} from "@/lib/dto/admin-accounts";

const SEARCH_PATH = "/api/v1/admin/accounts/search";
const SEARCH_CONTEXT = "POST /api/v1/admin/accounts/search";
const DETAIL_CONTEXT = "GET /api/v1/admin/accounts/{id}";
const EMAIL_CHANGE_CONTEXT = "GET /api/v1/admin/accounts/{id}/email-change";

/** The account id's shape: a GUID is checked before it becomes a path segment. */
const ACCOUNT_ID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

export function isAccountId(id: unknown): id is string {
  return typeof id === "string" && ACCOUNT_ID.test(id);
}

/** The path of an account's address change (#1975): its request, its cancel and its read share it. */
export function accountEmailChangePath(id: string): string {
  return `/api/v1/admin/accounts/${encodeURIComponent(id)}/email-change`;
}

/**
 * The wire body for a search. A blank term or a missing status is left out, so the backend reads it as no
 * filter; paging and the sort are always sent.
 */
export function searchBody(criteria: AccountSearchCriteria): Record<string, unknown> {
  const body: Record<string, unknown> = {
    sort: criteria.sort,
    page: criteria.page,
    pageSize: criteria.pageSize,
  };
  const address = criteria.address?.trim();
  if (address) body.address = address;
  if (criteria.status) body.status = criteria.status;
  if (criteria.registeredFrom !== undefined) body.registeredFrom = criteria.registeredFrom;
  if (criteria.registeredBefore !== undefined) body.registeredBefore = criteria.registeredBefore;
  return body;
}

/**
 * One page of all accounts and the status counts, in one call (#1974, ADR 0151). The term travels in the
 * request body, never in a URL.
 */
export async function searchAccounts(
  criteria: AccountSearchCriteria,
  signal?: AbortSignal,
): Promise<ApiResult<AccountSearchResponse>> {
  const sessionId = await getSessionId();
  if (!sessionId) return { kind: "unauthorized" };

  try {
    const res = await authedFetch(sessionId, SEARCH_PATH, {
      method: "POST",
      body: JSON.stringify(searchBody(criteria)),
      signal,
    });
    return await responseToResult(res, accountSearchResponseSchema, SEARCH_CONTEXT);
  } catch {
    return { kind: "error" };
  }
}

/** One account's details, or `notFound` when no account has the id. */
export async function getAccountDetails(
  id: string,
  signal?: AbortSignal,
): Promise<ApiResult<AccountDetailsDto>> {
  if (!isAccountId(id)) return { kind: "notFound" };
  const sessionId = await getSessionId();
  if (!sessionId) return { kind: "unauthorized" };

  try {
    const res = await authedFetch(sessionId, `/api/v1/admin/accounts/${encodeURIComponent(id)}`, { signal });
    return await responseToResult(res, accountDetailsSchema, DETAIL_CONTEXT, { includeNotFound: true });
  } catch {
    return { kind: "error" };
  }
}

/**
 * The account's pending address change (#1975, ADR 0153), or null when it has none. A read of its own, so a fault
 * on the volatile instance costs this one fact and never the account's details (ADR 0150 D2).
 */
export async function getPendingEmailChange(
  id: string,
  signal?: AbortSignal,
): Promise<ApiResult<PendingEmailChangeDto | null>> {
  if (!isAccountId(id)) return { kind: "notFound" };
  const sessionId = await getSessionId();
  if (!sessionId) return { kind: "unauthorized" };

  try {
    const res = await authedFetch(sessionId, accountEmailChangePath(id), { signal });
    // Nothing pending is no content, never a 404: the account may well exist.
    if (res.status === 204) return { kind: "ok", data: null };
    return await responseToResult(res, pendingEmailChangeSchema, EMAIL_CHANGE_CONTEXT);
  } catch {
    return { kind: "error" };
  }
}
