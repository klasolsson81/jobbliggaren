import { NextResponse, type NextRequest } from "next/server";
import { searchCompanyByOrgNr } from "@/lib/api/company-search";
import {
  isPersonnummerShapedOrgNr,
  normalizeOrgNrInput,
} from "@/lib/dto/company-registry";

/**
 * #560 PR-B — the org.nr search BFF proxy for `/foretag/sok`. The org.nr search term must never enter
 * a browser URL (ADR 0087 D8(c): a sole-prop org.nr can equal a personnummer, and query strings reach
 * access logs + history), so — unlike the shareable name/SNI/kommun axes, which the RSC page sends as
 * a POST-as-read body server-side — the org.nr field is a client island that POSTs the value HERE, in
 * a JSON body, and renders the 0/1 result client-side. It mirrors `/api/foretag/lookup`.
 *
 * Guards (pre-backend, defense-in-depth — the island already gates both):
 * - malformed org.nr → 400 without touching the backend;
 * - personnummer-shaped org.nr → 400 `reason: "protected"` WITHOUT forwarding (a potential
 *   personnummer travels no further than necessary; the backend validator remains the enforcing
 *   authority and refuses too). The response never echoes the typed value.
 *
 * On success the register search returns 0 or 1 row (exact PK equality); the route hands the island
 * the single `CompanyBrowseDto` or `null`. 429 forwards Retry-After so the island renders a concrete
 * retry time.
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
    case "ok":
      // 0 or 1 row (exact PK equality). Hand the island the single company or null.
      return NextResponse.json(result.data.companies.items[0] ?? null);
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
