import "server-only";

import { getSessionId } from "@/lib/auth/session";
import { authedFetch } from "@/lib/http/authed-fetch";
import { responseToResult, type ApiResult } from "@/lib/dto/_helpers";
import {
  accountDetailsSchema,
  accountSearchResponseSchema,
  type AccountDetailsDto,
  type AccountSearchCriteria,
  type AccountSearchResponse,
} from "@/lib/dto/admin-accounts";

const SEARCH_PATH = "/api/v1/admin/accounts/search";
const SEARCH_CONTEXT = "POST /api/v1/admin/accounts/search";
const DETAIL_CONTEXT = "GET /api/v1/admin/accounts/{id}";

/** The account id's shape: a GUID is checked before it becomes a path segment. */
const ACCOUNT_ID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

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
  if (!ACCOUNT_ID.test(id)) return { kind: "notFound" };
  const sessionId = await getSessionId();
  if (!sessionId) return { kind: "unauthorized" };

  try {
    const res = await authedFetch(sessionId, `/api/v1/admin/accounts/${encodeURIComponent(id)}`, { signal });
    return await responseToResult(res, accountDetailsSchema, DETAIL_CONTEXT, { includeNotFound: true });
  } catch {
    return { kind: "error" };
  }
}
