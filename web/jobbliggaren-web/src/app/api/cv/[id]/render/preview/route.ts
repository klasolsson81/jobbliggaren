import { type NextRequest, NextResponse } from "next/server";
import { env } from "@/lib/env";
import { getSessionId } from "@/lib/auth/session";
import { parseRetryAfter } from "@/lib/dto/_helpers";
import { isValidId } from "@/lib/validation/guid";

/**
 * BFF för den EFEMÄRA live-förhandsvisningen i mallbyggaren (Fas 4b PR-8b 8b.3,
 * CTO-bind Q1 Variant B). Binär GET-spegling av `api/cv/[id]/preview` — enda
 * skillnaden är att vi proxar mot backendens dedikerade efemär-render-väg
 * (`GET /api/v1/resumes/{id}/render/preview?template=&accent=&font=&density=`) och
 * vidarebefordrar de fyra OSPARADE malloptionerna i stället för `?profile=`. De fyra
 * optionerna komponeras över det persisterade fotot och INGET persisteras
 * (preview == export via den delade QuestPDF-renderaren; alltid Visual-profilen).
 *
 * Klienten GET:ar hit (same-origin, cookie auto-skickas) och vi vidarebefordrar till
 * backend med Bearer-auth. Backend returnerar en RÅ PDF (Results.File, inte JSON) —
 * vi strömmar igenom `backendRes.body` direkt (ingen server-side-buffring, ingen
 * klientbunt).
 *
 * PDF-bytesen är ägar-scopade i backend (RequireAuthorization, owner-scoped,
 * IDOR → 404) och persisteras aldrig (Invariant 3). Samma tunga DEK + QuestPDF-kost
 * som `/render` → samma `ResumeRenderPolicy`-bucket (429 möjligt).
 *
 * Vi EKAR ALDRIG backend-svarets body vid fel (GDPR Art. 5(1)(f) — ProblemDetails
 * kan bära stacktrace/PII); fel mappas till statusbaserad, säker svensk copy. Vid
 * framgång konstrueras FÄRSKA headers (Content-Type/-Disposition/Cache-Control) —
 * inga godtyckliga backend-headers vidarebefordras.
 *
 * `isValidId(id)`-allowlisten är path-injektions-/SSRF-barriären (samma
 * fail-safe-default som `[id]/preview`-routen). De fyra optionerna vidarebefordras
 * verbatim (tomsträng vid saknat värde) — backendens validator är den auktoritativa
 * (okänt namn → 400 fail-loud); klienten skickar alltid värden ur katalogen.
 */

export const runtime = "nodejs";
export const dynamic = "force-dynamic";

export async function GET(
  request: NextRequest,
  ctx: { params: Promise<{ id: string }> }
): Promise<NextResponse> {
  // SSOT-session via den delade getSessionId() (samma cookie-namn-källa som resten
  // av appen). Saknad session → backend nås aldrig.
  const sessionId = await getSessionId();
  if (!sessionId) {
    return NextResponse.json({ error: "unauthorized" }, { status: 401 });
  }

  const { id } = await ctx.params;
  // Allowlist (acceptera endast känd-god GUID-form) — path-injektions-/SSRF-barriär.
  // Ogiltigt id → 404 utan att nå backend (spegel av [id]/preview).
  if (!isValidId(id)) {
    return new NextResponse(null, { status: 404 });
  }

  // De fyra optionerna vidarebefordras verbatim (tomsträng vid saknat värde). Backend
  // är den auktoritativa validatorn (SmartEnum.TryFromName fail-loud → 400).
  const sp = request.nextUrl.searchParams;
  const upstreamParams = new URLSearchParams({
    template: sp.get("template") ?? "",
    accent: sp.get("accent") ?? "",
    font: sp.get("font") ?? "",
    density: sp.get("density") ?? "",
  });

  let backendRes: Response;
  try {
    backendRes = await fetch(
      `${env.BACKEND_URL}/api/v1/resumes/${encodeURIComponent(
        id
      )}/render/preview?${upstreamParams}`,
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
    // Övriga !ok (inkl. 400 vid ogiltigt optionsnamn) — backend-body EKAS ALDRIG
    // (PII-disciplin). Generiskt 502.
    return NextResponse.json({ error: "error" }, { status: 502 });
  }

  // 200 OK — strömma PDF:en igenom. FÄRSKA headers (inga vidarebefordrade
  // backend-headers); body passeras VERBATIM utan server-side-buffring.
  return new NextResponse(backendRes.body, {
    status: 200,
    headers: {
      "Content-Type": "application/pdf",
      "Content-Disposition": 'inline; filename="cv-forhandsvisning.pdf"',
      "Cache-Control": "no-store",
    },
  });
}
