import "server-only";
import { getSessionId } from "@/lib/auth/session";
import { authedFetch } from "@/lib/http/authed-fetch";
import {
  companyLookupSchema,
  type CompanyLookup,
} from "@/lib/dto/company-registry";
import { responseToResult, type ApiResult } from "@/lib/dto/_helpers";

/**
 * #454 (ADR 0088 D7) — look up a company by org.nr against the registry-backed lookup endpoint.
 * POST with the org.nr in the JSON BODY — never a URL path/query (ADR 0087 D8(c): a sole-prop
 * org.nr can equal a personnummer and must never reach an access log). The caller passes an
 * ALREADY-NORMALISED 10-digit value (`normalizeOrgNrInput`); the backend validator is the last
 * barrier (400). `notFound`/`unavailable` arrive as 200-with-status inside the DTO; a refused
 * personnummer-shaped input arrives as a 400 → `{ kind: "error" }` here and the BFF route maps the
 * civic copy. Rate-limited by the dedicated company-lookup policy (429 → retryAfterSeconds).
 */
export async function lookupCompany(
  orgNr: string
): Promise<ApiResult<CompanyLookup>> {
  const sessionId = await getSessionId();
  if (!sessionId) return { kind: "unauthorized" };

  try {
    const res = await authedFetch(sessionId, "/api/v1/companies/lookup", {
      method: "POST",
      body: JSON.stringify({ organizationNumber: orgNr }),
    });
    return await responseToResult(
      res,
      companyLookupSchema,
      "POST /api/v1/companies/lookup"
    );
  } catch {
    return { kind: "error" };
  }
}
