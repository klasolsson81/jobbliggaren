import { type NextRequest, NextResponse } from "next/server";
import { env } from "@/lib/env";
import { getSessionId } from "@/lib/auth/session";
import { parseRetryAfter } from "@/lib/dto/_helpers";
import { isValidId } from "@/lib/validation/guid";
import { renderProfileSchema } from "@/lib/dto/parsed-resume";

/**
 * BFF för CV-förhandsgranskning (Fas 4 STEG B-2, "Förhandsgranska CV"). Binär
 * GET-spegling av import-routens egress-doktrin: klienten GET:ar hit
 * (same-origin, cookie auto-skickas) och vi vidarebefordrar till backend
 * (`GET /api/v1/resumes/parsed/{id}/render?profile=Ats|Visual`) med Bearer-auth.
 * Backend returnerar en RÅ PDF (Results.File, inte JSON) — vi strömmar igenom
 * `backendRes.body` direkt (ingen server-side-buffring, ingen klientbunt).
 *
 * PDF-bytesen är ägar-scopade i backend (RequireAuthorization, owner-scoped,
 * IDOR → 404) och persisteras aldrig. Renderingen är keyad på `PendingReview`-
 * staging-artefakten (`parsedId`), som lever på `/cv/granska/[parsedId]`-familjen
 * — det finns ingen render-by-Resume-id-väg (medvetet uppskjuten backend-STEG).
 *
 * Vi EKAR ALDRIG backend-svarets body vid fel (GDPR Art. 5(1)(f) —
 * ProblemDetails kan bära stacktrace/PII); fel mappas till statusbaserad, säker
 * svensk copy. Vid framgång konstrueras FÄRSKA headers (Content-Type/-Disposition/
 * Cache-Control) — inga godtyckliga backend-headers vidarebefordras.
 *
 * `isValidId(parsedId)`-allowlisten är path-injektions-/SSRF-barriären (samma
 * fail-safe-default som `getResumeById` — backend nås aldrig vid ogiltigt id).
 * `profile` normaliseras till `Ats` vid saknat/ogiltigt värde (paritet med
 * granska-sidans `safeParse`-default; forbattra-sidan retirerades med
 * åtgärda-lagrets deferral, ADR 0112); backend är den auktoritativa
 * validatorn oavsett.
 */

export const runtime = "nodejs";
export const dynamic = "force-dynamic";

export async function GET(
  request: NextRequest,
  ctx: { params: Promise<{ parsedId: string }> }
): Promise<NextResponse> {
  // SSOT-session via den delade getSessionId() (samma cookie-namn-källa som
  // resten av appen). Saknad session → backend nås aldrig.
  const sessionId = await getSessionId();
  if (!sessionId) {
    return NextResponse.json({ error: "unauthorized" }, { status: 401 });
  }

  const { parsedId } = await ctx.params;
  // Allowlist (acceptera endast känd-god GUID-form) — path-injektions-/SSRF-
  // barriär. Ogiltigt id → 404 utan att nå backend (spegel av getResumeById).
  if (!isValidId(parsedId)) {
    return new NextResponse(null, { status: 404 });
  }

  // Normalisering, inte tyst korruption: saknad/ogiltig profil faller till "Ats"
  // (samma default som granska-sidan). Backend är auktoritativ.
  const profileResult = renderProfileSchema.safeParse(
    request.nextUrl.searchParams.get("profile")
  );
  const profile = profileResult.success ? profileResult.data : "Ats";

  let backendRes: Response;
  try {
    backendRes = await fetch(
      `${env.BACKEND_URL}/api/v1/resumes/parsed/${encodeURIComponent(
        parsedId
      )}/render?profile=${profile}`,
      {
        headers: { Authorization: `Bearer ${sessionId}` },
        cache: "no-store",
      }
    );
  } catch {
    return NextResponse.json({ error: "error" }, { status: 502 });
  }

  if (backendRes.status === 401) {
    return NextResponse.json({ error: "unauthorized" }, { status: 401 });
  }
  if (backendRes.status === 404) {
    return new NextResponse(null, { status: 404 });
  }
  if (backendRes.status === 429) {
    const retryAfterSeconds = parseRetryAfter(
      backendRes.headers.get("Retry-After")
    );
    return NextResponse.json(
      { error: "rateLimited", retryAfterSeconds },
      {
        status: 429,
        headers: { "Retry-After": String(retryAfterSeconds) },
      }
    );
  }
  if (!backendRes.ok) {
    // Övriga !ok — backend-body EKAS ALDRIG (PII-disciplin). Generiskt 502.
    return NextResponse.json({ error: "error" }, { status: 502 });
  }

  // 200 OK — strömma PDF:en igenom. FÄRSKA headers (inga vidarebefordrade
  // backend-headers); body passeras VERBATIM utan server-side-buffring.
  return new NextResponse(backendRes.body, {
    status: 200,
    headers: {
      "Content-Type": "application/pdf",
      "Content-Disposition": `inline; filename="cv-${profile.toLowerCase()}.pdf"`,
      "Cache-Control": "no-store",
    },
  });
}
