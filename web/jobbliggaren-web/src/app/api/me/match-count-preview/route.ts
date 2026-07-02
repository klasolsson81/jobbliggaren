import { NextResponse, type NextRequest } from "next/server";
import { z } from "zod";
import { getDraftMatchCount } from "@/lib/api/match-count";

/**
 * Epik #526 (ADR 0088) — live sök-preview-count-proxy. Den klientburna
 * setup-modalen kan inte anropa den `server-only` `getDraftMatchCount`-fetchern
 * direkt (session-cookie + BACKEND_URL är serverkontext). Speglar
 * `/api/jobb/facet-counts/route.ts`-mönstret: coerce:a body:n billigt här,
 * delegera till server-fetchern, mappa `ApiResult` → HTTP-status.
 *
 * Degraderings-kontrakt (samma som facet-counts): fel-utfall → NON-2xx så
 * hookens `!res.ok`-gren nollar talet (visar neutral platshållare, aldrig 0).
 * Ett 200 + `{ count: 0 }` vid backend-fel vore desinformation.
 *
 * Bara sökbara concept-id-listor passerar (aldrig fritext/kompetenser). Den
 * auktoritativa valideringen (concept-id-format + cap) sker i backend-validatorn;
 * här coerce:as bara formen till string-listor.
 */
// Kant-tak (defense-in-depth): backend är auktoritativ validator (concept-id-
// regex + MaxConceptIds), men ett list-/element-tak här fail-fast:ar en
// uppsvälld payload från en autentiserad missbrukare innan den forwardas.
const conceptList = z.array(z.string().max(64)).max(400).default([]);
const bodySchema = z.object({
  occupationGroups: conceptList,
  regions: conceptList,
  municipalities: conceptList,
  employmentTypes: conceptList,
});

export async function POST(request: NextRequest) {
  let raw: unknown;
  try {
    raw = await request.json();
  } catch {
    return NextResponse.json({ error: "Ogiltig body." }, { status: 400 });
  }

  const parsed = bodySchema.safeParse(raw);
  if (!parsed.success) {
    return NextResponse.json({ error: "Ogiltig body." }, { status: 400 });
  }

  const result = await getDraftMatchCount(parsed.data);

  switch (result.kind) {
    case "ok":
      return NextResponse.json(result.data);
    case "unauthorized":
      return NextResponse.json({}, { status: 401 });
    case "rateLimited":
      return NextResponse.json(
        {},
        {
          status: 429,
          headers: { "Retry-After": String(result.retryAfterSeconds) },
        }
      );
    // forbidden/notFound/error → 502: hooken nollar talet (neutral platshållare).
    // ALDRIG 200 + { count: 0 } vid backend-fel (aktiv desinformation).
    default:
      return NextResponse.json({}, { status: 502 });
  }
}
