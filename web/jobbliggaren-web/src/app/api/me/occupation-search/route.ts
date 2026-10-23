import { NextResponse, type NextRequest } from "next/server";
import { searchOccupations } from "@/lib/api/occupation-search";

/**
 * #2198 — the occupation picker's search read for its debounced client fetch, which cannot call the
 * `server-only` fetcher. GET only, and it writes nothing (ADR 0018). It forwards exactly one
 * parameter, `q`, the key the edge filter deletes from its log (security-auditor Major 4), and logs
 * nothing itself. Every branch says `private, no-store`: `NextResponse` does not copy the API's
 * header, and the answer is per word and per session.
 */
const HEADERS = { "Cache-Control": "private, no-store" } as const;

export const dynamic = "force-dynamic";

export async function GET(request: NextRequest) {
  const q = request.nextUrl.searchParams.get("q") ?? "";
  const result = await searchOccupations(q, request.signal);

  switch (result.kind) {
    case "ok":
      return NextResponse.json(result.data, { headers: HEADERS });
    case "unauthorized":
      return new NextResponse(null, { status: 401, headers: HEADERS });
    case "rateLimited":
      return new NextResponse(null, {
        status: 429,
        headers: { ...HEADERS, "Retry-After": String(result.retryAfterSeconds) },
      });
    default:
      return new NextResponse(null, { status: 502, headers: HEADERS });
  }
}
