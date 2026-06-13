import { NextResponse, type NextRequest } from "next/server";
import { suggestJobAdTerms } from "@/lib/api/job-ads";
import { SUGGEST_MIN_PREFIX } from "@/lib/dto/job-ads";

/**
 * ADR 0042 Beslut C — typeahead-proxy. Client-komponenten (JobAdTypeahead)
 * kan inte anropa den `server-only` `suggestJobAdTerms`-fetchern direkt
 * (session-cookie + BACKEND_URL är serverkontext). Denna route-handler
 * speglar `/api/me/route.ts`-mönstret: validerar prefix, delegerar till
 * server-fetchern, mappar `ApiResult` → HTTP-status.
 *
 * DoS-yta (ADR 0042 Beslut C): prefix < SUGGEST_MIN_PREFIX avvisas här
 * (400) utan att träffa backend. Backend-validator + SuggestPolicy
 * rate-limit är sista barriär. Client debouncar ≥300ms och skickar ej
 * request < min prefix.
 */
export async function GET(request: NextRequest) {
  const prefix = request.nextUrl.searchParams.get("prefix")?.trim() ?? "";

  if (prefix.length < SUGGEST_MIN_PREFIX) {
    return NextResponse.json(
      { error: `Prefix måste vara minst ${SUGGEST_MIN_PREFIX} tecken.` },
      { status: 400 }
    );
  }

  const result = await suggestJobAdTerms(prefix);

  switch (result.kind) {
    case "ok":
      return NextResponse.json(result.data);
    case "unauthorized":
      return NextResponse.json([], { status: 401 });
    case "rateLimited":
      return NextResponse.json([], {
        status: 429,
        headers: { "Retry-After": String(result.retryAfterSeconds) },
      });
    // forbidden/notFound/error: suggest är auth-gated, inga id:n — alla
    // faller till tom lista (typeahead degraderar tyst, civic-utility:
    // sökfältet förblir användbart utan förslag).
    default:
      return NextResponse.json([], { status: 200 });
  }
}
