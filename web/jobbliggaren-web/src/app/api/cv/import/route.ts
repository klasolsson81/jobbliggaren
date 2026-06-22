import { type NextRequest, NextResponse } from "next/server";
import { getTranslations } from "next-intl/server";
import { env } from "@/lib/env";
import { getSessionId } from "@/lib/auth/session";
import { parseResponse, parseRetryAfter } from "@/lib/dto/_helpers";
import { importResumeResponseSchema } from "@/lib/dto/parsed-resume";

/**
 * BFF för CV-import (Fas 4 STEG B, F1). Binär-passthrough: klienten POST:ar en
 * `multipart/form-data` med en "file"-del (PDF/DOCX ≤ 10 MB) hit; vi vidarebefordrar
 * kroppsströmmen VERBATIM till backend (`POST /api/v1/resumes/import`) med Bearer-
 * auth och original-`Content-Type` (multipart-boundaryn bevaras). Bytesen passerar
 * aldrig genom någon klientbunt och vi buffrar dem inte — strömmen kopplas direkt
 * till uppströms-fetchen (`duplex: "half"`).
 *
 * Storleksgrinden här är defense-in-depth (snabb-avvisning på `Content-Length`);
 * den AUKTORITATIVA capen är backendens Kestrel-body-cap (11 MiB → 413) +
 * FluentValidation-golvet (10 MiB → vänligt 400) + magic-byte-formatgrinden. Vi
 * EKAR ALDRIG backend-svarets body vid fel (GDPR Art. 5(1)(f) — ProblemDetails kan
 * bära stacktrace/PII); fel mappas till statusbaserad, säker svensk copy. Vid
 * framgång (201) returneras enbart `parsedResumeId` — inget CV-PII.
 */

export const runtime = "nodejs";
export const dynamic = "force-dynamic";

/** Paritet med backendens `ImportResumeCommandValidator.MaxFileBytes` (10 MiB) plus
 * 1 MiB headroom — samma "+1 MiB"-relation som endpointens Kestrel-cap. */
const MAX_UPLOAD_BYTES = 10 * 1024 * 1024;
const MAX_BODY_BYTES = MAX_UPLOAD_BYTES + 1024 * 1024;

// undici kräver `duplex: "half"` när request-kroppen är en ström. RequestInit
// typar inte fältet ännu → en explicit intersektion (ingen `any`).
type StreamingRequestInit = RequestInit & { duplex: "half" };

export async function POST(request: NextRequest): Promise<NextResponse> {
  const t = await getTranslations("pages.cv.importApi");
  // SSOT-session via den delade getSessionId() (samma cookie-namn-källa som
  // resten av appen — security-auditor Minor: ingen lokal cookie-namn-drift).
  const sessionId = await getSessionId();
  if (!sessionId) {
    return NextResponse.json({ error: "unauthorized" }, { status: 401 });
  }

  const contentType = request.headers.get("content-type") ?? "";
  if (!contentType.toLowerCase().startsWith("multipart/form-data")) {
    return NextResponse.json(
      { error: t("notMultipart") },
      { status: 400 }
    );
  }

  // Snabb-avvisning av uppenbart för stora kroppar innan vi proxar (kan saknas/
  // förfalskas vid chunkad överföring → backend är den riktiga grinden).
  const declaredLength = Number(request.headers.get("content-length") ?? "");
  if (Number.isFinite(declaredLength) && declaredLength > MAX_BODY_BYTES) {
    return NextResponse.json(
      { error: t("tooLarge") },
      { status: 413 }
    );
  }

  if (request.body === null) {
    return NextResponse.json(
      { error: t("noFile") },
      { status: 400 }
    );
  }

  const init: StreamingRequestInit = {
    method: "POST",
    headers: {
      Authorization: `Bearer ${sessionId}`,
      // Original-Content-Type bär multipart-boundaryn — måste skickas vidareverbatim.
      "Content-Type": contentType,
    },
    body: request.body,
    duplex: "half",
    cache: "no-store",
  };

  let backendRes: Response;
  try {
    backendRes = await fetch(`${env.BACKEND_URL}/api/v1/resumes/import`, init);
  } catch {
    return NextResponse.json(
      { error: t("serverUnreachable") },
      { status: 502 }
    );
  }

  if (backendRes.status === 401) {
    return NextResponse.json({ error: "unauthorized" }, { status: 401 });
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
  if (backendRes.status === 413) {
    return NextResponse.json(
      { error: t("tooLarge") },
      { status: 413 }
    );
  }
  if (!backendRes.ok) {
    // 400/415/… — backend-body EKAS ALDRIG (PII-disciplin). Statusbaserad copy.
    return NextResponse.json(
      { error: t("unreadable") },
      { status: 400 }
    );
  }

  // 201 Created — validera formen och returnera ENBART parsedResumeId.
  try {
    const data = await parseResponse(
      backendRes,
      importResumeResponseSchema,
      "POST /api/v1/resumes/import (proxy)"
    );
    return NextResponse.json(
      { parsedResumeId: data.parsedResumeId },
      { status: 201 }
    );
  } catch {
    return NextResponse.json({ error: "error" }, { status: 502 });
  }
}
