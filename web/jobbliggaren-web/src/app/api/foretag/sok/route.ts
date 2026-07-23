import { NextResponse, type NextRequest } from "next/server";
import { searchCompanyByOrgNr } from "@/lib/api/company-search";
import { getCompanyWatchStatusByOrgNr } from "@/lib/api/company-follows";
import {
  isPersonnummerShapedOrgNr,
  normalizeOrgNrInput,
} from "@/lib/dto/company-registry";

/**
 * #560 PR-B / #997 (S2) — the org.nr search BFF proxy for `/foretag/sok`. The org.nr search term must
 * never enter a browser URL (ADR 0087 D8(c): a sole-prop org.nr can equal a personnummer, and query
 * strings reach access logs + history), so — unlike the shareable name/SNI/kommun axes, which the RSC
 * page sends as a POST-as-read body server-side — the org.nr field is a client island that POSTs the
 * value HERE, in a JSON body, and renders the 0/1 result client-side.
 *
 * Guards (pre-backend, defense-in-depth — the island already gates both):
 * - malformed org.nr → 400 without touching the backend;
 * - personnummer-shaped org.nr → 400 `reason: "protected"` WITHOUT forwarding (a potential
 *   personnummer travels no further than necessary; the backend validator remains the enforcing
 *   authority and refuses too). The response never echoes the typed value.
 *
 * On success the register search returns 0 or 1 row (exact PK equality). For an unmasked legal-entity
 * hit the route ALSO composes the user's own follow-state (`companyWatchId`) from the same org.nr-keyed
 * overlay the streamed rows use (`getCompanyWatchStatusByOrgNr`) — two separate backend reads merged
 * here, never a server-side join against the firewalled register (DPIA C-D4/M-C5) — so the unified
 * field's org.nr result carries a correct Bevaka/Bevakar affordance (#997 caveat: preserve
 * follow-via-org.nr). Returns `{ company, companyWatchId }` or `null`. 429 forwards Retry-After.
 */
export async function POST(request: NextRequest) {
  const body: unknown = await request.json().catch(() => null);
  const raw =
    typeof body === "object" &&
    body !== null &&
    "organizationNumber" in body &&
    typeof (body as { organizationNumber: unknown }).organizationNumber === "string"
      ? (body as { organizationNumber: string }).organizationNumber
      : "";

  const orgNr = normalizeOrgNrInput(raw);
  if (!orgNr) {
    return NextResponse.json(
      { error: "Organisationsnumret måste vara 10 siffror." },
      { status: 400 },
    );
  }
  if (isPersonnummerShapedOrgNr(orgNr)) {
    return NextResponse.json({ reason: "protected" }, { status: 400 });
  }

  const result = await searchCompanyByOrgNr(orgNr);

  switch (result.kind) {
    case "ok": {
      // 0 or 1 row (exact PK equality). Hand the island the single company + its follow-state, or null.
      const company = result.data.companies.items[0] ?? null;
      if (company === null) return NextResponse.json(null);

      // Follow-state overlay for the Bevaka affordance — only an unmasked legal entity carries an
      // org.nr key (a masked/sole-prop hit is not followable, ADR 0087 D8(c)). Degrades to null on any
      // failure (getCompanyWatchStatusByOrgNr is fail-open), so a follow-graph hiccup never blocks the
      // lookup — the button then simply starts at "Bevaka".
      let companyWatchId: string | null = null;
      if (company.organizationNumber && !company.isProtectedIdentity) {
        const [status] = await getCompanyWatchStatusByOrgNr([company.organizationNumber]);
        companyWatchId = status?.companyWatchId ?? null;
      }
      return NextResponse.json({ company, companyWatchId });
    }
    case "unauthorized":
      return NextResponse.json({ error: "unauthorized" }, { status: 401 });
    case "rateLimited":
      return NextResponse.json(
        { error: "rateLimited" },
        {
          status: 429,
          headers: { "Retry-After": String(result.retryAfterSeconds) },
        },
      );
    // forbidden/notFound/error — a technical error the island renders civically.
    default:
      return NextResponse.json({ error: "error" }, { status: 502 });
  }
}
