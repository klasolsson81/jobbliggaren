import "server-only";
import { getSessionId } from "@/lib/auth/session";
import { authedFetch } from "@/lib/http/authed-fetch";
import {
  companySearchResponseSchema,
  type CompanySearchCriteria,
  type CompanySearchResponse,
} from "@/lib/dto/company-search";
import { responseToResult, type ApiResult } from "@/lib/dto/_helpers";

/**
 * #560 PR-B — server-only fetchers for the general company-register search (`POST
 * /api/v1/companies/search`). POST-as-read: the search predicate travels in the BODY, never a URL
 * (ADR 0087 D8(c) — a sole-prop org.nr can equal a personnummer and must never reach an access log;
 * translating even the shareable name/SNI/kommun axes into a body keeps the backend URL clean too).
 * `authedFetch` forces `cache: "no-store"`, matching the endpoint's `Cache-Control: private, no-store`.
 *
 * TWO disjoint entry points over the one endpoint, by design:
 * - {@link searchCompanies} takes {@link CompanySearchCriteria}, which has NO `organizationNumber`
 *   field — so the URL-reflected RSC search graph is structurally incapable of forwarding an org.nr.
 * - {@link searchCompanyByOrgNr} is the BFF-only org.nr lookup, reached only from the
 *   `/api/foretag/sok` route handler after it has refused personnummer-shaped input.
 * List semantics (ADR 0030): a 404 collapses to `error`, never `notFound`.
 */

const SEARCH_PATH = "/api/v1/companies/search";
const SEARCH_CONTEXT = "POST /api/v1/companies/search";
const ORGNR_PAGE_SIZE = 20;

/**
 * The wire body for a URL-driven search. Absent axis = "don't filter" → the key is OMITTED (never sent
 * as an empty list): the backend treats a missing axis as no constraint, and omitting keeps the body
 * minimal. `page`/`pageSize` are always present. Exported for a unit test that pins the org.nr
 * invariant: `"organizationNumber" in buildSearchBody(...)` is always false — the type has no such
 * field, so it cannot leak into the body.
 */
export function buildSearchBody(
  criteria: CompanySearchCriteria,
): Record<string, unknown> {
  const body: Record<string, unknown> = {
    page: criteria.page,
    pageSize: criteria.pageSize,
  };
  const name = criteria.name?.trim();
  if (name) body.name = name;
  if (criteria.sniCodes.length > 0) body.sniCodes = criteria.sniCodes;
  if (criteria.municipalityCodes.length > 0) {
    body.municipalityCodes = criteria.municipalityCodes;
  }
  return body;
}

/**
 * Search the company register by the shareable axes (name prefix + SNI + kommun) + pagination. The
 * absent-axis semantics live in {@link buildSearchBody}. Consumed by the `/foretag/sok` Server
 * Component only.
 */
export async function searchCompanies(
  criteria: CompanySearchCriteria,
): Promise<ApiResult<CompanySearchResponse>> {
  const sessionId = await getSessionId();
  if (!sessionId) return { kind: "unauthorized" };

  try {
    const res = await authedFetch(sessionId, SEARCH_PATH, {
      method: "POST",
      body: JSON.stringify(buildSearchBody(criteria)),
    });
    return await responseToResult(res, companySearchResponseSchema, SEARCH_CONTEXT);
  } catch {
    return { kind: "error" };
  }
}

/**
 * Look up a single company by an ALREADY-NORMALISED, non-personnummer-shaped org.nr (exact PK
 * equality → 0 or 1 row). BFF-ONLY: reached solely from the `/api/foretag/sok` route handler, which
 * normalises + refuses personnummer-shaped input BEFORE calling this. The org.nr travels in the body
 * (never a URL); the backend validator is the last barrier. Returns the same
 * {@link CompanySearchResponse} shape so the island renders the 0/1 row uniformly.
 */
export async function searchCompanyByOrgNr(
  orgNr: string,
): Promise<ApiResult<CompanySearchResponse>> {
  const sessionId = await getSessionId();
  if (!sessionId) return { kind: "unauthorized" };

  try {
    const res = await authedFetch(sessionId, SEARCH_PATH, {
      method: "POST",
      body: JSON.stringify({
        organizationNumber: orgNr,
        page: 1,
        pageSize: ORGNR_PAGE_SIZE,
      }),
    });
    return await responseToResult(res, companySearchResponseSchema, SEARCH_CONTEXT);
  } catch {
    return { kind: "error" };
  }
}
