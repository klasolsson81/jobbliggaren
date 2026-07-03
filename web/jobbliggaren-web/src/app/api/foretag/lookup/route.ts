import { NextResponse, type NextRequest } from "next/server";
import { lookupCompany } from "@/lib/api/company-registry";
import {
  isPersonnummerShapedOrgNr,
  normalizeOrgNrInput,
} from "@/lib/dto/company-registry";

/**
 * #454 (ADR 0088 D7) — company-lookup BFF proxy. The client island cannot call the `server-only`
 * `lookupCompany` fetcher (session cookie + BACKEND_URL are server context); this route mirrors the
 * `/api/jobb/suggest` pattern: validate pre-backend, delegate, map `ApiResult` → HTTP.
 *
 * Guards (pre-backend, defense-in-depth — the island already gates both):
 * - malformed org.nr → 400 without touching the backend (DoS surface parity suggest);
 * - personnummer-shaped org.nr → 400 `reason: "protected"` WITHOUT forwarding (ADR 0088 D4 — a
 *   potential personnummer travels no further than necessary; the backend handler is the enforcing
 *   authority and refuses too). The response never echoes the typed value.
 *
 * POST with the org.nr in the JSON body — never a URL (ADR 0087 D8(c): query strings reach access
 * logs). 429 forwards Retry-After so the island renders a concrete retry time.
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
      { status: 400 }
    );
  }
  if (isPersonnummerShapedOrgNr(orgNr)) {
    return NextResponse.json({ reason: "protected" }, { status: 400 });
  }

  const result = await lookupCompany(orgNr);

  switch (result.kind) {
    case "ok":
      return NextResponse.json(result.data);
    case "unauthorized":
      return NextResponse.json({ error: "unauthorized" }, { status: 401 });
    case "rateLimited":
      return NextResponse.json(
        { error: "rateLimited" },
        {
          status: 429,
          headers: { "Retry-After": String(result.retryAfterSeconds) },
        }
      );
    // forbidden/notFound/error — the lookup endpoint itself never 404s (notFound is
    // 200-with-status); anything else is a technical error the island renders civically.
    default:
      return NextResponse.json({ error: "error" }, { status: 502 });
  }
}
