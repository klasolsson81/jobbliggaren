import { type NextRequest, NextResponse } from "next/server";
import { env } from "@/lib/env";
import { getSessionId } from "@/lib/auth/session";
import { parseResponse, parseRetryAfter } from "@/lib/dto/_helpers";
import { isValidId } from "@/lib/validation/guid";
import { atsTextResponseSchema } from "@/lib/dto/resumes";

/**
 * BFF för den kanoniska ATS-textvyn av en BEFORDRAD Resume (Fas 4b PR-8.2/8.3).
 * JSON-spegling av den binära `api/cv/[id]/preview`-routen: samma auth-,
 * IDOR- och fel-disciplin, men backend returnerar `{ source, text }` (inte en
 * PDF) — vi validerar formen vid ACL-gränsen och returnerar den som JSON.
 *
 * Klienten (preview-modalens ATS-text-flik) GET:ar hit same-origin; vi proxar
 * mot `GET /api/v1/resumes/{id}/ats-text` med Bearer-auth. Texten är den
 * linjäriserade, redan pnr-redigerade CV-texten (motorns choke point) — backend
 * garanterar renheten; BFF:n läser bara vad som redan är säkert.
 *
 * Vi EKAR ALDRIG backend-svarets råa body vid fel (GDPR Art. 5(1)(f) —
 * ProblemDetails kan bära stacktrace/PII); fel mappas till statusbaserad, säker
 * copy och `parseResponse` redigerar bort `received` ur loggade shape-avvikelser.
 * `isValidId(id)`-allowlisten är path-injektions-/SSRF-barriären (backend nås
 * aldrig vid ogiltigt id — spegel av preview-routen). `no-store` på 200 (paritet
 * med backendens egen `private, no-store` — CV-textytan cachas aldrig).
 */

export const runtime = "nodejs";
export const dynamic = "force-dynamic";

export async function GET(
  _request: NextRequest,
  ctx: { params: Promise<{ id: string }> }
): Promise<NextResponse> {
  const sessionId = await getSessionId();
  if (!sessionId) {
    return NextResponse.json({ error: "unauthorized" }, { status: 401 });
  }

  const { id } = await ctx.params;
  if (!isValidId(id)) {
    return new NextResponse(null, { status: 404 });
  }

  let backendRes: Response;
  try {
    backendRes = await fetch(
      `${env.BACKEND_URL}/api/v1/resumes/${encodeURIComponent(id)}/ats-text`,
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
      { status: 429, headers: { "Retry-After": String(retryAfterSeconds) } }
    );
  }
  if (!backendRes.ok) {
    return NextResponse.json({ error: "error" }, { status: 502 });
  }

  // 200 OK — validera `{ source, text }` vid ACL-gränsen (aldrig eka rå body;
  // parseResponse kastar DtoParseError vid shape-avvikelse och redigerar bort
  // received). Färska headers: no-store, ingen vidarebefordrad backend-header.
  try {
    const data = await parseResponse(
      backendRes,
      atsTextResponseSchema,
      "GET /api/v1/resumes/{id}/ats-text"
    );
    return NextResponse.json(data, {
      status: 200,
      headers: { "Cache-Control": "no-store" },
    });
  } catch {
    return NextResponse.json({ error: "error" }, { status: 502 });
  }
}
