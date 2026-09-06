import { type NextRequest, NextResponse } from "next/server";
import { env } from "@/lib/env";
import { getSessionId } from "@/lib/auth/session";
import { parseRetryAfter } from "@/lib/dto/_helpers";
import { isValidId } from "@/lib/validation/guid";
import { pickForwardedHeaders } from "@/lib/http/forwarded-headers";

/**
 * The shared BFF half of "let the user see their OWN uploaded file". Two routes proxy the two
 * backend keys (`/api/v1/resumes/{id}/original` and `/api/v1/resumes/parsed/{parsedId}/original`)
 * and differ ONLY in that path, so the proxy lives here once instead of a third and fourth copy of
 * `api/cv/[id]/preview/route.ts`.
 *
 * Same posture as the delivered preview route, and for the same reasons: `isValidId` is the
 * path-injection/SSRF barrier (an invalid id never reaches the backend); the backend body is NEVER
 * echoed on an error path (ProblemDetails can carry stacktrace/PII, GDPR Art. 5(1)(f)); a 200 is
 * streamed through `backendRes.body` with no server-side buffering; and the response headers are
 * built FRESH here rather than forwarded.
 *
 * Two things are deliberately NOT inherited from the preview route:
 *
 * 1. **The content type is re-derived from an allowlist, not echoed.** The backend already sends a
 *    server-derived canonical MIME (never the client-declared upload type, M-F2), and this narrows
 *    it a second time to the two kinds a CV can be. Anything else becomes a 502 rather than a
 *    content type the browser might sniff into something active. The disposition stays
 *    `attachment` for both kinds — see below.
 * 2. **The filename is synthetic.** The stored name is user-controlled text (personnummer-redacted
 *    at rest, but still user-controlled), and putting it into a `Content-Disposition` header is the
 *    one place it could inject. The surfaces that need to NAME the file already hold a safe name of
 *    their own, so the header carries `original.pdf` / `original.docx` and nothing user-supplied.
 */

/** The only two kinds `CvFileSignature` can resolve, mapped to the extension each downloads as. */
const ALLOWED_CONTENT_TYPES = new Map<string, string>([
  ["application/pdf", "pdf"],
  [
    "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
    "docx",
  ],
]);

export async function proxyOriginalFile(
  request: NextRequest,
  id: string,
  backendPath: (id: string) => string
): Promise<NextResponse> {
  const sessionId = await getSessionId();
  if (!sessionId) {
    return NextResponse.json({ error: "unauthorized" }, { status: 401 });
  }

  if (!isValidId(id)) {
    return new NextResponse(null, { status: 404 });
  }

  let backendRes: Response;
  try {
    backendRes = await fetch(`${env.BACKEND_URL}${backendPath(encodeURIComponent(id))}`, {
      headers: {
        ...pickForwardedHeaders(request.headers),
        Authorization: `Bearer ${sessionId}`,
      },
      cache: "no-store",
    });
  } catch {
    return NextResponse.json({ error: "error" }, { status: 502 });
  }

  if (backendRes.status === 401) {
    return NextResponse.json({ error: "unauthorized" }, { status: 401 });
  }
  // 404 is the ORDINARY answer here, not only an error: a template-built CV, a personnummer-flagged
  // import the user declined to store, and an import predating the file store all have no original.
  // The client renders an honest empty state on it.
  if (backendRes.status === 404) {
    return new NextResponse(null, { status: 404 });
  }
  if (backendRes.status === 429) {
    const retryAfterSeconds = parseRetryAfter(backendRes.headers.get("Retry-After"));
    return NextResponse.json(
      { error: "rateLimited", retryAfterSeconds },
      { status: 429, headers: { "Retry-After": String(retryAfterSeconds) } }
    );
  }
  if (!backendRes.ok) {
    return NextResponse.json({ error: "error" }, { status: 502 });
  }

  // Narrow the backend's canonical MIME to the allowlist. A stored original outside it cannot be
  // rendered or safely offered, so it fails closed rather than reaching the browser untyped.
  const backendContentType = backendRes.headers.get("Content-Type")?.split(";")[0]?.trim() ?? "";
  const extension = ALLOWED_CONTENT_TYPES.get(backendContentType);
  if (extension === undefined) {
    return NextResponse.json({ error: "error" }, { status: 502 });
  }

  // `attachment` for BOTH kinds, never `inline`. DPIA #659 M-F2 prescribes RFC 6266 attachment
  // verbatim and is marked merge-blocking, and R-F6's residual rests on the parenthesis "a stored
  // HTML-in-PDF polyglot is never rendered inline from our origin" — the backend has always sent
  // `attachment` for exactly that reason, and this proxy must not weaken it on the way out.
  return new NextResponse(backendRes.body, {
    status: 200,
    headers: {
      "Content-Type": backendContentType,
      "Content-Disposition": `attachment; filename="original.${extension}"`,
      "Cache-Control": "no-store",
      "X-Content-Type-Options": "nosniff",
    },
  });
}
