import "server-only";

import { NextResponse } from "next/server";
import type { ApiResult } from "@/lib/dto/_helpers";
import {
  ACCOUNT_SEARCH_STATUSES,
  ACCOUNT_SORTS,
  type AccountSearchCriteria,
  type AccountSearchStatus,
  type AccountSort,
} from "@/lib/dto/admin-accounts";
import { isSameOriginRequest } from "@/lib/security/same-origin";

/**
 * The account directory's route handlers share this guard and this relay (#1974, ADR 0151). A body that
 * names a person is never echoed and never logged: every answer is a fixed code or the backend's own data,
 * and none is stored.
 */
const NO_STORE = { "Cache-Control": "no-store" } as const;

/** The longest address an account can hold; the backend refuses a longer term the same way. */
const MAX_TERM = 256;
const MAX_PAGE_SIZE = 100;

export function refuse(status: number, error: string, headers: Readonly<Record<string, string>> = {}): NextResponse {
  return NextResponse.json({ error }, { status, headers: { ...NO_STORE, ...headers } });
}

/**
 * The request's JSON body, or a fixed refusal: another origin is refused, and so is any other content
 * type, which also forces a preflight from any other origin. A body that does not parse is refused without
 * its text reaching an exception.
 */
export async function readAdminBody(
  request: Request,
): Promise<{ readonly body: Record<string, unknown> } | { readonly refusal: NextResponse }> {
  if (!isSameOriginRequest(request)) return { refusal: refuse(403, "forbidden") };
  if (!request.headers.get("content-type")?.toLowerCase().startsWith("application/json")) {
    return { refusal: refuse(415, "unsupported") };
  }
  const body: unknown = await request.json().catch(() => null);
  if (typeof body !== "object" || body === null || Array.isArray(body)) {
    return { refusal: refuse(400, "invalid") };
  }
  return { body: body as Record<string, unknown> };
}

/** The island's search request, or null when a field has the wrong shape. Missing fields take their defaults. */
export function parseSearchCriteria(body: Readonly<Record<string, unknown>>): AccountSearchCriteria | null {
  const { address, status, sort, page = 1, pageSize = 25 } = body;
  if (address !== undefined && (typeof address !== "string" || address.length > MAX_TERM)) return null;
  if (status !== undefined && !ACCOUNT_SEARCH_STATUSES.includes(status as AccountSearchStatus)) return null;
  if (sort !== undefined && !ACCOUNT_SORTS.includes(sort as AccountSort)) return null;
  if (!Number.isInteger(page) || (page as number) < 1) return null;
  if (!Number.isInteger(pageSize) || (pageSize as number) < 1 || (pageSize as number) > MAX_PAGE_SIZE) return null;
  return {
    address: address as string | undefined,
    status: status as AccountSearchStatus | undefined,
    sort: (sort as AccountSort | undefined) ?? "RegisteredNewest",
    page: page as number,
    pageSize: pageSize as number,
  };
}

/** The backend's answer as the island reads it: its data, or a fixed code. */
export function relay<T>(result: ApiResult<T>): NextResponse {
  switch (result.kind) {
    case "ok":
      return NextResponse.json(result.data, { headers: NO_STORE });
    case "unauthorized":
      return refuse(401, "unauthorized");
    case "forbidden":
      return refuse(403, "forbidden");
    case "notFound":
      return refuse(404, "notFound");
    case "rateLimited":
      return refuse(429, "rateLimited", { "Retry-After": String(result.retryAfterSeconds) });
    default:
      return refuse(502, "error");
  }
}
